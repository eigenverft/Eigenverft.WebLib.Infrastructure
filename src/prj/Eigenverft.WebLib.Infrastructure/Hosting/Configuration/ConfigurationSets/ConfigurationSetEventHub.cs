using System;
using System.Collections.Generic;
using System.Threading;

namespace Eigenverft.WebLib.Infrastructure.Hosting.Configuration.ConfigurationSets
{
    /// <summary>
    /// Aggregates completed lifecycle notifications from all configuration-set coordinators registered on the same host builder.
    /// </summary>
    /// <remarks>
    /// The hub is registered as a singleton through DI when the first configuration set is added. Subscription callbacks are
    /// notifications rather than transaction participants: they run after a coordinator operation completes, outside coordinator
    /// state locks, and a throwing subscriber cannot change the completed outcome or block later subscribers.
    /// </remarks>
    public interface IConfigurationSetEventHub
    {
        /// <summary>Subscribes to completed lifecycle notifications from every registered configuration set.</summary>
        /// <param name="handler">Callback invoked for each notification.</param>
        /// <returns>An idempotent lease that unsubscribes the callback when disposed.</returns>
        IDisposable Subscribe(Action<ConfigurationSetNotification> handler);

        /// <summary>Subscribes to completed lifecycle notifications from one named configuration set.</summary>
        /// <param name="setName">The caller-defined configuration-set identity to observe.</param>
        /// <param name="handler">Callback invoked for matching notifications.</param>
        /// <returns>An idempotent lease that unsubscribes the callback when disposed.</returns>
        IDisposable Subscribe(string setName, Action<ConfigurationSetNotification> handler);
    }

    /// <summary>Represents one completed configuration-set lifecycle notification published through the shared event hub.</summary>
    public sealed class ConfigurationSetNotification
    {
        internal ConfigurationSetNotification(long sequence, ConfigurationSetEventKind kind, ConfigurationSetSwitchResult result)
        {
            Sequence = sequence;
            Kind = kind;
            Result = result;
        }

        /// <summary>
        /// Gets the monotonically increasing sequence assigned by this event hub across all attached configuration sets.
        /// </summary>
        /// <remarks>
        /// Concurrent callbacks may complete in a different order than they were assigned. Consumers that maintain shared
        /// administrative state can compare this sequence instead of callback completion order.
        /// </remarks>
        public long Sequence { get; }

        /// <summary>Gets the completed coordinator lifecycle kind.</summary>
        public ConfigurationSetEventKind Kind { get; }

        /// <summary>Gets the complete switch result, including change and participant details.</summary>
        public ConfigurationSetSwitchResult Result { get; }

        /// <summary>Gets the configuration-set identity.</summary>
        public string SetName => Result.Name;

        /// <summary>
        /// Gets whether the operation changed the logical set value, a committed participant source, or effective configuration data.
        /// </summary>
        public bool HasChanges => Result.HasChanges;
    }

    internal sealed class ConfigurationSetEventHub : IConfigurationSetEventHub
    {
        private readonly object _gate = new();
        private readonly Dictionary<long, Subscription> _subscriptions = new();
        private long _nextSubscriptionId;
        private long _sequence;

        public IDisposable Subscribe(Action<ConfigurationSetNotification> handler)
        {
            return SubscribeCore(null, handler);
        }

        public IDisposable Subscribe(string setName, Action<ConfigurationSetNotification> handler)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(setName);
            return SubscribeCore(setName, handler);
        }

        internal void Attach(IConfigurationSetCoordinator coordinator)
        {
            ArgumentNullException.ThrowIfNull(coordinator);
            coordinator.LifecycleChanged += OnCoordinatorLifecycleChanged;
        }

        private IDisposable SubscribeCore(string? setName, Action<ConfigurationSetNotification> handler)
        {
            ArgumentNullException.ThrowIfNull(handler);

            long id;
            lock (_gate)
            {
                id = ++_nextSubscriptionId;
                _subscriptions.Add(id, new Subscription(setName, handler));
            }

            return new SubscriptionLease(this, id);
        }

        private void OnCoordinatorLifecycleChanged(object? sender, ConfigurationSetEventArgs eventArgs)
        {
            var notification = new ConfigurationSetNotification(
                Interlocked.Increment(ref _sequence),
                eventArgs.Kind,
                eventArgs.Result);
            Subscription[] subscriptions;

            lock (_gate)
            {
                subscriptions = new Subscription[_subscriptions.Count];
                _subscriptions.Values.CopyTo(subscriptions, 0);
            }

            foreach (Subscription subscription in subscriptions)
            {
                if (subscription.SetName is not null &&
                    !string.Equals(subscription.SetName, notification.SetName, StringComparison.Ordinal))
                {
                    continue;
                }

                try
                {
                    subscription.Handler(notification);
                }
                catch (Exception)
                {
                    // Subscribers are diagnostics/runtime consumers, not transaction participants.
                }
            }
        }

        private void Unsubscribe(long id)
        {
            lock (_gate)
            {
                _ = _subscriptions.Remove(id);
            }
        }

        private sealed record Subscription(string? SetName, Action<ConfigurationSetNotification> Handler);

        private sealed class SubscriptionLease : IDisposable
        {
            private ConfigurationSetEventHub? _owner;
            private readonly long _id;

            public SubscriptionLease(ConfigurationSetEventHub owner, long id)
            {
                _owner = owner;
                _id = id;
            }

            public void Dispose()
            {
                ConfigurationSetEventHub? owner = Interlocked.Exchange(ref _owner, null);
                owner?.Unsubscribe(_id);
            }
        }
    }
}
