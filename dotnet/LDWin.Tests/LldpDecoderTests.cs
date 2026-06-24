using System.Collections.Generic;
using LDWin.Protocols;
using Xunit;

namespace LDWin.Tests;

public class LldpDecoderTests
{
    private readonly LldpDecoder _decoder = new();

    [Fact]
    public void Protocol_IsLldp() => Assert.Equal("LLDP", _decoder.Protocol);

    [Fact]
    public void Decodes_FullFrame_AllFieldsPopulated()
    {
        var buf = FrameBuilder.LldpEthernetHeader();
        // Chassis ID, subtype 4 (MAC) - should later be overridden by System Name.
        FrameBuilder.AddLldpTlv(buf, 1, FrameBuilder.IdField(0x04, new byte[] { 0x00, 0x11, 0x22, 0x33, 0x44, 0x55 }));
        // Port ID, subtype 7 (locally assigned) = interface name string.
        FrameBuilder.AddLldpTlv(buf, 2, FrameBuilder.IdField(0x07, FrameBuilder.Ascii("Gi1/0/24")));
        // TTL = 120.
        FrameBuilder.AddLldpTlv(buf, 3, new byte[] { 0x00, 0x78 });
        // System Name.
        FrameBuilder.AddLldpTlv(buf, 5, FrameBuilder.Ascii("switch01.example.com"));
        // System Description.
        FrameBuilder.AddLldpTlv(buf, 6, FrameBuilder.Ascii("Cisco IOS Software, C3750"));
        // System Capabilities: supported 0x0014, enabled 0x0014 (Bridge + Router).
        FrameBuilder.AddLldpTlv(buf, 7, new byte[] { 0x00, 0x14, 0x00, 0x14 });
        // Management Address: IPv4 10.0.0.1.
        FrameBuilder.AddLldpTlv(buf, 8, FrameBuilder.LldpMgmtAddressIpv4(new byte[] { 10, 0, 0, 1 }));
        // End TLV.
        FrameBuilder.AddLldpTlv(buf, 0, System.Array.Empty<byte>());

        bool ok = _decoder.TryDecode(buf.ToArray(), out var data);

        Assert.True(ok);
        Assert.NotNull(data);
        Assert.Equal("LLDP", data!.Protocol);
        Assert.Equal("switch01.example.com", data.DeviceId); // System Name wins over Chassis MAC
        Assert.Equal("Gi1/0/24", data.PortId);
        Assert.Equal(120, data.TimeToLive);
        Assert.Equal("Cisco IOS Software, C3750", data.Platform);
        Assert.Equal("Bridge, Router", data.Capabilities);
        Assert.Equal("10.0.0.1", data.ManagementAddress);
    }

    [Fact]
    public void Decodes_ChassisMacWhenNoSystemName()
    {
        var buf = FrameBuilder.LldpEthernetHeader();
        FrameBuilder.AddLldpTlv(buf, 1, FrameBuilder.IdField(0x04, new byte[] { 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF }));
        FrameBuilder.AddLldpTlv(buf, 3, new byte[] { 0x00, 0x3C });
        FrameBuilder.AddLldpTlv(buf, 0, System.Array.Empty<byte>());

        bool ok = _decoder.TryDecode(buf.ToArray(), out var data);

        Assert.True(ok);
        Assert.Equal("AA:BB:CC:DD:EE:FF", data!.DeviceId);
        Assert.Equal(60, data.TimeToLive);
    }

    [Fact]
    public void PortDescription_UsedWhenPortIdMissing()
    {
        var buf = FrameBuilder.LldpEthernetHeader();
        FrameBuilder.AddLldpTlv(buf, 1, FrameBuilder.IdField(0x07, FrameBuilder.Ascii("dev")));
        FrameBuilder.AddLldpTlv(buf, 4, FrameBuilder.Ascii("Uplink to core")); // Port Description
        FrameBuilder.AddLldpTlv(buf, 0, System.Array.Empty<byte>());

        bool ok = _decoder.TryDecode(buf.ToArray(), out var data);

        Assert.True(ok);
        Assert.Equal("Uplink to core", data!.PortId);
    }

    [Fact]
    public void Rejects_NonLldpEthertype()
    {
        var buf = FrameBuilder.LldpEthernetHeader(ethertype: 0x0800); // IPv4, not LLDP
        FrameBuilder.AddLldpTlv(buf, 1, FrameBuilder.IdField(0x07, FrameBuilder.Ascii("x")));

        Assert.False(_decoder.TryDecode(buf.ToArray(), out var data));
        Assert.Null(data);
    }

    [Fact]
    public void Rejects_FrameWithNoNeighbourInfo()
    {
        // Valid LLDP ethertype but only a TTL TLV - nothing identifying.
        var buf = FrameBuilder.LldpEthernetHeader();
        FrameBuilder.AddLldpTlv(buf, 3, new byte[] { 0x00, 0x78 });
        FrameBuilder.AddLldpTlv(buf, 0, System.Array.Empty<byte>());

        Assert.False(_decoder.TryDecode(buf.ToArray(), out var data));
        Assert.Null(data);
    }

    [Fact]
    public void Rejects_TooShortFrame()
    {
        Assert.False(_decoder.TryDecode(new byte[] { 0x01, 0x02, 0x03 }, out var data));
        Assert.Null(data);
    }

    [Fact]
    public void Handles_TruncatedTlvGracefully()
    {
        var buf = FrameBuilder.LldpEthernetHeader();
        FrameBuilder.AddLldpTlv(buf, 1, FrameBuilder.IdField(0x07, FrameBuilder.Ascii("sw")));
        FrameBuilder.AddLldpTlv(buf, 2, FrameBuilder.IdField(0x07, FrameBuilder.Ascii("Gi0/1")));
        // Append a TLV header claiming more bytes than are present.
        int header = (6 << 9) | 0x1FF; // System Description, length 511, but no payload
        buf.Add((byte)(header >> 8));
        buf.Add((byte)(header & 0xFF));

        bool ok = _decoder.TryDecode(buf.ToArray(), out var data);

        // Should keep what was parsed before the truncated TLV.
        Assert.True(ok);
        Assert.Equal("sw", data!.DeviceId);
        Assert.Equal("Gi0/1", data.PortId);
    }

    [Fact]
    public void Returns_FalseForNull()
    {
        Assert.False(_decoder.TryDecode(null!, out var data));
        Assert.Null(data);
    }
}
