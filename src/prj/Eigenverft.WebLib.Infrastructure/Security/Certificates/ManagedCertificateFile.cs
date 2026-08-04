using System;
using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Eigenverft.WebLib.Infrastructure.Security.Certificates
{
    /// <summary>Loads a managed PFX or returns a policy-controlled self-signed recovery certificate.</summary>
    public static class ManagedCertificateFile
    {
        // Locks are retained for the process lifetime. Removing a lock while another caller is
        // waiting on it could allow a third caller to create a second lock for the same PFX path.
        private static readonly ConcurrentDictionary<string, object> PathLocks = new(
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        /// <summary>
        /// Loads a valid managed PFX, or creates a self-signed recovery certificate when the file
        /// is missing, outside its validity period, unreadable, password-mismatched, or otherwise unusable.
        /// </summary>
        /// <param name="options">The managed-file and replacement description.</param>
        /// <returns>A usable caller-owned certificate and the performed action.</returns>
        /// <remarks>
        /// <see cref="CertificateRecoveryMode.PreserveExisting"/> is the safe default: existing PFX
        /// files are never replaced, while a generated certificate remains available in memory.
        /// A persistence failure likewise does not discard a successfully generated certificate.
        /// </remarks>
        public static ManagedCertificateResult LoadOrCreate(ManagedCertificateFileOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(options.Replacement);

            if (string.IsNullOrWhiteSpace(options.FilePath))
            {
                throw new ArgumentException("A PFX path is required.", nameof(options));
            }

            string fullPath = System.IO.Path.GetFullPath(options.FilePath);
            object pathLock = PathLocks.GetOrAdd(fullPath, static _ => new object());

            lock (pathLock)
            {
                return LoadOrCreateLocked(
                    fullPath,
                    options.Password ?? string.Empty,
                    options.RecoveryMode,
                    options.Replacement);
            }
        }

        private static ManagedCertificateResult LoadOrCreateLocked(
            string fullPath,
            string password,
            CertificateRecoveryMode recoveryMode,
            SelfSignedCertificateOptions replacement)
        {
            ManagedCertificateAction recoveryAction;
            Exception? loadException = null;
            var existingFile = true;

            // Read directly instead of using File.Exists. File.Exists can collapse access and I/O
            // failures into false, which would incorrectly authorize creation at an occupied path.
            try
            {
                X509Certificate2 existing = ImportFile(fullPath, password);
                recoveryAction = Classify(existing);
                if (recoveryAction == ManagedCertificateAction.Loaded)
                {
                    return new ManagedCertificateResult(
                        existing,
                        ManagedCertificateAction.Loaded,
                        persisted: true,
                        existingFilePreserved: false,
                        loadException: null,
                        persistenceException: null);
                }

                existing.Dispose();
            }
            catch (FileNotFoundException)
            {
                existingFile = false;
                recoveryAction = ManagedCertificateAction.GeneratedForMissingFile;
            }
            catch (DirectoryNotFoundException)
            {
                existingFile = false;
                recoveryAction = ManagedCertificateAction.GeneratedForMissingFile;
            }
            catch (CryptographicException exception)
            {
                loadException = exception;
                recoveryAction = ManagedCertificateAction.GeneratedForImportFailure;
            }
            catch (UnauthorizedAccessException exception)
            {
                loadException = exception;
                recoveryAction = ManagedCertificateAction.GeneratedForAccessFailure;
            }
            catch (IOException exception)
            {
                loadException = exception;
                recoveryAction = ManagedCertificateAction.GeneratedForReadFailure;
            }

            X509Certificate2 generated = SelfSignedCertificateFactory.Create(replacement);
            bool mayPersist = MayPersistRecovery(recoveryMode, recoveryAction);
            if (existingFile && !mayPersist)
            {
                return new ManagedCertificateResult(
                    generated,
                    recoveryAction,
                    persisted: false,
                    existingFilePreserved: true,
                    loadException,
                    persistenceException: null);
            }

            string? temporaryPath = null;
            X509Certificate2? persistedCandidate = null;

            try
            {
                string directory = System.IO.Path.GetDirectoryName(fullPath)
                    ?? throw new ArgumentException("The PFX path has no parent directory.", nameof(fullPath));
                Directory.CreateDirectory(directory);

                temporaryPath = System.IO.Path.Combine(
                    directory,
                    $".{System.IO.Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
                File.WriteAllBytes(temporaryPath, generated.Export(X509ContentType.Pfx, password));

                persistedCandidate = ImportFile(temporaryPath, password);
                if (Classify(persistedCandidate) != ManagedCertificateAction.Loaded)
                {
                    persistedCandidate.Dispose();
                    persistedCandidate = null;
                    throw new CryptographicException("The generated PFX could not be validated.");
                }

                // A genuinely missing file is created without overwrite. If another process places a
                // certificate here during generation, its file wins and this result remains in memory.
                File.Move(temporaryPath, fullPath, overwrite: existingFile);
                temporaryPath = null;
                generated.Dispose();

                X509Certificate2 resultCertificate = persistedCandidate;
                persistedCandidate = null;
                return new ManagedCertificateResult(
                    resultCertificate,
                    recoveryAction,
                    persisted: true,
                    existingFilePreserved: false,
                    loadException,
                    persistenceException: null);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or CryptographicException)
            {
                // Importing the temporary PFX allocates a native private-key handle. When the
                // subsequent move fails, that candidate never transfers to the caller and remains ours.
                persistedCandidate?.Dispose();
                return new ManagedCertificateResult(
                    generated,
                    recoveryAction,
                    persisted: false,
                    existingFilePreserved: false,
                    loadException,
                    persistenceException: exception);
            }
            finally
            {
                if (temporaryPath is not null)
                {
                    try
                    {
                        File.Delete(temporaryPath);
                    }
                    catch (IOException)
                    {
                    }
                    catch (UnauthorizedAccessException)
                    {
                    }
                }
            }
        }

        private static X509Certificate2 ImportFile(string path, string password)
        {
            return Pkcs12CertificateImporter.Import(File.ReadAllBytes(path), password);
        }

        private static ManagedCertificateAction Classify(X509Certificate2 certificate)
        {
            DateTime utcNow = DateTime.UtcNow;
            if (certificate.NotBefore.ToUniversalTime() > utcNow)
            {
                return ManagedCertificateAction.GeneratedForNotYetValidFile;
            }

            if (certificate.NotAfter.ToUniversalTime() <= utcNow)
            {
                return ManagedCertificateAction.GeneratedForExpiredFile;
            }

            return certificate.HasPrivateKey
                ? ManagedCertificateAction.Loaded
                : ManagedCertificateAction.GeneratedForMissingPrivateKey;
        }

        private static bool MayPersistRecovery(
            CertificateRecoveryMode recoveryMode,
            ManagedCertificateAction recoveryAction)
        {
            // Missing files are safe to create. Every replacement of an existing credential must
            // be authorized explicitly by the selected policy and the classified failure reason.
            return recoveryAction == ManagedCertificateAction.GeneratedForMissingFile ||
                recoveryMode == CertificateRecoveryMode.ReplaceAnyUnusable ||
                recoveryMode == CertificateRecoveryMode.ReplaceExpired &&
                recoveryAction == ManagedCertificateAction.GeneratedForExpiredFile;
        }
    }
}
