using HidSharp;

namespace MaxiDodPS3;

/// <summary>Everything that differs between DS3, DS4 and DualSense: how to wake them up, read them and send rumble.</summary>
public interface IPadCodec
{
    PadKind Kind { get; }

    /// <summary>Called once after opening (enables a USB DS3, or switches a Bluetooth DS4/DualSense to full reports).</summary>
    void Prepare(HidStream stream, HidDevice dev, bool bluetooth);

    bool TryParse(ReadOnlySpan<byte> report, bool bluetooth, out PadState state);

    /// <summary>strong = large/left motor, weak = small/right motor, both 0-255.</summary>
    byte[] BuildOutput(byte strong, byte weak, int slot, bool bluetooth, int outLen, Config cfg);
}

public static class Codecs
{
    public static IPadCodec Create(PadKind kind)
    {
        switch (kind)
        {
            case PadKind.Ps3: return new Ps3Codec();
            case PadKind.Ds4: return new Ds4Codec();
            case PadKind.DualSense: return new Ds5Codec();
            default: throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }
}

public sealed class Ps3Codec : IPadCodec
{
    public PadKind Kind => PadKind.Ps3;

    public void Prepare(HidStream stream, HidDevice dev, bool bluetooth)
    {
        // Makes a USB pad start streaming. Rejection is not fatal: wireless paths often don't need it.
        HidUtil.TrySetFeature(stream, dev, new byte[] { 0xF4, 0x42, 0x03, 0x00, 0x00 });
    }

    public bool TryParse(ReadOnlySpan<byte> report, bool bluetooth, out PadState state) =>
        Ps3Report.TryParse(report, out state);

    public byte[] BuildOutput(byte strong, byte weak, int slot, bool bluetooth, int outLen, Config cfg)
    {
        // The PS3 weak motor is on/off only.
        bool weakOn = weak > 0 && weak >= cfg.SmallMotorThreshold;
        return Ps3Report.BuildOutput(strong, weakOn, slot, cfg.RumbleDuration, cfg.LedInReport, outLen);
    }
}

public sealed class Ds4Codec : IPadCodec
{
    public PadKind Kind => PadKind.Ds4;

    public void Prepare(HidStream stream, HidDevice dev, bool bluetooth)
    {
        if (bluetooth) HidUtil.TryGetFeature(stream, dev, 0x02, 37);
    }

    public bool TryParse(ReadOnlySpan<byte> report, bool bluetooth, out PadState state) =>
        Ds4Report.TryParse(report, out state);

    public byte[] BuildOutput(byte strong, byte weak, int slot, bool bluetooth, int outLen, Config cfg) =>
        Ds4Report.BuildOutput(strong, weak, slot, bluetooth, outLen, cfg.Ds4OutputFlags, cfg.Lightbar == "slot");
}

public sealed class Ds5Codec : IPadCodec
{
    private int _seq;

    public PadKind Kind => PadKind.DualSense;

    public void Prepare(HidStream stream, HidDevice dev, bool bluetooth)
    {
        if (bluetooth) HidUtil.TryGetFeature(stream, dev, 0x05, 41);
    }

    public bool TryParse(ReadOnlySpan<byte> report, bool bluetooth, out PadState state) =>
        Ds5Report.TryParse(report, bluetooth, out state);

    public byte[] BuildOutput(byte strong, byte weak, int slot, bool bluetooth, int outLen, Config cfg)
    {
        _seq = (_seq + 1) & 0x0F;
        return Ds5Report.BuildOutput(strong, weak, slot, bluetooth, outLen,
            cfg.Ds5HapticsSelect, cfg.Lightbar == "slot", _seq);
    }
}
