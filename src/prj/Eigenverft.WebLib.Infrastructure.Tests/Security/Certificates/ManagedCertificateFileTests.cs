using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

using Eigenverft.WebLib.Infrastructure.Security.Certificates;

namespace Eigenverft.WebLib.Infrastructure.Tests.Security.Certificates;

[TestClass]
public sealed class ManagedCertificateFileTests
{
    [TestMethod]
    public void FactoryCreatesDnsAndIpSubjectAlternativeNames()
    {
        using X509Certificate2 certificate = SelfSignedCertificateFactory.Create(
            CreateReplacement("example.test"));

        X509Extension encoded = certificate.Extensions["2.5.29.17"]
            ?? throw new AssertFailedException("Generated certificate has no SAN extension.");
        var subjectAlternativeNames = new X509SubjectAlternativeNameExtension(
            encoded.RawData,
            encoded.Critical);

        CollectionAssert.AreEquivalent(
            new[] { "example.test", "*.example.test" },
            subjectAlternativeNames.EnumerateDnsNames().ToArray());
        CollectionAssert.AreEquivalent(
            new[] { IPAddress.Loopback },
            subjectAlternativeNames.EnumerateIPAddresses().ToArray());
    }

    [TestMethod]
    public void ManagedFileCreatesThenReusesAValidPfx()
    {
        string workingDirectory = CreateWorkingDirectory();
        string path = Path.Combine(workingDirectory, "managed.pfx");
        var options = new ManagedCertificateFileOptions
        {
            FilePath = path,
            Password = "test-password",
            Replacement = CreateReplacement("managed.test")
        };

        try
        {
            ManagedCertificateResult created = ManagedCertificateFile.LoadOrCreate(options);
            string createdThumbprint;
            using (created.Certificate)
            {
                createdThumbprint = created.Certificate.Thumbprint;
                Assert.AreEqual(ManagedCertificateAction.GeneratedForMissingFile, created.Action);
                Assert.IsTrue(created.Persisted);
            }

            ManagedCertificateResult loaded = ManagedCertificateFile.LoadOrCreate(options);
            using (loaded.Certificate)
            {
                Assert.AreEqual(ManagedCertificateAction.Loaded, loaded.Action);
                Assert.IsTrue(loaded.Persisted);
                Assert.AreEqual(createdThumbprint, loaded.Certificate.Thumbprint);
            }
        }
        finally
        {
            Directory.Delete(workingDirectory, recursive: true);
        }
    }

    [TestMethod]
    public void ReplaceAnyUnusableReplacesExpiredCorruptAndPasswordMismatchedPfxFiles()
    {
        string workingDirectory = CreateWorkingDirectory();
        string path = Path.Combine(workingDirectory, "managed.pfx");

        try
        {
            WriteExpiredCertificate(path, "old-password");
            ManagedCertificateResult expiredReplacement = ManagedCertificateFile.LoadOrCreate(
                new ManagedCertificateFileOptions
                {
                    FilePath = path,
                    Password = "old-password",
                    RecoveryMode = CertificateRecoveryMode.ReplaceAnyUnusable,
                    Replacement = CreateReplacement("managed.test")
                });
            using (expiredReplacement.Certificate)
            {
                Assert.AreEqual(
                    ManagedCertificateAction.GeneratedForExpiredFile,
                    expiredReplacement.Action);
                Assert.IsTrue(expiredReplacement.Persisted);
            }

            File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4 });
            ManagedCertificateResult corruptReplacement = ManagedCertificateFile.LoadOrCreate(
                new ManagedCertificateFileOptions
                {
                    FilePath = path,
                    Password = "new-password",
                    RecoveryMode = CertificateRecoveryMode.ReplaceAnyUnusable,
                    Replacement = CreateReplacement("managed.test")
                });
            using (corruptReplacement.Certificate)
            {
                Assert.AreEqual(ManagedCertificateAction.GeneratedForImportFailure, corruptReplacement.Action);
                Assert.IsTrue(corruptReplacement.Persisted);
                Assert.IsInstanceOfType<CryptographicException>(corruptReplacement.LoadException);
            }

