using System.Runtime.InteropServices;

namespace MaxiDodPS3;

internal static class Native
{
    [DllImport("kernel32.dll")] private static extern bool AttachConsole(int pid);
    [DllImport("kernel32.dll")] private static extern bool AllocConsole();

    /// <summary>The app is a Windows (tray) program, so command-line tools borrow the parent console.</summary>
    public static void EnsureConsole()
    {
        if (!AttachConsole(-1)) AllocConsole();
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XINPUT_VIBRATION { public ushort wLeftMotorSpeed; public ushort wRightMotorSpeed; }

    [DllImport("xinput1_4.dll", EntryPoint = "XInputSetState")]
    public static extern int XInputSetState(int userIndex, ref XINPUT_VIBRATION vibration);
}
