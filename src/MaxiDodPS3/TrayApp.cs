using System.Diagnostics;
using System.Drawing.Drawing2D;

namespace MaxiDodPS3;

/// <summary>
/// The background face of the app: a tray icon (like WhatsApp) that keeps the bridge running with no window.
/// Closing any window only hides it; "Exit" in the tray menu is the one way to quit.
/// </summary>
public sealed class TrayApp : ApplicationContext
{
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
        menu.Items.Add("Open log folder", null, (_, _) => OpenLogFolder());
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
