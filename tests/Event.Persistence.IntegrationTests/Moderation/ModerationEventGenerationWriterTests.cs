using System.Data.Common;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using SourceDatabase = Event.Persistence.IntegrationTests.Moderation.ModerationQualificationWriterTests.SourceDatabase;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed class ModerationEventGenerationWriterTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task NativeEventObservation_PreservesGuidVersionAndExactAcceptedRetry()
    {
        await using var database = await SourceDatabase.CreateAsync();
        var eventId = await SeedAsync(database);
        var source = Source(database, eventId);
        var accepted = await database.ExecuteAsync(database.AdministratorId, source);
        var replay = await database.ExecuteAsync(database.AdministratorId, source);
        await Assert.That(accepted.IsSuccess).IsTrue();
        await Assert.That(replay.Id).IsEqualTo(accepted.Id);
        await Assert.That(replay.QualificationRevision).IsEqualTo(accepted.QualificationRevision);
        var next = source with
        {
            OperationId = Guid.CreateVersion7(),
            SourceId = Guid.CreateVersion7(),
            ExpectedEventRevision = 1,
            ExpectedQualificationRevision = 2
        };
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, next)).IsSuccess).IsTrue();
        await using var reader = database.Context();
        reader.ModerationTrustDomainId = database.Scope.TrustDomainId;
        var observations = await reader.ModerationEventGenerations.OrderBy(row => row.Revision).ToListAsync();
        var actual = await reader.Events.SingleAsync(row => row.Id == eventId);
        var publisher = await reader.Actors.SingleAsync(row => row.Id == actual.ActorId);
        await Assert.That(observations.Count).IsEqualTo(2);
        await Assert.That(observations[0].Revision).IsEqualTo(1L);
        await Assert.That(observations[1].Revision).IsEqualTo(2L);
        await Assert.That(observations[0].EventStamp).IsEqualTo(actual.ConcurrencyStamp);
        await Assert.That(observations[0].ActorStamp).IsEqualTo(publisher.ConcurrencyStamp);
        await Assert.That(observations[0].SubmittedByHumanId).IsEqualTo(actual.SubmittedByUserId);
        await Assert.That(observations[0].HasSameNativeVersion(observations[1])).IsTrue();
        await Assert.That(await reader.ModerationResourceGenerations.CountAsync()).IsEqualTo(0);
        await Assert.That(await reader.ModerationSanctions.CountAsync()).IsEqualTo(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Accepted_event_observation_replays_its_receipt_after_native_deletion(bool deletePublisher)
    {
        await using var database = await SourceDatabase.CreateAsync();
        var eventId = await SeedAsync(database);
        var source = Source(database, eventId);
        var accepted = await database.ExecuteAsync(database.AdministratorId, source);
        await Assert.That(accepted.IsSuccess).IsTrue();
        await using (var writer = database.Context())
        {
            var actorId = await writer.Events.Where(row => row.Id == eventId).Select(row => row.ActorId).SingleAsync();
            await writer.Events.Where(row => row.Id == eventId).ExecuteUpdateAsync(update => update.SetProperty(row => row.IsDeleted, true));
            if (deletePublisher)
                await writer.Actors.Where(row => row.Id == actorId).ExecuteUpdateAsync(update => update.SetProperty(row => row.IsDeleted, true));
        }
        var replay = await database.ExecuteAsync(database.AdministratorId, source);
        await Assert.That(replay.IsSuccess).IsTrue();
        await Assert.That(replay.Id).IsEqualTo(accepted.Id);
        await Assert.That(replay.QualificationRevision).IsEqualTo(accepted.QualificationRevision);
        await using var current = database.Context();
        current.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await Assert.That(await current.ModerationEventGenerations.CountAsync()).IsEqualTo(1);
        await Assert.That(await current.ModerationQualificationMutationReceipts.CountAsync(row => row.Id == source.OperationId)).IsEqualTo(1);
        await Assert.That((await current.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(2L);
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    [Arguments(5)]
    public async Task UnqualifiedEventObservation_DoesNotLeaveSourceReceiptOrEpochPrefix(int substitution)
    {
        await using var database = await SourceDatabase.CreateAsync();
        var eventId = await SeedAsync(database);
        var source = Source(database, eventId);
        if (substitution == 0) source = source with { ExpectedEventRevision = 1 };
        if (substitution == 1 || substitution == 2 || substitution == 4)
        {
            await using var writer = database.Context();
            if (substitution == 1)
                await writer.Events.Where(row => row.Id == eventId).ExecuteUpdateAsync(update => update.SetProperty(row => row.IsDeleted, true));
            else if (substitution == 2)
            {
                var actorId = await writer.Events.Where(row => row.Id == eventId).Select(row => row.ActorId).SingleAsync();
                await writer.Actors.Where(row => row.Id == actorId).ExecuteUpdateAsync(update => update.SetProperty(row => row.IsDeleted, true));
            }
            else
                await writer.Events.Where(row => row.Id == eventId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.ConcurrencyStamp, Guid.Empty));
        }
        if (substitution == 3) database.ExpireGovernanceAfterSave = true;
        if (substitution == 5) source = source with { Target = database.Instance };
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, source)).IsSuccess).IsFalse();
        await using var reader = database.Context();
        reader.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await Assert.That(await reader.ModerationEventGenerations.CountAsync()).IsEqualTo(0);
        await Assert.That(await reader.ModerationQualificationMutationReceipts.AnyAsync(row => row.Id == source.OperationId)).IsFalse();
        await Assert.That((await reader.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(1L);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task A_changed_event_owner_cannot_add_an_actor_anchor_after_the_tenant_fence(bool organizer)
    {
        await using var database = await SourceDatabase.CreateAsync();
        var eventId = await SeedAsync(database);
        var replacement = new Actor
        {
            Id = Guid.CreateVersion7(),
            ActorTypeId = (int)ActorTypeEnum.User,
            ActorType = null!,
            UserId = database.ReviewerId,
            Pii = new ActorPii { DisplayName = "Replacement publisher" }
        };
        Guid originalPublisher;
        await using (var seed = database.Context())
        {
            originalPublisher = await seed.Events.Where(row => row.Id == eventId).Select(row => row.ActorId).SingleAsync();
            seed.Actors.Add(replacement);
            await seed.SaveChangesAsync();
        }
        var change = new ReplaceOwnerBeforeTenant(eventId, replacement.Id, organizer);
        var source = Source(database, eventId);
        var result = await database.ExecuteAsync(database.AdministratorId, source, interceptor: change);
        await Assert.That(change.Observed).IsTrue();
        await Assert.That(change.ActorAnchors.Contains(replacement.Id)).IsFalse();
        await Assert.That(result.IsSuccess).IsFalse();
        await using var current = database.Context();
        current.ModerationTrustDomainId = database.Scope.TrustDomainId;
        var actual = await current.Events.SingleAsync(row => row.Id == eventId);
        await Assert.That(actual.ActorId).IsEqualTo(originalPublisher);
        await Assert.That(actual.OrganizerActorId).IsNull();
        await Assert.That(await current.ModerationEventGenerations.AnyAsync()).IsFalse();
        await Assert.That(await current.ModerationQualificationMutationReceipts.AnyAsync(row => row.Id == source.OperationId)).IsFalse();
        await Assert.That((await current.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(1L);
    }

    internal sealed class ReplaceOwnerBeforeTenant(Guid eventId, Guid replacementId, bool organizer) : DbCommandInterceptor
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
                if (organizer)
                    await context.Events.Where(row => row.Id == eventId).ExecuteUpdateAsync(
                        update => update.SetProperty(row => row.OrganizerActorId, replacementId), cancellationToken);
                else
                    await context.Events.Where(row => row.Id == eventId).ExecuteUpdateAsync(
                        update => update.SetProperty(row => row.ActorId, replacementId), cancellationToken);
            }
            return result;
        }
    }

    private static RecordModerationEventGenerationSource Source(SourceDatabase database, Guid eventId) => new()
    {
        OperationId = Guid.CreateVersion7(),
        SourceId = Guid.CreateVersion7(),
        EventId = eventId,
        Target = database.Scope,
        ExpectedQualificationRevision = 1,
        ExpectedEventRevision = 0,
        ValidUntil = Now.AddMinutes(10)
    };

    private static async Task<Guid> SeedAsync(SourceDatabase database)
    {
        var resourceId = await ModerationResourceGenerationWriterTests.SeedAsync(database);
        await using var reader = database.Context();
        return await reader.Set<EventResource>().Where(row => row.Id == resourceId).Select(row => row.EventId).SingleAsync();
    }
}
