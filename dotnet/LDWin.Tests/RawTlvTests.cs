using System.Collections.Generic;
using LDWin.Protocols;
using Xunit;

namespace LDWin.Tests;

/// <summary>
/// Verifies that the CDP and LLDP decoders populate <see cref="LDWin.Models.LinkData.RawTlvs"/>
/// with human-readable "Name: value" entries in wire order.
/// </summary>
public class RawTlvTests
{
    private readonly CdpDecoder _cdp = new();
    private readonly LldpDecoder _lldp = new();

    // ===================== CDP =====================

    [Fact]
    public void Cdp_FullFrame_RawTlvs_ContainsKnownEntries()
    {
        var buf = FrameBuilder.CdpHeader(ttl: 180);
        FrameBuilder.AddCdpTlv(buf, 0x0001, FrameBuilder.Ascii("Switch-Core"));
        FrameBuilder.AddCdpTlv(buf, 0x0002, FrameBuilder.CdpAddressesIpv4(new byte[] { 192, 168, 1, 1 }));
        FrameBuilder.AddCdpTlv(buf, 0x0003, FrameBuilder.Ascii("GigabitEthernet0/1"));
        FrameBuilder.AddCdpTlv(buf, 0x0004, new byte[] { 0x00, 0x00, 0x00, 0x09 }); // Router + Switch
        FrameBuilder.AddCdpTlv(buf, 0x0005, FrameBuilder.Ascii("Cisco IOS 15.0"));
        FrameBuilder.AddCdpTlv(buf, 0x0006, FrameBuilder.Ascii("cisco WS-C3750"));
        FrameBuilder.AddCdpTlv(buf, 0x000A, new byte[] { 0x00, 0x0A }); // VLAN 10

        bool ok = _cdp.TryDecode(buf.ToArray(), out var data);

        Assert.True(ok);
        Assert.NotNull(data);
        Assert.NotEmpty(data!.RawTlvs);

        // Device ID
        Assert.Contains(data.RawTlvs, s => s.Contains("Switch-Core"));
        // Addresses / management IP
        Assert.Contains(data.RawTlvs, s => s.Contains("192.168.1.1"));
        // Port ID
        Assert.Contains(data.RawTlvs, s => s.Contains("GigabitEthernet0/1"));
        // Capabilities
        Assert.Contains(data.RawTlvs, s => s.Contains("Router") && s.Contains("Switch"));
        // Software Version
        Assert.Contains(data.RawTlvs, s => s.Contains("Cisco IOS 15.0"));
        // Platform
        Assert.Contains(data.RawTlvs, s => s.Contains("cisco WS-C3750"));
        // Native VLAN
        Assert.Contains(data.RawTlvs, s => s.Contains("10"));
    }

    [Fact]
    public void Cdp_RawTlvs_AreInWireOrder()
    {
        var buf = FrameBuilder.CdpHeader(ttl: 60);
        FrameBuilder.AddCdpTlv(buf, 0x0001, FrameBuilder.Ascii("DeviceFirst"));
        FrameBuilder.AddCdpTlv(buf, 0x0003, FrameBuilder.Ascii("PortSecond"));
        FrameBuilder.AddCdpTlv(buf, 0x0006, FrameBuilder.Ascii("PlatformThird"));

        bool ok = _cdp.TryDecode(buf.ToArray(), out var data);

        Assert.True(ok);
        Assert.NotNull(data);

        int deviceIdx = data!.RawTlvs.FindIndex(s => s.Contains("DeviceFirst"));
        int portIdx   = data.RawTlvs.FindIndex(s => s.Contains("PortSecond"));
        int platIdx   = data.RawTlvs.FindIndex(s => s.Contains("PlatformThird"));

        Assert.True(deviceIdx >= 0);
        Assert.True(portIdx   >= 0);
        Assert.True(platIdx   >= 0);
        Assert.True(deviceIdx < portIdx, "Device ID should appear before Port ID");
        Assert.True(portIdx   < platIdx, "Port ID should appear before Platform");
    }

    [Fact]
    public void Cdp_UnknownTlv_AppearsAsHexFallback()
    {
        var buf = FrameBuilder.CdpHeader();
        FrameBuilder.AddCdpTlv(buf, 0x0001, FrameBuilder.Ascii("SW1"));
        FrameBuilder.AddCdpTlv(buf, 0x0003, FrameBuilder.Ascii("Fa0/1"));
        // Unknown TLV type 0x00FF, 3 bytes of payload.
        FrameBuilder.AddCdpTlv(buf, 0x00FF, new byte[] { 0xDE, 0xAD, 0xBE });

        bool ok = _cdp.TryDecode(buf.ToArray(), out var data);

        Assert.True(ok);
        Assert.NotNull(data);
        // The unknown TLV should produce a fallback entry with the hex type and byte count.
        Assert.Contains(data!.RawTlvs, s => s.Contains("0x00FF") && s.Contains("3 bytes"));
    }

