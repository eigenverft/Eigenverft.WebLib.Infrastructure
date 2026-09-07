using System;
using System.Collections.Generic;

using Microsoft.AspNetCore.StaticFiles;

namespace Eigenverft.WebLib.Infrastructure.Hosting.StaticFiles
{
    internal static class StaticFileContentTypeProviderFactory
    {
        internal static FileExtensionContentTypeProvider Create(StaticFileAdditionalMappings additionalMappings)
        {
            ArgumentNullException.ThrowIfNull(additionalMappings);

            var provider = new FileExtensionContentTypeProvider();

            foreach (KeyValuePair<string, string> mapping in additionalMappings.Mappings)
            {
                if (!provider.Mappings.ContainsKey(mapping.Key))
                {
                    provider.Mappings.Add(mapping.Key, mapping.Value);
                }
            }

            return provider;
        }
    }
}
