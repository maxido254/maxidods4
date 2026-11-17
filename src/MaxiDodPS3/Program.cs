using MaxiDodPS3;

const string Help = @"MaxiDodPS3 - use PS3, PS4 and PS5 controllers on Windows as virtual Xbox 360 (XInput) or DualShock 4 pads, with rumble.

Usage:
  MaxiDodPS3 [run] [options]          Start the bridge (default)
  MaxiDodPS3 pair [--address AA:BB:CC:DD:EE:FF]   Point a USB-connected DS3 at this PC's Bluetooth radio
  MaxiDodPS3 doctor                   Read-only check of drivers, pads and settings
  MaxiDodPS3 list                     List visible pads
  MaxiDodPS3 rumble-test [--pad N]    Find a rumble method that works for a clone pad
  MaxiDodPS3 unhide                   Un-hide any pads a previous run left hidden
  MaxiDodPS3 --help

Options for run:
  --background                        Start quietly in the tray (this is what 'Start with Windows' uses)
  --mode x360|ds4|native              Output for every kind of pad
  --ps3-mode / --ds4-mode / --ds5-mode x360|ds4|native    Output for one kind of pad
  --no-hide                           Do not hide real pads from games (may cause duplicates)
  --config file.json                  Use a different settings file

  x360   = virtual Xbox 360 (XInput)
  ds4    = virtual DualShock 4 (not XInput)
  native = leave that kind of pad alone

MaxiDodPS3 never installs or modifies drivers.";

string cmd = args.Length > 0 && !args[0].StartsWith('-') ? args[0].ToLowerInvariant() : "run";

string? Opt(string name)
{
    for (int i = 0; i < args.Length - 1; i++)
        if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
    return null;
}

bool background = args.Any(a => a.Equals("--background", StringComparison.OrdinalIgnoreCase));

// MaxiDodPS3 is a tray (Windows) program; the command-line tools borrow the console they were started from.
if (cmd != "run" || args.Any(a => a is "--help" or "-h" or "/?"))
    Native.EnsureConsole();

if (args.Any(a => a is "--help" or "-h" or "/?") || cmd is "help")
{
    Console.WriteLine(Help);
    return 0;
}

Config cfg;
try
{
    cfg = Config.Load(Opt("--config"));

    string? all = Opt("--mode");
    if (all != null) { cfg.Ps3Mode = all; cfg.Ds4Mode = all; cfg.DualSenseMode = all; }
    string? m;
    if ((m = Opt("--ps3-mode")) != null) cfg.Ps3Mode = m;
    if ((m = Opt("--ds4-mode")) != null) cfg.Ds4Mode = m;
    if ((m = Opt("--ds5-mode")) != null) cfg.DualSenseMode = m;
    if (args.Any(a => a.Equals("--no-hide", StringComparison.OrdinalIgnoreCase))) cfg.HideSourcePads = false;

    cfg.Normalize();
}
catch (Exception ex)
{
    if (cmd == "run")
        System.Windows.Forms.MessageBox.Show($"Config error: {ex.Message}", "MaxiDodPS3",
            System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
    else
        Console.WriteLine($"Config error: {ex.Message}");
    return 1;
}

switch (cmd)
{
    case "doctor":
        return Doctor.Run(cfg);

    case "list":
        return Doctor.List(cfg);

    case "rumble-test":
    {
        int padNumber = int.TryParse(Opt("--pad"), out int pn) ? pn : 1;
        return RumbleTest.Run(cfg, padNumber);
    }

    case "unhide":
    {
        var hide = new HidHideManager(cfg);
        if (!hide.Available)
        {
            Console.WriteLine("HidHide was not found, so nothing was hidden by this program.");
            return 0;
        }
        int n = hide.UnhideRecorded();
        Console.WriteLine(n > 0 ? $"Un-hid {n} pad(s)." : "Nothing to un-hide.");
        return 0;
    }

    case "pair":
    {
        byte[] host;
        string? given = Opt("--address");
        if (given != null)
        {
            if (!HostRadio.TryParse(given, out host))
            {
                Console.WriteLine("Invalid address. Use the form AA:BB:CC:DD:EE:FF.");
                return 1;
            }
        }
        else if (!HostRadio.TryGetAddress(out host, out _))
        {
            Console.WriteLine("No Bluetooth radio found. Pass one with --address AA:BB:CC:DD:EE:FF.");
            return 1;
        }

        Console.WriteLine($"Pairing USB-connected DS3 pads to {HostRadio.Format(host)} ...");
        int n = Pairing.SetMasterOnAllUsbPads(cfg, host);
        Console.WriteLine(n > 0
            ? "Done. Unplug the pad(s) and press the PS button. Wireless DS3 needs the BthPS3 driver installed."
            : "No DS3 pads were paired. Plug a DS3 in over USB first. (DS4 and DualSense pair through Windows Bluetooth settings.)");
        return n > 0 ? 0 : 1;
    }

    case "run":
        break;

    default:
        Console.WriteLine($"Unknown command '{cmd}'.\n");
        Console.WriteLine(Help);
        return 1;
}

// ---- run: tray app ------------------------------------------------------------------------------------

// One copy only. A second launch just asks the running copy to show its window.
using var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, "MaxiDodPS3.Show");
using var single = new Mutex(true, "MaxiDodPS3.SingleInstance", out bool firstInstance);
if (!firstInstance)
{
    showSignal.Set();
    return 0;
}

AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Error($"Unhandled: {e.ExceptionObject}");
Log.Info($"MaxiDodPS3 starting ({(background ? "background" : "manual")}, admin: {Startup.IsAdmin()}).");

int exitCode = 0;
var ui = new Thread(() =>
{
    try
    {
        System.Windows.Forms.Application.EnableVisualStyles();
        System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
        System.Windows.Forms.Application.ThreadException += (_, e) => Log.Error($"UI error: {e.Exception}");

        var bridge = new Bridge(cfg);
        // Started by hand: open the debugger so problems are visible. Started by Windows (--background): silent.
        bool openDebugger = !background || args.Any(a => a.Equals("--debug", StringComparison.OrdinalIgnoreCase));
        System.Windows.Forms.Application.Run(new TrayApp(bridge, openDebugger, showSignal));
    }
    catch (Exception ex)
    {
        Log.Error($"Fatal: {ex}");
        exitCode = 1;
    }
});
ui.SetApartmentState(ApartmentState.STA);
ui.Start();
ui.Join();
return exitCode;
