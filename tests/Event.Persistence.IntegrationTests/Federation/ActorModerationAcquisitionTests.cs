using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Persistence.IntegrationTests.Federation;

public sealed class ActorModerationAcquisitionTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task TrackedModerationReadPreservesStateAndAuditWithoutHydratingPrivateProfile(bool rollback)
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        ExploreDbContext context = fixture.Context;
        string originalProfile = await context.Set<ActorPii>()
            .Where(row => row.ActorId == fixture.ActorId).Select(row => row.DisplayName).SingleAsync();
        DateTime createdAt = await context.Actors
            .Where(row => row.Id == fixture.ActorId).Select(row => row.CreatedAt).SingleAsync();
        DateTime moderatedAt = DateTime.SpecifyKind(createdAt.AddMinutes(1), DateTimeKind.Utc);
        context.ChangeTracker.Clear();
        IActorRepository repository = fixture.Services.GetRequiredService<IActorRepository>();
        var unitOfWork = new EfCoreUnitOfWork(context);

        async Task Mutate(CancellationToken token)
        {
            Actor? actor = await repository.GetForModerationAsync(fixture.ActorId, token);
            await Assert.That(actor).IsNotNull();
            await Assert.That(actor?.Pii).IsNull();
            await Assert.That(context.ChangeTracker.Entries<ActorPii>()).IsEmpty();
            Guid originalStamp = actor!.ConcurrencyStamp;
            actor.Suspend("spam", moderatedAt, fixture.UserId);
            await repository.Update(actor);
            await Assert.That(actor.ConcurrencyStamp).IsNotEqualTo(originalStamp);
            if (rollback)
                throw new ModerationRollbackException();
        }

        if (rollback)
            await Assert.ThrowsAsync<ModerationRollbackException>(() =>
                unitOfWork.ExecuteInTransactionAsync(Mutate));
        else
            await unitOfWork.ExecuteInTransactionAsync(Mutate);

        context.ChangeTracker.Clear();
        bool suspended = await context.Actors.IgnoreAutoIncludes()
            .Where(row => row.Id == fixture.ActorId).Select(row => row.IsSuspended).SingleAsync();
        int audits = await context.Set<ActorModerationRecord>()
            .CountAsync(row => row.ActorId == fixture.ActorId);
        string persistedProfile = await context.Set<ActorPii>()
            .Where(row => row.ActorId == fixture.ActorId).Select(row => row.DisplayName).SingleAsync();
        await Assert.That(suspended).IsEqualTo(!rollback);
        await Assert.That(audits).IsEqualTo(rollback ? 0 : 1);
        await Assert.That(persistedProfile).IsEqualTo(originalProfile);
        await Assert.That(context.ChangeTracker.Entries<ActorPii>()).IsEmpty();
    }

    private sealed class ModerationRollbackException : Exception;
}
