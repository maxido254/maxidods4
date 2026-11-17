using Microsoft.Win32;

namespace MaxiDodPS3;

/// <summary>
/// Read-only environment check. It never installs, removes or modifies any driver,
/// which is how MaxiDodPS3 stays clear of your Bluetooth stack.
/// </summary>
public static class Doctor
{
    private static bool ServiceExists(string name)
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + name);
            return k != null;
        }
        catch { return false; }
    }

    private static void Line(string status, string text) => Console.WriteLine($"  [{status,-7}] {text}");

    public static int Run(Config cfg)
    {
        Console.WriteLine("MaxiDodPS3 doctor (read-only, changes nothing)\n");

        if (ServiceExists("ViGEmBus")) Line("OK", "ViGEmBus driver installed (needed for virtual X360/DS4 pads).");
        else Line("MISSING", "ViGEmBus not found. Install it from https://github.com/nefarius/ViGEmBus/releases");

        if (HostRadio.TryGetAddress(out var addr, out var name))
            Line("OK", $"Bluetooth radio: {HostRadio.Format(addr)} {(name.Length > 0 ? $"({name})" : "")}");
        else
            Line("INFO", "No Bluetooth radio found. USB mode still works.");

        if (ServiceExists("BthPS3")) Line("OK", "BthPS3 profile driver installed (wireless DS3 supported). DS4/DualSense need no extra driver.");
        else Line("INFO", "BthPS3 not installed. Only needed for wireless DS3: https://github.com/nefarius/BthPS3");

        var hideCli = HidHideManager.FindCli(cfg.HidHideCliPath);
        if (hideCli != null)
            Line("OK", "HidHide found: real pads can be hidden from games, which prevents duplicate controllers.");
        else if (cfg.HideSourcePads)
            Line("WARN", "HidHide not found. Games may see each real pad AND its virtual pad (duplicates). Install https://github.com/nefarius/HidHide or use native mode.");

        if (ServiceExists("dshidmini"))
            Line("WARN", "DsHidMini detected. If it already exposes your DS3 as XInput, set it to a mode that does not, or set Ps3Mode to \"native\", or you will get two pads.");

        Console.WriteLine();
        Console.WriteLine($"  Output per kind:  DS3 -> {cfg.Ps3Mode}   DS4 -> {cfg.Ds4Mode}   DualSense -> {cfg.DualSenseMode}");
        Console.WriteLine();

        var pads = Devices.Enumerate(cfg).ToList();
        if (pads.Count == 0) Line("INFO", "No supported pads visible right now. Plug one in, or connect it wirelessly.");
        foreach (var (dev, info) in pads)
            Line("PAD", $"{info.Name}, {HidUtil.Transport(dev.DevicePath)}, {dev.VendorID:X4}:{dev.ProductID:X4}, output: {cfg.ModeFor(info.Kind)}");

        Console.WriteLine();
        return 0;
    }

    public static int List(Config cfg)
    {
        var pads = Devices.Enumerate(cfg).ToList();
        if (pads.Count == 0) { Console.WriteLine("No supported pads found."); return 0; }
        foreach (var (dev, info) in pads)
            Console.WriteLine($"{info.Name}  {HidUtil.Transport(dev.DevicePath)}  {dev.VendorID:X4}:{dev.ProductID:X4}  {dev.DevicePath}");
        return 0;
    }
}
