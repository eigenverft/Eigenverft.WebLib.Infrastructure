using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

using Eigenverft.NetLib.Infrastructure.Security.Certificates;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace Eigenverft.WebLib.Infrastructure.Hosting.Kestrel
{
    /// <summary>
    /// Owns one host's reload subscription and certificate generations.
    /// Listener configuration deliberately remains outside this type because Kestrel cannot
    /// replace listener settings through an <see cref="IConfiguration"/> reload.
    /// </summary>
    internal sealed class SniCertificateState : IHostedService, IDisposable
    {
        private readonly object gate = new();
        private readonly List<CertificateSnapshot> retiredSnapshots = new();

        private CertificateSnapshot? currentSnapshot;
        private IConfiguration? configuration;
        private string? certificateDirectory;
        private bool preferLongestSuffixMatch;
        private IDisposable? reloadSubscription;
        private CancellationTokenRegistration applicationStoppedRegistration;
        private ILogger? logger;
        private bool stopping;
        private bool certificatesReleased;
        private bool disposed;

        /// <summary>
        /// Captures the startup-fixed inputs before hosted services start. Certificate I/O is deferred
        /// to <see cref="StartAsync"/>, so building a host without starting it acquires no certificate resources.
        /// </summary>
        internal void Configure(
            IConfiguration hostConfiguration,
            string managedCertificateDirectory,
            bool preferLongest)
        {
            ArgumentNullException.ThrowIfNull(hostConfiguration);

            lock (gate)
            {
                ThrowIfDisposed();

                if (configuration is not null)
                {
                    if (!ReferenceEquals(configuration, hostConfiguration) ||
                        !PathEquals(certificateDirectory!, managedCertificateDirectory) ||
                        preferLongestSuffixMatch != preferLongest)
                    {
                        throw new InvalidOperationException(
                            "The Kestrel SNI state was initialized more than once with different startup settings.");
                    }

                    return;
                }

                configuration = hostConfiguration;
                certificateDirectory = managedCertificateDirectory;
                preferLongestSuffixMatch = preferLongest;
            }
        }

        /// <summary>Returns the certificate selected from one immutable generation.</summary>
        internal X509Certificate2 Select(string? requestedSni)
        {
            CertificateSnapshot snapshot = Volatile.Read(ref currentSnapshot)
                ?? throw new InvalidOperationException("SNI certificate state is not initialized.");

            return snapshot.Select(requestedSni);
        }

        /// <summary>
        /// Attaches host services after dependency injection has built the hosted-service instance.
        /// The application-stopped callback is the explicit boundary after which Kestrel can no longer
        /// be using a certificate returned by the selector.
        /// </summary>
        internal void AttachHostingServices(ILogger stateLogger, IHostApplicationLifetime applicationLifetime)
        {
            ArgumentNullException.ThrowIfNull(stateLogger);
            ArgumentNullException.ThrowIfNull(applicationLifetime);

            lock (gate)
            {
                logger = stateLogger;
                applicationStoppedRegistration = applicationLifetime.ApplicationStopped.Register(ReleaseCertificates);
            }
        }

        /// <inheritdoc />
        public Task StartAsync(CancellationToken cancellationToken)
        {
            lock (gate)
            {
                ThrowIfDisposed();

                IConfiguration activeConfiguration = configuration
                    ?? throw new InvalidOperationException("SNI certificate state is not initialized.");

                if (currentSnapshot is null)
                {
                    // Initial failure is allowed to fail host startup because no last-known-good
                    // generation exists yet. Managed PFX failures still receive their configured
                    // recovery behavior before this point.
                    CertificateSelectionPlan initialPlan = KestrelSniConfiguration.BindCertificateSelection(
                        activeConfiguration,
                        certificateDirectory!);
                    currentSnapshot = BuildSnapshot(initialPlan, preferLongestSuffixMatch);
                }

                reloadSubscription ??= ChangeToken.OnChange(
                    activeConfiguration.GetReloadToken,
                    ReloadAfterConfigurationChange);

                LogReports(currentSnapshot, published: true);
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task StopAsync(CancellationToken cancellationToken)
        {
            IDisposable? subscription;

            lock (gate)
            {
                // Stop accepting reload work immediately, but keep every published certificate alive.
                // Kestrel may still be draining connections until ApplicationStopped is signalled.
                stopping = true;
                subscription = reloadSubscription;
                reloadSubscription = null;
            }

            subscription?.Dispose();

            return Task.CompletedTask;
        }

        /// <summary>
        /// Stops reloads and releases all certificate generations. During normal host shutdown the
        /// snapshots have already been released by the application-stopped callback.
        /// </summary>
        public void Dispose()
        {
            CancellationTokenRegistration stoppedRegistration;
            IDisposable? subscription;

            lock (gate)
            {
                if (disposed)
                {
                    return;
                }

                disposed = true;
                subscription = reloadSubscription;
                reloadSubscription = null;
                stoppedRegistration = applicationStoppedRegistration;
                ReleaseCertificatesUnderLock();
            }

            // Dispose callbacks outside the state lock. A concurrently running callback also takes
            // that lock, so waiting for its registration while holding the lock could deadlock.
            subscription?.Dispose();
            stoppedRegistration.Dispose();
        }

        private void ReloadAfterConfigurationChange()
        {
            lock (gate)
            {
                if (disposed || stopping || configuration is null || certificateDirectory is null)
                {
                    return;
                }

                try
                {
                    CertificateSelectionPlan candidatePlan = KestrelSniConfiguration.BindCertificateSelection(
                        configuration,
                        certificateDirectory);
                    CertificateSnapshot active = currentSnapshot
                        ?? throw new InvalidOperationException("SNI certificate state is not initialized.");

                    // Configuration providers can signal for unrelated keys. Reuse the generation only
                    // while both the normalized plan and its managed PFX files remain usable and unchanged.
                    if (active.CanReuse(candidatePlan))
                    {
                        return;
                    }

                    CertificateSnapshot candidate = BuildSnapshot(candidatePlan, preferLongestSuffixMatch);

                    // Memory-only recovery keeps startup available, but a reload must not replace a
                    // still-usable published identity merely because its backing file is temporarily
                    // unreadable or deliberately preserved. Publish the recovery generation only when
                    // no complete usable last-known-good generation remains.
                    if (candidate.HasMemoryOnlyRecovery && active.HasUsableCertificates)
                    {
                        LogReports(candidate, published: false);
                        candidate.Dispose();
                        logger?.LogWarning(
                            "The Kestrel SNI reload required a memory-only recovery certificate; " +
                            "the still-usable last-known-good mapping remains active.");
                        return;
                    }

                    // Publish exactly one reference after the complete candidate has succeeded. Selectors
                    // therefore observe either the old generation or the complete new generation.
                    Volatile.Write(ref currentSnapshot, candidate);
                    retiredSnapshots.Add(active);

                    LogReports(candidate, published: true);
                    logger?.LogInformation("Reloaded {MappingCount} Kestrel SNI certificate mappings.", candidate.Count);
                }
                catch (Exception exception)
                {
                    // Reload failure must not take a running HTTPS endpoint down. The currently published
                    // generation remains the last-known-good selection until a later reload succeeds.
                    logger?.LogError(
                        exception,
                        "Could not reload Kestrel SNI certificate mappings; the last-known-good mapping remains active.");
                }
            }
        }

        private CertificateSnapshot BuildSnapshot(CertificateSelectionPlan plan, bool preferLongest)
        {
            var certificates = new List<X509Certificate2>(plan.Mappings.Count);
            var entries = new List<CertificateEntry>(plan.Mappings.Count);
            var reports = new List<CertificateLoadReport>(plan.Mappings.Count);
            var managedFileStamps = new List<ManagedFileStamp>(plan.Mappings.Count);

            try
            {
                foreach (CertificateMappingPlan mapping in plan.Mappings)
                {
                    var replacement = new SelfSignedCertificateOptions
                    {
                        Subject = new CertificateSubject { CommonName = mapping.SniSuffix },
                        Purpose = CertificatePurpose.TlsServer,
                        KeyProfile = CertificateKeyProfile.Rsa2048Sha256,
                        Validity = TimeSpan.FromDays(730),
                        DnsNames = mapping.DnsNames,
                        IpAddresses = mapping.IpAddresses
                    };
                    ManagedCertificateResult result = ManagedCertificateFile.LoadOrCreate(
                        new ManagedCertificateFileOptions
                        {
                            FilePath = mapping.PfxPath,
                            Password = mapping.Password,
                            RecoveryMode = mapping.RecoveryMode,
                            Replacement = replacement
                        });

                    certificates.Add(result.Certificate);
                    entries.Add(new CertificateEntry(mapping.SniSuffix, result.Certificate));
                    reports.Add(new CertificateLoadReport(
                        mapping.SniSuffix,
                        mapping.PfxPath,
                        result.Action,
                        result.Persisted,
                        result.ExistingFilePreserved,
                        result.LoadException,
                        result.PersistenceException));
                    managedFileStamps.Add(ManagedFileStamp.Capture(mapping.PfxPath));
                }

                CertificateEntry fallback = entries[0];
                CertificateEntry[] matchingEntries = preferLongest
                    ? entries.OrderByDescending(static entry => entry.SniSuffix.Length).ToArray()
                    : entries.ToArray();

                return new CertificateSnapshot(
                    plan,
                    matchingEntries,
                    fallback.Certificate,
                    certificates,
                    reports,
                    managedFileStamps);
            }
            catch
            {
                foreach (X509Certificate2 certificate in certificates)
                {
                    certificate.Dispose();
                }

                throw;
            }
        }

        private void LogReports(CertificateSnapshot? snapshot, bool published)
        {
            if (logger is null || snapshot is null)
            {
                return;
            }

            foreach (CertificateLoadReport report in snapshot.Reports)
            {
                if (report.Action != ManagedCertificateAction.Loaded)
                {
                    logger.LogWarning(
                        report.LoadException,
                        "Managed certificate for SNI suffix {SniSuffix} required recovery ({Action}) at {PfxPath}.",
                        report.SniSuffix,
                        report.Action,
                        report.PfxPath);
                }

                if (report.ExistingFilePreserved)
                {
                    logger.LogWarning(
                        published
                            ? "The existing PFX for SNI suffix {SniSuffix} was preserved; its recovery certificate is active only in memory."
                            : "The existing PFX for SNI suffix {SniSuffix} was preserved; its recovery certificate was not published.",
                        report.SniSuffix);
                }
                else if (!report.Persisted)
                {
                    logger.LogWarning(
                        report.PersistenceException,
                        published
                            ? "The recovery certificate for SNI suffix {SniSuffix} is active in memory but could not be persisted at {PfxPath}."
                            : "The recovery certificate for SNI suffix {SniSuffix} could not be persisted at {PfxPath} and was not published.",
                        report.SniSuffix,
                        report.PfxPath);
                }
            }
        }

        private void ReleaseCertificates()
        {
            lock (gate)
            {
                ReleaseCertificatesUnderLock();
            }
        }

        private void ReleaseCertificatesUnderLock()
        {
            if (certificatesReleased)
            {
                return;
            }

            certificatesReleased = true;
            currentSnapshot?.Dispose();
            currentSnapshot = null;

            foreach (CertificateSnapshot snapshot in retiredSnapshots)
            {
                snapshot.Dispose();
            }

            retiredSnapshots.Clear();
        }

        private void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
        }

        private static bool PathEquals(string left, string right)
        {
            StringComparison comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            return string.Equals(left, right, comparison);
        }

        /// <summary>
        /// Owns one complete selection generation. Retired snapshots intentionally remain alive until
        /// host shutdown because Kestrel's selector offers no safe notification that every handshake
        /// using an older returned certificate has completed.
        /// </summary>
        private sealed class CertificateSnapshot : IDisposable
        {
            private readonly CertificateEntry[] matchingEntries;
            private readonly X509Certificate2 fallback;
            private readonly IReadOnlyList<X509Certificate2> ownedCertificates;
            private readonly IReadOnlyList<ManagedFileStamp> managedFileStamps;

            internal CertificateSnapshot(
                CertificateSelectionPlan plan,
                CertificateEntry[] matchingEntries,
                X509Certificate2 fallback,
                IReadOnlyList<X509Certificate2> ownedCertificates,
                IReadOnlyList<CertificateLoadReport> reports,
                IReadOnlyList<ManagedFileStamp> managedFileStamps)
            {
                Plan = plan;
                this.matchingEntries = matchingEntries;
                this.fallback = fallback;
                this.ownedCertificates = ownedCertificates;
                Reports = reports;
                this.managedFileStamps = managedFileStamps;
            }

            internal CertificateSelectionPlan Plan { get; }

            internal IReadOnlyList<CertificateLoadReport> Reports { get; }

            internal int Count => matchingEntries.Length;

            internal bool HasMemoryOnlyRecovery => Reports.Any(static report => !report.Persisted);

            internal bool HasUsableCertificates
            {
                get
                {
                    DateTime utcNow = DateTime.UtcNow;
                    return ownedCertificates.All(certificate =>
                        certificate.HasPrivateKey &&
                        certificate.NotBefore.ToUniversalTime() <= utcNow &&
                        certificate.NotAfter.ToUniversalTime() > utcNow);
                }
            }

            internal bool CanReuse(CertificateSelectionPlan candidatePlan)
            {
                if (!Plan.Equals(candidatePlan) || Reports.Any(static report => !report.Persisted))
                {
                    return false;
                }

                if (!HasUsableCertificates)
                {
                    return false;
                }

                for (var index = 0; index < managedFileStamps.Count; index++)
                {
                    if (managedFileStamps[index] != ManagedFileStamp.Capture(Plan.Mappings[index].PfxPath))
                    {
                        return false;
                    }
                }

                return true;
            }

            internal X509Certificate2 Select(string? requestedSni)
            {
                string? normalizedSni = requestedSni?.Trim().TrimEnd('.');
                if (!string.IsNullOrWhiteSpace(normalizedSni))
                {
                    foreach (CertificateEntry entry in matchingEntries)
                    {
                        if (IsExactOrDnsSuffix(normalizedSni, entry.SniSuffix))
                        {
                            return entry.Certificate;
                        }
                    }
                }

                // Kestrel still needs a certificate when a client omits SNI or no suffix matches.
                // The first configured usable mapping is the stable, configuration-ordered fallback.
                return fallback;
            }

            public void Dispose()
            {
                foreach (X509Certificate2 certificate in ownedCertificates)
                {
                    certificate.Dispose();
                }
            }

            private static bool IsExactOrDnsSuffix(string host, string suffix)
            {
                if (!host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                // A DNS-label boundary prevents "notexample.com" from matching "example.com".
                return host.Length == suffix.Length || host[host.Length - suffix.Length - 1] == '.';
            }
        }

        private sealed record CertificateEntry(string SniSuffix, X509Certificate2 Certificate);

        private sealed record CertificateLoadReport(
            string SniSuffix,
            string PfxPath,
            ManagedCertificateAction Action,
            bool Persisted,
            bool ExistingFilePreserved,
            Exception? LoadException,
            Exception? PersistenceException);

        /// <summary>
        /// Captures inexpensive file identity for reload decisions. It is not a trust check; the PFX
        /// is imported and validated again whenever this stamp changes.
        /// </summary>
        private sealed record ManagedFileStamp(
            bool Exists,
            long Length,
            DateTime LastWriteTimeUtc,
            string? ContentSha256)
        {
            internal static ManagedFileStamp Capture(string path)
            {
                try
                {
                    var file = new FileInfo(path);
                    if (!file.Exists)
                    {
                        return Missing;
                    }

                    using FileStream contents = file.Open(
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete);
                    string contentSha256 = Convert.ToHexString(SHA256.HashData(contents));
                    return new ManagedFileStamp(
                        true,
                        file.Length,
                        file.LastWriteTimeUtc,
                        contentSha256);
                }
                catch (IOException)
                {
                    return Missing;
                }
                catch (UnauthorizedAccessException)
                {
                    return Missing;
                }
            }

            private static ManagedFileStamp Missing { get; } = new(false, 0, default, null);
        }
    }
}
