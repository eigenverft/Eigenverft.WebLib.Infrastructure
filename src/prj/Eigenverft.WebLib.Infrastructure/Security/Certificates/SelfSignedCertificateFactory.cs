using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Eigenverft.WebLib.Infrastructure.Security.Certificates
{
    /// <summary>Creates self-signed certificates without storage or hosting dependencies.</summary>
    public static class SelfSignedCertificateFactory
    {
        /// <summary>Creates a self-signed certificate containing its private key.</summary>
        /// <param name="options">The certificate description.</param>
        /// <returns>A caller-owned certificate containing its private key.</returns>
        public static X509Certificate2 Create(SelfSignedCertificateOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(options.Subject);
            ArgumentNullException.ThrowIfNull(options.DnsNames);
            ArgumentNullException.ThrowIfNull(options.IpAddresses);

            if (options.Validity <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(options), options.Validity, "Validity must be positive.");
            }

            if (!Enum.IsDefined(options.KeyProfile))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(options),
                    options.KeyProfile,
                    "Unsupported certificate key profile.");
            }

            string[] dnsNames = options.DnsNames
                .Where(static name => !string.IsNullOrWhiteSpace(name))
                .Select(static name => name.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            IPAddress[] ipAddresses = options.IpAddresses
                .Where(static address => address is not null)
                .Distinct()
                .ToArray();

            if (RequiresSubjectAlternativeName(options.Purpose) &&
                dnsNames.Length == 0 &&
                ipAddresses.Length == 0)
            {
                throw new ArgumentException(
                    "TLS server certificates require at least one DNS name or IP address.",
                    nameof(options));
            }

            X500DistinguishedName subject = BuildSubject(options.Subject);
            ResolvePurpose(
                options.Purpose,
                IsRsa(options.KeyProfile),
                out X509KeyUsageFlags keyUsage,
                out OidCollection enhancedKeyUsages);

            DateTimeOffset notBefore = DateTimeOffset.UtcNow.AddDays(-1);
            DateTimeOffset notAfter = DateTimeOffset.UtcNow.Add(options.Validity);

            using X509Certificate2 generated = IsRsa(options.KeyProfile)
                ? CreateRsa(options.KeyProfile, subject, notBefore, notAfter, keyUsage, enhancedKeyUsages, dnsNames, ipAddresses)
                : CreateEcdsa(options.KeyProfile, subject, notBefore, notAfter, keyUsage, enhancedKeyUsages, dnsNames, ipAddresses);

            return Pkcs12CertificateImporter.Import(
                generated.Export(X509ContentType.Pfx, string.Empty),
                string.Empty);
        }

        private static X509Certificate2 CreateRsa(
            CertificateKeyProfile profile,
            X500DistinguishedName subject,
            DateTimeOffset notBefore,
            DateTimeOffset notAfter,
            X509KeyUsageFlags keyUsage,
            OidCollection enhancedKeyUsages,
            IReadOnlyCollection<string> dnsNames,
            IReadOnlyCollection<IPAddress> ipAddresses)
        {
            using RSA key = RSA.Create(profile == CertificateKeyProfile.Rsa3072Sha256 ? 3072 : 2048);
            var request = new CertificateRequest(subject, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            ApplyExtensions(request, keyUsage, enhancedKeyUsages, dnsNames, ipAddresses);
            return request.CreateSelfSigned(notBefore, notAfter);
        }

        private static X509Certificate2 CreateEcdsa(
            CertificateKeyProfile profile,
            X500DistinguishedName subject,
            DateTimeOffset notBefore,
            DateTimeOffset notAfter,
            X509KeyUsageFlags keyUsage,
            OidCollection enhancedKeyUsages,
            IReadOnlyCollection<string> dnsNames,
            IReadOnlyCollection<IPAddress> ipAddresses)
        {
            ECCurve curve = profile == CertificateKeyProfile.EcdsaP384Sha384
                ? ECCurve.NamedCurves.nistP384
                : ECCurve.NamedCurves.nistP256;
            HashAlgorithmName hash = profile == CertificateKeyProfile.EcdsaP384Sha384
                ? HashAlgorithmName.SHA384
                : HashAlgorithmName.SHA256;

            using ECDsa key = ECDsa.Create(curve);
            var request = new CertificateRequest(subject, key, hash);
            ApplyExtensions(request, keyUsage, enhancedKeyUsages, dnsNames, ipAddresses);
            return request.CreateSelfSigned(notBefore, notAfter);
        }

        private static void ApplyExtensions(
            CertificateRequest request,
            X509KeyUsageFlags keyUsage,
            OidCollection enhancedKeyUsages,
            IReadOnlyCollection<string> dnsNames,
            IReadOnlyCollection<IPAddress> ipAddresses)
        {
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(keyUsage, false));
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(enhancedKeyUsages, true));
            request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

            if (dnsNames.Count == 0 && ipAddresses.Count == 0)
            {
                return;
            }

            var subjectAlternativeNames = new SubjectAlternativeNameBuilder();
            foreach (string dnsName in dnsNames)
            {
                subjectAlternativeNames.AddDnsName(dnsName);
            }

            foreach (IPAddress ipAddress in ipAddresses)
            {
                subjectAlternativeNames.AddIpAddress(ipAddress);
            }

            request.CertificateExtensions.Add(subjectAlternativeNames.Build());
        }

        private static X500DistinguishedName BuildSubject(CertificateSubject subject)
        {
            var builder = new X500DistinguishedNameBuilder();
            var hasValue = false;

            Add(subject.CommonName, builder.AddCommonName);
            Add(subject.OrganizationName, builder.AddOrganizationName);
            Add(subject.OrganizationalUnitName, builder.AddOrganizationalUnitName);
            Add(subject.LocalityName, builder.AddLocalityName);
            Add(subject.StateOrProvinceName, builder.AddStateOrProvinceName);
            Add(subject.CountryOrRegion, builder.AddCountryOrRegion);

            if (!hasValue)
            {
                throw new ArgumentException("The certificate subject must contain at least one value.", nameof(subject));
            }

            return builder.Build();

            void Add(string? value, Action<string> add)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    return;
                }

                add(value.Trim());
                hasValue = true;
            }
        }

        private static bool RequiresSubjectAlternativeName(CertificatePurpose purpose)
        {
            return purpose is CertificatePurpose.TlsServer or CertificatePurpose.TlsServerAndClient;
        }

        private static bool IsRsa(CertificateKeyProfile profile)
        {
            return profile is CertificateKeyProfile.Rsa2048Sha256 or CertificateKeyProfile.Rsa3072Sha256;
        }

        private static void ResolvePurpose(
            CertificatePurpose purpose,
            bool isRsa,
            out X509KeyUsageFlags keyUsage,
            out OidCollection enhancedKeyUsages)
        {
            enhancedKeyUsages = new OidCollection();

            switch (purpose)
            {
                case CertificatePurpose.TlsServer:
                    enhancedKeyUsages.Add(new Oid("1.3.6.1.5.5.7.3.1"));
                    keyUsage = isRsa
                        ? X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment
                        : X509KeyUsageFlags.DigitalSignature;
                    break;

                case CertificatePurpose.TlsClient:
                    enhancedKeyUsages.Add(new Oid("1.3.6.1.5.5.7.3.2"));
                    keyUsage = X509KeyUsageFlags.DigitalSignature;
                    break;

                case CertificatePurpose.TlsServerAndClient:
                    enhancedKeyUsages.Add(new Oid("1.3.6.1.5.5.7.3.1"));
                    enhancedKeyUsages.Add(new Oid("1.3.6.1.5.5.7.3.2"));
                    keyUsage = isRsa
                        ? X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment
                        : X509KeyUsageFlags.DigitalSignature;
                    break;

                case CertificatePurpose.CodeSigning:
                    enhancedKeyUsages.Add(new Oid("1.3.6.1.5.5.7.3.3"));
                    keyUsage = X509KeyUsageFlags.DigitalSignature;
                    break;

                case CertificatePurpose.EmailProtection:
                    enhancedKeyUsages.Add(new Oid("1.3.6.1.5.5.7.3.4"));
                    keyUsage = isRsa
                        ? X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment
                        : X509KeyUsageFlags.DigitalSignature;
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(purpose), purpose, "Unsupported certificate purpose.");
            }
        }
    }
}
