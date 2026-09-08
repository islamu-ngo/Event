using System.Collections.Concurrent;
using System.Diagnostics;
using Cerbos.Api.V1.Effect;
using Cerbos.Sdk;
using Cerbos.Sdk.Builder;
using Cerbos.Sdk.Response;
using Cerbos.Sdk.Utility;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Settings;
using Explore.Application.Utilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using CerbosCheckResourcesRequest = Cerbos.Sdk.Builder.CheckResourcesRequest;
using CerbosResourceEntry = Cerbos.Sdk.Builder.ResourceEntry;

namespace Explore.Infrastructure.Services;

/// <summary>
/// Authorization service that delegates decisions to an external Cerbos PDP via the official gRPC SDK.
/// Supports both the instance PDP and per-tenant BYO (Bring Your Own) Cerbos endpoints.
/// No admin credentials are needed — runtime CheckResources is unauthenticated.
/// </summary>
public class CerbosAuthorizationService : IAuthorizationProvider
{
    private readonly ICerbosClient _client;
    private readonly CerbosPrincipalBuilder _principalBuilder;
    private readonly IAdminContext _adminContext;
    private readonly IMachinePrincipalAccessor _machinePrincipalAccessor;
    private readonly IHierarchicalSettingsResolver _resolver;
    private readonly ITenantContext _tenantContext;
    private readonly ICerbosClientFactory _clientFactory;
    private readonly ILogger<CerbosAuthorizationService> _logger;
    private readonly CerbosSettings _settings;

    public CerbosAuthorizationService(
        ICerbosClient client,
        CerbosPrincipalBuilder principalBuilder,
        IAdminContext adminContext,
        IMachinePrincipalAccessor machinePrincipalAccessor,
        IHierarchicalSettingsResolver resolver,
        ITenantContext tenantContext,
        ICerbosClientFactory clientFactory,
        IOptions<CerbosSettings> settings,
        ILogger<CerbosAuthorizationService> logger)
    {
        _client = client;
        _principalBuilder = principalBuilder;
        _adminContext = adminContext;
        _machinePrincipalAccessor = machinePrincipalAccessor;
        _resolver = resolver;
        _tenantContext = tenantContext;
        _clientFactory = clientFactory;
        _logger = logger;
        _settings = settings.Value;
    }

