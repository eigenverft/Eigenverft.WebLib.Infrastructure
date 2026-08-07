using System;

namespace Eigenverft.WebLib.Infrastructure.Hosting.Configuration.SwitchableJson
{
    /// <summary>
    /// Bridges a watcher that already exists during Prepare into the active runtime callback only after Commit.
    /// </summary>
    internal sealed class PreparedSourceWatcherRelay
    {
        private readonly object _gate = new();
        private readonly Action<long, string> _activeReloadCallback;
        private RelayState _state;
        private bool _changedWhilePrepared;

        public PreparedSourceWatcherRelay(Action<long, string> activeReloadCallback)
        {
            ArgumentNullException.ThrowIfNull(activeReloadCallback);
            _activeReloadCallback = activeReloadCallback;
        }

        public void ObserveSourceChanged(long generation, string sourcePath)
        {
            lock (_gate)
            {
                if (_state == RelayState.Prepared)
                {
                    // This immediate callback runs before the watcher's normal debounce timer. It closes the race where a
                    // candidate file changes after Prepare but Commit happens before the delayed active reload callback fires.
                    _changedWhilePrepared = true;
                }
            }
        }

        public void OnReload(long generation, string sourcePath)
        {
            bool forward;

            lock (_gate)
            {
                if (_state == RelayState.Closed)
                {
                    return;
                }

                if (_state == RelayState.Prepared)
                {
                    _changedWhilePrepared = true;
                    return;
                }

                forward = true;
            }

            if (forward)
            {
                _activeReloadCallback(generation, sourcePath);
            }
        }

        public bool TryActivate(Action commitAction)
        {
            ArgumentNullException.ThrowIfNull(commitAction);

            lock (_gate)
            {
                if (_state != RelayState.Prepared || _changedWhilePrepared)
                {
                    return false;
                }

                // Keep the relay gate across the tiny in-memory commit. A raw/watch reload callback can therefore only happen
                // entirely before activation (and invalidate the preparation) or after activation (and route through the active
                // generation). There is no notification gap between prepared and active watcher ownership.
                commitAction();
                _state = RelayState.Active;
                return true;
            }
        }

        public void Close()
        {
            lock (_gate)
            {
                _state = RelayState.Closed;
            }
        }

        private enum RelayState
        {
            Prepared = 0,
            Active = 1,
            Closed = 2,
        }
    }
}
