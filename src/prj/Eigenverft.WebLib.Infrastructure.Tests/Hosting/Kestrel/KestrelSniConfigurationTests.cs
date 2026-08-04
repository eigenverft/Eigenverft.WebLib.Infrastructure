using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

using Eigenverft.WebLib.Infrastructure.Hosting.Kestrel;
using Eigenverft.WebLib.Infrastructure.Security.Certificates;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Eigenverft.WebLib.Infrastructure.Tests.Hosting.Kestrel;

[TestClass]
public sealed class KestrelSniConfigurationTests
{
    [TestMethod]
    public async Task ExistingConfigurationContractBuildsTypedSubjectAlternativeNames()
    {
        string workingDirectory = CreateWorkingDirectory();
        string certificateDirectory = Path.Combine(workingDirectory, "certs");
        WebApplication? application = null;

        try
        {
            WebApplicationBuilder builder = CreateBuilder(
                workingDirectory,
                new Dictionary<string, string?>
                {
                    ["KestrelSettings:HTTP_PORT"] = ReserveTcpPort().ToString(),
                    ["KestrelSettings:AddServerHeader"] = "true",
                    ["CertificatesDirectory"] = "certs",
                    ["CertificatesMappingSettings:0:SNI"] = "localhost",
                    ["CertificatesMappingSettings:0:FileName"] = "localhost.pfx",
                    ["CertificatesMappingSettings:0:Password"] = "test-password",
                    ["CertificatesMappingSettings:0:AdditionalSelfSignedCertificateDnsNames:0"] = "*.localhost",
                    ["CertificatesMappingSettings:0:AdditionalSelfSignedCertificateDnsNames:1"] = "127.0.0.1",
                    ["CertificatesMappingSettings:0:AdditionalSelfSignedCertificateIpAddresses:0"] = "::1"
                });

            builder.WebHost.ConfigureKestrelSniFromConfiguration();

            application = builder.Build();
            KestrelServerOptions options = application.Services
                .GetRequiredService<IOptions<KestrelServerOptions>>()
                .Value;
            await application.StartAsync();

            Assert.IsTrue(options.AddServerHeader);
            string certificatePath = Path.Combine(certificateDirectory, "localhost.pfx");
            Assert.IsTrue(File.Exists(certificatePath));

            using X509Certificate2 certificate = X509CertificateLoader.LoadPkcs12FromFile(
                certificatePath,
                "test-password",
                X509KeyStorageFlags.EphemeralKeySet);
            X509SubjectAlternativeNameExtension sanExtension = ReadSubjectAlternativeNames(certificate);

            CollectionAssert.AreEquivalent(
                new[] { "localhost", "*.localhost" },
                sanExtension.EnumerateDnsNames().ToArray());
            CollectionAssert.AreEquivalent(
                new[] { IPAddress.Loopback, IPAddress.IPv6Loopback },
                sanExtension.EnumerateIPAddresses().ToArray());
        }
        finally
        {
            await DisposeApplicationAsync(application);
            Directory.Delete(workingDirectory, recursive: true);
        }
    }

    [TestMethod]
    public async Task InvalidStringPoliciesUseSafeDefaults()
    {
        string workingDirectory = CreateWorkingDirectory();
        WebApplication? application = null;

        try
        {
            WebApplicationBuilder builder = CreateBuilder(
                workingDirectory,
                new Dictionary<string, string?>
                {
                    ["KestrelSettings:HTTP_PORT"] = ReserveTcpPort().ToString(),
                    ["KestrelSettings:ListenScope"] = "999",
                    ["KestrelSettings:Protocols"] = "999",
                    ["KestrelSettings:TlsProtocolPolicy"] = "999",
                    ["CertificatesMappingSettings:0:SNI"] = "localhost",
                    ["CertificatesMappingSettings:0:FileName"] = "localhost.pfx",
                    ["CertificatesMappingSettings:0:Password"] = "test-password"
                });

            builder.WebHost.ConfigureKestrelSniFromConfiguration();
            application = builder.Build();
            await application.StartAsync();

            Assert.IsNotNull(application);
        }
        finally
        {
            await DisposeApplicationAsync(application);
            Directory.Delete(workingDirectory, recursive: true);
        }
    }