            ManagedCertificateResult passwordReplacement = ManagedCertificateFile.LoadOrCreate(
                new ManagedCertificateFileOptions
                {
                    FilePath = path,
                    Password = "different-password",
                    RecoveryMode = CertificateRecoveryMode.ReplaceAnyUnusable,
                    Replacement = CreateReplacement("managed.test")
                });
            using (passwordReplacement.Certificate)
            {
                Assert.AreEqual(ManagedCertificateAction.GeneratedForImportFailure, passwordReplacement.Action);
                Assert.IsTrue(passwordReplacement.Persisted);
                Assert.IsInstanceOfType<CryptographicException>(passwordReplacement.LoadException);
            }
        }
        finally
        {
            Directory.Delete(workingDirectory, recursive: true);
        }
    }

    [TestMethod]
    public void DefaultRecoveryPreservesPasswordMismatchedExistingPfx()
    {
        string workingDirectory = CreateWorkingDirectory();
        string path = Path.Combine(workingDirectory, "production.pfx");

        try
        {
            using X509Certificate2 production = SelfSignedCertificateFactory.Create(
                CreateReplacement("production.test"));
            File.WriteAllBytes(path, production.Export(X509ContentType.Pfx, "correct-password"));
            byte[] originalFile = File.ReadAllBytes(path);

            ManagedCertificateResult recovery = ManagedCertificateFile.LoadOrCreate(
                new ManagedCertificateFileOptions
                {
                    FilePath = path,
                    Password = "wrong-password",
                    Replacement = CreateReplacement("recovery.test")
                });

            using (recovery.Certificate)
            {
                Assert.AreEqual(ManagedCertificateAction.GeneratedForImportFailure, recovery.Action);
                Assert.IsFalse(recovery.Persisted);
                Assert.IsTrue(recovery.ExistingFilePreserved);
                Assert.IsInstanceOfType<CryptographicException>(recovery.LoadException);
                Assert.AreNotEqual(production.Thumbprint, recovery.Certificate.Thumbprint);
            }

            CollectionAssert.AreEqual(originalFile, File.ReadAllBytes(path));

            ManagedCertificateResult restored = ManagedCertificateFile.LoadOrCreate(
                new ManagedCertificateFileOptions
                {
                    FilePath = path,
                    Password = "correct-password",
                    Replacement = CreateReplacement("recovery.test")
                });
            using (restored.Certificate)
            {
                Assert.AreEqual(ManagedCertificateAction.Loaded, restored.Action);
                Assert.AreEqual(production.Thumbprint, restored.Certificate.Thumbprint);
            }
        }
        finally
        {
            Directory.Delete(workingDirectory, recursive: true);
        }
    }

    [TestMethod]
    public void ReplaceExpiredReplacesOnlyExpiredExistingPfx()
    {
        string workingDirectory = CreateWorkingDirectory();
        string path = Path.Combine(workingDirectory, "managed.pfx");
        var options = new ManagedCertificateFileOptions
        {
            FilePath = path,
            Password = "test-password",
            RecoveryMode = CertificateRecoveryMode.ReplaceExpired,
            Replacement = CreateReplacement("managed.test")
        };

        try
        {
            WriteNotYetValidCertificate(path, options.Password!);
            byte[] notYetValidFile = File.ReadAllBytes(path);

            ManagedCertificateResult notYetValidRecovery = ManagedCertificateFile.LoadOrCreate(options);
            using (notYetValidRecovery.Certificate)
            {
                Assert.AreEqual(
                    ManagedCertificateAction.GeneratedForNotYetValidFile,
                    notYetValidRecovery.Action);
                Assert.IsFalse(notYetValidRecovery.Persisted);
                Assert.IsTrue(notYetValidRecovery.ExistingFilePreserved);
            }

            CollectionAssert.AreEqual(notYetValidFile, File.ReadAllBytes(path));

            WriteExpiredCertificate(path, options.Password!);
            byte[] expiredFile = File.ReadAllBytes(path);

            ManagedCertificateResult expiredRecovery = ManagedCertificateFile.LoadOrCreate(options);
            using (expiredRecovery.Certificate)
            {
                Assert.AreEqual(ManagedCertificateAction.GeneratedForExpiredFile, expiredRecovery.Action);
                Assert.IsTrue(expiredRecovery.Persisted);
                Assert.IsFalse(expiredRecovery.ExistingFilePreserved);
            }

            Assert.IsFalse(expiredFile.SequenceEqual(File.ReadAllBytes(path)));
        }
        finally
        {
            Directory.Delete(workingDirectory, recursive: true);
        }
    }

    [TestMethod]
    public void PersistenceFailureReturnsAUsableInMemoryCertificate()
    {
        string workingDirectory = CreateWorkingDirectory();
        string parentFile = Path.Combine(workingDirectory, "not-a-directory");
        File.WriteAllText(parentFile, "blocks directory creation");

        try
        {
            ManagedCertificateResult result = ManagedCertificateFile.LoadOrCreate(
                new ManagedCertificateFileOptions
                {
                    FilePath = Path.Combine(parentFile, "managed.pfx"),
                    Password = "test-password",
                    Replacement = CreateReplacement("managed.test")
                });

            using (result.Certificate)
            {
                Assert.AreEqual(ManagedCertificateAction.GeneratedForMissingFile, result.Action);
                Assert.IsFalse(result.Persisted);
                Assert.IsNotNull(result.PersistenceException);
                Assert.IsTrue(result.Certificate.HasPrivateKey);
            }
        }
        finally
        {
            Directory.Delete(workingDirectory, recursive: true);
        }
    }

    [TestMethod]
    public void MoveFailureReturnsInMemoryCertificateAndRemovesTemporaryPfx()
    {
        string workingDirectory = CreateWorkingDirectory();
        string destinationDirectory = Path.Combine(workingDirectory, "managed.pfx");
        Directory.CreateDirectory(destinationDirectory);

        try
        {
            ManagedCertificateResult result = ManagedCertificateFile.LoadOrCreate(
                new ManagedCertificateFileOptions
                {
                    FilePath = destinationDirectory,
                    Password = "test-password",
                    RecoveryMode = CertificateRecoveryMode.ReplaceAnyUnusable,
                    Replacement = CreateReplacement("managed.test")
                });

            using (result.Certificate)
            {
                Assert.AreEqual(ManagedCertificateAction.GeneratedForAccessFailure, result.Action);
                Assert.IsFalse(result.Persisted);
                Assert.IsTrue(
                    result.PersistenceException is IOException or UnauthorizedAccessException,
                    $"Unexpected persistence exception: {result.PersistenceException?.GetType().FullName}");
                Assert.IsTrue(result.Certificate.HasPrivateKey);
            }

            Assert.AreEqual(
                0,
                Directory.GetFiles(workingDirectory, ".managed.pfx.*.tmp").Length,
                "The validated temporary PFX was not removed after File.Move failed.");
        }
        finally
        {
            Directory.Delete(workingDirectory, recursive: true);
        }
    }

    [TestMethod]
    public void DefaultRecoveryDoesNotReplaceAnUnreadableExistingPath()
    {
        string workingDirectory = CreateWorkingDirectory();
        string unreadablePath = Path.Combine(workingDirectory, "production.pfx");
        Directory.CreateDirectory(unreadablePath);

        try
        {
            ManagedCertificateResult result = ManagedCertificateFile.LoadOrCreate(
                new ManagedCertificateFileOptions
                {
                    FilePath = unreadablePath,
                    Password = "test-password",
                    Replacement = CreateReplacement("recovery.test")
                });

            using (result.Certificate)
            {
                Assert.IsTrue(
                    result.Action is ManagedCertificateAction.GeneratedForAccessFailure or
                        ManagedCertificateAction.GeneratedForReadFailure,
                    $"Unexpected recovery action: {result.Action}");
                Assert.IsFalse(result.Persisted);
                Assert.IsTrue(result.ExistingFilePreserved);
                Assert.IsNotNull(result.LoadException);
                Assert.IsNull(result.PersistenceException);
            }

            Assert.IsTrue(Directory.Exists(unreadablePath));
            Assert.AreEqual(0, Directory.GetFiles(workingDirectory, ".production.pfx.*.tmp").Length);
        }
        finally
        {
            Directory.Delete(workingDirectory, recursive: true);
        }
    }

    private static SelfSignedCertificateOptions CreateReplacement(string commonName)
    {
        return new SelfSignedCertificateOptions
        {
            Subject = new CertificateSubject { CommonName = commonName },
            Purpose = CertificatePurpose.TlsServer,
            KeyProfile = CertificateKeyProfile.Rsa2048Sha256,
            Validity = TimeSpan.FromDays(30),
            DnsNames = new[] { commonName, $"*.{commonName}" },
            IpAddresses = new[] { IPAddress.Loopback }
        };
    }

    private static void WriteExpiredCertificate(string path, string password)
    {
        using RSA key = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=expired.test",
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        using X509Certificate2 expired = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddYears(-2),
            DateTimeOffset.UtcNow.AddYears(-1));
        File.WriteAllBytes(path, expired.Export(X509ContentType.Pfx, password));
    }

    private static void WriteNotYetValidCertificate(string path, string password)
    {
        using RSA key = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=future.test",
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        using X509Certificate2 future = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(1),
            DateTimeOffset.UtcNow.AddYears(1));
        File.WriteAllBytes(path, future.Export(X509ContentType.Pfx, password));
    }

    private static string CreateWorkingDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"Eigenverft.WebLib.Infrastructure.Tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
