using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

using Eigenverft.WebLib.Infrastructure.Hosting.Configuration.JsonSettings;
using Eigenverft.WebLib.Infrastructure.Security.MachineBinding;
using Eigenverft.WebLib.Infrastructure.Text;
using Eigenverft.WebLib.Infrastructure.Transformations;

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
    public void ReversibleStringTransformsRoundTripWithoutJsonPersistenceFraming()
    {
        ReversibleStringTransform base64 = ReversibleStringTransforms.Base64;
        Assert.AreEqual("aGVsbG8=", base64.Apply("hello"));
        Assert.IsTrue(base64.TryReverse("aGVsbG8=", out string base64Clear));
        Assert.AreEqual("hello", base64Clear);

        ReversibleStringTransform base92 = ReversibleStringTransforms.Base92JsonSafe;
        string base92Value = base92.Apply("hello");
        Assert.IsFalse(base92Value.StartsWith("enc:", StringComparison.Ordinal));
        Assert.IsTrue(base92.TryReverse(base92Value, out string base92Clear));
        Assert.AreEqual("hello", base92Clear);

        Assert.AreEqual("Uryyb", ReversibleStringTransforms.Rot13.Apply("Hello"));
        Assert.IsTrue(ReversibleStringTransforms.Rot13.TryReverse("Uryyb", out string rot13Clear));
        Assert.AreEqual("Hello", rot13Clear);

        ReversibleStringTransform caesar = ReversibleStringTransforms.Caesar(5);
        string caesarValue = caesar.Apply("Hello");
        Assert.AreEqual("5:Mjqqt", caesarValue);
        Assert.IsTrue(caesar.TryReverse(caesarValue, out string caesarClear));
        Assert.AreEqual("Hello", caesarClear);
        Assert.IsFalse(ReversibleStringTransforms.Caesar(4).TryReverse(caesarValue, out string mismatchedCaesar));
        Assert.AreEqual(caesarValue, mismatchedCaesar);

        ReversibleStringTransform aes = ReversibleStringTransforms.AesPassword("transform-password");
        string aesValue = aes.Apply("sensitive-value");
        StringAssert.StartsWith(aesValue, "v1.");
        Assert.IsFalse(aesValue.StartsWith("enc:", StringComparison.Ordinal));
        Assert.IsTrue(aes.TryReverse(aesValue, out string aesClear));
        Assert.AreEqual("sensitive-value", aesClear);
        Assert.IsFalse(
            ReversibleStringTransforms.AesPassword("wrong-password").TryReverse(aesValue, out string failedAes));
        Assert.AreEqual(aesValue, failedAes);

        ReversibleStringTransform composed = ReversibleStringTransforms.Compose(
            ReversibleStringTransforms.Rot13,
            ReversibleStringTransforms.Base64);
        string composedValue = composed.Apply("Hello");
        Assert.AreEqual(Convert.ToBase64String(Encoding.UTF8.GetBytes("Uryyb")), composedValue);
        Assert.IsTrue(composed.TryReverse(composedValue, out string composedClear));
        Assert.AreEqual("Hello", composedClear);
    }

    [TestMethod]
    public void MachineBoundTransformRoundTripsWithoutJsonPersistenceFraming()
    {
        ReversibleStringTransform transform = ReversibleStringTransforms.PhysicalMachineBoundAes();
        string transformed = transform.Apply("machine-bound-value");

        Assert.IsFalse(transformed.StartsWith("enc:", StringComparison.Ordinal));
        StringAssert.StartsWith(transformed, "v1.");
        Assert.IsTrue(transform.TryReverse(transformed, out string clearText));
        Assert.AreEqual("machine-bound-value", clearText);
    }

    [TestMethod]
    public void DpapiTransformsRoundTripWithoutJsonPersistenceFramingOnWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Windows DPAPI is available only on Windows.");
        }

        ReversibleStringTransform base64 = ReversibleStringTransforms.DpapiMachine;
        string base64Value = base64.Apply("dpapi-base64");
        Assert.IsFalse(base64Value.StartsWith("enc:", StringComparison.Ordinal));
        Assert.IsTrue(base64.TryReverse(base64Value, out string base64Clear));
        Assert.AreEqual("dpapi-base64", base64Clear);

        ReversibleStringTransform base64Url = ReversibleStringTransforms.DpapiMachineBase64Url;
        string base64UrlValue = base64Url.Apply("dpapi-base64url");
        Assert.IsFalse(base64UrlValue.StartsWith("enc:", StringComparison.Ordinal));
        Assert.IsTrue(base64Url.TryReverse(base64UrlValue, out string base64UrlClear));
        Assert.AreEqual("dpapi-base64url", base64UrlClear);
    }

    [TestMethod]
    public void JsonSettingsCodecsKeepExistingFramingAroundNeutralTransformPayloads()
    {
        Assert.AreEqual(
            "enc:q7m2n4:" + ReversibleStringTransforms.Base64.Apply("hello"),
            JsonSettingsValueEncoders.Base64.Encode("hello"));
        Assert.AreEqual(
            "enc:r1t3o7:" + ReversibleStringTransforms.Rot13.Apply("Hello"),
            JsonSettingsValueEncoders.Rot13.Encode("Hello"));
        Assert.AreEqual(
            "enc:c4e5s2:" + ReversibleStringTransforms.Caesar(5).Apply("Hello"),
            JsonSettingsValueEncoders.Caesar(5).Encode("Hello"));
        Assert.AreEqual(
            "enc:b9j2s7:" + ReversibleStringTransforms.Base92JsonSafe.Apply("hello"),
            JsonSettingsValueEncoders.Base92JsonSafe.Encode("hello"));
    }

    [TestMethod]
    public void DataProtectionTransformKeepsIsolationContextOutsideJsonPersistence()
    {
        using var directory = new TemporaryDirectory();
        string keyDirectory = Path.Combine(directory.Path, "transform-dp-keys");
        ReversibleStringTransform transform = ReversibleStringTransforms.DataProtection(
            keyDirectory,
            "TransformTests",
            "purpose-v1");

        string protectedValue = transform.Apply("protected-value");
        Assert.IsFalse(protectedValue.StartsWith("enc:", StringComparison.Ordinal));
        Assert.IsTrue(transform.TryReverse(protectedValue, out string clearText));
        Assert.AreEqual("protected-value", clearText);

        ReversibleStringTransform wrongPurpose = ReversibleStringTransforms.DataProtection(
            keyDirectory,
            "TransformTests",
            "different-purpose");
        Assert.IsFalse(wrongPurpose.TryReverse(protectedValue, out string unavailable));
        Assert.AreEqual(protectedValue, unavailable);
    }

    [TestMethod]
    public void CandidatePreparationFactoriesMirrorAllOriginalCodecFactoriesAndShortcuts()
    {
        string[] codecProperties = typeof(JsonSettingsValueEncoders)
            .GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(property => property.PropertyType == typeof(JsonSettingsValueCodec))
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        string[] candidateProperties = typeof(JsonConfigurationCandidatePreparations)
            .GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(property => property.PropertyType == typeof(JsonConfigurationCandidatePreparation))
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        CollectionAssert.AreEqual(codecProperties, candidateProperties);

        string[] codecFactories = typeof(JsonSettingsValueEncoders)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => method.ReturnType == typeof(JsonSettingsValueCodec))
            .Where(method => !string.Equals(method.Name, nameof(JsonSettingsValueEncoders.Compose), StringComparison.Ordinal))
            .Select(CreateFactorySignature)
            .OrderBy(signature => signature, StringComparer.Ordinal)
            .ToArray();
        string[] candidateFactories = typeof(JsonConfigurationCandidatePreparations)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => method.ReturnType == typeof(JsonConfigurationCandidatePreparation))
            .Where(method =>
                !string.Equals(method.Name, nameof(JsonConfigurationCandidatePreparations.Compose), StringComparison.Ordinal) &&
                !string.Equals(method.Name, nameof(JsonConfigurationCandidatePreparations.Decode), StringComparison.Ordinal) &&
                !string.Equals(method.Name, nameof(JsonConfigurationCandidatePreparations.From), StringComparison.Ordinal))
            .Select(CreateFactorySignature)
            .OrderBy(signature => signature, StringComparer.Ordinal)
            .ToArray();

        CollectionAssert.AreEqual(codecFactories, candidateFactories);
    }

    [TestMethod]
    public void CandidatePreparationFactoriesAdaptOriginalValueCodecs()
    {
        using var directory = new TemporaryDirectory();

        AssertCandidateDecodes(
            directory,
            "candidate-base64.json",
            JsonSettingsValueEncoders.Base64,
            JsonConfigurationCandidatePreparations.Base64);
        AssertCandidateDecodes(
            directory,
            "candidate-base92.json",
            JsonSettingsValueEncoders.Base92JsonSafe,
            JsonConfigurationCandidatePreparations.Base92JsonSafe);
        AssertCandidateDecodes(
            directory,
            "candidate-rot13.json",
            JsonSettingsValueEncoders.Rot13,
            JsonConfigurationCandidatePreparations.Rot13);
        AssertCandidateDecodes(
            directory,
            "candidate-caesar.json",
            JsonSettingsValueEncoders.Caesar(7),
            JsonConfigurationCandidatePreparations.Caesar(7));
        AssertCandidateDecodes(
            directory,
            "candidate-aes.json",
            JsonSettingsValueEncoders.AesPassword("candidate-password"),
            JsonConfigurationCandidatePreparations.AesPassword("candidate-password"));

        string dataProtectionDirectory = Path.Combine(directory.Path, "candidate-dp-keys");
        AssertCandidateDecodes(
            directory,
            "candidate-dp.json",
            JsonSettingsValueEncoders.DataProtection(dataProtectionDirectory),
            JsonConfigurationCandidatePreparations.DataProtection(dataProtectionDirectory));

        _ = JsonConfigurationCandidatePreparations.PhysicalMachineBoundAes();
        _ = JsonConfigurationCandidatePreparations.Default("candidate-password", dataProtectionDirectory);
        _ = JsonConfigurationCandidatePreparations.Default(
            Encoding.ASCII.GetBytes("candidate-password"),
            dataProtectionDirectory);

        if (OperatingSystem.IsWindows())
        {
            _ = JsonConfigurationCandidatePreparations.DpapiMachine;
            _ = JsonConfigurationCandidatePreparations.DpapiMachineBase64Url;
            _ = JsonConfigurationCandidatePreparations.DpapiWithRot13();
            _ = JsonConfigurationCandidatePreparations.DpapiWithCaesar(4);
            _ = JsonConfigurationCandidatePreparations.DefaultWindows("candidate-password", dataProtectionDirectory);
            _ = JsonConfigurationCandidatePreparations.DefaultWindows(
                Encoding.ASCII.GetBytes("candidate-password"),
                dataProtectionDirectory);
        }
    }

    [TestMethod]
    public void CandidatePreparationComposeBundlesStepsInExecutionOrder()
    {
        using var directory = new TemporaryDirectory();
        string encoded = JsonSettingsValueEncoders.Base64.Encode("bundled-value");
        string settingsPath = directory.Write(
            "candidate-compose.json",
            $$"""{ "Value": "{{encoded}}" }""");
        JsonConfigurationCandidatePreparation preparation = JsonConfigurationCandidatePreparations.Compose(
            JsonConfigurationCandidatePreparations.Base64,
            new CopyPreparedValuePreparation());

        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddPreparedJsonFile(settingsPath, preparation)
            .Build();

        Assert.AreEqual("bundled-value", configuration["Value"]);
        Assert.AreEqual("bundled-value", configuration["ObservedAfterDecode"]);
        StringAssert.Contains(preparation.Name, "Decode(Base64)");
    }

    [TestMethod]
    public void PreparedEnvironmentJsonSettingsApplyPreparationAndEnvironmentPrecedenceWithoutMutatingFiles()
    {
        using var directory = new TemporaryDirectory();
        var xor = new XorBase64JsonConfigurationSourcePreparation(0x2D, "*Secret*");
        string commonEncoded = xor.EncodeValue("common-secret");
        string productionEncoded = xor.EncodeValue("production-secret");
        string commonPath = directory.Write("prepared.json", $$"""
            {
              "Shared": "common",
              "SecretValue": "{{commonEncoded}}"
            }
            """);
        string productionPath = directory.Write("prepared.Production.json", $$"""
            {
              "Shared": "production",
              "SecretValue": "{{productionEncoded}}"
            }
            """);
        WebApplicationBuilder builder = CreateBuilder(directory.Path, "Production");
        builder.Configuration.Sources.Clear();

        builder.AddPreparedEnvironmentJsonSettings(
            Path.GetFileName(commonPath),
            xor,
            reloadOnChange: false);

        Assert.AreEqual("production", builder.Configuration["Shared"]);
        Assert.AreEqual("production-secret", builder.Configuration["SecretValue"]);
        StringAssert.Contains(File.ReadAllText(commonPath), commonEncoded);
        StringAssert.Contains(File.ReadAllText(productionPath), productionEncoded);
    }

    [TestMethod]
    public void PreparedJsonReloadFailureKeepsPreviouslyPublishedSnapshot()
    {
        using var directory = new TemporaryDirectory();
        var xor = new XorBase64JsonConfigurationSourcePreparation(0x6C, "Secret");
        string settingsPath = directory.Write(
            "prepared-reload.json",
            $$"""{ "Mode": "Stable", "Secret": "{{xor.EncodeValue("stable-secret")}}" }""");
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddPreparedJsonFile(
                settingsPath,
                new IJsonConfigurationSourcePreparation[] { xor },
                reloadOnChange: false)
            .Build();

        Assert.AreEqual("Stable", configuration["Mode"]);
        Assert.AreEqual("stable-secret", configuration["Secret"]);

        File.WriteAllText(
            settingsPath,
            """{ "Mode": "Broken", "Secret": "xor1:not-valid-base64!" }""");

        InvalidDataException reloadException = Assert.ThrowsExactly<InvalidDataException>(() => configuration.Reload());
        Assert.IsInstanceOfType<JsonConfigurationSourcePreparationException>(reloadException.InnerException);

        Assert.AreEqual("Stable", configuration["Mode"]);
        Assert.AreEqual("stable-secret", configuration["Secret"]);
    }

    [TestMethod]
    public void RetainedPreparationValuesCannotMutatePublishedPreparedJsonSnapshot()
    {
        using var directory = new TemporaryDirectory();
        string settingsPath = directory.Write("prepared-retained.json", """{ "Mode": "Stable" }""");
        var preparation = new RetainingPreparation();
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddPreparedJsonFile(
                settingsPath,
                new IJsonConfigurationSourcePreparation[] { preparation })
            .Build();

        Assert.AreEqual("Stable", configuration["Mode"]);
        Assert.IsNotNull(preparation.RetainedValues);

        preparation.RetainedValues["Mode"] = "Tampered";

        Assert.AreEqual("Stable", configuration["Mode"]);
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
            JsonSettingsValueEncoders.Base64);
        string encodedJson = File.ReadAllText(settingsPath);
        DateTime preservedWriteTime = new(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(settingsPath, preservedWriteTime);

        int secondUpdateCount = JsonSettingsFileEncoder.EncodeMatchingValuesInPlace(
            settingsPath,
            "Authentication:*Passw*",
            JsonSettingsValueEncoders.Base64);

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
    public void FileEncoderDoesNotImplicitlyMigrateRecognizedCodecValues()
    {
        using var directory = new TemporaryDirectory();
        string existingEncodedValue = JsonSettingsValueEncoders.EncodeBase64("secret");
        string settingsPath = directory.Write(
            "settings.json",
            $"{{ \"Password\": \"{existingEncodedValue}\" }}");

        int updateCount = JsonSettingsFileEncoder.EncodeMatchingValuesInPlace(
            settingsPath,
            "Password",
            JsonSettingsValueEncoders.AesPassword("replacement-password"));

        Assert.AreEqual(0, updateCount);
        StringAssert.Contains(File.ReadAllText(settingsPath), existingEncodedValue);
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
            codec: JsonSettingsValueEncoders.Base64,
            reloadOnChange: false);

        Assert.AreEqual("production", builder.Configuration["Credentials:Password"]);
        StringAssert.Contains(File.ReadAllText(commonPath), "enc:q7m2n4:");
        StringAssert.Contains(File.ReadAllText(environmentPath), "enc:q7m2n4:");
    }

    [TestMethod]
    public void AesPasswordCodecRoundTripsWithExplicitCodec()
    {
        using var directory = new TemporaryDirectory();
        JsonSettingsValueCodec codec = JsonSettingsValueEncoders.AesPassword("test-only-password");
        string encodedValue = codec.Encode("secret");
        string settingsPath = directory.Write(
            "settings.json",
            $"{{ \"Password\": \"{encodedValue}\" }}");
        var configuration = new ConfigurationManager();

        ((IConfigurationBuilder)configuration).AddJsonFileWithDecodedValues(
            settingsPath,
            reloadOnChange: false,
            decodeCodec: codec);

        Assert.AreEqual("secret", configuration["Password"]);
        StringAssert.StartsWith(encodedValue, "enc:a3s6p1:v1.");
    }

    [TestMethod]
    public void AesPasswordByteRepresentationMatchesSameReadableString()
    {
        byte[] passwordBytes = { 0x68, 0x65, 0x6C, 0x6C, 0x6F };
        JsonSettingsValueCodec stringCodec = JsonSettingsValueEncoders.AesPassword("hello");
        JsonSettingsValueCodec byteCodec = JsonSettingsValueEncoders.AesPassword(passwordBytes);

        string encodedByString = stringCodec.Encode("first-secret");
        string encodedByBytes = byteCodec.Encode("second-secret");

        Assert.IsTrue(byteCodec.TryDecode(encodedByString, out string firstClearText));
        Assert.AreEqual("first-secret", firstClearText);
        Assert.IsTrue(stringCodec.TryDecode(encodedByBytes, out string secondClearText));
        Assert.AreEqual("second-secret", secondClearText);
    }

    [TestMethod]
    public void AesPasswordRejectsNonVisiblePasswordRepresentations()
    {
        _ = Assert.ThrowsExactly<ArgumentException>(() => JsonSettingsValueEncoders.AesPassword("hidden\u200Bvalue"));
        _ = Assert.ThrowsExactly<ArgumentException>(() => JsonSettingsValueEncoders.AesPassword(new byte[] { 0x00 }));
        _ = Assert.ThrowsExactly<ArgumentException>(() => JsonSettingsValueEncoders.AesPassword(new byte[] { 0xFF }));
    }

    [TestMethod]
    public void PhysicalMachineBoundAesUsesCurrentMachineFingerprintWhenAvailable()
    {
        if (!PhysicalMachineBinding.TryGetFingerprint(out _))
        {
            return;
        }

        JsonSettingsValueCodec writeCodec = JsonSettingsValueEncoders.PhysicalMachineBoundAes();
        string encodedValue = writeCodec.Encode("machine-secret");
        JsonSettingsValueCodec readCodec = JsonSettingsValueEncoders.PhysicalMachineBoundAes();

        Assert.AreEqual("PhysicalMachineBoundAes", writeCodec.Name);
        Assert.IsTrue(readCodec.TryDecode(encodedValue, out string clearText));
        Assert.AreEqual("machine-secret", clearText);
        StringAssert.StartsWith(encodedValue, "enc:a3s6p1:v1.");
    }

    [TestMethod]
    public void AesPasswordCodecRejectsUnknownPersistedPayloadVersionSoftly()
    {
        using var directory = new TemporaryDirectory();
        JsonSettingsValueCodec codec = JsonSettingsValueEncoders.AesPassword("test-only-password");
        string encodedValue = codec.Encode("secret");
        string unsupportedVersionValue = encodedValue.Replace(
            "enc:a3s6p1:v1.",
            "enc:a3s6p1:v2.",
            StringComparison.Ordinal);
        string settingsPath = directory.Write(
            "settings.json",
            $"{{ \"Password\": \"{unsupportedVersionValue}\" }}");
        var configuration = new ConfigurationManager();

        ((IConfigurationBuilder)configuration).AddJsonFileWithDecodedValues(
            settingsPath,
            reloadOnChange: false,
            decodeCodec: codec);

        Assert.AreEqual(unsupportedVersionValue, configuration["Password"]);
    }

    [TestMethod]
    public void ComposedCodecsRoundTripInEitherOrder()
    {
        using var directory = new TemporaryDirectory();
        JsonSettingsValueCodec aesThenBase64 = JsonSettingsValueEncoders.Compose(
            JsonSettingsValueEncoders.AesPassword("test-only-password"),
            JsonSettingsValueEncoders.Base64);
        JsonSettingsValueCodec base64ThenAes = JsonSettingsValueEncoders.Compose(
            JsonSettingsValueEncoders.Base64,
            JsonSettingsValueEncoders.AesPassword("test-only-password"));
        string firstEncoded = aesThenBase64.Encode("first-secret");
        string secondEncoded = base64ThenAes.Encode("second-secret");
        string firstPath = directory.Write("first.json", $"{{ \"Password\": \"{firstEncoded}\" }}");
        string secondPath = directory.Write("second.json", $"{{ \"Password\": \"{secondEncoded}\" }}");
        var firstConfiguration = new ConfigurationManager();
        var secondConfiguration = new ConfigurationManager();

        ((IConfigurationBuilder)firstConfiguration).AddJsonFileWithDecodedValues(
            firstPath,
            reloadOnChange: false,
            decodeCodec: aesThenBase64);
        ((IConfigurationBuilder)secondConfiguration).AddJsonFileWithDecodedValues(
            secondPath,
            reloadOnChange: false,
            decodeCodec: base64ThenAes);

        Assert.AreEqual("first-secret", firstConfiguration["Password"]);
        Assert.AreEqual("second-secret", secondConfiguration["Password"]);
        StringAssert.StartsWith(firstEncoded, "enc:q7m2n4:");
        StringAssert.StartsWith(secondEncoded, "enc:a3s6p1:v1.");
    }

    [TestMethod]
    public void ComposedCodecWithWrongProtectionContextRemainsEncoded()
    {
        using var directory = new TemporaryDirectory();
        JsonSettingsValueCodec writeCodec = JsonSettingsValueEncoders.Compose(
            JsonSettingsValueEncoders.AesPassword("test-only-password"),
            JsonSettingsValueEncoders.Base64);
        JsonSettingsValueCodec wrongReadCodec = JsonSettingsValueEncoders.Compose(
            JsonSettingsValueEncoders.AesPassword("wrong-test-password"),
            JsonSettingsValueEncoders.Base64);
        string encodedValue = writeCodec.Encode("secret");
        string settingsPath = directory.Write("settings.json", $"{{ \"Password\": \"{encodedValue}\" }}");
        var configuration = new ConfigurationManager();

        ((IConfigurationBuilder)configuration).AddJsonFileWithDecodedValues(
            settingsPath,
            reloadOnChange: false,
            decodeCodec: wrongReadCodec);

        Assert.AreEqual(encodedValue, configuration["Password"]);
    }

    [TestMethod]
    public void ComposedTryDecodeRollsBackWhenAnInnerProtectionStageFails()
    {
        JsonSettingsValueCodec writeCodec = JsonSettingsValueEncoders.Compose(
            JsonSettingsValueEncoders.AesPassword("test-only-password"),
            JsonSettingsValueEncoders.Base64);
        JsonSettingsValueCodec wrongReadCodec = JsonSettingsValueEncoders.Compose(
            JsonSettingsValueEncoders.AesPassword("wrong-test-password"),
            JsonSettingsValueEncoders.Base64);
        string encodedValue = writeCodec.Encode("secret");

        bool decoded = wrongReadCodec.TryDecode(encodedValue, out string clearText);

        Assert.IsFalse(decoded);
        Assert.AreEqual(encodedValue, clearText);
    }

    [TestMethod]
    public void DirectCodecTryDecodeAlwaysRollsBackOnFailure()
    {
        string encodedValue = JsonSettingsValueEncoders.Caesar(3).Encode("secret");

        bool decoded = JsonSettingsValueEncoders.Caesar(4).TryDecode(encodedValue, out string clearText);

        Assert.IsFalse(decoded);
        Assert.AreEqual(encodedValue, clearText);
    }

    [TestMethod]
    public void GenericDecoderRollsBackOuterLayersWhenInnerContextIsUnavailable()
    {
        using var directory = new TemporaryDirectory();
        JsonSettingsValueCodec codec = JsonSettingsValueEncoders.Compose(
            JsonSettingsValueEncoders.AesPassword("test-only-password"),
            JsonSettingsValueEncoders.Base64);
        string encodedValue = codec.Encode("secret");
        string settingsPath = directory.Write("settings.json", $"{{ \"Password\": \"{encodedValue}\" }}");
        var configuration = new ConfigurationManager();

        ((IConfigurationBuilder)configuration).AddJsonFileWithDecodedValues(
            settingsPath,
            reloadOnChange: false);

        Assert.AreEqual(encodedValue, configuration["Password"]);
    }

    [TestMethod]
    public void GenericDecoderHasNoArbitraryFiveLayerLimit()
    {
        using var directory = new TemporaryDirectory();
        string encodedValue = "secret";
        for (int index = 0; index < 7; index++)
        {
            encodedValue = JsonSettingsValueEncoders.Base64.Encode(encodedValue);
        }

        string settingsPath = directory.Write("settings.json", $"{{ \"Password\": \"{encodedValue}\" }}");
        var configuration = new ConfigurationManager();
        ((IConfigurationBuilder)configuration).AddJsonFileWithDecodedValues(settingsPath, reloadOnChange: false);

        Assert.AreEqual("secret", configuration["Password"]);
    }

    [TestMethod]
    public void NumericWrapperTokenIsNotTreatedAsRecognizedPersistence()
    {
        using var directory = new TemporaryDirectory();
        string settingsPath = directory.Write("settings.json", "{ \"Password\": \"enc:999:anything\" }");

        int updateCount = JsonSettingsFileEncoder.EncodeMatchingValuesInPlace(
            settingsPath,
            "Password",
            JsonSettingsValueEncoders.Base64);

        string persisted = File.ReadAllText(settingsPath);
        Assert.AreEqual(1, updateCount);
        StringAssert.Contains(persisted, "enc:q7m2n4:");
    }

    [TestMethod]
    public void RepresentationCodecsRejectInvalidUtf8WithoutChangingTheValue()
    {
        string invalidBase64 = "enc:q7m2n4:" + Convert.ToBase64String(new byte[] { 0xFF });
        string invalidBase92 = "enc:b9j2s7:" + Base92JsonSafeEncoder.Encode(new byte[] { 0xFF });

        Assert.IsFalse(JsonSettingsValueEncoders.Base64.TryDecode(invalidBase64, out string base64ClearText));
        Assert.AreEqual(invalidBase64, base64ClearText);
        Assert.IsFalse(JsonSettingsValueEncoders.Base92JsonSafe.TryDecode(invalidBase92, out string base92ClearText));
        Assert.AreEqual(invalidBase92, base92ClearText);
    }

    [TestMethod]
    public void CombinedWorkflowSupportsComposedCodec()
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
        JsonSettingsValueCodec codec = JsonSettingsValueEncoders.Compose(
            JsonSettingsValueEncoders.AesPassword("test-only-password"),
            JsonSettingsValueEncoders.Base64);

        builder.EncodeAndAddEnvironmentJsonSettings(
            commonPath,
            keyPathPattern: "*Password",
            codec: codec,
            reloadOnChange: false);

        Assert.AreEqual("production", builder.Configuration["Credentials:Password"]);
        StringAssert.Contains(File.ReadAllText(commonPath), "enc:q7m2n4:");
        StringAssert.Contains(File.ReadAllText(environmentPath), "enc:q7m2n4:");
    }

    [TestMethod]
    public void DataProtectionCodecPersistsKeyRingAcrossCodecInstances()
    {
        using var directory = new TemporaryDirectory();
        string keyDirectoryPath = Path.Combine(directory.Path, "data-protection-keys");

        JsonSettingsValueCodec writeCodec = JsonSettingsValueEncoders.DataProtection(keyDirectoryPath);
        string encodedValue = writeCodec.Encode("secret");

        JsonSettingsValueCodec readCodec = JsonSettingsValueEncoders.DataProtection(keyDirectoryPath);
        string settingsPath = directory.Write(
            "data-protection.json",
            $"{{ \"Password\": \"{encodedValue}\" }}");
        var configuration = new ConfigurationManager();

        ((IConfigurationBuilder)configuration).AddJsonFileWithDecodedValues(
            settingsPath,
            reloadOnChange: false,
            decodeCodec: readCodec);

        Assert.AreEqual("secret", configuration["Password"]);
        StringAssert.StartsWith(encodedValue, "enc:d7p4r8:");
        Assert.IsGreaterThan(0, Directory.GetFiles(keyDirectoryPath, "key-*.xml").Length);
    }

    [TestMethod]
    public void DataProtectionCodecWithDifferentIsolationRemainsEncoded()
    {
        using var directory = new TemporaryDirectory();
        string keyDirectoryPath = Path.Combine(directory.Path, "data-protection-keys");
        JsonSettingsValueCodec writeCodec = JsonSettingsValueEncoders.DataProtection(
            keyDirectoryPath,
            "Eigenverft.Tests.Writer",
            "JsonSettingsTests.SharedPurpose");
        string encodedValue = writeCodec.Encode("secret");
        string settingsPath = directory.Write(
            "data-protection-isolation.json",
            $"{{ \"Password\": \"{encodedValue}\" }}");

        JsonSettingsValueCodec wrongApplicationCodec = JsonSettingsValueEncoders.DataProtection(
            keyDirectoryPath,
            "Eigenverft.Tests.OtherApplication",
            "JsonSettingsTests.SharedPurpose");
        var wrongApplicationConfiguration = new ConfigurationManager();
        ((IConfigurationBuilder)wrongApplicationConfiguration).AddJsonFileWithDecodedValues(
            settingsPath,
            reloadOnChange: false,
            decodeCodec: wrongApplicationCodec);

        JsonSettingsValueCodec wrongPurposeCodec = JsonSettingsValueEncoders.DataProtection(
            keyDirectoryPath,
            "Eigenverft.Tests.Writer",
            "JsonSettingsTests.OtherPurpose");
        var wrongPurposeConfiguration = new ConfigurationManager();
        ((IConfigurationBuilder)wrongPurposeConfiguration).AddJsonFileWithDecodedValues(
            settingsPath,
            reloadOnChange: false,
            decodeCodec: wrongPurposeCodec);

        Assert.AreEqual(encodedValue, wrongApplicationConfiguration["Password"]);
        Assert.AreEqual(encodedValue, wrongPurposeConfiguration["Password"]);
    }

    [TestMethod]
    public void DataProtectionCodecComposesWithOtherParameterizedCodecs()
    {
        using var directory = new TemporaryDirectory();
        string keyDirectoryPath = Path.Combine(directory.Path, "data-protection-keys");

        JsonSettingsValueCodec writeCodec = JsonSettingsValueEncoders.Compose(
            JsonSettingsValueEncoders.Rot13,
            JsonSettingsValueEncoders.Caesar(13),
            JsonSettingsValueEncoders.DataProtection(
                keyDirectoryPath,
                "Eigenverft.Tests.Composed",
                "JsonSettingsTests.ComposedDataProtection"),
            JsonSettingsValueEncoders.AesPassword("test-only-password"),
            JsonSettingsValueEncoders.Base92JsonSafe);
        string encodedValue = writeCodec.Encode("composed-secret");
        string settingsPath = directory.Write(
            "data-protection-composed.json",
            $"{{ \"Password\": \"{encodedValue}\" }}");

        JsonSettingsValueCodec readCodec = JsonSettingsValueEncoders.Compose(
            JsonSettingsValueEncoders.Rot13,
            JsonSettingsValueEncoders.Caesar(13),
            JsonSettingsValueEncoders.DataProtection(
                keyDirectoryPath,
                "Eigenverft.Tests.Composed",
                "JsonSettingsTests.ComposedDataProtection"),
            JsonSettingsValueEncoders.AesPassword("test-only-password"),
            JsonSettingsValueEncoders.Base92JsonSafe);
        var configuration = new ConfigurationManager();
        ((IConfigurationBuilder)configuration).AddJsonFileWithDecodedValues(
            settingsPath,
            reloadOnChange: false,
            decodeCodec: readCodec);

        Assert.AreEqual("composed-secret", configuration["Password"]);
        StringAssert.StartsWith(encodedValue, "enc:b9j2s7:");
    }

    [TestMethod]
    public void Rot13CodecRoundTripsAsObfuscation()
    {
        using var directory = new TemporaryDirectory();
        string encodedValue = JsonSettingsValueEncoders.Rot13.Encode("Hello-Secret-123");
        string settingsPath = directory.Write(
            "settings.json",
            "{ \"Password\": \"" + encodedValue + "\" }");
        var configuration = new ConfigurationManager();

        ((IConfigurationBuilder)configuration).AddJsonFileWithDecodedValues(
            settingsPath,
            reloadOnChange: false);

        Assert.AreEqual("Hello-Secret-123", configuration["Password"]);
        StringAssert.StartsWith(encodedValue, "enc:r1t3o7:");
        Assert.AreNotEqual("Hello-Secret-123", encodedValue);
    }

    [TestMethod]
    public void DefaultShortcutDelegatesToDocumentedPlatformNeutralComposition()
    {
        if (!PhysicalMachineBinding.TryGetFingerprint(out _))
        {
            return;
        }

        using var directory = new TemporaryDirectory();
        string keyRingPath = Path.Combine(directory.Path, "AppState");
        byte[] passwordBytes =
        {
            0x74, 0x65, 0x73, 0x74, 0x2D, 0x6F, 0x6E, 0x6C, 0x79, 0x2D, 0x70, 0x61, 0x73, 0x73, 0x77, 0x6F, 0x72, 0x64,
        };

        JsonSettingsValueCodec shortcut = JsonSettingsValueEncoders.Default("test-only-password", keyRingPath);
        JsonSettingsValueCodec byteShortcut = JsonSettingsValueEncoders.Default(passwordBytes, keyRingPath);
        JsonSettingsValueCodec composed = JsonSettingsValueEncoders.Compose(
            JsonSettingsValueEncoders.Rot13,
            JsonSettingsValueEncoders.Caesar(13),
            JsonSettingsValueEncoders.DataProtection(keyRingPath),
            JsonSettingsValueEncoders.PhysicalMachineBoundAes(),
            JsonSettingsValueEncoders.AesPassword("test-only-password"),
            JsonSettingsValueEncoders.Base92JsonSafe);

        Assert.AreEqual(composed.Name, shortcut.Name);
        Assert.AreEqual(shortcut.Name, byteShortcut.Name);

        string encodedByShortcut = byteShortcut.Encode("shortcut-secret");
        Assert.IsTrue(composed.TryDecode(encodedByShortcut, out string shortcutClearText));
        Assert.AreEqual("shortcut-secret", shortcutClearText);

        string encodedByComposition = composed.Encode("composition-secret");
        Assert.IsTrue(shortcut.TryDecode(encodedByComposition, out string compositionClearText));
        Assert.AreEqual("composition-secret", compositionClearText);
        StringAssert.StartsWith(encodedByShortcut, "enc:b9j2s7:");
    }

    [TestMethod]
    public void DefaultWindowsAddsOuterDpapiMachineLayer()
    {
        if (!OperatingSystem.IsWindows() || !PhysicalMachineBinding.TryGetFingerprint(out _))
        {
            return;
        }

        using var directory = new TemporaryDirectory();
        string keyRingPath = Path.Combine(directory.Path, "AppState");
        JsonSettingsValueCodec codec = JsonSettingsValueEncoders.DefaultWindows("test-only-password", keyRingPath);

        string encodedValue = codec.Encode("secret");

        StringAssert.StartsWith(encodedValue, "enc:k4v8s2:");
        Assert.IsTrue(codec.TryDecode(encodedValue, out string clearText));
        Assert.AreEqual("secret", clearText);
    }

    [TestMethod]
    public void DefaultRejectsEmptyPassword()
    {
        _ = Assert.ThrowsExactly<ArgumentException>(() =>
            JsonSettingsValueEncoders.Default(string.Empty, "unused-key-ring"));
        _ = Assert.ThrowsExactly<ArgumentException>(() =>
            JsonSettingsValueEncoders.Default(Array.Empty<byte>(), "unused-key-ring"));
    }

    [TestMethod]
    public void DpapiWithRot13ShortcutDelegatesToCompositionOnWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        JsonSettingsValueCodec shortcut = JsonSettingsValueEncoders.DpapiWithRot13();
        JsonSettingsValueCodec composed = JsonSettingsValueEncoders.Compose(
            JsonSettingsValueEncoders.DpapiMachineBase64Url,
            JsonSettingsValueEncoders.Rot13);

        Assert.AreEqual(composed.Name, shortcut.Name);

        using var directory = new TemporaryDirectory();
        string encodedValue = shortcut.Encode("secret");
        string settingsPath = directory.Write(
            "settings.json",
            "{ \"Password\": \"" + encodedValue + "\" }");
        var configuration = new ConfigurationManager();

        ((IConfigurationBuilder)configuration).AddJsonFileWithDecodedValues(
            settingsPath,
            reloadOnChange: false,
            decodeCodec: shortcut);

        StringAssert.StartsWith(encodedValue, "enc:r1t3o7:");
        Assert.AreEqual("secret", configuration["Password"]);
    }

    [TestMethod]
    public void CaesarCodecPersistsNormalizedShiftAndGenericDecoderReversesIt()
    {
        using var directory = new TemporaryDirectory();
        JsonSettingsValueCodec codec = JsonSettingsValueEncoders.Caesar(55);
        string encodedValue = codec.Encode("Hello-Secret-123");
        string settingsPath = directory.Write(
            "settings.json",
            "{ \"Password\": \"" + encodedValue + "\" }");
        var configuration = new ConfigurationManager();

        ((IConfigurationBuilder)configuration).AddJsonFileWithDecodedValues(
            settingsPath,
            reloadOnChange: false);

        Assert.AreEqual("Caesar(3)", codec.Name);
        StringAssert.StartsWith(encodedValue, "enc:c4e5s2:3:");
        Assert.AreEqual("Hello-Secret-123", configuration["Password"]);
    }

    [TestMethod]
    public void Base92JsonSafeSettingsCodecUsesStandaloneEncoder()
    {
        using var directory = new TemporaryDirectory();
        string expectedPayload = Base92JsonSafeEncoder.Encode(Encoding.UTF8.GetBytes("Hello-Secret-123"));
        string encodedValue = JsonSettingsValueEncoders.Base92JsonSafe.Encode("Hello-Secret-123");
        string settingsPath = directory.Write(
            "settings.json",
            "{ \"Password\": \"" + encodedValue + "\" }");
        var configuration = new ConfigurationManager();

        ((IConfigurationBuilder)configuration).AddJsonFileWithDecodedValues(
            settingsPath,
            reloadOnChange: false);

        Assert.AreEqual("enc:b9j2s7:" + expectedPayload, encodedValue);
        Assert.AreEqual("Hello-Secret-123", configuration["Password"]);
    }

    [TestMethod]
    public void AesBase92AndCaesarComposeWithoutNewPipelineLogic()
    {
        using var directory = new TemporaryDirectory();
        JsonSettingsValueCodec codec = JsonSettingsValueEncoders.Compose(
            JsonSettingsValueEncoders.AesPassword("test-only-password"),
            JsonSettingsValueEncoders.Base92JsonSafe,
            JsonSettingsValueEncoders.Caesar(10));
        string encodedValue = codec.Encode("secret");
        string settingsPath = directory.Write(
            "settings.json",
            "{ \"Password\": \"" + encodedValue + "\" }");
        var configuration = new ConfigurationManager();

        ((IConfigurationBuilder)configuration).AddJsonFileWithDecodedValues(
            settingsPath,
            reloadOnChange: false,
            decodeCodec: codec);

        StringAssert.StartsWith(encodedValue, "enc:c4e5s2:10:");
        Assert.AreEqual("secret", configuration["Password"]);
    }

    [TestMethod]
    public void DpapiWithCaesarShortcutDelegatesToCompositionOnWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        JsonSettingsValueCodec shortcut = JsonSettingsValueEncoders.DpapiWithCaesar(10);
        JsonSettingsValueCodec composed = JsonSettingsValueEncoders.Compose(
            JsonSettingsValueEncoders.DpapiMachineBase64Url,
            JsonSettingsValueEncoders.Caesar(10));

        Assert.AreEqual(composed.Name, shortcut.Name);

        using var directory = new TemporaryDirectory();
        string encodedValue = shortcut.Encode("secret");
        string settingsPath = directory.Write(
            "settings.json",
            "{ \"Password\": \"" + encodedValue + "\" }");
        var configuration = new ConfigurationManager();

        ((IConfigurationBuilder)configuration).AddJsonFileWithDecodedValues(
            settingsPath,
            reloadOnChange: false,
            decodeCodec: shortcut);

        StringAssert.StartsWith(encodedValue, "enc:c4e5s2:10:");
        Assert.AreEqual("secret", configuration["Password"]);
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

    private sealed class RetainingPreparation : IJsonConfigurationSourcePreparation
    {
        public IDictionary<string, string?>? RetainedValues { get; private set; }

        public void Prepare(JsonConfigurationSourcePreparationContext context)
        {
            RetainedValues = context.Values;
        }
    }

    private static string CreateFactorySignature(MethodInfo method)
    {
        return $"{method.Name}({string.Join(",", method.GetParameters().Select(parameter => parameter.ParameterType.FullName))})";
    }

    private static void AssertCandidateDecodes(
        TemporaryDirectory directory,
        string fileName,
        JsonSettingsValueCodec codec,
        JsonConfigurationCandidatePreparation preparation)
    {
        string encoded = codec.Encode("candidate-secret");
        string settingsPath = directory.Write(
            fileName,
            $$"""{ "Value": "{{encoded}}" }""");
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddPreparedJsonFile(settingsPath, preparation)
            .Build();

        Assert.AreEqual("candidate-secret", configuration["Value"]);
    }

    private sealed class CopyPreparedValuePreparation : IJsonConfigurationSourcePreparation
    {
        public void Prepare(JsonConfigurationSourcePreparationContext context)
        {
            context.Values["ObservedAfterDecode"] = context.Values["Value"];
        }
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
