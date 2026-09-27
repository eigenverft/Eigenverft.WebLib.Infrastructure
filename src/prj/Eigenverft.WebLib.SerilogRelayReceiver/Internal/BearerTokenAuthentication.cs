using System;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

namespace Eigenverft.WebLib.SerilogRelayReceiver
{
    internal static class BearerTokenAuthentication
    {
        internal static bool IsAuthorized(
            string? configuredToken,
            string? authorizationHeader)
        {
            if (string.IsNullOrWhiteSpace(configuredToken))
                return true;

            if (!AuthenticationHeaderValue.TryParse(
                    authorizationHeader,
                    out AuthenticationHeaderValue? header)
                || !string.Equals(
                    header.Scheme,
                    "Bearer",
                    StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrEmpty(header.Parameter))
            {
                return false;
            }

            byte[] expected = Encoding.UTF8.GetBytes(configuredToken);
            byte[] actual = Encoding.UTF8.GetBytes(header.Parameter);

            return expected.Length == actual.Length
                && CryptographicOperations.FixedTimeEquals(actual, expected);
        }
    }
}
