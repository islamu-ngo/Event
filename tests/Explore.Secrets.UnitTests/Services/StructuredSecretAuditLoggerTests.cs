using Explore.Secrets.Abstractions;
using Explore.Secrets.Services;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TUnit.Core;

namespace Explore.Secrets.UnitTests.Services;

public class StructuredSecretAuditLoggerTests
{
    private readonly ILogger<StructuredSecretAuditLogger> _logger;
    private readonly StructuredSecretAuditLogger _auditLogger;

    public StructuredSecretAuditLoggerTests()
    {
        _logger = Substitute.For<ILogger<StructuredSecretAuditLogger>>();
        _auditLogger = new StructuredSecretAuditLogger(_logger);
    }

    [Test]
    public async Task Log_WithSuccessfulEntry_ShouldLogAtInformationLevel()
    {
        // Arrange
        var entry = new SecretAuditEntry(
            Operation: SecretOperation.Access,
            ProviderType: SecretProviderType.Infisical,
            KeyPattern: "Database:Host",
            Timestamp: SecretsTestValues.UtcNow,
            Success: true);

        // Act
        _auditLogger.Log(entry);

        // Assert - Should log at Information level for success
        _logger.Received().Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Test]
    public async Task Log_WithFailedEntry_ShouldLogAtWarningLevel()
    {
        // Arrange
        var entry = new SecretAuditEntry(
            Operation: SecretOperation.RefreshFailed,
            ProviderType: SecretProviderType.Environment,
            KeyPattern: null,
            Timestamp: SecretsTestValues.UtcNow,
            Success: false,
            ErrorMessage: "Connection timeout");

        // Act
        _auditLogger.Log(entry);

        // Assert - Should log at Warning level for failure
        _logger.Received().Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Test]
    public async Task LogAsync_ShouldCompleteSuccessfully()
    {
        // Arrange
        var entry = new SecretAuditEntry(
            Operation: SecretOperation.Initialize,
            ProviderType: SecretProviderType.Infisical,
            KeyPattern: null,
            Timestamp: SecretsTestValues.UtcNow,
            Success: true);

        // Act & Assert - Should complete without exception
        await _auditLogger.LogAsync(entry);
    }

    [Test]
    public async Task Log_ShouldIncludeEventIdFromOperation()
    {
        // Arrange
        var entry = new SecretAuditEntry(
            Operation: SecretOperation.Refresh,
            ProviderType: SecretProviderType.Infisical,
            KeyPattern: null,
            Timestamp: SecretsTestValues.UtcNow,
            Success: true);

        // Act
        _auditLogger.Log(entry);

        // Assert - EventId should match operation
        _logger.Received().Log(
            Arg.Any<LogLevel>(),
            Arg.Is<EventId>(e => e.Id == (int)SecretOperation.Refresh && e.Name == "Refresh"),
            Arg.Any<object>(),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Test]
    public async Task Log_WithNullKeyPattern_ShouldNotThrow()
    {
        // Arrange
        var entry = new SecretAuditEntry(
            Operation: SecretOperation.Initialize,
            ProviderType: SecretProviderType.Environment,
            KeyPattern: null,
            Timestamp: SecretsTestValues.UtcNow,
            Success: true);

        // Act & Assert
        var act = () => _auditLogger.Log(entry);
        await Assert.That(act).ThrowsNothing();
    }

    [Test]
    public async Task Log_WithAllFields_ShouldNotThrow()
    {
        // Arrange
        var entry = new SecretAuditEntry(
            Operation: SecretOperation.Access,
            ProviderType: SecretProviderType.Environment,
            KeyPattern: "App:***",
            Timestamp: SecretsTestValues.UtcNow,
            UserId: "user-123",
            CorrelationId: "corr-456",
            Success: true,
            ErrorMessage: null);

        // Act & Assert
        var act = () => _auditLogger.Log(entry);
        await Assert.That(act).ThrowsNothing();
    }

    [Test]
    public async Task Constructor_WithNullLogger_ShouldThrow()
    {
        // Act
        var act = () => new StructuredSecretAuditLogger(null!);

        // Assert
        await Assert.That(act).Throws<ArgumentNullException>();
    }
}
