using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.RegistrationOrders.Requests.Commands;
using Explore.Application.Services;
using Explore.Domain;
using NSubstitute;

namespace Event.Application.UnitTests.Services;

public sealed class TicketPurchaseActorOwnershipTests
{
    [Test]
    public async Task ImplicitPurchaserUsesTheAdmittedAccountActorIdentity()
    {
        var scenario = new Scenario();
        scenario.Ownership.Facts = new(scenario.ActorId, scenario.UserId, null, null);
        var result = await scenario.ResolveAsync();
        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Authority?.ActingAccountUserId).IsEqualTo(scenario.UserId);
        await Assert.That(result.Authority?.PurchaserActorId).IsEqualTo(scenario.ActorId);
        await Assert.That(result.Authority?.EnforcementKey).IsEqualTo($"account:{scenario.UserId:N}");
    }

    [Test]
    [Arguments("missing")]
    [Arguments("foreign-user")]
    [Arguments("foreign-tenant")]
    [Arguments("unauthenticated")]
    public async Task ImplicitPurchaserCannotCreateAuthorityFromUnownedFacts(string boundary)
    {
        var scenario = new Scenario();
        scenario.Ownership.Facts = new(scenario.ActorId,
            boundary == "foreign-user" ? Guid.CreateVersion7() : scenario.UserId, null, null);
        if (boundary == "missing") scenario.Ownership.Facts = null;
        if (boundary == "foreign-tenant") scenario.Ownership.TenantId = Guid.CreateVersion7();
        if (boundary == "unauthenticated") scenario.Current.IsAuthenticated.Returns(false);
        var result = await scenario.ResolveAsync();
        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Authority).IsNull();
        await Assert.That(result.FailureCode).IsEqualTo(TicketPurchaseFailureCodes.AuthorityUnavailable);
    }

    [Test]
    [Arguments("user", true)]
    [Arguments("user", false)]
    [Arguments("group", true)]
    [Arguments("group", false)]
    [Arguments("organization", true)]
    [Arguments("organization", false)]
    public async Task ExplicitPurchaserRetainsItsExistingOwnershipRules(string owner, bool controls)
    {
        var scenario = new Scenario();
        Guid group = Guid.CreateVersion7(), organization = Guid.CreateVersion7();
        var actor = new Actor
        {
            Id = scenario.ActorId,
            ActorType = null!,
            Pii = null!,
            UserId = owner == "user" ? (controls ? scenario.UserId : Guid.CreateVersion7()) : null,
            GroupId = owner == "group" ? group : null,
            OrganizationId = owner == "organization" ? organization : null
        };
        scenario.Actors.GetPublicActorProfileByTenantAsync(scenario.TenantId, actor.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Actor?>(actor));
        scenario.Groups.Exists(group, scenario.UserId).Returns(controls);
        scenario.Organizations.Exists(organization, scenario.UserId).Returns(controls);
        // A distinct implicit actor must never replace the explicitly requested actor.
        scenario.Ownership.Facts = new(Guid.CreateVersion7(), scenario.UserId, null, null);

        var result = await scenario.ResolveAsync(actor.Id);
        await Assert.That(result.IsSuccess).IsEqualTo(controls);
        await Assert.That(result.Authority?.PurchaserActorId).IsEqualTo(controls ? actor.Id : (Guid?)null);
        await Assert.That(result.Authority?.ActingAccountUserId).IsEqualTo(controls ? scenario.UserId : (Guid?)null);
    }

    private sealed class Scenario
    {
        public Guid UserId { get; } = Guid.CreateVersion7();
        public Guid TenantId { get; } = Guid.CreateVersion7();
        public Guid ActorId { get; } = Guid.CreateVersion7();
        public ICurrentUserService Current { get; } = Substitute.For<ICurrentUserService>();
        public IActorRepository Actors { get; } = Substitute.For<IActorRepository>();
        public IGroupMemberRepository Groups { get; } = Substitute.For<IGroupMemberRepository>();
        public IOrganizationMemberRepository Organizations { get; } = Substitute.For<IOrganizationMemberRepository>();
        public OwnershipRows Ownership { get; }

        public Scenario()
        {
            Current.UserId.Returns(UserId);
            Current.IsAuthenticated.Returns(true);
            Ownership = new OwnershipRows(UserId, TenantId);
        }

        public Task<TicketPurchaseAuthorityResolution> ResolveAsync(Guid? requestedActor = null) =>
            new TicketPurchaseAuthorityResolver(Current, new SelectedTenant(TenantId), Actors, Ownership,
                Groups, Organizations, Substitute.For<IRegistrationInventoryRepository>())
                .ResolveAsync(new(Guid.CreateVersion7(), Guid.CreateVersion7(),
                    TicketPurchaseAccessMode.AuthenticatedAccount, requestedActor), CancellationToken.None);
    }

    private sealed class OwnershipRows(Guid userId, Guid tenantId) : IActorOwnershipReader
    {
        public Guid TenantId { get; set; } = tenantId;
        public ActorOwnershipFacts? Facts { get; set; }
        public Task<ActorOwnershipFacts?> ReadForUserAsync(Guid user, Guid tenant, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(user == userId && tenant == TenantId ? Facts : null);
        }
    }

    private sealed record SelectedTenant(Guid TenantId) : ITenantContext;
}