    public async Task<AuthorizationDecision> AuthorizeAsync(
        AuthorizationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.ResourceId))
            return AuthorizationDecision.Deny(AuthorizationProviderMetadata.Cerbos, AuthorizationDecisionReasonCodes.InvalidRequest);

        var results = await AuthorizeBatchAsync([request], cancellationToken);
        return results.Count > 0
            ? results[0]
            : AuthorizationDecision.Deny(AuthorizationProviderMetadata.Cerbos, AuthorizationDecisionReasonCodes.ProviderError);
    }

    public async Task<IReadOnlyList<AuthorizationDecision>> AuthorizeBatchAsync(
        IReadOnlyList<AuthorizationRequest> checks,
        CancellationToken cancellationToken = default)
    {
        if (checks.Count == 0)
            return [];

        return await ExecuteCheckWithStorageTypedDenyAsync(_client, _settings.GrpcEndpoint, checks, cancellationToken);
    }

    private static bool IsStorageUploadCreateWithTypedFacts(AuthorizationRequest check) =>
        check.ResourceKind == ResourceKinds.StorageObject &&
        check.Action == AuthorizationActions.StorageObjects.Create &&
        check.Facts is StorageUploadIntentFacts;

    public async Task<IReadOnlyList<AuthorizationDecision>> AuthorizeBatchWithUnavailableSignalAsync(
        IReadOnlyList<AuthorizationRequest> checks,
        CancellationToken cancellationToken = default)
    {
        if (checks.Count == 0)
            return [];

        return await ExecuteCheckWithStorageTypedDenyAsync(
            _client,
            _settings.GrpcEndpoint,
            checks,
            cancellationToken,
            throwOnUnavailable: true);
    }

    /// <summary>
    /// Checks permissions against a specific Cerbos PDP endpoint (for BYO tenants).
    /// Uses <see cref="ICerbosClientFactory"/> to get a cached gRPC client for the BYO endpoint.
    /// </summary>
    public async Task<IReadOnlyList<AuthorizationDecision>> AuthorizeBatchWithEndpointAsync(
        string endpointUrl,
        IReadOnlyList<AuthorizationRequest> checks,
        CancellationToken cancellationToken = default)
    {
        if (checks.Count == 0)
            return [];

        var byoClient = _clientFactory.GetOrCreate(endpointUrl);
        return await ExecuteCheckWithStorageTypedDenyAsync(
            byoClient,
            endpointUrl,
            checks,
            cancellationToken,
            throwOnUnavailable: true);
    }

    private async Task<IReadOnlyList<AuthorizationDecision>> ExecuteCheckWithStorageTypedDenyAsync(
        ICerbosClient client,
        string endpointLabel,
        IReadOnlyList<AuthorizationRequest> checks,
        CancellationToken cancellationToken,
        bool throwOnUnavailable = false)
    {
        if (!checks.Any(IsStorageUploadCreateWithTypedFacts))
            return await ExecuteCheckAsync(client, endpointLabel, checks, cancellationToken, throwOnUnavailable);

        var results = Enumerable.Repeat(
            AuthorizationDecision.Deny(AuthorizationProviderMetadata.Cerbos),
            checks.Count).ToArray();
        var passthroughIndexes = new List<int>(checks.Count);
        var passthroughChecks = new List<AuthorizationRequest>(checks.Count);

        for (var index = 0; index < checks.Count; index++)
        {
            if (IsStorageUploadCreateWithTypedFacts(checks[index]))
                continue;

            passthroughIndexes.Add(index);
            passthroughChecks.Add(checks[index]);
        }

        if (passthroughChecks.Count == 0)
            return results;

        var passthroughResults = await ExecuteCheckAsync(
            client,
            endpointLabel,
            passthroughChecks,
            cancellationToken,
            throwOnUnavailable);

        for (var index = 0; index < passthroughIndexes.Count; index++)
        {
            results[passthroughIndexes[index]] = index < passthroughResults.Count
                ? passthroughResults[index]
                : AuthorizationDecision.Deny(AuthorizationProviderMetadata.Cerbos, AuthorizationDecisionReasonCodes.ProviderError);
        }

        return results;
    }

    private async Task<IReadOnlyList<AuthorizationDecision>> ExecuteCheckAsync(
        ICerbosClient client,
        string endpointLabel,
        IReadOnlyList<AuthorizationRequest> checks,
        CancellationToken cancellationToken,
        bool throwOnUnavailable = false)
    {
        var machineContext = _machinePrincipalAccessor.Current;
        var userId = _adminContext.UserId;

        if (userId is null && machineContext is null)
            userId = await _adminContext.ResolveUserIdAsync(cancellationToken);

        if (userId is null && machineContext is null)
        {
            _logger.LogWarning("Cerbos auth denied: no user id and no machine principal in admin context");
            return DenyAll(checks.Count, AuthorizationDecisionReasonCodes.MissingSubject);
        }

        var requestId = RequestId.Generate();
        var correlationId = Activity.Current?.Id ?? string.Empty;
        var principal = await _principalBuilder.BuildPrincipalAsync(userId, cancellationToken);

        var eventIds = ExtractEventIdsFromChecks(checks);
        if (eventIds.Count > 0 && userId.HasValue)
        {
            var tenantId = ResolveTenantIdFromChecks(checks);
            if (tenantId != Guid.Empty)
            {
                await _principalBuilder.EnrichWithEventAssignmentsAsync(
                    principal, userId.Value, tenantId, eventIds, cancellationToken);
            }
        }

        var resourceEntries = BuildResourceEntries(checks, machineContext is not null);

        _logger.LogDebug(
            "Cerbos gRPC batch request: {CheckCount} checks, requestId={RequestId} correlationId={CorrelationId}",
            checks.Count, requestId, correlationId);

        try
        {
            var request = CerbosCheckResourcesRequest
                .NewInstance()
                .WithRequestId(requestId)
                .WithPrincipal(principal)
                .WithResourceEntries(resourceEntries);

            var response = await client.CheckResourcesAsync(request, null);
            return BuildDecisionResults(checks, response, requestId, correlationId);
        }
        catch (Exception ex) when (ex is Grpc.Core.RpcException or OperationCanceledException)
        {
            _logger.LogError(
                "Cerbos PDP unreachable. Denying access for requestId={RequestId} correlationId={CorrelationId}. FailureType={FailureType}",
                requestId,
                correlationId,
                ex.GetType().Name);

            if (throwOnUnavailable)
                throw;

            return DenyAll(checks.Count, AuthorizationDecisionReasonCodes.ProviderUnavailable);
        }
    }

    private CerbosResourceEntry[] BuildResourceEntries(
        IReadOnlyList<AuthorizationRequest> checks,
        bool isMachine)
    {
        var ambientTenantId = _tenantContext.TenantId;

        return checks
            .Select(check =>
            {
                // Resolve tenant ID: prefer explicit scope from the check, fall back to ambient tenant context.
                var effectiveTenantId = !string.IsNullOrEmpty(check.Scope?.TenantId)
                    ? check.Scope.TenantId
                    : ambientTenantId != Guid.Empty
                        ? ambientTenantId.ToString()
                        : null;

                var entry = CerbosResourceEntry
                    .NewInstance(check.ResourceKind, check.ResourceId)
                    .WithActions(check.Action);

                if (_settings.UsePolicyScope && effectiveTenantId is not null)
                    entry = entry.WithScope(effectiveTenantId);

                // Map resource attributes to Cerbos AttributeValue types
                var attributes = TrustedAttributes(check);

                if (attributes is not null)
                {
                    foreach (var (key, value) in attributes)
                        entry = entry.WithAttribute(key, ToAttributeValue(value));
                }

                // Auto-enrich with tenantId when not explicitly provided in resource attributes.
                // Required for Cerbos derived role evaluation (tenant admin checks resource.attr.tenantId).
                if (effectiveTenantId is not null &&
                    (attributes is null || !attributes.ContainsKey("tenantId")))
                {
                    entry = entry.WithAttribute("tenantId", AttributeValue.StringValue(effectiveTenantId));
                }

                if (check.ResourceKind == ResourceKinds.RegistrationForm &&
                    (attributes is null || !attributes.ContainsKey("isMachine")))
                {
                    entry = entry.WithAttribute("isMachine", AttributeValue.BoolValue(isMachine));
                }

                return entry;
            })
            .ToArray();
    }

    private static HashSet<Guid> ExtractEventIdsFromChecks(IReadOnlyList<AuthorizationRequest> checks)
    {
        var eventIds = new HashSet<Guid>();
        foreach (var check in checks)
        {
            var attributes = TrustedAttributes(check);
            if (attributes is null)
                continue;

            if (attributes.TryGetValue("eventId", out var eventIdObj))
            {
                var eventIdStr = eventIdObj?.ToString();
                if (Guid.TryParse(eventIdStr, out var eventId))
                    eventIds.Add(eventId);
            }
        }
        return eventIds;
    }

    private Guid ResolveTenantIdFromChecks(IReadOnlyList<AuthorizationRequest> checks)
    {
        foreach (var check in checks)
        {
            if (!string.IsNullOrEmpty(check.Scope?.TenantId) &&
                Guid.TryParse(check.Scope.TenantId, out var scopedTenantId))
            {
                return scopedTenantId;
            }

            var attributes = TrustedAttributes(check);
            if (attributes?.TryGetValue("tenantId", out var tenantIdObj) == true)
            {
                var tenantIdStr = tenantIdObj?.ToString();
                if (Guid.TryParse(tenantIdStr, out var tenantId))
                    return tenantId;
            }
        }

        return _tenantContext.TenantId;
    }

    private IReadOnlyList<AuthorizationDecision> BuildDecisionResults(
        IReadOnlyList<AuthorizationRequest> checks,
        CheckResourcesResponse response,
        string requestId,
        string correlationId)
    {
        var decisions = new AuthorizationDecision[checks.Count];

        for (var i = 0; i < checks.Count; i++)
        {
            var check = checks[i];
            var resultEntry = FindResultEntry(response, check, i);

            if (resultEntry is not null && resultEntry.Actions.TryGetValue(check.Action, out var effect))
            {
                var isAllowed = effect == Effect.Allow;
                decisions[i] = isAllowed
                    ? AuthorizationDecision.Allow(AuthorizationProviderMetadata.Cerbos)
                    : AuthorizationDecision.Deny(AuthorizationProviderMetadata.Cerbos);
                _logger.LogDebug(
                    "Cerbos decision: effect={Effect} resource={Resource} action={Action} requestId={RequestId} correlationId={CorrelationId}",
                    effect, check.ResourceKind, check.Action, requestId, correlationId);
                continue;
            }

            _logger.LogWarning(
                "Cerbos decision missing. Default deny for resource={Resource} action={Action} requestId={RequestId} correlationId={CorrelationId}",
                check.ResourceKind, check.Action, requestId, correlationId);
            decisions[i] = AuthorizationDecision.Deny(AuthorizationProviderMetadata.Cerbos, AuthorizationDecisionReasonCodes.ProviderError);
        }

        return decisions;
    }

    /// <summary>
    /// Resource attributes sent to the PDP are projected from the closed fact catalog only, so a caller
    /// cannot influence <c>request.resource.attr.*</c> and therefore cannot influence a Cerbos rule.
    /// </summary>
    private static Dictionary<string, object>? TrustedAttributes(AuthorizationRequest request) =>
        AuthorizationFactAttributeProjection.ToAttributes(request.Facts);

    private static Cerbos.Api.V1.Response.CheckResourcesResponse.Types.ResultEntry? FindResultEntry(
        CheckResourcesResponse response,
        AuthorizationRequest check,
        int checkIndex)
    {
        if (checkIndex < response.Raw.Results.Count)
        {
            var positionalResult = response.Raw.Results[checkIndex];
            if (MatchesResult(positionalResult, check) && positionalResult.Actions.ContainsKey(check.Action))
                return positionalResult;
        }

        return response.Raw.Results.FirstOrDefault(result =>
            MatchesResult(result, check) &&
            result.Actions.ContainsKey(check.Action));
    }

    private static bool MatchesResult(
        Cerbos.Api.V1.Response.CheckResourcesResponse.Types.ResultEntry result,
        AuthorizationRequest check)
    {
        return result.Resource is { } resource &&
               string.Equals(resource.Id, check.ResourceId, StringComparison.Ordinal) &&
               string.Equals(resource.Kind, check.ResourceKind, StringComparison.Ordinal);
    }

    private static AuthorizationDecision[] DenyAll(int count, string reasonCode = AuthorizationDecisionReasonCodes.Denied)
    {
        return Enumerable.Repeat(AuthorizationDecision.Deny(AuthorizationProviderMetadata.Cerbos, reasonCode), count).ToArray();
    }

    /// <summary>
    /// Converts a CLR object to the Cerbos SDK <see cref="AttributeValue"/> type.
    /// </summary>
    internal static AttributeValue ToAttributeValue(object? value) => value switch
    {
        null => AttributeValue.NullValue(),
        bool b => AttributeValue.BoolValue(b),
        int n => AttributeValue.DoubleValue(n),
        long n => AttributeValue.DoubleValue(n),
        double d => AttributeValue.DoubleValue(d),
        float f => AttributeValue.DoubleValue(f),
        string s => AttributeValue.StringValue(s),
        IDictionary<string, object> dict => AttributeValue.MapValue(
            dict.ToDictionary(kvp => kvp.Key, kvp => ToAttributeValue(kvp.Value))),
        IEnumerable<object> list => AttributeValue.ListValue(
            list.Select(ToAttributeValue).ToArray()),
        _ => AttributeValue.StringValue(value.ToString() ?? string.Empty)
    };
}

