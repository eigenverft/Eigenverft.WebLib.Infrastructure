using System;
using System.Collections.Generic;

using Eigenverft.WebLib.Infrastructure.Hosting.Configuration.Sources;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.CommandLine;
using Microsoft.Extensions.Configuration.EnvironmentVariables;

namespace Eigenverft.WebLib.Infrastructure.Tests;

[TestClass]
public sealed class ConfigurationSourcesTests
{
    [TestMethod]
    public void MinimalSourcesReplaceExistingSourcesWithEnvironmentVariables()
    {
        WebApplicationBuilder builder = CreateBuilder();
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?> { ["Previous"] = "value" });

        WebApplicationBuilder result = builder.ResetToMinimalConfigurationSources();
        IList<IConfigurationSource> sources = GetSources(builder);

        Assert.AreSame(builder, result);
        Assert.AreEqual(1, sources.Count);
        Assert.IsInstanceOfType<EnvironmentVariablesConfigurationSource>(sources[0]);
    }

    [TestMethod]
    public void CommandLineArgumentsAreAddedAfterEnvironmentVariables()
    {
        WebApplicationBuilder builder = CreateBuilder();

        builder.ResetToMinimalConfigurationSources(includeCommandLineArguments: true);
        IList<IConfigurationSource> sources = GetSources(builder);

        Assert.AreEqual(2, sources.Count);
        Assert.IsInstanceOfType<EnvironmentVariablesConfigurationSource>(sources[0]);
        Assert.IsInstanceOfType<CommandLineConfigurationSource>(sources[1]);
    }

    [TestMethod]
    public void AllMinimalSourcesCanBeDisabled()
    {
        WebApplicationBuilder builder = CreateBuilder();

        builder.ResetToMinimalConfigurationSources(
            includeCommandLineArguments: false,
            includeEnvironmentVariables: false);

        Assert.AreEqual(0, GetSources(builder).Count);
    }

    [TestMethod]
    public void NullBuilderIsRejected()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() =>
            WebApplicationBuilderConfigurationExtensions.ResetToMinimalConfigurationSources(null!));
    }

    private static WebApplicationBuilder CreateBuilder()
    {
        return WebApplication.CreateBuilder(
            new WebApplicationOptions { Args = Array.Empty<string>() });
    }

    private static IList<IConfigurationSource> GetSources(WebApplicationBuilder builder)
    {
        return ((IConfigurationBuilder)builder.Configuration).Sources;
    }
}
