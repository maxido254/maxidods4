using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.DualShock4;
using Nefarius.ViGEm.Client.Targets.Xbox360;

namespace MaxiDodPS3;

internal static class Stick
{
    /// <summary>Raw 0-255 (128 = centre) to -1..1 with a deadzone, rescaled so travel stays full range.</summary>
    public static double Norm(byte raw, double deadzone)
    {
        double v = (raw - 127.5) / 127.5;
        double a = Math.Abs(v);
        if (a <= deadzone) return 0;
        return Math.Sign(v) * (a - deadzone) / (1.0 - deadzone);
    }

    public static short ToShort(double v) => (short)Math.Clamp((int)Math.Round(v * 32767.0), -32768, 32767);

    public static byte ToByte(double v) => (byte)Math.Clamp((int)Math.Round((v + 1.0) * 127.5), 0, 255);
}

public interface IVirtualPad : IDisposable
{
    /// <summary>Raised when a game sets rumble: (largeMotor, smallMotor), each 0-255.</summary>
    event Action<byte, byte>? Rumble;
    void Update(in PadState s);

    /// <summary>XInput user index (0-3) of this virtual pad, or -1 when it is not an XInput pad.</summary>
    int XInputIndex { get; }
}

public sealed class X360Pad : IVirtualPad
{
    private readonly IXbox360Controller _pad;
    private readonly double _dz;
    public event Action<byte, byte>? Rumble;
    public int XInputIndex { get { try { return (int)_pad.UserIndex; } catch { return -1; } } }

    public X360Pad(ViGEmClient client, Config cfg)
    {
        _dz = cfg.StickDeadzone;
        _pad = client.CreateXbox360Controller();
        _pad.AutoSubmitReport = false;
        _pad.FeedbackReceived += (_, e) => Rumble?.Invoke(e.LargeMotor, e.SmallMotor);
        _pad.Connect();
    }

    public void Update(in PadState s)
    {
        _pad.SetButtonState(Xbox360Button.A, s.Cross);
        _pad.SetButtonState(Xbox360Button.B, s.Circle);
        _pad.SetButtonState(Xbox360Button.X, s.Square);
        _pad.SetButtonState(Xbox360Button.Y, s.Triangle);
        _pad.SetButtonState(Xbox360Button.LeftShoulder, s.L1);
        _pad.SetButtonState(Xbox360Button.RightShoulder, s.R1);
        _pad.SetButtonState(Xbox360Button.LeftThumb, s.L3);
        _pad.SetButtonState(Xbox360Button.RightThumb, s.R3);
        _pad.SetButtonState(Xbox360Button.Back, s.Select);
        _pad.SetButtonState(Xbox360Button.Start, s.Start);
        _pad.SetButtonState(Xbox360Button.Guide, s.Ps);
        _pad.SetButtonState(Xbox360Button.Up, s.Up);
        _pad.SetButtonState(Xbox360Button.Down, s.Down);
        _pad.SetButtonState(Xbox360Button.Left, s.Left);
        _pad.SetButtonState(Xbox360Button.Right, s.Right);

        // PS3 stick Y grows downward; XInput Y grows upward, so invert.
        _pad.SetAxisValue(Xbox360Axis.LeftThumbX, Stick.ToShort(Stick.Norm(s.LX, _dz)));
        _pad.SetAxisValue(Xbox360Axis.LeftThumbY, Stick.ToShort(-Stick.Norm(s.LY, _dz)));
        _pad.SetAxisValue(Xbox360Axis.RightThumbX, Stick.ToShort(Stick.Norm(s.RX, _dz)));
        _pad.SetAxisValue(Xbox360Axis.RightThumbY, Stick.ToShort(-Stick.Norm(s.RY, _dz)));
        _pad.SetSliderValue(Xbox360Slider.LeftTrigger, s.L2);
        _pad.SetSliderValue(Xbox360Slider.RightTrigger, s.R2);
        _pad.SubmitReport();
    }

    public void Dispose()
    {
        try { _pad.Disconnect(); } catch { }
    }
}

public sealed class Ds4Pad : IVirtualPad
{
    private readonly IDualShock4Controller _pad;
    private readonly double _dz;
    private readonly byte _trigClick;
    public event Action<byte, byte>? Rumble;
    public int XInputIndex => -1;

    public Ds4Pad(ViGEmClient client, Config cfg)
    {
        _dz = cfg.StickDeadzone;
        _trigClick = cfg.TriggerClickThreshold;
        _pad = client.CreateDualShock4Controller();
        _pad.AutoSubmitReport = false;
        _pad.FeedbackReceived += (_, e) => Rumble?.Invoke(e.LargeMotor, e.SmallMotor);
        _pad.Connect();
    }

    private static DualShock4DPadDirection Dir(in PadState s)
    {
        if (s.Up && s.Right) return DualShock4DPadDirection.Northeast;
        if (s.Down && s.Right) return DualShock4DPadDirection.Southeast;
        if (s.Down && s.Left) return DualShock4DPadDirection.Southwest;
        if (s.Up && s.Left) return DualShock4DPadDirection.Northwest;
        if (s.Up) return DualShock4DPadDirection.North;
        if (s.Right) return DualShock4DPadDirection.East;
        if (s.Down) return DualShock4DPadDirection.South;
        if (s.Left) return DualShock4DPadDirection.West;
        return DualShock4DPadDirection.None;
    }

    public void Update(in PadState s)
    {
        _pad.SetButtonState(DualShock4Button.Cross, s.Cross);
        _pad.SetButtonState(DualShock4Button.Circle, s.Circle);
        _pad.SetButtonState(DualShock4Button.Square, s.Square);
        _pad.SetButtonState(DualShock4Button.Triangle, s.Triangle);
        _pad.SetButtonState(DualShock4Button.ShoulderLeft, s.L1);
        _pad.SetButtonState(DualShock4Button.ShoulderRight, s.R1);
        _pad.SetButtonState(DualShock4Button.ThumbLeft, s.L3);
        _pad.SetButtonState(DualShock4Button.ThumbRight, s.R3);
        _pad.SetButtonState(DualShock4Button.Share, s.Select);
        _pad.SetButtonState(DualShock4Button.Options, s.Start);
        _pad.SetButtonState(DualShock4Button.TriggerLeft, s.L2 >= _trigClick);
        _pad.SetButtonState(DualShock4Button.TriggerRight, s.R2 >= _trigClick);
        _pad.SetButtonState(DualShock4SpecialButton.Ps, s.Ps);
        _pad.SetButtonState(DualShock4SpecialButton.Touchpad, s.Touchpad);
        _pad.SetDPadDirection(Dir(s));

        // DS4 and DS3 share the same axis convention (0 = up), so no inversion here.
        _pad.SetAxisValue(DualShock4Axis.LeftThumbX, Stick.ToByte(Stick.Norm(s.LX, _dz)));
        _pad.SetAxisValue(DualShock4Axis.LeftThumbY, Stick.ToByte(Stick.Norm(s.LY, _dz)));
        _pad.SetAxisValue(DualShock4Axis.RightThumbX, Stick.ToByte(Stick.Norm(s.RX, _dz)));
        _pad.SetAxisValue(DualShock4Axis.RightThumbY, Stick.ToByte(Stick.Norm(s.RY, _dz)));
        _pad.SetSliderValue(DualShock4Slider.LeftTrigger, s.L2);
        _pad.SetSliderValue(DualShock4Slider.RightTrigger, s.R2);
        _pad.SubmitReport();
    }

    public void Dispose()
    {
        try { _pad.Disconnect(); } catch { }
    }
}
