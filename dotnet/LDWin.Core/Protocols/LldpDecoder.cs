using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using LDWin.Models;

namespace LDWin.Protocols;

/// <summary>
/// Decodes IEEE 802.1AB (LLDP) frames into <see cref="LinkData"/>.
///
/// This is a hand-rolled TLV parser rather than a PacketDotNet-based one.
/// PacketDotNet 1.4.7's LLDP type surface (class names, TLV accessors,
/// sub-type enums) is not guaranteed to be stable / present, so to keep the
/// decoder reliable we parse the raw byte stream directly:
///   - 14-byte Ethernet header, ethertype at bytes [12..13] must be 0x88CC.
///   - A sequence of TLVs, each prefixed with a 2-byte big-endian header:
///       type   = (header >> 9) & 0x7F   (7 bits)
///       length = header & 0x1FF         (9 bits)
///     followed by <length> bytes of value.
///   - TLV type 0 (length 0) marks the end of the LLDPDU.
/// </summary>
public sealed class LldpDecoder : ILinkDecoder
{
    public string Protocol => "LLDP";

    // LLDP TLV types.
    private const int TlvEnd = 0;
    private const int TlvChassisId = 1;
    private const int TlvPortId = 2;
    private const int TlvTtl = 3;
    private const int TlvPortDescription = 4;
    private const int TlvSystemName = 5;
    private const int TlvSystemDescription = 6;
    private const int TlvSystemCapabilities = 7;
    private const int TlvManagementAddress = 8;
    private const int TlvOrgSpecific = 127;

    private const int EthernetHeaderLength = 14;

