using System.Collections.Generic;

namespace Eigenverft.WebLib.Infrastructure.Hosting.Configuration.ConfigurationSets
{
    /// <summary>Immutable runtime snapshot of one named configuration-set coordinator.</summary>
    public sealed class ConfigurationSetStatus
    {
        internal ConfigurationSetStatus(
            string name,
            string activeValue,
            bool isConsistent,
            IReadOnlyList<string> allowedValues,
            IReadOnlyList<string> boundParticipantNames)
        {
            Name = name;
            ActiveValue = activeValue;
            IsConsistent = isConsistent;
            AllowedValues = allowedValues;
            BoundParticipantNames = boundParticipantNames;
        }

        /// <summary>Gets the caller-defined set identity.</summary>
        public string Name { get; }

        /// <summary>Gets the last fully coordinated active value.</summary>
        public string ActiveValue { get; }

        /// <summary>Gets whether all bound participants are known to represent <see cref="ActiveValue"/>.</summary>
        public bool IsConsistent { get; }

        /// <summary>Gets the authoritative values accepted by this set.</summary>
        public IReadOnlyList<string> AllowedValues { get; }

        /// <summary>Gets the switchable JSON participant identities bound to this set.</summary>
        public IReadOnlyList<string> BoundParticipantNames { get; }
    }

    /// <summary>Immutable state-store view of one configuration-set axis, including desired and active values.</summary>
    public sealed class ConfigurationSetStateStatus
    {
        internal ConfigurationSetStateStatus(
            ConfigurationSetStatus runtime,
            string desiredValue,
            ConfigurationSetStateApplyMode applyMode)
        {
            Runtime = runtime;
            DesiredValue = desiredValue;
            ApplyMode = applyMode;
        }

        /// <summary>Gets the coordinator runtime snapshot.</summary>
        public ConfigurationSetStatus Runtime { get; }

        /// <summary>Gets the configuration-set identity.</summary>
        public string Name => Runtime.Name;

        /// <summary>Gets the value active in the running process.</summary>
        public string ActiveValue => Runtime.ActiveValue;

        /// <summary>Gets the desired value owned by the state store.</summary>
        public string DesiredValue { get; }

        /// <summary>Gets the code-owned state-file apply policy.</summary>
        public ConfigurationSetStateApplyMode ApplyMode { get; }

        /// <summary>Gets whether the desired state differs from the value active in the running process.</summary>
        public bool HasDesiredStateDrift =>
            !string.Equals(ActiveValue, DesiredValue, System.StringComparison.Ordinal);

        /// <summary>Gets whether the desired startup-only value differs from the active runtime value.</summary>
        public bool HasPendingRestart =>
            ApplyMode == ConfigurationSetStateApplyMode.StartupOnly &&
            HasDesiredStateDrift;

        /// <summary>Gets whether all bound participants are known to represent <see cref="ActiveValue"/>.</summary>
        public bool IsConsistent => Runtime.IsConsistent;

        /// <summary>Gets the authoritative allowed values.</summary>
        public IReadOnlyList<string> AllowedValues => Runtime.AllowedValues;

        /// <summary>Gets the bound switchable JSON participant identities.</summary>
        public IReadOnlyList<string> BoundParticipantNames => Runtime.BoundParticipantNames;
    }

    /// <summary>Immutable runtime snapshot of the managed configuration-set state file and all captured set axes.</summary>
    public sealed class ConfigurationSetStateStoreStatus
    {
        internal ConfigurationSetStateStoreStatus(
            string filePath,
            IReadOnlyList<ConfigurationSetStatus> sets,
            IReadOnlyList<ConfigurationSetStateStatus> setStates,
            ConfigurationSetStateApplyResult? lastApplyResult)
        {
            FilePath = filePath;
            Sets = sets;
            SetStates = setStates;
            LastApplyResult = lastApplyResult;
        }

        /// <summary>Gets the normalized state-file path.</summary>
        public string FilePath { get; }

        /// <summary>Gets coordinator runtime snapshots in registration order.</summary>
        public IReadOnlyList<ConfigurationSetStatus> Sets { get; }

        /// <summary>Gets state-store snapshots including desired values and apply modes in registration order.</summary>
        public IReadOnlyList<ConfigurationSetStateStatus> SetStates { get; }

        /// <summary>Gets whether any managed set has desired state different from its active runtime state.</summary>
        public bool HasDesiredStateDrift
        {
            get
            {
                foreach (ConfigurationSetStateStatus state in SetStates)
                {
                    if (state.HasDesiredStateDrift)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        /// <summary>Gets whether any startup-only set has a desired value waiting for the next host startup.</summary>
        public bool HasPendingRestart
        {
            get
            {
                foreach (ConfigurationSetStateStatus state in SetStates)
                {
                    if (state.HasPendingRestart)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        /// <summary>Gets the most recent state-store apply or desired-value update result, or <see langword="null"/> before any state operation has completed.</summary>
        public ConfigurationSetStateApplyResult? LastApplyResult { get; }
    }
}
