using System.Globalization;
using System.Text.Json;

namespace MaxiDodPS3;

public sealed class Config
{
    // ---- What each kind of pad becomes -------------------------------------------------------
    // "x360"   = virtual Xbox 360 (XInput)
    // "ds4"    = virtual DualShock 4 (not XInput)
    // "native" = leave this kind alone: nothing virtual is created and nothing is hidden

    public string Ps3Mode { get; set; } = "x360";
    public string Ds4Mode { get; set; } = "x360";
    public string DualSenseMode { get; set; } = "x360";

    // ---- Duplicate prevention ----------------------------------------------------------------

    /// <summary>Hide the real pad from games with HidHide while its virtual pad is active, so games never see both.</summary>
    public bool HideSourcePads { get; set; } = true;

    /// <summary>Full path to HidHideCLI.exe. Leave empty to look in the default install folder.</summary>
    public string HidHideCliPath { get; set; } = "";

    /// <summary>Treat two devices with the same MAC-style serial (one pad on USB and Bluetooth at once) as one pad.</summary>
    public bool DedupeBySerial { get; set; } = true;

    // ---- General -----------------------------------------------------------------------------

    /// <summary>Maximum simultaneous pads (1-4).</summary>
    public int MaxPads { get; set; } = 4;

    /// <summary>Stick deadzone as a fraction of full travel (0.0-0.45).</summary>
    public double StickDeadzone { get; set; } = 0.0;

    /// <summary>L2/R2 analog value (0-255) at which DS4 output reports the digital trigger click.</summary>
    public byte TriggerClickThreshold { get; set; } = 100;

    /// <summary>"slot" shows the player number on the lightbar/LEDs, "off" turns them off.</summary>
    public string Lightbar { get; set; } = "slot";

    // ---- Rumble ------------------------------------------------------------------------------

    /// <summary>DS3 only: the weak motor is on/off and switches on at or above this game value (0-255).</summary>
    public byte SmallMotorThreshold { get; set; } = 40;

    /// <summary>Multiplier applied to the game's large-motor value (0.0-2.0).</summary>
    public double LargeMotorScale { get; set; } = 1.0;

    /// <summary>Multiplier applied to the game's small-motor value (0.0-2.0).</summary>
    public double SmallMotorScale { get; set; } = 1.0;

    /// <summary>"refresh" re-sends the rumble report while a motor is on (helps clones that stop after a short time); "latch" sends once.</summary>
    public string RumbleMode { get; set; } = "refresh";

    /// <summary>Refresh interval in ms when RumbleMode is "refresh" (30-1000).</summary>
    public int RumbleRefreshMs { get; set; } = 100;

    /// <summary>DS3 only: motor duration byte. 255 = until changed; some clones need a smaller value.</summary>
    public byte RumbleDuration { get; set; } = 255;

    /// <summary>Swap which motor the strong/weak game values drive (some clones wire them the other way).</summary>
    public bool SwapMotors { get; set; } = false;

    /// <summary>DS3 only: include the LED configuration bytes in the rumble report. Turn off if a clone misbehaves with them.</summary>
    public bool LedInReport { get; set; } = true;

    /// <summary>"auto" (normal write, then HidD_SetOutputReport if that fails), "stream", or "setoutput".</summary>
    public string WriteMethod { get; set; } = "auto";

    /// <summary>DS4 only: the "valid data" flags byte in the output report, as a decimal number. 7 = rumble+lightbar+blink; some clones want 255.</summary>
    public byte Ds4OutputFlags { get; set; } = 0x07;

    /// <summary>DualSense only: also set the "haptics select" bit. Most firmware needs it; a few clones want it off.</summary>
    public bool Ds5HapticsSelect { get; set; } = true;

    /// <summary>Clone pads under another ID, as "VID:PID:kind" in hex, kind = ps3, ds4 or ds5 (kind defaults to ps3). Example: "1234:ABCD:ds4".</summary>
    public List<string> ExtraDevices { get; set; } = new();

