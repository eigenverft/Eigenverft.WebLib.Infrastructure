using System;
using System.Collections.Generic;

namespace Eigenverft.WebLib.Infrastructure.Hosting.Configuration.JsonSettings
{
    /// <summary>
    /// Prepares one isolated JSON configuration snapshot before it is published by a configuration provider.
    /// </summary>
    /// <remarks>
    /// Implementations may inspect, validate or mutate only the candidate <see cref="JsonConfigurationSourcePreparationContext.Values"/>
    /// during the current <see cref="Prepare"/> call. They must not publish configuration changes, select another source or mutate
    /// external runtime state. Implementations may be invoked repeatedly or concurrently and must therefore be safe for concurrent
    /// use. Throwing rejects the candidate before provider state is committed. Preparations execute in registration order.
    /// Owners detach the prepared values before publication, so retaining and later mutating the supplied dictionary has no effect
    /// on published configuration state. A candidate may still be discarded as stale if owner state changes while preparation runs.
    /// </remarks>
    public interface IJsonConfigurationSourcePreparation
    {
        /// <summary>Prepares the isolated candidate snapshot.</summary>
        /// <param name="context">Candidate source path and mutable isolated configuration values.</param>
        void Prepare(JsonConfigurationSourcePreparationContext context);
    }

    /// <summary>Provides one preparation step with an isolated candidate JSON snapshot.</summary>
    public sealed class JsonConfigurationSourcePreparationContext
    {
        internal JsonConfigurationSourcePreparationContext(
            string sourcePath,
            IDictionary<string, string?> values)
        {
            SourcePath = sourcePath;
            Values = values;
        }

        /// <summary>Gets the source identity or path from which the candidate snapshot was loaded.</summary>
        public string SourcePath { get; }

        /// <summary>
        /// Gets the mutable isolated candidate values for the duration of the current preparation call. Owners detach the final
        /// prepared snapshot before publication; callers must not rely on retaining this dictionary after <c>Prepare</c> returns.
        /// </summary>
        public IDictionary<string, string?> Values { get; }
    }

    /// <summary>Wraps a failure raised by a registered JSON source preparation step.</summary>
    public sealed class JsonConfigurationSourcePreparationException : Exception
    {
        internal JsonConfigurationSourcePreparationException(
            string sourcePath,
            Type preparationType,
            Exception innerException)
            : base(
                $"JSON source preparation '{preparationType.FullName}' failed for '{sourcePath}'.",
                innerException)
        {
            SourcePath = sourcePath;
            PreparationType = preparationType;
        }

        /// <summary>Gets the source path whose isolated snapshot was being prepared.</summary>
        public string SourcePath { get; }

        /// <summary>Gets the preparation implementation type that failed.</summary>
        public Type PreparationType { get; }
    }

    internal static class JsonConfigurationSourcePreparationPipeline
    {
        public static void Apply(
            string sourcePath,
            IDictionary<string, string?> values,
            IReadOnlyList<IJsonConfigurationSourcePreparation> preparations)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
            ArgumentNullException.ThrowIfNull(values);
            ArgumentNullException.ThrowIfNull(preparations);

            if (preparations.Count == 0)
            {
                return;
            }

            var context = new JsonConfigurationSourcePreparationContext(sourcePath, values);
            foreach (IJsonConfigurationSourcePreparation preparation in preparations)
            {
                if (preparation is null)
                {
                    throw new InvalidOperationException("JSON source preparation collections cannot contain null entries.");
                }

                try
                {
                    preparation.Prepare(context);
                }
                catch (JsonConfigurationSourcePreparationException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    throw new JsonConfigurationSourcePreparationException(
                        sourcePath,
                        preparation.GetType(),
                        exception);
                }
            }
        }
    }
}