    [TestMethod]
    public async Task StrictTlsPolicyIsAppliedToTheConfiguredHttpsEndpoint()
    {
        string workingDirectory = CreateWorkingDirectory();
        int port = ReserveTcpPort();
        WebApplication? application = null;

        try
        {
            WebApplicationBuilder builder = CreateHttpsBuilder(
                workingDirectory,
                port,
                new Dictionary<string, string?>
                {
                    ["KestrelSettings:TlsProtocolPolicy"] = "Strict",
                    ["CertificatesMappingSettings:0:SNI"] = "localhost",
                    ["CertificatesMappingSettings:0:FileName"] = "localhost.pfx",
                    ["CertificatesMappingSettings:0:Password"] = "test-password"
                });

            application = builder.Build();
            await application.StartAsync();

            Exception? handshakeFailure = null;
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(IPAddress.Loopback, port);
                using var tls = new SslStream(
                    client.GetStream(),
                    leaveInnerStreamOpen: false,
                    static (_, _, _, _) => true);
                await tls.AuthenticateAsClientAsync(
                    new SslClientAuthenticationOptions
                    {
                        TargetHost = "localhost",
                        EnabledSslProtocols = SslProtocols.Tls12
                    });
            }
            catch (Exception exception) when (exception is AuthenticationException or IOException)
            {
                handshakeFailure = exception;
            }

            Assert.IsNotNull(
                handshakeFailure,
                "A TLS 1.2 client unexpectedly connected to an endpoint configured for TLS 1.3 only.");
        }
        finally
        {
            await DisposeApplicationAsync(application);
            Directory.Delete(workingDirectory, recursive: true);
        }
    }

    [TestMethod]
    public async Task CertificateMappingsReloadAtomicallyAndKeepLastKnownGood()
    {
        string workingDirectory = CreateWorkingDirectory();
        int port = ReserveTcpPort();
        WebApplication? application = null;

        try
        {
            WebApplicationBuilder builder = CreateHttpsBuilder(
                workingDirectory,
                port,
                new Dictionary<string, string?>
                {
                    ["CertificatesMappingSettings:0:SNI"] = "localhost",
                    ["CertificatesMappingSettings:0:FileName"] = "first.pfx",
                    ["CertificatesMappingSettings:0:Password"] = "test-password"
                });

            application = builder.Build();
            await application.StartAsync();

            using X509Certificate2 first = await ReadServerCertificateAsync(port, "localhost");

            builder.Configuration["CertificatesMappingSettings:0:FileName"] = "second.pfx";
            ((IConfigurationRoot)builder.Configuration).Reload();
            await WaitForFileAsync(Path.Combine(workingDirectory, "certs", "second.pfx"));

            using X509Certificate2 second = await ReadServerCertificateAsync(port, "localhost");
            Assert.AreNotEqual(first.Thumbprint, second.Thumbprint);

            string secondPath = Path.Combine(workingDirectory, "certs", "second.pfx");
            using X509Certificate2 externalReplacement = SelfSignedCertificateFactory.Create(
                new SelfSignedCertificateOptions
                {
                    Subject = new CertificateSubject { CommonName = "external.local" },
                    Purpose = CertificatePurpose.TlsServer,
                    KeyProfile = CertificateKeyProfile.Rsa3072Sha256,
                    Validity = TimeSpan.FromDays(30),
                    DnsNames = new[] { "localhost" }
                });
            File.WriteAllBytes(
                secondPath,
                externalReplacement.Export(X509ContentType.Pfx, "test-password"));
            ((IConfigurationRoot)builder.Configuration).Reload();

            using X509Certificate2 afterExternalReplacement = await ReadServerCertificateAsync(port, "localhost");
            Assert.AreEqual(externalReplacement.Thumbprint, afterExternalReplacement.Thumbprint);

            byte[] unchangedLengthReplacement = File.ReadAllBytes(secondPath);
            DateTime unchangedWriteTime = File.GetLastWriteTimeUtc(secondPath);
            for (var index = 0; index < unchangedLengthReplacement.Length; index++)
            {
                unchangedLengthReplacement[index] ^= 0xff;
            }

            File.WriteAllBytes(secondPath, unchangedLengthReplacement);
            File.SetLastWriteTimeUtc(secondPath, unchangedWriteTime);
            Assert.AreEqual(unchangedLengthReplacement.Length, new FileInfo(secondPath).Length);
            Assert.AreEqual(unchangedWriteTime, File.GetLastWriteTimeUtc(secondPath));

            ((IConfigurationRoot)builder.Configuration).Reload();
            using X509Certificate2 afterPreservedCorruptFile =
                await ReadServerCertificateAsync(port, "localhost");
            Assert.AreEqual(
                afterExternalReplacement.Thumbprint,
                afterPreservedCorruptFile.Thumbprint,
                "The default recovery mode replaced a still-usable last-known-good certificate.");
            CollectionAssert.AreEqual(unchangedLengthReplacement, File.ReadAllBytes(secondPath));

            builder.Configuration["CertificatesMappingSettings:0:CertificateRecoveryMode"] =
                "ReplaceAnyUnusable";
            ((IConfigurationRoot)builder.Configuration).Reload();

            using X509Certificate2 afterAllowlistedReplacement =
                await ReadServerCertificateAsync(port, "localhost");
            Assert.AreNotEqual(
                afterExternalReplacement.Thumbprint,
                afterAllowlistedReplacement.Thumbprint);

            builder.Configuration["CertificatesMappingSettings:0:FileName"] = @"..\outside.pfx";
            ((IConfigurationRoot)builder.Configuration).Reload();

            using X509Certificate2 afterInvalidReload = await ReadServerCertificateAsync(port, "localhost");
            Assert.AreEqual(afterAllowlistedReplacement.Thumbprint, afterInvalidReload.Thumbprint);
            Assert.IsFalse(File.Exists(Path.Combine(workingDirectory, "outside.pfx")));
        }
        finally
        {
            await DisposeApplicationAsync(application);
            Directory.Delete(workingDirectory, recursive: true);
        }
    }

    [TestMethod]
    public async Task NonBoundarySuffixUsesTheConfiguredFallbackCertificate()
    {
        string workingDirectory = CreateWorkingDirectory();
        int port = ReserveTcpPort();
        WebApplication? application = null;

        try
        {
            WebApplicationBuilder builder = CreateHttpsBuilder(
                workingDirectory,
                port,
                new Dictionary<string, string?>
                {
                    ["CertificatesMappingSettings:0:SNI"] = "fallback.local",
                    ["CertificatesMappingSettings:0:FileName"] = "fallback.pfx",
                    ["CertificatesMappingSettings:0:Password"] = "test-password",
                    ["CertificatesMappingSettings:1:SNI"] = "example.com",
                    ["CertificatesMappingSettings:1:FileName"] = "example.pfx",
                    ["CertificatesMappingSettings:1:Password"] = "test-password"
                });

            application = builder.Build();
            await application.StartAsync();

            using X509Certificate2 certificate = await ReadServerCertificateAsync(port, "notexample.com");
            Assert.AreEqual(
                "fallback.local",
                certificate.GetNameInfo(X509NameType.SimpleName, forIssuer: false));
        }
        finally
        {
            await DisposeApplicationAsync(application);
            Directory.Delete(workingDirectory, recursive: true);
        }
    }

    [TestMethod]
    public async Task PersistenceFailureStillProvidesAnInMemoryHttpsCertificate()
    {
        string workingDirectory = CreateWorkingDirectory();
        string blockedCertificateDirectory = Path.Combine(workingDirectory, "not-a-directory");
        File.WriteAllText(blockedCertificateDirectory, "blocks directory creation");
        int port = ReserveTcpPort();
        WebApplication? application = null;

        try
        {
            WebApplicationBuilder builder = CreateBuilder(
                workingDirectory,
                new Dictionary<string, string?>
                {
                    ["KestrelSettings:HTTPS_PORT"] = port.ToString(),
                    ["KestrelSettings:ListenScope"] = "AnyIP",
                    ["CertificatesDirectory"] = blockedCertificateDirectory,
                    ["CertificatesMappingSettings:0:SNI"] = "localhost",
                    ["CertificatesMappingSettings:0:FileName"] = "localhost.pfx",
                    ["CertificatesMappingSettings:0:Password"] = "test-password"
                });
            builder.WebHost.ConfigureKestrelSniFromConfiguration();

            application = builder.Build();
            await application.StartAsync();

            using X509Certificate2 certificate = await ReadServerCertificateAsync(port, "localhost");
            Assert.AreEqual(
                "localhost",
                certificate.GetNameInfo(X509NameType.SimpleName, forIssuer: false));
            Assert.IsFalse(File.Exists(Path.Combine(blockedCertificateDirectory, "localhost.pfx")));
        }
        finally
        {
            await DisposeApplicationAsync(application);
            Directory.Delete(workingDirectory, recursive: true);
        }
    }

    [TestMethod]
    public async Task PreservedFileRecoveryRemainsStableAcrossReloads()
    {
        string workingDirectory = CreateWorkingDirectory();
        string certificateDirectory = Path.Combine(workingDirectory, "certs");
        string certificatePath = Path.Combine(certificateDirectory, "production.pfx");
        byte[] unreadablePfx = { 1, 2, 3, 4 };
        Directory.CreateDirectory(certificateDirectory);
        File.WriteAllBytes(certificatePath, unreadablePfx);
        int port = ReserveTcpPort();
        WebApplication? application = null;

        try
        {
            WebApplicationBuilder builder = CreateHttpsBuilder(
                workingDirectory,
                port,
                new Dictionary<string, string?>
                {
                    ["CertificatesMappingSettings:0:SNI"] = "localhost",
                    ["CertificatesMappingSettings:0:FileName"] = "production.pfx",
                    ["CertificatesMappingSettings:0:Password"] = "test-password"
                });

            application = builder.Build();
            await application.StartAsync();

            using X509Certificate2 initialRecovery = await ReadServerCertificateAsync(port, "localhost");
            CollectionAssert.AreEqual(unreadablePfx, File.ReadAllBytes(certificatePath));

            ((IConfigurationRoot)builder.Configuration).Reload();

            using X509Certificate2 afterReload = await ReadServerCertificateAsync(port, "localhost");
            Assert.AreEqual(initialRecovery.Thumbprint, afterReload.Thumbprint);
            CollectionAssert.AreEqual(unreadablePfx, File.ReadAllBytes(certificatePath));
        }
        finally
        {
            await DisposeApplicationAsync(application);
            Directory.Delete(workingDirectory, recursive: true);
        }
    }

    [TestMethod]
    public async Task CertificateMappingRejectsSymbolicLinkEscapingCertificateDirectory()
    {
        string workingDirectory = CreateWorkingDirectory();
        string certificateDirectory = Path.Combine(workingDirectory, "certs");
        string outsideDirectory = Path.Combine(workingDirectory, "outside");
        Directory.CreateDirectory(certificateDirectory);
        Directory.CreateDirectory(outsideDirectory);
        string outsidePfx = Path.Combine(outsideDirectory, "outside.pfx");
        File.WriteAllBytes(outsidePfx, new byte[] { 1, 2, 3, 4 });
        string linkedPfx = Path.Combine(certificateDirectory, "linked.pfx");
        WebApplication? application = null;

        try
        {
            try
            {
                File.CreateSymbolicLink(linkedPfx, outsidePfx);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                Assert.Inconclusive($"Symbolic links are unavailable in this test environment: {exception.Message}");
            }

            WebApplicationBuilder builder = CreateBuilder(
                workingDirectory,
                new Dictionary<string, string?>
                {
                    ["KestrelSettings:HTTP_PORT"] = ReserveTcpPort().ToString(),
                    ["CertificatesDirectory"] = certificateDirectory,
                    ["CertificatesMappingSettings:0:SNI"] = "localhost",
                    ["CertificatesMappingSettings:0:FileName"] = "linked.pfx",
                    ["CertificatesMappingSettings:0:Password"] = "test-password"
                });
            builder.WebHost.ConfigureKestrelSniFromConfiguration();
            application = builder.Build();

            Exception? startFailure = null;
            try
            {
                await application.StartAsync();
            }
            catch (Exception exception)
            {
                startFailure = exception;
            }

            Assert.IsNotNull(startFailure);
            StringAssert.Contains(startFailure.ToString(), "symbolic-link targets");
        }
        finally
        {
            await DisposeApplicationAsync(application);
            Directory.Delete(workingDirectory, recursive: true);
        }
    }

    private static WebApplicationBuilder CreateHttpsBuilder(
        string workingDirectory,
        int port,
        IDictionary<string, string?> additionalSettings)
    {
        var settings = new Dictionary<string, string?>(additionalSettings)
        {
            ["KestrelSettings:HTTPS_PORT"] = port.ToString(),
            ["KestrelSettings:ListenScope"] = "AnyIP",
            ["CertificatesDirectory"] = "certs"
        };

        WebApplicationBuilder builder = CreateBuilder(workingDirectory, settings);
        builder.WebHost.ConfigureKestrelSniFromConfiguration();
        return builder;
    }

    private static WebApplicationBuilder CreateBuilder(
        string workingDirectory,
        IDictionary<string, string?> settings)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(
            new WebApplicationOptions
            {
                Args = Array.Empty<string>(),
                ContentRootPath = workingDirectory
            });
        builder.Configuration.AddInMemoryCollection(settings);
        return builder;
    }

    private static X509SubjectAlternativeNameExtension ReadSubjectAlternativeNames(X509Certificate2 certificate)
    {
        X509Extension encoded = certificate.Extensions["2.5.29.17"]
            ?? throw new AssertFailedException("Generated certificate has no SAN extension.");
        return new X509SubjectAlternativeNameExtension(encoded.RawData, encoded.Critical);
    }

    private static async Task<X509Certificate2> ReadServerCertificateAsync(int port, string targetHost)
    {
        X509Certificate2? serverCertificate = null;
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);
        using var tls = new SslStream(
            client.GetStream(),
            leaveInnerStreamOpen: false,
            (_, certificate, _, _) =>
            {
                if (certificate is not null)
                {
                    serverCertificate = new X509Certificate2(certificate);
                }

                return true;
            });

        await tls.AuthenticateAsClientAsync(
            new SslClientAuthenticationOptions
            {
                TargetHost = targetHost,
                EnabledSslProtocols = SslProtocols.Tls12
            });

        return serverCertificate
            ?? throw new AssertFailedException("The TLS endpoint returned no server certificate.");
    }

    private static async Task WaitForFileAsync(string path)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            if (File.Exists(path))
            {
                return;
            }

            await Task.Delay(20);
        }

        Assert.Fail($"Expected file was not created: {path}");
    }

    private static int ReserveTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static string CreateWorkingDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"Eigenverft.WebLib.Infrastructure.Tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static async Task DisposeApplicationAsync(WebApplication? application)
    {
        if (application is null)
        {
            return;
        }

        try
        {
            await application.StopAsync();
        }
        finally
        {
            await application.DisposeAsync();
        }
    }
}
