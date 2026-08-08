using System;
using System.Threading;

namespace Eigenverft.WebLib.Infrastructure.Hosting.Configuration.SwitchableJson
{
    /// <summary>
    /// Internal hand-off for an already committed prepared switch whose observer notifications are intentionally deferred.
    /// </summary>
    /// <remarks>
    /// State mutation is complete before this object is created. <see cref="Publish"/> only releases the IConfiguration reload and
    /// lifecycle notifications that belong to that completed commit. This lets higher-level orchestrators finish their own state
    /// transition and release their locks before arbitrary observer code can run.
    /// </remarks>
    internal sealed class SwitchableJsonDeferredCommit
    {
        private readonly SwitchableJsonConfigurationRuntime _runtime;
        private readonly SwitchableJsonConfigurationProvider? _providerToReload;
        private int _published;

        internal SwitchableJsonDeferredCommit(
            SwitchableJsonConfigurationRuntime runtime,
            SwitchableJsonSwitchResult result,
            SwitchableJsonConfigurationProvider? providerToReload)
        {
            ArgumentNullException.ThrowIfNull(runtime);
            ArgumentNullException.ThrowIfNull(result);
            _runtime = runtime;
            Result = result;
            _providerToReload = providerToReload;
        }

        internal SwitchableJsonSwitchResult Result { get; }

        internal void Publish()
        {
            if (Interlocked.Exchange(ref _published, 1) != 0)
            {
                return;
            }

            _runtime.PublishDeferredCommit(Result, _providerToReload);
        }
    }
}
