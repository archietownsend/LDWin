using System.Net.NetworkInformation;
using System.Net.Sockets;
using LDWin.Models;
using LDWin.Protocols;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;

namespace LDWin.Capture;

/// <summary>
/// <see cref="ICaptureService"/> built on Windows Packet Monitor (pktmon), so
/// no capture driver has to be installed. pktmon is configured to log only
/// LLDP/CDP frames on the chosen adapter, and its packet events are read live
/// from ETW, so each neighbour is decoded the moment its announcement arrives.
///
/// CDP/LLDP cannot be received over Wi-Fi: a switch sends these frames only to
/// whatever is directly plugged into its port - the access point, not a
/// wireless client - and access points do not relay CDP's/LLDP's multicast
/// discovery frames over the air. <see cref="GetWifiInfo"/> exposes the
/// closest available substitute (the current AP association) instead.
/// </summary>
public sealed class CaptureService : ICaptureService
{
    private const string EtwSessionName = "LDWin-PktMon";
    private const string LldpFilterName = "LDWin-LLDP";
    private const string CdpFilterName = "LDWin-CDP";
    private const string LldpEtherType = "35020"; // 0x88CC
    private const string CdpMulticastMac = "01-00-0C-CC-CC-CC";
    private const string CaptureFileSizeMb = "8";

    // pktmon is system-wide: one capture and one filter list. This marker
    // records that LDWin has a capture running, so if LDWin is killed mid-
    // capture the next run can stop it rather than fail to start.
    private static readonly string ActiveMarker = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LDWin", "pktmon-capture-active");

    private static readonly string CaptureFile = Path.Combine(Path.GetTempPath(), "LDWin-pktmon.etl");

    private readonly IReadOnlyList<ILinkDecoder> _decoders;
    private readonly IWifiInfoProvider _wifiInfo;

    public CaptureService(IEnumerable<ILinkDecoder> decoders)
        : this(decoders, new WifiInfoProvider())
    {
    }

    public CaptureService(IEnumerable<ILinkDecoder> decoders, IWifiInfoProvider wifiInfo)
    {
        ArgumentNullException.ThrowIfNull(decoders);
        ArgumentNullException.ThrowIfNull(wifiInfo);
        _decoders = decoders.ToList();
        _wifiInfo = wifiInfo;
    }

    /// <inheritdoc />
    public IReadOnlyList<AdapterInfo> GetAdapters()
    {
        var adapters = new List<AdapterInfo>();

        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            try
            {
                if (nic.OperationalStatus != OperationalStatus.Up ||
                    nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                {
                    continue;
                }

                // pktmon components are matched to adapters by MAC address.
                var mac = nic.GetPhysicalAddress().GetAddressBytes();
                if (mac.Length != 6)
                {
                    continue;
                }

                adapters.Add(new AdapterInfo
                {
                    Name = nic.Id,
                    Description = nic.Name,
                    IpAddress = FirstIpv4Address(nic),
                    MacAddress = string.Join(":", mac.Select(b => b.ToString("X2"))),
                    IsWireless = nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211,
                });
            }
            catch
            {
                // Skip a single problematic interface.
            }
        }

        return adapters;
    }

