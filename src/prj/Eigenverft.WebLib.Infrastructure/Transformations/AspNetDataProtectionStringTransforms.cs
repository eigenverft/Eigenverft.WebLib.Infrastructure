using System;
using System.IO;
using System.Security.Cryptography;

using Eigenverft.NetLib.Infrastructure.Transformations;

using Microsoft.AspNetCore.DataProtection;

namespace Eigenverft.WebLib.Infrastructure.Transformations
{
    /// <summary>Creates reversible string transforms backed by ASP.NET Core Data Protection.</summary>
    public static class AspNetDataProtectionStringTransforms
    {
        /// <summary>Creates an ASP.NET Core Data Protection transform with explicit application and purpose isolation.</summary>
        /// <param name="keyDirectoryPath">The persistent Data Protection key-ring directory.</param>
        /// <param name="applicationName">The stable logical application discriminator.</param>
        /// <param name="purpose">The stable purpose isolating this protected data from other Data Protection uses.</param>
        /// <returns>A transform backed by the specified persistent Data Protection key ring.</returns>
        /// <remarks>
        /// <para>
        /// The key ring is durable state. Back up the complete ring with protected values and retain old keys while data may still
        /// depend on them. Losing keys can make existing transformed values permanently unavailable.
        /// </para>
        /// <para>
        /// Keep <paramref name="applicationName"/> and <paramref name="purpose"/> stable for as long as values must remain
        /// reversible. Changing either makes values unavailable to the new protector. Data Protection alone adds no machine
        /// binding; moving the same key ring and isolation context to another machine is sufficient unless another transform adds
        /// a machine-context requirement.
        /// </para>
        /// <para>
        /// Directory separation is not an ACL boundary and this helper does not configure an additional at-rest encryptor for the
        /// key-ring files. A missing directory is created; on an existing installation an unexpectedly new or empty ring should
        /// therefore be treated as lost state, not as successful migration.
        /// </para>
        /// </remarks>
        public static ReversibleStringTransform DataProtection(
            string keyDirectoryPath,
            string applicationName,
            string purpose)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(keyDirectoryPath);
            ArgumentException.ThrowIfNullOrWhiteSpace(applicationName);
            ArgumentException.ThrowIfNullOrWhiteSpace(purpose);

            string fullKeyDirectoryPath = Path.GetFullPath(keyDirectoryPath);
            Directory.CreateDirectory(fullKeyDirectoryPath);

            IDataProtectionProvider provider = DataProtectionProvider.Create(
                new DirectoryInfo(fullKeyDirectoryPath),
                builder => builder.SetApplicationName(applicationName));
            IDataProtector protector = provider.CreateProtector(purpose);

            return new ReversibleStringTransform(
                $"DataProtection({applicationName})",
                protector.Protect,
                (string value, out string original) => TryReverseDataProtection(value, protector, out original));
        }

        private static bool TryReverseDataProtection(string value, IDataProtector protector, out string original)
        {
            original = value;
            try
            {
                original = protector.Unprotect(value);
                return true;
            }
            catch (CryptographicException)
            {
                original = value;
                return false;
            }
        }
    }
}