/// <summary>
/// Configuration for connecting to the Cerbos PDP server via gRPC.
/// Bound from the "Cerbos" configuration section in appsettings.json.
/// </summary>
public class CerbosSettings
{
    public const string SectionName = "Cerbos";

    /// <summary>
    /// The Cerbos PDP gRPC endpoint (e.g., "https://cerbosgrpc.openislamu.org:443" for TLS,
    /// "http://localhost:3593" for plaintext). Protocol prefix determines TLS behavior.
    /// </summary>
    public string GrpcEndpoint { get; set; } = "http://localhost:3593";

    /// <summary>
    /// When true, uses plaintext gRPC (no TLS). Required for local development with
    /// <c>http://</c> endpoints. Production deployments should use TLS (<c>https://</c>)
    /// and leave this false.
    /// </summary>
    public bool PlaintextMode { get; set; } = true;

    /// <summary>
    /// When true, sends the tenant id as the Cerbos resource scope for scoped policy
    /// overrides. Leave false for the shared bundled policies; tenant isolation is
    /// still enforced through resource attributes.
    /// </summary>
    public bool UsePolicyScope { get; set; }
}

/// <summary>
/// Factory for creating and caching Cerbos gRPC clients per endpoint.
/// Used for BYO (Bring Your Own) Cerbos endpoints where each tenant may have
/// a different PDP. gRPC channels are long-lived and thread-safe, so caching is optimal.
/// </summary>
public interface ICerbosClientFactory
{
    ICerbosClient GetOrCreate(string grpcEndpoint);

