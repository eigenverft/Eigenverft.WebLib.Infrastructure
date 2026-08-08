using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;

namespace Eigenverft.WebLib.Infrastructure.Hosting.Configuration.JsonSettings
{
    /// <summary>Adds JSON configuration providers whose isolated snapshots pass through generic source preparations.</summary>
    public static class PreparedJsonConfigurationExtensions
    {
        /// <summary>Adds one JSON file using one reusable candidate-preparation bundle.</summary>
        public static IConfigurationBuilder AddPreparedJsonFile(
            this IConfigurationBuilder builder,
            string path,
            IJsonConfigurationSourcePreparation candidatePreparation,
            bool optional = false,
            bool reloadOnChange = false)
        {
            ArgumentNullException.ThrowIfNull(candidatePreparation);
            return builder.AddPreparedJsonFile(
                path,
                new[] { candidatePreparation },
                optional,
                reloadOnChange);
        }

        /// <summary>Adds one JSON file using an explicit low-level sequence of source preparations.</summary>
        /// <remarks>
        /// The provider owns the complete load, prepare and publication boundary. When <paramref name="reloadOnChange"/> is
        /// enabled, physical reload failures preserve the last successfully published snapshot and do not publish a reload token.
        /// A missing optional file is a complete empty state and therefore does not invoke candidate preparations.
        /// </remarks>
        public static IConfigurationBuilder AddPreparedJsonFile(
            this IConfigurationBuilder builder,
            string path,
            IEnumerable<IJsonConfigurationSourcePreparation> sourcePreparations,
            bool optional = false,
            bool reloadOnChange = false)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            ArgumentNullException.ThrowIfNull(sourcePreparations);

            IJsonConfigurationSourcePreparation[] preparations = sourcePreparations.ToArray();
            if (preparations.Any(preparation => preparation is null))
            {
                throw new ArgumentException("Source preparations cannot contain null entries.", nameof(sourcePreparations));
            }

            var source = new PreparedJsonConfigurationSource
            {
                Path = path,
                Optional = optional,
                ReloadOnChange = reloadOnChange,
                SourcePreparations = preparations,
            };

            source.ResolveFileProvider();
            builder.Add(source);
            return builder;
        }

        private sealed class PreparedJsonConfigurationSource : JsonConfigurationSource
        {
            public IReadOnlyList<IJsonConfigurationSourcePreparation> SourcePreparations { get; init; } =
                Array.Empty<IJsonConfigurationSourcePreparation>();

            public override IConfigurationProvider Build(IConfigurationBuilder builder)
            {
                EnsureDefaults(builder);
                ResolveFileProvider();
                return new PreparedJsonConfigurationProvider(this);
            }
        }

        private sealed class PreparedJsonConfigurationProvider : ConfigurationProvider, IDisposable
        {
            private readonly object _watcherGate = new();
            private readonly IFileProvider _fileProvider;
            private readonly string _sourcePath;
            private readonly bool _optional;
            private readonly bool _reloadOnChange;
            private readonly int _reloadDelayMilliseconds;
            private readonly IReadOnlyList<IJsonConfigurationSourcePreparation> _sourcePreparations;
            private IDisposable? _changeRegistration;
            private Timer? _reloadTimer;
            private long _observedGeneration;
            private bool _disposed;

            public PreparedJsonConfigurationProvider(PreparedJsonConfigurationSource source)
            {
                ArgumentNullException.ThrowIfNull(source);

                _fileProvider = source.FileProvider ?? throw new InvalidOperationException(
                    "Prepared JSON source must resolve a file provider before its provider is created.");
                _sourcePath = source.Path ?? throw new InvalidOperationException(
                    "Prepared JSON source must define a path before its provider is created.");
                _optional = source.Optional;
                _reloadOnChange = source.ReloadOnChange;
                _reloadDelayMilliseconds = source.ReloadDelay;
                _sourcePreparations = source.SourcePreparations;
            }

            public override void Load()
            {
                while (true)
                {
                    long observedGeneration;

                    lock (_watcherGate)
                    {
                        ThrowIfDisposed();
                        observedGeneration = _observedGeneration;
                    }

                    // Explicit IConfigurationRoot.Reload() also runs arbitrary preparation outside our lifecycle gate. If a
                    // physical change is observed while it runs, retry instead of publishing a candidate from an older file
                    // generation and immediately raising ConfigurationRoot's reload token for that stale snapshot.
                    IDictionary<string, string?> candidateData = LoadPreparedData();

                    lock (_watcherGate)
                    {
                        ThrowIfDisposed();
                        if (observedGeneration != _observedGeneration)
                        {
                            continue;
                        }

                        Data = candidateData;
                        EnsureWatcherStartedLocked();
                        return;
                    }
                }
            }

