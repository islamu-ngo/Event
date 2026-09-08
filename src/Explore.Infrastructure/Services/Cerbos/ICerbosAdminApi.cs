using System.Threading;
using System.Threading.Tasks;
using Refit;

namespace Explore.Infrastructure.Services;

/// <summary>
/// Refit interface for Cerbos Admin API endpoints.
/// Used for pushing policies and schemas, and reloading instances.
/// </summary>
internal interface ICerbosAdminApi
{
    [Post("/admin/schema")]
    Task<ApiResponse<string>> PushSchemasAsync(
        [Header("Authorization")] string authorization,
        [Body(buffered: true)] CerbosSchemaBatchRequest request,
        CancellationToken cancellationToken = default);

    [Post("/admin/policy")]
    Task<ApiResponse<string>> PushPoliciesAsync(
        [Header("Authorization")] string authorization,
        [Body(buffered: true)] CerbosPolicyBatchRequest request,
        CancellationToken cancellationToken = default);

    [Get("/admin/store/reload")]
    Task<ApiResponse<string>> ReloadInstanceAsync(
        [Header("Authorization")] string authorization,
        [AliasAs("wait")] string wait = "true",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the policy identifiers currently held in the store.
    /// <para>
    /// Identifiers look like <c>resource.islamuevent_event.vdefault</c>. An empty store omits the
    /// <c>policyIds</c> field entirely rather than returning an empty array, so a <c>null</c>
    /// <see cref="CerbosPolicyListResponse.PolicyIds"/> means "empty", not "malformed".
    /// </para>
    /// </summary>
    [Get("/admin/policies")]
    Task<ApiResponse<CerbosPolicyListResponse>> ListPoliciesAsync(
        [Header("Authorization")] string authorization,
        [AliasAs("includeDisabled")] string includeDisabled = "true",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches stored policies by identifier, including the per-policy content hash Cerbos computes.
    /// <para>
    /// Cerbos exposes no store-wide revision, but every policy it returns carries
    /// <c>metadata.hash</c>, and that hash changes when a policy body is edited even if its identifier
    /// does not. Folding those hashes therefore yields a real content revision: out-of-band edits to a
    /// policy already in the store are detectable, not just missing or extra policies.
    /// </para>
    /// <para>
    /// The response does not preserve request order, so callers must sort before folding.
    /// </para>
    /// </summary>
    [Get("/admin/policy")]
    Task<ApiResponse<CerbosPolicyFetchResponse>> GetPoliciesAsync(
        [Header("Authorization")] string authorization,
        [AliasAs("id")][Query(CollectionFormat.Multi)] IEnumerable<string> policyIds,
        CancellationToken cancellationToken = default);
}

/// <summary>Response body of <c>GET /admin/policies</c>.</summary>
internal sealed record CerbosPolicyListResponse
{
    /// <summary>Policy identifiers currently in the store, or <c>null</c> when the store is empty.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("policyIds")]
    public IReadOnlyList<string>? PolicyIds { get; init; }
}

/// <summary>Response body of <c>GET /admin/policy</c>.</summary>
internal sealed record CerbosPolicyFetchResponse
{
    [System.Text.Json.Serialization.JsonPropertyName("policies")]
    public IReadOnlyList<CerbosStoredPolicy>? Policies { get; init; }
}

/// <summary>A single stored policy, reduced to the identity fields drift detection needs.</summary>
internal sealed record CerbosStoredPolicy
{
    [System.Text.Json.Serialization.JsonPropertyName("metadata")]
    public CerbosStoredPolicyMetadata? Metadata { get; init; }
}

/// <summary>
/// Store metadata Cerbos attaches to a policy it returns.
/// </summary>
internal sealed record CerbosStoredPolicyMetadata
{
    /// <summary>
    /// Cerbos's own content hash of the policy, rendered as an unsigned 64-bit integer in a JSON string.
    /// Treated as an opaque token: only equality across observations is meaningful.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("hash")]
    public string? Hash { get; init; }

    /// <summary>The store identifier this policy was filed under.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("storeIdentifier")]
    public string? StoreIdentifier { get; init; }
}
