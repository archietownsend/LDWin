using System.Collections.Generic;
using System.Text;
using LDWin.Protocols;
using Xunit;

namespace LDWin.Tests;

/// <summary>
/// Covers the richer TLVs added on top of the core set: LLDP 802.1 VLAN Name /
/// Port VLAN ID, and CDP VTP domain / duplex.
/// </summary>
public class ExtendedTlvTests
{
    private readonly LldpDecoder _lldp = new();
    private readonly CdpDecoder _cdp = new();

    private static byte[] Lldp8021VlanName(int vid, string name)
    {
        var v = new List<byte> { 0x00, 0x80, 0xC2, 0x03 }; // OUI 802.1 + subtype 3
        v.Add((byte)(vid >> 8));
        v.Add((byte)(vid & 0xFF));
        var nb = Encoding.ASCII.GetBytes(name);
        v.Add((byte)nb.Length);
        v.AddRange(nb);
        return v.ToArray();
    }

    private static byte[] Lldp8021Pvid(int pvid) =>
        new byte[] { 0x00, 0x80, 0xC2, 0x01, (byte)(pvid >> 8), (byte)(pvid & 0xFF) };

    [Fact]
    public void Lldp_DecodesVlanNameAndPvid()
    {
        var buf = FrameBuilder.LldpEthernetHeader();
        FrameBuilder.AddLldpTlv(buf, 1, FrameBuilder.IdField(0x07, FrameBuilder.Ascii("sw")));
        FrameBuilder.AddLldpTlv(buf, 2, FrameBuilder.IdField(0x07, FrameBuilder.Ascii("Gi0/1")));
        FrameBuilder.AddLldpTlv(buf, 127, Lldp8021Pvid(20));
        FrameBuilder.AddLldpTlv(buf, 127, Lldp8021VlanName(100, "VOICE"));
        FrameBuilder.AddLldpTlv(buf, 0, System.Array.Empty<byte>());

        bool ok = _lldp.TryDecode(buf.ToArray(), out var data);

        Assert.True(ok);
        Assert.Equal("20", data!.PortVlanId);
        Assert.Equal("100 (VOICE)", data.VlanName);
        Assert.Contains(data.RawTlvs, s => s.Contains("Port VLAN ID") && s.Contains("20"));
        Assert.Contains(data.RawTlvs, s => s.Contains("VLAN Name") && s.Contains("VOICE"));
    }

    [Fact]
    public void Lldp_VlanNameWithoutName_FallsBackToId()
    {
        var buf = FrameBuilder.LldpEthernetHeader();
        FrameBuilder.AddLldpTlv(buf, 2, FrameBuilder.IdField(0x07, FrameBuilder.Ascii("Te1/1")));
        FrameBuilder.AddLldpTlv(buf, 127, Lldp8021VlanName(42, ""));
        FrameBuilder.AddLldpTlv(buf, 0, System.Array.Empty<byte>());

        Assert.True(_lldp.TryDecode(buf.ToArray(), out var data));
        Assert.Equal("42", data!.VlanName);
    }

    [Fact]
    public void Lldp_UnknownOrgTlv_RecordedRawOnly()
    {
        var buf = FrameBuilder.LldpEthernetHeader();
        FrameBuilder.AddLldpTlv(buf, 2, FrameBuilder.IdField(0x07, FrameBuilder.Ascii("p1")));
        // Some other vendor OUI/subtype - must not throw, just appear in raw list.
        FrameBuilder.AddLldpTlv(buf, 127, new byte[] { 0xAA, 0xBB, 0xCC, 0x09, 0x01, 0x02 });
        FrameBuilder.AddLldpTlv(buf, 0, System.Array.Empty<byte>());

        Assert.True(_lldp.TryDecode(buf.ToArray(), out var data));
        Assert.Equal("", data!.VlanName);
        Assert.Contains(data.RawTlvs, s => s.Contains("AA-BB-CC"));
    }

    [Fact]
    public void Cdp_DecodesVtpDomainAndDuplex()
    {
        var buf = FrameBuilder.CdpHeader(ttl: 180);
        FrameBuilder.AddCdpTlv(buf, 0x0001, FrameBuilder.Ascii("Core-SW"));
        FrameBuilder.AddCdpTlv(buf, 0x0003, FrameBuilder.Ascii("Gi1/0/1"));
        FrameBuilder.AddCdpTlv(buf, 0x0009, FrameBuilder.Ascii("CORP"));   // VTP domain
        FrameBuilder.AddCdpTlv(buf, 0x000B, new byte[] { 0x01 });          // Duplex = full

        bool ok = _cdp.TryDecode(buf.ToArray(), out var data);

        Assert.True(ok);
        Assert.Equal("CORP", data!.VtpDomain);
        Assert.Equal("Full", data.Duplex);
        Assert.Contains(data.RawTlvs, s => s.Contains("VTP Domain") && s.Contains("CORP"));
        Assert.Contains(data.RawTlvs, s => s.Contains("Duplex") && s.Contains("Full"));
    }

    [Fact]
    public void Cdp_HalfDuplex()
    {
        var buf = FrameBuilder.CdpHeader();
        FrameBuilder.AddCdpTlv(buf, 0x0001, FrameBuilder.Ascii("SW"));
        FrameBuilder.AddCdpTlv(buf, 0x000B, new byte[] { 0x00 });

        Assert.True(_cdp.TryDecode(buf.ToArray(), out var data));
        Assert.Equal("Half", data!.Duplex);
    }
}
