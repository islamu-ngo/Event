using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Exceptions;

namespace Event.Application.UnitTests.Authorization;

public sealed class PersistedResourceAuthorityTests
{
    [Test]
    [Arguments(ResourceKinds.Event)]
    [Arguments(ResourceKinds.EventSession)]
    [Arguments(ResourceKinds.OrganizationMember)]
    [Arguments(ResourceKinds.StorageObject)]
    [Arguments(ResourceKinds.CustomPropertyProjection)]
    public async Task MissingPersistedOwnerCannotFallBackToDeclaredAuthority(string kind)
    {
        Guid tenantId = Guid.CreateVersion7();
        Guid targetId = Guid.CreateVersion7();
        var resolver = new AuthorizationResourceContextResolver(tenantContext: new Tenant(tenantId));
        IAuthorizationFacts facts = kind == ResourceKinds.CustomPropertyProjection
            ? new CustomPropertyProjectionAuthorizationFacts(tenantId, targetId)
            : new TenantScopedAuthorizationFacts(tenantId);

        await Assert.ThrowsAsync<AuthorizationException>(() => resolver.ResolveAsync(
            new object(), kind, AuthorizationActions.Update, targetId.ToString("D"),
            facts, CancellationToken.None));
    }

    [Test]
    public async Task PreCreateFactsCannotAuthorizeAnExistingResourceUpdate()
    {
        Guid tenantId = Guid.CreateVersion7();
        var resolver = new AuthorizationResourceContextResolver(tenantContext: new Tenant(tenantId));

        await Assert.ThrowsAsync<AuthorizationException>(() => resolver.ResolveAsync(
            new object(), ResourceKinds.Event, AuthorizationActions.Update,
            Guid.CreateVersion7().ToString("D"), new PreCreateAuthorizationFacts(tenantId),
            CancellationToken.None));
    }

    [Test]
    public async Task PreCreateScopeMustMatchTheCurrentTenant()
    {
        var resolver = new AuthorizationResourceContextResolver(
            tenantContext: new Tenant(Guid.CreateVersion7()));

        await Assert.ThrowsAsync<AuthorizationException>(() => resolver.ResolveAsync(
            new object(), ResourceKinds.Event, AuthorizationActions.Create, "new",
            new PreCreateAuthorizationFacts(Guid.CreateVersion7()), CancellationToken.None));
    }

    [Test]
    public async Task CancelledResolutionCannotReturnDeclaredAuthority()
    {
        Guid tenantId = Guid.CreateVersion7();
        var resolver = new AuthorizationResourceContextResolver(tenantContext: new Tenant(tenantId));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => resolver.ResolveAsync(
            new object(), ResourceKinds.Event, AuthorizationActions.Create, "new",
            new PreCreateAuthorizationFacts(tenantId), cancellation.Token));
    }

    [Test]
    public async Task ValidNewEventRetainsItsExactTenantCreationScope()
    {
        Guid tenantId = Guid.CreateVersion7();
        var facts = new PreCreateAuthorizationFacts(tenantId);
        var resolver = new AuthorizationResourceContextResolver(tenantContext: new Tenant(tenantId));

        var result = await resolver.ResolveAsync(new object(), ResourceKinds.Event,
            AuthorizationActions.Create, "new", facts, CancellationToken.None);

        await Assert.That(result.Facts).IsEqualTo(facts);
    }

    [Test]
    public async Task DeclaredClaimantCannotReplacePersistedClaimOwnership()
    {
        Guid tenantId = Guid.CreateVersion7();
        Guid claimId = Guid.CreateVersion7();
        var resolver = new AuthorizationResourceContextResolver(tenantContext: new Tenant(tenantId));
        var facts = new EventOrganizerClaimAuthorizationFacts(tenantId, Guid.CreateVersion7(),
            claimId, Guid.CreateVersion7(), Guid.CreateVersion7(), null, null, "PENDING");

        await Assert.ThrowsAsync<AuthorizationException>(() => resolver.ResolveAsync(
            new object(), ResourceKinds.EventOrganizerClaim, AuthorizationActions.Events.WithdrawOrganizerClaim,
            claimId.ToString("D"), facts, CancellationToken.None));
    }

    private sealed record Tenant(Guid TenantId) : ITenantContext;
}
