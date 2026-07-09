using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using LDWin.Models;
using LDWin.Protocols;
using SharpPcap;
using SharpPcap.LibPcap;

namespace LDWin.Capture;

/// <summary>
/// SharpPcap/Npcap-backed implementation of <see cref="ICaptureService"/>.
/// Enumerates capture-capable adapters and listens on a chosen adapter for the
/// first CDP or LLDP frame, decoding it via the registered
/// <see cref="ILinkDecoder"/> implementations.
/// </summary>
public sealed class CaptureService : ICaptureService
{
    // BPF filter: LLDP uses EtherType 0x88cc; CDP is delivered to the
    // Cisco multicast MAC 01:00:0c:cc:cc:cc (SNAP-encapsulated, no fixed
    // EtherType), so we match on the destination MAC as well.
    private const string CdpLldpFilter = "ether proto 0x88cc or ether dst 01:00:0c:cc:cc:cc";

    private const int ReadTimeoutMs = 1000;

    private readonly IReadOnlyList<ILinkDecoder> _decoders;

    public CaptureService(IEnumerable<ILinkDecoder> decoders)
    {
        ArgumentNullException.ThrowIfNull(decoders);
        _decoders = decoders.ToList();
    }

    /// <inheritdoc />
    public IReadOnlyList<AdapterInfo> GetAdapters()
    {
        var adapters = new List<AdapterInfo>();

        // CaptureDeviceList.Instance is a live, refreshing snapshot of the
        // adapters Npcap can capture on.
        foreach (var device in CaptureDeviceList.Instance)
        {
            try
            {
                string description = "";
                string ipAddress = "";
                string macAddress = "";

                if (device is LibPcapLiveDevice liveDevice)
                {
                    // Prefer the friendly name from the interface, falling back
                    // to the raw device description.
                    var friendly = liveDevice.Interface?.FriendlyName;
                    description = !string.IsNullOrWhiteSpace(friendly)
                        ? friendly!
                        : liveDevice.Description ?? "";

                    if (liveDevice.Interface?.Addresses is { } addresses)
                    {
                        foreach (var addr in addresses)
                        {
                            var sockAddr = addr.Addr?.ipAddress;
                            if (sockAddr is { AddressFamily: AddressFamily.InterNetwork })
                            {
                                ipAddress = sockAddr.ToString();
                                break;
                            }
                        }
                    }

                    if (liveDevice.Interface?.MacAddress is { } mac)
                    {
                        macAddress = FormatMac(mac);
                    }
                }
                else
                {
                    description = device.Description ?? "";
                }

                adapters.Add(new AdapterInfo
                {
                    Name = device.Name,
                    Description = description,
                    IpAddress = ipAddress,
                    MacAddress = macAddress,
                });
            }
            catch
            {
                // A single misbehaving adapter must not break enumeration; fall
                // back to just the device name so it remains selectable.
                try
                {
                    adapters.Add(new AdapterInfo { Name = device.Name });
                }
                catch
                {
                    // Even the name was unavailable - skip this device entirely.
                }
            }
        }

        // Npcap enumerates every capture-capable interface, including virtual /
        // WAN-miniport / Wi-Fi-Direct pseudo-adapters, Bluetooth PAN and the Npcap
        // loopback adapter. Filter down to interfaces Windows reports as actually
        // up and physical-ish, matching on the {GUID} embedded in the device name.
        var usable = GetUsableInterfaceGuids();
        if (usable.Count > 0)
        {
            var filtered = adapters
                .Where(a =>
                {
                    var guid = ExtractGuid(a.Name);
                    return guid is not null && usable.Contains(guid);
                })
                .ToList();

            // Only apply the filter if it leaves something selectable; otherwise
            // fall back to the full list rather than show an empty drop-down.
            if (filtered.Count > 0)
            {
                return filtered;
            }
        }

        return adapters;
    }

