using LDWin.Models;

namespace LDWin.Capture;

/// <summary>
/// Enumerates capture-capable adapters and listens on one for the first CDP or
/// LLDP announcement. Implemented over SharpPcap/Npcap.
/// </summary>
public interface ICaptureService
{
    /// <summary>All adapters Npcap can capture on.</summary>
    IReadOnlyList<AdapterInfo> GetAdapters();

    /// <summary>
    /// Listen on <paramref name="adapter"/> for the full <paramref name="timeout"/>,
    /// collecting every distinct CDP/LLDP neighbour that announces itself.
    /// Neighbours are deduplicated by (Protocol, DeviceId, PortId). Returns the
    /// collected list in the order first seen; the list is empty if nothing
    /// arrived before the deadline or cancellation.
    /// </summary>
    /// <param name="onNeighbourFound">
    /// Optional callback invoked as soon as each new distinct neighbour is decoded,
    /// so a caller (e.g. the GUI) can display results immediately instead of
    /// waiting for the full timeout - listening continues afterwards in case more
    /// switches are on the same segment.
    /// </param>
    IReadOnlyList<LinkData> CaptureAll(
        AdapterInfo adapter,
        TimeSpan timeout,
        CancellationToken cancellationToken = default,
        Action<LinkData>? onNeighbourFound = null);

    /// <summary>
    /// Returns the current Wi-Fi association (SSID/BSSID/signal) for
    /// <paramref name="adapter"/>, or <see langword="null"/> if the adapter
    /// is not wireless, is not currently associated, or the info could not be
    /// read. This is AP association info, not switch/port discovery data -
    /// see the <see cref="CaptureService"/> remarks on why CDP/LLDP cannot
    /// reach a wireless station.
    /// </summary>
    WifiInfo? GetWifiInfo(AdapterInfo adapter);
}
