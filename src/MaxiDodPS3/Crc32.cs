namespace MaxiDodPS3;

/// <summary>Standard CRC-32 (the zlib/PNG one). DS4 and DualSense Bluetooth reports use it with a one-byte seed.</summary>
public static class Crc32
{
    private static readonly uint[] Table = BuildTable();

    private static uint[] BuildTable()
    {
        var t = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            t[i] = c;
        }
        return t;
    }

    public static uint Compute(ReadOnlySpan<byte> data) => Compute(null, data);

    /// <summary>CRC-32 of (seed byte followed by data). Sony uses 0xA2 for output reports and 0xA1 for input reports.</summary>
    public static uint Compute(byte? seed, ReadOnlySpan<byte> data)
    {
        uint c = 0xFFFFFFFFu;
        if (seed.HasValue)
            c = Table[(c ^ seed.Value) & 0xFF] ^ (c >> 8);
        foreach (byte b in data)
            c = Table[(c ^ b) & 0xFF] ^ (c >> 8);
        return ~c;
    }
}
