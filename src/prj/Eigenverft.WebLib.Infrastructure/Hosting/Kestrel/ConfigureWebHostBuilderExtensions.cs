using System;
using System.Security.Authentication;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Eigenverft.WebLib.Infrastructure.Hosting.Kestrel
{
    /// <summary>Provides configuration-driven Kestrel hosting extensions.</summary>
    public static partial class ConfigureWebHostBuilderExtensions
    {
        /// <summary>
        /// Configures startup-fixed Kestrel listeners and reloadable SNI certificate mappings.
        /// </summary>
        /// <param name="configureWebHostBuilder">The web-host builder to configure.</param>
        /// <param name="certDirOverride">
        /// An optional certificate-directory override. Relative paths are resolved against the content root.
        /// </param>
        /// <param name="kestrelSettingsSectionPath">The configuration section containing startup-fixed Kestrel settings.</param>
        /// <remarks>
        /// <para>
        /// Add the configuration sources before building the application, then call this method once:
        /// </para>
        /// <code><![CDATA[
        /// builder.WebHost.ConfigureKestrelSniFromConfiguration(
        ///     certDirOverride: directories[DefaultDirectory.ApplicationCerts]);
        /// ]]></code>
        /// <para>Minimal configuration:</para>
        /// <code><![CDATA[
        /// {
        ///   "KestrelSettings": {
        ///     "HTTP_PORT": 8080,
        ///     "HTTPS_PORT": 8443,
        ///     "ListenScope": "Localhost",
        ///     "AddServerHeader": false,
        ///     "Protocols": "Http1AndHttp2",
        ///     "PreferLongestSuffixMatch": true,
        ///     "TlsProtocolPolicy": "Default"
        ///   },
        ///   "CertificatesMappingSettings": [
        ///     {
        ///       "SNI": "localhost",
        ///       "FileName": "localhost.pfx",
        ///       "Password": "change-me",
        ///       "CertificateRecoveryMode": "PreserveExisting",
        ///       "AdditionalSelfSignedCertificateDnsNames": ["*.localhost"],
        ///       "AdditionalSelfSignedCertificateIpAddresses": ["127.0.0.1", "::1"]
        ///     }
        ///   ]
        /// }
        /// ]]></code>
        /// <para>
        /// The certificate-directory override takes precedence over the top-level <c>CertificatesDirectory</c>
        /// setting. When neither is supplied, <c>certs</c> below the content root is used.
        /// </para>
        /// <para>
        /// <c>CertificatesMappingSettings</c> is the only hot-reload boundary. Ports, bind scope,
        /// protocols, TLS policy, matching strategy, and certificate directory require a host restart.
        /// A failed reload keeps the last-known-good certificate generation active.
        /// </para>
        /// <para>
        /// Missing PFX files are created as self-signed TLS server certificates. Each mapping can opt into
        /// replacing existing unusable PFX files through <c>CertificateRecoveryMode</c>; the default
        /// <c>PreserveExisting</c> mode never overwrites an existing unusable file.
        /// </para>
        /// </remarks>
        public static void ConfigureKestrelSniFromConfiguration(
            this ConfigureWebHostBuilder configureWebHostBuilder,
            string? certDirOverride = null,
            string kestrelSettingsSectionPath = "KestrelSettings")
        {
            ArgumentNullException.ThrowIfNull(configureWebHostBuilder);
            if (string.IsNullOrWhiteSpace(kestrelSettingsSectionPath))
            {
                throw new ArgumentException("A Kestrel settings section path is required.", nameof(kestrelSettingsSectionPath));
            }

            var certificateState = new SniCertificateState();

#pragma warning disable ASP0012 // ConfigureWebHostBuilder does not expose a Services collection.
            configureWebHostBuilder.ConfigureServices(services =>
            {
                services.AddSingleton<IHostedService>(provider =>
                {
                    certificateState.AttachHostingServices(
                        provider.GetRequiredService<ILoggerFactory>().CreateLogger<SniCertificateState>(),
                        provider.GetRequiredService<IHostApplicationLifetime>());
                    return certificateState;
                });
            });
#pragma warning restore ASP0012

            configureWebHostBuilder.ConfigureKestrel((context, serverOptions) =>
            {
                string contentRoot = context.HostingEnvironment.ContentRootPath ?? AppContext.BaseDirectory;
                KestrelStartupPlan startup = KestrelSniConfiguration.BindStartup(
                    context.Configuration,
                    kestrelSettingsSectionPath,
                    certDirOverride,
                    contentRoot);

                bool httpEnabled = TryGetEnabledHttpPort(startup.HttpPort, out int httpPort);
                bool httpsEnabled = startup.HttpsPort.HasValue;

                if (!httpEnabled && !httpsEnabled)
                {
                    throw new ArgumentException("At least one of HTTP_PORT or HTTPS_PORT must be enabled.");
                }

                if (httpsEnabled)
                {
                    ValidatePort(startup.HttpsPort!.Value, "HTTPS_PORT");
                }
                else if (startup.TlsProtocolPolicy != TlsProtocolPolicy.Default)
                {
                    throw new ArgumentException("TlsProtocolPolicy can only be set when HTTPS_PORT is enabled.");
                }

                // Certificate mappings remain required even for the HTTP-only form of this SNI feature.
                // Managed PFX failures produce a policy-controlled recovery certificate before listeners
                // are published; preserving an existing file never prevents a cold start.
                certificateState.Configure(
                    context.Configuration,
                    startup.CertificateDirectory,
                    startup.PreferLongestSuffixMatch);

                serverOptions.AddServerHeader = startup.AddServerHeader;
                Action<int, Action<ListenOptions>> listen = startup.ListenScope == ListenScope.Localhost
                    ? (port, configure) => serverOptions.ListenLocalhost(port, configure)
                    : (port, configure) => serverOptions.ListenAnyIP(port, configure);

                if (httpEnabled)
                {
                    listen(httpPort, options =>
                    {
                        // This helper intentionally exposes a conventional plaintext HTTP/1 endpoint.
                        // HTTP/2 negotiation for a combined HTTP/1+HTTP/2 endpoint relies on TLS ALPN.
                        options.Protocols = HttpProtocols.Http1;
                    });
                }

                if (httpsEnabled)
                {
                    listen(startup.HttpsPort!.Value, options =>
                    {
                        options.Protocols = startup.Protocols ?? HttpProtocols.Http1AndHttp2;
                        options.UseHttps(https =>
                        {
                            // Apply the policy to this endpoint. ConfigureHttpsDefaults would only affect
                            // HTTPS endpoints created after the defaults are registered.
                            https.SslProtocols = MapTlsPolicy(startup.TlsProtocolPolicy);
                            https.ServerCertificateSelector = (_, requestedSni) => certificateState.Select(requestedSni);
                        });
                    });
                }
            });
        }

        private static bool TryGetEnabledHttpPort(int? configuredPort, out int port)
        {
            port = default;
            if (!configuredPort.HasValue || configuredPort.Value <= 0)
            {
                return false;
            }

            ValidatePort(configuredPort.Value, "HTTP_PORT");
            port = configuredPort.Value;
            return true;
        }

        private static void ValidatePort(int port, string configurationKey)
        {
            if (port is < 1 or > 65535)
            {
                throw new ArgumentOutOfRangeException(configurationKey, port, "Port must be in range 1..65535.");
            }
        }

        private static SslProtocols MapTlsPolicy(TlsProtocolPolicy policy)
        {
            switch (policy)
            {
                case TlsProtocolPolicy.Default:
                    return SslProtocols.Tls12 | SslProtocols.Tls13;

                case TlsProtocolPolicy.Strict:
                    return SslProtocols.Tls13;

                case TlsProtocolPolicy.MaximumTlsCompatibility:
#pragma warning disable SYSLIB0039 // Explicit opt-in compatibility policy.
                    return SslProtocols.Tls | SslProtocols.Tls11 | SslProtocols.Tls12 | SslProtocols.Tls13;
#pragma warning restore SYSLIB0039

                case TlsProtocolPolicy.Legacy:
#pragma warning disable CS0618 // Explicit opt-in legacy policy.
#pragma warning disable SYSLIB0039 // Explicit opt-in legacy policy.
                    return SslProtocols.Ssl2 | SslProtocols.Ssl3 | SslProtocols.Tls |
                        SslProtocols.Tls11 | SslProtocols.Tls12 | SslProtocols.Tls13;
#pragma warning restore SYSLIB0039
#pragma warning restore CS0618

                default:
                    throw new ArgumentOutOfRangeException(nameof(policy), policy, "Unsupported TLS protocol policy.");
            }
        }
    }
}