    // ===================== LLDP =====================

    [Fact]
    public void Lldp_FullFrame_RawTlvs_ContainsKnownEntries()
    {
        var buf = FrameBuilder.LldpEthernetHeader();
        // Chassis ID (MAC).
        FrameBuilder.AddLldpTlv(buf, 1, FrameBuilder.IdField(0x04, new byte[] { 0x00, 0x11, 0x22, 0x33, 0x44, 0x55 }));
        // Port ID.
        FrameBuilder.AddLldpTlv(buf, 2, FrameBuilder.IdField(0x07, FrameBuilder.Ascii("Gi1/0/24")));
        // TTL = 120.
        FrameBuilder.AddLldpTlv(buf, 3, new byte[] { 0x00, 0x78 });
        // System Name.
        FrameBuilder.AddLldpTlv(buf, 5, FrameBuilder.Ascii("switch01.example.com"));
        // System Description.
        FrameBuilder.AddLldpTlv(buf, 6, FrameBuilder.Ascii("Cisco IOS Software, C3750"));
        // System Capabilities: supported 0x0014, enabled 0x0014 (Bridge + Router).
        FrameBuilder.AddLldpTlv(buf, 7, new byte[] { 0x00, 0x14, 0x00, 0x14 });
        // Management Address.
        FrameBuilder.AddLldpTlv(buf, 8, FrameBuilder.LldpMgmtAddressIpv4(new byte[] { 10, 0, 0, 1 }));
        // End TLV.
        FrameBuilder.AddLldpTlv(buf, 0, System.Array.Empty<byte>());

        bool ok = _lldp.TryDecode(buf.ToArray(), out var data);

        Assert.True(ok);
        Assert.NotNull(data);
        Assert.NotEmpty(data!.RawTlvs);

        // Chassis ID (MAC)
        Assert.Contains(data.RawTlvs, s => s.Contains("00:11:22:33:44:55"));
        // TTL
        Assert.Contains(data.RawTlvs, s => s.Contains("120"));
        // System Name
        Assert.Contains(data.RawTlvs, s => s.Contains("switch01.example.com"));
        // System Description
        Assert.Contains(data.RawTlvs, s => s.Contains("Cisco IOS Software, C3750"));
        // Capabilities
        Assert.Contains(data.RawTlvs, s => s.Contains("Bridge") || s.Contains("Router"));
        // Management address
        Assert.Contains(data.RawTlvs, s => s.Contains("10.0.0.1"));
    }

    [Fact]
    public void Lldp_EndTlv_NotAddedToRawTlvs()
    {
        var buf = FrameBuilder.LldpEthernetHeader();
        FrameBuilder.AddLldpTlv(buf, 1, FrameBuilder.IdField(0x07, FrameBuilder.Ascii("chassis")));
        FrameBuilder.AddLldpTlv(buf, 2, FrameBuilder.IdField(0x07, FrameBuilder.Ascii("port")));
        FrameBuilder.AddLldpTlv(buf, 0, System.Array.Empty<byte>()); // End TLV

        bool ok = _lldp.TryDecode(buf.ToArray(), out var data);

        Assert.True(ok);
        Assert.NotNull(data);
        // End TLV (type 0) must not produce a RawTlvs entry.
        Assert.DoesNotContain(data!.RawTlvs, s => s.StartsWith("TLV type 0"));
    }

    [Fact]
    public void Lldp_UnknownTlv_AppearsAsTypeFallback()
    {
        var buf = FrameBuilder.LldpEthernetHeader();
        FrameBuilder.AddLldpTlv(buf, 1, FrameBuilder.IdField(0x07, FrameBuilder.Ascii("sw")));
        FrameBuilder.AddLldpTlv(buf, 2, FrameBuilder.IdField(0x07, FrameBuilder.Ascii("Gi0/1")));
        // IEEE 802.3 org-specific TLV (OUI 00:12:0F, subtype 1) with no info bytes.
        // ParseOrgSpecific recognises the 802.3 OUI but the subtype-1 handler needs
        // at least 1 info byte; the fall-through raw entry is "802.3 TLV subtype 1: 0 bytes".
        FrameBuilder.AddLldpTlv(buf, 127, new byte[] { 0x00, 0x12, 0x0F, 0x01 });
        FrameBuilder.AddLldpTlv(buf, 0, System.Array.Empty<byte>());

        bool ok = _lldp.TryDecode(buf.ToArray(), out var data);

        Assert.True(ok);
        Assert.NotNull(data);
        Assert.Contains(data!.RawTlvs, s => s.Contains("802.3") && s.Contains("subtype 1"));
    }
}