    public bool TryDecode(byte[] frame, out LinkData? data)
    {
        data = null;
        try
        {
            if (frame is null || frame.Length < EthernetHeaderLength + 2)
            {
                return false;
            }

            // Verify the LLDP ethertype (0x88CC) at the standard offset.
            if (frame[12] != 0x88 || frame[13] != 0xCC)
            {
                return false;
            }

            var link = new LinkData { Protocol = "LLDP" };

            // System Name overrides a chassis-derived DeviceId; track whether we
            // already set DeviceId from the System Name TLV so Chassis ID does
            // not clobber it.
            bool deviceIdFromSystemName = false;

            int offset = EthernetHeaderLength;
            while (offset + 2 <= frame.Length)
            {
                int header = (frame[offset] << 8) | frame[offset + 1];
                int type = (header >> 9) & 0x7F;
                int length = header & 0x1FF;
                offset += 2;

                if (type == TlvEnd)
                {
                    break;
                }

                if (offset + length > frame.Length)
                {
                    // Truncated TLV - stop parsing but keep what we have.
                    break;
                }

                int valueOffset = offset;
                offset += length;

                switch (type)
                {
                    case TlvChassisId:
                        if (!deviceIdFromSystemName && length >= 1)
                        {
                            link.DeviceId = DecodeIdField(frame, valueOffset, length);
                        }
                        // Always record the raw chassis value, even if System Name overrides DeviceId.
                        string chassisValue = length >= 1 ? DecodeIdField(frame, valueOffset, length) : string.Empty;
                        link.RawTlvs.Add($"Chassis ID: {chassisValue}");
                        break;

                    case TlvPortId:
                        if (length >= 1)
                        {
                            link.PortId = DecodeIdField(frame, valueOffset, length);
                        }
                        link.RawTlvs.Add($"Port ID: {link.PortId}");
                        break;

                    case TlvTtl:
                        if (length >= 2)
                        {
                            link.TimeToLive = (frame[valueOffset] << 8) | frame[valueOffset + 1];
                        }
                        link.RawTlvs.Add($"TTL: {link.TimeToLive}");
                        break;

                    case TlvPortDescription:
                        string portDesc = length > 0 ? DecodeString(frame, valueOffset, length) : string.Empty;
                        if (string.IsNullOrEmpty(link.PortId) && length > 0)
                        {
                            link.PortId = portDesc;
                        }
                        link.RawTlvs.Add($"Port Description: {portDesc}");
                        break;

                    case TlvSystemName:
                        if (length > 0)
                        {
                            link.DeviceId = DecodeString(frame, valueOffset, length);
                            deviceIdFromSystemName = true;
                        }
                        link.RawTlvs.Add($"System Name: {link.DeviceId}");
                        break;

                    case TlvSystemDescription:
                        if (length > 0)
                        {
                            link.Platform = DecodeString(frame, valueOffset, length);
                        }
                        link.RawTlvs.Add($"System Description: {link.Platform}");
                        break;

                    case TlvSystemCapabilities:
                        // 2 bytes of supported caps, 2 bytes of enabled caps.
                        if (length >= 4)
                        {
                            int enabled = (frame[valueOffset + 2] << 8) | frame[valueOffset + 3];
                            link.Capabilities = DecodeCapabilities(enabled);
                        }
                        link.RawTlvs.Add($"Capabilities: {link.Capabilities}");
                        break;

                    case TlvManagementAddress:
                        ParseManagementAddress(frame, valueOffset, length, link);
                        link.RawTlvs.Add($"Management Address: {link.ManagementAddress}");
                        break;

                    case TlvOrgSpecific:
                        ParseOrgSpecific(frame, valueOffset, length, link);
                        break;

                    default:
                        link.RawTlvs.Add($"TLV type {type}: {length} bytes");
                        break;
                }
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
    /// Decodes a Chassis-ID / Port-ID field. The first byte is the subtype.
    /// Subtypes that carry a MAC address (chassis subtype 4, port subtype 3)
    /// are rendered as colon-separated hex; everything else is treated as a
    /// printable string, falling back to hex if it is not printable.
    /// </summary>
    private static string DecodeIdField(byte[] frame, int valueOffset, int length)
    {
        int subtype = frame[valueOffset];
        int dataOffset = valueOffset + 1;
        int dataLength = length - 1;

        if (dataLength <= 0)
        {
            return string.Empty;
        }

        // MAC address subtypes: chassis-id subtype 4 (MAC), port-id subtype 3 (MAC).
        bool looksLikeMac = (subtype == 4 || subtype == 3) && dataLength == 6;
        if (looksLikeMac)
        {
            return FormatMac(frame, dataOffset, dataLength);
        }

        // Otherwise prefer a string rendering; if it has non-printable bytes
        // (e.g. a network-address subtype) fall back to hex.
        if (IsMostlyPrintable(frame, dataOffset, dataLength))
        {
            return DecodeString(frame, dataOffset, dataLength);
        }

        if (dataLength == 6)
        {
            return FormatMac(frame, dataOffset, dataLength);
        }

        return FormatHex(frame, dataOffset, dataLength);
    }

    /// <summary>
    /// Management Address TLV layout:
    ///   address-string-length (1)  = 1 (subtype) + N (address)
    ///   address-subtype       (1)  IANA family: 1 = IPv4, 2 = IPv6
    ///   address               (N)
    ///   interface-numbering-subtype (1)
    ///   interface-number      (4)
    ///   OID-length            (1)
    ///   OID                   (...)
    /// </summary>
    private static void ParseManagementAddress(byte[] frame, int valueOffset, int length, LinkData link)
    {
        if (length < 1)
        {
            return;
        }

        int addrStrLen = frame[valueOffset];
        if (addrStrLen < 1 || 1 + addrStrLen > length)
        {
            return;
        }

        int addrSubtype = frame[valueOffset + 1];
        int addrOffset = valueOffset + 2;
        int addrLen = addrStrLen - 1;

        if (addrSubtype == 1 && addrLen == 4)
        {
            link.ManagementAddress = string.Join(
                ".",
                frame[addrOffset],
                frame[addrOffset + 1],
                frame[addrOffset + 2],
                frame[addrOffset + 3]);
        }
        else if (addrSubtype == 2 && addrLen == 16)
        {
            var sb = new StringBuilder(addrLen * 3);
            for (int i = 0; i < addrLen; i++)
            {
                if (i > 0 && (i % 2) == 0)
                {
                    sb.Append(':');
                }

                sb.Append(frame[addrOffset + i].ToString("x2", CultureInfo.InvariantCulture));
            }

            link.ManagementAddress = sb.ToString();
        }
        else if (addrLen > 0)
        {
            link.ManagementAddress = FormatHex(frame, addrOffset, addrLen);
        }
    }

    /// <summary>
    /// Organizationally-specific TLV (type 127): 3-byte OUI + 1-byte subtype + info.
    /// Decodes the common IEEE 802.1 extensions (Port VLAN ID and VLAN Name); other
    /// OUIs/subtypes are recorded in the raw list only.
    /// </summary>
    private static void ParseOrgSpecific(byte[] frame, int valueOffset, int length, LinkData link)
    {
        if (length < 4)
        {
            link.RawTlvs.Add($"Org-specific TLV: {length} bytes");
            return;
        }

        bool is8021 = frame[valueOffset] == 0x00 && frame[valueOffset + 1] == 0x80 && frame[valueOffset + 2] == 0xC2;
        bool is8023 = frame[valueOffset] == 0x00 && frame[valueOffset + 1] == 0x12 && frame[valueOffset + 2] == 0x0F;
        int subtype = frame[valueOffset + 3];
        int infoOffset = valueOffset + 4;
        int infoLen = length - 4;

        // IEEE 802.1, subtype 1: Port VLAN ID (PVID).
        if (is8021 && subtype == 1 && infoLen >= 2)
        {
            int pvid = (frame[infoOffset] << 8) | frame[infoOffset + 1];
            link.PortVlanId = pvid.ToString(CultureInfo.InvariantCulture);
            link.RawTlvs.Add($"Port VLAN ID: {pvid}");
            return;
        }

        // IEEE 802.1, subtype 3: VLAN Name (2-byte VLAN ID + 1-byte name length + name).
        if (is8021 && subtype == 3 && infoLen >= 3)
        {
            int vid = (frame[infoOffset] << 8) | frame[infoOffset + 1];
            int nameLen = frame[infoOffset + 2];
            string name = "";
            if (nameLen > 0 && infoOffset + 3 + nameLen <= valueOffset + length)
            {
                name = DecodeString(frame, infoOffset + 3, nameLen);
            }

            link.VlanName = string.IsNullOrEmpty(name)
                ? vid.ToString(CultureInfo.InvariantCulture)
                : $"{vid} ({name})";
            link.RawTlvs.Add($"VLAN Name: {link.VlanName}");
            return;
        }

        // IEEE 802.3, subtype 1: MAC/PHY config/status - report autoneg state.
        if (is8023 && subtype == 1 && infoLen >= 1)
        {
            bool autonegEnabled = (frame[infoOffset] & 0x02) != 0;
            link.Duplex = autonegEnabled ? "auto-negotiated" : "fixed";
            link.RawTlvs.Add($"802.3 MAC/PHY: autoneg {(autonegEnabled ? "on" : "off")}");
            return;
        }

        string ouiName = is8021 ? "802.1" : is8023 ? "802.3"
            : $"{frame[valueOffset]:X2}-{frame[valueOffset + 1]:X2}-{frame[valueOffset + 2]:X2}";
        link.RawTlvs.Add($"{ouiName} TLV subtype {subtype}: {infoLen} bytes");
    }

    private static string DecodeCapabilities(int bitmap)
    {
        // LLDP system-capability bit positions (LSB first).
        var names = new[]
        {
            "Other",      // bit 0
            "Repeater",   // bit 1
            "Bridge",     // bit 2
            "WLAN AP",    // bit 3
            "Router",     // bit 4
            "Telephone",  // bit 5
            "DOCSIS",     // bit 6
            "Station",    // bit 7
        };

        var enabled = new List<string>();
        for (int i = 0; i < names.Length; i++)
        {
            if ((bitmap & (1 << i)) != 0)
            {
                enabled.Add(names[i]);
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

    private static string FormatMac(byte[] frame, int offset, int length)
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

    private static bool IsMostlyPrintable(byte[] frame, int offset, int length)
    {
        for (int i = 0; i < length; i++)
        {
            byte b = frame[offset + i];
            // Allow printable ASCII plus tab and trailing NUL padding.
            if (b == 0x00)
            {
                continue;
            }

            if (b < 0x20 || b > 0x7E)
            {
                return false;
            }
        }

        return true;
    }
}
