namespace LDWin.Capture;

/// <summary>
/// A selectable network adapter, as presented in the GUI drop-down.
/// <see cref="Name"/> is the Npcap device name used to open the capture;
/// the remaining fields are for display and for stamping into the result.
/// </summary>
public sealed class AdapterInfo
{
    /// <summary>Npcap device name, e.g. <c>\Device\NPF_{GUID}</c>.</summary>
    public required string Name { get; init; }

    /// <summary>Friendly description shown to the user.</summary>
    public string Description { get; init; } = "";

    public string IpAddress { get; init; } = "";
    public string MacAddress { get; init; } = "";

    /// <summary>Text shown in the adapter drop-down.</summary>
    public override string ToString() =>
        string.IsNullOrWhiteSpace(Description) ? Name : Description;
}
