using System;
using System.Collections.Generic;

namespace Eigenverft.WebLib.Infrastructure.Hosting.StaticFiles
{
    /// <summary>
    /// Represents an opaque, typed group of MIME mappings that can be added to ASP.NET Core static-file defaults.
    /// </summary>
    /// <remarks>
    /// The underlying content-type provider is intentionally not part of the public API. Mappings in a group are
    /// applied only when the target framework's default static-file provider does not already define the extension.
    /// </remarks>
    public sealed class StaticFileAdditionalMappings
    {
        private readonly IReadOnlyDictionary<string, string> _mappings;

        internal StaticFileAdditionalMappings(IReadOnlyDictionary<string, string> mappings)
        {
            _mappings = mappings ?? throw new ArgumentNullException(nameof(mappings));
        }

        internal IReadOnlyDictionary<string, string> Mappings => _mappings;
    }
}
