using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace NorthLife.Api.Health;

public sealed class MetricsAccessOptions
{
    public const string SectionName = "Metrics";

    /// <summary>When set, scrapers must send "Authorization: Bearer {Token}".</summary>
    public string? Token { get; set; }

    /// <summary>
    /// Allows scrapes without a token from loopback and private addresses (Docker networks). Leave
    /// off in production, where the platform's proxy is the only direct client.
    /// </summary>
    public bool AllowPrivateNetwork { get; set; }
}

public static class MetricsAccess
{
    public static bool IsAllowed(HttpContext context, MetricsAccessOptions options)
    {
        if (!string.IsNullOrEmpty(options.Token))
        {
            var header = context.Request.Headers.Authorization.ToString();
            const string prefix = "Bearer ";
            return header.StartsWith(prefix, StringComparison.Ordinal) &&
                CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(header[prefix.Length..]),
                    Encoding.UTF8.GetBytes(options.Token));
        }

        return options.AllowPrivateNetwork && IsPrivate(context.Connection.RemoteIpAddress);
    }

    public static bool IsPrivate(IPAddress? address)
    {
        if (address is null) return false;
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address)) return true;

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return bytes[0] == 10 ||
                (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) ||
                (bytes[0] == 192 && bytes[1] == 168);
        }

        // IPv6 unique local addresses (fc00::/7).
        return address.AddressFamily == AddressFamily.InterNetworkV6 && (address.GetAddressBytes()[0] & 0xFE) == 0xFC;
    }
}
