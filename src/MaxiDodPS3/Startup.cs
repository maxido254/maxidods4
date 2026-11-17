using System.Diagnostics;
using System.Security.Principal;
using Microsoft.Win32;

namespace MaxiDodPS3;

/// <summary>
/// "Start with Windows", like WhatsApp: launches hidden in the tray at sign-in.
/// Elevated: a scheduled task that runs with highest privileges (HidHide needs admin, and a plain Run entry
/// would be blocked by UAC at boot). Not elevated: the per-user Run registry key.
/// </summary>
public static class Startup
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Name = "MaxiDodPS3";
    private const string TaskName = "MaxiDodPS3";

    public static string ExePath => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "MaxiDodPS3.exe");

    public static bool IsAdmin()
    {
        try { using var id = WindowsIdentity.GetCurrent(); return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator); }
        catch { return false; }
    }

    public static bool IsEnabled()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey);
            if (k?.GetValue(Name) != null) return true;
        }
        catch { }
        return TaskExists();
    }

    public static bool Enable(out string message)
    {
        try
        {
            string exe = ExePath;
            if (IsAdmin())
            {
                RemoveRunKey();
                var (code, output) = Schtasks($"/Create /TN \"{TaskName}\" /TR \"\\\"{exe}\\\" --background\" /SC ONLOGON /RL HIGHEST /F");
                if (code == 0) { message = "Will start with Windows (as administrator)."; return true; }
                Log.Warn($"[startup] scheduled task failed ({output.Trim()}); falling back to the Run key.");
            }
            using var k = Registry.CurrentUser.CreateSubKey(RunKey);
            k.SetValue(Name, $"\"{exe}\" --background");
            message = IsAdmin()
                ? "Will start with Windows."
                : "Will start with Windows. (Run once as administrator and toggle this again if HidHide needs admin rights at boot.)";
            return true;
        }
        catch (Exception ex) { message = $"Could not set startup: {ex.Message}"; Log.Error(message); return false; }
    }

    public static void Disable()
    {
        try { RemoveRunKey(); } catch (Exception ex) { Log.Warn($"[startup] {ex.Message}"); }
        if (TaskExists()) Schtasks($"/Delete /TN \"{TaskName}\" /F");
    }

    private static void RemoveRunKey()
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        k?.DeleteValue(Name, throwOnMissingValue: false);
    }

    private static bool TaskExists() => Schtasks($"/Query /TN \"{TaskName}\"").Code == 0;

    private static (int Code, string Output) Schtasks(string args)
    {
        try
        {
            var psi = new ProcessStartInfo("schtasks.exe", args)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
            };
            using var p = Process.Start(psi)!;
            string o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit(10000);
            return (p.ExitCode, o);
        }
        catch (Exception ex) { return (-1, ex.Message); }
    }
}
