using System.Net.NetworkInformation;
using FluentAssertions;
using Synapse.Infrastructure.Features.ControlCenter.Services;
using Xunit;

namespace Synapse.Infrastructure.Tests.Services;

public sealed class HardwareInventoryNetworkFilterTests
{
    [Theory]
    [InlineData(NetworkInterfaceType.Wireless80211, "Wi-Fi", "Intel Wi-Fi 7 BE200")]
    [InlineData(NetworkInterfaceType.Ethernet, "Ethernet", "Realtek Gaming 2.5GbE")]
    [InlineData(NetworkInterfaceType.GigabitEthernet, "LAN", "Intel I225-V")]
    public void IsUserFacingNetworkAdapter_AcceptsPhysicalAdapters(
        NetworkInterfaceType type,
        string name,
        string description)
    {
        HardwareInventoryService.IsUserFacingNetworkAdapter(type, name, description).Should().BeTrue();
    }

    [Theory]
    [InlineData(NetworkInterfaceType.Wireless80211, "Connexion réseau local* 1", "Microsoft Wi-Fi Direct Virtual Adapter")]
    [InlineData(NetworkInterfaceType.Ethernet, "vEthernet (Default Switch)", "Hyper-V Virtual Ethernet Adapter")]
    [InlineData(NetworkInterfaceType.Ethernet, "Tailscale", "Tailscale Tunnel")]
    [InlineData(NetworkInterfaceType.Ethernet, "Ethernet 3", "TAP-Windows Adapter V9")]
    [InlineData(NetworkInterfaceType.Tunnel, "Teredo", "Microsoft Teredo Tunneling Adapter")]
    public void IsUserFacingNetworkAdapter_RejectsVirtualAndTunnelAdapters(
        NetworkInterfaceType type,
        string name,
        string description)
    {
        HardwareInventoryService.IsUserFacingNetworkAdapter(type, name, description).Should().BeFalse();
    }
}
