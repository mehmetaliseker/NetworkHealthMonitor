using System.Net;
using System.Net.Sockets;

namespace NetworkHealthMonitor.Services;

public static class IpAddressValidator
{
    public static bool IsValidIpv4(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return IPAddress.TryParse(value.Trim(), out var address)
               && address.AddressFamily == AddressFamily.InterNetwork
               && value.Trim().Count(character => character == '.') == 3;
    }
}