    public Config Clone()
    {
        var c = (Config)MemberwiseClone();
        c.ExtraDevices = new List<string>(ExtraDevices);
        return c;
    }

    public static Config Load(string? explicitPath)
    {
        string path = explicitPath ?? Path.Combine(AppContext.BaseDirectory, "maxidodps3.json");
        if (!File.Exists(path))
        {
            if (explicitPath != null) throw new FileNotFoundException("Config file not found.", path);
            return new Config();
        }

        var opts = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };
        var cfg = JsonSerializer.Deserialize<Config>(File.ReadAllText(path), opts) ?? new Config();
        cfg.Normalize();
        return cfg;
    }

    /// <summary>Validates and tidies values. Throws ArgumentException with a readable message for a bad mode.</summary>
    public void Normalize()
    {
        Ps3Mode = CheckMode(Ps3Mode, nameof(Ps3Mode));
        Ds4Mode = CheckMode(Ds4Mode, nameof(Ds4Mode));
        DualSenseMode = CheckMode(DualSenseMode, nameof(DualSenseMode));

        MaxPads = Math.Clamp(MaxPads, 1, 4);
        StickDeadzone = Math.Clamp(StickDeadzone, 0.0, 0.45);
        LargeMotorScale = Math.Clamp(LargeMotorScale, 0.0, 2.0);
        SmallMotorScale = Math.Clamp(SmallMotorScale, 0.0, 2.0);
        ExtraDevices ??= new List<string>();
        HidHideCliPath ??= "";

        Lightbar = (Lightbar ?? "slot").Trim().ToLowerInvariant() == "off" ? "off" : "slot";

        RumbleMode = (RumbleMode ?? "refresh").Trim().ToLowerInvariant();
        if (RumbleMode != "latch") RumbleMode = "refresh";
        RumbleRefreshMs = Math.Clamp(RumbleRefreshMs, 30, 1000);

        WriteMethod = (WriteMethod ?? "auto").Trim().ToLowerInvariant();
        if (WriteMethod != "stream" && WriteMethod != "setoutput") WriteMethod = "auto";
    }

    private static string CheckMode(string? value, string field)
    {
        string v = (value ?? "x360").Trim().ToLowerInvariant();
        if (v is "x360" or "ds4" or "native") return v;
        throw new ArgumentException(
            $"{field} must be x360, ds4 or native (got '{value}'). A DualSense output is not available yet; see docs/DS5.md.");
    }

    public string ModeFor(PadKind kind) => kind switch
    {
        PadKind.Ps3 => Ps3Mode,
        PadKind.Ds4 => Ds4Mode,
        _ => DualSenseMode,
    };

    /// <summary>Built-in pads plus any ExtraDevices entries (which override a built-in with the same VID/PID).</summary>
    public List<KnownPad> AllKnown()
    {
        var list = new List<KnownPad>(Devices.Builtin);
        foreach (var entry in ExtraDevices)
        {
            var parts = entry.Split(':');
            if (parts.Length < 2 || parts.Length > 3) continue;
            if (!int.TryParse(parts[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int vid)) continue;
            if (!int.TryParse(parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int pid)) continue;

            PadKind kind = PadKind.Ps3;
            if (parts.Length == 3)
            {
                switch (parts[2].Trim().ToLowerInvariant())
                {
                    case "ps3": kind = PadKind.Ps3; break;
                    case "ds4": kind = PadKind.Ds4; break;
                    case "ds5":
                    case "dualsense": kind = PadKind.DualSense; break;
                    default: continue;
                }
            }

            list.RemoveAll(k => k.Vid == vid && k.Pid == pid);
            list.Add(new KnownPad(vid, pid, kind, $"Custom {kind} pad {vid:X4}:{pid:X4}"));
        }
        return list;
    }
}
