namespace LDWin.Capture;

/// <summary>
/// The current 802.11 association for a wireless adapter: which access point
/// it is talking to, not which switch/port the AP's uplink is on. CDP/LLDP
/// cannot reach a wireless station (see <see cref="CaptureService"/>), so this
/// is the closest "link data" available over Wi-Fi.
/// </summary>
public sealed class WifiInfo
{
    public string Ssid { get; init; } = "";
    public string Bssid { get; init; } = "";
    public int SignalQualityPercent { get; init; }
    public int LinkSpeedMbps { get; init; }
}

/// <summary>Reads the current Wi-Fi association for a wireless adapter.</summary>
public interface IWifiInfoProvider
{
    /// <summary>
    /// Returns the current association for the adapter identified by its
    /// interface GUID, or <see langword="null"/> if unavailable, with the
    /// Win32 error code in <paramref name="errorCode"/> (0 on success).
    /// </summary>
    WifiInfo? GetCurrentConnection(Guid interfaceGuid, out uint errorCode);
}
