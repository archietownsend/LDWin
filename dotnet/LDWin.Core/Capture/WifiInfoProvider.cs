using System.Runtime.InteropServices;

namespace LDWin.Capture;

/// <summary>
/// Reads 802.11 association state via the Windows Native Wifi API
/// (wlanapi.dll). Packet capture has no visibility into this - a Wi-Fi
/// adapter only hands Ethernet-translated frames up the stack.
/// </summary>
public sealed class WifiInfoProvider : IWifiInfoProvider
{
    public const uint ErrorAccessDenied = 5;
    public const uint ErrorNotSupported = 50;
    public const uint ErrorServiceNotActive = 1062;
    public const uint ErrorNotFound = 1168;
    public const uint ErrorInvalidState = 5023;
    public const uint ErrorUnexpected = uint.MaxValue;

    private const uint ClientVersion = 2; // Windows Vista and later
    private const int OpcodeCurrentConnection = 7; // wlan_intf_opcode_current_connection

    /// <summary>Explains a <see cref="GetCurrentConnection"/> error code to the user.</summary>
    public static string DescribeError(uint errorCode) => errorCode switch
    {
        ErrorAccessDenied =>
            "Windows blocked access to the Wi-Fi details.\n" +
            "Windows 11 24H2 and later only share the network\n" +
            "name and access point with apps allowed to use\n" +
            "location. In Settings > Privacy & security >\n" +
            "Location, turn on \"Location services\" and\n" +
            "\"Let desktop apps access your location\", then\n" +
            "try again.",
        ErrorInvalidState => "This adapter is not connected to a Wi-Fi network.",
        ErrorServiceNotActive => "The WLAN AutoConfig service is not running.",
        ErrorNotFound => "Windows did not recognise this adapter as a Wi-Fi interface.",
        ErrorNotSupported => "Reading Wi-Fi details is only supported on Windows.",
        _ => $"Could not read the Wi-Fi details (error {errorCode}).",
    };

    /// <inheritdoc />
    public WifiInfo? GetCurrentConnection(Guid interfaceGuid, out uint errorCode)
    {
        if (!OperatingSystem.IsWindows())
        {
            errorCode = ErrorNotSupported;
            return null;
        }

        IntPtr clientHandle = IntPtr.Zero;
        IntPtr dataPtr = IntPtr.Zero;

        try
        {
            errorCode = WlanOpenHandle(ClientVersion, IntPtr.Zero, out _, out clientHandle);
            if (errorCode != 0)
            {
                return null;
            }

            var guid = interfaceGuid;
            errorCode = WlanQueryInterface(
                clientHandle, ref guid, OpcodeCurrentConnection, IntPtr.Zero,
                out _, out dataPtr, out _);
            if (errorCode != 0)
            {
                return null;
            }

            if (dataPtr == IntPtr.Zero)
            {
                errorCode = ErrorUnexpected;
                return null;
            }

            var attrs = Marshal.PtrToStructure<WlanConnectionAttributes>(dataPtr);
            var assoc = attrs.wlanAssociationAttributes;

            int ssidLength = (int)Math.Min(assoc.dot11Ssid.SsidLength, 32);
            string ssid = ssidLength > 0 && assoc.dot11Ssid.Ssid is not null
                ? System.Text.Encoding.UTF8.GetString(assoc.dot11Ssid.Ssid, 0, ssidLength)
                : "";

            string bssid = assoc.dot11Bssid is { Length: 6 }
                ? string.Join(":", assoc.dot11Bssid.Select(b => b.ToString("X2")))
                : "";

            return new WifiInfo
            {
                Ssid = ssid,
                Bssid = bssid,
                SignalQualityPercent = (int)assoc.wlanSignalQuality,
                LinkSpeedMbps = (int)(assoc.ulRxRate / 1000),
            };
        }
        catch
        {
            errorCode = ErrorUnexpected;
            return null;
        }
        finally
        {
            if (dataPtr != IntPtr.Zero)
            {
                try { WlanFreeMemory(dataPtr); } catch { /* best-effort */ }
            }

            if (clientHandle != IntPtr.Zero)
            {
                try { WlanCloseHandle(clientHandle, IntPtr.Zero); } catch { /* best-effort */ }
            }
        }
    }

    // ---- Native Wifi API interop (wlanapi.dll) ----

    [StructLayout(LayoutKind.Sequential)]
    private struct Dot11Ssid
    {
        public uint SsidLength;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] Ssid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WlanAssociationAttributes
    {
        public Dot11Ssid dot11Ssid;
        public int dot11BssType;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
        public byte[] dot11Bssid;

        public int dot11PhyType;
        public uint uDot11PhyIndex;
        public uint wlanSignalQuality;
        public uint ulRxRate;
        public uint ulTxRate;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WlanSecurityAttributes
    {
        public int bSecurityEnabled;
        public int bOneXEnabled;
        public int dot11AuthAlgorithm;
        public int dot11CipherAlgorithm;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WlanConnectionAttributes
    {
        public int isState;
        public int wlanConnectionMode;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string strProfileName;

        public WlanAssociationAttributes wlanAssociationAttributes;
        public WlanSecurityAttributes wlanSecurityAttributes;
    }

    [DllImport("wlanapi.dll")]
    private static extern uint WlanOpenHandle(
        uint clientVersion, IntPtr pReserved, out uint negotiatedVersion, out IntPtr clientHandle);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanCloseHandle(IntPtr clientHandle, IntPtr pReserved);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanQueryInterface(
        IntPtr clientHandle,
        ref Guid interfaceGuid,
        int opCode,
        IntPtr pReserved,
        out uint dataSize,
        out IntPtr ppData,
        out int wlanOpcodeValueType);

    [DllImport("wlanapi.dll")]
    private static extern void WlanFreeMemory(IntPtr pMemory);
}
