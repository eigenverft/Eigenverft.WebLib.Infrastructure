using System;
using System.IO;
using System.Threading;

using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.FileProviders.Physical;
using Microsoft.Extensions.Primitives;

namespace Eigenverft.WebLib.Infrastructure.Hosting.Configuration.SwitchableJson
{
    /// <summary>
    /// Watches exactly one normalized source path and debounces physical file notifications before handing them back to the
    /// owning provider together with the generation for which the watcher was created.
    /// </summary>
    internal sealed class ActiveSourceWatcher : IDisposable
    {
        private readonly object _gate = new();
        private readonly PhysicalFileProvider _fileProvider;
        private readonly IDisposable _changeRegistration;
        private readonly Timer _reloadTimer;
        private readonly Action<long, string> _reloadCallback;
        private readonly int _reloadDelayMilliseconds;
        private readonly long _generation;
        private readonly string _sourcePath;
        private bool _disposed;

        private ActiveSourceWatcher(
            PhysicalFileProvider fileProvider,
            string filter,
            string sourcePath,
            long generation,
            int reloadDelayMilliseconds,
            Action<long, string> reloadCallback)
        {
            _fileProvider = fileProvider;
            _sourcePath = sourcePath;
            _generation = generation;
            _reloadDelayMilliseconds = reloadDelayMilliseconds;
            _reloadCallback = reloadCallback;
            _reloadTimer = new Timer(ReloadTimerElapsed, null, Timeout.Infinite, Timeout.Infinite);

            // ChangeToken.OnChange rearms the underlying one-shot file-provider token. A direct FileSystemWatcher or polling
            // loop is possible, but the Microsoft file-provider abstraction keeps platform-specific watching behavior out of
            // this provider while still letting us own source switching and candidate commit semantics ourselves.
            _changeRegistration = ChangeToken.OnChange(
                () => _fileProvider.Watch(filter),
                ScheduleReload);
        }

        public static ActiveSourceWatcher Create(
            string sourcePath,
            long generation,
            int reloadDelayMilliseconds,
            Action<long, string> reloadCallback)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
            ArgumentNullException.ThrowIfNull(reloadCallback);

            // The leaf file and even some leaf directories may not exist yet (for example an optional source that will be
            // provisioned later). Anchor the PhysicalFileProvider at the nearest existing ancestor and keep the missing path
            // segments in the relative filter. This preserves normal file-provider semantics without polling for creation.
            string watchRoot = FindExistingWatchRoot(sourcePath);
            string filter = Path.GetRelativePath(watchRoot, sourcePath).Replace('\\', '/');
            // The caller named an exact source path, so do not apply PhysicalFileProvider's default hidden/system/dot-file
            // exclusions. Those filters are useful for broad directory discovery, but here they could make a file load normally
            // while silently never producing change notifications.
            var fileProvider = new PhysicalFileProvider(watchRoot, ExclusionFilters.None);

            try
            {
                return new ActiveSourceWatcher(
                    fileProvider,
                    filter,
                    sourcePath,
                    generation,
                    reloadDelayMilliseconds,
                    reloadCallback);
            }
            catch
            {
                fileProvider.Dispose();
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

            // A timer callback may already be queued after _disposed becomes true. Disposing these resources prevents new work;
            // the queued callback performs its own disposed check and the provider additionally validates watcher generation.
            // The two checks intentionally make teardown/replacement safe without waiting for ThreadPool callbacks to drain.
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

                // Physical saves commonly produce several notifications (write, rename, metadata change). Resetting one timer on
                // every notification coalesces that burst and delays reading until the file is more likely to contain a complete
                // document. This is debounce, not a correctness guarantee: malformed/transient JSON is still rejected by the
                // provider and LKG remains active. An async queue/Channel could coalesce too, but adds needless background state.
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

            // Never hold the watcher gate while entering the provider. The provider can replace/dispose this watcher while a
            // callback is queued; generation validation in the provider makes such stale callbacks harmless.
            _reloadCallback(_generation, _sourcePath);
        }

        private static string FindExistingWatchRoot(string sourcePath)
        {
            // PhysicalFileProvider requires an existing root. Walking upward instead of requiring the source directory itself to
            // exist is what lets reloadOnChange work with optional future files/directories. The returned root is an implementation
            // detail only; source identity remains the fully normalized requested file path held by the provider.
            string? current = Path.GetDirectoryName(sourcePath);

            while (!string.IsNullOrWhiteSpace(current) && !Directory.Exists(current))
            {
                current = Directory.GetParent(current)?.FullName;
            }

            if (string.IsNullOrWhiteSpace(current))
            {
                throw new DirectoryNotFoundException(
                    $"No existing parent directory could be found for switchable JSON source '{sourcePath}'.");
            }

            return current;
        }
    }
}
