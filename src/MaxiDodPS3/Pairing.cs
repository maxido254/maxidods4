using HidSharp;

namespace MaxiDodPS3;

/// <summary>
/// Tells a USB-connected DualShock 3 which Bluetooth host to connect to (feature report 0xF5),
/// the same thing the classic "sixpair" tool does. Unplug the pad afterwards and press the PS button.
/// DS4 and DualSense pads do not need this: pair them in Windows Bluetooth settings.
/// </summary>
public static class Pairing
{
    public static int SetMasterOnAllUsbPads(Config cfg, byte[] host)
    {
        int done = 0;
        foreach (var (dev, info) in Devices.Enumerate(cfg))
        {
            if (info.Kind != PadKind.Ps3) continue;                 // only DS3 uses this
            if (HidUtil.LooksBluetooth(dev.DevicePath)) continue;   // pairing is done over USB only

            if (!dev.TryOpen(out HidStream stream))
            {
                Console.WriteLine("  Could not open a pad (in use by another program?).");
                continue;
            }

            using (stream)
            {
                // Report 0xF5: [id, 0x01, 0x00, host address (display order)].
                var report = new byte[9];
                report[0] = 0xF5; report[1] = 0x01; report[2] = 0x00;
                Array.Copy(host, 0, report, 3, 6);

                if (HidUtil.TrySetFeature(stream, dev, report))
                {
                    Console.WriteLine($"  Paired pad to {HostRadio.Format(host)}.");
                    done++;
                }
                else
                {
                    Console.WriteLine("  The pad rejected the pairing report (clone pads sometimes do).");
                }
            }
        }
        return done;
    }
}
