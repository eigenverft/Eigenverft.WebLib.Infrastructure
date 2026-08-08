using System;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Hosting;

namespace Eigenverft.WebLib.Infrastructure.Hosting.Configuration.ConfigurationSets
{
    /// <summary>Connects state-file watching and disposal to the host lifecycle.</summary>
    internal sealed class ConfigurationSetStateStoreHostedService : IHostedService, IDisposable
    {
        private readonly ConfigurationSetStateStore _store;
        private bool _disposed;

        public ConfigurationSetStateStoreHostedService(ConfigurationSetStateStore store)
        {
            ArgumentNullException.ThrowIfNull(store);
            _store = store;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _store.StartWatching();
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _store.StopWatching();
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _store.Dispose();
        }
    }
}
