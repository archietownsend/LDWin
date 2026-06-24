using System.Collections.Generic;
using LDWin.Protocols;
using Xunit;

namespace LDWin.Tests;

public class CdpDecoderTests
{
    private readonly CdpDecoder _decoder = new();

    [Fact]
    public void Protocol_IsCdp() => Assert.Equal("CDP", _decoder.Protocol);

    [Fact]
    public void Decodes_FullFrame_AllFieldsPopulated()
    {
        var buf = FrameBuilder.CdpHeader(ttl: 180);
        FrameBuilder.AddCdpTlv(buf, 0x0001, FrameBuilder.Ascii("Switch-Core"));          // Device ID
        FrameBuilder.AddCdpTlv(buf, 0x0002, FrameBuilder.CdpAddressesIpv4(new byte[] { 192, 168, 1, 1 })); // Addresses
        FrameBuilder.AddCdpTlv(buf, 0x0003, FrameBuilder.Ascii("GigabitEthernet0/1"));   // Port ID
        FrameBuilder.AddCdpTlv(buf, 0x0004, new byte[] { 0x00, 0x00, 0x00, 0x09 });      // Capabilities: Router + Switch
        FrameBuilder.AddCdpTlv(buf, 0x0005, FrameBuilder.Ascii("Cisco IOS 15.0"));       // Software Version
        FrameBuilder.AddCdpTlv(buf, 0x0006, FrameBuilder.Ascii("cisco WS-C3750"));       // Platform
        FrameBuilder.AddCdpTlv(buf, 0x000A, new byte[] { 0x00, 0x0A });                  // Native VLAN = 10

        bool ok = _decoder.TryDecode(buf.ToArray(), out var data);

        Assert.True(ok);
        Assert.NotNull(data);
        Assert.Equal("CDP", data!.Protocol);
        Assert.Equal(180, data.TimeToLive);
        Assert.Equal("Switch-Core", data.DeviceId);
        Assert.Equal("192.168.1.1", data.ManagementAddress);
        Assert.Equal("GigabitEthernet0/1", data.PortId);
        Assert.Equal("Router, Switch", data.Capabilities);
        Assert.Equal("Cisco IOS 15.0", data.SoftwareVersion);
        Assert.Equal("cisco WS-C3750", data.Platform);
        Assert.Equal("10", data.Vlan);
    }

    [Fact]
    public void Decodes_MinimalFrame_DeviceAndPortOnly()
    {
        var buf = FrameBuilder.CdpHeader(ttl: 60);
        FrameBuilder.AddCdpTlv(buf, 0x0001, FrameBuilder.Ascii("Edge-SW"));
        FrameBuilder.AddCdpTlv(buf, 0x0003, FrameBuilder.Ascii("Fa0/24"));

        bool ok = _decoder.TryDecode(buf.ToArray(), out var data);

        Assert.True(ok);
        Assert.Equal("Edge-SW", data!.DeviceId);
        Assert.Equal("Fa0/24", data.PortId);
        Assert.Equal(60, data.TimeToLive);
    }

    [Fact]
    public void Rejects_WrongDestinationMac()
    {
        var buf = FrameBuilder.CdpHeader(destMac: new byte[] { 0x01, 0x80, 0xC2, 0x00, 0x00, 0x0E });
        FrameBuilder.AddCdpTlv(buf, 0x0001, FrameBuilder.Ascii("Switch"));

        Assert.False(_decoder.TryDecode(buf.ToArray(), out var data));
        Assert.Null(data);
    }

    [Fact]
    public void Rejects_NonCdpSnapPid()
    {
        var buf = FrameBuilder.CdpHeader(snapPid: 0x0800);
        FrameBuilder.AddCdpTlv(buf, 0x0001, FrameBuilder.Ascii("Switch"));

        Assert.False(_decoder.TryDecode(buf.ToArray(), out var data));
        Assert.Null(data);
    }

    [Fact]
    public void Rejects_FrameWithNoNeighbourInfo()
    {
        // Valid CDP framing but only a Native VLAN TLV - nothing identifying.
        var buf = FrameBuilder.CdpHeader();
        FrameBuilder.AddCdpTlv(buf, 0x000A, new byte[] { 0x00, 0x0A });

        Assert.False(_decoder.TryDecode(buf.ToArray(), out var data));
        Assert.Null(data);
    }

    [Fact]
    public void Rejects_TooShortFrame()
    {
        Assert.False(_decoder.TryDecode(new byte[] { 0x01, 0x00, 0x0C }, out var data));
        Assert.Null(data);
    }

    [Fact]
    public void Handles_TruncatedTlvGracefully()
    {
        var buf = FrameBuilder.CdpHeader();
        FrameBuilder.AddCdpTlv(buf, 0x0001, FrameBuilder.Ascii("CoreSW"));
        FrameBuilder.AddCdpTlv(buf, 0x0003, FrameBuilder.Ascii("Te1/1/1"));
        // TLV header claiming a length that runs past the end of the frame.
        buf.AddRange(new byte[] { 0x00, 0x06, 0x00, 0xFF }); // Platform, len 255, no payload

        bool ok = _decoder.TryDecode(buf.ToArray(), out var data);

        Assert.True(ok);
        Assert.Equal("CoreSW", data!.DeviceId);
        Assert.Equal("Te1/1/1", data.PortId);
    }

    [Fact]
    public void Returns_FalseForNull()
    {
        Assert.False(_decoder.TryDecode(null!, out var data));
        Assert.Null(data);
    }
}
