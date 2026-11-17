using HidSharp;

namespace MaxiDodPS3;

public sealed record KnownPad(int Vid, int Pid, PadKind Kind, string Name);

public static class Devices
{
    public static readonly KnownPad[] Builtin =
    {
        new KnownPad(0x054C, 0x0268, PadKind.Ps3, "DualShock 3 / Sixaxis"),
        new KnownPad(0x054C, 0x05C4, PadKind.Ds4, "DualShock 4 (v1)"),
        new KnownPad(0x054C, 0x09CC, PadKind.Ds4, "DualShock 4 (v2)"),
        new KnownPad(0x054C, 0x0CE6, PadKind.DualSense, "DualSense"),
        new KnownPad(0x054C, 0x0DF2, PadKind.DualSense, "DualSense Edge"),
    };

    /// <summary>
    /// Every real HID pad that matches a known (or configured clone) ID. ViGEm's own virtual pads are skipped,
    /// otherwise a virtual DualShock 4 made in "ds4" mode would be read as a source pad and fed back in.
    /// </summary>
    public static IEnumerable<(HidDevice Device, KnownPad Info)> Enumerate(Config cfg)
    {
        var table = cfg.AllKnown();
        foreach (var d in DeviceList.Local.GetHidDevices())
        {
            var match = table.FirstOrDefault(k => k.Vid == d.VendorID && k.Pid == d.ProductID);
            if (match == null) continue;
            if (VirtualDeviceFilter.IsVirtual(d.DevicePath)) continue;
            yield return (d, match);
        }
    }
}
