using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Cryptography.X509Certificates;

namespace Eigenverft.WebLib.Infrastructure.Security.Certificates
{
    /// <summary>Identifies the intended use of a generated certificate.</summary>
    public enum CertificatePurpose
    {
        /// <summary>TLS server authentication.</summary>
        TlsServer,

        /// <summary>TLS client authentication.</summary>
        TlsClient,

        /// <summary>TLS server and client authentication.</summary>
        TlsServerAndClient,

        /// <summary>Code signing.</summary>
        CodeSigning,

        /// <summary>Email protection.</summary>
        EmailProtection
    }

    /// <summary>Identifies a supported certificate key and signature profile.</summary>
    public enum CertificateKeyProfile
    {
        /// <summary>RSA 2048 with SHA-256.</summary>
        Rsa2048Sha256,

        /// <summary>RSA 3072 with SHA-256.</summary>
        Rsa3072Sha256,

        /// <summary>ECDSA P-256 with SHA-256.</summary>
        EcdsaP256Sha256,

        /// <summary>ECDSA P-384 with SHA-384.</summary>
        EcdsaP384Sha384
    }

    /// <summary>Describes the X.500 subject of a generated certificate.</summary>
    public sealed class CertificateSubject
    {
        /// <summary>Gets or initializes the common name.</summary>
        public string? CommonName { get; init; }

        /// <summary>Gets or initializes the organization name.</summary>
        public string? OrganizationName { get; init; }

        /// <summary>Gets or initializes the organizational unit name.</summary>
        public string? OrganizationalUnitName { get; init; }

        /// <summary>Gets or initializes the locality name.</summary>
        public string? LocalityName { get; init; }

        /// <summary>Gets or initializes the state or province name.</summary>
        public string? StateOrProvinceName { get; init; }

        /// <summary>Gets or initializes the two-letter country or region code.</summary>
        public string? CountryOrRegion { get; init; }
    }

    /// <summary>Describes a self-signed certificate to create.</summary>
    public sealed class SelfSignedCertificateOptions
    {
        /// <summary>Gets or initializes the certificate subject.</summary>
        public required CertificateSubject Subject { get; init; }

        /// <summary>Gets or initializes the certificate purpose.</summary>
        public CertificatePurpose Purpose { get; init; } = CertificatePurpose.TlsServer;

        /// <summary>Gets or initializes the key profile.</summary>
        public CertificateKeyProfile KeyProfile { get; init; } = CertificateKeyProfile.Rsa2048Sha256;

        /// <summary>Gets or initializes how long the certificate remains valid.</summary>
        public TimeSpan Validity { get; init; } = TimeSpan.FromDays(730);

        /// <summary>Gets or initializes DNS subject alternative names.</summary>
        public IReadOnlyCollection<string> DnsNames { get; init; } = Array.Empty<string>();

        /// <summary>Gets or initializes IP-address subject alternative names.</summary>
        public IReadOnlyCollection<IPAddress> IpAddresses { get; init; } = Array.Empty<IPAddress>();
    }

    /// <summary>Describes a managed PFX file and its self-signed replacement.</summary>
    public sealed class ManagedCertificateFileOptions
    {
        /// <summary>Gets or initializes the complete PFX path.</summary>
        public required string FilePath { get; init; }

        /// <summary>Gets or initializes the PFX password. An empty password is supported.</summary>
        public string? Password { get; init; }

        /// <summary>
        /// Gets or initializes which existing PFX failures may replace the file.
        /// The default preserves every existing file while still returning an in-memory recovery certificate.
        /// </summary>
        public CertificateRecoveryMode RecoveryMode { get; init; } = CertificateRecoveryMode.PreserveExisting;

        /// <summary>Gets or initializes the certificate created when the managed PFX is unusable.</summary>
        public required SelfSignedCertificateOptions Replacement { get; init; }
    }

    /// <summary>Controls when managed-certificate recovery may replace an existing PFX file.</summary>
    public enum CertificateRecoveryMode
    {
        /// <summary>
        /// Creates and persists a missing PFX, but never replaces an existing unusable PFX.
        /// An in-memory recovery certificate remains available to the caller.
        /// </summary>
        PreserveExisting,

        /// <summary>
        /// Creates a missing PFX and replaces an existing PFX only after it was successfully
        /// imported and found to be expired.
        /// </summary>
        ReplaceExpired,

        /// <summary>
        /// Replaces any existing PFX that cannot be used, including files affected by import,
        /// password, read, or access failures. This mode can overwrite an externally managed PFX.
        /// </summary>
        ReplaceAnyUnusable
    }

    /// <summary>Describes what a managed-certificate operation did.</summary>
    public enum ManagedCertificateAction
    {
        /// <summary>An existing valid PFX was loaded.</summary>
        Loaded,

        /// <summary>A certificate was generated because the managed PFX was missing.</summary>
        GeneratedForMissingFile,

        /// <summary>A certificate was generated because the managed PFX was expired.</summary>
        GeneratedForExpiredFile,

        /// <summary>A certificate was generated because the managed PFX was not yet valid.</summary>
        GeneratedForNotYetValidFile,

        /// <summary>A certificate was generated because the managed PFX had no private key.</summary>
        GeneratedForMissingPrivateKey,

        /// <summary>A certificate was generated because the managed PFX could not be imported.</summary>
        GeneratedForImportFailure,

        /// <summary>A certificate was generated because the managed PFX could not be read.</summary>
        GeneratedForReadFailure,

        /// <summary>A certificate was generated because access to the managed PFX was denied.</summary>
        GeneratedForAccessFailure
    }

    /// <summary>Returns a usable certificate and reports the managed-file outcome.</summary>
    public sealed class ManagedCertificateResult
    {
        internal ManagedCertificateResult(
            X509Certificate2 certificate,
            ManagedCertificateAction action,
            bool persisted,
            bool existingFilePreserved,
            Exception? loadException,
            Exception? persistenceException)
        {
            Certificate = certificate;
            Action = action;
            Persisted = persisted;
            ExistingFilePreserved = existingFilePreserved;
            LoadException = loadException;
            PersistenceException = persistenceException;
        }

        /// <summary>
        /// Gets the usable certificate. The caller owns and must dispose this instance.
        /// </summary>
        public X509Certificate2 Certificate { get; }

        /// <summary>Gets the load or recovery action.</summary>
        public ManagedCertificateAction Action { get; }

        /// <summary>Gets whether the returned certificate is represented by the managed PFX file.</summary>
        public bool Persisted { get; }

        /// <summary>
        /// Gets whether an existing unusable PFX was deliberately left unchanged and the returned
        /// recovery certificate therefore exists only in memory.
        /// </summary>
        public bool ExistingFilePreserved { get; }

        /// <summary>Gets the read, access, or import error that caused recovery, when available.</summary>
        public Exception? LoadException { get; }

        /// <summary>Gets the persistence error when persistence was attempted and failed.</summary>
        public Exception? PersistenceException { get; }
    }

    internal static class Pkcs12CertificateImporter
    {
        internal static X509Certificate2 Import(byte[] pfxBytes, string? password)
        {
#pragma warning disable SYSLIB0057 // Required for the net8.0 target.
            // Windows Schannel cannot use an ephemeral private key as a Kestrel server
            // credential. UserKeySet retains broad server and non-hosting compatibility.
            return new X509Certificate2(
                pfxBytes,
                password ?? string.Empty,
                X509KeyStorageFlags.Exportable | X509KeyStorageFlags.UserKeySet);
#pragma warning restore SYSLIB0057
        }
    }
}
