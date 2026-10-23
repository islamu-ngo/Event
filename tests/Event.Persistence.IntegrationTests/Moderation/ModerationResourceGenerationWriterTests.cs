using System.Data.Common;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using DomainEvent = Explore.Domain.Event;
using SourceDatabase = Event.Persistence.IntegrationTests.Moderation.ModerationQualificationWriterTests.SourceDatabase;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationResourceGenerationWriterTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ActualNativeAncestry_AllocatesNumericGenerationAndPreservesExactRetry(bool storedFile)
    {
        await using var database = await SourceDatabase.CreateAsync();
        var resourceId = await SeedAsync(database, storedFile: storedFile);
        var source = Source(database, resourceId);
        var accepted = await database.ExecuteAsync(database.AdministratorId, source);
        var replay = await database.ExecuteAsync(database.AdministratorId, source);
        await Assert.That(accepted.IsSuccess).IsTrue();
        await Assert.That(replay.Id).IsEqualTo(accepted.Id);
        await Assert.That(replay.QualificationRevision).IsEqualTo(accepted.QualificationRevision);
        var next = source with
        {
            SourceId = Guid.CreateVersion7(), OperationId = Guid.CreateVersion7(),
            ExpectedQualificationRevision = 2, ExpectedResourceRevision = 1
        };
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, next)).IsSuccess).IsTrue();
        await using var reader = database.Context();
        reader.ModerationTrustDomainId = database.Scope.TrustDomainId;
        var generations = await reader.ModerationResourceGenerations.OrderBy(row => row.Revision).ToListAsync();
        var resource = await reader.Set<EventResource>().SingleAsync(row => row.Id == resourceId);
        var owningEvent = await reader.Events.SingleAsync(row => row.Id == resource.EventId);
        var actor = await reader.Actors.SingleAsync(row => row.Id == owningEvent.ActorId);
        await Assert.That(generations.Count).IsEqualTo(2);
        await Assert.That(generations[0].Revision).IsEqualTo(1L);
        await Assert.That(generations[1].Revision).IsEqualTo(2L);
        await Assert.That(generations[0].ResourceStamp).IsEqualTo(resource.ConcurrencyStamp);
        await Assert.That(generations[0].EventStamp).IsEqualTo(owningEvent.ConcurrencyStamp);
        await Assert.That(generations[0].ActorStamp).IsEqualTo(actor.ConcurrencyStamp);
        await Assert.That(generations[1].ResourceStamp).IsEqualTo(generations[0].ResourceStamp);
        if (storedFile)
        {
            var storage = await reader.Set<StorageObject>().SingleAsync(row => row.Id == resource.StorageObjectId);
            await Assert.That(generations[0].StorageObjectId).IsEqualTo(storage.Id);
            await Assert.That(generations[0].StorageObjectStamp).IsEqualTo(storage.ConcurrencyStamp);
            await Assert.That(generations[0].StorageProviderBindingId).IsEqualTo(storage.StorageProviderBindingId);
        }
        await Assert.That(await reader.ModerationEffectIntents.CountAsync()).IsEqualTo(0);
        await Assert.That(await reader.ModerationSanctions.CountAsync()).IsEqualTo(0);
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    public async Task UnqualifiedObservation_DoesNotAdvanceSourceOrEpoch(int substitution)
    {
        await using var database = await SourceDatabase.CreateAsync();
        var resourceId = await SeedAsync(database, substitution == 2, storedFile: substitution == 4);
        var source = Source(database, resourceId);
        if (substitution == 0) source = source with { ExpectedResourceRevision = 1 };
        if (substitution == 1)
        {
            await using var writer = database.Context();
            await writer.Set<EventResource>().Where(row => row.Id == resourceId)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsDeleted, true));
        }
        if (substitution == 3) database.ExpireGovernanceAfterSave = true;
        if (substitution == 4)
        {
            await using var writer = database.Context();
            await writer.Set<StorageObject>().Where(row => row.OwningResourceId == resourceId)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.Purpose, StorageObjectPurposes.Attachment));
        }
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, source)).IsSuccess).IsFalse();
        await using var reader = database.Context();
        reader.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await Assert.That(await reader.ModerationResourceGenerations.CountAsync()).IsEqualTo(0);
        await Assert.That(await reader.ModerationQualificationMutationReceipts.AnyAsync(row => row.Id == source.OperationId)).IsFalse();
        await Assert.That((await reader.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(1L);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task A_changed_resource_event_parent_cannot_add_a_late_actor(bool organizer)
    {
        await using var database = await SourceDatabase.CreateAsync();
        var resourceId = await SeedAsync(database);
        var replacement = new Actor
        {
            Id = Guid.CreateVersion7(), ActorTypeId = (int)ActorTypeEnum.User, ActorType = null!,
            UserId = database.ReviewerId, Pii = new ActorPii { DisplayName = "Replacement resource publisher" }
        };
        Guid eventId;
        Guid publisher;
        await using (var seed = database.Context())
        {
            eventId = await seed.Set<EventResource>().Where(row => row.Id == resourceId)
                .Select(row => row.EventId).SingleAsync();
            publisher = await seed.Events.Where(row => row.Id == eventId).Select(row => row.ActorId).SingleAsync();
            seed.Actors.Add(replacement);
            await seed.SaveChangesAsync();
        }
        var change = new ModerationEventGenerationWriterTests.ReplaceOwnerBeforeTenant(eventId, replacement.Id, organizer);
        var source = Source(database, resourceId);
        var result = await database.ExecuteAsync(database.AdministratorId, source, interceptor: change);
        await Assert.That(change.Observed).IsTrue();
        await Assert.That(change.ActorAnchorsBeforeTenant.Contains(publisher)).IsTrue();
        await Assert.That(change.ActorAnchors.Contains(replacement.Id)).IsFalse();
        await Assert.That(result.IsSuccess).IsFalse();
        await using var current = database.Context();
        current.ModerationTrustDomainId = database.Scope.TrustDomainId;
        var actual = await current.Events.SingleAsync(row => row.Id == eventId);
        await Assert.That(actual.ActorId).IsEqualTo(publisher);
        await Assert.That(actual.OrganizerActorId).IsNull();
        await Assert.That(await current.ModerationResourceGenerations.AnyAsync()).IsFalse();
        await Assert.That(await current.ModerationQualificationMutationReceipts.AnyAsync(row => row.Id == source.OperationId)).IsFalse();
        await Assert.That((await current.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(1L);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Stored_file_actor_is_planned_before_tenant_and_cannot_be_substituted(bool replaceAfterPlanning)
    {
        await using var database = await SourceDatabase.CreateAsync();
        var resourceId = await SeedAsync(database, storedFile: true);
        var owner = new Actor
        {
            Id = Guid.CreateVersion7(), ActorTypeId = (int)ActorTypeEnum.User, ActorType = null!,
            UserId = database.ReviewerId, Pii = new ActorPii { DisplayName = "Stored resource owner" }
        };
        var replacement = new Actor
        {
            Id = Guid.CreateVersion7(), ActorTypeId = (int)ActorTypeEnum.User, ActorType = null!,
            UserId = database.UrgencyAssessorId, Pii = new ActorPii { DisplayName = "Replacement storage owner" }
        };
        Guid storageId;
        await using (var seed = database.Context())
        {
            seed.Actors.AddRange(owner, replacement);
            await seed.SaveChangesAsync();
            storageId = await seed.Set<EventResource>().Where(row => row.Id == resourceId)
                .Select(row => row.StorageObjectId!.Value).SingleAsync();
            await seed.Set<StorageObject>().Where(row => row.Id == storageId)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.ActorId, owner.Id));
        }
        var checkpoint = new ReplaceStorageActorBeforeTenant(storageId, replaceAfterPlanning ? replacement.Id : null);
        var source = Source(database, resourceId);
        var result = await database.ExecuteAsync(database.AdministratorId, source, interceptor: checkpoint);
        await Assert.That(checkpoint.Observed).IsTrue();
        await Assert.That(checkpoint.ActorAnchorsBeforeTenant.Contains(owner.Id)).IsTrue();
        await Assert.That(checkpoint.ActorAnchors.Contains(replacement.Id)).IsFalse();
        await Assert.That(result.IsSuccess).IsEqualTo(!replaceAfterPlanning);
        await using var current = database.Context();
        current.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await Assert.That((await current.Set<StorageObject>().SingleAsync(row => row.Id == storageId)).ActorId).IsEqualTo(owner.Id);
        await Assert.That(await current.ModerationResourceGenerations.CountAsync()).IsEqualTo(replaceAfterPlanning ? 0 : 1);
        await Assert.That(await current.ModerationQualificationMutationReceipts.AnyAsync(row => row.Id == source.OperationId)).IsEqualTo(!replaceAfterPlanning);
        await Assert.That((await current.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(replaceAfterPlanning ? 1L : 2L);
    }

    [Test]
    public async Task Recorded_resource_retry_preserves_its_original_receipt_after_native_resource_deletion()
    {
        await using var database = await SourceDatabase.CreateAsync();
        var resourceId = await SeedAsync(database);
        var source = Source(database, resourceId);
        var first = await database.ExecuteAsync(database.AdministratorId, source);
        await Assert.That(first.IsSuccess).IsTrue();
        await using (var deleted = database.Context())
        {
            var eventId = await deleted.Set<EventResource>().Where(row => row.Id == resourceId)
                .Select(row => row.EventId).SingleAsync();
            var actorId = await deleted.Events.Where(row => row.Id == eventId).Select(row => row.ActorId).SingleAsync();
            await deleted.Set<EventResource>().Where(row => row.Id == resourceId)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsDeleted, true));
            await deleted.Events.Where(row => row.Id == eventId)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsDeleted, true));
            await deleted.Actors.Where(row => row.Id == actorId)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsDeleted, true));
        }
        var replay = await database.ExecuteAsync(database.AdministratorId, source);
        await Assert.That(replay.IsSuccess).IsTrue();
        await Assert.That(replay.Id).IsEqualTo(first.Id);
        await Assert.That(replay.QualificationRevision).IsEqualTo(first.QualificationRevision);
        await using var current = database.Context();
        current.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await Assert.That(await current.ModerationResourceGenerations.CountAsync()).IsEqualTo(1);
        await Assert.That(await current.ModerationQualificationMutationReceipts.CountAsync(row => row.Id == source.OperationId)).IsEqualTo(1);
        await Assert.That((await current.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(2L);
    }

    internal sealed class ReplaceStorageActorBeforeTenant(Guid storageId, Guid? replacementId) : DbCommandInterceptor
    {
        public bool Observed { get; private set; }
        public List<Guid> ActorAnchors { get; } = [];
        public IReadOnlyList<Guid> ActorAnchorsBeforeTenant { get; private set; } = [];

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            var context = (ExploreDbContext)eventData.Context!;
            var sql = context.GetService<ISqlGenerationHelper>();
            string Table(Type type)
            {
                var entity = context.Model.FindEntityType(type)!;
                return sql.DelimitIdentifier(entity.GetTableName()!, entity.GetSchema());
            }
            if (command.CommandText.StartsWith($"UPDATE {Table(typeof(Actor))}", StringComparison.Ordinal))
                ActorAnchors.AddRange(command.Parameters.Cast<DbParameter>().Select(row => row.Value).OfType<Guid>());
            if (!Observed && command.CommandText.StartsWith($"UPDATE {Table(typeof(Tenant))}", StringComparison.Ordinal))
            {
                Observed = true;
                ActorAnchorsBeforeTenant = ActorAnchors.ToArray();
                if (replacementId is { } actorId)
                    await context.Set<StorageObject>().Where(row => row.Id == storageId)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.ActorId, actorId), cancellationToken);
            }
            return result;
        }
    }

    private static RecordModerationResourceGenerationSource Source(SourceDatabase database, Guid resourceId) => new()
    {
        OperationId = Guid.CreateVersion7(), SourceId = Guid.CreateVersion7(), Target = database.Scope,
        ExpectedQualificationRevision = 1, ResourceId = resourceId, ExpectedResourceRevision = 0, ValidUntil = Now.AddMinutes(10)
    };

}
