namespace MaxiDodPS3;

/// <summary>
/// Pure parsing/building of DS3 HID reports (no I/O), so it is easy to unit test.
/// Offsets include the report ID at index 0, which is how HidSharp returns reports on Windows.
/// </summary>
public static class Ps3Report
{
    public const int InputLength = 49;
    public const int OutputLength = 49;

    public static bool TryParse(ReadOnlySpan<byte> b, out PadState s)
    {
        s = default;
        if (b.Length < 20 || b[0] != 0x01) return false;

        s.Select = (b[2] & 0x01) != 0;
        s.L3 = (b[2] & 0x02) != 0;
        s.R3 = (b[2] & 0x04) != 0;
        s.Start = (b[2] & 0x08) != 0;
        s.Up = (b[2] & 0x10) != 0;
        s.Right = (b[2] & 0x20) != 0;
        s.Down = (b[2] & 0x40) != 0;
        s.Left = (b[2] & 0x80) != 0;

        // b[3] bit0 = L2, bit1 = R2 (digital); analog pressure is read below.
        s.L1 = (b[3] & 0x04) != 0;
        s.R1 = (b[3] & 0x08) != 0;
        s.Triangle = (b[3] & 0x10) != 0;
        s.Circle = (b[3] & 0x20) != 0;
        s.Cross = (b[3] & 0x40) != 0;
        s.Square = (b[3] & 0x80) != 0;

        s.Ps = (b[4] & 0x01) != 0;

        s.LX = b[6]; s.LY = b[7]; s.RX = b[8]; s.RY = b[9];
        s.L2 = b[18]; s.R2 = b[19];
        return true;
    }

    /// <summary>
    /// Builds output report 0x01. Strong motor is variable (0-255); the weak motor is on/off.
    /// ledSlot 0-3 lights LED1-LED4 to show the player number.
    /// </summary>
    public static byte[] BuildOutput(byte strong, bool weakOn, int ledSlot,
        byte duration = 0xFF, bool includeLed = true, int length = OutputLength)
    {
        var o = new byte[OutputLength];
        o[0] = 0x01;

        // [1] weak duration, [2] weak on/off, [3] strong duration, [4] strong force. 0xFF = until changed.
        o[1] = (byte)(weakOn ? duration : 0x00);
        o[2] = (byte)(weakOn ? 0x01 : 0x00);
        o[3] = (byte)(strong > 0 ? duration : 0x00);
        o[4] = strong;

        if (includeLed)
        {
            // [9] LED bitmask: 0x02 = LED1, 0x04 = LED2, 0x08 = LED3, 0x10 = LED4.
            int slot = Math.Clamp(ledSlot, 0, 3);
            o[9] = (byte)(0x02 << slot);

            // Four LED config blocks of 5 bytes at [10..29].
            for (int i = 0; i < 4; i++)
            {
                int p = 10 + 5 * i;
                o[p] = 0xFF; o[p + 1] = 0x27; o[p + 2] = 0x10; o[p + 3] = 0x00; o[p + 4] = 0x32;
            }
        }

        // Windows needs the buffer to match the device's output report length exactly.
        if (length == OutputLength || length < 5) return o;
        var r = new byte[length];
        Array.Copy(o, r, Math.Min(length, OutputLength));
        return r;
    }
}
