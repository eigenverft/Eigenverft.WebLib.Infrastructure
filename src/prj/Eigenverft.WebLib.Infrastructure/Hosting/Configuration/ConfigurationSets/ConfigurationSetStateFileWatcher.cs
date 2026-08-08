using System;
using System.IO;
using System.Threading;

using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.FileProviders.Physical;
using Microsoft.Extensions.Primitives;

namespace Eigenverft.WebLib.Infrastructure.Hosting.Configuration.ConfigurationSets
{
    /// <summary>Debounces physical notifications for one fixed configuration-set state file.</summary>
    internal sealed class ConfigurationSetStateFileWatcher : IDisposable
    {
        private readonly object _gate = new();
        private readonly PhysicalFileProvider _fileProvider;
        private readonly IDisposable _changeRegistration;
        private readonly Timer _reloadTimer;
        private readonly Action _reloadCallback;
        private readonly int _reloadDelayMilliseconds;
        private bool _disposed;

        private ConfigurationSetStateFileWatcher(
            PhysicalFileProvider fileProvider,
            string filter,
            int reloadDelayMilliseconds,
            Action reloadCallback)
        {
            _fileProvider = fileProvider;
            _reloadDelayMilliseconds = reloadDelayMilliseconds;
            _reloadCallback = reloadCallback;
            _reloadTimer = new Timer(ReloadTimerElapsed, null, Timeout.Infinite, Timeout.Infinite);
            _changeRegistration = ChangeToken.OnChange(
                () => _fileProvider.Watch(filter),
                ScheduleReload);
        }

        public static ConfigurationSetStateFileWatcher Create(
            string filePath,
            int reloadDelayMilliseconds,
            Action reloadCallback)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
            ArgumentNullException.ThrowIfNull(reloadCallback);

            string directory = Path.GetDirectoryName(filePath)
                ?? throw new InvalidOperationException($"State file '{filePath}' has no parent directory.");
            Directory.CreateDirectory(directory);

            var provider = new PhysicalFileProvider(directory, ExclusionFilters.None);
            try
            {
                return new ConfigurationSetStateFileWatcher(
                    provider,
                    Path.GetFileName(filePath),
                    reloadDelayMilliseconds,
                    reloadCallback);
            }
            catch
            {
                provider.Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
            }

            _changeRegistration.Dispose();
            _reloadTimer.Dispose();
            _fileProvider.Dispose();
        }

        private void ScheduleReload()
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                _reloadTimer.Change(_reloadDelayMilliseconds, Timeout.Infinite);
            }
        }

        private void ReloadTimerElapsed(object? state)
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }
            }

            try
            {
                _reloadCallback();
            }
            catch (Exception)
            {
                // The watcher is a notification transport only. The state store converts file/apply failures into lifecycle outcomes.
            }
        }
    }
}
