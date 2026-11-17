namespace MaxiDodPS3;

/// <summary>DualSense HID reports (USB report 0x01/0x02, Bluetooth report 0x31). Pure functions, no I/O.</summary>
public static class Ds5Report
{
    public const int UsbOutputLength = 63;
    public const int BtOutputLength = 78;

    public static bool TryParse(ReadOnlySpan<byte> r, bool bluetooth, out PadState s)
    {
        s = default;
        if (r.Length < 10) return false;

        // Full Bluetooth report.
        if (r[0] == 0x31 && r.Length >= 13) { ParseCommon(r, 2, ref s); return true; }

        if (r[0] == 0x01)
        {
            // Over Bluetooth, report 0x01 is the "basic" report, which uses the DS4 field layout.
            if (bluetooth) { Ds4Report.ParseCommon(r, 1, ref s); return true; }
            if (r.Length >= 12) { ParseCommon(r, 1, ref s); return true; }
        }
        return false;
    }

    /// <summary>o = index of left-stick X. USB report 0x01 uses 1, Bluetooth report 0x31 uses 2.</summary>
    private static void ParseCommon(ReadOnlySpan<byte> r, int o, ref PadState s)
    {
        s.LX = r[o]; s.LY = r[o + 1]; s.RX = r[o + 2]; s.RY = r[o + 3];
        s.L2 = r[o + 4]; s.R2 = r[o + 5];
        // r[o + 6] is a sequence counter.
        byte b0 = r[o + 7], b1 = r[o + 8], b2 = r[o + 9];

        Ds4Report.ApplyHat(ref s, b0 & 0x0F);
        Ds4Report.ApplyFaceAndShoulders(ref s, b0, b1, b2);
    }

    private static byte PlayerMask(int slot) => Math.Clamp(slot, 0, 3) switch
    {
        0 => (byte)0x04,
        1 => (byte)0x0A,
        2 => (byte)0x15,
        _ => (byte)0x1B,
    };

    /// <summary>
    /// Builds a DualSense output report. strong = left motor, weak = right motor.
    /// hapticsSelect chooses classic rumble emulation (needed by most firmware). seq is the 4-bit Bluetooth sequence.
    /// </summary>
    public static byte[] BuildOutput(byte strong, byte weak, int slot, bool bluetooth, int outLen,
        bool hapticsSelect, bool lightbar, int seq)
    {
        byte[] o;
        int c; // index where the "common" block starts

        if (bluetooth)
        {
            o = new byte[Math.Max(outLen, BtOutputLength)];
            o[0] = 0x31;
            o[1] = (byte)((seq & 0x0F) << 4);
            o[2] = 0x10;
            c = 3;
        }
        else
        {
            o = new byte[Math.Max(outLen, UsbOutputLength)];
            o[0] = 0x02;
            c = 1;
        }

        o[c] = (byte)(0x01 | (hapticsSelect ? 0x02 : 0x00));   // compatible vibration (+ haptics select)
        o[c + 1] = (byte)(lightbar ? 0x14 : 0x00);              // lightbar + player LED control
        o[c + 2] = weak;
        o[c + 3] = strong;

        if (lightbar)
        {
            Led.SlotColor(slot, true, out byte r, out byte g, out byte bl);
            o[c + 43] = PlayerMask(slot);
            o[c + 44] = r; o[c + 45] = g; o[c + 46] = bl;
        }

        if (bluetooth)
        {
            uint crc = Crc32.Compute((byte)0xA2, o.AsSpan(0, 74));
            o[74] = (byte)crc;
            o[75] = (byte)(crc >> 8);
            o[76] = (byte)(crc >> 16);
            o[77] = (byte)(crc >> 24);
        }
        return o;
    }
}
