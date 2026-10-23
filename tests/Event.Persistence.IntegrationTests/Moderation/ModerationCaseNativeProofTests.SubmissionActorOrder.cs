using System.Data.Common;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationCaseNativeProofTests
{
    [Test]
    [Arguments(ModerationCaseTargetKind.EventVersion, "unchanged")]
    [Arguments(ModerationCaseTargetKind.LocalIncident, "unchanged")]
    [Arguments(ModerationCaseTargetKind.EventVersion, "publisher")]
    [Arguments(ModerationCaseTargetKind.LocalIncident, "publisher")]
    [Arguments(ModerationCaseTargetKind.EventVersion, "organizer")]
    [Arguments(ModerationCaseTargetKind.LocalIncident, "organizer")]
    [Arguments(ModerationCaseTargetKind.EventVersion, "terminal-publisher")]
    [Arguments(ModerationCaseTargetKind.LocalIncident, "terminal-publisher")]
    [Arguments(ModerationCaseTargetKind.EventVersion, "terminal-organizer")]
    [Arguments(ModerationCaseTargetKind.LocalIncident, "terminal-organizer")]
    [Arguments(ModerationCaseTargetKind.HiddenContentReceipt, "unchanged")]
    [Arguments(ModerationCaseTargetKind.HiddenContentReceipt, "publisher")]
    [Arguments(ModerationCaseTargetKind.HiddenContentReceipt, "organizer")]
    [Arguments(ModerationCaseTargetKind.HiddenContentReceipt, "terminal-publisher")]
    [Arguments(ModerationCaseTargetKind.HiddenContentReceipt, "terminal-organizer")]
    [Arguments(ModerationCaseTargetKind.Service, "unchanged")]
    [Arguments(ModerationCaseTargetKind.Service, "publisher")]
    [Arguments(ModerationCaseTargetKind.Service, "organizer")]
    [Arguments(ModerationCaseTargetKind.Service, "terminal-publisher")]
    [Arguments(ModerationCaseTargetKind.Service, "terminal-organizer")]
    public async Task Submission_receipt_keeps_its_original_actor_plan_before_tenant_and_through_commit(
        ModerationCaseTargetKind kind, string change)
    {
        await using var database = await NativeDatabase.CreateAsync(kind);
        Guid organizerId = Guid.CreateVersion7(), replacementId = Guid.CreateVersion7();
        long originalEpoch;
        await using (var seed = database.Context())
        {
            foreach (var id in new[] { organizerId, replacementId })
            {
                var service = new ServicePrincipal
                {
                    Id = Guid.CreateVersion7(), Code = $"receipt-plan-{id:N}", DisplayName = "Receipt publisher"
                };
                seed.AddRange(service, new Actor
                {
                    Id = id, ActorTypeId = (int)ActorTypeEnum.Bot, ActorType = null!,
                    ServicePrincipalId = service.Id, Pii = new ActorPii { ActorId = id, DisplayName = "Receipt publisher" }
                });
            }
            (await seed.Events.SingleAsync(row => row.Id == database.EventId)).OrganizerActorId = organizerId;
            await seed.SaveChangesAsync();
            originalEpoch = (await seed.ModerationQualificationEpochs.AsNoTracking().SingleAsync()).Revision;
        }
        var order = new SubmissionActorOrder(database.EventId, replacementId, change);
        var terminal = new ReceiptSavedCheckpoint(async context =>
        {
            if (change.StartsWith("terminal-", StringComparison.Ordinal))
                await order.ReplaceAsync(context, change.EndsWith("organizer", StringComparison.Ordinal), default);
        });
        await using (var context = database.Context(order, terminal))
        {
            var request = database.Request(ModerationCaseRelationshipKind.EventSubmitter);
            if (kind == ModerationCaseTargetKind.HiddenContentReceipt)
                request = request with { NativeKind = ModerationCaseTargetKind.Actor, NativeTargetId = database.ActorId };
            var issued = await IssuanceCommitter(context, database, database.Caller).IssueSubmissionReceiptAsync(
                request, default);
            await Assert.That(issued is not null).IsEqualTo(change == "unchanged");
            await Assert.That(order.SawTenant).IsTrue();
            await Assert.That(order.ActorsBeforeTenant.SequenceEqual(new[] { database.ActorId, organizerId }.Order())).IsTrue();
            await Assert.That(order.ActorAnchors.Contains(replacementId)).IsFalse();
            await Assert.That(order.CallerBeforeFirstActor).IsTrue();
            await Assert.That(context.Database.CurrentTransaction).IsNull();
            await Assert.That(context.ChangeTracker.Entries().Any()).IsFalse();
        }
        await using var inspect = database.Context();
        var parent = await inspect.Events.AsNoTracking().SingleAsync(row => row.Id == database.EventId);
        await Assert.That(parent.ActorId).IsEqualTo(database.ActorId);
        await Assert.That(parent.OrganizerActorId).IsEqualTo((Guid?)organizerId);
        await Assert.That(await inspect.ModerationCaseTargetSources.CountAsync()).IsEqualTo(change == "unchanged" ? 1 : 0);
        await Assert.That(await inspect.ModerationCaseTargetReceipts.CountAsync()).IsEqualTo(change == "unchanged" ? 1 : 0);
        await Assert.That((await inspect.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(originalEpoch);
    }

    private sealed class SubmissionActorOrder(Guid eventId, Guid replacementId, string change) : DbCommandInterceptor
    {
        public List<Guid> ActorAnchors { get; } = [];
        public Guid[] ActorsBeforeTenant { get; private set; } = [];
        public bool SawTenant { get; private set; }
        public bool CallerBeforeFirstActor { get; private set; }
        private bool _sawCaller;

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context is not ExploreDbContext context) return result;
            string Table<T>()
            {
                var entity = context.Model.FindEntityType(typeof(T))!;
                return context.GetService<ISqlGenerationHelper>().DelimitIdentifier(entity.GetTableName()!, entity.GetSchema());
            }
            if (command.CommandText.StartsWith($"UPDATE {Table<User>()}", StringComparison.Ordinal)) _sawCaller = true;
            if (command.CommandText.StartsWith($"UPDATE {Table<Actor>()}", StringComparison.Ordinal))
            {
                if (ActorAnchors.Count == 0) CallerBeforeFirstActor = _sawCaller;
                ActorAnchors.AddRange(command.Parameters.Cast<DbParameter>().Select(row => row.Value).OfType<Guid>());
            }
            if (!SawTenant && command.CommandText.StartsWith($"UPDATE {Table<Tenant>()}", StringComparison.Ordinal))
            {
                SawTenant = true;
                ActorsBeforeTenant = ActorAnchors.ToArray();
                if (change is "publisher" or "organizer")
                    await ReplaceAsync(context, change == "organizer", cancellationToken);
            }
            return result;
        }

        public Task<int> ReplaceAsync(ExploreDbContext context, bool organizer, CancellationToken token) => organizer
            ? context.Events.Where(row => row.Id == eventId).ExecuteUpdateAsync(
                update => update.SetProperty(row => row.OrganizerActorId, replacementId), token)
            : context.Events.Where(row => row.Id == eventId).ExecuteUpdateAsync(
                update => update.SetProperty(row => row.ActorId, replacementId), token);
    }
}
