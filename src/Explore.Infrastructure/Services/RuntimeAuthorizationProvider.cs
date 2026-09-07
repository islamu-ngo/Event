using System.Diagnostics;
using System.Text.Json;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Organizations.Requests.Commands;
using Explore.Application.Features.StorageObjects.Requests.Commands;
using Explore.Application.Models;
using Explore.Application.Telemetry;
using Explore.Domain.Constants;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Services;

/// <summary>
/// Authorization provider that routes decisions based on per-tenant and instance-level configuration.
/// <para><b>Decision flow (evaluated in order):</b></para>
/// <list type="number">
/// <item><b>BYO Cerbos</b>: If tenant has a custom Cerbos endpoint configured via
/// <see cref="ICerbosConfigResolver"/>, route ALL resource checks there (regardless of instance mode).
/// This allows tenants to enforce stricter or custom policies.</item>
/// <item><b>Instance Cerbos</b>: If the <c>AuthorizationProvider</c> system setting is <c>"cerbos"</c>,
/// route to the shared instance PDP. <see cref="Application.Authorization.AuthorizationScope"/> on each
/// check provides tenant context for scoped policy resolution.</item>
/// <item><b>Fallback RBAC</b>: Otherwise, use <see cref="FallbackAuthorizationService"/>
/// (database-driven role/permission checks).</item>
/// </list>
/// <para><b>Failure handling:</b></para>
/// <list type="bullet">
/// <item>Instance Cerbos failure → deny all checks. The operator chose Cerbos; falling back
/// to a potentially more permissive local RBAC would silently bypass intended policies.</item>
/// <item>BYO Cerbos failure - Safe-Mode activated: deny all except instance admin. Prevents
/// bypassing stricter tenant policies. The latch is scoped to the request, so recovery needs no
/// operator action. There is no configurable fail-open: an inert <c>cerbos.failure_mode</c> setting
/// used to suggest otherwise and was deleted.</item>
/// </list>
/// <para><b>Setting access</b>: Always uses instance-level provider (never BYO).
/// Settings are platform governance, not tenant-customizable.</para>
/// </summary>
public sealed class RuntimeAuthorizationProvider : IAuthorizationProvider, IAuthorizationProviderModeCacheInvalidator
{
    private readonly CerbosAuthorizationService _cerbosProvider;
    private readonly FallbackAuthorizationService _localProvider;
    private readonly ICerbosConfigResolver _cerbosConfigResolver;
    private readonly ISystemSettingRepository _systemSettingRepository;
    private readonly ISupportAccessSessionService? _supportAccessSessionService;
    private readonly IMemoryCache _cache;
    private readonly ILogger<RuntimeAuthorizationProvider> _logger;
    private readonly AuthorizationProviderDeploymentOptions _deploymentOptions;
    private readonly BusinessMetrics? _metrics;

    private const string InstanceModeCacheKey = "AuthorizationProvider_Mode";
    private static readonly TimeSpan InstanceModeCacheDuration = TimeSpan.FromMinutes(1);

    public RuntimeAuthorizationProvider(
        CerbosAuthorizationService cerbosProvider,
        FallbackAuthorizationService localProvider,
        ICerbosConfigResolver cerbosConfigResolver,
        ISystemSettingRepository systemSettingRepository,
        IMemoryCache cache,
        ILogger<RuntimeAuthorizationProvider> logger,
        IOptions<AuthorizationProviderDeploymentOptions> deploymentOptions,
        ISupportAccessSessionService? supportAccessSessionService = null,
        BusinessMetrics? metrics = null)
    {
        _cerbosProvider = cerbosProvider;
        _localProvider = localProvider;
        _cerbosConfigResolver = cerbosConfigResolver;
        _systemSettingRepository = systemSettingRepository;
        _supportAccessSessionService = supportAccessSessionService;
        _cache = cache;
        _logger = logger;
        _deploymentOptions = deploymentOptions.Value;
        _metrics = metrics;
    }

