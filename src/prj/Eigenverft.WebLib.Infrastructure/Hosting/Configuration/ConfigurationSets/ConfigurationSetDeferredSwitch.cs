using System;
using System.Collections.Generic;
using System.Threading;

using Eigenverft.WebLib.Infrastructure.Hosting.Configuration.SwitchableJson;

namespace Eigenverft.WebLib.Infrastructure.Hosting.Configuration.ConfigurationSets
{
    /// <summary>
    /// Internal hand-off for a completed coordinator state transition whose observer publication is intentionally deferred.
    /// </summary>
    internal sealed class ConfigurationSetDeferredSwitch
    {
        private readonly ConfigurationSetCoordinator _coordinator;
        private readonly IReadOnlyList<SwitchableJsonDeferredCommit> _participantCommits;
        private readonly bool _completesSwitchInProgress;
        private int _published;

        internal ConfigurationSetDeferredSwitch(
            ConfigurationSetCoordinator coordinator,
            ConfigurationSetSwitchResult result,
            IReadOnlyList<SwitchableJsonDeferredCommit> participantCommits,
            bool completesSwitchInProgress)
        {
            ArgumentNullException.ThrowIfNull(coordinator);
            ArgumentNullException.ThrowIfNull(result);
            ArgumentNullException.ThrowIfNull(participantCommits);
            _coordinator = coordinator;
            Result = result;
            _participantCommits = participantCommits;
            _completesSwitchInProgress = completesSwitchInProgress;
        }

        internal ConfigurationSetSwitchResult Result { get; }

        internal void Publish()
        {
            if (Interlocked.Exchange(ref _published, 1) != 0)
            {
                return;
            }

            try
            {
                foreach (SwitchableJsonDeferredCommit participantCommit in _participantCommits)
                {
                    participantCommit.Publish();
                }
            }
            finally
            {
                _coordinator.CompleteDeferredSwitchPublication(Result, _completesSwitchInProgress);
            }
        }
    }
}
