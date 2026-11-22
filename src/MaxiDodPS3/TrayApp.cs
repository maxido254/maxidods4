using System.Diagnostics;
using System.Drawing.Drawing2D;

namespace MaxiDodPS3;

/// <summary>
/// The background face of the app: a tray icon (like WhatsApp) that keeps the bridge running with no window.
/// Closing any window only hides it; "Exit" in the tray menu is the one way to quit.
/// </summary>
public sealed class TrayApp : ApplicationContext
{
    private static void AddContact(ContextMenuStrip menu, string text, Color color, string letter, string url)
    {
        var bmp = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var b = new SolidBrush(color);
            g.FillEllipse(b, 0, 0, 15, 15);
            using var f = new Font("Segoe UI", 7.5f, FontStyle.Bold);
            var sz = g.MeasureString(letter, f);
            g.DrawString(letter, f, Brushes.White, (16 - sz.Width) / 2f, (16 - sz.Height) / 2f);
        }
        menu.Items.Add(text, bmp, (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
        });
    }

    private readonly Bridge _bridge;
    private readonly NotifyIcon _tray;
    private readonly System.Windows.Forms.Timer _tick;
    private readonly ToolStripMenuItem _status;
    private readonly ToolStripMenuItem _startup;
    private readonly EventWaitHandle _showSignal;
    private readonly Icon _icon;
    private int _lastPadCount;
    private readonly Control _ui = new();   // only used to hop back onto the UI thread
#if DEBUGGER
    private DebugTools.DebugForm? _debug;
#endif

    public TrayApp(Bridge bridge, bool openDebugger, EventWaitHandle showSignal)
    {
        _bridge = bridge;
        _showSignal = showSignal;
        _icon = MakeIcon();

        _status = new ToolStripMenuItem("Starting...") { Enabled = false };
        _startup = new ToolStripMenuItem("Start with Windows") { CheckOnClick = false };
        _startup.Click += (_, _) => ToggleStartup();

        var menu = new ContextMenuStrip();
        menu.Items.Add(_status);
        menu.Items.Add(new ToolStripSeparator());
#if DEBUGGER
        menu.Items.Add("Open debugger", null, (_, _) => ShowDebugger());
#endif
        menu.Items.Add(_startup);
        menu.Items.Add("Set up wireless DS3 (Bluetooth)...", null, (_, _) => SetupBluetooth());
        menu.Items.Add("Pair a DS3 (plug it in by USB first)", null, (_, _) => PairDs3());
        menu.Items.Add("Open log folder", null, (_, _) => OpenLogFolder());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("maxidoizooh254 (author and publisher)").Enabled = false;
        AddContact(menu, "WhatsApp  +254 115 710 055", Color.FromArgb(37, 211, 102), "W", "https://wa.me/254115710055");
        AddContact(menu, "Instagram  maxtech_058", Color.FromArgb(225, 48, 108), "I", "https://www.instagram.com/maxtech_058");
        AddContact(menu, "TikTok  @maxtech_058", Color.FromArgb(20, 20, 20), "T", "https://www.tiktok.com/@maxtech_058");
        AddContact(menu, "YouTube  @MaxidoEnt", Color.FromArgb(255, 0, 0), "Y", "https://www.youtube.com/@MaxidoEnt");
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitThread());
        menu.Opening += (_, _) => _startup.Checked = Startup.IsEnabled();

        _tray = new NotifyIcon
        {
            Icon = _icon,
            Text = "MaxiDodPS3",
            ContextMenuStrip = menu,
            Visible = true,
        };
#if DEBUGGER
        _tray.DoubleClick += (_, _) => ShowDebugger();
#endif

        FirstRunSetup();

        _tick = new System.Windows.Forms.Timer { Interval = 1500 };
        _tick.Tick += (_, _) => Refresh();
        _tick.Start();

        // A second launch of the exe pokes this event, which brings up the debugger of the running copy.
        _ = _ui.Handle; // force handle creation on the UI thread
        var waiter = new Thread(() =>
        {
            while (true)
            {
                try { _showSignal.WaitOne(); } catch { return; }
#if DEBUGGER
                try { _ui.BeginInvoke(new Action(ShowDebugger)); } catch { }
#endif
            }
        }) { IsBackground = true };
        waiter.Start();

        _bridge.Start();

#if DEBUGGER
        if (openDebugger) ShowDebugger();
