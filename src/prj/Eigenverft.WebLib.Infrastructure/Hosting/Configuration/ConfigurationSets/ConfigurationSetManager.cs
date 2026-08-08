using System;
using System.Collections.Generic;

namespace Eigenverft.WebLib.Infrastructure.Hosting.Configuration.ConfigurationSets
{
    /// <summary>
    /// Process-wide control-plane facade for inspecting and switching registered configuration sets.
    /// </summary>
    /// <remarks>
    /// This service is registered automatically when the first configuration set is added. It is intentionally persistence-neutral:
    /// <see cref="TrySwitchRuntime"/> performs an ephemeral runtime switch and returns only after the coordinator has completed the request.
    /// Callers that need persistent desired state should use <see cref="IConfigurationSetDesiredStateStore"/> instead.
    /// The runtime API remains synchronous by design because the underlying IConfigurationProvider and local JSON file operations are synchronous;
    /// an asynchronous wrapper would not make the configuration work itself asynchronous.
    /// </remarks>
    public interface IConfigurationSetManager
    {
        /// <summary>Captures immutable runtime snapshots of every registered configuration set in registration order.</summary>
        IReadOnlyList<ConfigurationSetStatus> GetStatus();

        /// <summary>Attempts to capture the runtime status of one named configuration set.</summary>
        bool TryGetStatus(string setName, out ConfigurationSetStatus? status);

        /// <summary>
        /// Attempts an ephemeral runtime switch of one named configuration set and waits for the complete coordinated outcome.
        /// </summary>
        /// <param name="setName">Registered configuration-set name.</param>
        /// <param name="value">Allowed value to request.</param>
        /// <param name="result">
        /// Completed coordinator result when the set exists; otherwise <see langword="null"/>.
        /// A non-null rejected result distinguishes a known set whose switch failed from an unknown set name.
        /// </param>
        /// <returns>
        /// <see langword="true"/> when the requested value is the fully coordinated active value after the call;
        /// otherwise <see langword="false"/>.
        /// </returns>
        bool TrySwitchRuntime(string setName, string value, out ConfigurationSetSwitchResult? result);
    }

    internal sealed class ConfigurationSetManager : IConfigurationSetManager
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, IConfigurationSetCoordinator> _coordinators =
            new(StringComparer.Ordinal);
        private readonly List<IConfigurationSetCoordinator> _registrationOrder = new();

        internal void Attach(IConfigurationSetCoordinator coordinator)
        {
            ArgumentNullException.ThrowIfNull(coordinator);

            lock (_gate)
            {
                if (_coordinators.ContainsKey(coordinator.Name))
                {
                    return;
                }

                _coordinators.Add(coordinator.Name, coordinator);
                _registrationOrder.Add(coordinator);
            }
        }

        public IReadOnlyList<ConfigurationSetStatus> GetStatus()
        {
            IConfigurationSetCoordinator[] snapshot;
            lock (_gate)
            {
                snapshot = _registrationOrder.ToArray();
            }

            var result = new ConfigurationSetStatus[snapshot.Length];
            for (int index = 0; index < snapshot.Length; index++)
            {
                result[index] = snapshot[index].GetStatus();
            }

            return Array.AsReadOnly(result);
        }

        public bool TryGetStatus(string setName, out ConfigurationSetStatus? status)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(setName);

            IConfigurationSetCoordinator? coordinator;
            lock (_gate)
            {
                _coordinators.TryGetValue(setName, out coordinator);
            }

            if (coordinator is null)
            {
                status = null;
                return false;
            }

            status = coordinator.GetStatus();
            return true;
        }

        public bool TrySwitchRuntime(string setName, string value, out ConfigurationSetSwitchResult? result)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(setName);
            ArgumentException.ThrowIfNullOrWhiteSpace(value);

            IConfigurationSetCoordinator? coordinator;
            lock (_gate)
            {
                _coordinators.TryGetValue(setName, out coordinator);
            }

            if (coordinator is null)
            {
                result = null;
                return false;
            }

            result = coordinator.TrySwitch(value);
            return result.Succeeded;
        }
    }
}
