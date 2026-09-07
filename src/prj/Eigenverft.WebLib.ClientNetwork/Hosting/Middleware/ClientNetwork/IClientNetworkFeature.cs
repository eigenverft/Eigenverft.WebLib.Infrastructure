using System.Collections.Generic;
using System.Net;

namespace Eigenverft.WebLib.Infrastructure.Hosting.Middleware.ClientNetwork
{
    /// <summary>
    /// Identifies the request header family from which forwarded IP information was observed.
    /// </summary>
    public enum ClientForwardedIpSource
    {
        /// <summary>
        /// The standardized <c>Forwarded</c> header.
        /// </summary>
        Forwarded = 0,

        /// <summary>
        /// The de-facto <c>X-Forwarded-For</c> header.
        /// </summary>
        XForwardedFor = 1,
    }

    /// <summary>
    /// Represents one forwarded-IP token without assigning trust or legitimacy to it.
    /// </summary>
    public sealed class ClientForwardedIpAddress
    {
        internal ClientForwardedIpAddress(ClientForwardedIpSource source, string rawValue, IPAddress? address, bool isMalformed)
        {
            Source = source;
            RawValue = rawValue;
            Address = address;
            IsMalformed = isMalformed;
        }

        /// <summary>
        /// Gets the header family that supplied the token.
        /// </summary>
        public ClientForwardedIpSource Source { get; }

        /// <summary>
        /// Gets the original trimmed token value.
        /// </summary>
        public string RawValue { get; }

        /// <summary>
        /// Gets the normalized IP address when the token could be parsed as an IP endpoint or address.
        /// </summary>
        public IPAddress? Address { get; }

        /// <summary>
        /// Gets a value indicating whether the token was present but could not be interpreted as an IP address.
        /// </summary>
        public bool IsMalformed { get; }
    }

    /// <summary>
    /// Exposes normalized client-network facts collected for the current request.
    /// </summary>
    /// <remarks>
    /// This feature intentionally performs no proxy-trust, legitimacy, or request-behavior evaluation. Consumers decide
    /// how the actual peer address and any forwarded-IP information should affect filtering or other behavior.
    /// </remarks>
    public interface IClientNetworkFeature
    {
        /// <summary>
        /// Gets the normalized actual remote peer address from <c>HttpContext.Connection.RemoteIpAddress</c>.
        /// </summary>
        IPAddress RemoteIpAddress { get; }

        /// <summary>
        /// Gets forwarded-IP tokens observed in the request. Order is preserved within each source; standardized
        /// <c>Forwarded</c> entries are collected before <c>X-Forwarded-For</c> entries when both are present.
        /// </summary>
        IReadOnlyList<ClientForwardedIpAddress> ForwardedIpChain { get; }

        /// <summary>
        /// Gets a value indicating whether any forwarded-IP token was present but malformed.
        /// </summary>
        bool HasMalformedForwardedIpInformation { get; }
    }

    internal sealed class ClientNetworkFeature : IClientNetworkFeature
    {
        public ClientNetworkFeature(IPAddress remoteIpAddress, IReadOnlyList<ClientForwardedIpAddress> forwardedIpChain)
        {
            RemoteIpAddress = remoteIpAddress;
            ForwardedIpChain = forwardedIpChain;

            for (var i = 0; i < forwardedIpChain.Count; i++)
            {
                if (forwardedIpChain[i].IsMalformed)
                {
                    HasMalformedForwardedIpInformation = true;
                    break;
                }
            }
        }

        public IPAddress RemoteIpAddress { get; }

        public IReadOnlyList<ClientForwardedIpAddress> ForwardedIpChain { get; }

        public bool HasMalformedForwardedIpInformation { get; }
    }
}
