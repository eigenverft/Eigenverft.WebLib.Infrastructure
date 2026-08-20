using System;
using System.Reflection;

using Eigenverft.NetLib.Infrastructure.Hosting.Configuration.Values;
using Eigenverft.NetLib.Infrastructure.Hosting.DirectoryLayout;

using Eigenverft.WebLib.Infrastructure.Transformations;

namespace Eigenverft.WebLib.Infrastructure.Hosting.Configuration.Values
{
    /// <summary>Creates configuration-value codecs backed by ASP.NET Core Data Protection.</summary>
    /// <remarks>
    /// Each result is an ordinary <see cref="ConfigurationValueCodec"/> and can be used independently or at any position
    /// in <see cref="ConfigurationValueCodecs.Compose(ConfigurationValueCodec[])"/>.
    /// </remarks>
    public static class AspNetDataProtectionConfigurationValueCodecs
    {
        /// <summary>
        /// Creates a Data Protection codec using the standard persistent key-ring directory and the process entry assembly
        /// as the application discriminator.
        /// </summary>
        /// <param name="directories">The registered application directory layout.</param>
        /// <param name="purpose">The stable purpose isolating this protected configuration value.</param>
        /// <exception cref="InvalidOperationException">The current process has no named entry assembly.</exception>
        public static ConfigurationValueCodec DataProtection(
            IAppDirectoryLayout directories,
            string purpose)
        {
            ArgumentNullException.ThrowIfNull(directories);

            string applicationName = Assembly.GetEntryAssembly()?.GetName().Name
                ?? throw new InvalidOperationException(
                    "The current process has no named entry assembly. Use the explicit DataProtection overload instead.");

            return DataProtection(
                directories[DefaultDirectory.ApplicationProtectionKeys],
                applicationName,
                purpose);
        }

        /// <summary>Creates a Data Protection codec with an explicit key-ring directory, application name, and purpose.</summary>
        /// <param name="keyDirectoryPath">The persistent Data Protection key-ring directory.</param>
        /// <param name="applicationName">The stable logical application discriminator.</param>
        /// <param name="purpose">The stable purpose isolating this protected configuration value.</param>
        public static ConfigurationValueCodec DataProtection(
            string keyDirectoryPath,
            string applicationName,
            string purpose)
        {
            return new ConfigurationValueCodec(
                nameof(AspNetDataProtectionStringTransforms.DataProtection),
                ConfigurationValueKind.DataProtection,
                AspNetDataProtectionStringTransforms.DataProtection(
                    keyDirectoryPath,
                    applicationName,
                    purpose));
        }
    }
}
