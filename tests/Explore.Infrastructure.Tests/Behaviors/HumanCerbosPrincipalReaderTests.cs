using Explore.Application.Authentication;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Users.Requests.Queries;
using Explore.Infrastructure.Services;
using Explore.Infrastructure.Tests.Fixtures;
using Explore.Persistence.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Infrastructure.Tests.Behaviors;

public sealed class HumanCerbosPrincipalReaderTests
{
    [Test]
    [Arguments("current")]
    [Arguments("foreign_user")]
    [Arguments("signed_out")]
    [Arguments("deleted_user")]
    public async Task ReadsOnlyTheSignedPersistedOriginalHuman(string state)
    {
        Guid user = Guid.CreateVersion7(), tenant = Guid.CreateVersion7();
        await using var fixture = new CurrentHumanPrincipalFixture(user, tenant);
        await fixture.InitializeAsync();
        if (state == "signed_out")
            fixture.SignOut();
        if (state == "deleted_user")
            await fixture.Context.Users.Where(row => row.Id == user)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsDeleted, true));
        var reader = Reader(fixture);
        if (state == "current")
        {
            var facts = await reader.ReadAsync(user, default);
            await Assert.That(facts.PrincipalId).IsEqualTo(user);
            await Assert.That(facts.SelectedTenantId).IsEqualTo(tenant);
            await Assert.That(facts.Users.Single().IsDeleted).IsFalse();
        }
        else
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                reader.ReadAsync(state == "foreign_user" ? Guid.CreateVersion7() : user, default));
    }

    [Test]
    public async Task IdleObservationRejectsAnAlreadyOpenTransaction()
    {
        Guid user = Guid.CreateVersion7();
        await using var fixture = new CurrentHumanPrincipalFixture(user, Guid.CreateVersion7());
        await fixture.InitializeAsync();
        var reader = Reader(fixture);
        await using var transaction = await fixture.Context.Database.BeginTransactionAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => reader.ReadAsync(user, default));
        await transaction.RollbackAsync();
    }

    private static HumanCerbosPrincipalReader Reader(CurrentHumanPrincipalFixture fixture)
    {
        var preparation = new SignedSubjectPreparation(fixture.Services);
        return new(preparation,
            fixture.Services.GetRequiredService<IHttpContextAccessor>(),
            fixture.Services.GetRequiredService<IHumanAuthenticationLifetimeObserver>(),
            fixture.Machine, fixture.Tenant, TimeProvider.System,
            () => new HumanPrincipalSourceFence(fixture.Context,
                preparation.Subject ?? throw new UnauthorizedAccessException("A signed subject is required."),
                PolicySourceSetPurpose.RuntimeTenant));
    }

    private sealed class SignedSubjectPreparation(IServiceProvider services) : IHumanCommitScopePreparation
    {
        public HumanCommitSubject? Subject { get; private set; }

        public async ValueTask PrepareAsync(CancellationToken cancellationToken)
        {
            Subject ??= await HumanCommitSubject.ResolveAsync(
                services.GetRequiredService<IHttpContextAccessor>().HttpContext!.User,
                services.GetRequiredService<IQueryHandler<ResolveCurrentUserIdByIdentityRequest, Guid?>>(),
                cancellationToken);
        }
    }
}
