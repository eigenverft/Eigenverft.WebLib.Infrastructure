using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;

using Eigenverft.WebLib.Infrastructure.Security.Certificates;

using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;

namespace Eigenverft.WebLib.Infrastructure.Hosting.Kestrel
{
    /// <summary>
    /// Binds the existing JSON contract and compiles it into startup and reload plans.
    /// Keeping compilation free of certificate I/O makes the lifecycle boundary explicit:
    /// Kestrel settings are read once, while only certificate mappings are rebuilt on reload.
    /// </summary>
    internal static class KestrelSniConfiguration
    {
        internal const string CertificateMappingsSectionPath = "CertificatesMappingSettings";

        internal static KestrelStartupPlan BindStartup(
            IConfiguration configuration,
            string settingsSectionPath,
            string? certificateDirectoryOverride,
            string contentRootPath)
        {
            ArgumentNullException.ThrowIfNull(configuration);

            RawKestrelSettings settings = configuration
                .GetSection(settingsSectionPath)
                .Get<RawKestrelSettings>()
                ?? throw new ArgumentException($"Missing configuration section '{settingsSectionPath}'.");

            string configuredCertificateDirectory = certificateDirectoryOverride
                ?? configuration.GetValue<string>("CertificatesDirectory")
                ?? "certs";
            string fullContentRoot = Path.GetFullPath(contentRootPath);
            string certificateDirectory = Path.IsPathFullyQualified(configuredCertificateDirectory)
                ? Path.GetFullPath(configuredCertificateDirectory)
                : Path.GetFullPath(configuredCertificateDirectory, fullContentRoot);

            return new KestrelStartupPlan(
                settings.HTTP_PORT,
                settings.HTTPS_PORT,
                ParseListenScope(settings.ListenScope),
                settings.AddServerHeader,
                ParseProtocols(settings.Protocols),
                settings.PreferLongestSuffixMatch,
                ParseTlsPolicy(settings.TlsProtocolPolicy),
                certificateDirectory);
        }

        internal static CertificateSelectionPlan BindCertificateSelection(
            IConfiguration configuration,
            string certificateDirectory)
        {
            ArgumentNullException.ThrowIfNull(configuration);

            List<RawCertificateMapping> mappings = configuration
                .GetSection(CertificateMappingsSectionPath)
                .Get<List<RawCertificateMapping>>()
                ?? new List<RawCertificateMapping>();

            var normalized = new List<CertificateMappingPlan>(mappings.Count);
            var seenSniSuffixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (var index = 0; index < mappings.Count; index++)
            {
                RawCertificateMapping? mapping = mappings[index];
                string? sniSuffix = NormalizeHostName(mapping?.SNI);
                if (string.IsNullOrWhiteSpace(sniSuffix))
                {
                    continue;
                }

                if (!seenSniSuffixes.Add(sniSuffix))
                {
                    throw new ArgumentException(
                        $"Duplicate SNI suffix '{sniSuffix}' in {CertificateMappingsSectionPath}.");
                }

                string fileName = mapping?.FileName?.Trim() ?? string.Empty;
                if (fileName.Length == 0)
                {
                    throw new ArgumentException(
                        $"{CertificateMappingsSectionPath}[{index}].FileName is missing for SNI '{sniSuffix}'.");
                }

                string pfxPath = ResolveContainedPath(certificateDirectory, fileName, index);
                var dnsNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var ipAddresses = new HashSet<IPAddress>();

                AddSubjectAlternativeName(sniSuffix, dnsNames, ipAddresses);
                foreach (string? configuredName in mapping?.AdditionalSelfSignedCertificateDnsNames ?? Enumerable.Empty<string>())
                {
                    AddSubjectAlternativeName(configuredName, dnsNames, ipAddresses);
                }

                foreach (string? configuredAddress in mapping?.AdditionalSelfSignedCertificateIpAddresses ?? Enumerable.Empty<string>())
                {
                    if (!IPAddress.TryParse(configuredAddress?.Trim(), out IPAddress? address))
                    {
                        throw new ArgumentException(
                            $"{CertificateMappingsSectionPath}[{index}] contains invalid IP address '{configuredAddress}'.");
                    }

                    ipAddresses.Add(address);
                }

                normalized.Add(new CertificateMappingPlan(
                    sniSuffix,
                    pfxPath,
                    mapping?.Password ?? string.Empty,
                    ParseRecoveryMode(mapping?.CertificateRecoveryMode),
                    dnsNames.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
                    ipAddresses.OrderBy(static value => value.ToString(), StringComparer.Ordinal).ToArray()));
            }

            if (normalized.Count == 0)
            {
                throw new ArgumentException(
                    $"No usable certificate mappings were configured under '{CertificateMappingsSectionPath}'.");
            }

            return new CertificateSelectionPlan(normalized.ToArray());
        }

