using System;
using System.Collections.Generic;

namespace Eigenverft.WebLib.Infrastructure.Hosting.Configuration.ConfigurationSets
{
    /// <summary>
    /// Coordinates the active value of one independent named configuration-set axis.
    /// </summary>
    /// <remarks>
    /// Multiple coordinators may exist in the same process. A coordinator can remain a pure logical set, or switchable
    /// configuration sources can be bound to it. A coordinated transition prepares every mapped source, commits participant state
    /// with observer publication deferred, finalizes coordinator state, and only then releases IConfiguration/lifecycle notifications.
    /// A bound participant grants this coordinator exclusive ownership of manual source selection for that runtime.
    /// The coordinator assigns no application meaning to set names or values.
    /// </remarks>
    public interface IConfigurationSetCoordinator
    {
        /// <summary>Gets the caller-defined set identity.</summary>
        string Name { get; }

        /// <summary>Gets the code-defined value used to initialize this set before any optional desired-state source is applied.</summary>
        string InitialValue { get; }

        /// <summary>
        /// Gets the last set value that was successfully coordinated across every bound participant.
        /// </summary>
        string ActiveValue { get; }

        /// <summary>
        /// Gets whether all bound participants are known to represent <see cref="ActiveValue"/>.
        /// </summary>
        /// <remarks>
        /// A rare participant commit race can leave a coordinated operation partially committed. In that case this becomes false
        /// until a later successful switch converges every binding on one allowed value again.
        /// </remarks>
        bool IsConsistent { get; }

        /// <summary>Gets the values that may become active for this set.</summary>
        IReadOnlyList<string> AllowedValues { get; }

        /// <summary>Gets the identities of switchable configuration sources currently bound to this set.</summary>
        IReadOnlyList<string> BoundParticipantNames { get; }

        /// <summary>
        /// Occurs after a completed switch request, including success, already-active no-op, rejection and partial commit.
        /// </summary>
        /// <remarks>
        /// Lifecycle observers are notifications rather than transaction participants. Observer exceptions are isolated and do
        /// not alter an already completed coordinator outcome.
        /// </remarks>
        event EventHandler<ConfigurationSetEventArgs>? LifecycleChanged;

        /// <summary>Captures an immutable, internally consistent snapshot of this coordinator's current runtime state.</summary>
        ConfigurationSetStatus GetStatus();

        /// <summary>Returns whether a value is valid for this set.</summary>
        bool IsAllowed(string value);

        /// <summary>Requests that this set activate another allowed value.</summary>
        /// <param name="value">The caller-defined set value to activate.</param>
        /// <returns>The completed coordinated switch outcome.</returns>
        ConfigurationSetSwitchResult TrySwitch(string value);
    }
}
