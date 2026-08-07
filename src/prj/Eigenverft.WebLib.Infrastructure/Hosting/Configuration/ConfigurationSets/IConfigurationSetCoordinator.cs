using System;
using System.Collections.Generic;

namespace Eigenverft.WebLib.Infrastructure.Hosting.Configuration.ConfigurationSets
{
    /// <summary>
    /// Coordinates the active value of one independent named configuration-set axis.
    /// </summary>
    /// <remarks>
    /// Multiple coordinators may exist in the same process. For example, one coordinator may represent an environment set
    /// while another independently represents proxy behavior or a feature set. The coordinator itself assigns no semantics to
    /// names or values. Later participants can use the active value to coordinate concrete configuration-source transitions.
    /// </remarks>
    public interface IConfigurationSetCoordinator
    {
        /// <summary>Gets the caller-defined set identity.</summary>
        string Name { get; }

        /// <summary>Gets the currently active value for this set.</summary>
        string ActiveValue { get; }

        /// <summary>Gets the values that may become active for this set.</summary>
        IReadOnlyList<string> AllowedValues { get; }

        /// <summary>
        /// Occurs after a completed switch request, including successful changes, already-active no-ops, and rejected values.
        /// </summary>
        /// <remarks>
        /// Lifecycle observers are notifications rather than transaction participants. Observer exceptions are isolated and do
        /// not alter an already completed coordinator outcome.
        /// </remarks>
        event EventHandler<ConfigurationSetEventArgs>? LifecycleChanged;

        /// <summary>Returns whether a value is valid for this set.</summary>
        bool IsAllowed(string value);

        /// <summary>
        /// Requests that this set activate another allowed value.
        /// </summary>
        /// <param name="value">The caller-defined set value to activate.</param>
        /// <returns>The completed switch outcome.</returns>
        ConfigurationSetSwitchResult TrySwitch(string value);
    }
}
