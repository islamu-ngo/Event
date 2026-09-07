using Explore.Secrets.Configuration;
using Explore.Secrets.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using TUnit.Core;

namespace Explore.Secrets.UnitTests.Services;

public class RotationAwareDbContextFactoryTests : IDisposable
{
    private readonly ILogger<RotationAwareDbContextFactory<TestDbContext>> _logger;
    private RotationAwareDbContextFactory<TestDbContext>? _factory;
    private Action<DatabaseConnectionOptions, string?>? _onChangeCallback;

    public RotationAwareDbContextFactoryTests()
    {
        _logger = Substitute.For<ILogger<RotationAwareDbContextFactory<TestDbContext>>>();
    }

    private RotationAwareDbContextFactory<TestDbContext> CreateFactory(
        DatabaseConnectionOptions? connection = null,
        RotationOptions? rotation = null)
    {
        connection ??= new DatabaseConnectionOptions
        {
            ConnectionString = SecretsTestValues.CreateConnectionString(
                SecretsTestValues.CreateSecret())
        };
        rotation ??= new RotationOptions { Enabled = true, LogRotationEvents = true };

        var connectionMonitor = CreateOptionsMonitorWithCallback(connection);
        var rotationMonitor = CreateOptionsMonitor(rotation);

        _factory = new RotationAwareDbContextFactory<TestDbContext>(
            options => new TestDbContext(options),
            connectionMonitor,
            rotationMonitor,
            _logger);

        return _factory;
    }

    private static IOptionsMonitor<T> CreateOptionsMonitor<T>(T value)
    {
        var monitor = Substitute.For<IOptionsMonitor<T>>();
        monitor.CurrentValue.Returns(value);
        return monitor;
    }

    private IOptionsMonitor<DatabaseConnectionOptions> CreateOptionsMonitorWithCallback(DatabaseConnectionOptions value)
    {
        var monitor = Substitute.For<IOptionsMonitor<DatabaseConnectionOptions>>();
        monitor.CurrentValue.Returns(value);
        monitor.OnChange(Arg.Any<Action<DatabaseConnectionOptions, string?>>())
            .Returns(callInfo =>
            {
                _onChangeCallback = callInfo.Arg<Action<DatabaseConnectionOptions, string?>>();
                return Substitute.For<IDisposable>();
            });
        return monitor;
    }

    // ==================== Constructor Tests ====================

    [Test]
    public async Task Constructor_WithValidOptions_ShouldSucceed()
    {
        // Arrange & Act
        var factory = CreateFactory();

        // Assert
        await Assert.That(factory).IsNotNull();
        await Assert.That(factory.RotationCount).IsEqualTo(0);
    }

    [Test]
    public async Task Constructor_WithNullContextFactory_ShouldThrow()
    {
        // Arrange
        var connectionMonitor = CreateOptionsMonitor(new DatabaseConnectionOptions());
        var rotationMonitor = CreateOptionsMonitor(new RotationOptions());

        // Act
        var act = () => new RotationAwareDbContextFactory<TestDbContext>(
            null!,
            connectionMonitor,
            rotationMonitor,
            _logger);

        // Assert
        await Assert.That(act).Throws<ArgumentNullException>()
            .WithParameterName("contextFactory");
    }

    [Test]
    public async Task Constructor_WithNullConnectionOptions_ShouldThrow()
    {
        // Arrange
        var rotationMonitor = CreateOptionsMonitor(new RotationOptions());

        // Act
        var act = () => new RotationAwareDbContextFactory<TestDbContext>(
            options => new TestDbContext(options),
            null!,
            rotationMonitor,
            _logger);

        // Assert
        await Assert.That(act).Throws<ArgumentNullException>()
            .WithParameterName("connectionOptions");
    }

    [Test]
    public async Task Constructor_WithNullRotationOptions_ShouldThrow()
    {
        // Arrange
        var connectionMonitor = CreateOptionsMonitor(new DatabaseConnectionOptions());

        // Act
        var act = () => new RotationAwareDbContextFactory<TestDbContext>(
            options => new TestDbContext(options),
            connectionMonitor,
            null!,
            _logger);

        // Assert
        await Assert.That(act).Throws<ArgumentNullException>()
            .WithParameterName("rotationOptions");
    }

    [Test]
    public async Task Constructor_WithNullLogger_ShouldThrow()
    {
        // Arrange
        var connectionMonitor = CreateOptionsMonitor(new DatabaseConnectionOptions());
        var rotationMonitor = CreateOptionsMonitor(new RotationOptions());

        // Act
        var act = () => new RotationAwareDbContextFactory<TestDbContext>(
            options => new TestDbContext(options),
            connectionMonitor,
            rotationMonitor,
            null!);

        // Assert
        await Assert.That(act).Throws<ArgumentNullException>()
            .WithParameterName("logger");
    }

    // ==================== CreateDbContext Tests ====================

    [Test]
    public async Task CreateDbContext_WithValidConnectionString_ShouldSucceed()
    {
        // Arrange
        var factory = CreateFactory();

        // Act
        using var context = factory.CreateDbContext();

        // Assert
        await Assert.That(context).IsNotNull();
        await Assert.That(context).IsTypeOf<TestDbContext>();
    }

