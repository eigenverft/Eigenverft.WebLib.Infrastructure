using System;
using System.Reflection;

namespace Eigenverft.WebLib.Infrastructure.Hosting.DirectoryLayout
{
    /// <summary>
    /// Identifies the standard directories used by a self-contained Eigenverft web host.
    /// </summary>
    public enum DefaultDirectory
    {
        /// <summary>Application log files.</summary>
        [DefaultDirectoryName("AppLogs")]
        ApplicationLogFiles,

        /// <summary>Persistent application data.</summary>
        [DefaultDirectoryName("AppData")]
        ApplicationData,

        /// <summary>Application certificates.</summary>
        [DefaultDirectoryName("AppCerts")]
        ApplicationCerts,

        /// <summary>Application configuration files.</summary>
        [DefaultDirectoryName("AppSettings")]
        ApplicationSettings,

        /// <summary>Static web content.</summary>
        [DefaultDirectoryName("wwwroot")]
        Web,
    }

    /// <summary>
    /// Declares the conventional folder name associated with a <see cref="DefaultDirectory"/> value.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
    public sealed class DefaultDirectoryNameAttribute : Attribute
    {
        /// <summary>
        /// Initializes a new instance of <see cref="DefaultDirectoryNameAttribute"/>.
        /// </summary>
        /// <param name="name">The direct-child folder name.</param>
        public DefaultDirectoryNameAttribute(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Directory name must not be null or whitespace.", nameof(name));
            }

            Name = name;
        }

        /// <summary>Gets the conventional folder name.</summary>
        public string Name { get; }
    }

    /// <summary>
    /// Provides metadata operations for <see cref="DefaultDirectory"/> values.
    /// </summary>
    public static class DefaultDirectoryExtensions
    {
        /// <summary>
        /// Gets the stable semantic key used by <see cref="AppDirectoryLayout"/>.
        /// </summary>
        public static string GetKey(this DefaultDirectory directory)
        {
            EnsureDefined(directory);
            return directory.ToString();
        }

        /// <summary>
        /// Gets the conventional direct-child folder name declared on the enum value.
        /// </summary>
        public static string GetDefaultFolderName(this DefaultDirectory directory)
        {
            EnsureDefined(directory);

            FieldInfo field = typeof(DefaultDirectory).GetField(directory.ToString())
                ?? throw new InvalidOperationException($"Unable to resolve metadata for directory '{directory}'.");

            DefaultDirectoryNameAttribute attribute = field.GetCustomAttribute<DefaultDirectoryNameAttribute>()
                ?? throw new InvalidOperationException($"Directory '{directory}' has no default folder name.");

            return attribute.Name;
        }

        private static void EnsureDefined(DefaultDirectory directory)
        {
            if (!Enum.IsDefined(typeof(DefaultDirectory), directory))
            {
                throw new ArgumentOutOfRangeException(nameof(directory), directory, "Unknown default directory.");
            }
        }
    }
}
