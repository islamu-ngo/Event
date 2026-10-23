using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Federation;

public sealed partial class EventDiscoveryEnrollmentProducerTests
{
    [Test]
    public async Task AddedOrganizationPiiWaitsForDiscoveryBeforePlanningItsExistingActorAudience()
    {
        await using var store = await Store.CreateAsync();
        Guid organizationId = Guid.CreateVersion7();
        Guid actorId = Guid.CreateVersion7();
        Guid seriesId = Guid.CreateVersion7();
        long originalEpoch;
        await using (var seed = store.Open(store.TenantId))
        {
            // A principal can survive without its hard-deleteable PII extension.
            // Create its genuine related actor and audience; never substitute a foreign key.
            seed.Organizations.Add(new Organization { Id = organizationId, Pii = null! });
            seed.Actors.Add(new Actor
            {
                Id = actorId,
                OrganizationId = organizationId,
                ActorTypeId = (int)ActorTypeEnum.Organization,
                ActorType = null!,
                Pii = new ActorPii { ActorId = actorId, DisplayName = "Organization audience" }
            });
            seed.EventSeries.Add(new EventSeries
            {
                Id = seriesId,
                TenantId = store.TenantId,
                ActorId = actorId,
                Title = "Existing organization audience",
                IsPublished = true,
                VisibilityTypeId = (int)VisibilityTypeEnum.Public,
                VisibilityType = null!
            });
            await seed.SaveChangesAsync();
            await Assert.That(await seed.Set<OrganizationPii>().AnyAsync(row => row.OrganizationId == organizationId)).IsFalse();
            originalEpoch = await seed.Set<EventDiscoveryRevision>().Select(row => row.DisclosureEpoch).SingleAsync();
        }

        var observation = new LocalWriterObservation();
        await using var writer = store.Open(store.TenantId,
            new LocalWriterTransactionObserver(observation), new LocalWriterCommandObserver(observation));
        observation.Bind(writer, store.TenantId);
        observation.AllowCommit.TrySetResult();
        writer.Set<OrganizationPii>().Add(new OrganizationPii
        {
            OrganizationId = organizationId,
            FullName = "Restored organization contact"
        });
        await using var holder = store.Open(store.TenantId);
        await using var held = await new EventDiscoveryEnrollmentGate(holder).AcquireAsync();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Task<int> writing = writer.SaveChangesAsync(cancellation.Token);
        bool beganWhileHeld;
        int commandsWhileHeld;
        try
        {
            await Task.WhenAny(observation.Begin.Task, Task.Delay(TimeSpan.FromMilliseconds(150), cancellation.Token));
            beganWhileHeld = observation.Begin.Task.IsCompleted;
            commandsWhileHeld = observation.Commands;
            await held.DisposeAsync();
            await Assert.That(await writing).IsGreaterThan(0);
        }
        finally
        {
            await held.DisposeAsync();
            cancellation.Cancel();
            await writing;
        }

        await using var reader = store.Open(store.TenantId);
        await Assert.That(await reader.Organizations.IgnoreAutoIncludes().AnyAsync(row => row.Id == organizationId)).IsTrue();
        await Assert.That(await reader.Actors.IgnoreAutoIncludes()
            .AnyAsync(row => row.Id == actorId && row.OrganizationId == organizationId)).IsTrue();
        await Assert.That(await reader.EventSeries.IgnoreAutoIncludes()
            .AnyAsync(row => row.Id == seriesId && row.ActorId == actorId && row.TenantId == store.TenantId)).IsTrue();
        await Assert.That((await reader.Set<OrganizationPii>().SingleAsync(row => row.OrganizationId == organizationId)).FullName)
            .IsEqualTo("Restored organization contact");
        var revision = await reader.Set<EventDiscoveryRevision>().SingleAsync();
        await Assert.That(revision.Id).IsEqualTo(store.RevisionId);
        await Assert.That(revision.IdentityEpoch).IsEqualTo(7);
        await Assert.That(revision.DisclosureEpoch).IsEqualTo(originalEpoch + 1);
        await Assert.That(observation.OriginalOwnerPreserved).IsTrue();
        await Assert.That(beganWhileHeld).IsFalse();
        await Assert.That(commandsWhileHeld).IsEqualTo(0);
        await Assert.That(observation.HeldBeforeBegin).IsTrue();
        await Assert.That(observation.HeldBeforeCommit).IsTrue();
        await Assert.That(observation.HeldAfterCommit).IsTrue();
    }
}
