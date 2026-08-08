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
        private readonly ISwitchableJsonConfiguration _configuration;
        private readonly IReadOnlyDictionary<string, string> _sourcePaths;

        public SwitchableJsonConfigurationSetBinding(
            ISwitchableJsonConfiguration configuration,
            IReadOnlyDictionary<string, string> sourcePaths)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            ArgumentNullException.ThrowIfNull(sourcePaths);
            _configuration = configuration;
            _sourcePaths = sourcePaths;
        }

        public string Name => _configuration.Name;

        public SwitchableJsonSwitchPreparation Prepare(string setValue)
        {
            if (!_sourcePaths.TryGetValue(setValue, out string? sourcePath))
            {
                throw new InvalidOperationException(
                    $"Configuration set binding '{Name}' has no source mapping for value '{setValue}'.");
            }

            return _configuration.PrepareSwitch(sourcePath);
        }
    }
}
