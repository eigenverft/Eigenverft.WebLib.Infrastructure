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
            // The registration creates one provider instance because the same runtime object must back both IConfiguration and
            // the keyed DI handle. A source-created provider plus a separate runtime binder is possible, but adds indirection
            // without changing the V1 semantics.
            return _provider;
        }
    }
}
