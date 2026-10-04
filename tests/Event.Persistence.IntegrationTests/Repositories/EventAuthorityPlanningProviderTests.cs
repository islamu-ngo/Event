using System.Data.Common;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Services;
using Explore.Application.Exceptions;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Explore.Persistence.Services;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.Repositories;

[RequiresRetainingReadDatabase]
[NotInParallel("PrimaryDatabaseProviderBehaviorContract")]
public sealed class EventAuthorityPlanningProviderTests
{
    private static readonly DateTime Now = new(2040, 1, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(30);

    [Test]
    [Arguments("unchanged")]
    [Arguments("actor")]
    [Arguments("organizer")]
    public async Task Authority_planning_does_not_retain_event_locks_ahead_of_detached_actor_fences(
        string ownershipChange)
    {
        var fixture = PrimaryDatabaseProviderBehaviorFixture.Create();
        await fixture.PrepareAsync();
        using var timeout = new CancellationTokenSource(Deadline);
        var token = timeout.Token;
        var tenant = new Tenant
        {
            Id = Guid.CreateVersion7(), Slug = $"authority-planning-{Guid.CreateVersion7():N}",
            FullName = "Authority planning", TenantStatusId = (int)TenantStatusEnum.Active, TenantStatus = null!
        };
        var actors = Enumerable.Range(0, 2).Select(_ => new Actor
        {
            Id = Guid.CreateVersion7(), ActorTypeId = (int)ActorTypeEnum.User, ActorType = null!,
            User = new User
            {
                Id = Guid.CreateVersion7(),
                Pii = new UserPii
                {
                    Email = $"authority-{Guid.CreateVersion7():N}@example.test",
                    FirstName = "Authority", LastName = "Planner"
                }
            },
            Pii = new ActorPii { DisplayName = "Authority planner" }
        }).ToArray();
        Guid reviewerId = actors[0].User!.Id;
        var entity = new Explore.Domain.Event(EventStatusEnum.Published)
        {
            Id = Guid.CreateVersion7(), TenantId = tenant.Id, Tenant = tenant,
            ActorId = actors[0].Id, Actor = actors[0], OrganizerActorId = actors[0].Id,
            Title = "Before detached update", PublicCode = Guid.CreateVersion7().ToString("N")[..12],
            EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
            VisibilityTypeId = (int)VisibilityTypeEnum.Public, VisibilityType = null!,
            EventFormatId = (int)EventFormatEnum.Local, EventFormat = null!, EventStatus = null!,
            Timezone = "UTC"
        };
        var assignment = EventRoleAssignment.Create(tenant.Id, entity.Id, reviewerId,
            (int)RoleEnum.EventManager, EventRoleAssignmentStatus.Active, Now.AddDays(-1),
            Now.AddDays(1), reviewerId);
        await using (var seed = fixture.CreateSystemContext())
        {
            seed.AddRange(actors);
            seed.Add(entity);
            seed.Add(assignment);
            await seed.SaveChangesAsync(token);
            Console.WriteLine($"Authority planning witness provider: {fixture.Provider}; {seed.Database.ProviderName}.");
        }

        // Subscribe both boundaries before either transaction begins. The detached
        // writer reaches its Event fence only after taking its old/new Actor anchors.
        var writerEvent = new BeforeFence(typeof(Explore.Domain.Event));
        var reviewerActor = new BeforeFence(typeof(Actor));
        await using var writer = fixture.CreateTenantContext(tenant.Id, writerEvent);
        await using var reviewer = fixture.CreateTenantContext(tenant.Id, reviewerActor);
        var trackedGrant = await reviewer.EventRoleAssignments.SingleAsync(row => row.Id == assignment.Id, token);
        var detached = await writer.Events.AsNoTracking().SingleAsync(row => row.Id == entity.Id, token);
        detached.Title = "After detached update";
        if (ownershipChange == "actor")
            detached.ActorId = actors[1].Id;
        if (ownershipChange == "organizer")
            detached.OrganizerActorId = actors[1].Id;

        Task write = new EfCoreUnitOfWork(writer).ExecuteSerializableAsync(async ct =>
        {
            await new EventRepository(writer).Update(detached);
            var grant = await writer.EventRoleAssignments.SingleAsync(row => row.Id == assignment.Id, ct);
            grant.Revoke(reviewerId, Now);
            await writer.SaveChangesAsync(ct);
            return true;
        }, token);
        EventAuthoritySnapshot? snapshot = null;
        Task<Exception?>? review = null;
        try
        {
            await writerEvent.Reached.WaitAsync(Deadline, token);
            review = ReviewAsync();
            await reviewerActor.Reached.WaitAsync(Deadline, token);
            writerEvent.Release();
            // This is the decisive witness: the real writer must commit while the
            // real planner remains paused before its first Actor command. An ordinary
            // Serializable planning read retains Event S here and blocks that commit.
            await write.WaitAsync(Deadline, token);
        }
        finally
        {
            writerEvent.Release();
            reviewerActor.Release();
            await Task.WhenAll(write, (Task?)review ?? Task.CompletedTask).WaitAsync(Deadline);
        }

        Exception? failure = await review!;
        if (ownershipChange == "unchanged")
        {
            await Assert.That(failure).IsNull();
            await Assert.That(snapshot!.Events[entity.Id].IsManager).IsFalse();
        }
        else
        {
            await Assert.That(failure is ConcurrencyConflictException).IsTrue();
            await Assert.That(snapshot).IsNull();
        }
        await Assert.That(trackedGrant.Status).IsEqualTo(EventRoleAssignmentStatus.Active);

        await using var fresh = fixture.CreateTenantContext(tenant.Id);
        var freshSnapshot = await new EfCoreUnitOfWork(fresh).ExecuteSerializableAsync(
            ct => new EventAuthoritySnapshotService(fresh).GetCommitBoundForUserAndEventsAsync(
                tenant.Id, reviewerId, [entity.Id], Now, ct), token);
        await Assert.That(freshSnapshot.Events[entity.Id].IsManager).IsFalse();
        var committed = await new EventRepository(fresh).GetById(entity.Id);
        await Assert.That(committed!.Title).IsEqualTo("After detached update");
        await Assert.That(committed.ActorId)
            .IsEqualTo(ownershipChange == "actor" ? actors[1].Id : actors[0].Id);
        await Assert.That(committed.OrganizerActorId)
            .IsEqualTo(ownershipChange == "organizer" ? actors[1].Id : actors[0].Id);

        async Task<Exception?> ReviewAsync()
        {
            try
            {
                snapshot = await new EfCoreUnitOfWork(reviewer).ExecuteSerializableAsync(
                    ct => new EventAuthoritySnapshotService(reviewer).GetCommitBoundForUserAndEventsAsync(
                        tenant.Id, reviewerId, [entity.Id], Now, ct), token);
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }
    }

    private sealed class BeforeFence(Type entityType) : DbCommandInterceptor
    {
        private readonly TaskCompletionSource _reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Reached => _reached.Task;
        public void Release() => _release.TrySetResult();

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (_reached.Task.IsCompleted)
                return result;
            var context = eventData.Context!;
            var entity = context.Model.FindEntityType(entityType)!;
            string table = context.GetService<ISqlGenerationHelper>()
                .DelimitIdentifier(entity.GetTableName()!, entity.GetSchema());
            if (!command.CommandText.Contains(table, StringComparison.Ordinal)
                || !(command.CommandText.StartsWith($"UPDATE {table}", StringComparison.Ordinal)
                    || command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal)
                    || command.CommandText.Contains("UPDLOCK", StringComparison.Ordinal)))
                return result;
            _reached.TrySetResult();
            await _release.Task.WaitAsync(Deadline, cancellationToken);
            return result;
        }
    }
}

internal sealed class RequiresRetainingReadDatabaseAttribute()
    : SkipAttribute("This lock-order regression requires the SQL Server or InnoDB structured provider lane.")
{
    public override Task<bool> ShouldSkip(TestRegisteredContext _)
        => Task.FromResult(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("Database__Provider"))
            || PrimaryDatabaseProviderBehaviorFixture.Create().Provider is not
                (PrimaryDatabaseProvider.SqlServer or PrimaryDatabaseProvider.MySql or PrimaryDatabaseProvider.MariaDb));
}
