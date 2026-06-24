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
}
