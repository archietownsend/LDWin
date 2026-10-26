using LDWin.Capture;
using Xunit;

namespace LDWin.Tests;

/// <summary>
/// Covers the Wi-Fi-awareness added to adapter selection: AdapterInfo flags
/// and labels wireless adapters, and CaptureService.GetWifiInfo only queries
/// the Wi-Fi association for adapters flagged as wireless. The actual WLAN
/// API call is Windows-only and covered indirectly here via a fake provider,
/// since CI runs this suite on Linux.
/// </summary>
public class WifiInfoTests
{
    private sealed class FakeWifiInfoProvider : IWifiInfoProvider
    {
        public int CallCount { get; private set; }
        public Guid? LastGuid { get; private set; }
        public WifiInfo? Result { get; set; }

        public WifiInfo? GetCurrentConnection(Guid interfaceGuid)
        {
            CallCount++;
            LastGuid = interfaceGuid;
            return Result;
        }
    }

    [Fact]
    public void AdapterInfo_ToString_WiredAdapter_ShowsDescriptionOnly()
    {
        var adapter = new AdapterInfo { Name = "\\Device\\NPF_{A}", Description = "Ethernet", IsWireless = false };

        Assert.Equal("Ethernet", adapter.ToString());
    }

    [Fact]
    public void AdapterInfo_ToString_WirelessAdapter_AppendsWiFiTag()
    {
        var adapter = new AdapterInfo { Name = "\\Device\\NPF_{A}", Description = "Intel Wi-Fi 6", IsWireless = true };

        Assert.Equal("Intel Wi-Fi 6 (Wi-Fi)", adapter.ToString());
    }

    [Fact]
    public void GetWifiInfo_WiredAdapter_DoesNotQueryProvider()
    {
        var provider = new FakeWifiInfoProvider { Result = new WifiInfo { Ssid = "should-not-be-returned" } };
        var service = new CaptureService(new LDWin.Protocols.ILinkDecoder[0], provider);
        var adapter = new AdapterInfo
        {
            Name = "\\Device\\NPF_{11111111-1111-1111-1111-111111111111}",
            IsWireless = false,
        };

        var result = service.GetWifiInfo(adapter);

        Assert.Null(result);
        Assert.Equal(0, provider.CallCount);
    }

    [Fact]
    public void GetWifiInfo_WirelessAdapter_QueriesProviderWithParsedGuid()
    {
        var expectedGuid = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var provider = new FakeWifiInfoProvider
        {
            Result = new WifiInfo { Ssid = "HomeNet", Bssid = "AA:BB:CC:DD:EE:FF", SignalQualityPercent = 80, LinkSpeedMbps = 866 },
        };
        var service = new CaptureService(new LDWin.Protocols.ILinkDecoder[0], provider);
        var adapter = new AdapterInfo
        {
            Name = $"\\Device\\NPF_{{{expectedGuid}}}",
            IsWireless = true,
        };

        var result = service.GetWifiInfo(adapter);

        Assert.Equal(1, provider.CallCount);
        Assert.Equal(expectedGuid, provider.LastGuid);
        Assert.NotNull(result);
        Assert.Equal("HomeNet", result!.Ssid);
    }

    [Fact]
    public void WifiInfoProvider_OffWindows_ReturnsNullRatherThanThrowing()
    {
        var provider = new WifiInfoProvider();

        var result = provider.GetCurrentConnection(Guid.NewGuid());

        if (!OperatingSystem.IsWindows())
        {
            Assert.Null(result);
        }
    }
}
