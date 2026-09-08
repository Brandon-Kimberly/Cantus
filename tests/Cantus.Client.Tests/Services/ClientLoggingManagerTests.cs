using System;
using Cantus.Client.Services;
using Cantus.Core.Logging;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Cantus.Client.Tests.Services;

public sealed class ClientLoggingManagerTests : IDisposable
{
    public ClientLoggingManagerTests()
    {
        ClientLoggingManager.ResetForTesting();
    }

    public void Dispose()
    {
        ClientLoggingManager.ResetForTesting();
    }

    [Theory]
    [InlineData("none", LoggingConfiguration.None)]
    [InlineData("None", LoggingConfiguration.None)]
    [InlineData("debug", LoggingConfiguration.Debug)]
    [InlineData("Debug", LoggingConfiguration.Debug)]
    [InlineData("DEBUG", LoggingConfiguration.Debug)]
    [InlineData("trace", LoggingConfiguration.Trace)]
    [InlineData("Trace", LoggingConfiguration.Trace)]
    [InlineData("TRACE", LoggingConfiguration.Trace)]
    [InlineData("  debug  ", LoggingConfiguration.Debug)]
    public void ParseConfiguration_ValidStrings_ReturnsExpectedConfiguration(
        string input,
        LoggingConfiguration expected)
    {
        LoggingConfiguration result = ClientLoggingManager.ParseConfiguration(input);

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("invalid")]
    [InlineData("verbose")]
    public void ParseConfiguration_NullOrInvalid_ReturnsDefaultConfiguration(string? input)
    {
        LoggingConfiguration result = ClientLoggingManager.ParseConfiguration(input);

        result.Should().Be(ClientLoggingManager.DefaultConfiguration);
    }

    [Fact]
    public void ParseConfiguration_WithExplicitDefault_ReturnsSpecifiedDefaultOnInvalid()
    {
        LoggingConfiguration result = ClientLoggingManager.ParseConfiguration(
            "unknown",
            LoggingConfiguration.Trace);

        result.Should().Be(LoggingConfiguration.Trace);
    }

    [Fact]
    public void InitializeLogging_SetsCurrentConfigurationAndCachesFactory()
    {
        ILoggerFactory factory = ClientLoggingManager.InitializeLogging(LoggingConfiguration.Debug);

        factory.Should().NotBeNull();
        ClientLoggingManager.IsInitialized.Should().BeTrue();
        ClientLoggingManager.CurrentConfiguration.Should().Be(LoggingConfiguration.Debug);
        ClientLoggingManager.LoggerFactory.Should().BeSameAs(factory);
    }

    [Fact]
    public void CreateLogger_WithCategoryName_ReturnsValidLogger()
    {
        ClientLoggingManager.InitializeLogging(LoggingConfiguration.Debug);

        ILogger logger = ClientLoggingManager.CreateLogger("TestCategory");

        logger.Should().NotBeNull();
    }

    [Fact]
    public void CreateLogger_Generic_ReturnsValidTypedLogger()
    {
        ClientLoggingManager.InitializeLogging(LoggingConfiguration.Debug);

        ILogger<ClientLoggingManagerTests> logger = ClientLoggingManager.CreateLogger<ClientLoggingManagerTests>();

        logger.Should().NotBeNull();
    }

    [Fact]
    public void GetLogger_ReturnsValidLoggerEquivalentToCreateLogger()
    {
        ILogger loggerByName = ClientLoggingManager.GetLogger("CustomCategory");
        ILogger<ClientLoggingManagerTests> typedLogger = ClientLoggingManager.GetLogger<ClientLoggingManagerTests>();

        loggerByName.Should().NotBeNull();
        typedLogger.Should().NotBeNull();
    }

    [Fact]
    public void CreateLogger_WhenNotInitialized_LazilyInitializesFactory()
    {
        ClientLoggingManager.IsInitialized.Should().BeFalse();

        ILogger logger = ClientLoggingManager.CreateLogger("LazyCategory");

        logger.Should().NotBeNull();
        ClientLoggingManager.IsInitialized.Should().BeTrue();
        ClientLoggingManager.LoggerFactory.Should().NotBeNull();
    }
}
