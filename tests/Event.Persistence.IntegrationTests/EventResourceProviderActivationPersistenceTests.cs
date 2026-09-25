using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests;

[ClassDataSource<EventResourcePersistenceTests.TestDatabase>(Shared = SharedType.PerClass)]
[NotInParallel("EventResourceProviderActivation")]
public sealed class EventResourceProviderActivationPersistenceTests(
    EventResourcePersistenceTests.TestDatabase database)
{
    private static readonly DateTime Now = new(2026, 9, 22, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task DeploymentAliasesObserveOneFreshFenceDespiteTrackedOldAuthority()
    {
        var deployment = EventResourceProviderActivation.Create(Guid.CreateVersion7(), Now);
        var operation = Guid.CreateVersion7();
        var epoch = deployment.BeginOperation(operation, Now);
        deployment.TryActivate(operation, epoch, true, 2, 2, Now);
        await using (var seed = database.CreateContext())
        {
            seed.Set<EventResourceProviderActivation>().Add(deployment);
            await seed.SaveChangesAsync();
        }

        await using var firstAlias = database.CreateContext();
        firstAlias.TenantContext = new AliasTenant(Guid.CreateVersion7());
        var tracked = await firstAlias.Set<EventResourceProviderActivation>().SingleAsync(row => row.Id == deployment.Id);
        await using (var secondAlias = database.CreateContext())
        {
            secondAlias.TenantContext = new AliasTenant(Guid.CreateVersion7());
            var current = await secondAlias.Set<EventResourceProviderActivation>().SingleAsync(row => row.Id == deployment.Id);
            current.BeginOperation(Guid.CreateVersion7(), Now);
            await secondAlias.SaveChangesAsync();
        }

        var fresh = (await new EventResourceProviderActivationRepository(firstAlias)
            .GetAsync(deployment.Id, default))!;
        await Assert.That(tracked.HasActiveAuthority(epoch, operation)).IsTrue();
        await Assert.That(fresh.Epoch).IsEqualTo(epoch + 1);
        await Assert.That(fresh.State).IsEqualTo(EventResourceProviderActivationStateEnum.Transitioning);
        await Assert.That(fresh.HasActiveAuthority(epoch, operation)).IsFalse();
    }

    [Test]
    public async Task StaleActivationCannotCommitOverANewerTransition()
    {
        var deployment = EventResourceProviderActivation.Create(Guid.CreateVersion7(), Now);
        var operation = Guid.CreateVersion7();
        var epoch = deployment.BeginOperation(operation, Now);
        await using (var seed = database.CreateContext())
        {
            seed.Set<EventResourceProviderActivation>().Add(deployment);
            await seed.SaveChangesAsync();
        }

        await using var first = database.CreateContext();
        await using var stale = database.CreateContext();
        var current = await first.Set<EventResourceProviderActivation>().SingleAsync(row => row.Id == deployment.Id);
        var previous = await stale.Set<EventResourceProviderActivation>().SingleAsync(row => row.Id == deployment.Id);
        current.BeginOperation(Guid.CreateVersion7(), Now);
        await first.SaveChangesAsync();
        await Assert.That(previous.TryActivate(operation, epoch, true, 1, 1, Now)).IsTrue();
        await Assert.That(() => stale.SaveChangesAsync()).Throws<DbUpdateConcurrencyException>();

        await using var verification = database.CreateContext();
        var persisted = await verification.Set<EventResourceProviderActivation>().AsNoTracking()
            .SingleAsync(row => row.Id == deployment.Id);
        await Assert.That(persisted.Epoch).IsEqualTo(epoch + 1);
        await Assert.That(persisted.State).IsEqualTo(EventResourceProviderActivationStateEnum.Transitioning);
    }

    private sealed record AliasTenant(Guid TenantId) : ITenantContext;
}
