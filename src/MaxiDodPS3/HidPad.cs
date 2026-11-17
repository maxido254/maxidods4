using HidSharp;

namespace MaxiDodPS3;

public interface IPhysicalPad : IDisposable
{
    string Path { get; }
    PadKind Kind { get; }
    string Name { get; }
    bool IsBluetooth { get; }

    /// <summary>Blocks until a valid input report arrives. False means the pad is gone.</summary>
    bool TryRead(out PadState state);

    void SetLed(int slot);

    /// <summary>strong = large/left motor, weak = small/right motor, both 0-255.</summary>
    void SetRumble(byte strong, byte weak);
}

/// <summary>
/// One physical pad (DS3, DS4 or DualSense, genuine or clone) opened as a HID device over USB or Bluetooth.
/// </summary>
public sealed class HidPad : IPhysicalPad
{
    private readonly Config _cfg;
    private readonly HidStream _stream;
    private readonly IPadCodec _codec;
    private readonly byte[] _readBuf;
    private readonly int _outLen;
    private readonly object _writeLock = new();
    private readonly System.Threading.Timer? _refresh;
    private RawHidWriter? _raw;
    private bool _preferRaw;
    private bool _errLogged;
    private byte _strong;
    private byte _weak;
    private int _slot;
    private int _disposed;

    // ---- Telemetry (read by the debugger window; harmless otherwise) ----
    public long WriteOk;
    public long WriteFail;
    public string LastWriteInfo = "no write yet";
    public byte LastStrong, LastWeak;
    public int OutputReportLength => _outLen;
    public byte[]? LastReport;

    public string Path { get; }
    public PadKind Kind { get; }
    public string Name { get; }
    public bool IsBluetooth { get; }

    private HidPad(HidDevice dev, KnownPad info, HidStream stream, IPadCodec codec, bool bluetooth, Config cfg)
    {
        Path = dev.DevicePath;
        Kind = info.Kind;
        Name = info.Name;
        IsBluetooth = bluetooth;
        _cfg = cfg;
        _stream = stream;
        _codec = codec;

        int maxIn = 0;
        try { maxIn = dev.GetMaxInputReportLength(); } catch { }
        _readBuf = new byte[maxIn > 0 ? maxIn : 64];

        // 0 means "unknown": each codec then uses its own standard length.
        int outLen = 0;
        try { outLen = dev.GetMaxOutputReportLength(); } catch { }
        _outLen = outLen;

        if (cfg.RumbleMode == "refresh")
        {
            _refresh = new System.Threading.Timer(_ =>
            {
                lock (_writeLock)
                {
                    if (_strong > 0 || _weak > 0) Send();
                }
            }, null, cfg.RumbleRefreshMs, cfg.RumbleRefreshMs);
        }
    }

    /// <summary>Opens the pad and wakes it up. Returns null if it cannot be opened.</summary>
    public static HidPad? TryOpen(HidDevice dev, KnownPad info, Config cfg)
    {
        if (!dev.TryOpen(out HidStream stream)) return null;

        bool bt = HidUtil.LooksBluetooth(dev.DevicePath);
        IPadCodec codec = Codecs.Create(info.Kind);
        try { codec.Prepare(stream, dev, bt); } catch { /* not fatal */ }

        // Idle pads can be quiet for a while, so a timeout just means "try again", not "gone".
        stream.ReadTimeout = 5000;
        return new HidPad(dev, info, stream, codec, bt, cfg);
    }

    public bool TryRead(out PadState state)
    {
        while (true)
        {
            int n;
            try { n = _stream.Read(_readBuf); }
            catch (TimeoutException)
            {
                if (Volatile.Read(ref _disposed) != 0) { state = default; return false; }
                continue;
            }
            catch { state = default; return false; }

            if (_codec.TryParse(_readBuf.AsSpan(0, n), IsBluetooth, out state)) return true;
        }
    }

    public void SetLed(int slot)
    {
        lock (_writeLock) { _slot = slot; Send(); }
    }

    public void SetRumble(byte strong, byte weak)
    {
        lock (_writeLock)
        {
            if (strong == _strong && weak == _weak) return; // only write on change; the refresh timer handles repeats
            _strong = strong; _weak = weak;
            Send();
        }
    }

    // Caller holds _writeLock.
    private void Send()
    {
        if (Volatile.Read(ref _disposed) != 0) return;

        byte strongOut = _strong;
        byte weakOut = _weak;
        if (_cfg.SwapMotors) { strongOut = _weak; weakOut = _strong; }

        var buf = _codec.BuildOutput(strongOut, weakOut, _slot, IsBluetooth, _outLen, _cfg);
        LastReport = buf;
        LastStrong = strongOut; LastWeak = weakOut;
        bool ok = WriteBuf(buf);
        if (ok) WriteOk++; else WriteFail++;
        if (!ok && !_errLogged)
        {
            _errLogged = true;
            Log.Warn($"[rumble] The pad did not accept the output report ({HidUtil.Transport(Path)}). " +
                              "Run 'MaxiDodPS3 rumble-test' to find a method that works for this pad.");
        }
    }

    private bool WriteBuf(byte[] buf)
    {
        string method = _cfg.WriteMethod;

        if (method != "setoutput" && !_preferRaw)
        {
            try { _stream.Write(buf); LastWriteInfo = $"OK via stream write ({buf.Length} bytes)"; return true; }
            catch (Exception ex)
            {
                LastWriteInfo = $"stream write failed: {ex.GetType().Name}: {ex.Message}";
                if (method == "stream") return false;
            }
        }

        // "setoutput", or "auto" falling back after a failed normal write.
        _raw ??= RawHidWriter.TryOpen(Path);
        if (_raw != null && _raw.SetOutputReport(buf))
        {
            if (method == "auto") _preferRaw = true;
            LastWriteInfo = $"OK via HidD_SetOutputReport ({buf.Length} bytes)";
            return true;
        }
        LastWriteInfo += _raw == null ? " | HidD_SetOutputReport: could not open raw handle" : " | HidD_SetOutputReport failed too";
        return false;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _refresh?.Dispose();

        // Best-effort "motors off" before closing.
        lock (_writeLock)
        {
            try
            {
                var off = _codec.BuildOutput(0, 0, _slot, IsBluetooth, _outLen, _cfg);
                try { _stream.Write(off); }
                catch { _raw?.SetOutputReport(off); }
            }
            catch { }
        }

        try { _stream.Dispose(); } catch { }
        _raw?.Dispose();
    }
}
