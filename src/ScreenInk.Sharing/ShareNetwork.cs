using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace ScreenInk.Sharing;

public sealed record ShareNetwork(IPAddress Address, string Name)
{
    public string Label => $"{Name} · {Address}";

    public static IReadOnlyList<ShareNetwork> Available()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                n.NetworkInterfaceType is NetworkInterfaceType.Wireless80211 or NetworkInterfaceType.Ethernet)
            .OrderByDescending(n => n.GetIPProperties().GatewayAddresses.Count > 0)
            .ThenByDescending(n => n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses
                .Where(a => IsPrivate(a.Address)).Select(a => new ShareNetwork(a.Address, n.Name)))
            .DistinctBy(n => n.Address).ToArray();
    }

    public static bool IsPrivate(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork) { return false; }
        var b = address.GetAddressBytes();
        return b[0] == 10 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168);
    }
}
