using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using LDWin.Models;

namespace LDWin.Protocols;

/// <summary>
/// Decodes Cisco Discovery Protocol (CDP) frames into <see cref="LinkData"/>.
///
/// Hand-rolled parser over the raw frame (PacketDotNet 1.4.7's CDP support is
/// not relied upon). CDP rides on an IEEE 802.3 frame with an LLC/SNAP header:
///
///   Ethernet 802.3 header (14 bytes):
///     dest MAC (6) = 01:00:0C:CC:CC:CC
///     src  MAC (6)
///     length   (2)
///   LLC (3 bytes): DSAP 0xAA, SSAP 0xAA, Control 0x03
///   SNAP (5 bytes): OUI 00:00:0C, PID 0x2000 (CDP)
///   CDP header (4 bytes): version (1), ttl (1), checksum (2)
///   TLVs: type (2 BE), length (2 BE, INCLUDES the 4-byte TLV header),
///         value (length - 4 bytes)
/// </summary>
public sealed class CdpDecoder : ILinkDecoder
{
    public string Protocol => "CDP";

    // CDP TLV types.
    private const int TlvDeviceId = 0x0001;
    private const int TlvAddresses = 0x0002;
    private const int TlvPortId = 0x0003;
    private const int TlvCapabilities = 0x0004;
    private const int TlvSoftwareVersion = 0x0005;
    private const int TlvPlatform = 0x0006;
    private const int TlvVtpDomain = 0x0009;
    private const int TlvNativeVlan = 0x000A;
    private const int TlvDuplex = 0x000B;

    private const int EthernetHeaderLength = 14;
    private const int LlcSnapLength = 8;   // LLC(3) + SNAP(5)
    private const int CdpHeaderLength = 4; // version(1) + ttl(1) + checksum(2)

    private static readonly byte[] CdpDestMac = { 0x01, 0x00, 0x0C, 0xCC, 0xCC, 0xCC };

    public bool TryDecode(byte[] frame, out LinkData? data)
    {
        data = null;
        try
        {
            int minLength = EthernetHeaderLength + LlcSnapLength + CdpHeaderLength;
            if (frame is null || frame.Length < minLength)
            {
                return false;
            }

            // Destination MAC must be the CDP multicast address.
            for (int i = 0; i < CdpDestMac.Length; i++)
            {
                if (frame[i] != CdpDestMac[i])
                {
                    return false;
                }
            }

            // LLC: DSAP 0xAA, SSAP 0xAA, Control 0x03.
            int llcOffset = EthernetHeaderLength;
            if (frame[llcOffset] != 0xAA || frame[llcOffset + 1] != 0xAA || frame[llcOffset + 2] != 0x03)
            {
                return false;
            }

            // SNAP: OUI 00:00:0C, PID 0x2000.
            int snapOffset = llcOffset + 3;
            if (frame[snapOffset] != 0x00 || frame[snapOffset + 1] != 0x00 || frame[snapOffset + 2] != 0x0C)
            {
                return false;
            }

            int pid = (frame[snapOffset + 3] << 8) | frame[snapOffset + 4];
            if (pid != 0x2000)
            {
                return false;
            }

            var link = new LinkData { Protocol = "CDP" };

            // CDP header.
            int cdpHeaderOffset = EthernetHeaderLength + LlcSnapLength;
            // version = frame[cdpHeaderOffset] (unused beyond validation)
            link.TimeToLive = frame[cdpHeaderOffset + 1];

            int offset = cdpHeaderOffset + CdpHeaderLength;
            while (offset + 4 <= frame.Length)
            {
                int type = (frame[offset] << 8) | frame[offset + 1];
                int tlvLength = (frame[offset + 2] << 8) | frame[offset + 3];

                // tlvLength includes the 4-byte header.
                if (tlvLength < 4 || offset + tlvLength > frame.Length)
                {
                    break;
                }

                int valueOffset = offset + 4;
                int valueLength = tlvLength - 4;

                switch (type)
                {
                    case TlvDeviceId:
                        link.DeviceId = DecodeString(frame, valueOffset, valueLength);
                        link.RawTlvs.Add($"Device ID: {link.DeviceId}");
                        break;

                    case TlvAddresses:
                        link.ManagementAddress = ParseAddresses(frame, valueOffset, valueLength);
                        link.RawTlvs.Add($"Addresses: {link.ManagementAddress}");
                        break;

                    case TlvPortId:
                        link.PortId = DecodeString(frame, valueOffset, valueLength);
                        link.RawTlvs.Add($"Port ID: {link.PortId}");
                        break;

                    case TlvCapabilities:
                        if (valueLength >= 4)
                        {
                            int caps = (frame[valueOffset] << 24)
                                       | (frame[valueOffset + 1] << 16)
                                       | (frame[valueOffset + 2] << 8)
                                       | frame[valueOffset + 3];
                            link.Capabilities = DecodeCapabilities(caps);
                        }
                        link.RawTlvs.Add($"Capabilities: {link.Capabilities}");
                        break;

                    case TlvSoftwareVersion:
                        link.SoftwareVersion = DecodeString(frame, valueOffset, valueLength);
                        link.RawTlvs.Add($"Software Version: {link.SoftwareVersion}");
                        break;

                    case TlvPlatform:
                        link.Platform = DecodeString(frame, valueOffset, valueLength);
                        link.RawTlvs.Add($"Platform: {link.Platform}");
                        break;

                    case TlvVtpDomain:
                        link.VtpDomain = DecodeString(frame, valueOffset, valueLength);
                        link.RawTlvs.Add($"VTP Domain: {link.VtpDomain}");
                        break;

                    case TlvNativeVlan:
                        if (valueLength >= 2)
                        {
                            int vlan = (frame[valueOffset] << 8) | frame[valueOffset + 1];
                            link.Vlan = vlan.ToString(CultureInfo.InvariantCulture);
                        }
                        link.RawTlvs.Add($"Native VLAN: {link.Vlan}");
                        break;

                    case TlvDuplex:
                        if (valueLength >= 1)
                        {
                            link.Duplex = frame[valueOffset] != 0 ? "Full" : "Half";
                        }
                        link.RawTlvs.Add($"Duplex: {link.Duplex}");
                        break;

                    default:
                        link.RawTlvs.Add($"TLV 0x{type:X4}: {valueLength} bytes");
                        break;
                }

                offset += tlvLength;
            }

            if (!link.HasNeighbourInfo)
            {
                return false;
            }

            data = link;
            return true;
        }
        catch
        {
            data = null;
            return false;
        }
    }

