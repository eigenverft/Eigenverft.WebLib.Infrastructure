using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace Eigenverft.WebLib.Infrastructure.Hosting.Configuration.JsonSettings
{
    /// <summary>
    /// Produces self-describing encoded values for JSON configuration files.
    /// </summary>
    public static class JsonSettingsValueEncoders
    {
        /// <summary>
        /// Encodes UTF-8 text as Base64.
        /// </summary>
        /// <param name="clearText">The value to encode; <see langword="null"/> is treated as empty.</param>
        /// <returns>A wrapped Base64 value understood by the decoding JSON provider.</returns>
        /// <remarks>Base64 is an encoding, not encryption, and does not protect sensitive data.</remarks>
        public static string EncodeBase64(string? clearText)
        {
            string payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(clearText ?? string.Empty));
            return EncodedConfigurationValueFormat.Wrap(EncodedConfigurationValueKind.Base64, payload);
        }

        /// <summary>
        /// Protects UTF-8 text with Windows DPAPI machine scope and stores the payload as Base64.
        /// </summary>
        /// <param name="clearText">The value to protect; <see langword="null"/> is treated as empty.</param>
        /// <returns>A wrapped DPAPI value understood by the decoding JSON provider.</returns>
        /// <exception cref="PlatformNotSupportedException">The current platform is not Windows.</exception>
        /// <remarks>
        /// Machine-scope DPAPI values are tied to the Windows machine that created them and are not portable
        /// configuration secrets.
        /// </remarks>
        public static string EncodeDpapiMachine(string? clearText)
        {
            byte[] protectedBytes = DpapiMachineProtection.Protect(Encoding.UTF8.GetBytes(clearText ?? string.Empty));
            string payload = Convert.ToBase64String(protectedBytes);
            return EncodedConfigurationValueFormat.Wrap(EncodedConfigurationValueKind.DpapiMachine, payload);
        }

        /// <summary>
        /// Protects UTF-8 text with Windows DPAPI machine scope and stores the payload as unpadded Base64Url.
        /// </summary>
        /// <param name="clearText">The value to protect; <see langword="null"/> is treated as empty.</param>
        /// <returns>A wrapped DPAPI value understood by the decoding JSON provider.</returns>
        /// <exception cref="PlatformNotSupportedException">The current platform is not Windows.</exception>
        /// <remarks>
        /// This is the correctly named equivalent of the historical
        /// <c>EncodeDpapiMachineBase64</c> API. The persisted token remains compatible with existing values.
        /// Machine-scope DPAPI values are tied to the Windows machine that created them.
        /// </remarks>
        public static string EncodeDpapiMachineBase64Url(string? clearText)
        {
            byte[] protectedBytes = DpapiMachineProtection.Protect(Encoding.UTF8.GetBytes(clearText ?? string.Empty));
            string payload = Base64Url.Encode(protectedBytes);
            return EncodedConfigurationValueFormat.Wrap(
                EncodedConfigurationValueKind.DpapiMachineBase64Url,
                payload);
        }
    }

    internal enum EncodedConfigurationValueKind
    {
        Base64 = 0,
        DpapiMachine = 1,
        DpapiMachineBase64Url = 2,
    }

    internal static class EncodedConfigurationValueFormat
    {
        private const string Prefix = "enc:";

        private static readonly IReadOnlyDictionary<EncodedConfigurationValueKind, string> EncodingToToken =
            new Dictionary<EncodedConfigurationValueKind, string>
            {
                { EncodedConfigurationValueKind.Base64, "q7m2n4" },
                { EncodedConfigurationValueKind.DpapiMachine, "x1p9d0" },
                { EncodedConfigurationValueKind.DpapiMachineBase64Url, "k4v8s2" },
            };

        private static readonly IReadOnlyDictionary<string, EncodedConfigurationValueKind> TokenToEncoding =
            new Dictionary<string, EncodedConfigurationValueKind>(StringComparer.OrdinalIgnoreCase)
            {
                { "q7m2n4", EncodedConfigurationValueKind.Base64 },
                { "x1p9d0", EncodedConfigurationValueKind.DpapiMachine },
                { "k4v8s2", EncodedConfigurationValueKind.DpapiMachineBase64Url },
            };

        public static string Wrap(EncodedConfigurationValueKind encoding, string? payload)
        {
            string token = EncodingToToken.TryGetValue(encoding, out string? knownToken)
                ? knownToken
                : encoding.ToString().ToLowerInvariant();

            return $"{Prefix}{token}:{payload ?? string.Empty}";
        }

        public static bool TryUnwrap(
            string? value,
            out EncodedConfigurationValueKind encoding,
            out string payload)
        {
            encoding = default;
            payload = string.Empty;

            if (value is null || !value.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string remainder = value.Substring(Prefix.Length);
            int delimiterIndex = remainder.IndexOf(':');

            if (delimiterIndex <= 0)
            {
                return false;
            }

            string token = remainder.Substring(0, delimiterIndex);
            payload = remainder.Substring(delimiterIndex + 1);

            if (TokenToEncoding.TryGetValue(token, out encoding))
            {
                return true;
            }

            if (Enum.TryParse(token, ignoreCase: true, out encoding))
            {
                return true;
            }

            // Historical enum name used before Base64Url was named accurately.
            if (string.Equals(token, "DpapiMachineBase64", StringComparison.OrdinalIgnoreCase))
            {
                encoding = EncodedConfigurationValueKind.DpapiMachineBase64Url;
                return true;
            }

            encoding = default;
            return false;
        }

        public static bool HasRecognizedWrapper(string? value)
        {
            return TryUnwrap(value, out _, out _);
        }
    }

    internal static class Base64Url
    {
        public static string Encode(byte[] bytes)
        {
            ArgumentNullException.ThrowIfNull(bytes);

            return Convert.ToBase64String(bytes)
                .Replace('+', '-')
                .Replace('/', '_')
                .TrimEnd('=');
        }

        public static bool TryDecode(string? value, out byte[] bytes)
        {
            bytes = Array.Empty<byte>();

            if (value is null)
            {
                return false;
            }

            try
            {
                string padded = value.Replace('-', '+').Replace('_', '/');
                int remainder = padded.Length % 4;

                if (remainder != 0)
                {
                    padded = padded.PadRight(padded.Length + 4 - remainder, '=');
                }

                bytes = Convert.FromBase64String(padded);
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }
    }

    internal static class DpapiMachineProtection
    {
        private const string NotAvailableMessage =
            "Windows DPAPI machine-scope protection is available only on Windows.";

        public static byte[] Protect(byte[] clearBytes)
        {
            ArgumentNullException.ThrowIfNull(clearBytes);

            if (!OperatingSystem.IsWindows())
            {
                throw new PlatformNotSupportedException(NotAvailableMessage);
            }

            return ProtectedData.Protect(clearBytes, optionalEntropy: null, DataProtectionScope.LocalMachine);
        }

        public static bool TryUnprotect(byte[] protectedBytes, out byte[] clearBytes)
        {
            ArgumentNullException.ThrowIfNull(protectedBytes);

            clearBytes = Array.Empty<byte>();

            if (!OperatingSystem.IsWindows())
            {
                return false;
            }

            try
            {
                clearBytes = ProtectedData.Unprotect(
                    protectedBytes,
                    optionalEntropy: null,
                    DataProtectionScope.LocalMachine);
                return true;
            }
            catch (CryptographicException)
            {
                return false;
            }
            catch (PlatformNotSupportedException)
            {
                return false;
            }
        }
    }

    internal static class EncodedConfigurationValueDecoder
    {
        public static bool TryDecode(string? value, out string clearText)
        {
            clearText = value ?? string.Empty;
            string current = value ?? string.Empty;
            bool changed = false;

            for (int depth = 0; depth < 5; depth++)
            {
                if (!TryDecodeSingle(current, out string next))
                {
                    break;
                }

                changed = true;
                current = next;
            }

            clearText = current;
            return changed;
        }

        private static bool TryDecodeSingle(string value, out string clearText)
        {
            clearText = value;

            if (!EncodedConfigurationValueFormat.TryUnwrap(value, out EncodedConfigurationValueKind encoding, out string payload))
            {
                return false;
            }

            return encoding switch
            {
                EncodedConfigurationValueKind.Base64 => TryDecodeBase64(payload, out clearText),
                EncodedConfigurationValueKind.DpapiMachine => TryDecodeDpapiBase64(payload, out clearText),
                EncodedConfigurationValueKind.DpapiMachineBase64Url => TryDecodeDpapiBase64Url(payload, out clearText),
                _ => false,
            };
        }

        private static bool TryDecodeBase64(string payload, out string clearText)
        {
            clearText = string.Empty;

            try
            {
                clearText = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        private static bool TryDecodeDpapiBase64(string payload, out string clearText)
        {
            clearText = string.Empty;

            try
            {
                return TryUnprotect(Convert.FromBase64String(payload), out clearText);
            }
            catch (FormatException)
            {
                return false;
            }
        }

        private static bool TryDecodeDpapiBase64Url(string payload, out string clearText)
        {
            clearText = string.Empty;
            return Base64Url.TryDecode(payload, out byte[] protectedBytes) &&
                TryUnprotect(protectedBytes, out clearText);
        }

        private static bool TryUnprotect(byte[] protectedBytes, out string clearText)
        {
            clearText = string.Empty;

            if (!DpapiMachineProtection.TryUnprotect(protectedBytes, out byte[] clearBytes))
            {
                return false;
            }

            clearText = Encoding.UTF8.GetString(clearBytes);
            return true;
        }
    }
}
