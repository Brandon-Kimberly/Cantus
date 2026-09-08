using System;
using Cantus.Core.Logging;
using Microsoft.Extensions.Logging;

namespace Cantus.Client.Services;

public static class ClientLoggingManager
{
    private static readonly object _syncLock = new();
    private static LoggingConfiguration _currentConfiguration = LoggingConfiguration.None;
    private static bool _isInitialized;
    private static ILoggerFactory? _loggerFactory;

    public static LoggingConfiguration CurrentConfiguration => _currentConfiguration;

    public static bool IsInitialized => _isInitialized;

    public static ILoggerFactory? LoggerFactory => _loggerFactory;

#if DEBUG
    public const LoggingConfiguration DEFAULT_CONFIGURATION = LoggingConfiguration.Debug;
#else
    public const LoggingConfiguration DEFAULT_CONFIGURATION = LoggingConfiguration.None;
#endif

    public static LoggingConfiguration DefaultConfiguration => DEFAULT_CONFIGURATION;

    public static LoggingConfiguration ParseConfiguration(string? value)
    {
        return ParseConfiguration(value, DefaultConfiguration);
    }

    public static LoggingConfiguration ParseConfiguration(string? value, LoggingConfiguration defaultConfiguration)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultConfiguration;
        }

        if (Enum.TryParse(value.Trim(), ignoreCase: true, out LoggingConfiguration parsed))
        {
            return parsed;
        }

        return defaultConfiguration;
    }

    public static ILoggerFactory CreateLoggerFactory(LoggingConfiguration configuration = DEFAULT_CONFIGURATION)
    {
        lock (_syncLock)
        {
            _currentConfiguration = configuration;

            ILoggerFactory factory = Microsoft.Extensions.Logging.LoggerFactory.Create(builder =>
            {
#if __WASM__
                builder.AddProvider(new global::Uno.Extensions.Logging.WebAssembly.WebAssemblyConsoleLoggerProvider());
#else
                builder.AddConsole();
#endif
                LogLevel minLevel = configuration switch
                {
                    LoggingConfiguration.None => LogLevel.Information,
                    LoggingConfiguration.Debug => LogLevel.Debug,
                    LoggingConfiguration.Trace => LogLevel.Trace,
                    _ => LogLevel.Information
                };

                builder.SetMinimumLevel(minLevel);
                builder.AddFilter("Uno", LogLevel.Warning);
                builder.AddFilter("Windows", LogLevel.Warning);
                builder.AddFilter("Microsoft", LogLevel.Warning);
            });

            _loggerFactory = factory;
            _isInitialized = true;
            return factory;
        }
    }

    public static ILoggerFactory InitializeLogging(LoggingConfiguration configuration = DEFAULT_CONFIGURATION)
    {
        return CreateLoggerFactory(configuration);
    }

    public static ILogger CreateLogger(string categoryName)
    {
        lock (_syncLock)
        {
            _loggerFactory ??= CreateLoggerFactory();
            return _loggerFactory.CreateLogger(categoryName);
        }
    }

    public static ILogger<T> CreateLogger<T>()
    {
        lock (_syncLock)
        {
            _loggerFactory ??= CreateLoggerFactory();
            return _loggerFactory.CreateLogger<T>();
        }
    }

    public static ILogger GetLogger(string categoryName) => CreateLogger(categoryName);

    public static ILogger<T> GetLogger<T>() => CreateLogger<T>();

    internal static void ResetForTesting()
    {
        lock (_syncLock)
        {
            _loggerFactory = null;
            _isInitialized = false;
            _currentConfiguration = LoggingConfiguration.None;
        }
    }
}
