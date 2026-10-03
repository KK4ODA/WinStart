using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using WinStart.Native;

namespace WinStart.Core;

/// <summary>Checks that tell WinStart when it is reasonable to begin launching applications.</summary>
internal static class Readiness
{
    /// <summary>The desktop is ready once Explorer has created the taskbar.</summary>
    public static bool IsDesktopReady() =>
        NativeMethods.FindWindow("Shell_TrayWnd", null) != IntPtr.Zero;

    /// <summary>
    /// True when a real network adapter is up with a routable address and a default gateway.
    /// Virtual adapters (Hyper-V, VPN stubs) normally have no gateway and are not counted.
    /// </summary>
    public static bool IsNetworkReady()
    {
        try
        {
            if (!NetworkInterface.GetIsNetworkAvailable()) return false;

            foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.OperationalStatus != OperationalStatus.Up) continue;
                if (adapter.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;

                IPInterfaceProperties properties = adapter.GetIPProperties();
                bool hasGateway = properties.GatewayAddresses.Any(g =>
                    g.Address != null && !g.Address.Equals(IPAddress.Any) && !g.Address.Equals(IPAddress.IPv6Any));
                bool hasAddress = properties.UnicastAddresses.Any(a => IsRoutable(a.Address));
                if (hasGateway && hasAddress) return true;
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Network check failed: " + ex.Message);
        }

        return false;
    }

    private static bool IsRoutable(IPAddress address)
    {
        if (IPAddress.IsLoopback(address)) return false;
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            byte[] bytes = address.GetAddressBytes();
            return !(bytes[0] == 169 && bytes[1] == 254); // self-assigned: DHCP has not answered yet
        }

        return address.AddressFamily == AddressFamily.InterNetworkV6 && !address.IsIPv6LinkLocal;
    }
}