    public async Task<AuthorizationDecision> AuthorizeAsync(
        AuthorizationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.ResourceId))
            return AuthorizationDecision.Deny(AuthorizationProviderMetadata.Runtime, AuthorizationDecisionReasonCodes.InvalidRequest);

        var results = await AuthorizeBatchAsync([request], cancellationToken);
        return results.Count > 0
            ? results[0]
            : AuthorizationDecision.Deny(AuthorizationProviderMetadata.Runtime, AuthorizationDecisionReasonCodes.ProviderError);
    }

    public async Task<IReadOnlyList<AuthorizationDecision>> AuthorizeBatchAsync(
        IReadOnlyList<AuthorizationRequest> checks,
        CancellationToken cancellationToken = default)
    {
        if (checks.Count == 0)
            return [];

        var startedAt = Stopwatch.GetTimestamp();
        var decisions = await EvaluateBatchAsync(checks, cancellationToken);
        RecordDecisionTelemetry(checks, decisions, Stopwatch.GetElapsedTime(startedAt));
        return decisions;
    }

    /// <summary>
    /// Emits one bounded metric and one span event per decision.
    /// <para>
    /// This wraps every routing path — support-access boundary denials included — so a decision cannot be
    /// returned without being counted. Batch duration is attributed to each decision in the batch rather
    /// than divided among them: the question an operator asks is "how long does authorizing this
    /// capability take", and a batched check genuinely did wait that long.
    /// </para>
    /// </summary>
    private void RecordDecisionTelemetry(
        IReadOnlyList<AuthorizationRequest> checks,
        IReadOnlyList<AuthorizationDecision> decisions,
        TimeSpan duration)
    {
        if (_metrics is null && Activity.Current is null)
            return;

        var durationMs = duration.TotalMilliseconds;
        var activity = Activity.Current;

        for (var i = 0; i < decisions.Count && i < checks.Count; i++)
        {
            var check = checks[i];
            var decision = decisions[i];
            var outcome = decision.IsAllowed ? "allowed" : "denied";

            _metrics?.RecordAuthorizationDecision(
                check.ResourceKind,
                check.Action,
                outcome,
                decision.ReasonCode,
                decision.Provider.ProviderId,
                durationMs);

            // Only denials get a span event. Tagging every allow would bury the interesting case in a
            // trace that is mostly allows, and the metric already carries the allow counts.
            if (activity is null || decision.IsAllowed)
                continue;

            activity.AddEvent(new ActivityEvent(
                "authorization.denied",
                tags: new ActivityTagsCollection
                {
                    { "authorization.resource_kind", check.ResourceKind },
                    { "authorization.action", check.Action },
                    { "authorization.reason_code", decision.ReasonCode },
                    { "authorization.provider", decision.Provider.ProviderId }
                }));
        }
    }

    private async Task<IReadOnlyList<AuthorizationDecision>> EvaluateBatchAsync(
        IReadOnlyList<AuthorizationRequest> checks,
        CancellationToken cancellationToken)
    {
        var supportBoundary = await ApplySupportAccessBoundaryAsync(checks, cancellationToken);
        if (supportBoundary.EffectiveChecks.Count == 0)
            return supportBoundary.Results;

        var effectiveChecks = supportBoundary.EffectiveChecks;
        IReadOnlyList<AuthorizationDecision> evaluatedResults;

        if (UsesSettingAuthorization(effectiveChecks))
        {
            evaluatedResults = await ExecuteInstanceProviderAsync(effectiveChecks, cancellationToken);
            return supportBoundary.Complete(evaluatedResults);
        }

        // Step 1: Check if the tenant has a BYO Cerbos configuration (works regardless of instance mode)
        CerbosConfiguration? byoConfig;
        try
        {
            byoConfig = await ResolveTenantByoConfigAsync(cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(
                "Failed to resolve tenant BYO Cerbos config for batch ({Count} checks). Activating safe mode to avoid local RBAC bypass. FailureType={FailureType}",
                effectiveChecks.Count,
                ex.GetType().Name);
            _localProvider.ActivateSafeMode();
            evaluatedResults = await _localProvider.AuthorizeBatchAsync(effectiveChecks, cancellationToken);
            return supportBoundary.Complete(evaluatedResults);
        }

        if (byoConfig is not null)
        {
            evaluatedResults = await ExecuteByoAsync(byoConfig, effectiveChecks, cancellationToken);
            return supportBoundary.Complete(evaluatedResults);
        }

        // The selected instance provider decides the whole batch. There is no third "both" mode: splitting
        // a batch between Local and Cerbos would make the local evaluator a second production authority,
        // and a tightened Cerbos rule would then have no effect on the capabilities routed around it.
        evaluatedResults = await ExecuteInstanceProviderAsync(effectiveChecks, cancellationToken);
        return supportBoundary.Complete(evaluatedResults);
    }

    private async Task<IReadOnlyList<AuthorizationDecision>> ExecuteInstanceProviderAsync(
        IReadOnlyList<AuthorizationRequest> checks,
        CancellationToken cancellationToken)
    {
        var provider = await ResolveInstanceProviderAsync(cancellationToken);

        try
        {
            if (provider != _cerbosProvider)
                return await provider.AuthorizeBatchAsync(checks, cancellationToken);

            return await _cerbosProvider.AuthorizeBatchWithUnavailableSignalAsync(checks, cancellationToken);
        }
        catch (Exception ex) when (provider == _cerbosProvider)
        {
            // When Cerbos is the configured instance authorization provider and is unavailable,
            // deny all checks. Falling back to a potentially more permissive local RBAC
            // would silently bypass the policies the operator explicitly chose to enforce.
            _logger.LogError(
                "Instance Cerbos provider unavailable for batch ({Count} checks). " +
                "Denying all — Cerbos is the configured authorization provider. " +
                "Restore Cerbos connectivity or switch authorization.provider setting to resolve. FailureType={FailureType}",
                checks.Count,
                ex.GetType().Name);
            return checks
                .Select(_ => AuthorizationDecision.Deny(AuthorizationProviderMetadata.Cerbos, AuthorizationDecisionReasonCodes.ProviderUnavailable))
                .ToArray();
        }
    }

    private async Task<SupportAccessBoundaryResult> ApplySupportAccessBoundaryAsync(
        IReadOnlyList<AuthorizationRequest> checks,
        CancellationToken cancellationToken)
    {
        if (_supportAccessSessionService is null)
            return SupportAccessBoundaryResult.PassThrough(checks);

        var supportContext = await _supportAccessSessionService.GetCurrentAsync(cancellationToken);
        if (!supportContext.WasForwarded && !supportContext.IsActive)
            return SupportAccessBoundaryResult.PassThrough(checks);

        AddSupportAccessTraceTags(supportContext);

        var results = Enumerable.Repeat(
            AuthorizationDecision.Deny(AuthorizationProviderMetadata.Runtime),
            checks.Count).ToArray();
        var effectiveChecks = new List<AuthorizationRequest>(checks.Count);
        var originalIndexes = new List<int>(checks.Count);

        for (var i = 0; i < checks.Count; i++)
        {
            var check = checks[i];
            if (!IsSupportAccessBoundedResource(check))
            {
                effectiveChecks.Add(check);
                originalIndexes.Add(i);
                continue;
            }

            var denialReason = GetSupportAccessBoundaryDenialReason(supportContext, check);
            if (denialReason is null)
            {
                effectiveChecks.Add(check);
                originalIndexes.Add(i);
                continue;
            }

            _metrics?.RecordSupportAccessBoundaryDenial(
                denialReason,
                check.Action,
                supportContext.Mode?.ToString());
            AddSupportAccessBoundaryDeniedTraceEvent(check, denialReason);
            _logger.LogWarning(
                "Support-access authorization boundary denied resource={ResourceKind} action={Action} reason={Reason}",
                check.ResourceKind,
                check.Action,
                denialReason);
        }

        return new SupportAccessBoundaryResult(effectiveChecks, originalIndexes, results);
    }

    private static void AddSupportAccessTraceTags(ISupportAccessContext supportContext)
    {
        var activity = Activity.Current;
        if (activity is null)
        {
            return;
        }

        activity.SetTag("support_access.active", supportContext.IsActive);
        activity.SetTag("support_access.was_forwarded", supportContext.WasForwarded);
        activity.SetTag("support_access.allows_writes", supportContext.AllowsWrites);
        activity.SetTag("support_access.mode", supportContext.Mode?.ToString() ?? "unknown");
    }

    private static void AddSupportAccessBoundaryDeniedTraceEvent(AuthorizationRequest check, string reason)
    {
        var activity = Activity.Current;
        if (activity is null)
        {
            return;
        }

        var tags = new ActivityTagsCollection
        {
            ["support_access.denial.reason"] = reason,
            ["resource.kind"] = check.ResourceKind,
            ["resource.action"] = check.Action
        };
        activity.AddEvent(new ActivityEvent("support_access.authorization_boundary_denied", tags: tags));
    }

    private static string? GetSupportAccessBoundaryDenialReason(
        ISupportAccessContext supportContext,
        AuthorizationRequest check)
    {
        if (supportContext.WasForwarded && !supportContext.IsActive)
            return "support_access_inactive";

        if (!supportContext.IsActive)
            return null;

        if (!supportContext.AllowsWrites && !IsReadOnlyCompatibleAction(check.Action))
            return "support_access_read_only";

        if (!supportContext.TargetTenantId.HasValue || supportContext.TargetTenantId.Value == Guid.Empty)
            return "support_access_missing_target_tenant";

        if (!TryResolveGuidAttribute(TrustedAttributes(check), "tenantId", out var resourceTenantId))
            return "support_access_missing_tenant_context";

        return resourceTenantId == supportContext.TargetTenantId.Value
            ? null
            : "support_access_target_tenant_mismatch";
    }

    private static bool IsSupportAccessBoundedResource(AuthorizationRequest check)
    {
        if (HasTenantAttribute(TrustedAttributes(check)))
            return check.ResourceKind is not ResourceKinds.SupportAccessSession;

        return check.ResourceKind is
            ResourceKinds.Tenant or
            ResourceKinds.TenantSetting or
            ResourceKinds.TenantUserRoleGrant or
            ResourceKinds.Category or
            ResourceKinds.Tag or
            ResourceKinds.Location or
            ResourceKinds.LocationRoom or
            ResourceKinds.CustomPropertyDefinition or
            ResourceKinds.CustomPropertyTemplate or
            ResourceKinds.CustomPropertyValue or
            ResourceKinds.CustomPropertyProjection or
            ResourceKinds.CustomPropertyGovernance or
            ResourceKinds.EmailDispatch or
            ResourceKinds.Webhook or
            ResourceKinds.Organization or
            ResourceKinds.OrganizationMember or
            ResourceKinds.OrganizationReview or
            ResourceKinds.Group or
            ResourceKinds.GroupMember or
            ResourceKinds.Event or
            ResourceKinds.EventSession or
            ResourceKinds.EventSessionGroup or
            ResourceKinds.EventSessionAgendaItem or
            ResourceKinds.EventDay or
            ResourceKinds.EventAgendaItem or
            ResourceKinds.EventContactShareConsent or
            ResourceKinds.StorageObject or
            ResourceKinds.Actor;
    }

    private static bool IsReadOnlyCompatibleAction(string action)
    {
        return action is
                AuthorizationActions.View or
                AuthorizationActions.SupportAccessSessions.List or
                AuthorizationActions.SupportAccessSessions.ViewAudit or
                AuthorizationActions.Events.ViewManagement or
                AuthorizationActions.StorageObjects.Download or
                AuthorizationActions.StorageObjects.PresignedDownload or
                AuthorizationActions.ViewSharedContacts or
                AuthorizationActions.ExportSharedContacts
            || action.EndsWith(":view", StringComparison.Ordinal)
            || action.EndsWith(":view-delivery", StringComparison.Ordinal);
    }

    private static bool HasTenantAttribute(IReadOnlyDictionary<string, object>? resourceAttributes) =>
        resourceAttributes?.ContainsKey("tenantId") == true;

    private static bool TryResolveGuidAttribute(
        IReadOnlyDictionary<string, object>? resourceAttributes,
        string attributeName,
        out Guid value)
    {
        value = Guid.Empty;

        if (resourceAttributes?.TryGetValue(attributeName, out var attributeValue) != true)
            return false;

        if (attributeValue is Guid guidValue)
        {
            value = guidValue;
            return true;
        }

        return attributeValue is string stringValue && Guid.TryParse(stringValue, out value);
    }

    public void InvalidateInstanceMode()
    {
        _cache.Remove(InstanceModeCacheKey);
        _logger.LogInformation("Authorization provider mode cache invalidated");
    }

    /// <summary>
    /// Resolves BYO Cerbos config for the current tenant. Returns null if tenant uses instance PDP.
    /// </summary>
    private async Task<CerbosConfiguration?> ResolveTenantByoConfigAsync(CancellationToken cancellationToken)
    {
        var config = await _cerbosConfigResolver.ResolveAsync(cancellationToken);

        if (config is null || config.IsInstanceDefault || config.Mode == CerbosMode.Instance)
            return null;

        return config;
    }

    /// <summary>
    /// Executes authorization checks against a BYO Cerbos endpoint.
    /// On failure, always activates safe mode; permissive BYO outage fallback is forbidden.
    /// </summary>
    private async Task<IReadOnlyList<AuthorizationDecision>> ExecuteByoAsync(
        CerbosConfiguration config,
        IReadOnlyList<AuthorizationRequest> checks,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogDebug("Routing {Count} auth checks to BYO Cerbos endpoint", checks.Count);
            return await _cerbosProvider.AuthorizeBatchWithEndpointAsync(config.Endpoint, checks, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                "BYO Cerbos PDP unreachable. Activating safe mode. FailureType={FailureType}",
                ex.GetType().Name);

            // Never fall back to instance PDP or standard local RBAC; tenant policies might be stricter.
            _localProvider.ActivateSafeMode();
            return await _localProvider.AuthorizeBatchAsync(checks, cancellationToken);
        }
    }

    /// <summary>
    /// Resolves the instance-level provider (Cerbos or Local) based on SystemSetting.
    /// </summary>
    private async Task<IAuthorizationProvider> ResolveInstanceProviderAsync(CancellationToken cancellationToken)
    {
        var deploymentProvider = _deploymentOptions.GetProvider();
        if (deploymentProvider is not null)
        {
            _logger.LogDebug(
                "Authorization provider resolved from deployment configuration: {Provider}",
                deploymentProvider);
            return deploymentProvider == AuthorizationProviderDeploymentOptions.CerbosProvider
                ? _cerbosProvider
                : _localProvider;
        }

        var mode = await _cache.GetOrCreateAsync(InstanceModeCacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = InstanceModeCacheDuration;

            try
            {
                var setting = await _systemSettingRepository.GetByKey(GovernanceSettingKeys.Security.AuthorizationProvider);
                var value = NormalizeProviderMode(setting?.Value);

                if (value is "cerbos")
                {
                    _logger.LogDebug("Authorization provider resolved to: Cerbos (from SystemSetting)");
                    return "cerbos";
                }

                _logger.LogDebug("Authorization provider resolved to: Local (setting={Value})", value ?? "null");
                return "local";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    "Failed to read authorization provider setting. Using Cerbos fail-closed path. FailureType={FailureType}",
                    ex.GetType().Name);
                return "cerbos";
            }
        });

        return mode == "cerbos" ? _cerbosProvider : _localProvider;
    }

    private static Dictionary<string, object>? TrustedAttributes(AuthorizationRequest request) =>
        AuthorizationFactAttributeProjection.ToAttributes(request.Facts);

    private static bool UsesSettingAuthorization(IReadOnlyList<AuthorizationRequest> checks)
    {
        return checks.Count > 0
            && checks.All(check => check.ResourceKind is ResourceKinds.InstanceSetting or ResourceKinds.TenantSetting);
    }

    private sealed record SupportAccessBoundaryResult(
        IReadOnlyList<AuthorizationRequest> EffectiveChecks,
        IReadOnlyList<int> OriginalIndexes,
        AuthorizationDecision[] Results)
    {
        public static SupportAccessBoundaryResult PassThrough(IReadOnlyList<AuthorizationRequest> checks)
        {
            return new SupportAccessBoundaryResult(
                checks,
                Enumerable.Range(0, checks.Count).ToArray(),
                Enumerable.Repeat(AuthorizationDecision.Deny(AuthorizationProviderMetadata.Runtime), checks.Count).ToArray());
        }

        public IReadOnlyList<AuthorizationDecision> Complete(IReadOnlyList<AuthorizationDecision> evaluatedResults)
        {
            for (var i = 0; i < OriginalIndexes.Count; i++)
            {
                Results[OriginalIndexes[i]] = i < evaluatedResults.Count
                    ? evaluatedResults[i]
                    : AuthorizationDecision.Deny(AuthorizationProviderMetadata.Runtime, AuthorizationDecisionReasonCodes.ProviderError);
            }

            return Results;
        }
    }

    private static string NormalizeProviderMode(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
            return "local";

        var trimmedValue = rawValue.Trim();

        if (TryDeserializeString(trimmedValue, out var deserializedValue)
            && !string.IsNullOrWhiteSpace(deserializedValue))
        {
            return deserializedValue.Trim().ToLowerInvariant();
        }

        return trimmedValue.Trim('"').Trim().ToLowerInvariant();
    }

    private static bool TryDeserializeString(string rawValue, out string? value)
    {
        try
        {
            value = JsonSerializer.Deserialize<string>(rawValue);
            return true;
        }
        catch (JsonException)
        {
            value = null;
            return false;
        }
        catch (NotSupportedException)
        {
            value = null;
            return false;
        }
    }
}
