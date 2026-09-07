using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Models;
using Explore.Application.Settings;
using Explore.Domain.Constants;
using Explore.Domain.Secrets;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Explore.Infrastructure.Mail;

/// <summary>
/// Resolves SMTP settings from the cascading settings engine (SystemSetting → TenantSetting).
/// <para>
/// SaaS scenarios supported:
/// - Instance admin locks SMTP settings → all tenants use the SaaS provider's SMTP server
/// - Instance admin leaves SMTP unlocked → tenants can bring their own SMTP credentials
/// - Default SMTP at instance level → tenants use it unless they override
/// </para>
/// </summary>
public class SmtpConfigResolver : ISmtpConfigResolver
{
    private readonly IHierarchicalSettingsResolver _resolver;
    private readonly ITenantContext _tenantContext;
    private readonly IMemoryCache _cache;
    private readonly ILogger<SmtpConfigResolver> _logger;
    private readonly ISecretResolver _secretResolver;

    private static readonly TimeSpan CacheExpiration = TimeSpan.FromMinutes(5);
    private const string CacheKeyPrefix = "SmtpConfig:";

    public SmtpConfigResolver(
        IHierarchicalSettingsResolver resolver,
        ITenantContext tenantContext,
        IMemoryCache cache,
        ISecretResolver secretResolver,
        ILogger<SmtpConfigResolver> logger)
    {
        _resolver = resolver;
        _tenantContext = tenantContext;
        _cache = cache;
        _secretResolver = secretResolver;
        _logger = logger;
    }

    public async Task<SmtpConfiguration?> ResolveAsync(CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.TenantId;
        var cacheKey = $"{CacheKeyPrefix}{tenantId}";

        if (_cache.TryGetValue(cacheKey, out SmtpConfiguration? cached))
        {
            return cached;
        }

        var config = await ResolveFromSettingsAsync(tenantId, cancellationToken);

        if (config is not null)
        {
            _cache.Set(cacheKey, config, CacheExpiration);
        }

        return config;
    }

    public void InvalidateCache(Guid? tenantId = null)
    {
        if (tenantId.HasValue)
        {
            _cache.Remove($"{CacheKeyPrefix}{tenantId.Value}");
        }
        else
        {
            // Can't enumerate MemoryCache keys — callers should invalidate specific tenants.
            // For a full flush, the SettingsResolver also has its own cache invalidation.
            _logger.LogInformation("SMTP config cache invalidation requested for all tenants");
        }
    }

    private async Task<SmtpConfiguration?> ResolveFromSettingsAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        // IHierarchicalSettingsResolver handles the cascade:
        // 1. If setting is IsLocked at system level → uses system value (instance admin control)
        // 2. If tenant has an override → uses tenant value (tenant brings own SMTP)
        // 3. Falls back to system default

        var ctx = new SettingContext(TenantId: tenantId);

        var host = await _resolver.ResolveAsync<string>(GovernanceSettingKeys.Email.SmtpHost, ctx, cancellationToken);
        if (string.IsNullOrWhiteSpace(host))
        {
            _logger.LogDebug("SMTP not configured for tenant {TenantId}: host is empty", tenantId);
            return null;
        }

        var fromAddress = await _resolver.ResolveAsync<string>(GovernanceSettingKeys.Email.FromAddress, ctx, cancellationToken);
        if (string.IsNullOrWhiteSpace(fromAddress))
        {
            _logger.LogDebug("SMTP not configured for tenant {TenantId}: from_address is empty", tenantId);
            return null;
        }

        var port = await _resolver.ResolveAsync<int>(GovernanceSettingKeys.Email.SmtpPort, ctx, cancellationToken);
        var securityStr = await _resolver.ResolveAsync<string>(GovernanceSettingKeys.Email.SmtpSecurity, ctx, cancellationToken);
        var timeout = await _resolver.ResolveAsync<int>(GovernanceSettingKeys.Email.SmtpTimeoutSeconds, ctx, cancellationToken);

        return new SmtpConfiguration
        {
            Host = host,
            Port = port > 0 ? port : 587,
            Username = await ResolveOptionalSecretAsync(
                SecretDefinitionRegistry.Keys.Smtp.Username,
                tenantId,
                cancellationToken),
            Password = await ResolveOptionalSecretAsync(
                SecretDefinitionRegistry.Keys.Smtp.Password,
                tenantId,
                cancellationToken),
            Security = Enum.TryParse<SmtpSecurityMode>(securityStr, ignoreCase: true, out var security)
                ? security
                : SmtpSecurityMode.StartTls,
            FromAddress = fromAddress,
            FromName = await _resolver.ResolveAsync<string>(GovernanceSettingKeys.Email.FromName, ctx, cancellationToken) ?? "Explore",
            TimeoutSeconds = timeout > 0 ? timeout : 30,
            SkipCertificateValidation = await _resolver.ResolveAsync<bool>(GovernanceSettingKeys.Email.SmtpSkipCertValidation, ctx, cancellationToken)
        };
    }

    private async Task<string?> ResolveOptionalSecretAsync(
        string settingKey,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        SecretResolutionResult result = await _secretResolver.ResolveAsync(
            settingKey,
            tenantId,
            cancellationToken);
        return result.Status switch
        {
            SecretResolutionStatus.Resolved => result.Value,
            SecretResolutionStatus.Unconfigured => null,
            _ => throw new InvalidOperationException("smtp_secret_unavailable")
        };
    }
}
