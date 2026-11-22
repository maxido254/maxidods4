using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Win32;

namespace MaxiDodPS3;

/// <summary>
/// One-click setup for wireless DS3 pads. A DS3 does not speak the normal Bluetooth profiles, so Windows needs a small
/// "profile driver" (BthPS3, by Nefarius) that sits on top of the Microsoft Bluetooth stack. It does not touch or replace
/// the Bluetooth radio's own driver (MediaTek, Intel, Realtek ...).
/// This class makes a System Restore point first, downloads the official installer from BthPS3's own GitHub page,
/// and runs it with one admin prompt. MaxiDodPS3 itself never modifies a driver.
/// </summary>
public static class BluetoothSetup
{
    private const string ReleasesApi = "https://api.github.com/repos/nefarius/BthPS3/releases?per_page=30";
    private static readonly string[] OfficialPrefixes =
    {
        "https://github.com/nefarius/BthPS3/",
        "https://github.com/ViGEm/BthPS3/",
    };

    public static bool IsInstalled()
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\BthPS3");
            return k != null;
        }
        catch { return false; }
    }

    /// <summary>Returns 0 on success, 3010 when Windows asks for a restart, anything else is a failure.</summary>
    public static async Task<int> RunAsync(Action<string> say)
    {
        if (IsInstalled())
        {
            say("Wireless DS3 support (BthPS3) is already installed. Plug each DS3 in by USB once and pair it, then unplug and press the PS button.");
            return 0;
        }
        if (!HostRadio.TryGetAddress(out _, out _))
        {
            say("No Bluetooth radio was found. Turn Bluetooth on (or plug in a Bluetooth adapter) and try again.");
            return 1;
        }

        string msi = Path.Combine(Path.GetTempPath(), "MaxiDodPS3-BthPS3-setup.msi");
        try
        {
            say("Looking up the official BthPS3 installer...");
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("MaxiDodPS3");
            string json = await http.GetStringAsync(ReleasesApi);
            using var doc = JsonDocument.Parse(json);

            string? url = null, tag = null;
            foreach (var rel in doc.RootElement.EnumerateArray())
            {
                if (rel.TryGetProperty("prerelease", out var pre) && pre.GetBoolean()) continue;
                string? best = null;
                foreach (var asset in rel.GetProperty("assets").EnumerateArray())
                {
                    string name = asset.GetProperty("name").GetString() ?? "";
                    string u = asset.GetProperty("browser_download_url").GetString() ?? "";
                    if (!name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase)) continue;
                    if (name.Contains("arm64", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!OfficialPrefixes.Any(p => u.StartsWith(p, StringComparison.OrdinalIgnoreCase))) continue;
                    best = u;
                    if (name.Contains("x64", StringComparison.OrdinalIgnoreCase)) break;
                }
                if (best != null) { url = best; tag = rel.GetProperty("tag_name").GetString(); break; }
            }
            if (url == null)
            {
                say("Could not find the BthPS3 installer on its GitHub page. Download it from github.com/nefarius/BthPS3/releases and run it yourself.");
                return 1;
            }

            say($"Downloading BthPS3 ({tag})...");
            byte[] data = await http.GetByteArrayAsync(url);
            await File.WriteAllBytesAsync(msi, data);
            Log.Info($"BthPS3 installer {tag}: {data.Length} bytes, SHA-256 {Convert.ToHexString(SHA256.HashData(data))}");
        }
        catch (Exception ex)
        {
            Log.Warn($"BthPS3 download failed: {ex.Message}");
            say("The download failed. Check your internet connection and try again.");
            return 1;
        }

        say("Making a System Restore point, then installing. Press Yes on the Windows prompt.");
        string script =
            "try { Checkpoint-Computer -Description 'MaxiDodPS3: before BthPS3' -RestorePointType MODIFY_SETTINGS -ErrorAction Stop } catch { } ; " +
            $"$p = Start-Process msiexec.exe -ArgumentList '/i','{msi}','/passive','/norestart' -Wait -PassThru; exit $p.ExitCode";
        try
        {
            var psi = new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            psi.ArgumentList.Add("-NoProfile");
            psi.ArgumentList.Add("-ExecutionPolicy");
            psi.ArgumentList.Add("Bypass");
            psi.ArgumentList.Add("-Command");
            psi.ArgumentList.Add(script);
            using var p = Process.Start(psi);
            if (p == null) { say("Could not start the installer."); return 1; }
            await p.WaitForExitAsync();
            int code = p.ExitCode;
            Log.Info($"BthPS3 installer finished with code {code}.");
            if (code == 0 || code == 3010)
            {
                say(code == 3010
                    ? "Installed. Restart Windows, then plug each DS3 in by USB once, pair it (tray menu), unplug it and press the PS button."
                    : "Installed. Plug each DS3 in by USB once, pair it (tray menu), unplug it and press the PS button.");
                return code;
            }
            say($"The installer stopped with code {code}. Nothing was changed if it was cancelled. Your restore point can undo it.");
            return code;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            say("The administrator prompt was cancelled, so nothing was installed.");
            return 1;
        }
        catch (Exception ex)
        {
            Log.Warn($"BthPS3 setup failed: {ex.Message}");
            say("Setup could not run: " + ex.Message);
            return 1;
        }
        finally
        {
            try { File.Delete(msi); } catch { /* temp file */ }
        }
    }
}
