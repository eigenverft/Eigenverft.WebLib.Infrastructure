using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Eigenverft.WebLib.Infrastructure.Hosting.Configuration.ConfigurationSets
{
    /// <summary>
    /// Defines one named configuration-set axis and the values that are valid for that axis.
    /// </summary>
    /// <remarks>
    /// The coordinator assigns no application meaning to either the name or the values. A caller may use a set for
    /// environments, proxy behavior, build generations, feature collections, deployment lanes, or any other convention.
    /// </remarks>
    public sealed class ConfigurationSetDefinition
    {
        private readonly HashSet<string> _allowedValueLookup;

        /// <summary>
        /// Initializes a configuration-set definition.
        /// </summary>
        /// <param name="name">Caller-defined identity of the independent set axis.</param>
        /// <param name="initialValue">Value that is active when the coordinator is created.</param>
        /// <param name="allowedValues">Complete set of values that may become active.</param>
        public ConfigurationSetDefinition(
            string name,
            string initialValue,
            IEnumerable<string> allowedValues)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            ArgumentException.ThrowIfNullOrWhiteSpace(initialValue);
            ArgumentNullException.ThrowIfNull(allowedValues);

            var values = new List<string>();
            _allowedValueLookup = new HashSet<string>(StringComparer.Ordinal);

            foreach (string value in allowedValues)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(value);

                if (!_allowedValueLookup.Add(value))
                {
                    throw new ArgumentException(
                        $"Configuration set '{name}' contains duplicate allowed value '{value}'.",
                        nameof(allowedValues));
                }

                values.Add(value);
            }

            if (values.Count == 0)
            {
                throw new ArgumentException(
                    $"Configuration set '{name}' must define at least one allowed value.",
                    nameof(allowedValues));
            }

            if (!_allowedValueLookup.Contains(initialValue))
            {
                throw new ArgumentException(
                    $"Initial value '{initialValue}' is not allowed for configuration set '{name}'.",
                    nameof(initialValue));
            }

            Name = name;
            InitialValue = initialValue;
            AllowedValues = new ReadOnlyCollection<string>(values);
        }

        /// <summary>Gets the caller-defined identity of this set axis.</summary>
        public string Name { get; }

        /// <summary>Gets the value that is active when a coordinator for this definition is created.</summary>
        public string InitialValue { get; }

        /// <summary>Gets the complete ordered collection of values accepted by this set.</summary>
        public IReadOnlyList<string> AllowedValues { get; }

        /// <summary>Returns whether the supplied value belongs to this set.</summary>
        public bool IsAllowed(string value)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            return _allowedValueLookup.Contains(value);
        }
    }
}
