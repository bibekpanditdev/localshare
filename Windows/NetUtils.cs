using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace LocalShareWindows;

public static class NetUtils
{
    /// <summary>
    /// Returns every local IPv4 address across all active NICs (Wi-Fi, Ethernet, hotspot, etc.),
    /// sorted so common home-network ranges (192.168.x.x) surface first — mirrors the Android
    /// app's getLocalIpAddresses() ordering.
    /// </summary>
    public static List<string> GetLocalIpAddresses()
    {
        var addrs = new List<string>();

        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up) continue;
            if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

            foreach (var addr in nic.GetIPProperties().UnicastAddresses)
            {
                if (addr.Address.AddressFamily != AddressFamily.InterNetwork) continue;

                var ip = addr.Address.ToString();
                if (ip.StartsWith("169.254.")) continue; // link-local autoconfig, not useful to share
                addrs.Add(ip);
            }
        }

        return addrs.Distinct().OrderBy(RangeRank).ThenBy(a => a, StringComparer.Ordinal).ToList();
    }

    private static int RangeRank(string ip)
    {
        if (ip.StartsWith("192.168.")) return 0;
        if (ip.StartsWith("10.")) return 1;

        if (ip.StartsWith("172."))
        {
            var parts = ip.Split('.');
            if (parts.Length > 1 && int.TryParse(parts[1], out var second) && second is >= 16 and <= 31)
                return 2;
        }

        return 3;
    }
}
