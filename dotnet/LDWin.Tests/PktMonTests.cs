using System.Linq;
using LDWin.Capture;
using LDWin.Protocols;
using Xunit;

namespace LDWin.Tests;

/// <summary>
/// Covers the pktmon-specific parsing that can run off-Windows: matching an
/// adapter to its pktmon component IDs, and finding CDP/LLDP frames inside a
/// PktMon ETW event payload.
/// </summary>
public class PktMonTests
{
    private const string SampleList = @"Network Adapters:
  Id  MAC Address        Name
  --  -----------        ----
   7  F8-AC-65-11-22-33  Intel(R) Wi-Fi 6 AX201 160MHz
   5  00-E0-4C-68-01-02  Realtek USB GbE Family Controller
  12  00-E0-4C-68-01-02  Hyper-V Virtual Ethernet Adapter
";

    [Fact]
    public void ParseComponentIds_MatchesByMac_IgnoringSeparatorAndCase()
    {
        var ids = PktMon.ParseComponentIds(SampleList, "f8:ac:65:11:22:33");

        Assert.Equal(new[] { "7" }, ids);
    }

    [Fact]
    public void ParseComponentIds_SharedMac_ReturnsEveryComponent()
    {
        var ids = PktMon.ParseComponentIds(SampleList, "00:E0:4C:68:01:02");

        Assert.Equal(new[] { "5", "12" }, ids);
    }

    [Fact]
    public void ParseComponentIds_UnknownOrMissingMac_ReturnsNothing()
    {
        Assert.Empty(PktMon.ParseComponentIds(SampleList, "AA:BB:CC:DD:EE:FF"));
        Assert.Empty(PktMon.ParseComponentIds(SampleList, ""));
    }

    [Fact]
    public void FindFrames_LldpAfterMetadata_DecodesNeighbour()
    {
        var frame = FrameBuilder.LldpEthernetHeader();
        FrameBuilder.AddLldpTlv(frame, 1, FrameBuilder.IdField(4, new byte[] { 0, 1, 2, 3, 4, 5 }));
        FrameBuilder.AddLldpTlv(frame, 2, FrameBuilder.IdField(5, FrameBuilder.Ascii("Gi1/0/7")));
        FrameBuilder.AddLldpTlv(frame, 3, new byte[] { 0x00, 0x78 });
        FrameBuilder.AddLldpTlv(frame, 5, FrameBuilder.Ascii("Access-SW1"));
        FrameBuilder.AddLldpTlv(frame, 0, new byte[0]);

        var eventData = WithMetadata(frame.ToArray());

        var decoded = DecodeFirst(eventData);

        Assert.NotNull(decoded);
        Assert.Equal("LLDP", decoded!.Protocol);
        Assert.Equal("Access-SW1", decoded.DeviceId);
        Assert.Equal("Gi1/0/7", decoded.PortId);
    }

    [Fact]
    public void FindFrames_CdpWithTrailingBytes_TrimsToIeee8023Length()
    {
        var frame = FrameBuilder.CdpHeader();
        FrameBuilder.AddCdpTlv(frame, 0x0001, FrameBuilder.Ascii("Core-SW"));
        FrameBuilder.AddCdpTlv(frame, 0x0003, FrameBuilder.Ascii("GigabitEthernet0/1"));
        int llcLength = frame.Count - 14;
        frame[12] = (byte)(llcLength >> 8);
        frame[13] = (byte)(llcLength & 0xFF);

        var payload = frame.Concat(new byte[] { 0x00, 0x09, 0x00, 0x08, 0xDE, 0xAD, 0xBE, 0xEF }).ToArray();
        var eventData = WithMetadata(payload);

        var frames = PktMonFrameExtractor.FindFrames(eventData).ToList();

        Assert.Single(frames);
        Assert.Equal(frame.Count, frames[0].Length);
        Assert.Equal("Core-SW", DecodeFirst(eventData)!.DeviceId);
    }

    [Fact]
    public void FindFrames_LldpMulticastWithoutLldpEtherType_IsIgnored()
    {
        // Same destination MAC as STP BPDUs, but not LLDP.
        var notLldp = FrameBuilder.LldpEthernetHeader(
            new byte[] { 0x01, 0x80, 0xC2, 0x00, 0x00, 0x00 }, ethertype: 0x0026);
        notLldp.AddRange(new byte[20]);

        Assert.Empty(PktMonFrameExtractor.FindFrames(WithMetadata(notLldp.ToArray())));
    }

    [Fact]
    public void FindFrames_NoFrame_ReturnsNothing()
    {
        Assert.Empty(PktMonFrameExtractor.FindFrames(new byte[64]));
        Assert.Empty(PktMonFrameExtractor.FindFrames(new byte[0]));
    }

    private static byte[] WithMetadata(byte[] frame)
    {
        var metadata = Enumerable.Range(0, 40).Select(i => (byte)(i * 7)).ToArray();
        return metadata.Concat(frame).ToArray();
    }

    private static LDWin.Models.LinkData? DecodeFirst(byte[] eventData)
    {
        var decoders = new ILinkDecoder[] { new LldpDecoder(), new CdpDecoder() };
        foreach (var frame in PktMonFrameExtractor.FindFrames(eventData))
        {
            foreach (var decoder in decoders)
            {
                if (decoder.TryDecode(frame, out var data) && data is not null)
                {
                    return data;
                }
            }
        }

        return null;
    }
}
