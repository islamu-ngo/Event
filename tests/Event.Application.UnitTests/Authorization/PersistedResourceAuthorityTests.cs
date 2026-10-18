using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.DTOs.Event;
using Explore.Application.Exceptions;
using Explore.Application.Features.Events.Requests.Commands;

namespace Event.Application.UnitTests.Authorization;

public sealed class PersistedResourceAuthorityTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task EventCreationDerivesTenantAndOwnerFactsFromOriginalServerContext(bool forgedFacts)
    {
        Guid tenantId = Guid.CreateVersion7();
        Guid organizationId = Guid.CreateVersion7();
        Guid groupId = Guid.CreateVersion7();
        var command = new CreateEventCommand
        {
            EventDto = new CreateEventDto
            {
                Title = "Selected tenant event",
                OrganizationId = organizationId,
                GroupId = groupId,
                ParticipationConfiguration = new()
            }
        };
        var resolver = new AuthorizationResourceContextResolver(tenantContext: new Tenant(tenantId));
        IAuthorizationFacts? declared = forgedFacts
            ? new PreCreateAuthorizationFacts(Guid.CreateVersion7(), Guid.CreateVersion7(),
                Guid.CreateVersion7(), Guid.CreateVersion7())
            : ((ISecureRequest)command).AuthorizationFacts;

        var result = await resolver.ResolveAsync(command, ResourceKinds.Event,
            AuthorizationActions.Create, CreateEventCommand.PreCreateResourceId, declared, CancellationToken.None);

        await Assert.That(result.ResourceId).IsEqualTo(CreateEventCommand.PreCreateResourceId);
        await Assert.That(result.Facts).IsEqualTo(new PreCreateAuthorizationFacts(
            tenantId, OrganizationId: organizationId, GroupId: groupId));
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task EventCreationWithoutSelectedTenantFailsClosed(bool emptyTenant)
    {
        var command = new CreateEventCommand
        {
            EventDto = new CreateEventDto { Title = "No selected tenant", ParticipationConfiguration = new() }
        };
        var resolver = new AuthorizationResourceContextResolver(
            tenantContext: emptyTenant ? new Tenant(Guid.Empty) : null);

        await Assert.ThrowsAsync<AuthorizationException>(() => resolver.ResolveAsync(
            command, ResourceKinds.Event, AuthorizationActions.Create, CreateEventCommand.PreCreateResourceId,
            ((ISecureRequest)command).AuthorizationFacts, CancellationToken.None));
    }

    [Test]
    public async Task EventCreationCannotUseAnExistingResourceIdentity()
    {
        Guid tenantId = Guid.CreateVersion7();
        var command = new CreateEventCommand
        {
            EventDto = new CreateEventDto { Title = "Invalid target", ParticipationConfiguration = new() }
        };
        var resolver = new AuthorizationResourceContextResolver(tenantContext: new Tenant(tenantId));

        await Assert.ThrowsAsync<AuthorizationException>(() => resolver.ResolveAsync(
            command, ResourceKinds.Event, AuthorizationActions.Create, Guid.CreateVersion7().ToString("D"),
            new PreCreateAuthorizationFacts(tenantId), CancellationToken.None));
    }

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
