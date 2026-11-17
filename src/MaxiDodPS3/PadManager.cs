using HidSharp;
using Nefarius.ViGEm.Client;

namespace MaxiDodPS3;

/// <summary>
/// Hot-plug manager for DS3, DS4 and DualSense pads (USB or Bluetooth, genuine or clone).
/// Each pad gets a slot and one virtual controller of the kind chosen for it, game rumble is forwarded
/// back to the real pad, and the real pad is hidden from games so nothing shows up twice.
/// </summary>
public sealed class PadManager
{
    private readonly Config _cfg;
    private readonly ViGEmClient _client;
    private readonly HidHideManager _hide;
    private readonly object _lock = new();
    private readonly HashSet<string> _active = new();
    private readonly HashSet<string> _activeSerials = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _noted = new();
    private readonly List<Task> _tasks = new();
    private readonly bool[] _slots;
    private readonly Dictionary<int, SessionInfo> _sessions = new();

    /// <summary>Live view of each connected pad, for the tray app and the debugger.</summary>
    public SessionInfo[] Sessions { get { lock (_lock) return _sessions.Values.OrderBy(x => x.Slot).ToArray(); } }

    public PadManager(Config cfg, ViGEmClient client)
    {
        _cfg = cfg;
        _client = client;
        _hide = new HidHideManager(cfg);
        _slots = new bool[cfg.MaxPads];
    }

    public async Task RunAsync(CancellationToken ct)
    {
        if (_cfg.HideSourcePads && !_hide.Available)
        {
            Log.Info("Note: HidHide was not found, so real pads stay visible to games next to their virtual pads.");
            Log.Info("      That can show every controller twice (as seen with early DsHidMini in FIFA/FC).");
            Log.Info("      Install HidHide (https://github.com/nefarius/HidHide) or set a kind to \"native\" in maxidodps3.json.");
        }

        while (!ct.IsCancellationRequested)
        {
            try { ScanOnce(ct); }
            catch (Exception ex) { Log.Error($"[scan] {ex.Message}"); }

            try { await Task.Delay(1000, ct); }
            catch (OperationCanceledException) { break; }
        }

        Task[] pending;
        lock (_lock) pending = _tasks.ToArray();
        await Task.WhenAny(Task.WhenAll(pending), Task.Delay(3000));

        _hide.UnhideAll();
    }

    private void Note(string key, string message)
    {
        bool first;
        lock (_lock) first = _noted.Add(key);
        if (first) Log.Info(message);
    }

    private void ScanOnce(CancellationToken ct)
    {
        // USB first, so a pad that is on both USB and Bluetooth is taken over USB.
        var found = Devices.Enumerate(_cfg)
            .OrderBy(p => HidUtil.LooksBluetooth(p.Device.DevicePath) ? 1 : 0)
            .ToList();

        foreach (var (dev, info) in found)
        {
            string path = dev.DevicePath;
            string mode = _cfg.ModeFor(info.Kind);

            if (mode == "native")
            {
                Note(path, $"{info.Name} ({HidUtil.Transport(path)}): left alone (native mode), so it is not translated or hidden.");
                continue;
            }
            if (!HidUtil.PlausibleGamepad(dev)) continue;

            string? serial = _cfg.DedupeBySerial ? HidUtil.TrySerial(dev) : null;
            int slot;
            lock (_lock)
            {
                if (_active.Contains(path)) continue;
                if (serial != null && _activeSerials.Contains(serial)) continue; // same pad, other transport
                slot = Array.IndexOf(_slots, false);
                if (slot < 0) return; // all slots busy
                _slots[slot] = true;
                _active.Add(path);
                if (serial != null) _activeSerials.Add(serial);
            }

            var t = Task.Run(() => RunSession(dev, info, mode, slot, serial, ct));
            lock (_lock) _tasks.Add(t);
        }
    }

    private IVirtualPad CreateVirtual(string mode) =>
        mode == "ds4" ? new Ds4Pad(_client, _cfg) : new X360Pad(_client, _cfg);

    private static byte Scale(byte v, double factor) =>
        (byte)Math.Clamp((int)Math.Round(v * factor), 0, 255);

    private void RunSession(HidDevice dev, KnownPad info, string mode, int slot, string? serial, CancellationToken ct)
    {
        string path = dev.DevicePath;
        bool hidden = false;
        try
        {
            using var pad = HidPad.TryOpen(dev, info, _cfg);
            if (pad is null)
            {
                Note("open:" + path, $"{info.Name} was found but could not be opened (is another app using it?). Will keep retrying.");
                return;
            }

            // Open first, then hide: hiding only affects programs that open the pad after this point.
            if (_cfg.HideSourcePads && _hide.Available) hidden = _hide.Hide(dev);

            using IVirtualPad virt = CreateVirtual(mode);
            var si = new SessionInfo { Slot = slot, Name = info.Name, Path = path, Transport = HidUtil.Transport(path),
                                       Mode = mode, Pad = pad, Virtual = virt, Hidden = hidden, Started = DateTime.Now };
            lock (_lock) _sessions[slot] = si;
            virt.Rumble += (large, small) =>
            {
                si.GameRumbleLarge = large; si.GameRumbleSmall = small; si.GameRumbleCount++; si.GameRumbleAt = DateTime.Now;
                pad.SetRumble(Scale(large, _cfg.LargeMotorScale), Scale(small, _cfg.SmallMotorScale));
            };
            pad.SetLed(slot);

            string modeName = mode == "ds4" ? "DS4" : "X360";
            Log.Info($"[pad {slot + 1}] {info.Name} connected ({HidUtil.Transport(path)}) -> virtual {modeName}"
                              + (hidden ? ", real pad hidden from games" : ""));
            lock (_lock) _noted.Remove("open:" + path);

            using var reg = ct.Register(pad.Dispose); // unblocks the read on shutdown
            while (!ct.IsCancellationRequested && pad.TryRead(out var state))
            {
                si.Reports++; si.LastReportAt = DateTime.UtcNow; si.State = state;
                virt.Update(state);
            }

            Log.Info($"[pad {slot + 1}] disconnected");
        }
        catch (Exception ex)
        {
            Log.Error($"[pad {slot + 1}] error: {ex.Message}");
        }
        finally
        {
            if (hidden) _hide.Unhide(dev);
            lock (_lock)
            {
                _sessions.Remove(slot);
                _slots[slot] = false;
                _active.Remove(path);
                if (serial != null) _activeSerials.Remove(serial);
            }
        }
    }
}

/// <summary>Plain data about one running pad. Written by the pad's thread, read by the UI (races are harmless here).</summary>
public sealed class SessionInfo
{
    public int Slot;
    public string Name = "";
    public string Path = "";
    public string Transport = "";
    public string Mode = "";
    public bool Hidden;
    public DateTime Started;
    public HidPad? Pad;
    public IVirtualPad? Virtual;
    public long Reports;
    public DateTime LastReportAt;
    public PadState State;
    public byte GameRumbleLarge, GameRumbleSmall;
    public long GameRumbleCount;
    public DateTime GameRumbleAt;
}
