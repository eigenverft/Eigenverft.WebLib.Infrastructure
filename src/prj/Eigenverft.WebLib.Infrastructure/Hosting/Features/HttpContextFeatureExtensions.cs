using System;
using System.Diagnostics.CodeAnalysis;

using Microsoft.AspNetCore.Http;

namespace Eigenverft.WebLib.Infrastructure.Hosting.Features
{
    /// <summary>
    /// Provides typed convenience access to <see cref="HttpContext.Features"/>.
    /// </summary>
    public static class HttpContextFeatureExtensions
    {
        /// <summary>
        /// Gets a typed feature, or <see langword="null"/> when it is not present.
        /// </summary>
        public static TFeature? GetFeature<TFeature>(this HttpContext context) where TFeature : class
        {
            ArgumentNullException.ThrowIfNull(context);
            return context.Features.Get<TFeature>();
        }

        /// <summary>
        /// Gets a typed feature and throws when it is not present.
        /// </summary>
        public static TFeature GetRequiredFeature<TFeature>(this HttpContext context) where TFeature : class
        {
            ArgumentNullException.ThrowIfNull(context);
            return context.Features.Get<TFeature>()
                ?? throw new InvalidOperationException($"Required HTTP feature '{typeof(TFeature).FullName ?? typeof(TFeature).Name}' is not available.");
        }

        /// <summary>
        /// Tries to get a typed feature.
        /// </summary>
        public static bool TryGetFeature<TFeature>(this HttpContext context, [NotNullWhen(true)] out TFeature? feature) where TFeature : class
        {
            ArgumentNullException.ThrowIfNull(context);
            feature = context.Features.Get<TFeature>();
            return feature is not null;
        }

        /// <summary>
        /// Sets a typed feature in <see cref="HttpContext.Features"/>.
        /// </summary>
        public static void SetFeature<TFeature>(this HttpContext context, TFeature feature) where TFeature : class
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(feature);
            context.Features.Set(feature);
        }

        /// <summary>
        /// Removes a typed feature and reports whether one was present.
        /// </summary>
        public static bool RemoveFeature<TFeature>(this HttpContext context) where TFeature : class
        {
            ArgumentNullException.ThrowIfNull(context);
            var existing = context.Features.Get<TFeature>();
            context.Features.Set<TFeature>(null);
            return existing is not null;
        }

        /// <summary>
        /// Gets an existing typed feature or creates and stores one using its parameterless constructor.
        /// </summary>
        public static TFeature GetOrCreateFeature<TFeature>(this HttpContext context) where TFeature : class, new()
            => GetOrCreateFeature(context, static () => new TFeature());

        /// <summary>
        /// Gets an existing typed feature or creates and stores one with the supplied factory.
        /// </summary>
        public static TFeature GetOrCreateFeature<TFeature>(this HttpContext context, Func<TFeature> factory) where TFeature : class
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(factory);

            var feature = context.Features.Get<TFeature>();
            if (feature is not null)
                return feature;

            feature = factory() ?? throw new InvalidOperationException($"The feature factory for '{typeof(TFeature).FullName ?? typeof(TFeature).Name}' returned null.");
            context.Features.Set(feature);
            return feature;
        }
    }
}
