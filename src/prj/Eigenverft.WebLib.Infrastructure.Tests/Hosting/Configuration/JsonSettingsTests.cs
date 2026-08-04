using System;
using System.Collections.Generic;
using System.IO;

using Eigenverft.WebLib.Infrastructure.Hosting.Configuration.JsonSettings;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;

namespace Eigenverft.WebLib.Infrastructure.Tests;

[TestClass]
public sealed class JsonSettingsTests
{
    [TestMethod]
    public void EnvironmentJsonSettingsUseEnvironmentFileAsHighestPrecedence()
    {
        using var directory = new TemporaryDirectory();
        string commonPath = directory.Write("settings.json", """
            {
              "Shared": "common",
              "CommonOnly": "common"
            }
            """);
        _ = directory.Write("settings.Production.json", """
            {
              "Shared": "production"
            }
            """);
        WebApplicationBuilder builder = CreateBuilder(directory.Path, "Production");
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?> { ["Shared"] = "earlier" });

        ((IConfigurationBuilder)builder.Configuration).AddEnvironmentJsonSettings(
            System.IO.Path.GetFileName(commonPath),
            builder.Environment,
            reloadOnChange: false);

        Assert.AreEqual("production", builder.Configuration["Shared"]);
        Assert.AreEqual("common", builder.Configuration["CommonOnly"]);
    }

    [TestMethod]
    public void MissingRequiredEnvironmentJsonSettingsAreRejected()
    {
        using var directory = new TemporaryDirectory();
        string commonPath = directory.Write("settings.json", "{}");
        WebApplicationBuilder builder = CreateBuilder(directory.Path, "Production");

        FileNotFoundException exception = Assert.ThrowsExactly<FileNotFoundException>(() =>
            ((IConfigurationBuilder)builder.Configuration).AddEnvironmentJsonSettings(
                commonPath,
                builder.Environment,
                optionalEnvironment: false,
                reloadOnChange: false));

        StringAssert.EndsWith(exception.FileName, "settings.Production.json");
    }

    [TestMethod]
    public void FileEncoderMatchesCompletePathsAndDoesNotRewriteEncodedValues()
    {
        using var directory = new TemporaryDirectory();
        string settingsPath = directory.Write("settings.json", """
            {
              // This comment is removed when an update requires a rewrite.
              "Authentication": {
                "Password": "secret",
                "User": "alice"
              }
            }
            """);

        int firstUpdateCount = JsonSettingsFileEncoder.EncodeMatchingValuesInPlace(
            settingsPath,
            "Authentication:*Passw*",
            JsonSettingsValueEncoders.EncodeBase64);
        string encodedJson = File.ReadAllText(settingsPath);
        DateTime preservedWriteTime = new(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(settingsPath, preservedWriteTime);

        int secondUpdateCount = JsonSettingsFileEncoder.EncodeMatchingValuesInPlace(
            settingsPath,
            "Authentication:*Passw*",
            JsonSettingsValueEncoders.EncodeBase64);

        Assert.AreEqual(1, firstUpdateCount);
        Assert.AreEqual(0, secondUpdateCount);
        StringAssert.Contains(encodedJson, "enc:q7m2n4:");
        StringAssert.Contains(encodedJson, "alice");
        Assert.IsFalse(encodedJson.Contains("This comment", StringComparison.Ordinal));
        Assert.AreEqual(preservedWriteTime, File.GetLastWriteTimeUtc(settingsPath));
    }

    [TestMethod]
    public void DecodingJsonProviderReturnsClearTextWithoutChangingTheFile()
    {
        using var directory = new TemporaryDirectory();
        string encodedValue = JsonSettingsValueEncoders.EncodeBase64("secret");
        string settingsPath = directory.Write(
            "settings.json",
            $$"""
            {
              "Password": "{{encodedValue}}"
            }
            """);
        string persistedBeforeLoad = File.ReadAllText(settingsPath);
        var configuration = new ConfigurationManager();

        ((IConfigurationBuilder)configuration).AddJsonFileWithDecodedValues(
            settingsPath,
            reloadOnChange: false);

        Assert.AreEqual("secret", configuration["Password"]);
        Assert.AreEqual(persistedBeforeLoad, File.ReadAllText(settingsPath));
    }

    [TestMethod]
    public void MalformedEncodedValueRemainsAvailableForConsumerValidation()
    {
        using var directory = new TemporaryDirectory();
        string settingsPath = directory.Write("settings.json", """
            {
              "Password": "enc:q7m2n4:not-base64!"
            }
            """);
        var configuration = new ConfigurationManager();

        ((IConfigurationBuilder)configuration).AddJsonFileWithDecodedValues(
            settingsPath,
            reloadOnChange: false);

        Assert.AreEqual("enc:q7m2n4:not-base64!", configuration["Password"]);
    }

    [TestMethod]
    public void CombinedWorkflowEncodesBothFilesAndLoadsEnvironmentOverride()
    {
        using var directory = new TemporaryDirectory();
        string commonPath = directory.Write("settings.json", """
            {
              "Credentials": {
                "Password": "common"
              }
            }
            """);
        string environmentPath = directory.Write("settings.Production.json", """
            {
              "Credentials": {
                "Password": "production"
              }
            }
            """);
        WebApplicationBuilder builder = CreateBuilder(directory.Path, "Production");
        builder.Configuration.Sources.Clear();

        builder.EncodeAndAddEnvironmentJsonSettings(
            commonPath,
            keyPathPattern: "*Password",
            encode: JsonSettingsValueEncoders.EncodeBase64,
            reloadOnChange: false);

        Assert.AreEqual("production", builder.Configuration["Credentials:Password"]);
        StringAssert.Contains(File.ReadAllText(commonPath), "enc:q7m2n4:");
        StringAssert.Contains(File.ReadAllText(environmentPath), "enc:q7m2n4:");
    }

    [TestMethod]
    public void DpapiMachineBase64UrlRoundTripsOnWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var directory = new TemporaryDirectory();
        string encodedValue = JsonSettingsValueEncoders.EncodeDpapiMachineBase64Url("secret");
        string settingsPath = directory.Write(
            "settings.json",
            $$"""
            {
              "Password": "{{encodedValue}}"
            }
            """);
        var configuration = new ConfigurationManager();

        ((IConfigurationBuilder)configuration).AddJsonFileWithDecodedValues(
            settingsPath,
            reloadOnChange: false);

        Assert.AreEqual("secret", configuration["Password"]);
        StringAssert.StartsWith(encodedValue, "enc:k4v8s2:");
    }

    private static WebApplicationBuilder CreateBuilder(string contentRootPath, string environmentName)
    {
        return WebApplication.CreateBuilder(
            new WebApplicationOptions
            {
                Args = Array.Empty<string>(),
                ContentRootPath = contentRootPath,
                EnvironmentName = environmentName,
            });
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"Eigenverft.WebLib.Infrastructure.Tests.{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string Write(string fileName, string content)
        {
            string path = System.IO.Path.Combine(Path, fileName);
            File.WriteAllText(path, content);
            return path;
        }

        public void Dispose()
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
