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

    /// <summary>Immutable runtime snapshot of the managed configuration-set state file and all captured set axes.</summary>
    public sealed class ConfigurationSetStateStoreStatus
    {
        internal ConfigurationSetStateStoreStatus(
            string filePath,
            IReadOnlyList<ConfigurationSetStatus> sets,
            ConfigurationSetStateApplyResult? lastApplyResult)
        {
            FilePath = filePath;
            Sets = sets;
            LastApplyResult = lastApplyResult;
        }

        /// <summary>Gets the normalized state-file path.</summary>
        public string FilePath { get; }

        /// <summary>Gets the current set snapshots in registration order.</summary>
        public IReadOnlyList<ConfigurationSetStatus> Sets { get; }

        /// <summary>Gets the most recent state-file apply result, or <see langword="null"/> when no state file has been applied yet.</summary>
        public ConfigurationSetStateApplyResult? LastApplyResult { get; }
    }
}
