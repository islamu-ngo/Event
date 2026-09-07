using System.Diagnostics;
using Explore.Application.Authentication;
using Explore.Secrets.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Explore.Secrets.Providers;

/// <summary>
/// Decorator that adds audit logging to any <see cref="ISecretProvider"/>.
/// Logs provider-state mutations with correlation ID and user context.
/// </summary>
public sealed class AuditingSecretProviderDecorator : ISecretProvider
{
    private readonly ISecretProvider _inner;
    private readonly ISecretAuditLogger _auditLogger;
    private readonly IHttpContextAccessor? _httpContextAccessor;
    private readonly ILogger<AuditingSecretProviderDecorator> _logger;
    private readonly TimeProvider _clock;

    public AuditingSecretProviderDecorator(
        ISecretProvider inner,
        ISecretAuditLogger auditLogger,
        ILogger<AuditingSecretProviderDecorator> logger,
        IHttpContextAccessor? httpContextAccessor = null,
        TimeProvider? clock = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _auditLogger = auditLogger ?? throw new ArgumentNullException(nameof(auditLogger));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _httpContextAccessor = httpContextAccessor;
        _clock = clock ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public SecretProviderType ProviderType => _inner.ProviderType;

    /// <inheritdoc />
    public bool SupportsRefresh => _inner.SupportsRefresh;

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var correlationId = GetCorrelationId();
        var userId = GetUserId();

        _logger.LogInformation(
            "Initializing secret provider {ProviderType}. CorrelationId: {CorrelationId}",
            _inner.ProviderType,
            correlationId);

        try
        {
            await _inner.InitializeAsync(cancellationToken);

            await _auditLogger.LogAsync(new SecretAuditEntry(
                Operation: SecretOperation.Initialize,
                ProviderType: _inner.ProviderType,
                KeyPattern: null,
                Timestamp: _clock.GetUtcNow(),
                UserId: userId,
                CorrelationId: correlationId,
                Success: true),
                cancellationToken);

            _logger.LogInformation(
                "Secret provider {ProviderType} initialized successfully. CorrelationId: {CorrelationId}",
                _inner.ProviderType,
                correlationId);
        }
        catch (Exception)
        {
            await _auditLogger.LogAsync(new SecretAuditEntry(
                Operation: SecretOperation.InitializeFailed,
                ProviderType: _inner.ProviderType,
                KeyPattern: null,
                Timestamp: _clock.GetUtcNow(),
                UserId: userId,
                CorrelationId: correlationId,
                Success: false,
                ErrorMessage: "secret_provider_initialization_failed"),
                cancellationToken);

            _logger.LogError(
                "secret_provider_initialization_failed provider={ProviderType} correlation_id={CorrelationId}",
                _inner.ProviderType,
                correlationId);

            throw;
        }
    }

    /// <inheritdoc />
    public Task<string?> GetSecretAsync(string key, CancellationToken cancellationToken = default) =>
        _inner.GetSecretAsync(key, cancellationToken);

    /// <inheritdoc />
    public Task<SecretValue?> GetSecretWithMetadataAsync(string key, CancellationToken cancellationToken = default) =>
        _inner.GetSecretWithMetadataAsync(key, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<string, string>> GetSecretsByPathAsync(
        string pathPrefix,
        CancellationToken cancellationToken = default) =>
        _inner.GetSecretsByPathAsync(pathPrefix, cancellationToken);

    /// <inheritdoc />
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var correlationId = GetCorrelationId();
        var userId = GetUserId();

        _logger.LogInformation(
            "Refreshing secrets from {ProviderType}. CorrelationId: {CorrelationId}",
            _inner.ProviderType,
            correlationId);

        try
        {
            await _inner.RefreshAsync(cancellationToken);

            await _auditLogger.LogAsync(new SecretAuditEntry(
                Operation: SecretOperation.Refresh,
                ProviderType: _inner.ProviderType,
                KeyPattern: null,
                Timestamp: _clock.GetUtcNow(),
                UserId: userId,
                CorrelationId: correlationId,
                Success: true),
                cancellationToken);

            _logger.LogInformation(
                "Secrets refreshed successfully from {ProviderType}. CorrelationId: {CorrelationId}",
                _inner.ProviderType,
                correlationId);
        }
        catch (Exception)
        {
            await _auditLogger.LogAsync(new SecretAuditEntry(
                Operation: SecretOperation.RefreshFailed,
                ProviderType: _inner.ProviderType,
                KeyPattern: null,
                Timestamp: _clock.GetUtcNow(),
                UserId: userId,
                CorrelationId: correlationId,
                Success: false,
                ErrorMessage: "secret_provider_refresh_failed"),
                cancellationToken);

            _logger.LogError(
                "secret_provider_refresh_failed provider={ProviderType} correlation_id={CorrelationId}",
                _inner.ProviderType,
                correlationId);

            throw;
        }
    }

    /// <inheritdoc />
    public Task<ProviderHealthInfo> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        // Health checks are not audited to avoid noise
        return _inner.GetHealthAsync(cancellationToken);
    }

    /// <summary>
    /// Gets the correlation ID from the current HTTP context or activity.
    /// </summary>
    private string GetCorrelationId()
    {
        // Try HTTP request headers first
        var httpContext = _httpContextAccessor?.HttpContext;
        if (httpContext is not null)
        {
            // Check common correlation ID headers
            if (httpContext.Request.Headers.TryGetValue("X-Correlation-Id", out var correlationHeader) &&
                !string.IsNullOrEmpty(correlationHeader.ToString()))
            {
                return correlationHeader.ToString();
            }

            if (httpContext.Request.Headers.TryGetValue("X-Request-Id", out var requestIdHeader) &&
                !string.IsNullOrEmpty(requestIdHeader.ToString()))
            {
                return requestIdHeader.ToString();
            }

            // Fall back to trace identifier
            if (!string.IsNullOrEmpty(httpContext.TraceIdentifier))
            {
                return httpContext.TraceIdentifier;
            }
        }

        // Try OpenTelemetry Activity
        var activity = Activity.Current;
        if (activity is not null)
        {
            return activity.TraceId.ToString();
        }

        // Generate a new one if nothing available
        return Guid.CreateVersion7().ToString("N")[..8];
    }

    /// <summary>
    /// Gets the user ID from the current HTTP context claims.
    /// </summary>
    private string? GetUserId()
    {
        var httpContext = _httpContextAccessor?.HttpContext;
        if (httpContext?.User?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        return httpContext.User.GetPlatformUserId()?.ToString("D");
    }
}
