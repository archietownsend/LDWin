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
    /// Listen on <paramref name="adapter"/> until a CDP/LLDP frame is received
    /// and decoded, the <paramref name="timeout"/> elapses, or the operation is
    /// cancelled. Returns the decoded <see cref="LinkData"/> (with local-adapter
    /// fields populated) or <c>null</c> if nothing was received in time.
    /// </summary>
    LinkData? Capture(AdapterInfo adapter, TimeSpan timeout, CancellationToken cancellationToken = default);

    /// <summary>
    /// Listen on <paramref name="adapter"/> for the full <paramref name="timeout"/>,
    /// collecting every distinct CDP/LLDP neighbour that announces itself.
    /// Neighbours are deduplicated by (Protocol, DeviceId, PortId). Returns the
    /// collected list in the order first seen; the list is empty if nothing
    /// arrived before the deadline or cancellation.
    /// </summary>
    IReadOnlyList<LinkData> CaptureAll(AdapterInfo adapter, TimeSpan timeout, CancellationToken cancellationToken = default);
}
