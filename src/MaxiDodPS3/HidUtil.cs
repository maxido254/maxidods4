using System.Text.RegularExpressions;
using HidSharp;

namespace MaxiDodPS3;

public static class HidUtil
{
    public static bool LooksBluetooth(string path) =>
        path.Contains("bthenum", StringComparison.OrdinalIgnoreCase)
        || path.Contains("00001124-0000-1000-8000-00805f9b34fb", StringComparison.OrdinalIgnoreCase);

    public static string Transport(string path) => LooksBluetooth(path) ? "Bluetooth" : "USB/other";

    public static string SafeName(HidDevice d)
    {
        try { return d.GetProductName() ?? "pad"; }
        catch { return "pad"; }
    }

    /// <summary>Skips HID interfaces that are too small to be a gamepad report.</summary>
    public static bool PlausibleGamepad(HidDevice d)
    {
        try { return d.GetMaxInputReportLength() >= 10; }
        catch { return true; }
    }

    private static readonly Regex MacLike = new("^[0-9A-F]{12}$", RegexOptions.Compiled);

    /// <summary>
    /// A MAC-style serial (12 hex digits, not all the same) used to recognise one physical pad
    /// showing up over both USB and Bluetooth. Returns null when there is no usable serial.
    /// </summary>
    public static string? TrySerial(HidDevice d)
    {
        try
        {
            string? s = d.GetSerialNumber();
            if (string.IsNullOrWhiteSpace(s)) return null;
            s = s.Trim().Replace(":", "").Replace("-", "").ToUpperInvariant();
            if (!MacLike.IsMatch(s)) return null;
            if (s.Distinct().Count() == 1) return null;
            return s;
        }
        catch { return null; }
    }

    public static bool TrySetFeature(HidStream stream, HidDevice dev, byte[] report)
    {
        try { stream.SetFeature(report); return true; }
        catch
        {
            // Some Windows HID stacks insist on the full feature-report length.
            try
            {
                int max = dev.GetMaxFeatureReportLength();
                if (max > report.Length)
                {
                    var padded = new byte[max];
                    Array.Copy(report, padded, report.Length);
                    stream.SetFeature(padded);
                    return true;
                }
            }
            catch { }
            return false;
        }
    }

    /// <summary>
    /// Reads a feature report and discards it. Reading DS4 report 0x02 / DualSense report 0x05 over Bluetooth
    /// is what makes those pads switch from "basic" to full reports.
    /// </summary>
    public static bool TryGetFeature(HidStream stream, HidDevice dev, byte id, int length)
    {
        try
        {
            var b = new byte[length];
            b[0] = id;
            stream.GetFeature(b);
            return true;
        }
        catch
        {
            try
            {
                int max = dev.GetMaxFeatureReportLength();
                if (max > 0 && max != length)
                {
                    var b = new byte[max];
                    b[0] = id;
                    stream.GetFeature(b);
                    return true;
                }
            }
            catch { }
            return false;
        }
    }
}
