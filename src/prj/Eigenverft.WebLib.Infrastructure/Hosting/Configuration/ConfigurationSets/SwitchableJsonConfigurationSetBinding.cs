using System;
using System.Collections.Generic;

using Eigenverft.WebLib.Infrastructure.Hosting.Configuration.SwitchableJson;

namespace Eigenverft.WebLib.Infrastructure.Hosting.Configuration.ConfigurationSets
{
    /// <summary>
    /// Internal binding between one configuration-set axis and one switchable JSON runtime.
    /// </summary>
    internal sealed class SwitchableJsonConfigurationSetBinding
    {
        private readonly SwitchableJsonConfigurationRuntime _configuration;
        private readonly IReadOnlyDictionary<string, string> _sourcePaths;
        private readonly string _ownerName;

        public SwitchableJsonConfigurationSetBinding(
            string ownerName,
            ISwitchableJsonConfiguration configuration,
            IReadOnlyDictionary<string, string> sourcePaths)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(ownerName);
            ArgumentNullException.ThrowIfNull(configuration);
            ArgumentNullException.ThrowIfNull(sourcePaths);

            _configuration = configuration as SwitchableJsonConfigurationRuntime ??
                throw new NotSupportedException(
                    "Configuration-set binding requires the Eigenverft switchable JSON runtime so exclusive source-selection ownership can be enforced.");
            _sourcePaths = sourcePaths;
            _ownerName = ownerName;
        }

        public string Name => _configuration.Name;

        public void ClaimOwnership()
        {
            _configuration.ClaimSourceSelectionOwnership(_ownerName);
        }

        public void ReleaseOwnership()
        {
            _configuration.ReleaseSourceSelectionOwnership(_ownerName);
        }

        public SwitchableJsonSwitchPreparation Prepare(string setValue)
        {
            if (!_sourcePaths.TryGetValue(setValue, out string? sourcePath))
            {
                throw new InvalidOperationException(
                    $"Configuration set binding '{Name}' has no source mapping for value '{setValue}'.");
            }

            return _configuration.PrepareSwitchForOwner(_ownerName, sourcePath);
        }
    }
}
