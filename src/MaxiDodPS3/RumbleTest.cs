namespace MaxiDodPS3;

/// <summary>
/// Interactive tool for clone pads (DS3, DS4 or DualSense): tries several ways of sending rumble and asks
/// which one you felt, then prints the settings to paste into maxidodps3.json. Run it once over USB and once wirelessly.
/// </summary>
public static class RumbleTest
{
    private sealed record Variant(string Name, Action<Config> Apply);

    public static int Run(Config baseCfg, int padNumber)
    {
        var pads = Devices.Enumerate(baseCfg).ToList();
        if (pads.Count == 0)
        {
            Console.WriteLine("No supported pads found. Connect one (USB or wireless) and try again.");
            return 1;
        }
        if (padNumber < 1 || padNumber > pads.Count)
        {
            Console.WriteLine($"--pad must be between 1 and {pads.Count}.");
            return 1;
        }

        var (dev, info) = pads[padNumber - 1];
        Console.WriteLine($"Testing pad {padNumber} of {pads.Count}: {info.Name} ({HidUtil.Transport(dev.DevicePath)}).");
        Console.WriteLine("Close MaxiDodPS3 'run' first so nothing else is controlling the pad.");
        Console.WriteLine("Hold the pad and feel for vibration. Each method runs both motors for 2 seconds.\n");

        var variants = new List<Variant>
        {
            new("A: standard, refresh every 100 ms", c => { }),
            new("B: single write, no refresh", c => c.RumbleMode = "latch"),
            new("C: motors swapped", c => c.SwapMotors = true),
            new("D: send with HidD_SetOutputReport", c => c.WriteMethod = "setoutput"),
            new("E: motors swapped + HidD_SetOutputReport", c => { c.SwapMotors = true; c.WriteMethod = "setoutput"; }),
        };

        switch (info.Kind)
        {
            case PadKind.Ps3:
                variants.Add(new("F: shorter motor duration (50)", c => c.RumbleDuration = 50));
                variants.Add(new("G: no LED data in the rumble report", c => c.LedInReport = false));
                variants.Add(new("H: shorter duration + HidD_SetOutputReport", c => { c.RumbleDuration = 50; c.WriteMethod = "setoutput"; }));
                variants.Add(new("I: swapped + no LED data + HidD_SetOutputReport", c => { c.SwapMotors = true; c.LedInReport = false; c.WriteMethod = "setoutput"; }));
                break;

            case PadKind.Ds4:
                variants.Add(new("F: DS4 flags 255 instead of 7", c => c.Ds4OutputFlags = 0xFF));
                variants.Add(new("G: DS4 flags 255 + HidD_SetOutputReport", c => { c.Ds4OutputFlags = 0xFF; c.WriteMethod = "setoutput"; }));
                variants.Add(new("H: DS4 flags 255 + motors swapped", c => { c.Ds4OutputFlags = 0xFF; c.SwapMotors = true; }));
                break;

            case PadKind.DualSense:
                variants.Add(new("F: without the haptics-select bit", c => c.Ds5HapticsSelect = false));
                variants.Add(new("G: without haptics-select + HidD_SetOutputReport", c => { c.Ds5HapticsSelect = false; c.WriteMethod = "setoutput"; }));
                variants.Add(new("H: without haptics-select + motors swapped", c => { c.Ds5HapticsSelect = false; c.SwapMotors = true; }));
                break;
        }

        foreach (var v in variants)
        {
            // Start from defaults so every method is tested on its own, not on top of your current settings.
            var c = new Config { ExtraDevices = baseCfg.ExtraDevices.ToList() };
            v.Apply(c);
            c.Normalize();

            using var pad = HidPad.TryOpen(dev, info, c);
            if (pad is null)
            {
                Console.WriteLine("Could not open the pad. Is another program using it?");
                return 1;
            }

            Console.WriteLine($"Method {v.Name}");
            Console.WriteLine("  Vibrating for 2 seconds...");
            pad.SetRumble(255, 255);
            Thread.Sleep(2000);
            pad.SetRumble(0, 0);

            Console.Write("  Did you feel it? [y = yes, n = no, q = quit] ");
            string answer = (Console.ReadLine() ?? "q").Trim().ToLowerInvariant();
            if (answer == "q") return 0;
            if (answer == "y")
            {
                Console.WriteLine("\nGood. Put these in maxidodps3.json (next to the exe), replacing any existing values:\n");
                Console.WriteLine($"  \"RumbleMode\": \"{c.RumbleMode}\",");
                Console.WriteLine($"  \"RumbleRefreshMs\": {c.RumbleRefreshMs},");
                Console.WriteLine($"  \"SwapMotors\": {c.SwapMotors.ToString().ToLowerInvariant()},");
                Console.WriteLine($"  \"WriteMethod\": \"{c.WriteMethod}\",");
                switch (info.Kind)
                {
                    case PadKind.Ps3:
                        Console.WriteLine($"  \"RumbleDuration\": {c.RumbleDuration},");
                        Console.WriteLine($"  \"LedInReport\": {c.LedInReport.ToString().ToLowerInvariant()}");
                        break;
                    case PadKind.Ds4:
                        Console.WriteLine($"  \"Ds4OutputFlags\": {c.Ds4OutputFlags}");
                        break;
                    default:
                        Console.WriteLine($"  \"Ds5HapticsSelect\": {c.Ds5HapticsSelect.ToString().ToLowerInvariant()}");
                        break;
                }
                Console.WriteLine("\nIf the pad needs different settings over USB and wirelessly, run the test again on the other connection and compare.");
                return 0;
            }
            Console.WriteLine();
        }

        Console.WriteLine("None of the methods vibrated this pad.");
        Console.WriteLine("- Run this over USB too. If USB works but wireless does not, the wireless driver is not forwarding output reports to the pad.");
        Console.WriteLine("- Run 'MaxiDodPS3 doctor' to see which driver stack is in use.");
        Console.WriteLine("- If the pad rumbles wirelessly on another device, the pad is fine and the limit is the Windows driver path.");
        return 1;
    }
}
