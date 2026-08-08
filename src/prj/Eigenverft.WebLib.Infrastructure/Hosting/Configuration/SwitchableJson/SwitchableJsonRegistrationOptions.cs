using System;

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

        internal void Validate()
        {
            if (ReloadDelayMilliseconds < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(ReloadDelayMilliseconds));
            }
        }
    }
}
