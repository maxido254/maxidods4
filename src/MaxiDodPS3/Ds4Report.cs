namespace MaxiDodPS3;

/// <summary>Player-slot colours for lightbars and LEDs.</summary>
public static class Led
{
    /// <summary>Dim colours: slot 1 blue, 2 red, 3 green, 4 pink. All zero when disabled.</summary>
    public static void SlotColor(int slot, bool enabled, out byte r, out byte g, out byte b)
    {
        r = 0; g = 0; b = 0;
        if (!enabled) return;
        switch (Math.Clamp(slot, 0, 3))
        {
            case 0: b = 0x40; break;
            case 1: r = 0x40; break;
            case 2: g = 0x40; break;
            default: r = 0x30; b = 0x30; break;
        }
    }
}

/// <summary>DualShock 4 HID reports (USB report 0x01/0x05, Bluetooth report 0x11). Pure functions, no I/O.</summary>
public static class Ds4Report
{
    public const int UsbOutputLength = 32;
    public const int BtOutputLength = 78;

    public static bool TryParse(ReadOnlySpan<byte> r, out PadState s)
    {
        s = default;
        // 0x01 is the USB report, and also the "basic" Bluetooth report which has the same field layout.
        if (r.Length >= 10 && r[0] == 0x01) { ParseCommon(r, 1, ref s); return true; }
        // 0x11 is the full Bluetooth report: two extra header bytes.
        if (r.Length >= 14 && r[0] == 0x11) { ParseCommon(r, 3, ref s); return true; }
        return false;
    }

    /// <summary>Parses the layout shared by DS4 USB/Bluetooth and the DualSense basic Bluetooth report. o = index of left-stick X.</summary>
    internal static void ParseCommon(ReadOnlySpan<byte> r, int o, ref PadState s)
    {
        s.LX = r[o]; s.LY = r[o + 1]; s.RX = r[o + 2]; s.RY = r[o + 3];
        byte b0 = r[o + 4], b1 = r[o + 5], b2 = r[o + 6];
        s.L2 = r[o + 7]; s.R2 = r[o + 8];

        ApplyHat(ref s, b0 & 0x0F);
        ApplyFaceAndShoulders(ref s, b0, b1, b2);
    }

    internal static void ApplyHat(ref PadState s, int hat)
    {
        // 0 = up, then clockwise; 8 = released.
        s.Up = hat is 7 or 0 or 1;
        s.Right = hat is 1 or 2 or 3;
        s.Down = hat is 3 or 4 or 5;
        s.Left = hat is 5 or 6 or 7;
    }

    /// <summary>b0 = hat+face buttons, b1 = shoulders/share/options/sticks, b2 = PS/touchpad. Same bit layout on DS4 and DualSense.</summary>
    internal static void ApplyFaceAndShoulders(ref PadState s, byte b0, byte b1, byte b2)
    {
        s.Square = (b0 & 0x10) != 0;
        s.Cross = (b0 & 0x20) != 0;
        s.Circle = (b0 & 0x40) != 0;
        s.Triangle = (b0 & 0x80) != 0;

        s.L1 = (b1 & 0x01) != 0;
        s.R1 = (b1 & 0x02) != 0;
        // 0x04 / 0x08 are the digital L2/R2; the analog values are used instead.
        s.Select = (b1 & 0x10) != 0;
        s.Start = (b1 & 0x20) != 0;
        s.L3 = (b1 & 0x40) != 0;
        s.R3 = (b1 & 0x80) != 0;

        s.Ps = (b2 & 0x01) != 0;
        s.Touchpad = (b2 & 0x02) != 0;
    }

    /// <summary>
    /// Builds a DS4 output report. strong = left/heavy motor, weak = right/light motor.
    /// flags is the "valid data" byte (0x07 = rumble + lightbar + blink). Bluetooth reports carry a CRC-32.
    /// </summary>
    public static byte[] BuildOutput(byte strong, byte weak, int slot, bool bluetooth, int outLen, byte flags, bool lightbar)
    {
        Led.SlotColor(slot, lightbar, out byte r, out byte g, out byte b);

        if (!bluetooth)
        {
            var o = new byte[Math.Max(outLen, UsbOutputLength)];
            o[0] = 0x05;
            o[1] = flags;
            o[4] = weak;
            o[5] = strong;
            o[6] = r; o[7] = g; o[8] = b;
            return o;
        }

        var bo = new byte[Math.Max(outLen, BtOutputLength)];
        bo[0] = 0x11;
        bo[1] = 0xC0;           // HID report + CRC present
        bo[3] = flags;
        bo[6] = weak;
        bo[7] = strong;
        bo[8] = r; bo[9] = g; bo[10] = b;

        uint crc = Crc32.Compute((byte)0xA2, bo.AsSpan(0, 74));
        bo[74] = (byte)crc;
        bo[75] = (byte)(crc >> 8);
        bo[76] = (byte)(crc >> 16);
        bo[77] = (byte)(crc >> 24);
        return bo;
    }
}