    /// <inheritdoc />
    public WifiInfo? GetWifiInfo(AdapterInfo adapter, out uint errorCode)
    {
        ArgumentNullException.ThrowIfNull(adapter);

        errorCode = 0;
        if (!adapter.IsWireless)
        {
            return null;
        }

        var guidText = ExtractGuid(adapter.Name);
        if (guidText is null || !Guid.TryParse(guidText, out var guid))
        {
            errorCode = WifiInfoProvider.ErrorNotFound;
            return null;
        }

        try
        {
            return _wifiInfo.GetCurrentConnection(guid, out errorCode);
        }
        catch
        {
            errorCode = WifiInfoProvider.ErrorUnexpected;
            return null;
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<LinkData> CaptureAll(
        AdapterInfo adapter,
        TimeSpan timeout,
        CancellationToken cancellationToken = default,
        Action<LinkData>? onNeighbourFound = null)
    {
        ArgumentNullException.ThrowIfNull(adapter);

        if (!PktMon.IsAvailable())
        {
            throw new InvalidOperationException(
                "Windows Packet Monitor (pktmon.exe) was not found. It is built into " +
                "Windows 10 version 2004 and later, and Windows 11.");
        }

        StopInterruptedCapture();

        var results = new List<LinkData>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sync = new object();

        TraceEventSession? session = null;
        Task? processing = null;
        bool filtersSet = false;
        bool started = false;
        try
        {
            // Start listening before pktmon starts logging so no early frame is missed.
            session = new TraceEventSession(EtwSessionName);
            session.EnableProvider(PktMon.ProviderGuid, TraceEventLevel.Verbose, ulong.MaxValue);
            session.Source.AllEvents += data =>
            {
                if (data.ProviderGuid != PktMon.ProviderGuid)
                {
                    return;
                }

                var neighbour = Decode(data.EventData());
                if (neighbour is null)
                {
                    return;
                }

                // pktmon logs a packet once per component it passes through, so
                // duplicates are expected here.
                string key = $"{neighbour.Protocol}|{neighbour.DeviceId}|{neighbour.PortId}";
                lock (sync)
                {
                    if (!seen.Add(key))
                    {
                        return;
                    }

                    PopulateLocalFields(neighbour, adapter);
                    results.Add(neighbour);
                }

                onNeighbourFound?.Invoke(neighbour);
            };

            processing = Task.Factory.StartNew(
                () => session.Source.Process(), TaskCreationOptions.LongRunning);

            var componentIds = FindComponentIds(adapter);

            PktMon.Run("filter", "remove");
            filtersSet = true;
            RunOrThrow("filter", "add", LldpFilterName, "-d", LldpEtherType);
            RunOrThrow("filter", "add", CdpFilterName, "-m", CdpMulticastMac);

            Directory.CreateDirectory(Path.GetDirectoryName(ActiveMarker)!);
            File.WriteAllText(ActiveMarker, "");

            var start = new List<string> { "start", "--capture", "--comp" };
            start.AddRange(componentIds);
            start.AddRange(new[] { "--pkt-size", "0", "--file-name", CaptureFile, "--file-size", CaptureFileSizeMb });
            var (exitCode, output) = PktMon.Run(start.ToArray());
            if (exitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Windows Packet Monitor could not start a capture:{Environment.NewLine}{output}" +
                    $"{Environment.NewLine}{Environment.NewLine}If another Packet Monitor capture is " +
                    "running, stop it with \"pktmon stop\" and try again.");
            }

            started = true;

            var signalled = WaitHandle.WaitAny(
                new[] { cancellationToken.WaitHandle, ((IAsyncResult)processing).AsyncWaitHandle },
                timeout);
            if (signalled == 1)
            {
                processing.GetAwaiter().GetResult(); // surfaces the ETW failure, if any
                throw new InvalidOperationException("The packet capture session ended unexpectedly.");
            }
        }
        finally
        {
            if (started)
            {
                TryRun("stop");
            }

            if (filtersSet)
            {
                TryRun("filter", "remove");
            }

            TryDelete(ActiveMarker);
            session?.Dispose();
            try { processing?.Wait(TimeSpan.FromSeconds(5)); } catch { /* already reported */ }
            TryDelete(CaptureFile);
        }

        cancellationToken.ThrowIfCancellationRequested();

        lock (sync)
        {
            return results.ToList();
        }
    }

    private LinkData? Decode(byte[] eventData)
    {
        foreach (var frame in PktMonFrameExtractor.FindFrames(eventData))
        {
            foreach (var decoder in _decoders)
            {
                try
                {
                    if (decoder.TryDecode(frame, out var data) && data is not null)
                    {
                        return data;
                    }
                }
                catch
                {
                    // A malformed frame must not stop the capture.
                }
            }
        }

        return null;
    }

    /// <summary>
    /// The pktmon component IDs for <paramref name="adapter"/>, or "nics" (every
    /// network adapter) if it can't be matched by MAC address.
    /// </summary>
    private static IReadOnlyList<string> FindComponentIds(AdapterInfo adapter)
    {
        var (exitCode, output) = PktMon.Run("list");
        if (exitCode == 0)
        {
            var ids = PktMon.ParseComponentIds(output, adapter.MacAddress);
            if (ids.Count > 0)
            {
                return ids;
            }
        }

        return new[] { "nics" };
    }

    private static void StopInterruptedCapture()
    {
        if (!File.Exists(ActiveMarker))
        {
            return;
        }

        TryRun("stop");
        TryRun("filter", "remove");
        TryDelete(ActiveMarker);
    }

    private static void RunOrThrow(params string[] arguments)
    {
        var (exitCode, output) = PktMon.Run(arguments);
        if (exitCode != 0)
        {
            throw new InvalidOperationException(
                $"pktmon {string.Join(' ', arguments)} failed:{Environment.NewLine}{output}");
        }
    }

    private static void TryRun(params string[] arguments)
    {
        try { PktMon.Run(arguments); } catch { /* best-effort cleanup */ }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { /* best-effort cleanup */ }
    }

    private static string FirstIpv4Address(NetworkInterface nic)
    {
        foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
        {
            if (unicast.Address.AddressFamily == AddressFamily.InterNetwork)
            {
                return unicast.Address.ToString();
            }
        }

        return "";
    }

    private static void PopulateLocalFields(LinkData data, AdapterInfo adapter)
    {
        data.AdapterName = adapter.Name;
        data.AdapterDescription = adapter.Description;
        data.LocalIpAddress = adapter.IpAddress;
        data.LocalMacAddress = adapter.MacAddress;
    }

    /// <summary>Extracts the GUID from an adapter name like {GUID} or \Device\NPF_{GUID}.</summary>
    private static string? ExtractGuid(string name)
    {
        int open = name.IndexOf('{');
        int close = name.IndexOf('}');
        if (open >= 0 && close > open)
        {
            return name.Substring(open + 1, close - open - 1).ToUpperInvariant();
        }

        return null;
    }
}
