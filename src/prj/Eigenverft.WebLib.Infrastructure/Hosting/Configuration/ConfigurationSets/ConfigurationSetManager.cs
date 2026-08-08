using System;
using System.Collections.Generic;

namespace Eigenverft.WebLib.Infrastructure.Hosting.Configuration.ConfigurationSets
{
    /// <summary>
    /// Process-wide control-plane facade for inspecting and switching registered configuration sets.
    /// </summary>
    /// <remarks>
    /// This service is registered automatically when the first configuration set is added. It is intentionally persistence-neutral:
    /// <see cref="TrySwitch"/> performs an ephemeral runtime switch and returns only after the coordinator has completed the request.
    /// A caller that needs persistent desired state may combine this facade with an optional desired-state store.
    /// </remarks>
    public interface IConfigurationSetManager
    {
        /// <summary>Captures immutable runtime snapshots of every registered configuration set in registration order.</summary>
        IReadOnlyList<ConfigurationSetStatus> GetStatus();

        /// <summary>Attempts to capture the runtime status of one named configuration set.</summary>
        bool TryGetStatus(string setName, out ConfigurationSetStatus? status);

        /// <summary>
        /// Attempts to switch one named configuration set and waits for the complete coordinated outcome.
        /// </summary>
        /// <param name="setName">Registered configuration-set name.</param>
        /// <param name="value">Allowed value to request.</param>
        /// <param name="result">
        /// Completed coordinator result when the set exists; otherwise <see langword="null"/>.
        /// A <see langword="true"/> return value means the set was found, not that the switch itself succeeded.
        /// </param>
        /// <returns><see langword="true"/> when the named set exists; otherwise <see langword="false"/>.</returns>
        bool TrySwitch(string setName, string value, out ConfigurationSetSwitchResult? result);
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

        public bool TrySwitch(string setName, string value, out ConfigurationSetSwitchResult? result)
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
            return true;
        }
    }
}
