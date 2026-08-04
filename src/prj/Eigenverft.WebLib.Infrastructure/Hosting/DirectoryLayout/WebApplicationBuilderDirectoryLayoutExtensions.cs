using System;
using Microsoft.AspNetCore.Builder;

namespace Eigenverft.WebLib.Infrastructure.Hosting
{
    /// <summary>
    /// Extensions to attach and retrieve an <see cref="AppDirectoryLayout"/> from a <see cref="WebApplicationBuilder"/>.
    /// </summary>
    public static class WebApplicationBuilderDirectoryLayoutExtensions
    {
        private static readonly object LayoutKey = new object();

        /// <summary>
        /// Attaches a directory layout to the builder (pre-Build access).
        /// </summary>
        /// <param name="builder">The builder.</param>
        /// <param name="layout">The layout to attach.</param>
        public static void SetDirectoryLayout(this WebApplicationBuilder builder, AppDirectoryLayout layout)
        {
            if (builder is null) throw new ArgumentNullException(nameof(builder));
            if (layout is null) throw new ArgumentNullException(nameof(layout));

            builder.Host.Properties[LayoutKey] = layout;
        }

        /// <summary>
        /// Retrieves the directory layout previously attached to the builder.
        /// </summary>
        /// <param name="builder">The builder.</param>
        /// <returns>The attached <see cref="AppDirectoryLayout"/>.</returns>
        public static AppDirectoryLayout GetDirectoryLayout(this WebApplicationBuilder builder)
        {
            if (builder is null) throw new ArgumentNullException(nameof(builder));

            if (builder.Host.Properties.TryGetValue(LayoutKey, out var value) &&
                value is AppDirectoryLayout layout)
            {
                return layout;
            }

            throw new InvalidOperationException("No AppDirectoryLayout is attached to this builder. Ensure the factory calls SetDirectoryLayout(...).");
        }
    }
}
