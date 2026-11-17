#if DEBUGGER
using System.Drawing.Drawing2D;

namespace MaxiDodPS3.DebugTools;

/// <summary>
/// Picture of one controller for the debugger. The pad is drawn colourless (grey). A button that is pressed
/// turns red and stays visible for FadeSeconds after you let go, so you can press several buttons one after
/// another and still see which ones worked. The two rumble motors light up and shake while the pad is being
/// told to vibrate (by a game or by a test button).
/// </summary>
public sealed class PadView : Control
{
    /// <summary>How long a pressed button takes to fade back to grey after it is released.</summary>
    public const double FadeSeconds = 3.0;

    private const float W = 480f, H = 340f;

    private static readonly Color Grey = Color.FromArgb(232, 232, 232);
    private static readonly Color Red = Color.FromArgb(225, 30, 30);
    private static readonly Color Line = Color.FromArgb(120, 120, 120);

    private readonly Func<SessionInfo?> _session;
    private readonly Config _cfg;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 33 };
    private readonly Dictionary<string, (DateTime At, double Level)> _last = new();
    private readonly Font _font = new("Segoe UI", 12f, FontStyle.Bold, GraphicsUnit.Pixel);
    private readonly Font _symFont = new("Segoe UI Symbol", 15f, FontStyle.Bold, GraphicsUnit.Pixel);
    private readonly Font _small = new("Segoe UI", 11f, FontStyle.Regular, GraphicsUnit.Pixel);
    private readonly Pen _outline = new(Line, 1.6f);
    private readonly StringFormat _center = new() { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
    private string? _path;

    public PadView(Func<SessionInfo?> session, Config cfg)
    {
        _session = session;
        _cfg = cfg;
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = Color.White;
        MinimumSize = new Size(240, 170);
        _timer.Tick += (_, _) => { if (Visible) Invalidate(); };
        _timer.Start();
    }

    // ------------------------------------------------------------ colour / fade

    private static Color Mix(double h)
    {
        h = Math.Clamp(h, 0, 1);
        return Color.FromArgb(
            (int)(Grey.R + (Red.R - Grey.R) * h),
            (int)(Grey.G + (Red.G - Grey.G) * h),
            (int)(Grey.B + (Red.B - Grey.B) * h));
    }

    /// <summary>1 (red) while active, then fades to 0 over FadeSeconds. 'active' is 0..1.</summary>
    private double Heat(string key, double active)
    {
        var now = DateTime.UtcNow;
        if (active > 0.02)
        {
            _last[key] = (now, active);
            return active;
        }
        if (_last.TryGetValue(key, out var l))
        {
            double age = (now - l.At).TotalSeconds;
            if (age < FadeSeconds) return l.Level * (1 - age / FadeSeconds);
        }
        return 0;
    }

    // ------------------------------------------------------------ shapes

    private static GraphicsPath Round(RectangleF r, float rad)
    {
        float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
        var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        g.Clear(BackColor);

        float sc = Math.Min(Width / W, Height / H);
        if (sc <= 0) return;
        g.TranslateTransform((Width - W * sc) / 2f, (Height - H * sc) / 2f);
        g.ScaleTransform(sc, sc);

        var s = _session();
        if (s?.Path != _path) { _last.Clear(); _path = s?.Path; }
        PadState st = s?.State ?? default;
        bool live = s != null;

        // ---- body (colourless). Thick outlines first, fills on top, so overlaps leave one clean outer edge.
        using (var bodyPath = Round(new RectangleF(90, 60, 300, 190), 70))
        using (var left = new GraphicsPath())
        using (var right = new GraphicsPath())
        using (var thick = new Pen(Line, 5f) { LineJoin = LineJoin.Round })
        using (var fill = new SolidBrush(Grey))
        {
            left.AddEllipse(30, 140, 170, 190);
            right.AddEllipse(280, 140, 170, 190);
            g.DrawPath(thick, bodyPath); g.DrawPath(thick, left); g.DrawPath(thick, right);
            g.FillPath(fill, bodyPath); g.FillPath(fill, left); g.FillPath(fill, right);
        }

        // ---- triggers (analog) and bumpers
        Key(g, "L2", new RectangleF(70, 8, 90, 40), 12, st.L2 / 255.0, $"L2 {st.L2}");
        Key(g, "R2", new RectangleF(320, 8, 90, 40), 12, st.R2 / 255.0, $"R2 {st.R2}");
        Key(g, "L1", new RectangleF(70, 54, 90, 22), 9, st.L1 ? 1 : 0, "L1");
        Key(g, "R1", new RectangleF(320, 54, 90, 22), 9, st.R1 ? 1 : 0, "R1");

        // ---- d-pad
        const float a = 26f, dx = 125f, dy = 135f;
        Key(g, "Up", new RectangleF(dx - a / 2, dy - a * 1.5f, a, a), 5, st.Up ? 1 : 0, "");
        Key(g, "Down", new RectangleF(dx - a / 2, dy + a * 0.5f, a, a), 5, st.Down ? 1 : 0, "");
        Key(g, "Left", new RectangleF(dx - a * 1.5f, dy - a / 2, a, a), 5, st.Left ? 1 : 0, "");
        Key(g, "Right", new RectangleF(dx + a * 0.5f, dy - a / 2, a, a), 5, st.Right ? 1 : 0, "");

        // ---- face buttons
        const float fx = 355f, fy = 135f, off = 30f, fr = 16f;
        Dot(g, "Triangle", fx, fy - off, fr, st.Triangle, "\u25B3", true);
        Dot(g, "Cross", fx, fy + off, fr, st.Cross, "X", false);
        Dot(g, "Square", fx - off, fy, fr, st.Square, "\u25A1", true);
        Dot(g, "Circle", fx + off, fy, fr, st.Circle, "O", false);

        // ---- centre buttons
        if (s?.Pad is { Kind: not PadKind.Ps3 })
            Key(g, "Touchpad", new RectangleF(190, 82, 100, 24), 8, st.Touchpad ? 1 : 0, "touchpad");
        Key(g, "Select", new RectangleF(190, 114, 40, 16), 7, st.Select ? 1 : 0, "SEL");
        Key(g, "Start", new RectangleF(250, 114, 40, 16), 7, st.Start ? 1 : 0, "START");
        Dot(g, "Ps", 240, 158, 15, st.Ps, "PS", false);

        // ---- sticks
        Stick(g, "LS", 170, 228, st.LX, st.LY, st.L3, "L3");
        Stick(g, "RS", 310, 228, st.RX, st.RY, st.R3, "R3");

        // ---- motors: what the pad is currently being told to do
        byte large = 0, small = 0;
        if (s?.Pad is { } pad)
        {
            large = _cfg.SwapMotors ? pad.LastWeak : pad.LastStrong;
            small = _cfg.SwapMotors ? pad.LastStrong : pad.LastWeak;
        }
        Motor(g, "MotorL", 100, 286, 22, large, "Large motor");
        Motor(g, "MotorS", 380, 286, 15, small, "Small motor");

        if (!live)
        {
            using var br = new SolidBrush(Color.FromArgb(150, 80, 80, 80));
            g.DrawString("No pad connected in this slot", _font, br, new RectangleF(0, 0, W, 4), new StringFormat());
            using var b2 = new SolidBrush(Color.FromArgb(110, 70, 70, 70));
            g.DrawString("waiting for a pad...", _font, b2, new RectangleF(120, 255, 240, 20), _center);
        }
    }

    // ------------------------------------------------------------ elements

    private void Label(Graphics g, string text, RectangleF r, double heat, Font? font = null)
    {
        if (text.Length == 0) return;
        using var br = new SolidBrush(heat > 0.55 ? Color.White : Color.FromArgb(60, 60, 60));
        g.DrawString(text, font ?? _font, br, r, _center);
    }

    private void Key(Graphics g, string key, RectangleF r, float radius, double active, string label)
    {
        double h = Heat(key, active);
        using var p = Round(r, radius);
        using var br = new SolidBrush(Mix(h));
        g.FillPath(br, p);
        g.DrawPath(_outline, p);
        Label(g, label, r, h, _small);
    }

    private void Dot(Graphics g, string key, float cx, float cy, float r, bool pressed, string label, bool symbol)
    {
        double h = Heat(key, pressed ? 1 : 0);
        var rect = new RectangleF(cx - r, cy - r, r * 2, r * 2);
        using var br = new SolidBrush(Mix(h));
        g.FillEllipse(br, rect);
        g.DrawEllipse(_outline, rect);
        Label(g, label, rect, h, symbol ? _symFont : _font);
    }

    private void Stick(Graphics g, string key, float cx, float cy, byte rx, byte ry, bool click, string label)
    {
        const float ring = 32f, knob = 16f;
        double nx = (rx - 127.5) / 127.5, ny = (ry - 127.5) / 127.5;
        double mag = Math.Sqrt(nx * nx + ny * ny);
        bool moved = mag > 0.2;
        double h = Heat(key, (click || moved) ? 1 : 0);

        var ringRect = new RectangleF(cx - ring, cy - ring, ring * 2, ring * 2);
        using (var rb = new SolidBrush(Color.FromArgb(214, 214, 214))) g.FillEllipse(rb, ringRect);
        g.DrawEllipse(_outline, ringRect);

        float kx = cx + (float)(nx * (ring - knob));
        float ky = cy + (float)(ny * (ring - knob));
        var knobRect = new RectangleF(kx - knob, ky - knob, knob * 2, knob * 2);
        using (var kb = new SolidBrush(Mix(h))) g.FillEllipse(kb, knobRect);
        g.DrawEllipse(_outline, knobRect);
        Label(g, label, knobRect, h, _small);
    }

    private void Motor(Graphics g, string key, float cx, float cy, float r, byte value, string name)
    {
        double level = value / 255.0;
        double h = Heat(key, level);

        // shake the icon while it is really running
        float shake = level > 0.02 ? (float)(Math.Sin(Environment.TickCount / 18.0) * 2.5 * level) : 0f;
        var rect = new RectangleF(cx - r + shake, cy - r, r * 2, r * 2);

        using (var br = new SolidBrush(Mix(h))) g.FillEllipse(br, rect);
        g.DrawEllipse(_outline, rect);
        // off-centre weight, so it reads as a motor
        using (var wb = new SolidBrush(Color.FromArgb(h > 0.55 ? 255 : 120, h > 0.55 ? Color.White : Color.Gray)))
            g.FillEllipse(wb, cx + shake - r * 0.15f, cy - r * 0.55f, r * 0.7f, r * 0.7f);

        if (h > 0.03)
        {
            using var pen = new Pen(Color.FromArgb((int)(255 * Math.Clamp(h, 0, 1)), Red), 2.5f);
            for (int i = 1; i <= 2; i++)
            {
                var o = RectangleF.Inflate(rect, 6 * i, 6 * i);
                g.DrawArc(pen, o, -35, 70);
                g.DrawArc(pen, o, 145, 70);
            }
        }
        Label(g, $"{name} {value}", new RectangleF(cx - 70, cy + r + 4, 140, 16), 0, _small);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Stop(); _timer.Dispose();
            _font.Dispose(); _symFont.Dispose(); _small.Dispose();
            _outline.Dispose(); _center.Dispose();
        }
        base.Dispose(disposing);
    }
}
#endif
