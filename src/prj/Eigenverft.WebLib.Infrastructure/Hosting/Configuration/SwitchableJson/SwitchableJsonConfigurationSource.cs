using Microsoft.Extensions.Configuration;

namespace Eigenverft.WebLib.Infrastructure.Hosting.Configuration.SwitchableJson
{
    internal sealed class SwitchableJsonConfigurationSource : IConfigurationSource
    {
        private readonly SwitchableJsonConfigurationRuntime _runtime;

        public SwitchableJsonConfigurationSource(SwitchableJsonConfigurationRuntime runtime)
        {
            _runtime = runtime;
        }

        public IConfigurationProvider Build(IConfigurationBuilder builder)
        {
            // ConfigurationManager may call Build() again for every source when Sources/Properties are mutated. Returning the
            // same provider instance is unsafe because the manager replaces its provider set and later disposes the old set.
            // A fresh provider per Build follows the normal IConfigurationSource ownership contract. The stable runtime object
            // is shared intentionally: DI identity, CurrentSource, watcher generation and lifecycle subscriptions survive rebuilds.
            return new SwitchableJsonConfigurationProvider(_runtime);
        }
    }
}
