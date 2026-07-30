using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

using Eigenverft.WebLib.Infrastructure.Hosting;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Eigenverft.WebLib.Infrastructure.Tests;

[TestClass]
public sealed class DirectoryLayoutTests
{
    [TestMethod]
    public void FactoryUsesExecutableRootAndCreatesConfiguredDirectories()
    {
        WebApplicationBuilder builder = CreateBuilder();
        AppDirectoryLayout layout = builder.GetDirectoryLayout();
        string expectedRoot = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);

        Assert.AreEqual(expectedRoot, layout.RootPath);
        Assert.AreEqual(expectedRoot, builder.Environment.ContentRootPath);
        Assert.AreEqual(Path.Combine(expectedRoot, "wwwroot"), builder.Environment.WebRootPath);
        Assert.AreEqual(Path.Combine(expectedRoot, "AppLogs"), layout["ApplicationLogFiles"]);
        Assert.AreEqual(Path.Combine(expectedRoot, "AppData"), layout["ApplicationData"]);
        Assert.AreEqual(Path.Combine(expectedRoot, "AppCerts"), layout["ApplicationCerts"]);
        Assert.AreEqual(Path.Combine(expectedRoot, "AppSettings"), layout["ApplicationSettings"]);
        Assert.AreEqual(Path.Combine(expectedRoot, "wwwroot"), layout["Web"]);

        foreach (string directoryPath in layout.GetByKey.Values)
        {
            Assert.IsTrue(Directory.Exists(directoryPath), $"Expected directory '{directoryPath}' to exist.");
        }
    }

    [TestMethod]
    public async Task FactoryRegistersTheSameLayoutForDependencyInjection()
    {
        WebApplicationBuilder builder = CreateBuilder();
        AppDirectoryLayout beforeBuild = builder.GetDirectoryLayout();

        await using WebApplication application = builder.Build();
        AppDirectoryLayout fromServices = application.Services.GetRequiredService<AppDirectoryLayout>();

        Assert.AreSame(beforeBuild, fromServices);
    }

    [TestMethod]
    public void FactoryRejectsNestedFolderMappings()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            WebApplicationBuilderFactory.CreateWithDefaultDirectory(
                new Dictionary<string, string>
                {
                    ["ApplicationData"] = Path.Combine("nested", "AppData"),
                },
                includeCommandLineArgs: false));
    }

    private static WebApplicationBuilder CreateBuilder()
    {
        return WebApplicationBuilderFactory.CreateWithDefaultDirectory(
            new Dictionary<string, string>
            {
                ["ApplicationLogFiles"] = "AppLogs",
                ["ApplicationData"] = "AppData",
                ["ApplicationCerts"] = "AppCerts",
                ["ApplicationSettings"] = "AppSettings",
            },
            includeCommandLineArgs: false);
    }
}