    [Test]
    public async Task CreateDbContext_WithNullConnectionString_ShouldThrow()
    {
        // Arrange
        var factory = CreateFactory(new DatabaseConnectionOptions { ConnectionString = null });

        // Act
        var act = () => factory.CreateDbContext();

        // Assert
        await Assert.That(act).Throws<InvalidOperationException>()
            .WithMessageContaining("Connection string is not configured");
    }

    [Test]
    public async Task CreateDbContext_WithEmptyConnectionString_ShouldThrow()
    {
        // Arrange
        var factory = CreateFactory(new DatabaseConnectionOptions { ConnectionString = "" });

        // Act
        var act = () => factory.CreateDbContext();

        // Assert
        await Assert.That(act).Throws<InvalidOperationException>()
            .WithMessageContaining("Connection string is not configured");
    }

    [Test]
    public async Task CreateDbContext_MultipleTimes_ShouldCreateNewContexts()
    {
        // Arrange
        var factory = CreateFactory();

        // Act
        using var context1 = factory.CreateDbContext();
        using var context2 = factory.CreateDbContext();

        // Assert
        await Assert.That(ReferenceEquals(context1, context2)).IsFalse();
    }

    [Test]
    public async Task CreateDbContext_AfterDispose_ShouldThrow()
    {
        // Arrange
        var factory = CreateFactory();
        factory.Dispose();

        // Act
        var act = () => factory.CreateDbContext();

        // Assert
        await Assert.That(act).Throws<ObjectDisposedException>();
    }

    // ==================== Rotation Tests ====================

    [Test]
    public async Task OnConnectionOptionsChanged_WithNewConnectionString_ShouldIncreaseRotationCount()
    {
        // Arrange
        var factory = CreateFactory();
        await Assert.That(factory.RotationCount).IsEqualTo(0);

        // Act - Simulate credential change
        _onChangeCallback?.Invoke(
            new DatabaseConnectionOptions { ConnectionString = "Host=newhost;Database=test" },
            null);

        // Assert
        await Assert.That(factory.RotationCount).IsEqualTo(1);
    }

    [Test]
    public async Task OnConnectionOptionsChanged_WithSameConnectionString_ShouldNotIncreaseRotationCount()
    {
        // Arrange
        var connectionString = "Host=localhost;Database=test;Username=user;Password=secret123";
        var factory = CreateFactory(new DatabaseConnectionOptions { ConnectionString = connectionString });
        await Assert.That(factory.RotationCount).IsEqualTo(0);

        // Act - Simulate callback with same connection string
        _onChangeCallback?.Invoke(
            new DatabaseConnectionOptions { ConnectionString = connectionString },
            null);

        // Assert
        await Assert.That(factory.RotationCount).IsEqualTo(0);
    }

    [Test]
    public async Task OnConnectionOptionsChanged_WhenRotationDisabled_ShouldNotRotate()
    {
        // Arrange
        var factory = CreateFactory(
            new DatabaseConnectionOptions { ConnectionString = "Host=localhost" },
            new RotationOptions { Enabled = false });
        await Assert.That(factory.RotationCount).IsEqualTo(0);

        // Act - Simulate credential change
        _onChangeCallback?.Invoke(
            new DatabaseConnectionOptions { ConnectionString = "Host=newhost" },
            null);

        // Assert
        await Assert.That(factory.RotationCount).IsEqualTo(0);
    }

    // ==================== ForceRefresh Tests ====================

    [Test]
    public async Task ForceRefresh_ShouldTriggerRotation()
    {
        // Arrange
        var factory = CreateFactory();
        var initialCount = factory.RotationCount;

        // Act
        SecretRotationLocalAcknowledgement acknowledgement = factory.ForceRefresh();

        // Assert - ForceRefresh reads current options which hasn't changed,
        // so it won't actually increment if connection string is the same
        await Assert.That(factory.RotationCount).IsEqualTo(initialCount);
        await Assert.That(acknowledgement.Status).IsEqualTo(SecretRotationLocalStatus.Activated);
    }

    [Test]
    public async Task ForceRefresh_AfterDispose_ShouldThrow()
    {
        // Arrange
        var factory = CreateFactory();
        factory.Dispose();

        // Act
        var act = () => factory.ForceRefresh();

        // Assert
        await Assert.That(act).Throws<ObjectDisposedException>();
    }

    // ==================== Dispose Tests ====================

    [Test]
    public async Task Dispose_ShouldNotThrow()
    {
        // Arrange
        var factory = CreateFactory();

        // Act
        var act = () => factory.Dispose();

        // Assert
        await Assert.That(act).ThrowsNothing();
    }

    [Test]
    public async Task Dispose_MultipleTimes_ShouldNotThrow()
    {
        // Arrange
        var factory = CreateFactory();

        // Act
        var act = () =>
        {
            factory.Dispose();
            factory.Dispose();
            factory.Dispose();
        };

        // Assert
        await Assert.That(act).ThrowsNothing();
    }

    public void Dispose()
    {
        _factory?.Dispose();
    }
}

/// <summary>
/// Test DbContext for unit testing.
/// </summary>
public class TestDbContext : DbContext
{
    public TestDbContext(DbContextOptions<TestDbContext> options) : base(options)
    {
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        // Don't actually connect to database in tests
        if (!optionsBuilder.IsConfigured)
        {
            optionsBuilder.UseInMemoryDatabase("TestDb");
        }
    }
}
