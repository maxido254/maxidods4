using System.Diagnostics;
using HidSharp;

namespace MaxiDodPS3;

/// <summary>
/// Hides the real pad from games while its virtual pad is active, using the separate HidHide driver
/// (https://github.com/nefarius/HidHide) through its command-line tool. This is what stops a game from
/// seeing both the physical controller and the virtual one (the duplicate-pad problem).
///
/// MaxiDodPS3 only ever calls HidHide's CLI. It installs and changes no driver. Everything it hides is
/// recorded in a state file so "MaxiDodPS3 unhide" can undo it even after a crash.
/// </summary>
public sealed class HidHideManager
{
    private readonly string? _cli;
    private readonly object _lock = new();
    private readonly HashSet<string> _hidden = new(StringComparer.OrdinalIgnoreCase);
    private bool _prepared;
    private bool _prepareFailed;

    public bool Available => _cli != null;
    public string? CliPath => _cli;

    public static string StatePath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MaxiDodPS3", "hidden-devices.txt");

    public HidHideManager(Config cfg)
    {
        _cli = FindCli(cfg.HidHideCliPath);
    }

    public static string? FindCli(string? configured)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(configured)) candidates.Add(configured);

        foreach (var pf in new[] { Environment.GetEnvironmentVariable("ProgramFiles"), Environment.GetEnvironmentVariable("ProgramW6432") })
        {
            if (string.IsNullOrEmpty(pf)) continue;
            string root = System.IO.Path.Combine(pf, "Nefarius Software Solutions", "HidHide");
            candidates.Add(System.IO.Path.Combine(root, "x64", "HidHideCLI.exe"));
            candidates.Add(System.IO.Path.Combine(root, "HidHideCLI.exe"));
        }
        return candidates.FirstOrDefault(File.Exists);
    }

    /// <summary>Converts a HID interface path (\\?\hid#vid_054c&amp;pid_09cc#7&amp;...#{guid}) to a device instance ID (HID\VID_054C&amp;PID_09CC\7&amp;...).</summary>
    public static string ToInstanceId(string devicePath)
    {
        string p = devicePath;
        if (p.StartsWith(@"\\?\", StringComparison.Ordinal)) p = p.Substring(4);
        int g = p.LastIndexOf("#{", StringComparison.Ordinal);
        if (g > 0) p = p.Substring(0, g);
        return p.Replace('#', '\\').ToUpperInvariant();
    }

    private (int Code, string Output) Run(params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo(_cli!)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var a in args) psi.ArgumentList.Add(a);

            using var p = Process.Start(psi);
            if (p is null) return (-1, "could not start HidHideCLI");

            string output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            if (!p.WaitForExit(5000))
            {
                try { p.Kill(); } catch { }
                return (-1, "HidHideCLI timed out");
            }
            return (p.ExitCode, output);
        }
        catch (Exception ex)
        {
            return (-1, ex.Message);
        }
    }

    // Caller holds _lock. Whitelists this program (so it can still open hidden pads) and switches hiding on.
    private bool Prepare()
    {
        if (_prepared) return true;
        if (_prepareFailed || _cli == null) return false;

        string? exe = Environment.ProcessPath;
        if (exe != null)
        {
            var r = Run("--app-reg", exe);
            if (r.Code != 0) return Fail($"could not whitelist this program ({r.Output.Trim()})");
        }

        var c = Run("--cloak-on");
        if (c.Code != 0) return Fail($"could not switch hiding on ({c.Output.Trim()})");

        _prepared = true;
        return true;
    }

    private bool Fail(string why)
    {
        _prepareFailed = true;
        Log.Warn($"[hide] HidHide setup failed: {why}. Try running as administrator. Games may see duplicate pads.");
        return false;
    }

    public bool Hide(HidDevice dev)
    {
        if (_cli == null) return false;
        string id = ToInstanceId(dev.DevicePath);
        lock (_lock)
        {
            if (_hidden.Contains(id)) return true;
            if (!Prepare()) return false;

            var r = Run("--dev-hide", id);
            if (r.Code != 0)
            {
                Log.Warn($"[hide] HidHide would not hide the pad ({r.Output.Trim()}). Games may see duplicates.");
                return false;
            }
            _hidden.Add(id);
            SaveState();
            return true;
        }
    }

    public void Unhide(HidDevice dev)
    {
        if (_cli == null) return;
        string id = ToInstanceId(dev.DevicePath);
        lock (_lock)
        {
            if (!_hidden.Remove(id)) return;
            Run("--dev-unhide", id);
            SaveState();
        }
    }

    public void UnhideAll()
    {
        if (_cli == null) return;
        lock (_lock)
        {
            foreach (var id in _hidden.ToList()) Run("--dev-unhide", id);
            _hidden.Clear();
            SaveState();
        }
    }

    /// <summary>Undoes anything a previous run left hidden (for example after a crash). Returns how many pads were unhidden.</summary>
    public int UnhideRecorded()
    {
        if (_cli == null || !File.Exists(StatePath)) return 0;
        int n = 0;
        foreach (var id in File.ReadAllLines(StatePath).Select(l => l.Trim()).Where(l => l.Length > 0))
        {
            if (Run("--dev-unhide", id).Code == 0) n++;
        }
        try { File.Delete(StatePath); } catch { }
        return n;
    }

    // Caller holds _lock.
    private void SaveState()
    {
        try
        {
            string dir = System.IO.Path.GetDirectoryName(StatePath)!;
            Directory.CreateDirectory(dir);
            if (_hidden.Count == 0) { if (File.Exists(StatePath)) File.Delete(StatePath); }
            else File.WriteAllLines(StatePath, _hidden);
        }
        catch { /* state file is a convenience; hiding still works without it */ }
    }
}
