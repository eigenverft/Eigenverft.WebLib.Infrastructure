using Microsoft.Extensions.Configuration;

namespace Eigenverft.WebLib.Infrastructure.Hosting.Configuration.SwitchableJson
{
    internal sealed class SwitchableJsonConfigurationSource : IConfigurationSource
    {
        private readonly SwitchableJsonConfigurationProvider _provider;

        public SwitchableJsonConfigurationSource(SwitchableJsonConfigurationProvider provider)
        {
            _provider = provider;
        }

        public IConfigurationProvider Build(IConfigurationBuilder builder)
        {
            // Registration creates exactly one provider instance because IConfiguration and the keyed runtime handle must
            // observe the same CurrentSource/Data/generation/watcher state. Returning a fresh provider here would create two
            // independent realities: IConfiguration could watch one file while the DI handle switches another.
            //
            // ConfigurationManager owns this returned IConfigurationProvider and disposes it with the host/configuration root.
            // The keyed DI registration is only another reference to this same instance, not a second provider lifecycle.
            // A source-created provider plus a separate runtime binder is possible, but adds indirection without changing V1.
            return _provider;
        }
    }
}
