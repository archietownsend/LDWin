using System.Diagnostics;
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

    /// <summary>
    /// Returns the installed Npcap (or WinPcap-compatible) version string by reading
    /// the product version stamped into wpcap.dll, or <see langword="null"/> if the
    /// version cannot be determined (driver not installed, insufficient access, or
    /// running off-Windows).
    /// </summary>
    /// <returns>
    /// A trimmed version string such as <c>"1.79"</c>, or <see langword="null"/>.
    /// </returns>
    public string? GetInstalledVersion()
    {
        try
        {
            var winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (string.IsNullOrEmpty(winDir))
            {
                return null;
            }

            var candidates = new[]
            {
                Path.Combine(winDir, "System32", "Npcap", "wpcap.dll"),
                Path.Combine(winDir, "System32", "wpcap.dll"),
            };

            foreach (var path in candidates)
            {
                try
                {
                    if (!File.Exists(path))
                    {
                        continue;
                    }

                    var info = FileVersionInfo.GetVersionInfo(path);

                    // Prefer ProductVersion; fall back to FileVersion.
                    var version = info.ProductVersion?.Trim();
                    if (string.IsNullOrEmpty(version))
                    {
                        version = info.FileVersion?.Trim();
                    }

                    if (!string.IsNullOrEmpty(version))
                    {
                        return version;
                    }
                }
                catch
                {
                    // Unable to read this candidate — try the next one.
                }
            }
        }
        catch
        {
            // Any unexpected failure (e.g. security exception resolving %WinDir%).
        }

        return null;
    }
}