    void Evict(string grpcEndpoint);
}

/// <summary>
/// Thread-safe factory that caches <see cref="ICerbosClient"/> instances per gRPC endpoint.
/// </summary>
public class CerbosClientFactory : ICerbosClientFactory
{
    private readonly ConcurrentDictionary<string, ICerbosClient> _clients = new();
    private readonly ILogger<CerbosClientFactory> _logger;

    public CerbosClientFactory(ILogger<CerbosClientFactory> logger)
    {
        _logger = logger;
    }

    public ICerbosClient GetOrCreate(string grpcEndpoint)
    {
        var normalizedEndpoint = GrpcEndpointNormalizer.Normalize(grpcEndpoint);

        return _clients.GetOrAdd(normalizedEndpoint, endpoint =>
        {
            _logger.LogInformation("Creating Cerbos gRPC client for BYO endpoint");
            var builder = CerbosClientBuilder
                .ForTarget(endpoint)
                .WithGrpcChannelOptions(CerbosGrpcChannelOptionsFactory.Create());

            if (endpoint.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                builder = builder.WithPlaintext();

            return builder.Build();
        });
    }

    public void Evict(string grpcEndpoint)
    {
        var normalizedEndpoint = GrpcEndpointNormalizer.Normalize(grpcEndpoint);

        if (string.IsNullOrWhiteSpace(normalizedEndpoint))
            return;

        if (_clients.TryRemove(normalizedEndpoint, out var client))
        {
            if (client is IDisposable disposable)
                disposable.Dispose();

            _logger.LogInformation("Evicted Cerbos gRPC client for BYO endpoint");
        }
    }
}
