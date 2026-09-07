using Explore.Secrets.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Explore.Secrets.Services;

/// <summary>
/// DbContext factory that supports connection string rotation.
/// Monitors for connection string changes and ensures new contexts use updated credentials.
/// </summary>
/// <remarks>
/// Key features:
/// - Listens to IOptionsMonitor for connection string changes
/// - Each CreateDbContext call uses the latest connection string
/// - Thread-safe via lock on options access
/// - Logs connection string changes (credentials redacted)
///
/// Unlike HttpClient, DbContext instances are short-lived and should be disposed
/// after each unit of work. This factory ensures that newly created contexts
/// automatically use updated connection strings without explicit rotation.
/// </remarks>
/// <typeparam name="TContext">The type of DbContext to create.</typeparam>
public sealed class RotationAwareDbContextFactory<TContext> : IDbContextFactory<TContext>, IDisposable
    where TContext : DbContext
{
    private readonly Func<DbContextOptions<TContext>, TContext> _contextFactory;
    private readonly IOptionsMonitor<DatabaseConnectionOptions> _connectionOptions;
    private readonly IOptionsMonitor<RotationOptions> _rotationOptions;
    private readonly ILogger<RotationAwareDbContextFactory<TContext>> _logger;
    private readonly Func<string, bool> _validateCandidate;
    private readonly string _replicaId;
    private readonly IDisposable? _connectionChangeListener;
    private readonly object _optionsLock = new();

    private string? _currentConnectionString;
    private DateTime _lastConnectionStringChange;
    private int _rotationCount;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the RotationAwareDbContextFactory.
    /// </summary>
    /// <param name="contextFactory">Factory function to create DbContext instances.</param>
    /// <param name="connectionOptions">Options monitor for database connection settings.</param>
    /// <param name="rotationOptions">Options monitor for rotation settings.</param>
    /// <param name="logger">Logger instance.</param>
    public RotationAwareDbContextFactory(
        Func<DbContextOptions<TContext>, TContext> contextFactory,
        IOptionsMonitor<DatabaseConnectionOptions> connectionOptions,
        IOptionsMonitor<RotationOptions> rotationOptions,
        ILogger<RotationAwareDbContextFactory<TContext>> logger,
        Func<string, bool>? validateCandidate = null,
        string? replicaId = null)
    {
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _connectionOptions = connectionOptions ?? throw new ArgumentNullException(nameof(connectionOptions));
        _rotationOptions = rotationOptions ?? throw new ArgumentNullException(nameof(rotationOptions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _validateCandidate = validateCandidate ?? IsValidPostgreSqlConnectionString;
        _replicaId = string.IsNullOrWhiteSpace(replicaId)
            ? Environment.GetEnvironmentVariable("HOSTNAME") ?? Environment.MachineName
            : replicaId;

        // Initialize with current connection string
        _currentConnectionString = connectionOptions.CurrentValue.ConnectionString;
        _lastConnectionStringChange = DateTime.UtcNow;

        // Subscribe to connection string changes
        _connectionChangeListener = _connectionOptions.OnChange(
            (options, name) => _ = OnConnectionOptionsChanged(options, name));

        _logger.LogDebug(
            "RotationAwareDbContextFactory<{ContextType}> initialized",
            typeof(TContext).Name);
    }

    /// <summary>
    /// Gets the number of times the connection string has been rotated.
    /// </summary>
    public int RotationCount => _rotationCount;

    /// <summary>
    /// Gets the timestamp of the last connection string change.
    /// </summary>
    public DateTime LastConnectionStringChange => _lastConnectionStringChange;

    /// <summary>
    /// Creates a new DbContext with the current connection string.
    /// </summary>
    /// <returns>A new DbContext instance.</returns>
    public TContext CreateDbContext()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        string? connectionString;
        lock (_optionsLock)
        {
            connectionString = _currentConnectionString;
        }

        if (string.IsNullOrEmpty(connectionString))
        {
            throw new InvalidOperationException(
                $"Connection string is not configured for {typeof(TContext).Name}. " +
                $"Ensure DatabaseConnectionOptions is properly configured.");
        }

        var optionsBuilder = new DbContextOptionsBuilder<TContext>();
        ConfigureDbContextOptions(optionsBuilder, connectionString);

        var context = _contextFactory(optionsBuilder.Options);

        _logger.LogTrace(
            "Created DbContext<{ContextType}> (rotations: {RotationCount})",
            typeof(TContext).Name,
            _rotationCount);

        return context;
    }

    /// <summary>
    /// Configures the DbContext options with the connection string.
    /// </summary>
    private void ConfigureDbContextOptions(
        DbContextOptionsBuilder<TContext> optionsBuilder,
        string connectionString)
    {
        var dbOptions = _connectionOptions.CurrentValue;

        // Build Npgsql-specific connection string with optional pool settings
        var npgsqlConnectionString = connectionString;

        // Note: Connection pooling settings should be part of the connection string
        // or configured via NpgsqlDataSource. We log them here for visibility.
        if (dbOptions.MaxPoolSize.HasValue)
        {
            _logger.LogDebug("MaxPoolSize configured: {MaxPoolSize}", dbOptions.MaxPoolSize);
        }

        optionsBuilder.UseNpgsql(npgsqlConnectionString);
    }

    private SecretRotationLocalAcknowledgement OnConnectionOptionsChanged(
        DatabaseConnectionOptions newOptions,
        string? name,
        Guid? requestedAttemptId = null)
    {
        var attemptId = requestedAttemptId is { } value && value != Guid.Empty
            ? value
            : Guid.CreateVersion7();
        if (!_rotationOptions.CurrentValue.Enabled)
        {
            _logger.LogDebug("secret_rotation_disabled");
            return Acknowledge(attemptId, SecretRotationLocalStatus.Rejected);
        }

        var newConnectionString = newOptions.ConnectionString;
        if (string.IsNullOrWhiteSpace(newConnectionString) || !_validateCandidate(newConnectionString))
        {
            _logger.LogWarning("secret_rotation_candidate_rejected");
            return Acknowledge(attemptId, SecretRotationLocalStatus.Rejected);
        }

        lock (_optionsLock)
        {
            // Check if connection string actually changed
            if (string.Equals(_currentConnectionString, newConnectionString, StringComparison.Ordinal))
            {
                _logger.LogDebug("secret_rotation_unchanged");
                return Acknowledge(attemptId, SecretRotationLocalStatus.Activated);
            }

            _currentConnectionString = newConnectionString;
            _lastConnectionStringChange = DateTime.UtcNow;
            Interlocked.Increment(ref _rotationCount);
        }

        if (_rotationOptions.CurrentValue.LogRotationEvents)
        {
            _logger.LogInformation("secret_rotation_activated");
        }

        return Acknowledge(attemptId, SecretRotationLocalStatus.Activated);
    }

    private static bool IsValidPostgreSqlConnectionString(string connectionString)
    {
        try
        {
            _ = new NpgsqlConnectionStringBuilder(connectionString);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// Forces a connection string update from the current options.
    /// Useful for testing or manual rotation triggers.
    /// </summary>
    public SecretRotationLocalAcknowledgement ForceRefresh(Guid? attemptId = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var currentOptions = _connectionOptions.CurrentValue;
        return OnConnectionOptionsChanged(currentOptions, null, attemptId);
    }

    /// <summary>
    /// Disposes the factory and unsubscribes from change notifications.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _logger.LogDebug(
            "Disposing RotationAwareDbContextFactory<{ContextType}> (total rotations: {RotationCount})",
            typeof(TContext).Name,
            _rotationCount);

        _connectionChangeListener?.Dispose();
    }

    private SecretRotationLocalAcknowledgement Acknowledge(
        Guid attemptId,
        SecretRotationLocalStatus status) =>
        new(attemptId, _replicaId, "database", status, DateTimeOffset.UtcNow);
}

/// <summary>
/// Non-generic base interface for rotation-aware factory operations.
/// </summary>
public interface IRotationAwareDbContextFactory
{
    /// <summary>
    /// Gets the number of times the connection string has been rotated.
    /// </summary>
    int RotationCount { get; }

    /// <summary>
    /// Gets the timestamp of the last connection string change.
    /// </summary>
    DateTime LastConnectionStringChange { get; }

    /// <summary>
    /// Forces a connection string refresh from the current options.
    /// </summary>
    SecretRotationLocalAcknowledgement ForceRefresh(Guid? attemptId = null);
}
