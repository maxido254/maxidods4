using System.Runtime.InteropServices;

namespace MaxiDodPS3;

/// <summary>
/// Read-only lookup of the PC's Bluetooth radio address via the documented Win32 API (bthprops.cpl).
/// This only reads information; it never installs or modifies any Bluetooth driver.
/// </summary>
public static class HostRadio
{
    [StructLayout(LayoutKind.Sequential)]
    private struct BLUETOOTH_FIND_RADIO_PARAMS
    {
        public uint dwSize;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct BLUETOOTH_RADIO_INFO
    {
        public uint dwSize;
        public ulong address;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 248)]
        public string szName;
        public uint ulClassofDevice;
        public ushort lmpSubversion;
        public ushort manufacturer;
    }

    [DllImport("bthprops.cpl", SetLastError = true)]
    private static extern IntPtr BluetoothFindFirstRadio(ref BLUETOOTH_FIND_RADIO_PARAMS pbtfrp, out IntPtr phRadio);

    [DllImport("bthprops.cpl", SetLastError = true)]
    private static extern bool BluetoothFindRadioClose(IntPtr hFind);

    [DllImport("bthprops.cpl", SetLastError = true)]
    private static extern uint BluetoothGetRadioInfo(IntPtr hRadio, ref BLUETOOTH_RADIO_INFO pRadioInfo);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    /// <summary>Returns the first radio's 6-byte address in display order (most significant first).</summary>
    public static bool TryGetAddress(out byte[] address, out string name)
    {
        address = Array.Empty<byte>();
        name = "";
        try
        {
            var p = new BLUETOOTH_FIND_RADIO_PARAMS { dwSize = (uint)Marshal.SizeOf<BLUETOOTH_FIND_RADIO_PARAMS>() };
            IntPtr hFind = BluetoothFindFirstRadio(ref p, out IntPtr hRadio);
            if (hFind == IntPtr.Zero) return false;

            try
            {
                var info = new BLUETOOTH_RADIO_INFO { dwSize = (uint)Marshal.SizeOf<BLUETOOTH_RADIO_INFO>(), szName = "" };
                uint rc = BluetoothGetRadioInfo(hRadio, ref info);
                if (rc != 0) return false;

                address = new byte[6];
                for (int i = 0; i < 6; i++)
                    address[i] = (byte)((info.address >> (8 * (5 - i))) & 0xFF);
                name = info.szName ?? "";
                return true;
            }
            finally
            {
                CloseHandle(hRadio);
                BluetoothFindRadioClose(hFind);
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    public static string Format(byte[] addr) => string.Join(":", addr.Select(b => b.ToString("X2")));

    public static bool TryParse(string text, out byte[] addr)
    {
        addr = new byte[6];
        var parts = text.Split(':', '-');
        if (parts.Length != 6) return false;
        for (int i = 0; i < 6; i++)
            if (!byte.TryParse(parts[i], System.Globalization.NumberStyles.HexNumber, null, out addr[i])) return false;
        return true;
    }
}