            public void Dispose()
            {
                IDisposable? changeRegistration;
                Timer? reloadTimer;

                lock (_watcherGate)
                {
                    if (_disposed)
                    {
                        return;
                    }

                    _disposed = true;
                    changeRegistration = _changeRegistration;
                    reloadTimer = _reloadTimer;
                    _changeRegistration = null;
                    _reloadTimer = null;
                }

                changeRegistration?.Dispose();
                reloadTimer?.Dispose();
            }

            private IDictionary<string, string?> LoadPreparedData()
            {
                IFileInfo file = _fileProvider.GetFileInfo(_sourcePath);

                if (!file.Exists)
                {
                    if (_optional)
                    {
                        // Optional absence is already a complete empty state. There is no source candidate to transform or
                        // validate, which keeps this owner aligned with normal file-provider and SwitchableJson semantics.
                        return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                    }

                    throw new FileNotFoundException(
                        $"The prepared JSON configuration file '{_sourcePath}' was not found and is not optional.",
                        _sourcePath);
                }

                try
                {
                    using Stream stream = file.CreateReadStream();
                    IDictionary<string, string?> candidateData = ParseSnapshot(stream);
                    JsonConfigurationSourcePreparationPipeline.Apply(
                        _sourcePath,
                        candidateData,
                        _sourcePreparations);

                    // Detach the published dictionary from the preparation-owned working snapshot. A custom preparation may have
                    // retained its context.Values reference, but its mutation authority ends when Prepare returns.
                    return new Dictionary<string, string?>(candidateData, StringComparer.OrdinalIgnoreCase);
                }
                catch (InvalidDataException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    // Preserve the established JsonConfigurationProvider/FileConfigurationProvider contract for initial and
                    // explicit IConfigurationRoot.Reload() failures even though physical reload publication is now owned here.
                    throw new InvalidDataException(
                        $"Failed to load prepared JSON configuration file '{_sourcePath}'.",
                        exception);
                }
            }

            private void EnsureWatcherStartedLocked()
            {
                if (!_reloadOnChange || _changeRegistration is not null)
                {
                    return;
                }

                _reloadTimer = new Timer(ReloadTimerElapsed, null, Timeout.Infinite, Timeout.Infinite);
                _changeRegistration = ChangeToken.OnChange(
                    () => _fileProvider.Watch(_sourcePath),
                    ScheduleReload);
            }

            private void ScheduleReload()
            {
                lock (_watcherGate)
                {
                    if (_disposed)
                    {
                        return;
                    }

                    _observedGeneration++;
                    _reloadTimer?.Change(_reloadDelayMilliseconds, Timeout.Infinite);
                }
            }

            private void ReloadTimerElapsed(object? state)
            {
                long generation;

                lock (_watcherGate)
                {
                    if (_disposed)
                    {
                        return;
                    }

                    generation = _observedGeneration;
                }

                IDictionary<string, string?> candidateData;

                try
                {
                    // Parse and run arbitrary application preparation outside the watcher gate. A newer physical change or
                    // provider disposal may interleave; generation/disposed validation below prevents this candidate publishing.
                    candidateData = LoadPreparedData();
                }
                catch (Exception)
                {
                    // Physical reload is notification-only and has no synchronous caller to receive a load exception. Preserve
                    // the last successfully published snapshot and wait for a later valid file change.
                    return;
                }

                lock (_watcherGate)
                {
                    if (_disposed || generation != _observedGeneration)
                    {
                        return;
                    }

                    Data = candidateData;
                }

                // Never invoke IConfiguration observers while holding the watcher/lifecycle gate.
                OnReload();
            }

            private void ThrowIfDisposed()
            {
                if (_disposed)
                {
                    throw new ObjectDisposedException(nameof(PreparedJsonConfigurationProvider));
                }
            }

            private static IDictionary<string, string?> ParseSnapshot(Stream stream)
            {
                var parser = new SnapshotJsonConfigurationProvider();
                return parser.Parse(stream);
            }

            private sealed class SnapshotJsonConfigurationProvider : JsonConfigurationProvider
            {
                public SnapshotJsonConfigurationProvider()
                    : base(new JsonConfigurationSource
                    {
                        Path = "prepared-json-snapshot.json",
                        Optional = false,
                        ReloadOnChange = false,
                    })
                {
                }

                public IDictionary<string, string?> Parse(Stream stream)
                {
                    base.Load(stream);
                    return new Dictionary<string, string?>(Data, StringComparer.OrdinalIgnoreCase);
                }
            }
        }
    }
}
