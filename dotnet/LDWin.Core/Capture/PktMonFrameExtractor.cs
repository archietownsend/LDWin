namespace LDWin.Capture;

/// <summary>
/// Finds CDP/LLDP Ethernet frames inside the raw payload of a PktMon ETW
/// packet event. The payload is per-packet metadata followed by the logged
/// packet bytes; rather than depend on the (undocumented, build-specific)
/// metadata layout, this locates the frame by its destination MAC address
/// and leaves full validation to the protocol decoders.
/// </summary>
public static class PktMonFrameExtractor
{
    private const int EthernetHeaderLength = 14;
    private const int MaxIeee8023Length = 1500;

    private static readonly byte[] CdpDestMac = { 0x01, 0x00, 0x0C, 0xCC, 0xCC, 0xCC };

    // Nearest bridge, nearest non-TPMR bridge, nearest customer bridge.
    private static readonly byte[][] LldpDestMacs =
    {
        new byte[] { 0x01, 0x80, 0xC2, 0x00, 0x00, 0x0E },
        new byte[] { 0x01, 0x80, 0xC2, 0x00, 0x00, 0x03 },
        new byte[] { 0x01, 0x80, 0xC2, 0x00, 0x00, 0x00 },
    };

    /// <summary>Candidate frames found in <paramref name="eventData"/>, in payload order.</summary>
    public static IEnumerable<byte[]> FindFrames(byte[] eventData)
    {
        if (eventData is null)
        {
            yield break;
        }

        for (int i = 0; i + EthernetHeaderLength <= eventData.Length; i++)
        {
            if (MatchesAt(eventData, i, CdpDestMac))
            {
                yield return SliceCdp(eventData, i);
            }
            else if (IsLldpAt(eventData, i))
            {
                yield return eventData[i..];
            }
        }
    }

    private static bool IsLldpAt(byte[] data, int offset)
    {
        if (data[offset + 12] != 0x88 || data[offset + 13] != 0xCC)
        {
            return false;
        }

        foreach (var mac in LldpDestMacs)
        {
            if (MatchesAt(data, offset, mac))
            {
                return true;
            }
        }

        return false;
    }

    // CDP rides on 802.3, whose length field gives the exact frame size; use it
    // to drop anything that follows the frame in the event payload.
    private static byte[] SliceCdp(byte[] data, int offset)
    {
        int length = (data[offset + 12] << 8) | data[offset + 13];
        int end = offset + EthernetHeaderLength + length;
        if (length > 0 && length <= MaxIeee8023Length && end <= data.Length)
        {
            return data[offset..end];
        }

        return data[offset..];
    }

    private static bool MatchesAt(byte[] data, int offset, byte[] pattern)
    {
        if (offset + pattern.Length > data.Length)
        {
            return false;
        }

        for (int i = 0; i < pattern.Length; i++)
        {
            if (data[offset + i] != pattern[i])
            {
                return false;
            }
        }

        return true;
    }
}