    /// <summary>
    /// Addresses TLV value layout:
    ///   number-of-addresses (4 BE)
    ///   per address:
    ///     protocol-type   (1)
    ///     protocol-length (1)
    ///     protocol        (protocol-length bytes)  e.g. 0xCC = IP
    ///     address-length  (2 BE)
    ///     address         (address-length bytes)
    /// Returns the first IPv4 address as a dotted quad, else the first address as hex.
    /// </summary>
    private static string ParseAddresses(byte[] frame, int valueOffset, int valueLength)
    {
        if (valueLength < 4)
        {
            return string.Empty;
        }

        int end = valueOffset + valueLength;
        int count = (frame[valueOffset] << 24)
                    | (frame[valueOffset + 1] << 16)
                    | (frame[valueOffset + 2] << 8)
                    | frame[valueOffset + 3];

        int pos = valueOffset + 4;
        string? firstAny = null;

        for (int i = 0; i < count; i++)
        {
            if (pos + 2 > end)
            {
                break;
            }

            int protocolType = frame[pos];
            int protocolLength = frame[pos + 1];
            pos += 2;

            if (pos + protocolLength + 2 > end)
            {
                break;
            }

            // protocol field (e.g. 0xCC for IP under NLPID, type 1).
            int protocolValue = 0;
            for (int p = 0; p < protocolLength; p++)
            {
                protocolValue = (protocolValue << 8) | frame[pos + p];
            }

            pos += protocolLength;

            int addressLength = (frame[pos] << 8) | frame[pos + 1];
            pos += 2;

            if (pos + addressLength > end)
            {
                break;
            }

            int addrOffset = pos;
            pos += addressLength;

            // IPv4: NLPID protocol-type 1 with protocol 0xCC, address length 4.
            bool isIpv4 = addressLength == 4 && (protocolType == 1 && protocolValue == 0xCC);
            if (isIpv4)
            {
                return string.Join(
                    ".",
                    frame[addrOffset],
                    frame[addrOffset + 1],
                    frame[addrOffset + 2],
                    frame[addrOffset + 3]);
            }

            // Also treat a bare 4-byte address as IPv4 if protocol hints are absent.
            if (firstAny == null && addressLength == 4)
            {
                firstAny = string.Join(
                    ".",
                    frame[addrOffset],
                    frame[addrOffset + 1],
                    frame[addrOffset + 2],
                    frame[addrOffset + 3]);
            }
            else if (firstAny == null && addressLength > 0)
            {
                firstAny = FormatHex(frame, addrOffset, addressLength);
            }
        }

        return firstAny ?? string.Empty;
    }

    private static string DecodeCapabilities(int bitmap)
    {
        var names = new (int Bit, string Name)[]
        {
            (0x01, "Router"),
            (0x02, "Transparent Bridge"),
            (0x04, "Source Route Bridge"),
            (0x08, "Switch"),
            (0x10, "Host"),
            (0x20, "IGMP"),
            (0x40, "Repeater"),
        };

        var enabled = new List<string>();
        foreach (var (bit, name) in names)
        {
            if ((bitmap & bit) != 0)
            {
                enabled.Add(name);
            }
        }

        return string.Join(", ", enabled);
    }

    private static string DecodeString(byte[] frame, int offset, int length)
    {
        if (length <= 0)
        {
            return string.Empty;
        }

        return Encoding.UTF8.GetString(frame, offset, length).TrimEnd('\0').TrimEnd();
    }

    private static string FormatHex(byte[] frame, int offset, int length)
    {
        var sb = new StringBuilder(length * 3);
        for (int i = 0; i < length; i++)
        {
            if (i > 0)
            {
                sb.Append(':');
            }

            sb.Append(frame[offset + i].ToString("X2", CultureInfo.InvariantCulture));
        }

        return sb.ToString();
    }
}
