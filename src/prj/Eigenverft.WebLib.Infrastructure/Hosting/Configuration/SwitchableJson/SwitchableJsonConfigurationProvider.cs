using System;
using System.Collections.Generic;
using System.Threading;

using Microsoft.Extensions.Configuration;

namespace Eigenverft.WebLib.Infrastructure.Hosting.Configuration.SwitchableJson
{
    /// <summary>
    /// Represents one concrete IConfiguration provider instance built from a switchable source.
    /// </summary>
    /// <remarks>
    /// ConfigurationManager is allowed to rebuild its provider list whenever Sources or builder Properties are mutated.
    /// A source must therefore return a fresh provider instance from Build(). Runtime source identity, watching and lifecycle
    /// state live in <see cref="SwitchableJsonConfigurationRuntime"/> and survive such framework-driven provider rebuilds.
    /// </remarks>
    internal sealed class SwitchableJsonConfigurationProvider : ConfigurationProvider, IDisposable
    {
        private readonly object _dataGate = new();
        private readonly SwitchableJsonConfigurationRuntime _runtime;
        private int _disposeStarted;
        private int _disposed;

        public SwitchableJsonConfigurationProvider(SwitchableJsonConfigurationRuntime runtime)
        {
            ArgumentNullException.ThrowIfNull(runtime);
            _runtime = runtime;
        }

        public override bool TryGet(string key, out string? value)
        {
            lock (_dataGate)
            {
                ThrowIfDisposed();
                return base.TryGet(key, out value);
            }
        }

        public override void Set(string key, string? value)
        {
            // IConfiguration Set participates in the same operation serialization as manual switching and watcher reloads.
            // Without that serialization, a Set performed while candidate IO is in progress could be silently overwritten by
            // the candidate commit. Reads do not take the runtime operation gate; they only see one complete Data reference.
            _runtime.Set(this, key, value);
        }

        public override IEnumerable<string> GetChildKeys(
            IEnumerable<string> earlierKeys,
            string? parentPath)
        {
            lock (_dataGate)
            {
                ThrowIfDisposed();
                return new List<string>(base.GetChildKeys(earlierKeys, parentPath));
            }
        }

        public override void Load()
        {
            ThrowIfDisposed();
            _runtime.LoadProvider(this);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposeStarted, 1) != 0)
            {
                return;
            }

            // Detach while this instance is still usable. During ConfigurationManager.ReloadSources a newly built provider
            // is loaded/activated before the previous provider set is disposed, so disposal of the previous instance becomes
            // a no-op for runtime ownership. If this instance is still active, detaching ends the runtime source lifecycle.
            _runtime.DetachProvider(this);
            Volatile.Write(ref _disposed, 1);
        }

        internal bool CommitCandidate(IDictionary<string, string?> candidateData)
        {
            ArgumentNullException.ThrowIfNull(candidateData);

            lock (_dataGate)
            {
                ThrowIfDisposed();

                bool changed = !SwitchableJsonConfigurationRuntime.ConfigurationDataEquals(Data, candidateData);
                if (changed)
                {
                    // Replace the complete dictionary reference. Readers can therefore observe only the old or the new
                    // provider snapshot, never a Clear/Add sequence containing a partially committed configuration.
                    Data = candidateData;
                }

                return changed;
            }
        }

        internal void ReplaceData(IDictionary<string, string?> data)
        {
            ArgumentNullException.ThrowIfNull(data);

            lock (_dataGate)
            {
                ThrowIfDisposed();
                Data = data;
            }
        }

        internal void SetCore(string key, string? value)
        {
            lock (_dataGate)
            {
                ThrowIfDisposed();
                base.Set(key, value);
            }
        }

        internal void RaiseReload()
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                return;
            }

            OnReload();
        }

        private void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        }
    }
}