#endif
    }

    private void FirstRunSetup()
    {
        // The first time the app ever runs, turn "start with Windows" on, the way chat apps do.
        string marker = Path.Combine(Log.Folder, "first-run.done");
        if (File.Exists(marker)) return;
        try
        {
            Directory.CreateDirectory(Log.Folder);
            File.WriteAllText(marker, DateTime.Now.ToString("o"));
        }
        catch { }
        if (Startup.Enable(out string msg))
            _tray.ShowBalloonTip(4000, "MaxiDodPS3 is running", "It lives in the tray and starts with Windows. " + msg, ToolTipIcon.Info);
    }

    private void ToggleStartup()
    {
        if (Startup.IsEnabled())
        {
            Startup.Disable();
            _tray.ShowBalloonTip(2500, "MaxiDodPS3", "Will no longer start with Windows.", ToolTipIcon.Info);
        }
        else
        {
            Startup.Enable(out string msg);
            _tray.ShowBalloonTip(3000, "MaxiDodPS3", msg, ToolTipIcon.Info);
        }
    }

    private void Refresh()
    {
        var sessions = _bridge.Manager?.Sessions ?? Array.Empty<SessionInfo>();
        string text = sessions.Length == 0
            ? $"{_bridge.Status} - no controllers"
            : $"{_bridge.Status} - {sessions.Length} controller(s)";
        _status.Text = text;
        _tray.Text = ("MaxiDodPS3: " + text).Length > 63 ? ("MaxiDodPS3: " + text).Substring(0, 63) : "MaxiDodPS3: " + text;

        if (sessions.Length > _lastPadCount)
        {
            var s = sessions.OrderByDescending(x => x.Started).First();
            _tray.ShowBalloonTip(2000, "Controller connected", $"{s.Name} ({s.Transport}) as pad {s.Slot + 1}", ToolTipIcon.Info);
        }
        else if (sessions.Length < _lastPadCount)
        {
            _tray.ShowBalloonTip(2000, "Controller disconnected", "A controller was disconnected.", ToolTipIcon.Info);
        }
        _lastPadCount = sessions.Length;
    }

#if DEBUGGER
    private void ShowDebugger()
    {
        if (_debug == null || _debug.IsDisposed)
        {
            _debug = new DebugTools.DebugForm(_bridge);
            _debug.Icon = _icon;
        }
        // Closing the window hides it (see DebugForm), so showing it again is always cheap.
        if (!_debug.Visible) _debug.Show();
        if (_debug.WindowState == FormWindowState.Minimized) _debug.WindowState = FormWindowState.Normal;
        _debug.Activate();
    }
#endif

    private bool _setupRunning;

    private async void SetupBluetooth()
    {
        if (_setupRunning) return;
        if (BluetoothSetup.IsInstalled())
        {
            MessageBox.Show("Wireless DS3 support (BthPS3) is already installed.\n\nPlug each DS3 in by USB, choose \"Pair a DS3\" in this menu, unplug it and press the PS button.",
                "MaxiDodPS3", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var ok = MessageBox.Show(
            "This sets up Bluetooth for DS3 pads.\n\n" +
            "1. Windows makes a System Restore point first, so you can undo it.\n" +
            "2. The official BthPS3 installer is downloaded from its own GitHub page.\n" +
            "3. It is installed once you press Yes on the Windows admin prompt.\n\n" +
            "BthPS3 sits on top of Windows' Bluetooth and does not replace your Bluetooth chip's driver.\n" +
            "Your Bluetooth must be version 2.0 + EDR or newer. Continue?",
            "MaxiDodPS3: wireless DS3", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (ok != DialogResult.Yes) return;

        _setupRunning = true;
        try
        {
            string last = "";
            int code = await BluetoothSetup.RunAsync(s =>
            {
                last = s;
                Log.Info("BthPS3 setup: " + s);
                _ui.BeginInvoke(() => _tray.ShowBalloonTip(4000, "MaxiDodPS3", s, ToolTipIcon.Info));
            });
            MessageBox.Show(last, "MaxiDodPS3: wireless DS3", MessageBoxButtons.OK,
                code == 0 || code == 3010 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
        finally { _setupRunning = false; }
    }

    private void PairDs3()
    {
        if (!HostRadio.TryGetAddress(out var host, out _))
        {
            MessageBox.Show("No Bluetooth radio was found. Turn Bluetooth on first.", "MaxiDodPS3", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        int n = Pairing.SetMasterOnAllUsbPads(_bridge.Config, host);
        MessageBox.Show(n > 0
            ? $"{n} DS3 pad(s) paired to this PC. Unplug them and press the PS button.\n\nWireless DS3 needs BthPS3 (menu: Set up wireless DS3)."
            : "No DS3 pad was found on USB. Plug a DS3 in with a USB cable and try again.",
            "MaxiDodPS3", MessageBoxButtons.OK, n > 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
    }

    private static void OpenLogFolder()
    {
        try
        {
            Directory.CreateDirectory(Log.Folder);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Log.Folder}\"") { UseShellExecute = true });
        }
        catch (Exception ex) { Log.Warn($"Could not open the log folder: {ex.Message}"); }
    }

    protected override void ExitThreadCore()
    {
        _tick.Stop();
        _tray.Visible = false;
        _tray.Dispose();
#if DEBUGGER
        if (_debug != null && !_debug.IsDisposed) { _debug.AllowClose = true; _debug.Close(); }
#endif
        _bridge.Dispose(); // un-hides pads and removes the virtual ones
        base.ExitThreadCore();
    }

    /// <summary>A small gamepad drawn in code, so there is no icon file to ship.</summary>
    private static Icon MakeIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var body = new SolidBrush(Color.FromArgb(0x2D, 0x6C, 0xDF));
            using var white = new SolidBrush(Color.White);
            g.FillEllipse(body, 1, 7, 30, 20);
            g.FillRectangle(white, 8, 13, 8, 3);   // d-pad
            g.FillRectangle(white, 10.5f, 10.5f, 3, 8);
            g.FillEllipse(white, 20, 11, 4, 4);    // buttons
            g.FillEllipse(white, 24, 15, 4, 4);
        }
        IntPtr h = bmp.GetHicon();
        return Icon.FromHandle(h);
    }
}