    /// <summary>
    /// GUIDs (upper-case, no braces) of network interfaces that are operationally up
    /// and not loopback/tunnel - i.e. the adapters worth listening on.
    /// </summary>
    private static HashSet<string> GetUsableInterfaceGuids()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                try
                {
                    if (nic.OperationalStatus != OperationalStatus.Up)
                    {
                        continue;
                    }

                    if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                    {
                        continue;
                    }

                    var id = nic.Id?.Trim('{', '}');
                    if (!string.IsNullOrEmpty(id))
                    {
                        set.Add(id!.ToUpperInvariant());
                    }
                }
                catch
                {
                    // Skip a single problematic interface.
                }
            }
        }
        catch
        {
            // If enumeration fails entirely, return an empty set so GetAdapters
            // falls back to showing every Npcap device unfiltered.
        }

        return set;
    }

    /// <summary>Extracts the GUID from an Npcap device name like \Device\NPF_{GUID}.</summary>
    private static string? ExtractGuid(string deviceName)
    {
        int open = deviceName.IndexOf('{');
        int close = deviceName.IndexOf('}');
        if (open >= 0 && close > open)
        {
            return deviceName.Substring(open + 1, close - open - 1).ToUpperInvariant();
        }

        return null;
    }

    /// <inheritdoc />
    public IReadOnlyList<LinkData> CaptureAll(
        AdapterInfo adapter,
        TimeSpan timeout,
        CancellationToken cancellationToken = default,
        Action<LinkData>? onNeighbourFound = null)
    {
        ArgumentNullException.ThrowIfNull(adapter);

        var device = FindDevice(adapter.Name)
            ?? throw new InvalidOperationException(
                $"Capture adapter '{adapter.Name}' was not found. It may have been removed " +
                "or Npcap may not be installed.");

        var deadline = Stopwatch.StartNew();
        var results = new List<LinkData>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool opened = false;

        try
        {
            device.Open(new DeviceConfiguration
            {
                Mode = DeviceModes.Promiscuous,
                ReadTimeout = ReadTimeoutMs,
            });
            opened = true;

            device.Filter = CdpLldpFilter;

            while (deadline.Elapsed < timeout)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var status = device.GetNextPacket(out PacketCapture e);

                if (status != GetPacketStatus.PacketRead)
                {
                    continue;
                }

                byte[] frame;
                try
                {
                    frame = e.GetPacket().Data;
                }
                catch
                {
                    continue;
                }

                if (frame is not { Length: > 0 })
                {
                    continue;
                }

                foreach (var decoder in _decoders)
                {
                    LinkData? data;
                    try
                    {
                        if (!decoder.TryDecode(frame, out data) || data is null)
                        {
                            continue;
                        }
                    }
                    catch
                    {
                        continue;
                    }

                    string key = $"{data.Protocol}|{data.DeviceId}|{data.PortId}";
                    if (seen.Add(key))
                    {
                        PopulateLocalFields(data, adapter);
                        results.Add(data);
                        onNeighbourFound?.Invoke(data);
                    }

                    break; // Frame matched - no need to try remaining decoders.
                }
            }

            return results;
        }
        finally
        {
            if (opened)
            {
                try
                {
                    device.Close();
                }
                catch
                {
                    // Best-effort close; nothing actionable on failure.
                }
            }
        }
    }

    private static void PopulateLocalFields(LinkData data, AdapterInfo adapter)
    {
        data.AdapterName = adapter.Name;
        data.AdapterDescription = adapter.Description;
        data.LocalIpAddress = adapter.IpAddress;
        data.LocalMacAddress = adapter.MacAddress;
    }

    private static ILiveDevice? FindDevice(string name)
    {
        foreach (var device in CaptureDeviceList.Instance)
        {
            if (string.Equals(device.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return device;
            }
        }

        return null;
    }

    private static string FormatMac(PhysicalAddress mac)
    {
        return string.Join(":", mac.GetAddressBytes().Select(b => b.ToString("X2")));
    }
}
