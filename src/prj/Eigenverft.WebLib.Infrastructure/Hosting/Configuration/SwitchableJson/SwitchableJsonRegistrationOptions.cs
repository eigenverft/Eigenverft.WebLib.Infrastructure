using System;
using System.Collections.Generic;
using System.Linq;

using Eigenverft.WebLib.Infrastructure.Hosting.Configuration.JsonSettings;

namespace Eigenverft.WebLib.Infrastructure.Hosting.Configuration.SwitchableJson
{
    /// <summary>
    /// Shared registration options for one or more switchable JSON configuration sources.
    /// </summary>
    public sealed class SwitchableJsonRegistrationOptions
    {
        /// <summary>Gets or initializes whether a missing active source is treated as empty by framework-driven loads.</summary>
        public bool Optional { get; init; }

        /// <summary>Gets or initializes whether each active JSON source is watched independently for physical changes.</summary>
        public bool ReloadOnChange { get; init; }

        /// <summary>Gets or initializes the debounce delay for active-source file notifications.</summary>
        public int ReloadDelayMilliseconds { get; init; } = 250;

        /// <summary>Gets or initializes the runtime failure policy used by each registered switchable JSON source.</summary>
        public SwitchableJsonRuntimeFailurePolicy RuntimeFailurePolicy { get; init; } =
            SwitchableJsonRuntimeFailurePolicy.KeepLastKnownGood;

        /// <summary>
        /// Gets or initializes candidate preparation steps applied after JSON parsing and before any provider snapshot is committed.
        /// </summary>
        /// <remarks>
        /// Steps run in order for initial loads, manual switches, framework reloads and active-file watcher reloads.
        /// </remarks>
        public IReadOnlyList<IJsonConfigurationSourcePreparation> SourcePreparations { get; init; } =
            Array.Empty<IJsonConfigurationSourcePreparation>();

        internal void Validate()
        {
            if (ReloadDelayMilliseconds < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(ReloadDelayMilliseconds));
            }

            if (!Enum.IsDefined(RuntimeFailurePolicy))
            {
                throw new ArgumentOutOfRangeException(nameof(RuntimeFailurePolicy));
            }
            ArgumentNullException.ThrowIfNull(SourcePreparations);
            if (SourcePreparations.Any(preparation => preparation is null))
            {
                throw new ArgumentException("SourcePreparations cannot contain null entries.", nameof(SourcePreparations));
            }
        }
    }
}
