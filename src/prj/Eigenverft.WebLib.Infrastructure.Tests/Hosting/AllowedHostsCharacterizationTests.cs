using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HostFiltering;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Eigenverft.WebLib.Infrastructure.Tests;

[TestClass]
public sealed class AllowedHostsCharacterizationTests
{
    [TestMethod]
    public async Task CreateBuilderHostFilteringReadsConfigurationRebuiltAfterSourcesAreCleared()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();

        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AllowedHosts"] = "example.com;www.example.com",
        });

        await using WebApplication application = builder.Build();
        HostFilteringOptions options = application.Services
            .GetRequiredService<IOptions<HostFilteringOptions>>()
            .Value;

        CollectionAssert.AreEquivalent(
            new[] { "example.com", "www.example.com" },
            options.AllowedHosts.ToArray());
    }
}
