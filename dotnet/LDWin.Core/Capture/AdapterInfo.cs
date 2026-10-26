namespace LDWin.Capture;

/// <summary>
/// A selectable network adapter, as presented in the GUI drop-down.
/// <see cref="Name"/> is the Windows interface ID; the remaining fields are
/// for display, for matching the adapter in pktmon, and for the result.
/// </summary>
public sealed class AdapterInfo
{
    /// <summary>Windows interface ID, e.g. <c>{GUID}</c>.</summary>
    public required string Name { get; init; }

    /// <summary>Friendly description shown to the user.</summary>
    public string Description { get; init; } = "";

    public string IpAddress { get; init; } = "";
    public string MacAddress { get; init; } = "";

    /// <summary>
    /// True if Windows reports this as an 802.11 (Wi-Fi) interface. CDP/LLDP
    /// cannot be received over Wi-Fi - see <see cref="CaptureService"/>.
    /// </summary>
    public bool IsWireless { get; init; }

    /// <summary>Text shown in the adapter drop-down.</summary>
    public override string ToString()
    {
        var label = string.IsNullOrWhiteSpace(Description) ? Name : Description;
        return IsWireless ? $"{label} (Wi-Fi)" : label;
    }
}
