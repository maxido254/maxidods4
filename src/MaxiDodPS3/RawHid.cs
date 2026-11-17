using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace MaxiDodPS3;

/// <summary>
/// Sends an output report with HidD_SetOutputReport (the control-channel path).
/// Some clone pads and wireless drivers accept this when a plain WriteFile does nothing.
/// Uses only the documented hid.dll API; touches no drivers.
/// </summary>
internal sealed class RawHidWriter : IDisposable
{
    private const uint GENERIC_READ = 0x80000000;
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint FILE_SHARE_READ = 1;
    private const uint FILE_SHARE_WRITE = 2;
    private const uint OPEN_EXISTING = 3;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security,
        uint disposition, uint flags, IntPtr template);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_SetOutputReport(SafeFileHandle handle, byte[] buffer, uint length);

    private readonly SafeFileHandle _h;

    private RawHidWriter(SafeFileHandle h) { _h = h; }

    public static RawHidWriter? TryOpen(string devicePath)
    {
        try
        {
            var h = CreateFile(devicePath, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (h.IsInvalid) { h.Dispose(); return null; }
            return new RawHidWriter(h);
        }
        catch { return null; }
    }

    public bool SetOutputReport(byte[] report)
    {
        try { return HidD_SetOutputReport(_h, report, (uint)report.Length); }
        catch { return false; }
    }

    public void Dispose() => _h.Dispose();
}
