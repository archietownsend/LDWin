namespace LDWin.Capture;

/// <summary>
/// Detects whether Npcap (the HVCI-compatible WinPcap successor) is installed,
/// so the GUI can prompt the user to install it rather than failing silently.
/// </summary>
public interface INpcapDetector
{
    bool IsInstalled();

    /// <summary>Where to send the user to install Npcap.</summary>
    string DownloadUrl { get; }

    /// <summary>
    /// The installed Npcap (or WinPcap-compatible) version string, e.g. "1.79",
    /// or <c>null</c> if it cannot be determined / nothing is installed.
    /// </summary>
    string? GetInstalledVersion();
}
