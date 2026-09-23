using System.Net;
using Microsoft.Extensions.Primitives;

namespace Alpha6Ops.Server;

// Cloudflare's published edge ranges (https://www.cloudflare.com/ips-v4 and /ips-v6, fetched 2026-09-22).
// They change rarely; refresh this list if Cloudflare announces new ranges.
public static class CloudflareNetworks
{
    private static readonly IPNetwork[] Ranges =
    [
        .. new[]
        {
            "173.245.48.0/20", "103.21.244.0/22", "103.22.200.0/22", "103.31.4.0/22", "141.101.64.0/18",
            "108.162.192.0/18", "190.93.240.0/20", "188.114.96.0/20", "197.234.240.0/22", "198.41.128.0/17",
            "162.158.0.0/15", "104.16.0.0/13", "104.24.0.0/14", "172.64.0.0/13", "131.0.72.0/22",
            "2400:cb00::/32", "2606:4700::/32", "2803:f800::/32", "2405:b500::/32", "2405:8100::/32",
            "2a06:98c0::/29", "2c0f:f248::/32"
        }.Select(IPNetwork.Parse)
    ];

    public static bool Contains(IPAddress address)
    {
        var candidate = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
        return Ranges.Any(range => range.Contains(candidate));
    }

    // The visitor named in CF-Connecting-IP, but only when the connecting peer is a Cloudflare edge; anyone can send
    // the header straight to the origin host name.
    public static IPAddress? Visitor(IPAddress? peer, StringValues header) =>
        peer is not null && Contains(peer) && header.Count == 1 && IPAddress.TryParse(header[0], out var visitor) ? visitor : null;
}
