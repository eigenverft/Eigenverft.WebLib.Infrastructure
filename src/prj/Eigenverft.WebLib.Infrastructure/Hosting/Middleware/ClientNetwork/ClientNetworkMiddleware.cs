using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

using Eigenverft.WebLib.Infrastructure.Hosting.Features;

using Microsoft.AspNetCore.Http;

namespace Eigenverft.WebLib.Infrastructure.Hosting.Middleware.ClientNetwork
{
    /// <summary>
    /// Collects normalized client-network facts into an <see cref="IClientNetworkFeature"/> for the current request.
    /// </summary>
    public sealed class ClientNetworkMiddleware
    {
        private const string ForwardedHeaderName = "Forwarded";
        private const string XForwardedForHeaderName = "X-Forwarded-For";

        private readonly RequestDelegate _next;

        /// <summary>
        /// Initializes a new instance of the <see cref="ClientNetworkMiddleware"/> class.
        /// </summary>
        public ClientNetworkMiddleware(RequestDelegate next)
        {
            _next = next ?? throw new ArgumentNullException(nameof(next));
        }

        /// <summary>
        /// Populates the typed client-network feature and continues the request pipeline.
        /// </summary>
        public Task InvokeAsync(HttpContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            var remoteIpAddress = NormalizeAddress(context.Connection.RemoteIpAddress)
                ?? throw new InvalidOperationException(
                    "The client-network feature requires HttpContext.Connection.RemoteIpAddress to contain an IPv4 or IPv6 address.");

            var forwardedIpChain = new List<ClientForwardedIpAddress>();
            AppendForwardedHeader(context, forwardedIpChain);
            AppendXForwardedForHeader(context, forwardedIpChain);

            context.SetFeature<IClientNetworkFeature>(new ClientNetworkFeature(remoteIpAddress, forwardedIpChain));
            return _next(context);
        }

        private static void AppendForwardedHeader(HttpContext context, List<ClientForwardedIpAddress> target)
        {
            foreach (var headerValue in context.Request.Headers[ForwardedHeaderName])
            {
                if (string.IsNullOrEmpty(headerValue))
                    continue;

                foreach (var element in SplitOutsideQuotes(headerValue, ','))
                {
                    foreach (var parameter in SplitOutsideQuotes(element, ';'))
                    {
                        var equalsIndex = parameter.IndexOf('=');
                        if (equalsIndex < 0)
                            continue;

                        var name = parameter.Substring(0, equalsIndex).Trim();
                        if (!string.Equals(name, "for", StringComparison.OrdinalIgnoreCase))
                            continue;

                        var rawValue = parameter.Substring(equalsIndex + 1).Trim();
                        target.Add(ParseForwardedIp(ClientForwardedIpSource.Forwarded, rawValue));
                    }
                }
            }
        }

        private static void AppendXForwardedForHeader(HttpContext context, List<ClientForwardedIpAddress> target)
        {
            foreach (var headerValue in context.Request.Headers[XForwardedForHeaderName])
            {
                if (headerValue is null)
                    continue;

                foreach (var token in headerValue.Split(','))
                {
                    target.Add(ParseForwardedIp(ClientForwardedIpSource.XForwardedFor, token.Trim()));
                }
            }
        }

        private static ClientForwardedIpAddress ParseForwardedIp(ClientForwardedIpSource source, string rawValue)
        {
            var value = Unquote(rawValue.Trim());
            if (value.Length == 0)
                return new ClientForwardedIpAddress(source, rawValue, null, true);

            if (IPAddress.TryParse(value, out var directAddress))
                return new ClientForwardedIpAddress(source, rawValue, NormalizeAddress(directAddress), false);

            if (value[0] == '[')
            {
                var closingBracket = value.IndexOf(']');
                if (closingBracket > 1)
                {
                    var host = value.Substring(1, closingBracket - 1);
                    var remainder = value.Substring(closingBracket + 1);
                    if ((remainder.Length == 0 || IsValidPortSuffix(remainder)) && IPAddress.TryParse(host, out var bracketedAddress))
                    {
                        return new ClientForwardedIpAddress(source, rawValue, NormalizeAddress(bracketedAddress), false);
                    }
                }
            }

            var firstColon = value.IndexOf(':');
            if (firstColon > 0 && firstColon == value.LastIndexOf(':'))
            {
                var host = value.Substring(0, firstColon);
                var portText = value.Substring(firstColon + 1);
                if (IsValidPort(portText) && IPAddress.TryParse(host, out var endpointAddress))
                {
                    return new ClientForwardedIpAddress(source, rawValue, NormalizeAddress(endpointAddress), false);
                }
            }

            return new ClientForwardedIpAddress(source, rawValue, null, true);
        }

        private static IPAddress? NormalizeAddress(IPAddress? address)
        {
            if (address is null)
                return null;

            if (address.AddressFamily == AddressFamily.InterNetworkV6 && address.IsIPv4MappedToIPv6)
                address = address.MapToIPv4();

            if (address.AddressFamily == AddressFamily.InterNetwork)
                return new IPAddress(address.GetAddressBytes());

            if (address.AddressFamily == AddressFamily.InterNetworkV6)
                return new IPAddress(address.GetAddressBytes());

            return null;
        }

        private static bool IsValidPortSuffix(string value)
            => value.Length > 1 && value[0] == ':' && IsValidPort(value.Substring(1));

        private static bool IsValidPort(string value)
            => ushort.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out _);

        private static string Unquote(string value)
        {
            if (value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"')
            {
                value = value.Substring(1, value.Length - 2);
                value = value.Replace("\\\"", "\"").Replace("\\\\", "\\");
            }

            return value;
        }

        private static IEnumerable<string> SplitOutsideQuotes(string value, char separator)
        {
            var start = 0;
            var quoted = false;
            var escaped = false;

            for (var i = 0; i < value.Length; i++)
            {
                var current = value[i];

                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (quoted && current == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (current == '"')
                {
                    quoted = !quoted;
                    continue;
                }

                if (!quoted && current == separator)
                {
                    yield return value.Substring(start, i - start).Trim();
                    start = i + 1;
                }
            }

            yield return value.Substring(start).Trim();
        }
    }
}
