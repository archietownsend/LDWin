using System.Collections.Generic;
using System.Text;

namespace LDWin.Tests;

/// <summary>
/// Helpers for hand-constructing raw CDP and LLDP Ethernet frames so the decoders
/// can be exercised without live capture hardware. The byte layouts here mirror
/// the wire formats the decoders parse (see CdpDecoder / LldpDecoder doc-comments).
/// </summary>
internal static class FrameBuilder
{
    // ---- Ethernet ----
    private static readonly byte[] LldpMulticast = { 0x01, 0x80, 0xC2, 0x00, 0x00, 0x0E };
    private static readonly byte[] CdpMulticast = { 0x01, 0x00, 0x0C, 0xCC, 0xCC, 0xCC };
    private static readonly byte[] SampleSrcMac = { 0x00, 0x11, 0x22, 0x33, 0x44, 0x55 };

    internal static byte[] Ascii(string s) => Encoding.ASCII.GetBytes(s);

    // ===================== LLDP =====================

    /// <summary>Appends an LLDP TLV: 7-bit type + 9-bit length header, then the value.</summary>
    internal static void AddLldpTlv(List<byte> buf, int type, byte[] value)
    {
        int header = ((type & 0x7F) << 9) | (value.Length & 0x1FF);
        buf.Add((byte)(header >> 8));
        buf.Add((byte)(header & 0xFF));
        buf.AddRange(value);
    }

    /// <summary>An LLDP Ethernet header (dest = LLDP multicast, ethertype 0x88CC).</summary>
    internal static List<byte> LldpEthernetHeader(byte[]? destMac = null, int ethertype = 0x88CC)
    {
        var buf = new List<byte>();
        buf.AddRange(destMac ?? LldpMulticast);
        buf.AddRange(SampleSrcMac);
        buf.Add((byte)(ethertype >> 8));
        buf.Add((byte)(ethertype & 0xFF));
        return buf;
    }

    /// <summary>Chassis-ID / Port-ID value = 1-byte subtype + id bytes.</summary>
    internal static byte[] IdField(byte subtype, byte[] id)
    {
        var v = new byte[id.Length + 1];
        v[0] = subtype;
        id.CopyTo(v, 1);
        return v;
    }

    /// <summary>LLDP Management Address TLV value for an IPv4 address.</summary>
    internal static byte[] LldpMgmtAddressIpv4(byte[] ipv4)
    {
        var v = new List<byte>
        {
            (byte)(ipv4.Length + 1), // address string length = subtype + address
            0x01,                    // address subtype 1 = IPv4
        };
        v.AddRange(ipv4);
        v.Add(0x02);                 // interface numbering subtype
        v.AddRange(new byte[] { 0x00, 0x00, 0x00, 0x01 }); // interface number
        v.Add(0x00);                 // OID length 0
        return v.ToArray();
    }

    // ===================== CDP =====================

    /// <summary>Appends a CDP TLV: type (2 BE), length (2 BE, INCLUDES 4-byte header), value.</summary>
    internal static void AddCdpTlv(List<byte> buf, int type, byte[] value)
    {
        int len = value.Length + 4;
        buf.Add((byte)(type >> 8));
        buf.Add((byte)(type & 0xFF));
        buf.Add((byte)(len >> 8));
        buf.Add((byte)(len & 0xFF));
        buf.AddRange(value);
    }

    /// <summary>
    /// A CDP 802.3 + LLC/SNAP + CDP header. <paramref name="snapPid"/> defaults to the
    /// CDP PID 0x2000; pass a different value to simulate a non-CDP SNAP frame.
    /// </summary>
    internal static List<byte> CdpHeader(byte ttl = 180, byte[]? destMac = null, int snapPid = 0x2000)
    {
        var buf = new List<byte>();
        buf.AddRange(destMac ?? CdpMulticast); // dest MAC
        buf.AddRange(SampleSrcMac);            // src MAC
        buf.AddRange(new byte[] { 0x00, 0x00 }); // 802.3 length (decoder ignores)
        buf.AddRange(new byte[] { 0xAA, 0xAA, 0x03 }); // LLC
        buf.AddRange(new byte[] { 0x00, 0x00, 0x0C }); // SNAP OUI
        buf.Add((byte)(snapPid >> 8));
        buf.Add((byte)(snapPid & 0xFF));
        buf.Add(0x02);  // CDP version
        buf.Add(ttl);   // CDP TTL
        buf.AddRange(new byte[] { 0x00, 0x00 }); // checksum
        return buf;
    }

    /// <summary>CDP Addresses TLV value carrying a single IPv4 address.</summary>
    internal static byte[] CdpAddressesIpv4(byte[] ipv4)
    {
        var v = new List<byte>
        {
            0x00, 0x00, 0x00, 0x01, // number of addresses = 1
            0x01,                   // protocol type = 1 (NLPID)
            0x01,                   // protocol length = 1
            0xCC,                   // protocol = IP
            0x00, 0x04,             // address length = 4
        };
        v.AddRange(ipv4);
        return v.ToArray();
    }
}
