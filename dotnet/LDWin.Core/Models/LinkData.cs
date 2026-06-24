using System.Text;

namespace LDWin.Models;

/// <summary>
/// The result of a link-discovery capture: the local adapter details plus the
/// information advertised by the directly-connected switch (CDP or LLDP).
/// This is the shared contract between the capture engine, the protocol
/// decoders, and the GUI - decoders populate the "neighbour" fields, the
/// capture engine fills in the local-adapter fields.
/// </summary>
public sealed class LinkData
{
    /// <summary>"CDP" or "LLDP".</summary>
    public string Protocol { get; set; } = "";

    // ---- Local adapter (filled in by the capture engine) ----
    public string AdapterName { get; set; } = "";          // Npcap device name, e.g. \Device\NPF_{GUID}
    public string AdapterDescription { get; set; } = "";   // friendly description
    public string LocalIpAddress { get; set; } = "";
    public string LocalMacAddress { get; set; } = "";

    // ---- Discovered neighbour (filled in by a decoder) ----
    public string DeviceId { get; set; } = "";             // switch name / chassis id
    public string PortId { get; set; } = "";               // switch port / interface
    public string Platform { get; set; } = "";             // model / system description
    public string SoftwareVersion { get; set; } = "";
    public string ManagementAddress { get; set; } = "";    // switch management IP
    public string Vlan { get; set; } = "";                 // native VLAN (CDP) / VLAN id
    public string Capabilities { get; set; } = "";
    public int TimeToLive { get; set; }

    /// <summary>True once a decoder has populated at least the device + port.</summary>
    public bool HasNeighbourInfo =>
        !string.IsNullOrWhiteSpace(DeviceId) || !string.IsNullOrWhiteSpace(PortId);

    /// <summary>Human-readable report, also used for "Save Link Data".</summary>
    public string ToReport()
    {
        var sb = new StringBuilder();
        sb.AppendLine("LDWin - Link Discovery Result");
        sb.AppendLine("=============================");
        sb.AppendLine($"Protocol            : {Protocol}");
        sb.AppendLine();
        sb.AppendLine("-- Local adapter --");
        sb.AppendLine($"Adapter             : {AdapterDescription}");
        sb.AppendLine($"Local IP address    : {LocalIpAddress}");
        sb.AppendLine($"Local MAC address   : {LocalMacAddress}");
        sb.AppendLine();
        sb.AppendLine("-- Connected switch --");
        sb.AppendLine($"Device / switch name: {DeviceId}");
        sb.AppendLine($"Port                : {PortId}");
        sb.AppendLine($"Platform / model    : {Platform}");
        sb.AppendLine($"Software version    : {SoftwareVersion}");
        sb.AppendLine($"Management address  : {ManagementAddress}");
        sb.AppendLine($"VLAN                : {Vlan}");
        sb.AppendLine($"Capabilities        : {Capabilities}");
        sb.AppendLine($"TTL (seconds)       : {TimeToLive}");
        return sb.ToString();
    }
}
