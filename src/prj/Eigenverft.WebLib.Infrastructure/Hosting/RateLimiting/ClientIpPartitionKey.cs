using System.Net;
using System.Net.Sockets;

using Microsoft.AspNetCore.Http;

namespace Eigenverft.WebLib.Infrastructure.Hosting.RateLimiting
{
    internal static class ClientIpPartitionKey
    {
        internal const string MissingSharedPartition = "__weblib_missing_client_ip__";
        internal const string MissingBypassPartition = "__weblib_missing_client_ip_bypass__";
        internal const string GlobalConcurrencyPartition = "__weblib_global_concurrency__";

        internal static string? Resolve(HttpContext context)
        {
            IPAddress? address = context.Connection.RemoteIpAddress;
            if (address is null)
            {
                return null;
            }

            if (address.IsIPv4MappedToIPv6)
            {
                address = address.MapToIPv4();
            }
            else if (address.AddressFamily == AddressFamily.InterNetworkV6 && address.ScopeId != 0)
            {
                // Scope IDs identify a local interface, not a distinct client. Drop them from the partition identity.
                address = new IPAddress(address.GetAddressBytes());
            }

            return address.ToString();
        }
    }
}
