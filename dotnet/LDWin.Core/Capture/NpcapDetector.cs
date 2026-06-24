using Microsoft.Win32;

namespace LDWin.Capture;

/// <summary>
/// Detects an installed Npcap (or legacy WinPcap-compatible) capture driver by
/// probing the well-known wpcap.dll locations and the relevant service registry
/// keys. Used by the GUI to prompt the user to install Npcap rather than failing
/// silently when no capture backend is present.
/// </summary>
public sealed class NpcapDetector : INpcapDetector
{
    /// <inheritdoc />
    public string DownloadUrl => "https://npcap.com/";

    /// <inheritdoc />
    public bool IsInstalled()
    {
        // (a) wpcap.dll on disk. Npcap installs into System32\Npcap; some
        // configurations (or legacy WinPcap) place it directly in System32.
        try
        {
            var winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (!string.IsNullOrEmpty(winDir))
            {
                var candidates = new[]
                {
                    Path.Combine(winDir, "System32", "Npcap", "wpcap.dll"),
                    Path.Combine(winDir, "System32", "wpcap.dll"),
                };

                foreach (var path in candidates)
                {
                    if (File.Exists(path))
                    {
                        return true;
                    }
                }
            }
        }
        catch
        {
            // Fall through to the registry check.
        }

        // (b) The "npcap" (Npcap) or "npf" (WinPcap) driver service.
        // The registry is Windows-only; LDWin.Core targets net8.0 so guard the call.
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        foreach (var service in new[] { "npcap", "npf" })
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(
                    $@"SYSTEM\CurrentControlSet\Services\{service}");
                if (key is not null)
                {
                    return true;
                }
            }
            catch
            {
                // Ignore and try the next service / give up.
            }
        }

        return false;
    }
}