        private static string ResolveContainedPath(string certificateDirectory, string configuredFileName, int index)
        {
            string root = Path.GetFullPath(certificateDirectory);
            string candidate = Path.GetFullPath(configuredFileName, root);
            EnsureContained(root, candidate, index);

            // Lexical containment alone is insufficient when an existing child is a symbolic
            // link or junction. Resolve the configured root and every existing candidate segment,
            // then enforce the same boundary against their actual targets.
            string resolvedRoot = ResolveExistingLinks(root);
            string resolvedCandidate = ResolveExistingLinks(candidate);
            EnsureContained(resolvedRoot, resolvedCandidate, index);

            return candidate;
        }

        private static void EnsureContained(string root, string candidate, int index)
        {
            string relative = Path.GetRelativePath(root, candidate);
            if (Path.IsPathFullyQualified(relative) ||
                relative.Equals("..", StringComparison.Ordinal) ||
                relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"{CertificateMappingsSectionPath}[{index}].FileName and its symbolic-link targets " +
                    "must remain inside the certificate directory.");
            }
        }

        private static string ResolveExistingLinks(string path)
        {
            string fullPath = Path.GetFullPath(path);
            string pathRoot = Path.GetPathRoot(fullPath)
                ?? throw new ArgumentException($"Path '{path}' has no root.", nameof(path));
            string[] segments = fullPath[pathRoot.Length..].Split(
                new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries);
            string resolved = pathRoot;

            for (var index = 0; index < segments.Length; index++)
            {
                string next = Path.Combine(resolved, segments[index]);
                FileSystemInfo? existing = Directory.Exists(next)
                    ? new DirectoryInfo(next)
                    : File.Exists(next)
                        ? new FileInfo(next)
                        : null;

                if (existing is null)
                {
                    // Once a parent does not exist, no remaining child can currently be a link.
                    for (; index < segments.Length; index++)
                    {
                        resolved = Path.Combine(resolved, segments[index]);
                    }

                    break;
                }

                FileSystemInfo? linkTarget = existing.ResolveLinkTarget(returnFinalTarget: true);
                resolved = linkTarget is null
                    ? next
                    : Path.GetFullPath(linkTarget.FullName);
            }

            return Path.GetFullPath(resolved);
        }

        private static void AddSubjectAlternativeName(
            string? configuredName,
            ISet<string> dnsNames,
            ISet<IPAddress> ipAddresses)
        {
            string? normalized = NormalizeHostName(configuredName);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return;
            }

            if (IPAddress.TryParse(normalized, out IPAddress? address))
            {
                ipAddresses.Add(address);
                return;
            }

