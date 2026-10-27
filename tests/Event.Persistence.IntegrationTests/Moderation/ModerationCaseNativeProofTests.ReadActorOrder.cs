using System.Data.Common;
using Explore.Application.Contracts.Moderation;
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
    [Arguments(ModerationCaseTargetKind.EventVersion, "terminal-swap")]
    [Arguments(ModerationCaseTargetKind.LocalIncident, "terminal-swap")]
    [Arguments(ModerationCaseTargetKind.EventVersion, "grant-caller")]
    [Arguments(ModerationCaseTargetKind.LocalIncident, "grant-caller")]
    [Arguments(ModerationCaseTargetKind.EventVersion, "grant-source")]
    [Arguments(ModerationCaseTargetKind.LocalIncident, "grant-source")]
    [Arguments(ModerationCaseTargetKind.Actor, "unchanged")]
    [Arguments(ModerationCaseTargetKind.Service, "unchanged")]
    [Arguments(ModerationCaseTargetKind.Actor, "organizer")]
    [Arguments(ModerationCaseTargetKind.Service, "organizer")]
    [Arguments(ModerationCaseTargetKind.Actor, "terminal-swap")]
    [Arguments(ModerationCaseTargetKind.Service, "terminal-swap")]
    [Arguments(ModerationCaseTargetKind.Actor, "native-stamp")]
    [Arguments(ModerationCaseTargetKind.Service, "native-stamp")]
    [Arguments(ModerationCaseTargetKind.Service, "service-owner")]
    public async Task Read_receipt_fences_original_actors_before_first_tenant_and_rejects_planned_authority_drift(
        ModerationCaseTargetKind kind, string change)
    {
        await using var database = await NativeDatabase.CreateAsync(kind);
        Guid organizerId = Guid.CreateVersion7(), replacementId = Guid.CreateVersion7(), otherCallerId = Guid.CreateVersion7();
        Guid replacementServiceId = Guid.CreateVersion7(), driftStamp = Guid.CreateVersion7();
        await using (var seed = database.Context())
        {
            foreach (var id in new[] { organizerId, replacementId })
            {
                var service = new ServicePrincipal
                {
                    Id = Guid.CreateVersion7(),
                    Code = $"read-order-{id:N}",
                    DisplayName = "Read order publisher"
                };
                seed.AddRange(service, new Actor
                {
                    Id = id,
                    ActorTypeId = (int)ActorTypeEnum.Bot,
                    ActorType = null!,
                    ServicePrincipalId = service.Id,
                    Pii = new ActorPii { ActorId = id, DisplayName = "Read order publisher" }
                });
            }
            seed.Users.Add(new User
            {
                Id = otherCallerId,
                Pii = new UserPii
                {
                    UserId = otherCallerId,
                    Email = $"{otherCallerId:N}@example.invalid",
                    FirstName = "Other",
                    LastName = "Reader"
                }
            });
            if (change == "service-owner")
                seed.ServicePrincipals.Add(new ServicePrincipal
                {
                    Id = replacementServiceId,
                    Code = $"read-unowned-{replacementServiceId:N}",
                    DisplayName = "Unowned service"
                });
            (await seed.Events.SingleAsync(row => row.Id == database.EventId)).OrganizerActorId = organizerId;
            await seed.SaveChangesAsync();
        }

        // Provision every submission/source before observing the read-only issuance operation.
        var (_, grantId) = await GrantedReadAsync(database);
        Guid alternateCaseId = Guid.Empty, alternateSourceId = Guid.Empty;
        if (change == "grant-source")
        {
            await using var setup = database.Context();
            var intake = IssuanceCommitter(setup, database, database.Caller);
            var request = database.Request(material: "Separate native material for the alternate read source") with
            {
                OccurrenceMaterial = System.Text.Encoding.UTF8.GetBytes("A separate native occurrence at the event")
            };
            var submission = await intake.IssueSubmissionReceiptAsync(request, default);
            await Assert.That(submission).IsNotNull();
            var accepted = await intake.SubmitAsync(new(ModerationCaseAccessPurpose.SubmitAllegation,
                Guid.Empty, Guid.CreateVersion7(), 0, submission!.Receipt.Id, submission.Token)
            { Narrative = request.Material }, default);
            await Assert.That(accepted.Outcome).IsEqualTo(ModerationCaseIntakeCommitOutcome.Accepted);
            alternateCaseId = accepted.Acknowledgement!.CaseId;
            alternateSourceId = submission.Receipt.SourceId;
        }

        await using var before = database.Context();
        var original = await before.Events.AsNoTracking().SingleAsync(row => row.Id == database.EventId);
        var actorBefore = await before.Actors.AsNoTracking().SingleAsync(row => row.Id == database.ActorId);
        var serviceBefore = await before.ServicePrincipals.AsNoTracking().SingleAsync(row => row.Id == database.ServiceId);
        var grantBefore = await before.ModerationCaseReadGrants.AsNoTracking().SingleAsync(row => row.Id == grantId);
        if (kind is ModerationCaseTargetKind.Actor or ModerationCaseTargetKind.Service)
        {
            var source = await before.ModerationCaseTargetSources.AsNoTracking().SingleAsync(row => row.Id == grantBefore.SourceId);
            await Assert.That(source.Kind).IsEqualTo(kind);
            await Assert.That(source.NativeKind).IsEqualTo(kind);
            await Assert.That(source.NativeTargetId).IsEqualTo(kind == ModerationCaseTargetKind.Actor ? database.ActorId : database.ServiceId);
            await Assert.That(source.EventId).IsEqualTo(database.EventId);
        }
        if (change == "grant-source")
        {
            await Assert.That(alternateSourceId).IsNotEqualTo(grantBefore.SourceId);
            await Assert.That(alternateCaseId).IsNotEqualTo(grantBefore.CaseId);
        }
        var receiptsBefore = await before.ModerationCaseTargetReceipts.AsNoTracking().OrderBy(row => row.Id)
            .Select(row => new { row.Id, row.Revision, row.ConsumedAt, row.TokenDigest }).ToArrayAsync();
        var sourceCount = await before.ModerationCaseTargetSources.CountAsync();
        var caseCount = await before.ModerationCases.CountAsync();
        var acknowledgementCount = await before.ModerationCaseIntakeAcknowledgements.CountAsync();
        var changedRows = 0;
        var order = new ReadIssuanceActorOrder(database.CallerId, async (context, token) =>
        {
            switch (change)
            {
                case "publisher":
                    changedRows = await context.Events.Where(row => row.Id == database.EventId).ExecuteUpdateAsync(
                        update => update.SetProperty(row => row.ActorId, replacementId), token);
                    break;
                case "organizer":
                    changedRows = await context.Events.Where(row => row.Id == database.EventId).ExecuteUpdateAsync(
                        update => update.SetProperty(row => row.OrganizerActorId, replacementId), token);
                    break;
                case "native-stamp":
                    changedRows = kind == ModerationCaseTargetKind.Actor
                        ? await context.Actors.Where(row => row.Id == database.ActorId).ExecuteUpdateAsync(
                            update => update.SetProperty(row => row.ConcurrencyStamp, driftStamp), token)
                        : await context.ServicePrincipals.Where(row => row.Id == database.ServiceId).ExecuteUpdateAsync(
                            update => update.SetProperty(row => row.ConcurrencyStamp, driftStamp), token);
                    break;
                case "service-owner":
                    changedRows = await context.Actors.Where(row => row.Id == database.ActorId).ExecuteUpdateAsync(
                        update => update.SetProperty(row => row.ServicePrincipalId, replacementServiceId), token);
                    break;
                case "grant-caller":
                case "grant-source":
                    var previousTrust = context.ModerationTrustDomainId;
                    try
                    {
                        context.ModerationTrustDomainId = database.TrustId;
                        changedRows = change == "grant-caller"
                            ? await context.ModerationCaseReadGrants.Where(row => row.Id == grantId).ExecuteUpdateAsync(
                                update => update.SetProperty(row => row.CallerUserId, otherCallerId), token)
                            : await context.ModerationCaseReadGrants.Where(row => row.Id == grantId).ExecuteUpdateAsync(
                                update => update.SetProperty(row => row.SourceId, alternateSourceId)
                                    .SetProperty(row => row.CaseId, alternateCaseId).SetProperty(row => row.CaseRevision, 1L), token);
                    }
                    finally
                    {
                        context.ModerationTrustDomainId = previousTrust;
                    }
                    break;
            }
        });
        var saved = new ReceiptSavedCheckpoint(async context =>
        {
            if (change == "terminal-swap")
                changedRows = await context.Events.Where(row => row.Id == database.EventId).ExecuteUpdateAsync(update => update
                    .SetProperty(row => row.ActorId, organizerId)
                    .SetProperty(row => row.OrganizerActorId, database.ActorId));
        });
        var succeeds = change == "unchanged";
        await using (var context = database.Context(order, saved))
        {
            var intake = IssuanceCommitter(context, database, database.Caller);
            await Assert.That(context.ModerationTrustDomainId).IsNull();
            var issued = await intake.IssueReadReceiptAsync(grantId, default);
            await Assert.That(issued is not null).IsEqualTo(succeeds);
            await Assert.That(order.SawTenant).IsTrue();
            await Assert.That(order.CallerBeforeFirstActor).IsTrue();
            await Assert.That(order.TrustAtFirstActor).IsNull();
            await Assert.That(order.ActorsBeforeTenant.SequenceEqual(new[] { database.ActorId, organizerId }.Order())).IsTrue();
            await Assert.That(order.ActorAnchors.Contains(replacementId)).IsFalse();
            await Assert.That(order.ActorAnchors.All(id => id == database.ActorId || id == organizerId)).IsTrue();
            await Assert.That(saved.ObservedReceiptSave).IsEqualTo(succeeds || change == "terminal-swap");
            await Assert.That(changedRows).IsEqualTo(succeeds ? 0 : 1);
            await Assert.That(context.Database.CurrentTransaction).IsNull();
            await Assert.That(context.ModerationTrustDomainId).IsNull();
            await Assert.That(context.ChangeTracker.Entries().Any()).IsFalse();
            if (issued is not null)
            {
                await Assert.That(issued.Receipt.RelationshipKind).IsEqualTo(ModerationCaseRelationshipKind.CaseReadGrant);
                await Assert.That(issued.Receipt.RelationshipSourceId).IsEqualTo(grantId);
                await Assert.That(issued.Receipt.SourceId).IsEqualTo(grantBefore.SourceId);
                await Assert.That(issued.Receipt.CallerUserId).IsEqualTo(database.CallerId);
            }
        }

        await using var inspect = database.Context();
        var parent = await inspect.Events.AsNoTracking().SingleAsync(row => row.Id == database.EventId);
        await Assert.That(parent.ActorId).IsEqualTo(original.ActorId);
        await Assert.That(parent.OrganizerActorId).IsEqualTo(original.OrganizerActorId);
        await Assert.That(parent.ConcurrencyStamp).IsEqualTo(original.ConcurrencyStamp);
        var actorAfter = await inspect.Actors.AsNoTracking().SingleAsync(row => row.Id == database.ActorId);
        var serviceAfter = await inspect.ServicePrincipals.AsNoTracking().SingleAsync(row => row.Id == database.ServiceId);
        await Assert.That(actorAfter.ConcurrencyStamp).IsEqualTo(actorBefore.ConcurrencyStamp);
        await Assert.That(actorAfter.ServicePrincipalId).IsEqualTo(actorBefore.ServicePrincipalId);
        await Assert.That(serviceAfter.ConcurrencyStamp).IsEqualTo(serviceBefore.ConcurrencyStamp);
        var receiptsAfter = await inspect.ModerationCaseTargetReceipts.AsNoTracking()
            .Where(row => row.RelationshipKind != ModerationCaseRelationshipKind.CaseReadGrant).OrderBy(row => row.Id)
            .Select(row => new { row.Id, row.Revision, row.ConsumedAt, row.TokenDigest }).ToArrayAsync();
        await Assert.That(receiptsAfter.SequenceEqual(receiptsBefore)).IsTrue();
        await Assert.That(await inspect.ModerationCaseTargetReceipts.CountAsync(
            row => row.RelationshipKind == ModerationCaseRelationshipKind.CaseReadGrant)).IsEqualTo(succeeds ? 1 : 0);
        await Assert.That(await inspect.ModerationCaseTargetSources.CountAsync()).IsEqualTo(sourceCount);
        await Assert.That(await inspect.ModerationCases.CountAsync()).IsEqualTo(caseCount);
        await Assert.That(await inspect.ModerationCaseIntakeAcknowledgements.CountAsync()).IsEqualTo(acknowledgementCount);
        await Assert.That(await inspect.ModerationCaseReadGrants.CountAsync()).IsEqualTo(1);
        var grantAfter = await inspect.ModerationCaseReadGrants.AsNoTracking().SingleAsync(row => row.Id == grantId);
        await Assert.That(grantAfter.TenantId).IsEqualTo(grantBefore.TenantId);
        await Assert.That(grantAfter.TrustDomainId).IsEqualTo(grantBefore.TrustDomainId);
        await Assert.That(grantAfter.CaseId).IsEqualTo(grantBefore.CaseId);
        await Assert.That(grantAfter.CaseRevision).IsEqualTo(grantBefore.CaseRevision);
        await Assert.That(grantAfter.SourceId).IsEqualTo(grantBefore.SourceId);
        await Assert.That(grantAfter.CallerUserId).IsEqualTo(grantBefore.CallerUserId);
        await Assert.That(grantAfter.IssuerUserId).IsEqualTo(grantBefore.IssuerUserId);
        await Assert.That(grantAfter.IssuerRoleGrantId).IsEqualTo(grantBefore.IssuerRoleGrantId);
        await Assert.That(grantAfter.IssuerPlatformGrant).IsEqualTo(grantBefore.IssuerPlatformGrant);
        await Assert.That(grantAfter.IssuedAt).IsEqualTo(grantBefore.IssuedAt);
        await Assert.That(grantAfter.ExpiresAt).IsEqualTo(grantBefore.ExpiresAt);
        await Assert.That(grantAfter.RevokedAt).IsEqualTo(grantBefore.RevokedAt);
        await Assert.That(grantAfter.Revision).IsEqualTo(grantBefore.Revision);
    }

    private sealed class ReadIssuanceActorOrder(
        Guid callerId, Func<ExploreDbContext, CancellationToken, Task> atFirstTenant) : DbCommandInterceptor
    {
        public List<Guid> ActorAnchors { get; } = [];
        public Guid[] ActorsBeforeTenant { get; private set; } = [];
        public bool SawTenant { get; private set; }
        public bool CallerBeforeFirstActor { get; private set; }
        public Guid? TrustAtFirstActor { get; private set; }
        private bool _sawCaller;
        private bool _mutating;

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (_mutating || eventData.Context is not ExploreDbContext context) return result;
            string Table<TEntity>()
            {
                var entity = context.Model.FindEntityType(typeof(TEntity))!;
                return context.GetService<ISqlGenerationHelper>().DelimitIdentifier(entity.GetTableName()!, entity.GetSchema());
            }
            if (command.CommandText.StartsWith($"UPDATE {Table<User>()}", StringComparison.Ordinal)
                && command.Parameters.Cast<DbParameter>().Any(parameter => parameter.Value is Guid id && id == callerId))
                _sawCaller = true;
            if (command.CommandText.StartsWith($"UPDATE {Table<Actor>()}", StringComparison.Ordinal))
            {
                if (ActorAnchors.Count == 0)
                {
                    CallerBeforeFirstActor = _sawCaller;
                    TrustAtFirstActor = context.ModerationTrustDomainId;
                }
                ActorAnchors.AddRange(command.Parameters.Cast<DbParameter>().Select(parameter => parameter.Value).OfType<Guid>());
            }
            if (!SawTenant && command.CommandText.StartsWith($"UPDATE {Table<Tenant>()}", StringComparison.Ordinal))
            {
                SawTenant = true;
                ActorsBeforeTenant = ActorAnchors.ToArray();
                _mutating = true;
                try { await atFirstTenant(context, cancellationToken); }
                finally { _mutating = false; }
            }
            return result;
        }
    }
}
