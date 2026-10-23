using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Users.Handlers.Commands;
using Explore.Application.Features.Users.Requests.Commands;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace ApplicationUnitTests.Features.Users;

public sealed class TenantPreferenceOwnershipTests
{
    [Test]
    [Arguments(true, true, true)]
    [Arguments(false, true, true)]
    [Arguments(true, false, true)]
    [Arguments(true, true, false)]
    public async Task PreferenceMutationRequiresAuthenticatedResolvedSelf(bool authenticated, bool sameSubject, bool validTarget)
    {
        Guid subject = Guid.CreateVersion7();
        Guid tenant = validTarget ? Guid.CreateVersion7() : Guid.Empty;
        var identity = Substitute.For<IUserContext>();
        identity.IsAuthenticated.Returns(authenticated);
        var authority = Substitute.For<IAdminContext>();
        authority.ResolveUserIdAsync(Arg.Any<CancellationToken>())
            .Returns(sameSubject ? subject : Guid.CreateVersion7());
        var membership = new Memberships();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHybridCache();
        using var provider = services.BuildServiceProvider();
        var handler = new UpdateUserLastActiveTenantCommandHandler(membership,
            provider.GetRequiredService<HybridCache>(), identity, authority);

        bool result = await handler.ExecuteAsync(new(subject, tenant));

        bool admitted = authenticated && sameSubject && validTarget;
        await Assert.That(result).IsEqualTo(admitted);
        await Assert.That(membership.LastTenant).IsEqualTo(admitted ? tenant : (Guid?)null);
    }

    private sealed class Memberships : IHumanTenantPreferencePreparation
    {
        internal Guid? LastTenant { get; private set; }
        public IHumanTenantPreferenceMutation? CurrentMutation => null;

        public Task<bool> ExecuteAsync(UpdateUserLastActiveTenantCommand command, CancellationToken cancellationToken)
        {
            LastTenant = command.TenantId;
            return Task.FromResult(true);
        }

    }
}
