// Configurable to allow-all or deny-all for testing endpoint authorization behavior.

using Explore.Application.Contracts.Infrastructure;

namespace Event.Api.IntegrationTests.Fixtures;

/// <summary>
/// Stub authorization provider for integration tests.
/// Allows configuring per-action allow/deny behavior.
/// </summary>
public class StubAuthorizationProvider : IAuthorizationProvider
{
    /// <summary>
    /// When true, all authorization checks return allowed. When false, all return denied.
    /// </summary>
    public bool AllowAll { get; set; } = true;

    /// <summary>
    /// Optional predicate for fine-grained control over which checks pass.
    /// When set, overrides AllowAll for each individual check.
    /// </summary>
    public Func<AuthorizationRequest, bool>? CheckPredicate { get; set; }

    public async Task<AuthorizationDecision> AuthorizeAsync(
        AuthorizationRequest request,
        CancellationToken cancellationToken = default)
    {
        var allowed = (await IsAllowedBatchAsync([request], cancellationToken))[0];
        return allowed
            ? AuthorizationDecision.Allow(AuthorizationProviderMetadata.Local)
            : AuthorizationDecision.Deny(AuthorizationProviderMetadata.Local);
    }

    public async Task<IReadOnlyList<AuthorizationDecision>> AuthorizeBatchAsync(
        IReadOnlyList<AuthorizationRequest> requests,
        CancellationToken cancellationToken = default) =>
        (await IsAllowedBatchAsync(requests, cancellationToken))
            .Select(allowed => allowed
                ? AuthorizationDecision.Allow(AuthorizationProviderMetadata.Local)
                : AuthorizationDecision.Deny(AuthorizationProviderMetadata.Local))
            .ToArray();

    private Task<IReadOnlyList<bool>> IsAllowedBatchAsync(IReadOnlyList<AuthorizationRequest> checks,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<bool> results = CheckPredicate is not null
            ? checks.Select(check => CheckPredicate(check)).ToList()
            : checks.Select(_ => AllowAll).ToList();

        return Task.FromResult(results);
    }
}
