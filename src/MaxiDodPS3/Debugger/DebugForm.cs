#if DEBUGGER
using System.Drawing.Imaging;
using System.Text;
using HidSharp;

namespace MaxiDodPS3.DebugTools;

/// <summary>
/// TEMPORARY debugging window. Shows what the bridge is doing, tests rumble every way it can be sent
/// (including through XInput the way a game does), and has a Screenshot button so you can send me the result.
/// Remove it by setting EnableDebugger to false in MaxiDodPS3.csproj (and deleting this folder).
/// </summary>
public sealed class DebugForm : Form
{
    private readonly Bridge _bridge;
    private readonly RichTextBox _log = new();
    private readonly TextBox _pads = new();
    private readonly TextBox _env = new();
    private readonly ComboBox _padPick = new();
    private readonly TrackBar _strong = new();
    private readonly TrackBar _weak = new();
    private readonly Label _result = new();
    private readonly System.Windows.Forms.Timer _tick = new();
    private readonly Queue<Log.Entry> _pending = new();
    private readonly object _pendLock = new();
    private volatile bool _sweepRunning;
    private readonly List<GroupBox> _padBoxes = new();
    private volatile SessionInfo[] _sess = Array.Empty<SessionInfo>();

    /// <summary>The X button hides the window (the app keeps running in the tray) unless this is set.</summary>
    public bool AllowClose;

    public DebugForm(Bridge bridge)
    {
        _bridge = bridge;
        Text = "MaxiDodPS3 debugger (temporary)";
        Width = 1100; Height = 860;
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;
        Font = new Font("Segoe UI", 9f);

        BuildLayout();

        foreach (var e in Log.Snapshot()) AppendLog(e);
        Log.Written += OnLog;

        _tick.Interval = 400;
        _tick.Tick += (_, _) => RefreshPanels();
        _tick.Start();

        KeyDown += (_, e) => { if (e.KeyCode == Keys.F12) TakeScreenshot(); };
        RefreshEnv();
    }

    // ---------------------------------------------------------------- layout

