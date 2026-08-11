using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Eigenverft.WebLib.Infrastructure.Hosting.Logging.BootstrapLogger
{
    /// <summary>
    /// Provides a refactor-friendly pre-host logger for early startup logging.
    /// </summary>
    /// <typeparam name="TCategoryName">The category type used to name the logger.</typeparam>
    /// <remarks>
    /// Intended for logging before the host is built, when DI resolution of <see cref="ILoggerFactory"/> is not available.
    /// The resulting logger is deliberately a stable, process-wide bootstrap channel. It is not rebound when the
    /// application later replaces Serilog's global logger or configures the host logging pipeline. This separation lets
    /// startup diagnostics inspect configuration and logging setup without depending on the setup being diagnosed.
    ///
    /// Resolution strategy on the first call:
    /// - If Serilog and the Serilog-to-MEL bridge are available and the current global logger is not Serilog's built-in
    ///   default silent logger, that logger is captured by a Serilog-backed <see cref="ILoggerFactory"/>.
    /// - Serilog's built-in default silent logger is treated as "not explicitly initialized" and uses the Microsoft fallback.
    ///   Sink configuration is otherwise not inferred; every explicitly assigned Serilog logger is accepted as intentional.
    /// - Otherwise, a minimal Microsoft logging factory is created with Console support when available, optionally
    ///   applying the "Logging" configuration section when possible.
    ///
    /// The first successful factory creation wins for the process. Later configuration arguments and later application
    /// logging changes intentionally do not alter existing bootstrap loggers.
    /// </remarks>
    public static class BootstrapLogger<TCategoryName>
    {
        /// <summary>
        /// Creates a pre-host <see cref="ILogger{TCategoryName}"/> using <typeparamref name="TCategoryName"/> as category.
        /// </summary>
        /// <param name="configuration">
        /// Optional configuration used only when the process-wide factory is first created. It may apply the
        /// "Logging" section to the Microsoft fallback factory when the required logging configuration package is available.
        /// </param>
        /// <returns>A logger instance that can be used prior to building the host.</returns>
        /// <example>
        /// <code>
        /// ILogger startupLogger = BootstrapLogger&lt;Program&gt;.CreateLogger(builder.Configuration);
        /// </code>
        /// </example>
        public static ILogger<TCategoryName> CreateLogger(IConfiguration? configuration = null)
        {
            var factory = BootstrapLoggerFactoryCache.GetOrCreate(configuration);
            return factory.CreateLogger<TCategoryName>();
        }

        /// <summary>
        /// Creates a required Serilog-backed pre-host logger from one explicit JSON configuration file.
        /// </summary>
        /// <param name="configurationFile">
        /// The JSON file containing the isolated bootstrap Serilog configuration. Relative paths are resolved beneath
        /// <paramref name="baseDirectory"/>. The default is <c>AppSettings/BootstrapLoggerSettings.json</c>.
        /// </param>
        /// <param name="baseDirectory">
        /// The base directory used to resolve a relative <paramref name="configurationFile"/>. A null value uses
        /// <see cref="AppContext.BaseDirectory"/>, making the parameterless call suitable for static field initialization.
        /// </param>
        /// <param name="sectionName">
        /// The configuration section consumed by Serilog.Settings.Configuration. The default is <c>Serilog</c>.
        /// </param>
        /// <param name="reloadOnChange">
        /// Enables the JSON configuration provider's change notifications. The default is false so the bootstrap channel
        /// remains completely stable. When enabled, Serilog can update existing minimum-level overrides and level switches;
        /// it does not rebuild or replace the configured sink pipeline.
        /// </param>
        /// <returns>A stable <see cref="ILogger{TCategoryName}"/> backed by the Serilog instance created from the file.</returns>
        /// <remarks>
        /// This strict API must not be confused with <see cref="CreateLogger(IConfiguration?)"/>. The automatic method uses
        /// an already initialized Serilog logger when one exists and otherwise falls back to Microsoft logging. This method
        /// instead explicitly creates Serilog from the named file and never falls back.
        ///
        /// The bootstrap configuration is intentionally isolated. It does not load ASP.NET Core defaults, environment-specific
        /// companion files, environment variables, command-line arguments, DPAPI decoding, or the later application logging
        /// configuration. This separation allows the static bootstrap logger to report failures in those later configuration
        /// stages without depending on them.
        ///
        /// Serilog core, Serilog.Settings.Configuration, Serilog.Extensions.Logging, and every sink or enricher named by the
        /// JSON file must be supplied by the consuming application. The WebLib has no compile-time Serilog reference and uses
        /// reflection exclusively. Missing components, invalid JSON, and incompatible Serilog APIs produce descriptive startup
        /// exceptions. This method must be the first BootstrapLogger initialization in the process; later replacement of the
        /// global Serilog logger does not rebind the created bootstrap channel.
        ///
        /// When called from a static field initializer, failures may occur before the <c>Main</c> method body is entered and can
        /// therefore surface as a type-initialization exception. This fail-fast behavior is intentional for a required provider.
        /// </remarks>
        public static ILogger<TCategoryName> CreateRequiredSerilogLogger(
            string configurationFile = BootstrapLoggerDefaults.RequiredSerilogConfigurationFile,
            string? baseDirectory = null,
            string sectionName = BootstrapLoggerDefaults.SerilogSectionName,
            bool reloadOnChange = false)
        {
            var factory = BootstrapLoggerFactoryCache.GetOrCreateRequiredSerilog(
                configurationFile,
                baseDirectory,
                sectionName,
                reloadOnChange);
            return factory.CreateLogger<TCategoryName>();
        }

        /// <summary>
        /// Creates (or returns the cached) <see cref="ILoggerFactory"/> suitable for pre-host usage.
        /// </summary>
        /// <param name="configuration">
        /// Optional configuration used to apply the "Logging" section (levels and filters) if available.
        /// </param>
        /// <returns>
        /// The process-owned cached <see cref="ILoggerFactory"/>. Callers may create loggers from it but must not dispose it.
        /// </returns>
        public static ILoggerFactory CreateLoggerFactory(IConfiguration? configuration = null)
        {
            return BootstrapLoggerFactoryCache.GetOrCreate(configuration);
        }

        /// <summary>
        /// Clears the cached factory. Intended for tests.
        /// </summary>
        internal static void ResetForTests()
        {
            BootstrapLoggerFactoryCache.ResetForTests();
        }
    }

    /// <summary>
    /// Provides non-generic convenience APIs for creating a pre-host logger using an explicit category name.
    /// </summary>
    /// <remarks>
    /// This is a convenience facade over the same cached factory used by <see cref="BootstrapLogger{TCategoryName}"/>.
    /// </remarks>
    public static class BootstrapLogger
    {
        /// <summary>
        /// Creates a pre-host <see cref="ILogger"/> for the given category name.
        /// </summary>
        /// <param name="categoryName">The logger category name.</param>
        /// <param name="configuration">
        /// Optional configuration used only when the process-wide factory is first created. It may apply the
        /// "Logging" section to the Microsoft fallback factory when the required logging configuration package is available.
        /// </param>
        /// <returns>A logger instance that can be used prior to building the host.</returns>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="categoryName"/> is null.</exception>
        /// <example>
        /// <code>
        /// ILogger startupLogger = BootstrapLogger.CreateLogger("Startup", builder.Configuration);
        /// </code>
        /// </example>
        public static ILogger CreateLogger(string categoryName, IConfiguration? configuration = null)
        {
            if (categoryName is null)
            {
                throw new ArgumentNullException(nameof(categoryName));
            }

            var factory = BootstrapLoggerFactoryCache.GetOrCreate(configuration);
            return factory.CreateLogger(categoryName);
        }

        /// <summary>
        /// Creates a required Serilog-backed pre-host logger for an explicit category from one JSON file.
        /// </summary>
        /// <param name="categoryName">The logger category name.</param>
        /// <param name="configurationFile">The isolated bootstrap Serilog JSON file.</param>
        /// <param name="baseDirectory">The optional base directory; null uses <see cref="AppContext.BaseDirectory"/>.</param>
        /// <param name="sectionName">The Serilog configuration section name.</param>
        /// <param name="reloadOnChange">
        /// Whether existing Serilog level overrides and switches may follow JSON changes. Sink topology is not rebuilt.
        /// </param>
        /// <returns>A stable Serilog-backed <see cref="ILogger"/>.</returns>
        /// <remarks>
        /// This is the non-generic facade for
        /// <see cref="BootstrapLogger{TCategoryName}.CreateRequiredSerilogLogger(string, string?, string, bool)"/> and has the
        /// same strict first-call, no-fallback, isolated-configuration, reflection-only, and static-initialization semantics.
        /// </remarks>
        public static ILogger CreateRequiredSerilogLogger(
            string categoryName,
            string configurationFile = BootstrapLoggerDefaults.RequiredSerilogConfigurationFile,
            string? baseDirectory = null,
            string sectionName = BootstrapLoggerDefaults.SerilogSectionName,
            bool reloadOnChange = false)
        {
            if (categoryName is null)
            {
                throw new ArgumentNullException(nameof(categoryName));
            }

            var factory = BootstrapLoggerFactoryCache.GetOrCreateRequiredSerilog(
                configurationFile,
                baseDirectory,
                sectionName,
                reloadOnChange);
            return factory.CreateLogger(categoryName);
        }

        /// <summary>
        /// Creates (or returns the cached) <see cref="ILoggerFactory"/> suitable for pre-host usage.
        /// </summary>
        /// <param name="configuration">
        /// Optional configuration used to apply the "Logging" section (levels and filters) if available.
        /// </param>
        /// <returns>
        /// The process-owned cached <see cref="ILoggerFactory"/>. Callers may create loggers from it but must not dispose it.
        /// </returns>
        public static ILoggerFactory CreateLoggerFactory(IConfiguration? configuration = null)
        {
            return BootstrapLoggerFactoryCache.GetOrCreate(configuration);
        }

        /// <summary>
        /// Clears the cached factory. Intended for tests.
        /// </summary>
        internal static void ResetForTests()
        {
            BootstrapLoggerFactoryCache.ResetForTests();
        }
    }

    internal static class BootstrapLoggerDefaults
    {
        internal const string RequiredSerilogConfigurationFile = "AppSettings/BootstrapLoggerSettings.json";
        internal const string SerilogSectionName = "Serilog";
    }

    /// <summary>
    /// Centralized cache and creation logic for the pre-host <see cref="ILoggerFactory"/>.
    /// </summary>
    /// <remarks>
    /// Kept internal to ensure there is exactly one stable bootstrap factory per process, regardless of how many generic
    /// categories are used. Capturing the first available backend is intentional: bootstrap logging remains independent
    /// from later application logging reconfiguration.
    /// </remarks>
    internal static class BootstrapLoggerFactoryCache
    {
        private static readonly object Gate = new();
        private static ILoggerFactory? _cachedFactory;
        private static BootstrapLoggerBackend _cachedBackend;
        private static RequiredSerilogConfigurationIdentity? _requiredSerilogIdentity;
        private static IConfigurationRoot? _retainedRequiredSerilogConfiguration;
        private static IDisposable? _ownedRequiredSerilogLogger;

        /// <summary>
        /// Returns a cached factory or creates one if absent.
        /// </summary>
        /// <param name="configuration">
        /// Optional configuration used only when creating the factory for the first time.
        /// Subsequent calls intentionally ignore this parameter because the bootstrap channel is process-stable and
        /// independent from later application logging configuration.
        /// </param>
        /// <returns>An <see cref="ILoggerFactory"/>.</returns>
        public static ILoggerFactory GetOrCreate(IConfiguration? configuration)
        {
            lock (Gate)
            {
                if (_cachedFactory is not null)
                {
                    return _cachedFactory;
                }

                ILoggerFactory? serilogFactory = TryCreateSerilogBackedFactory();
                if (serilogFactory is not null)
                {
                    _cachedFactory = serilogFactory;
                    _cachedBackend = BootstrapLoggerBackend.AutomaticSerilog;
                }
                else
                {
                    _cachedFactory = CreateMicrosoftFallbackFactory(configuration);
                    _cachedBackend = BootstrapLoggerBackend.Microsoft;
                }

                return _cachedFactory;
            }
        }

        /// <summary>
        /// Creates or returns the process-wide required Serilog bootstrap factory.
        /// </summary>
        internal static ILoggerFactory GetOrCreateRequiredSerilog(
            string configurationFile,
            string? baseDirectory,
            string sectionName,
            bool reloadOnChange)
        {
            if (string.IsNullOrWhiteSpace(configurationFile))
            {
                throw new ArgumentException(
                    "A non-empty required Serilog bootstrap configuration file is required.",
                    nameof(configurationFile));
            }

            if (string.IsNullOrWhiteSpace(sectionName))
            {
                throw new ArgumentException(
                    "A non-empty Serilog configuration section name is required.",
                    nameof(sectionName));
            }

            string resolvedBaseDirectory = Path.GetFullPath(baseDirectory ?? AppContext.BaseDirectory);
            string resolvedConfigurationFile = Path.GetFullPath(
                Path.IsPathRooted(configurationFile)
                    ? configurationFile
                    : Path.Combine(resolvedBaseDirectory, configurationFile));
            var requestedIdentity = new RequiredSerilogConfigurationIdentity(
                resolvedConfigurationFile,
                sectionName,
                reloadOnChange);

            lock (Gate)
            {
                if (_cachedFactory is not null)
                {
                    if (_cachedBackend == BootstrapLoggerBackend.RequiredSerilog &&
                        _requiredSerilogIdentity is not null &&
                        _requiredSerilogIdentity.Equals(requestedIdentity))
                    {
                        return _cachedFactory;
                    }

                    throw new InvalidOperationException(
                        $"Required Serilog bootstrap logging must be the first BootstrapLogger initialization. " +
                        $"The process-wide bootstrap backend is already '{_cachedBackend}'.");
                }

                IConfigurationRoot? configuration = null;
                IDisposable? createdSerilogLogger = null;

                try
                {
                    configuration = BuildRequiredSerilogConfiguration(
                        resolvedConfigurationFile,
                        sectionName,
                        reloadOnChange);
                    object serilogLogger = CreateRequiredSerilogLoggerFromConfiguration(
                        configuration,
                        sectionName);
                    createdSerilogLogger = serilogLogger as IDisposable;

                    ILoggerFactory factory =
                        TryCreateSerilogBackedFactory(serilogLogger)
                        ?? throw new InvalidOperationException(
                            "Serilog bootstrap logging was explicitly required, but " +
                            "Serilog.Extensions.Logging could not create a compatible ILoggerFactory.");

                    SetGlobalSerilogLogger(serilogLogger);

                    _cachedFactory = factory;
                    _cachedBackend = BootstrapLoggerBackend.RequiredSerilog;
                    _requiredSerilogIdentity = requestedIdentity;
                    _retainedRequiredSerilogConfiguration = configuration;
                    _ownedRequiredSerilogLogger = createdSerilogLogger;

                    configuration = null;
                    createdSerilogLogger = null;
                    return factory;
                }
                catch
                {
                    createdSerilogLogger?.Dispose();
                    (configuration as IDisposable)?.Dispose();
                    throw;
                }
            }
        }

        /// <summary>
        /// Clears the cached factory. Intended for tests.
        /// </summary>
        internal static void ResetForTests()
        {
            lock (Gate)
            {
                _cachedFactory = null;
                _cachedBackend = BootstrapLoggerBackend.None;
                _requiredSerilogIdentity = null;

                _ownedRequiredSerilogLogger?.Dispose();
                _ownedRequiredSerilogLogger = null;

                (_retainedRequiredSerilogConfiguration as IDisposable)?.Dispose();
                _retainedRequiredSerilogConfiguration = null;
            }
        }

        private static IConfigurationRoot BuildRequiredSerilogConfiguration(
            string resolvedConfigurationFile,
            string sectionName,
            bool reloadOnChange)
        {
            string? configurationDirectory = Path.GetDirectoryName(resolvedConfigurationFile);
            string configurationFileName = Path.GetFileName(resolvedConfigurationFile);

            if (string.IsNullOrEmpty(configurationDirectory) || string.IsNullOrEmpty(configurationFileName))
            {
                throw new ArgumentException(
                    $"The required Serilog bootstrap configuration path '{resolvedConfigurationFile}' is invalid.",
                    nameof(resolvedConfigurationFile));
            }

            IConfigurationRoot configuration;
            try
            {
                configuration = new ConfigurationBuilder()
                    .SetBasePath(configurationDirectory)
                    .AddJsonFile(
                        configurationFileName,
                        optional: false,
                        reloadOnChange: reloadOnChange)
                    .Build();
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    $"The required Serilog bootstrap configuration file '{resolvedConfigurationFile}' " +
                    "could not be loaded.",
                    exception);
            }

            IConfigurationSection section = configuration.GetSection(sectionName);
            if (section.Value is null && !section.GetChildren().Any())
            {
                (configuration as IDisposable)?.Dispose();
                throw new InvalidOperationException(
                    $"The required Serilog bootstrap configuration file '{resolvedConfigurationFile}' " +
                    $"does not contain the section '{sectionName}'.");
            }

            return configuration;
        }

        private static object CreateRequiredSerilogLoggerFromConfiguration(
            IConfiguration configuration,
            string sectionName)
        {
            Type loggerConfigurationType = RequireType(
                "Serilog",
                "Serilog.LoggerConfiguration",
                "Serilog core");
            Type configurationExtensionsType = RequireType(
                "Serilog.Settings.Configuration",
                "Serilog.ConfigurationLoggerConfigurationExtensions",
                "Serilog JSON configuration");
            Type readerOptionsType = RequireType(
                "Serilog.Settings.Configuration",
                "Serilog.Settings.Configuration.ConfigurationReaderOptions",
                "Serilog JSON configuration");

            object loggerConfiguration = Activator.CreateInstance(loggerConfigurationType)
                ?? throw new InvalidOperationException(
                    "Serilog.LoggerConfiguration could not be instantiated.");
            object readFrom = loggerConfigurationType
                .GetProperty("ReadFrom", BindingFlags.Public | BindingFlags.Instance)?
                .GetValue(loggerConfiguration)
                ?? throw new InvalidOperationException(
                    "Serilog.LoggerConfiguration.ReadFrom is unavailable.");
            object readerOptions = Activator.CreateInstance(readerOptionsType)
                ?? throw new InvalidOperationException(
                    "Serilog ConfigurationReaderOptions could not be instantiated.");

            PropertyInfo? sectionNameProperty = readerOptionsType.GetProperty(
                "SectionName",
                BindingFlags.Public | BindingFlags.Instance);
            if (sectionNameProperty?.CanWrite != true)
            {
                throw new InvalidOperationException(
                    "Serilog ConfigurationReaderOptions.SectionName is unavailable.");
            }

            sectionNameProperty.SetValue(readerOptions, sectionName);

            MethodInfo? configurationMethod = configurationExtensionsType
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(static method => string.Equals(
                    method.Name,
                    "Configuration",
                    StringComparison.Ordinal))
                .FirstOrDefault(method =>
                {
                    ParameterInfo[] parameters = method.GetParameters();
                    return parameters.Length == 3 &&
                        parameters[0].ParameterType.IsInstanceOfType(readFrom) &&
                        parameters[1].ParameterType.IsInstanceOfType(configuration) &&
                        parameters[2].ParameterType.IsInstanceOfType(readerOptions);
                });

            if (configurationMethod is null)
            {
                throw new InvalidOperationException(
                    "Serilog.Settings.Configuration does not expose a compatible " +
                    "Configuration(IConfiguration, ConfigurationReaderOptions) API.");
            }

            InvokeAndUnwrap(
                configurationMethod,
                target: null,
                new[] { readFrom, configuration, readerOptions });

            MethodInfo? createLoggerMethod = loggerConfigurationType.GetMethod(
                "CreateLogger",
                BindingFlags.Public | BindingFlags.Instance,
                binder: null,
                types: Type.EmptyTypes,
                modifiers: null);
            if (createLoggerMethod is null)
            {
                throw new InvalidOperationException(
                    "Serilog.LoggerConfiguration.CreateLogger() is unavailable.");
            }

            return InvokeAndUnwrap(
                createLoggerMethod,
                loggerConfiguration,
                Array.Empty<object?>())
                ?? throw new InvalidOperationException(
                    "Serilog.LoggerConfiguration.CreateLogger() returned null.");
        }

        private static void SetGlobalSerilogLogger(object serilogLogger)
        {
            Type logType = RequireType("Serilog", "Serilog.Log", "Serilog core");
            PropertyInfo? loggerProperty = logType.GetProperty(
                "Logger",
                BindingFlags.Public | BindingFlags.Static);

            if (loggerProperty?.CanWrite != true ||
                !loggerProperty.PropertyType.IsInstanceOfType(serilogLogger))
            {
                throw new InvalidOperationException(
                    "Serilog.Log.Logger is unavailable or incompatible with the configured logger.");
            }

            loggerProperty.SetValue(null, serilogLogger);
        }

        private static Type RequireType(
            string assemblyName,
            string fullTypeName,
            string capabilityName)
        {
            return TryLoadType(assemblyName, fullTypeName)
                ?? throw new InvalidOperationException(
                    $"{capabilityName} was explicitly required, but assembly '{assemblyName}' " +
                    $"or type '{fullTypeName}' is not available.");
        }

        private static object? InvokeAndUnwrap(
            MethodInfo method,
            object? target,
            object?[] arguments)
        {
            try
            {
                return method.Invoke(target, arguments);
            }
            catch (TargetInvocationException exception)
                when (exception.InnerException is not null)
            {
                ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                throw;
            }
        }

        private static ILoggerFactory CreateMicrosoftFallbackFactory(IConfiguration? configuration)
        {
            return LoggerFactory.Create(logging =>
            {
                // Optional: apply IConfiguration "Logging" section if the extension is available.
                if (configuration is not null)
                {
                    var loggingSection = configuration.GetSection("Logging");
                    TryInvokeLoggingBuilderExtension(
                        logging,
                        assemblyName: "Microsoft.Extensions.Logging.Configuration",
                        typeName: "Microsoft.Extensions.Logging.Configuration.LoggingBuilderConfigurationExtensions",
                        methodName: "AddConfiguration",
                        args: new object?[] { loggingSection });
                }

                // Optional: add Console if the package is referenced.
                TryInvokeLoggingBuilderExtension(
                    logging,
                    assemblyName: "Microsoft.Extensions.Logging.Console",
                    typeName: "Microsoft.Extensions.Logging.ConsoleLoggerExtensions",
                    methodName: "AddConsole",
                    args: Array.Empty<object?>());
            });
        }

        private static ILoggerFactory? TryCreateSerilogBackedFactory()
        {
            // Goal: Produce a stable Microsoft ILoggerFactory backed by the Serilog global logger that exists at
            // bootstrap-factory creation time, without a compile-time dependency on Serilog. Later replacement of
            // Serilog.Log.Logger intentionally does not rebind the bootstrap channel. Serilog's built-in default silent
            // logger is the one exception: it means no Serilog bootstrap channel was explicitly initialized, so the caller
            // receives the Microsoft fallback instead. Explicitly assigned Serilog loggers remain valid even without sinks.
            // This requires:
            // - Serilog assembly: Serilog.Log and Serilog.ILogger
            // - Bridge assembly: Serilog.Extensions.Logging.SerilogLoggerFactory

            var serilogLogType = TryLoadType("Serilog", "Serilog.Log");
            var serilogILoggerType = TryLoadType("Serilog", "Serilog.ILogger");
            if (serilogLogType is null || serilogILoggerType is null)
            {
                return null;
            }

            var loggerProperty = serilogLogType.GetProperty("Logger", BindingFlags.Public | BindingFlags.Static);
            if (loggerProperty is null)
            {
                return null;
            }

            var serilogLogger = loggerProperty.GetValue(null);
            if (serilogLogger is null)
            {
                return null;
            }

            if (IsSerilogDefaultSilentLogger(serilogLogger))
            {
                return null;
            }

            return TryCreateSerilogBackedFactory(serilogLogger);
        }

        private static ILoggerFactory? TryCreateSerilogBackedFactory(object serilogLogger)
        {
            Type? serilogLoggerFactoryType = TryLoadType(
                "Serilog.Extensions.Logging",
                "Serilog.Extensions.Logging.SerilogLoggerFactory");
            if (serilogLoggerFactoryType is null)
            {
                return null;
            }

            Type? providerCollectionType = TryLoadType(
                "Serilog.Extensions.Logging",
                "Serilog.Extensions.Logging.LoggerProviderCollection");
            ConstructorInfo[] constructors = serilogLoggerFactoryType.GetConstructors(
                BindingFlags.Public | BindingFlags.Instance);

            foreach (ConstructorInfo constructor in constructors.OrderByDescending(
                static candidate => candidate.GetParameters().Length))
            {
                ParameterInfo[] parameters = constructor.GetParameters();

                try
                {
                    if (parameters.Length == 3 &&
                        parameters[0].ParameterType.IsInstanceOfType(serilogLogger) &&
                        parameters[1].ParameterType == typeof(bool) &&
                        IsLoggerProviderCollectionParameter(parameters[2].ParameterType, providerCollectionType))
                    {
                        return (ILoggerFactory)constructor.Invoke(
                            new object?[] { serilogLogger, false, null });
                    }

                    if (parameters.Length == 2 &&
                        parameters[0].ParameterType.IsInstanceOfType(serilogLogger) &&
                        parameters[1].ParameterType == typeof(bool))
                    {
                        return (ILoggerFactory)constructor.Invoke(
                            new object?[] { serilogLogger, false });
                    }

                    if (parameters.Length == 1 &&
                        parameters[0].ParameterType.IsInstanceOfType(serilogLogger))
                    {
                        return (ILoggerFactory)constructor.Invoke(
                            new[] { serilogLogger });
                    }
                }
                catch
                {
                    // Continue probing constructors from other supported bridge versions.
                }
            }

            return null;
        }

        private static bool IsSerilogDefaultSilentLogger(object serilogLogger)
        {
            try
            {
                var concreteLoggerType = TryLoadType("Serilog", "Serilog.Core.Logger");
                var noneProperty = concreteLoggerType?.GetProperty(
                    "None",
                    BindingFlags.Public | BindingFlags.Static);
                var noneLogger = noneProperty?.GetValue(null);

                if (noneLogger is not null && ReferenceEquals(serilogLogger, noneLogger))
                {
                    return true;
                }
            }
            catch
            {
                // Fall through to the type-name check for Serilog versions whose public surface differs.
            }

            return string.Equals(
                serilogLogger.GetType().FullName,
                "Serilog.Core.Pipeline.SilentLogger",
                StringComparison.Ordinal);
        }

        private static bool IsLoggerProviderCollectionParameter(Type actualParameterType, Type? providerCollectionType)
        {
            if (providerCollectionType is not null)
            {
                return actualParameterType == providerCollectionType;
            }

            // Fall back to name-based check if the type cannot be loaded.
            return string.Equals(
                actualParameterType.FullName,
                "Serilog.Extensions.Logging.LoggerProviderCollection",
                StringComparison.Ordinal);
        }

        private static void TryInvokeLoggingBuilderExtension(
            ILoggingBuilder loggingBuilder,
            string assemblyName,
            string typeName,
            string methodName,
            object?[] args)
        {
            var extensionType = TryLoadType(assemblyName, typeName);
            if (extensionType is null)
            {
                return;
            }

            var candidates = extensionType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => string.Equals(m.Name, methodName, StringComparison.Ordinal))
                .ToArray();

            if (candidates.Length == 0)
            {
                return;
            }

            foreach (var method in candidates)
            {
                var parameters = method.GetParameters();
                if (parameters.Length != args.Length + 1)
                {
                    continue;
                }

                if (!parameters[0].ParameterType.IsInstanceOfType(loggingBuilder))
                {
                    continue;
                }

                try
                {
                    var invokeArgs = new object?[args.Length + 1];
                    invokeArgs[0] = loggingBuilder;
                    Array.Copy(args, 0, invokeArgs, 1, args.Length);

                    method.Invoke(null, invokeArgs);
                    return;
                }
                catch
                {
                    // Swallow and continue: missing package, incompatible version, etc.
                }
            }
        }

        private enum BootstrapLoggerBackend
        {
            None,
            Microsoft,
            AutomaticSerilog,
            RequiredSerilog,
        }

        private sealed class RequiredSerilogConfigurationIdentity
        {
            internal RequiredSerilogConfigurationIdentity(
                string configurationFile,
                string sectionName,
                bool reloadOnChange)
            {
                ConfigurationFile = configurationFile;
                SectionName = sectionName;
                ReloadOnChange = reloadOnChange;
            }

            private string ConfigurationFile { get; }
            private string SectionName { get; }
            private bool ReloadOnChange { get; }

            internal bool Equals(RequiredSerilogConfigurationIdentity other)
            {
                StringComparison pathComparison = OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal;

                return string.Equals(ConfigurationFile, other.ConfigurationFile, pathComparison) &&
                    string.Equals(SectionName, other.SectionName, StringComparison.Ordinal) &&
                    ReloadOnChange == other.ReloadOnChange;
            }
        }

        private static Type? TryLoadType(string assemblyName, string fullTypeName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var t = asm.GetType(fullTypeName, throwOnError: false, ignoreCase: false);
                    if (t is not null)
                    {
                        return t;
                    }
                }
                catch
                {
                    // Ignore and continue probing.
                }
            }

            try
            {
                var asm = Assembly.Load(new AssemblyName(assemblyName));
                return asm.GetType(fullTypeName, throwOnError: false, ignoreCase: false);
            }
            catch
            {
                return null;
            }
        }
    }
}
