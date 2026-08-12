using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

using Eigenverft.NetLib.Infrastructure.Hosting.DirectoryLayout;
using Eigenverft.WebLib.Infrastructure.Hosting.DirectoryLayout;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Eigenverft.WebLib.Infrastructure.Tests;

[TestClass]
public sealed class DirectoryLayoutTests
{
    [TestMethod]
    public void FactoryUsesExecutableRootAndCreatesDefaultDirectories()
    {
        WebApplicationBuilder builder = CreateBuilder();
        IAppDirectoryLayout layout = builder.GetDirectoryLayout();
        string expectedRoot = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);

        Assert.AreEqual(expectedRoot, layout.RootPath);
        Assert.AreEqual(expectedRoot, builder.Environment.ContentRootPath);
        Assert.AreEqual(Path.Combine(expectedRoot, "wwwroot"), builder.Environment.WebRootPath);
        Assert.AreEqual(Path.Combine(expectedRoot, "AppLogs"), layout[DefaultDirectory.ApplicationLogFiles]);
        Assert.AreEqual(Path.Combine(expectedRoot, "AppData"), layout[DefaultDirectory.ApplicationData]);
        Assert.AreEqual(Path.Combine(expectedRoot, "AppState"), layout[DefaultDirectory.ApplicationState]);
        Assert.AreEqual(Path.Combine(expectedRoot, "AppCerts"), layout[DefaultDirectory.ApplicationCerts]);
        Assert.AreEqual(Path.Combine(expectedRoot, "AppSettings"), layout[DefaultDirectory.ApplicationSettings]);
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
        IAppDirectoryLayout beforeBuild = builder.GetDirectoryLayout();

        await using WebApplication application = builder.Build();
        IAppDirectoryLayout fromServices = application.Services.GetRequiredService<IAppDirectoryLayout>();

        Assert.AreSame(beforeBuild, fromServices);
    }

    [TestMethod]
    public void TypedOverridesRetainUnspecifiedDefaults()
    {
        WebApplicationBuilder builder = WebApplicationBuilderFactory.CreateWithDefaultDirectory(
            new Dictionary<DefaultDirectory, string>
            {
                [DefaultDirectory.ApplicationData] = "State",
            });

        IAppDirectoryLayout layout = builder.GetDirectoryLayout();

        Assert.AreEqual("State", Path.GetFileName(layout[DefaultDirectory.ApplicationData]));
        Assert.AreEqual("AppLogs", Path.GetFileName(layout[DefaultDirectory.ApplicationLogFiles]));
        Assert.AreEqual("AppState", Path.GetFileName(layout[DefaultDirectory.ApplicationState]));
        Assert.AreEqual("AppCerts", Path.GetFileName(layout[DefaultDirectory.ApplicationCerts]));
        Assert.AreEqual("AppSettings", Path.GetFileName(layout[DefaultDirectory.ApplicationSettings]));
        Assert.AreEqual("wwwroot", Path.GetFileName(layout["Web"]));
    }

    [TestMethod]
    public void FactoryRejectsNestedFolderMappings()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            WebApplicationBuilderFactory.CreateWithDefaultDirectory(
                new Dictionary<DefaultDirectory, string>
                {
                    [DefaultDirectory.ApplicationData] = Path.Combine("nested", "AppData"),
                }));
    }

    [TestMethod]
    public void FactoryAcceptsExplicitArguments()
    {
        WebApplicationBuilder builder = WebApplicationBuilderFactory.CreateWithDefaultDirectory(
            new[] { "--SampleSetting=Expected" });

        Assert.AreEqual("Expected", builder.Configuration["SampleSetting"]);
        Assert.IsNotNull(builder.GetDirectoryLayout());
    }

    private static WebApplicationBuilder CreateBuilder()
    {
        return WebApplicationBuilderFactory.CreateWithDefaultDirectory();
    }
}
