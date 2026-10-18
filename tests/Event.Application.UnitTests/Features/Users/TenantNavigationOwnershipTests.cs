using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Application.Features.Users.Handlers.Queries;
using Explore.Application.Features.Users.Requests.Queries;
using Explore.Domain;
using NSubstitute;

namespace ApplicationUnitTests.Features.Users;

public sealed class TenantNavigationOwnershipTests
{
    [Test]
    [Arguments(false, true, false)]
    [Arguments(true, false, false)]
    [Arguments(true, true, true)]
    public async Task ForeignOrUnresolvedRequestsCannotReadNavigation(
        bool authenticated, bool sameSubject, bool emptySubject)
    {
        Guid subject = emptySubject ? Guid.Empty : Guid.CreateVersion7();
        var memberships = Substitute.For<IUserTenantMembershipRepository>();
        var handler = Handler(subject, authenticated, sameSubject,
            new Navigation(null, refuseRead: true), memberships);

        await Assert.ThrowsAsync<AuthorizationException>(() =>
            handler.QueryAsync(new ResolveUserTenantRedirectionRequest(subject)));
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task MissingOrDeletedUserCannotEnumerateMemberships(bool deleted)
    {
        Guid subject = Guid.CreateVersion7();
        var memberships = Substitute.For<IUserTenantMembershipRepository>();
        memberships.GetActiveTenantsForUserAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<List<TenantUser>>(
                new InvalidOperationException("A missing user cannot enumerate memberships.")));
        var handler = Handler(subject, true, true,
            new Navigation(deleted ? new User { Id = subject, IsDeleted = true, Pii = null! } : null), memberships);

        await Assert.ThrowsAsync<AuthorizationException>(() =>
            handler.QueryAsync(new ResolveUserTenantRedirectionRequest(subject)));
    }

    [Test]
    [Arguments(0, false)]
    [Arguments(1, false)]
    [Arguments(2, false)]
    [Arguments(2, true)]
    public async Task NavigationUsesOnlyCurrentMemberships(int count, bool rememberedMember)
    {
        Guid subject = Guid.CreateVersion7();
        Guid remembered = Guid.CreateVersion7();
        DateTime joined = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var rows = Enumerable.Range(0, count).Select(index => new TenantUser
        {
            Id = Guid.CreateVersion7(),
            UserId = subject,
            User = null!,
            TenantId = index == 0 && rememberedMember ? remembered : Guid.CreateVersion7(),
            JoinedAt = joined.AddDays(index),
            Tenant = new Tenant
            {
                Id = Guid.CreateVersion7(),
                FullName = "Tenant",
                Slug = $"tenant-{index}",
                TenantStatus = null!
            }
        }).ToList();
        var memberships = Substitute.For<IUserTenantMembershipRepository>();
        memberships.GetActiveTenantsForUserAsync(subject, Arg.Any<CancellationToken>()).Returns(rows);
        var handler = Handler(subject, true, true,
            new Navigation(new User { Id = subject, LastActiveTenantId = remembered, Pii = null! }), memberships);

        var result = await handler.QueryAsync(new ResolveUserTenantRedirectionRequest(subject));

        var expected = count == 0 ? null : rememberedMember ? rows[0] : rows[^1];
        await Assert.That(result.TenantId).IsEqualTo(expected?.TenantId);
        await Assert.That(result.TenantSlug).IsEqualTo(expected?.Tenant.Slug);
        await Assert.That(result.HasMultipleTenants).IsEqualTo(count > 1);
    }

    private static ResolveUserTenantRedirectionRequestHandler Handler(Guid subject, bool authenticated,
        bool sameSubject, IUserTenantNavigationRepository navigation, IUserTenantMembershipRepository memberships)
    {
        var identity = Substitute.For<IUserContext>();
        identity.IsAuthenticated.Returns(authenticated);
        var authority = Substitute.For<IAdminContext>();
        authority.ResolveUserIdAsync(Arg.Any<CancellationToken>())
            .Returns(sameSubject ? subject : Guid.CreateVersion7());
        return new(memberships, navigation, identity, authority);
    }

    private sealed class Navigation(User? user, bool refuseRead = false) : IUserTenantNavigationRepository
    {
        public Task<User?> GetNavigationUserAsync(Guid userId, CancellationToken cancellationToken) =>
            refuseRead ? throw new InvalidOperationException("Unresolved identity reached private navigation data.")
                : Task.FromResult(user is not null && user.Id == userId ? user : null);
    }
}