            dnsNames.Add(normalized);
        }

        private static string? NormalizeHostName(string? value)
        {
            string? normalized = value?.Trim().TrimEnd('.');
            return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
        }

        private static ListenScope ParseListenScope(string? value)
        {
            return Enum.TryParse(value, ignoreCase: true, out ListenScope parsed) && Enum.IsDefined(parsed)
                ? parsed
                : ListenScope.Localhost;
        }

        private static HttpProtocols? ParseProtocols(string? value)
        {
            return Enum.TryParse(value, ignoreCase: true, out HttpProtocols parsed) && Enum.IsDefined(parsed)
                ? parsed
                : null;
        }

        private static TlsProtocolPolicy ParseTlsPolicy(string? value)
        {
            return Enum.TryParse(value, ignoreCase: true, out TlsProtocolPolicy parsed) && Enum.IsDefined(parsed)
                ? parsed
                : TlsProtocolPolicy.Default;
        }

        private static CertificateRecoveryMode ParseRecoveryMode(string? value)
        {
            return Enum.TryParse(value, ignoreCase: true, out CertificateRecoveryMode parsed) && Enum.IsDefined(parsed)
                ? parsed
                : CertificateRecoveryMode.PreserveExisting;
        }

        private sealed class RawKestrelSettings
        {
            public int? HTTP_PORT { get; set; }

            public int? HTTPS_PORT { get; set; }

            public string? ListenScope { get; set; }

            public bool AddServerHeader { get; set; }

            public string? Protocols { get; set; }

            public bool PreferLongestSuffixMatch { get; set; } = true;

            public string? TlsProtocolPolicy { get; set; }
        }

        private sealed class RawCertificateMapping
        {
            public string? SNI { get; set; }

            public string? FileName { get; set; }

            public string? Password { get; set; }

            public string? CertificateRecoveryMode { get; set; }

            public List<string>? AdditionalSelfSignedCertificateDnsNames { get; set; }

            public List<string>? AdditionalSelfSignedCertificateIpAddresses { get; set; }
        }
    }

    /// <summary>Immutable Kestrel values that require a host restart to change.</summary>
    internal sealed record KestrelStartupPlan(
        int? HttpPort,
        int? HttpsPort,
        ListenScope ListenScope,
        bool AddServerHeader,
        HttpProtocols? Protocols,
        bool PreferLongestSuffixMatch,
        TlsProtocolPolicy TlsProtocolPolicy,
        string CertificateDirectory);

    /// <summary>Immutable normalized input for one managed certificate mapping.</summary>
    internal sealed record CertificateMappingPlan(
        string SniSuffix,
        string PfxPath,
        string Password,
        CertificateRecoveryMode RecoveryMode,
        IReadOnlyList<string> DnsNames,
        IReadOnlyList<IPAddress> IpAddresses);

    /// <summary>
    /// Immutable configuration generation used as the hot-reload comparison boundary.
    /// It contains no loaded certificates and therefore owns no native resources.
    /// </summary>
    internal sealed class CertificateSelectionPlan : IEquatable<CertificateSelectionPlan>
    {
        internal CertificateSelectionPlan(IReadOnlyList<CertificateMappingPlan> mappings)
        {
            Mappings = mappings;
        }

        internal IReadOnlyList<CertificateMappingPlan> Mappings { get; }

        public bool Equals(CertificateSelectionPlan? other)
        {
            if (other is null || Mappings.Count != other.Mappings.Count)
            {
                return false;
            }

            for (var index = 0; index < Mappings.Count; index++)
            {
                CertificateMappingPlan left = Mappings[index];
                CertificateMappingPlan right = other.Mappings[index];

                if (!StringComparer.OrdinalIgnoreCase.Equals(left.SniSuffix, right.SniSuffix) ||
                    !PathComparer.Equals(left.PfxPath, right.PfxPath) ||
                    !StringComparer.Ordinal.Equals(left.Password, right.Password) ||
                    left.RecoveryMode != right.RecoveryMode ||
                    !left.DnsNames.SequenceEqual(right.DnsNames, StringComparer.OrdinalIgnoreCase) ||
                    !left.IpAddresses.SequenceEqual(right.IpAddresses))
                {
                    return false;
                }
            }

            return true;
        }

        public override bool Equals(object? obj)
        {
            return obj is CertificateSelectionPlan other && Equals(other);
        }

        public override int GetHashCode()
        {
            var hash = new HashCode();
            foreach (CertificateMappingPlan mapping in Mappings)
            {
                hash.Add(mapping.SniSuffix, StringComparer.OrdinalIgnoreCase);
                hash.Add(mapping.PfxPath, PathComparer);
                hash.Add(mapping.Password, StringComparer.Ordinal);
                hash.Add(mapping.RecoveryMode);
                foreach (string dnsName in mapping.DnsNames)
                {
                    hash.Add(dnsName, StringComparer.OrdinalIgnoreCase);
                }

                foreach (IPAddress address in mapping.IpAddresses)
                {
                    hash.Add(address);
                }
            }

            return hash.ToHashCode();
        }

        private static StringComparer PathComparer { get; } = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
    }

    internal enum ListenScope
    {
        Localhost,
        AnyIP
    }

    internal enum TlsProtocolPolicy
    {
        Default,
        Strict,
        MaximumTlsCompatibility,
        Legacy
    }
}