    private void BuildLayout()
    {
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(6, 6, 6, 0), WrapContents = false };
        top.Controls.Add(Btn("Screenshot (F12)", (_, _) => TakeScreenshot()));
        top.Controls.Add(Btn("Save report (.txt)", (_, _) => SaveReport()));
        top.Controls.Add(Btn("Copy log", (_, _) => { try { Clipboard.SetText(AllLogText()); Say("Log copied to clipboard."); } catch { } }));
        top.Controls.Add(Btn("Re-check drivers", (_, _) => RefreshEnv()));
        top.Controls.Add(Btn("Clear log", (_, _) => _log.Clear()));
        Controls.Add(top);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        var tabPads = new TabPage("Controllers (visual test)");
        var tabDiag = new TabPage("Diagnostics, rumble test and log");
        tabs.TabPages.Add(tabPads);
        tabs.TabPages.Add(tabDiag);
        Controls.Add(tabs);
        tabs.BringToFront();
        tabPads.Controls.Add(BuildPadsTab());

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 400 };
        tabDiag.Controls.Add(split);

        // ---- top half: environment + pads + rumble test
        var upper = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
        upper.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));
        upper.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 37));
        upper.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
        split.Panel1.Controls.Add(upper);

        upper.Controls.Add(Boxed("Environment", Mono(_env)), 0, 0);
        upper.Controls.Add(Boxed("Pads (live)", Mono(_pads)), 1, 0);
        upper.Controls.Add(Boxed("Rumble test", BuildRumblePanel()), 2, 0);

        // ---- bottom half: log
        _log.Dock = DockStyle.Fill;
        _log.ReadOnly = true;
        _log.BackColor = Color.FromArgb(20, 22, 28);
        _log.ForeColor = Color.Gainsboro;
        _log.Font = new Font("Consolas", 9f);
        _log.WordWrap = true;
        split.Panel2.Controls.Add(Boxed("Log (also saved to " + Log.FilePath + ")", _log));
    }

    /// <summary>One grey controller picture per pad slot. Pressed buttons turn red and fade out after 3 s.</summary>
    private Control BuildPadsTab()
    {
        var root = new Panel { Dock = DockStyle.Fill };

        var head = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 78, Padding = new Padding(6, 4, 6, 0) };
        head.Controls.Add(new Label
        {
            AutoSize = false, Width = 1040, Height = 38,
            Text = "Press buttons on each pad: a pressed button turns RED and fades out 3 seconds after you let go, so you can test " +
                   "the other buttons meanwhile. Sticks light up when moved, triggers show their value. The two motor circles turn " +
                   "red and shake while the pad is told to vibrate (by a game or by the buttons below).",
        });
        head.Controls.Add(Btn("Rumble large motor (1.5 s)", (_, _) => RumbleAll(255, 0)));
        head.Controls.Add(Btn("Rumble small motor (1.5 s)", (_, _) => RumbleAll(0, 255)));
        head.Controls.Add(Btn("Rumble both (1.5 s)", (_, _) => RumbleAll(255, 255)));

        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(6) };
        int n = Math.Clamp(_bridge.Config.MaxPads, 1, 8);
        for (int i = 0; i < n; i++)
        {
            int slot = i;
            var view = new PadView(() => Array.Find(_sess, x => x.Slot == slot), _bridge.Config) { Dock = DockStyle.Fill };
            var box = new GroupBox { Text = $"Pad {i + 1}: (empty)", Width = 450, Height = 345, Margin = new Padding(6) };
            box.Controls.Add(view);
            flow.Controls.Add(box);
            _padBoxes.Add(box);
        }

        root.Controls.Add(flow);
        root.Controls.Add(head);
        flow.BringToFront();
        return root;
    }

    /// <summary>Direct rumble on every running pad, so you can watch the motor circles light up.</summary>
    private async void RumbleAll(byte large, byte small)
    {
        var pads = _sess.Where(x => x.Pad != null).Select(x => x.Pad!).ToList();
        if (pads.Count == 0) { Say("No pad is running in the bridge."); return; }
        try
        {
            foreach (var p in pads) p.SetRumble(large, small);
            await Task.Delay(1500);
        }
        finally
        {
            foreach (var p in pads) { try { p.SetRumble(0, 0); } catch { } }
        }
    }

    private Control BuildRumblePanel()
    {
        var p = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(4), AutoScroll = true };

        _padPick.DropDownStyle = ComboBoxStyle.DropDownList;
        _padPick.Width = 300;
        p.Controls.Add(new Label { Text = "Pad to test (must be running in the bridge):", AutoSize = true });
        p.Controls.Add(_padPick);

        _strong.Minimum = 0; _strong.Maximum = 255; _strong.Value = 255; _strong.Width = 300; _strong.TickFrequency = 51;
        _weak.Minimum = 0; _weak.Maximum = 255; _weak.Value = 255; _weak.Width = 300; _weak.TickFrequency = 51;
        p.Controls.Add(new Label { Text = "Strong (left/large) motor", AutoSize = true });
        p.Controls.Add(_strong);
        p.Controls.Add(new Label { Text = "Weak (right/small) motor", AutoSize = true });
        p.Controls.Add(_weak);

        p.Controls.Add(Btn("1. Direct rumble 2 s  (app -> pad)", (_, _) => DirectRumble()));
        p.Controls.Add(Btn("2. Through XInput 2 s  (like a game)", (_, _) => XInputRumble()));
        p.Controls.Add(Btn("3. Sweep all write methods", (_, _) => _ = SweepAsync()));
        p.Controls.Add(Btn("Stop rumble", (_, _) => StopRumble()));

        _result.AutoSize = false; _result.Width = 300; _result.Height = 110;
        _result.ForeColor = Color.DarkSlateBlue;
        p.Controls.Add(_result);

        p.Controls.Add(new Label
        {
            AutoSize = false, Width = 300, Height = 90,
            Text = "How to read it:\r\n1 works, 2 fails -> the problem is between the game and the virtual pad (XInput/ViGEm).\r\n" +
                   "1 fails -> the pad rejects our report; use 3 to find a method that works.",
        });
        return p;
    }

    private static Button Btn(string text, EventHandler click)
    {
        var b = new Button { Text = text, AutoSize = true, Height = 28, Margin = new Padding(3) };
        b.Click += click;
        return b;
    }

    private static TextBox Mono(TextBox t)
    {
        t.Multiline = true; t.ReadOnly = true; t.ScrollBars = ScrollBars.Both; t.WordWrap = false;
        t.Font = new Font("Consolas", 9f); t.Dock = DockStyle.Fill; t.BackColor = Color.White;
        return t;
    }

    private static GroupBox Boxed(string title, Control inner)
    {
        var g = new GroupBox { Text = title, Dock = DockStyle.Fill };
        inner.Dock = DockStyle.Fill;
        g.Controls.Add(inner);
        return g;
    }

    // ---------------------------------------------------------------- log

    private void OnLog(Log.Entry e)
    {
        lock (_pendLock) { _pending.Enqueue(e); }
    }

    private void AppendLog(Log.Entry e)
    {
        Color c = e.Level switch { Log.Level.Error => Color.Tomato, Log.Level.Warn => Color.Gold, _ => Color.Gainsboro };
        _log.SelectionStart = _log.TextLength;
        _log.SelectionColor = c;
        _log.AppendText($"{e.Time:HH:mm:ss.fff} {e.Text}\n");
        _log.SelectionColor = _log.ForeColor;
        if (_log.Lines.Length > 1500) { _log.Select(0, _log.GetFirstCharIndexFromLine(300)); _log.SelectedText = ""; }
        _log.SelectionStart = _log.TextLength;
        _log.ScrollToCaret();
    }

    private string AllLogText() => string.Join(Environment.NewLine,
        Log.Snapshot().Select(e => $"{e.Time:HH:mm:ss.fff} [{e.Level}] {e.Text}"));

    private void Say(string text)
    {
        Log.Info("[debugger] " + text);
        _result.Text = text;
    }

    // ---------------------------------------------------------------- live panels

    private void RefreshPanels()
    {
        List<Log.Entry> batch;
        lock (_pendLock) { batch = _pending.ToList(); _pending.Clear(); }
        foreach (var e in batch) AppendLog(e);

        var mgr = _bridge.Manager;
        var sessions = mgr?.Sessions ?? Array.Empty<SessionInfo>();
        _sess = sessions;
        for (int i = 0; i < _padBoxes.Count; i++)
        {
            var si = Array.Find(sessions, x => x.Slot == i);
            string title = si == null ? $"Pad {i + 1}: (empty)" : $"Pad {i + 1}: {si.Name} ({si.Transport}, {si.Mode})";
            if (_padBoxes[i].Text != title) _padBoxes[i].Text = title;
        }

        // keep the pad picker in step with the running pads
        string? selected = _padPick.SelectedItem as string;
        var names = sessions.Select(s => $"Pad {s.Slot + 1}: {s.Name} ({s.Transport})").ToArray();
        if (!names.SequenceEqual(_padPick.Items.Cast<string>()))
        {
            _padPick.Items.Clear();
            _padPick.Items.AddRange(names);
            if (names.Length > 0) _padPick.SelectedItem = names.Contains(selected) ? selected : names[0];
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Bridge: {_bridge.Status}");
        sb.AppendLine();
        if (sessions.Length == 0)
            sb.AppendLine("No pad is running in the bridge.\r\n(Plug one in. If one is plugged in and nothing\r\nshows here, read the log below.)");
        foreach (var s in sessions)
        {
            double age = s.LastReportAt == default ? -1 : (DateTime.UtcNow - s.LastReportAt).TotalSeconds;
            sb.AppendLine($"[Pad {s.Slot + 1}] {s.Name}");
            sb.AppendLine($"  link: {s.Transport}   output: {s.Mode}   hidden: {s.Hidden}");
            sb.AppendLine($"  input reports: {s.Reports}  (last {(age < 0 ? "never" : age.ToString("0.0") + " s ago")})");
            var st = s.State;
            sb.AppendLine($"  sticks L {st.LX,3},{st.LY,3}  R {st.RX,3},{st.RY,3}   L2 {st.L2,3}  R2 {st.R2,3}");
            sb.AppendLine($"  buttons: {Buttons(st)}");
            sb.AppendLine($"  game rumble requests: {s.GameRumbleCount}  last L={s.GameRumbleLarge} S={s.GameRumbleSmall}" +
                          (s.GameRumbleAt == default ? "" : $"  ({(DateTime.Now - s.GameRumbleAt).TotalSeconds:0.0} s ago)"));
            if (s.Pad != null)
            {
                sb.AppendLine($"  writes to pad: ok {s.Pad.WriteOk}  failed {s.Pad.WriteFail}   report len {s.Pad.OutputReportLength}");
                sb.AppendLine($"  last write: {s.Pad.LastWriteInfo}");
                if (s.Pad.LastReport is { } r)
                    sb.AppendLine($"  last report: {BitConverter.ToString(r, 0, Math.Min(r.Length, 24))}{(r.Length > 24 ? "..." : "")}");
            }
            if (s.Virtual != null && s.Virtual.XInputIndex >= 0) sb.AppendLine($"  XInput slot: {s.Virtual.XInputIndex}");
            sb.AppendLine();
        }
        _pads.Text = sb.ToString();
    }

    private static string Buttons(PadState s)
    {
        var l = new List<string>();
        void A(bool v, string n) { if (v) l.Add(n); }
        A(s.Cross, "X"); A(s.Circle, "O"); A(s.Square, "[]"); A(s.Triangle, "^");
        A(s.L1, "L1"); A(s.R1, "R1"); A(s.L3, "L3"); A(s.R3, "R3");
        A(s.Select, "Sel"); A(s.Start, "Start"); A(s.Ps, "PS");
        A(s.Up, "Up"); A(s.Down, "Dn"); A(s.Left, "Lt"); A(s.Right, "Rt");
        return l.Count == 0 ? "-" : string.Join(" ", l);
    }

    private void RefreshEnv()
    {
        var cfg = _bridge.Config;
        var sb = new StringBuilder();
        void L(string tag, string text) => sb.AppendLine($"[{tag,-7}] {text}");

        L(Startup.IsAdmin() ? "OK" : "WARN", Startup.IsAdmin() ? "Running as administrator" : "NOT administrator (HidHide may fail)");
        L(Service("ViGEmBus") ? "OK" : "MISSING", "ViGEmBus driver");
        L(Service("BthPS3") ? "OK" : "INFO", "BthPS3 (wireless DS3 only)");
        L(Service("dshidmini") ? "WARN" : "OK", Service("dshidmini") ? "DsHidMini present: may duplicate DS3" : "DsHidMini not present");
        L(HidHideManager.FindCli(cfg.HidHideCliPath) != null ? "OK" : "WARN", "HidHide CLI");
        L(HostRadio.TryGetAddress(out var a, out _) ? "OK" : "INFO", HostRadio.TryGetAddress(out a, out _) ? "Bluetooth radio " + HostRadio.Format(a) : "No Bluetooth radio");
        L(Startup.IsEnabled() ? "OK" : "INFO", "Start with Windows: " + (Startup.IsEnabled() ? "on" : "off"));
        sb.AppendLine();
        sb.AppendLine($"Modes: DS3={cfg.Ps3Mode} DS4={cfg.Ds4Mode} DS5={cfg.DualSenseMode}");
        sb.AppendLine($"Rumble: {cfg.RumbleMode}/{cfg.RumbleRefreshMs}ms swap={cfg.SwapMotors} write={cfg.WriteMethod}");
        sb.AppendLine($"DS3 dur={cfg.RumbleDuration} ledInReport={cfg.LedInReport}");
        sb.AppendLine($"DS4 flags={cfg.Ds4OutputFlags}  DS5 haptics={cfg.Ds5HapticsSelect}");
        sb.AppendLine();
        sb.AppendLine("HID devices matching a known pad:");
        try
        {
            var found = Devices.Enumerate(cfg).ToList();
            if (found.Count == 0) sb.AppendLine("  none visible");
            foreach (var (d, i) in found)
                sb.AppendLine($"  {i.Name}\r\n    {d.VendorID:X4}:{d.ProductID:X4} {HidUtil.Transport(d.DevicePath)} in={Safe(() => d.GetMaxInputReportLength())} out={Safe(() => d.GetMaxOutputReportLength())}");
        }
        catch (Exception ex) { sb.AppendLine("  enumeration failed: " + ex.Message); }
        _env.Text = sb.ToString();
    }

    private static string Safe(Func<int> f) { try { return f().ToString(); } catch { return "?"; } }

    private static bool Service(string name)
    {
        try { using var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + name); return k != null; }
        catch { return false; }
    }

    // ---------------------------------------------------------------- rumble tests

    private SessionInfo? Picked()
    {
        var sessions = _bridge.Manager?.Sessions ?? Array.Empty<SessionInfo>();
        int idx = _padPick.SelectedIndex;
        if (idx < 0 || idx >= sessions.Length) { Say("No pad selected. The pad has to be running in the bridge first."); return null; }
        return sessions[idx];
    }

    /// <summary>App -> pad directly: the same path the bridge uses when a game asks for rumble.</summary>
    private async void DirectRumble()
    {
        var s = Picked(); if (s?.Pad == null) return;
        byte st = (byte)_strong.Value, wk = (byte)_weak.Value;
        long okBefore = s.Pad.WriteOk, failBefore = s.Pad.WriteFail;
        s.Pad.SetRumble(st, wk);
        await Task.Delay(2000);
        s.Pad.SetRumble(0, 0);
        Say($"Direct rumble sent (strong {st}, weak {wk}).\r\nWrites ok +{s.Pad.WriteOk - okBefore}, failed +{s.Pad.WriteFail - failBefore}.\r\n{s.Pad.LastWriteInfo}\r\nDid you feel it? If writes are OK but nothing vibrated, the pad ignores this report: try the sweep.");
    }

    /// <summary>Game -> XInput -> ViGEm -> bridge -> pad: proves the whole chain a game uses.</summary>
    private async void XInputRumble()
    {
        var s = Picked(); if (s == null) return;
        if (s.Virtual == null || s.Virtual.XInputIndex < 0)
        {
            Say($"This pad's output is '{s.Mode}', not x360, so there is no XInput path to test. Set the mode to x360 to test it.");
            return;
        }
        int idx = s.Virtual.XInputIndex;
        long before = s.GameRumbleCount;
        var v = new Native.XINPUT_VIBRATION { wLeftMotorSpeed = (ushort)(_strong.Value * 257), wRightMotorSpeed = (ushort)(_weak.Value * 257) };
        int rc;
        try { rc = Native.XInputSetState(idx, ref v); }
        catch (Exception ex) { Say("XInput could not be called: " + ex.Message); return; }

        await Task.Delay(2000);
        long got = s.GameRumbleCount - before;
        var off = new Native.XINPUT_VIBRATION();
        Native.XInputSetState(idx, ref off);
        Say($"XInputSetState(slot {idx}) returned {rc} (0 = OK).\r\nThe bridge received {got} rumble request(s) from the virtual pad.\r\n" +
            (rc != 0 ? "Windows did not accept it: the virtual pad is not at that XInput slot."
             : got == 0 ? "XInput accepted it but ViGEm never passed it to the bridge: driver side problem."
             : "The bridge got it. If the pad did not vibrate, test 1 and 3 show where it stops."));
    }

    private void StopRumble()
    {
        var s = Picked(); s?.Pad?.SetRumble(0, 0);
        if (s?.Virtual is { XInputIndex: >= 0 } v) { var off = new Native.XINPUT_VIBRATION(); Native.XInputSetState(v.XInputIndex, ref off); }
        Say("Stopped.");
    }

    /// <summary>
    /// Tries every write method on the picked pad and reports write results for each, so a screenshot shows
    /// which ones the pad accepted and which it refused. You still feel which one actually vibrates.
    /// </summary>
    private async Task SweepAsync()
    {
        if (_sweepRunning) return;
        var s = Picked(); if (s?.Pad == null) return;
        var info = Devices.Enumerate(_bridge.Config).FirstOrDefault(p => p.Device.DevicePath == s.Path);
        if (info.Device == null) { Say("Could not find the pad's HID device again."); return; }

        _sweepRunning = true;
        try
        {
            Say("Sweep started (about 30 s). Hold the pad and note which letters you FEEL.");
            s.Pad.SetRumble(0, 0); // the bridge stops driving it; each test opens the pad a second time

            var variants = Variants(info.Info.Kind);
            var results = new StringBuilder();
            foreach (var (name, apply) in variants)
            {
                var c = new Config { ExtraDevices = _bridge.Config.ExtraDevices.ToList() };
                apply(c); c.Normalize();

                using var pad = HidPad.TryOpen(info.Device, info.Info, c);
                if (pad == null)
                {
                    results.AppendLine($"{name}: could not open the pad (the bridge holds it exclusively?)");
                    Log.Warn($"[sweep] {name}: could not open the pad");
                    continue;
                }
                Say($"Now trying {name}\r\nVibrating for 2 s: do you feel it?");
                pad.SetRumble(255, 255);
                await Task.Delay(2000);
                pad.SetRumble(0, 0);
                string line = $"{name}: writes ok {pad.WriteOk}, failed {pad.WriteFail}; {pad.LastWriteInfo}";
                results.AppendLine(line);
                Log.Info("[sweep] " + line);
                await Task.Delay(500);
            }
            Say("Sweep finished. Screenshot this window (F12) and tell me which letter(s) you felt.\r\n" + results);
        }
        finally { _sweepRunning = false; }
    }

    private static List<(string, Action<Config>)> Variants(PadKind kind)
    {
        var v = new List<(string, Action<Config>)>
        {
            ("A: standard + refresh", c => { }),
            ("B: single write", c => c.RumbleMode = "latch"),
            ("C: motors swapped", c => c.SwapMotors = true),
            ("D: HidD_SetOutputReport", c => c.WriteMethod = "setoutput"),
            ("E: swapped + SetOutputReport", c => { c.SwapMotors = true; c.WriteMethod = "setoutput"; }),
        };
        switch (kind)
        {
            case PadKind.Ps3:
                v.Add(("F: duration 50", c => c.RumbleDuration = 50));
                v.Add(("G: no LED bytes", c => c.LedInReport = false));
                v.Add(("H: duration 50 + SetOutputReport", c => { c.RumbleDuration = 50; c.WriteMethod = "setoutput"; }));
                v.Add(("I: swapped + no LED + SetOutputReport", c => { c.SwapMotors = true; c.LedInReport = false; c.WriteMethod = "setoutput"; }));
                break;
            case PadKind.Ds4:
                v.Add(("F: flags 255", c => c.Ds4OutputFlags = 0xFF));
                v.Add(("G: flags 255 + SetOutputReport", c => { c.Ds4OutputFlags = 0xFF; c.WriteMethod = "setoutput"; }));
                v.Add(("H: flags 255 + swapped", c => { c.Ds4OutputFlags = 0xFF; c.SwapMotors = true; }));
                break;
            default:
                v.Add(("F: no haptics-select", c => c.Ds5HapticsSelect = false));
                v.Add(("G: no haptics-select + SetOutputReport", c => { c.Ds5HapticsSelect = false; c.WriteMethod = "setoutput"; }));
                v.Add(("H: no haptics-select + swapped", c => { c.Ds5HapticsSelect = false; c.SwapMotors = true; }));
                break;
        }
        return v;
    }

    // ---------------------------------------------------------------- screenshot / report

    private void TakeScreenshot()
    {
        try
        {
            RefreshPanels();
            using var bmp = new Bitmap(Width, Height);
            // CopyFromScreen shows exactly what you see (including text boxes), unlike DrawToBitmap.
            using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(Location, Point.Empty, Size);

            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "MaxiDodPS3");
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, $"debug-{DateTime.Now:yyyyMMdd-HHmmss}.png");
            bmp.Save(file, ImageFormat.Png);
            try { Clipboard.SetImage(bmp); } catch { }
            Say($"Screenshot saved and copied to the clipboard:\r\n{file}\r\nPaste or attach it in the chat.");
            try { System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{file}\""); } catch { }
        }
        catch (Exception ex) { Say("Screenshot failed: " + ex.Message); }
    }

    private void SaveReport()
    {
        try
        {
            string dir = Log.Folder; Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, $"report-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            File.WriteAllText(file, "== ENVIRONMENT ==\r\n" + _env.Text + "\r\n== PADS ==\r\n" + _pads.Text + "\r\n== LOG ==\r\n" + AllLogText());
            Say("Report saved: " + file);
            try { System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{file}\""); } catch { }
        }
        catch (Exception ex) { Say("Could not save report: " + ex.Message); }
    }

    // ---------------------------------------------------------------- closing

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!AllowClose && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;   // hide to the tray, like a chat app; the bridge keeps running
            Hide();
            return;
        }
        Log.Written -= OnLog;
        _tick.Stop();
        base.OnFormClosing(e);
    }
}
#endif
