using System.Data;
using System.Text;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Moderation;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using Explore.Persistence;
using Explore.Persistence.Moderation;
using Explore.Persistence.Repositories;
using ISLAMU.Moderation.Domain;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using DomainEvent = Explore.Domain.Event;

namespace Event.Persistence.IntegrationTests.Moderation;

/// <summary>Real native source guards. Authored only; programme assurance owns execution.</summary>
public sealed partial class ModerationCaseNativeProofTests
{
    [Test]
    [Arguments(ModerationCaseTargetKind.Actor)]
    [Arguments(ModerationCaseTargetKind.Service)]
    public async Task StandaloneTargetAcceptsAndReplaysOriginalAcknowledgementWithoutAnyEvent(ModerationCaseTargetKind kind)
    {
        await using var database = await NativeDatabase.CreateAsync(kind, standalone: true);
        var first = await database.IssueAsync(ModerationCaseRelationshipKind.InstanceAdministrator);
        var copied = await database.IssueAsync(ModerationCaseRelationshipKind.InstanceAdministrator);
        await Assert.That(copied.Receipt.SourceId).IsEqualTo(first.Receipt.SourceId);
        await Assert.That(first.Receipt.Evidence.Target.Origin.Kind).IsEqualTo(ModerationScopeKind.Instance);
        await Assert.That(first.Receipt.RelationshipGeneration.Identity).IsEqualTo(database.AdminId);
        await Assert.That(first.Receipt.RelationshipGeneration.ConcurrencyStamp).IsNull();
        await Assert.That(first.Receipt.RelationshipGeneration.Revision).IsNull();
        await using var context = database.Context();
        await Assert.That(await context.Events.AnyAsync()).IsFalse();
        if (kind == ModerationCaseTargetKind.Service)
            await Assert.That(await context.Actors.AnyAsync(row => row.ServicePrincipalId == database.ServiceId)).IsFalse();
        var source = await context.ModerationCaseTargetSources.SingleAsync();
        await Assert.That(source.EventId).IsNull();
        await Assert.That(source.EventVersion).IsNull();
        var request = new ModerationCaseAccessRequest(ModerationCaseAccessPurpose.SubmitAllegation,
            Guid.Empty, Guid.CreateVersion7(), 0, first.Receipt.Id, first.Token)
        { Narrative = database.Request().Material };
        var committer = Committer(context, database);
        var accepted = await committer.SubmitAsync(request, default);
        await Assert.That(accepted.Outcome).IsEqualTo(ModerationCaseIntakeCommitOutcome.Accepted);
        database.Clock.Now = database.Clock.Now.AddHours(1);
        var replay = await committer.SubmitAsync(request, default);
        await Assert.That(replay.Outcome).IsEqualTo(ModerationCaseIntakeCommitOutcome.Accepted);
        await Assert.That(replay.Acknowledgement!.Id).IsEqualTo(accepted.Acknowledgement!.Id);
        await Assert.That(replay.Acknowledgement.AcceptedAt).IsEqualTo(accepted.Acknowledgement.AcceptedAt);
    }

    [Test]
    [Arguments(ModerationCaseTargetKind.Actor)]
    [Arguments(ModerationCaseTargetKind.Service)]
    public async Task StandaloneSourceDuplicateIsRejectedByDatabaseWithoutIssuanceLock(ModerationCaseTargetKind kind)
    {
        await using var database = await NativeDatabase.CreateAsync(kind, standalone: true);
        var issued = await database.IssueAsync(ModerationCaseRelationshipKind.InstanceAdministrator);
        await using var context = database.Context();
        var source = await context.ModerationCaseTargetSources.SingleAsync(row => row.Id == issued.Receipt.SourceId);
        var competing = ModerationCaseTargetSource.Record(Guid.CreateVersion7(), Guid.CreateVersion7(),
            source.TenantId, source.TrustDomainId, null, null, source.Kind, source.NativeKind,
            source.NativeTargetId, source.NativeTargetVersion, source.NativeFactsDigest,
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            source.OccurrenceDigest, source.MaterialDigest, database.Clock.Now);
        context.ModerationCaseTargetSources.Add(competing);
        await Assert.That(async () => await context.SaveChangesAsync()).Throws<DbUpdateException>();
    }

    [Test]
    [Arguments(ModerationCaseTargetKind.Actor, "source")]
    [Arguments(ModerationCaseTargetKind.Service, "source")]
    [Arguments(ModerationCaseTargetKind.Actor, "grant")]
    [Arguments(ModerationCaseTargetKind.Service, "grant")]
    [Arguments(ModerationCaseTargetKind.Actor, "unrelated-reporter")]
    [Arguments(ModerationCaseTargetKind.Service, "unrelated-reporter")]
    public async Task StandaloneSubmissionRejectsChangedSourceGrantOrUnrelatedReporter(
        ModerationCaseTargetKind kind, string defect)
    {
        await using var database = await NativeDatabase.CreateAsync(kind, standalone: true);
        var issued = await database.IssueAsync(ModerationCaseRelationshipKind.InstanceAdministrator);
        var caller = database.Caller;
        await using (var mutation = database.Context())
        {
            if (defect == "source")
            {
                if (kind == ModerationCaseTargetKind.Actor)
                    await mutation.Actors.Where(row => row.Id == database.ActorId).ExecuteUpdateAsync(update =>
                        update.SetProperty(row => row.ConcurrencyStamp, Guid.CreateVersion7()));
                else
                    await mutation.ServicePrincipals.Where(row => row.Id == database.ServiceId).ExecuteUpdateAsync(update =>
                        update.SetProperty(row => row.ConcurrencyStamp, Guid.CreateVersion7()));
            }
            else if (defect == "grant")
                await mutation.PlatformUserRoles.Where(row => row.Id == database.AdminId).ExecuteUpdateAsync(update =>
                    update.SetProperty(row => row.GrantedAt, database.Clock.Now.UtcDateTime.AddMinutes(-1)));
            else
            {
                var id = Guid.CreateVersion7();
                mutation.Users.Add(new User
                {
                    Id = id, Pii = new UserPii
                    {
                        UserId = id, Email = $"{id:N}@example.invalid", FirstName = "Unrelated", LastName = "Reporter"
                    }
                });
                await mutation.SaveChangesAsync();
                caller = new NativeCaller(id);
            }
        }
        await using var context = database.Context();
        context.CurrentUserService = caller;
        var committer = IssuanceCommitter(context, database, caller);
        var rejected = await committer.SubmitAsync(new(ModerationCaseAccessPurpose.SubmitAllegation,
            Guid.Empty, Guid.CreateVersion7(), 0, issued.Receipt.Id, issued.Token)
        { Narrative = database.Request().Material }, default);
        await Assert.That(rejected.Outcome).IsEqualTo(ModerationCaseIntakeCommitOutcome.Unavailable);
        context.ModerationTrustDomainId = database.TrustId;
        await Assert.That(await context.ModerationCases.AnyAsync()).IsFalse();
        await Assert.That(await context.ModerationCaseIntakeAcknowledgements.AnyAsync()).IsFalse();
    }

    [Test]
    [Arguments(ModerationCaseTargetKind.Actor, ModerationCaseRelationshipKind.EventSubmitter)]
    [Arguments(ModerationCaseTargetKind.Actor, ModerationCaseRelationshipKind.EventRole)]
    [Arguments(ModerationCaseTargetKind.Actor, ModerationCaseRelationshipKind.RegistrationOrder)]
    [Arguments(ModerationCaseTargetKind.Actor, ModerationCaseRelationshipKind.RegistrationParticipant)]
    [Arguments(ModerationCaseTargetKind.Actor, ModerationCaseRelationshipKind.PublicEvent)]
    [Arguments(ModerationCaseTargetKind.Service, ModerationCaseRelationshipKind.EventSubmitter)]
    [Arguments(ModerationCaseTargetKind.Service, ModerationCaseRelationshipKind.EventRole)]
    [Arguments(ModerationCaseTargetKind.Service, ModerationCaseRelationshipKind.RegistrationOrder)]
    [Arguments(ModerationCaseTargetKind.Service, ModerationCaseRelationshipKind.RegistrationParticipant)]
    [Arguments(ModerationCaseTargetKind.Service, ModerationCaseRelationshipKind.PublicEvent)]
    public async Task StandaloneTargetRejectsEventOnlyReporterRelationship(
        ModerationCaseTargetKind kind, ModerationCaseRelationshipKind relationship)
    {
        await using var database = await NativeDatabase.CreateAsync(kind, standalone: true);
        await database.IssueAsync(ModerationCaseRelationshipKind.InstanceAdministrator);
        await using var context = database.Context();
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var request = database.Request(relationship) with { RelationshipSourceId = database.AdminId };
        await Assert.That(await database.Owner(context).IssueSubmissionAsync(request, default)).IsNull();
    }

    [Test]
    [Arguments(ModerationCaseTargetKind.Actor)]
    [Arguments(ModerationCaseTargetKind.Service)]
    public async Task StandaloneIssuanceRejectsUnrelatedReporterAndRevokedNativeGrant(ModerationCaseTargetKind kind)
    {
        await using var database = await NativeDatabase.CreateAsync(kind, standalone: true);
        await database.IssueAsync(ModerationCaseRelationshipKind.InstanceAdministrator);
        var unrelatedId = Guid.CreateVersion7();
        await using (var setup = database.Context())
        {
            setup.Users.Add(new User
            {
                Id = unrelatedId, Pii = new UserPii
                {
                    UserId = unrelatedId, Email = $"{unrelatedId:N}@example.invalid", FirstName = "Unrelated", LastName = "Reporter"
                }
            });
            await setup.SaveChangesAsync();
        }
        await using (var unrelated = database.Context())
        {
            var caller = new NativeCaller(unrelatedId);
            unrelated.CurrentUserService = caller;
            await using var transaction = await unrelated.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var owner = new ModerationCaseTargetProofOwner(unrelated, caller, database.Machine, database.Clock);
            await Assert.That(await owner.IssueSubmissionAsync(
                database.Request(ModerationCaseRelationshipKind.InstanceAdministrator), default)).IsNull();
        }
        await using (var revoked = database.Context())
            await revoked.PlatformUserRoles.Where(row => row.Id == database.AdminId).ExecuteDeleteAsync();
        await using (var denied = database.Context())
        {
            await using var transaction = await denied.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            await Assert.That(await database.Owner(denied).IssueSubmissionAsync(
                database.Request(ModerationCaseRelationshipKind.InstanceAdministrator), default)).IsNull();
        }
        await using var verify = database.Context();
        await Assert.That(await verify.ModerationCaseTargetReceipts.CountAsync()).IsEqualTo(1);
    }

    [Test]
    [Arguments(ModerationCaseTargetKind.Actor)]
    [Arguments(ModerationCaseTargetKind.Service)]
    public async Task StandaloneFinalIssuanceRecheckRejectsNativeSourceChange(ModerationCaseTargetKind kind)
    {
        await using var database = await NativeDatabase.CreateAsync(kind, standalone: true);
        await using var context = database.Context();
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var owner = database.Owner(context);
        var issued = await owner.IssueSubmissionAsync(database.Request(ModerationCaseRelationshipKind.InstanceAdministrator), default);
        await Assert.That(issued).IsNotNull();
        await context.SaveChangesAsync();
        await Assert.That(await owner.RecheckIssuanceAsync(default)).IsTrue();
        if (kind == ModerationCaseTargetKind.Actor)
            await context.Actors.Where(row => row.Id == database.ActorId).ExecuteUpdateAsync(update =>
                update.SetProperty(row => row.IsSuspended, true));
        else
            await context.ServicePrincipals.Where(row => row.Id == database.ServiceId).ExecuteUpdateAsync(update =>
                update.SetProperty(row => row.IsDeleted, true));
        await Assert.That(await owner.RecheckIssuanceAsync(default)).IsFalse();
        await transaction.RollbackAsync();
        await using var verify = database.Context();
        await Assert.That(await verify.ModerationCaseTargetSources.AnyAsync()).IsFalse();
        await Assert.That(await verify.ModerationCaseTargetReceipts.AnyAsync()).IsFalse();
    }

    [Test]
    [Arguments(ModerationCaseTargetKind.Actor, false)]
    [Arguments(ModerationCaseTargetKind.Service, false)]
    [Arguments(ModerationCaseTargetKind.Actor, true)]
    [Arguments(ModerationCaseTargetKind.Service, true)]
    public async Task StandaloneFinalIntakeCheckpointRejectsChangedSourceOrGrantFacts(
        ModerationCaseTargetKind kind, bool changeGrant)
    {
        await using var database = await NativeDatabase.CreateAsync(kind, standalone: true);
        var issued = await database.IssueAsync(ModerationCaseRelationshipKind.InstanceAdministrator);
        await using var context = database.Context();
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var intake = new ModerationCaseIntakeRepository(context,
            new ModerationCaseTargetReceiptRepository(context, database.Caller, database.Machine, database.Clock),
            database.Owner(context), database.Clock);
        var request = new ModerationCaseAccessRequest(ModerationCaseAccessPurpose.SubmitAllegation,
            Guid.Empty, Guid.CreateVersion7(), 0, issued.Receipt.Id, issued.Token);
        var stage = await intake.StageAsync(request, default);
        await Assert.That(stage).IsNotNull();
        await Assert.That(stage!.Case.Subject).IsNull();
        var receipt = stage.Receipt;
        receipt.Consume(receipt.TenantId, receipt.CallerUserId, receipt.HostBindingId,
            receipt.QualificationEpochId, receipt.QualificationRevision, receipt.RelationshipSourceId,
            receipt.RelationshipGeneration, receipt.Evidence, System.Security.Cryptography.SHA256.HashData(issued.Token.Span),
            stage.Case.Id, request.OperationId, receipt.Revision, database.Clock.Now);
        await context.SaveChangesAsync();
        await Assert.That(await intake.RecheckStagedAsync(default)).IsTrue();
        if (changeGrant)
            await context.PlatformUserRoles.Where(row => row.Id == database.AdminId).ExecuteUpdateAsync(update =>
                update.SetProperty(row => row.GrantedBy, (Guid?)database.CallerId));
        else if (kind == ModerationCaseTargetKind.Actor)
            await context.Actors.Where(row => row.Id == database.ActorId).ExecuteUpdateAsync(update =>
                update.SetProperty(row => row.IsSuspended, true));
        else
            await context.ServicePrincipals.Where(row => row.Id == database.ServiceId).ExecuteUpdateAsync(update =>
                update.SetProperty(row => row.Code, $"changed-{database.ServiceId:N}"));
        await Assert.That(await intake.RecheckStagedAsync(default)).IsFalse();
        await transaction.RollbackAsync();
        await using var verify = database.Context();
        await Assert.That(await verify.ModerationCases.AnyAsync()).IsFalse();
        await Assert.That((await verify.ModerationCaseTargetReceipts.SingleAsync()).ConsumedAt).IsNull();
    }

    [Test]
    public async Task PublicEventReport_QualifiesUnrelatedAccountWithoutPrivateOrReadAuthority()
    {
        await using var database = await NativeDatabase.CreateAsync(ModerationCaseTargetKind.EventVersion);
        var reporterId = await PublicPublisherAsync(database);
        var caller = new NativeCaller(reporterId);
        var issued = await IssuePublicAsync(database, caller);
        await Assert.That(issued).IsNotNull();
        await Assert.That(issued!.Receipt.CallerUserId).IsEqualTo(reporterId);
        await Assert.That(issued.Receipt.RelationshipKind).IsEqualTo(ModerationCaseRelationshipKind.PublicEvent);
        await using var context = database.Context();
        context.CurrentUserService = caller;
        context.ModerationTrustDomainId = null;
        var owner = new ModerationCaseTargetProofOwner(context, caller, database.Machine, database.Clock);
        var intake = new ModerationCaseIntakeRepository(context,
            new ModerationCaseTargetReceiptRepository(context, caller, database.Machine, database.Clock),
            owner, database.Clock);
        var request = new ModerationCaseAccessRequest(ModerationCaseAccessPurpose.SubmitAllegation,
            Guid.Empty, Guid.CreateVersion7(), 0, issued.Receipt.Id, issued.Token)
        { Narrative = database.Request().Material };
        var accepted = await IssuanceCommitter(context, database, caller).SubmitAsync(request, default);
        await Assert.That(accepted.Outcome).IsEqualTo(ModerationCaseIntakeCommitOutcome.Accepted);
        context.ModerationTrustDomainId = database.TrustId;
        await using var read = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await Assert.That(await new ModerationCaseIntakeRepository(context,
            new ModerationCaseTargetReceiptRepository(context, caller, database.Machine, database.Clock),
            new ModerationCaseTargetProofOwner(context, caller, database.Machine, database.Clock), database.Clock)
            .ReadAsync(request with
            {
                Purpose = ModerationCaseAccessPurpose.ReadAllegation, ExpectedCaseRevision = 1,
                CaseId = accepted.Acknowledgement!.CaseId,
                OperationId = Guid.Empty, Narrative = ReadOnlyMemory<byte>.Empty
            }, default)).IsNull();
    }

    [Test]
    [Arguments("private")]
    [Arguments("draft")]
    [Arguments("publisher-suspended")]
    [Arguments("publisher-membership")]
    public async Task PublicEventReport_RejectsLostVisibilityAfterPositiveControl(string defect)
    {
        await using var database = await NativeDatabase.CreateAsync(ModerationCaseTargetKind.EventVersion);
        var caller = new NativeCaller(await PublicPublisherAsync(database));
        await Assert.That(await IssuePublicAsync(database, caller)).IsNotNull();
        await using (var context = database.Context())
        {
            switch (defect)
            {
                case "private":
                    await context.Events.Where(row => row.Id == database.EventId).ExecuteUpdateAsync(update =>
                        update.SetProperty(row => row.VisibilityTypeId, (int)VisibilityTypeEnum.Private));
                    break;
                case "draft":
                    await context.Events.Where(row => row.Id == database.EventId).ExecuteUpdateAsync(update =>
                        update.SetProperty(row => row.EventStatusId, (int)EventStatusEnum.Draft));
                    break;
                case "publisher-suspended":
                    await context.Actors.Where(row => row.Id == database.ActorId).ExecuteUpdateAsync(update =>
                        update.SetProperty(row => row.IsSuspended, true));
                    break;
                case "publisher-membership":
                    await context.TenantUsers.Where(row => row.ActorId == database.ActorId).ExecuteUpdateAsync(update =>
                        update.SetProperty(row => row.IsDeleted, true));
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(defect));
            }
        }
        await Assert.That(await IssuePublicAsync(database, caller)).IsNull();
        await using var retained = database.Context();
        await Assert.That(await retained.ModerationCaseTargetReceipts.CountAsync()).IsEqualTo(1);
        await Assert.That(await retained.ModerationCaseIntakeAcknowledgements.CountAsync()).IsEqualTo(0);
    }

    private static async Task<Guid> PublicPublisherAsync(NativeDatabase database)
    {
        await using var context = database.Context();
        var publisher = await context.Users.SingleAsync(row => row.Id == database.CallerId);
        var tenant = await context.Tenants.SingleAsync(row => row.Id == database.TenantId);
        var actor = await context.Actors.SingleAsync(row => row.Id == database.ActorId);
        actor.ActorTypeId = (int)ActorTypeEnum.User;
        actor.ServicePrincipalId = null;
        actor.UserId = publisher.Id;
        actor.ConcurrencyStamp = Guid.CreateVersion7();
        await context.Events.Where(row => row.Id == database.EventId).ExecuteUpdateAsync(update =>
            update.SetProperty(row => row.EventStatusId, (int)EventStatusEnum.Published)
                .SetProperty(row => row.VisibilityTypeId, (int)VisibilityTypeEnum.Public)
                .SetProperty(row => row.ConcurrencyStamp, Guid.CreateVersion7()));
        context.TenantUsers.Add(new TenantUser
        {
            Id = Guid.CreateVersion7(), TenantId = tenant.Id, Tenant = tenant,
            UserId = publisher.Id, User = publisher, ActorId = actor.Id, Actor = actor,
            StatusId = (int)TenantUserStatusEnum.Active
        });
        var reporterId = Guid.CreateVersion7();
        context.Users.Add(new User
        {
            Id = reporterId, Pii = new UserPii
            {
                UserId = reporterId, Email = $"{reporterId:N}@example.invalid", FirstName = "Public", LastName = "Reporter"
            }
        });
        await context.SaveChangesAsync();
        return reporterId;
    }

    private static async Task<ModerationCaseIssuedReceipt?> IssuePublicAsync(NativeDatabase database, NativeCaller caller)
    {
        await using var context = database.Context();
        context.CurrentUserService = caller;
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var owner = new ModerationCaseTargetProofOwner(context, caller, database.Machine, database.Clock);
        var issued = await owner.IssueSubmissionAsync(database.Request(ModerationCaseRelationshipKind.PublicEvent), default);
        if (issued is null) return null;
        await context.SaveChangesAsync();
        await Assert.That(await owner.RecheckIssuanceAsync(default)).IsTrue();
        await transaction.CommitAsync();
        return issued;
    }

    [Test]
    [Arguments(ModerationCaseTargetKind.EventVersion)]
    [Arguments(ModerationCaseTargetKind.ParticipantIncident)]
    [Arguments(ModerationCaseTargetKind.Actor)]
    [Arguments(ModerationCaseTargetKind.Service)]
    [Arguments(ModerationCaseTargetKind.Resource)]
    [Arguments(ModerationCaseTargetKind.Form)]
    [Arguments(ModerationCaseTargetKind.Destination)]
    [Arguments(ModerationCaseTargetKind.HiddenContentReceipt)]
    [Arguments(ModerationCaseTargetKind.LocalIncident)]
    public async Task NativeCommitter_AtomicallyAcceptsAndReplaysOriginalAcknowledgementAfterExpiry(ModerationCaseTargetKind kind)
    {
        await using var database = await NativeDatabase.CreateAsync(kind);
        var issued = await database.IssueAsync();
        var originalToken = issued.Token.ToArray();
        await using var context = database.Context();
        var committer = Committer(context, database);
        var request = new ModerationCaseAccessRequest(ModerationCaseAccessPurpose.SubmitAllegation,
            Guid.Empty, Guid.CreateVersion7(), 0, issued.Receipt.Id, issued.Token)
        { Narrative = database.Request().Material };
        var accepted = await committer.SubmitAsync(request, default);
        await Assert.That(accepted.Outcome).IsEqualTo(ModerationCaseIntakeCommitOutcome.Accepted);
        await Assert.That(accepted.Acknowledgement).IsNotNull();
        var original = accepted.Acknowledgement!;
        database.Clock.Now = database.Clock.Now.AddHours(1);
        var replay = await committer.SubmitAsync(request, default);
        await Assert.That(replay.Outcome).IsEqualTo(ModerationCaseIntakeCommitOutcome.Accepted);
        await Assert.That(replay.Acknowledgement!.Id).IsEqualTo(original.Id);
        await Assert.That(replay.Acknowledgement.AcceptedAt).IsEqualTo(original.AcceptedAt);
        await Assert.That(issued.Token.ToArray().SequenceEqual(originalToken)).IsTrue();
        context.ModerationTrustDomainId = original.TrustDomainId;
        await Assert.That(await context.ModerationCases.CountAsync()).IsEqualTo(1);
        await Assert.That(await context.ModerationCaseIntakeAcknowledgements.CountAsync()).IsEqualTo(1);
        await Assert.That((await context.ModerationCaseTargetReceipts.SingleAsync()).Revision).IsEqualTo(2);
    }

    [Test]
    [Arguments("receipt")]
    [Arguments("token")]
    [Arguments("narrative")]
    public async Task NativeCommitter_RejectsChangedRequestUnderTheSameAcceptedOperation(string defect)
    {
        await using var database = await NativeDatabase.CreateAsync(ModerationCaseTargetKind.EventVersion);
        var issued = await database.IssueAsync();
        await using var context = database.Context();
        var committer = Committer(context, database);
        var request = new ModerationCaseAccessRequest(ModerationCaseAccessPurpose.SubmitAllegation,
            Guid.Empty, Guid.CreateVersion7(), 0, issued.Receipt.Id, issued.Token)
        { Narrative = database.Request().Material };
        var accepted = await committer.SubmitAsync(request, default);
        await Assert.That(accepted.Outcome).IsEqualTo(ModerationCaseIntakeCommitOutcome.Accepted);
        var changedToken = issued.Token.ToArray();
        changedToken[0] ^= 1;
        var changed = defect switch
        {
            "receipt" => request with { ReceiptId = Guid.CreateVersion7() },
            "token" => request with { PresentedToken = changedToken },
            "narrative" => request with { Narrative = Encoding.UTF8.GetBytes("Different alleged material") },
            _ => throw new ArgumentOutOfRangeException(nameof(defect))
        };
        var rejected = await committer.SubmitAsync(changed, default);
        await Assert.That(rejected.Outcome).IsEqualTo(ModerationCaseIntakeCommitOutcome.Conflict);
        await Assert.That(rejected.Acknowledgement).IsNull();
        context.ModerationTrustDomainId = accepted.Acknowledgement!.TrustDomainId;
        await Assert.That(await context.ModerationCaseIntakeAcknowledgements.CountAsync()).IsEqualTo(1);
        await Assert.That(await context.ModerationCases.CountAsync()).IsEqualTo(1);
        await Assert.That((await context.ModerationCaseTargetReceipts.SingleAsync()).Revision).IsEqualTo(2);
    }

    [Test]
    public async Task NativeCommitter_CannotRecreateMissingAcknowledgementFromConsumedReceipt()
    {
        await using var database = await NativeDatabase.CreateAsync(ModerationCaseTargetKind.EventVersion);
        var issued = await database.IssueAsync();
        await using var context = database.Context();
        var committer = Committer(context, database);
        var request = new ModerationCaseAccessRequest(ModerationCaseAccessPurpose.SubmitAllegation,
            Guid.Empty, Guid.CreateVersion7(), 0, issued.Receipt.Id, issued.Token)
        { Narrative = database.Request().Material };
        var accepted = await committer.SubmitAsync(request, default);
        await Assert.That(accepted.Outcome).IsEqualTo(ModerationCaseIntakeCommitOutcome.Accepted);
        context.ModerationTrustDomainId = accepted.Acknowledgement!.TrustDomainId;
        await context.ModerationCaseIntakeAcknowledgements.ExecuteDeleteAsync();
        context.ChangeTracker.Clear();
        context.ModerationTrustDomainId = null;
        committer = Committer(context, database);
        var replay = await committer.SubmitAsync(request, default);
        await Assert.That(replay.Outcome).IsEqualTo(ModerationCaseIntakeCommitOutcome.Unavailable);
        context.ModerationTrustDomainId = accepted.Acknowledgement.TrustDomainId;
        await Assert.That(await context.ModerationCaseIntakeAcknowledgements.CountAsync()).IsEqualTo(0);
        await Assert.That(await context.ModerationCases.CountAsync()).IsEqualTo(1);
        await Assert.That((await context.ModerationCaseTargetReceipts.SingleAsync()).Revision).IsEqualTo(2);
    }

    private static ModerationCaseIntakeCommitter Committer(ExploreDbContext context, NativeDatabase database)
        => IssuanceCommitter(context, database, database.Caller);

    [Test]
    public async Task NativeIntake_RolledBackTransactionCannotKeepOrBlockFreshAuthorityFences()
    {
        await using var database = await NativeDatabase.CreateAsync(ModerationCaseTargetKind.EventVersion);
        var issued = await database.IssueAsync();
        await using var context = database.Context();
        var owner = database.Owner(context);
        var intake = new ModerationCaseIntakeRepository(context,
            new ModerationCaseTargetReceiptRepository(context, database.Caller, database.Machine, database.Clock),
            owner, database.Clock);
        var request = new ModerationCaseAccessRequest(ModerationCaseAccessPurpose.SubmitAllegation,
            Guid.Empty, Guid.CreateVersion7(), 0, issued.Receipt.Id, issued.Token);
        await using (var first = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            await Assert.That(await intake.StageAsync(request, default)).IsNotNull();
            await context.SaveChangesAsync();
            await first.RollbackAsync();
        }
        context.ChangeTracker.Clear();
        await Assert.That(await intake.RecheckStagedAsync(default)).IsFalse();
        await using (var second = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            var fresh = await intake.StageAsync(request, default);
            await Assert.That(fresh).IsNotNull();
            var receipt = fresh!.Receipt;
            receipt.Consume(receipt.TenantId, receipt.CallerUserId, receipt.HostBindingId,
                receipt.QualificationEpochId, receipt.QualificationRevision, receipt.RelationshipSourceId,
                receipt.RelationshipGeneration, receipt.Evidence, System.Security.Cryptography.SHA256.HashData(issued.Token.Span),
                fresh.Case.Id, request.OperationId, receipt.Revision, database.Clock.Now);
            await context.SaveChangesAsync();
            await Assert.That(await intake.RecheckStagedAsync(default)).IsTrue();
            await second.RollbackAsync();
        }
    }

    [Test]
    [Arguments(ModerationCaseTargetKind.EventVersion)]
    [Arguments(ModerationCaseTargetKind.ParticipantIncident)]
    [Arguments(ModerationCaseTargetKind.Actor)]
    [Arguments(ModerationCaseTargetKind.Service)]
    [Arguments(ModerationCaseTargetKind.Resource)]
    [Arguments(ModerationCaseTargetKind.Form)]
    [Arguments(ModerationCaseTargetKind.Destination)]
    [Arguments(ModerationCaseTargetKind.HiddenContentReceipt)]
    [Arguments(ModerationCaseTargetKind.LocalIncident)]
    public async Task EveryNativeTargetStagesUnattributedAllegationUnderExactProof(ModerationCaseTargetKind kind)
    {
        await using var database = await NativeDatabase.CreateAsync(kind);
        var issued = await database.IssueAsync();
        await using var context = database.Context();
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var targetOwner = database.Owner(context);
        var intake = new ModerationCaseIntakeRepository(context,
            new ModerationCaseTargetReceiptRepository(context, database.Caller, database.Machine, database.Clock),
            targetOwner, database.Clock);
        var request = new ModerationCaseAccessRequest(ModerationCaseAccessPurpose.SubmitAllegation,
            Guid.Empty, Guid.CreateVersion7(), 0, issued.Receipt.Id, issued.Token);
        var stage = await intake.StageAsync(request, default);
        await Assert.That(stage).IsNotNull();
        await Assert.That(stage!.Case.Subject).IsNull();
        await Assert.That(stage.Case.Target).IsEqualTo(issued.Receipt.Evidence.Target);
        await Assert.That(stage.Receipt.RelationshipGeneration)
            .IsEqualTo(ModerationCaseRelationshipGeneration.FromRevision(7));
        stage.Receipt.Consume(stage.Receipt.TenantId, stage.Receipt.CallerUserId, stage.Receipt.HostBindingId,
            stage.Receipt.QualificationEpochId, stage.Receipt.QualificationRevision,
            stage.Receipt.RelationshipSourceId, stage.Receipt.RelationshipGeneration, stage.Receipt.Evidence,
            System.Security.Cryptography.SHA256.HashData(issued.Token.Span), stage.Case.Id, request.OperationId, 1, database.Clock.Now);
        await context.SaveChangesAsync();
        await Assert.That(await intake.RecheckStagedAsync(default)).IsTrue();
        if (kind == ModerationCaseTargetKind.LocalIncident)
        {
            await Assert.That(stage.Case.Target!.Id).IsNotEqualTo(database.ParticipantId);
            await Assert.That(stage.Case.Target.Id).IsEqualTo(stage.Case.OccurrenceId);
        }
        await transaction.CommitAsync();
    }

    [Test]
    [Arguments(ModerationCaseRelationshipKind.EventSubmitter)]
    [Arguments(ModerationCaseRelationshipKind.RegistrationOrder)]
    [Arguments(ModerationCaseRelationshipKind.RegistrationParticipant)]
    public async Task NativeGuidRelationshipStampIsRetainedWithoutCounterConversion(ModerationCaseRelationshipKind kind)
    {
        await using var database = await NativeDatabase.CreateAsync(ModerationCaseTargetKind.Form);
        var issued = await database.IssueAsync(kind);
        await using var context = database.Context();
        var expected = kind switch
        {
            ModerationCaseRelationshipKind.EventSubmitter =>
                await context.Events.Where(row => row.Id == database.EventId).Select(row => row.ConcurrencyStamp).SingleAsync(),
            ModerationCaseRelationshipKind.RegistrationOrder =>
                await context.RegistrationOrders.Where(row => row.Id == database.OrderId).Select(row => row.ConcurrencyStamp).SingleAsync(),
            _ => await context.RegistrationParticipants.Where(row => row.Id == database.ParticipantId)
                .Select(row => row.ConcurrencyStamp).SingleAsync()
        };
        await Assert.That(issued.Receipt.RelationshipConcurrencyStamp).IsEqualTo(expected);
        await Assert.That(issued.Receipt.RelationshipRevision).IsNull();
    }

    [Test]
    [Arguments("role")]
    [Arguments("event")]
    [Arguments("policy")]
    [Arguments("provider")]
    [Arguments("expiry")]
    public async Task FinalIssuanceCheckpointRejectsChangedNativeAuthority(string defect)
    {
        await using var database = await NativeDatabase.CreateAsync(ModerationCaseTargetKind.Form);
        await using var context = database.Context();
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var owner = database.Owner(context);
        var issued = await owner.IssueSubmissionAsync(database.Request(), default);
        await Assert.That(issued).IsNotNull();
        await context.SaveChangesAsync();
        await Assert.That(await owner.RecheckIssuanceAsync(default)).IsTrue();
        switch (defect)
        {
            case "role":
                (await context.EventRoleAssignments.SingleAsync(row => row.Id == database.RoleId))
                    .Revoke(database.CallerId, database.Clock.Now.UtcDateTime);
                break;
            case "event":
                (await context.Events.SingleAsync(row => row.Id == database.EventId)).Title = "Changed material";
                break;
            case "policy":
                (await context.ModerationCaseTargetPolicies.SingleAsync()).Change(1,
                    TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1), false, database.CallerId);
                break;
            case "provider":
                (await context.Set<RegistrationProviderConnection>().SingleAsync(row => row.Id == database.ProviderId))
                    .RecordAccessValidated(database.Clock.Now.AddSeconds(1).UtcDateTime);
                break;
            case "expiry": database.Clock.Now = issued!.Receipt.ExpiresAt; break;
            default: throw new ArgumentOutOfRangeException(nameof(defect));
        }
        await context.SaveChangesAsync();
        await Assert.That(await owner.RecheckIssuanceAsync(default)).IsFalse();
        await transaction.RollbackAsync();
        await using var verify = database.Context();
        await Assert.That(await verify.ModerationCaseTargetReceipts.AnyAsync()).IsFalse();
        await Assert.That(await verify.ModerationCaseTargetSources.AnyAsync()).IsFalse();
    }

    [Test]
    [Arguments(false, "order-version")]
    [Arguments(false, "order-deleted")]
    [Arguments(false, "participant-version")]
    [Arguments(false, "participant-deleted")]
    [Arguments(true, "order-version")]
    [Arguments(true, "order-deleted")]
    [Arguments(true, "participant-version")]
    [Arguments(true, "participant-deleted")]
    public async Task ParticipantAuthority_RetainsBothNativeGenerationsThroughFinalCheckpoint(
        bool relationship, string defect)
    {
        await using var database = await NativeDatabase.CreateAsync(relationship
            ? ModerationCaseTargetKind.EventVersion : ModerationCaseTargetKind.ParticipantIncident);
        await using var context = database.Context();
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var owner = database.Owner(context);
        var issued = await owner.IssueSubmissionAsync(database.Request(relationship
            ? ModerationCaseRelationshipKind.RegistrationParticipant : ModerationCaseRelationshipKind.EventRole), default);
        await Assert.That(issued).IsNotNull();
        await context.SaveChangesAsync();
        await Assert.That(await owner.RecheckIssuanceAsync(default)).IsTrue();
        switch (defect)
        {
            case "order-version":
                await context.RegistrationOrders.Where(row => row.Id == database.OrderId).ExecuteUpdateAsync(update =>
                    update.SetProperty(row => row.ConcurrencyStamp, Guid.CreateVersion7()));
                break;
            case "order-deleted":
                await context.RegistrationOrders.Where(row => row.Id == database.OrderId).ExecuteUpdateAsync(update =>
                    update.SetProperty(row => row.IsDeleted, true));
                break;
            case "participant-version":
                await context.RegistrationParticipants.Where(row => row.Id == database.ParticipantId).ExecuteUpdateAsync(update =>
                    update.SetProperty(row => row.ConcurrencyStamp, Guid.CreateVersion7()));
                break;
            case "participant-deleted":
                await context.RegistrationParticipants.Where(row => row.Id == database.ParticipantId).ExecuteUpdateAsync(update =>
                    update.SetProperty(row => row.IsDeleted, true));
                break;
            default: throw new ArgumentOutOfRangeException(nameof(defect));
        }
        await Assert.That(await owner.RecheckIssuanceAsync(default)).IsFalse();
        await transaction.RollbackAsync();
        await using var verify = database.Context();
        await Assert.That(await verify.ModerationCaseTargetReceipts.AnyAsync()).IsFalse();
        await Assert.That(await verify.ModerationCaseTargetSources.AnyAsync()).IsFalse();
    }

    [Test]
    public async Task StoredResourceProofHoldsExactOwnedObjectAndImmutableProviderBinding()
    {
        await using var database = await NativeDatabase.CreateAsync(ModerationCaseTargetKind.Resource);
        Guid storageId;
        await using (var context = database.Context())
        {
            var binding = StorageProviderBinding.Local(Path.GetTempPath());
            var storage = new StorageObject
            {
                Id = Guid.CreateVersion7(), TenantId = database.TenantId, Tenant = null!,
                FileTypeId = await context.FileTypes.Select(row => row.Id).FirstAsync(), FileType = null!,
                Provider = StorageProviders.Local, StorageProviderBindingId = binding.Id,
                ObjectKey = $"moderation-test/{Guid.CreateVersion7():N}", FullName = "source.pdf", SafeDisplayName = "source.pdf",
                Extension = ".pdf", ContentType = "application/pdf", Size = 1,
                Visibility = StorageObjectVisibilities.PrivateOwner, Purpose = StorageObjectPurposes.EventResource,
                OwningResourceKind = StorageOwningResourceKinds.EventResource, OwningResourceId = database.ResourceId,
                LifecycleState = StorageObjectLifecycleStates.Active
            };
            storageId = storage.Id;
            var resource = await context.Set<EventResource>().SingleAsync(row => row.Id == database.ResourceId);
            resource.SetStoredFile(storage.Id, resource.ConcurrencyStamp, database.CallerId, database.Clock.Now.UtcDateTime);
            context.AddRange(binding, storage);
            await context.SaveChangesAsync();
        }
        var issued = await database.IssueAsync();
        await using var access = database.Context();
        await using var transaction = await access.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var owner = database.Owner(access);
        var request = new ModerationCaseAccessRequest(ModerationCaseAccessPurpose.SubmitAllegation,
            Guid.Empty, Guid.CreateVersion7(), 0, issued.Receipt.Id, issued.Token);
        var proof = await owner.AcquireAsync(request, issued.Receipt, transaction.TransactionId, database.Clock.Now, default);
        await Assert.That(proof).IsNotNull();
        await Assert.That(await owner.RecheckAsync(proof!, database.Clock.Now, default)).IsNotNull();
        (await access.Set<StorageObject>().SingleAsync(row => row.Id == storageId))
            .MarkQuarantined(database.CallerId, "Source changed under the held object fence", database.Clock.Now.UtcDateTime);
        await access.SaveChangesAsync();
        await Assert.That(await owner.RecheckAsync(proof!, database.Clock.Now, default)).IsNull();
    }

    [Test]
    public async Task StagedCaseCannotCommitAfterItsHeldRelationshipIsRevoked()
    {
        await using var database = await NativeDatabase.CreateAsync(ModerationCaseTargetKind.LocalIncident);
        var issued = await database.IssueAsync();
        await using var context = database.Context();
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var intake = new ModerationCaseIntakeRepository(context,
            new ModerationCaseTargetReceiptRepository(context, database.Caller, database.Machine, database.Clock),
            database.Owner(context), database.Clock);
        var request = new ModerationCaseAccessRequest(ModerationCaseAccessPurpose.SubmitAllegation,
            Guid.Empty, Guid.CreateVersion7(), 0, issued.Receipt.Id, issued.Token);
        var stage = await intake.StageAsync(request, default);
        await Assert.That(stage).IsNotNull();
        stage!.Receipt.Consume(stage.Receipt.TenantId, stage.Receipt.CallerUserId, stage.Receipt.HostBindingId,
            stage.Receipt.QualificationEpochId, stage.Receipt.QualificationRevision, stage.Receipt.RelationshipSourceId,
            stage.Receipt.RelationshipGeneration, stage.Receipt.Evidence,
            System.Security.Cryptography.SHA256.HashData(issued.Token.Span),
            stage.Case.Id, request.OperationId, 1, database.Clock.Now);
        await context.SaveChangesAsync();
        await Assert.That(await intake.RecheckStagedAsync(default)).IsTrue();
        (await context.EventRoleAssignments.SingleAsync(row => row.Id == database.RoleId))
            .Revoke(database.CallerId, database.Clock.Now.UtcDateTime);
        await context.SaveChangesAsync();
        await Assert.That(await intake.RecheckStagedAsync(default)).IsFalse();
        await transaction.RollbackAsync();
        await using var verify = database.Context();
        await Assert.That(await verify.ModerationCases.AnyAsync()).IsFalse();
        await Assert.That((await verify.ModerationCaseTargetReceipts.SingleAsync(row => row.Id == issued.Receipt.Id))
            .ConsumedCaseId).IsNull();
    }

    [Test]
    public async Task CopiedMaterialDeduplicatesProvenanceButRetainsNewFacts()
    {
        await using var database = await NativeDatabase.CreateAsync(ModerationCaseTargetKind.LocalIncident);
        var first = await database.IssueAsync();
        var duplicate = await database.IssueAsync();
        var newFact = await database.IssueAsync(material: "A distinct first-hand material fact");
        await Assert.That(duplicate.Receipt.SourceId).IsEqualTo(first.Receipt.SourceId);
        await Assert.That(duplicate.Receipt.MaterialFactId).IsEqualTo(first.Receipt.MaterialFactId);
        await Assert.That(newFact.Receipt.SourceId).IsNotEqualTo(first.Receipt.SourceId);
        await Assert.That(newFact.Receipt.MaterialFactId).IsNotEqualTo(first.Receipt.MaterialFactId);
        await Assert.That(newFact.Receipt.OccurrenceId).IsEqualTo(first.Receipt.OccurrenceId);
        await Assert.That(newFact.Receipt.Evidence.Target).IsEqualTo(first.Receipt.Evidence.Target);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ReadIssuance_ReacquiresAuthorityAfterPreviousTransaction(bool commitPrevious)
    {
        await using var database = await NativeDatabase.CreateAsync(ModerationCaseTargetKind.EventVersion);
        var submitted = await database.IssueAsync();
        Guid grantId;
        await using (var setup = database.Context())
        {
            var allegation = ModerationCase.OpenAllegation(Guid.CreateVersion7(),
                submitted.Receipt.Evidence.SourceOrigin, submitted.Receipt.Evidence, database.Clock.Now);
            setup.ModerationCases.Add(allegation);
            await setup.SaveChangesAsync();
            await using var transaction = await setup.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var issuer = database.Owner(setup);
            var grant = await issuer.GrantReadAsync(new(Guid.CreateVersion7(), allegation.Id, allegation.Revision,
                submitted.Receipt.SourceId, database.CallerId, database.Clock.Now.AddMinutes(1)), default);
            await Assert.That(grant).IsNotNull();
            grantId = grant!.Id;
            await setup.SaveChangesAsync();
            await Assert.That(await issuer.RecheckIssuanceAsync(default)).IsTrue();
            await transaction.CommitAsync();
        }
        await using var context = database.Context();
        var owner = database.Owner(context);
        Guid previousReceiptId;
        await using (var previous = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            var issued = await owner.IssueReadAsync(grantId, default);
            await Assert.That(issued).IsNotNull();
            previousReceiptId = issued!.Receipt.Id;
            await context.SaveChangesAsync();
            await Assert.That(await owner.RecheckIssuanceAsync(default)).IsTrue();
            if (commitPrevious) await previous.CommitAsync();
            else await previous.RollbackAsync();
        }
        context.ChangeTracker.Clear();
        await using (var current = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            var issued = await owner.IssueReadAsync(grantId, default);
            await Assert.That(issued).IsNotNull();
            await Assert.That(issued!.Receipt.Id).IsNotEqualTo(previousReceiptId);
            await context.SaveChangesAsync();
            await Assert.That(await owner.RecheckIssuanceAsync(default)).IsTrue();
            await current.CommitAsync();
        }
        await using var verify = database.Context();
        await Assert.That(await verify.ModerationCaseTargetReceipts.CountAsync(row =>
            row.RelationshipKind == ModerationCaseRelationshipKind.CaseReadGrant)).IsEqualTo(commitPrevious ? 2 : 1);
    }

    [Test]
    [Arguments(ModerationCaseTargetKind.EventVersion)]
    [Arguments(ModerationCaseTargetKind.Actor)]
    [Arguments(ModerationCaseTargetKind.Service)]
    public async Task SubmissionDoesNotGrantReadAndExplicitReadGrantIsCaseBoundAndRevocable(ModerationCaseTargetKind kind)
    {
        var standalone = kind is ModerationCaseTargetKind.Actor or ModerationCaseTargetKind.Service;
        await using var database = await NativeDatabase.CreateAsync(kind, standalone);
        var submitted = await database.IssueAsync(standalone
            ? ModerationCaseRelationshipKind.InstanceAdministrator : ModerationCaseRelationshipKind.EventRole);
        Guid caseId;
        await using (var context = database.Context())
        {
            var allegation = ModerationCase.OpenAllegation(Guid.CreateVersion7(),
                submitted.Receipt.Evidence.SourceOrigin, submitted.Receipt.Evidence, database.Clock.Now);
            caseId = allegation.Id;
            context.ModerationCases.Add(allegation);
            await context.SaveChangesAsync();
        }
        await using (var denied = database.Context())
        {
            await using var transaction = await denied.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var owner = database.Owner(denied);
            var read = new ModerationCaseAccessRequest(ModerationCaseAccessPurpose.ReadAllegation,
                caseId, Guid.Empty, 1, submitted.Receipt.Id, submitted.Token);
            await Assert.That(await owner.AcquireAsync(read, submitted.Receipt,
                transaction.TransactionId, database.Clock.Now, default)).IsNull();
        }
        Guid grantId;
        await using (var context = database.Context())
        {
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var owner = database.Owner(context);
            var grant = await owner.GrantReadAsync(new(Guid.CreateVersion7(), caseId, 1,
                submitted.Receipt.SourceId, database.CallerId, database.Clock.Now.AddMinutes(1)), default);
            await Assert.That(grant).IsNotNull();
            grantId = grant!.Id;
            await context.SaveChangesAsync();
            await Assert.That(await owner.RecheckIssuanceAsync(default)).IsTrue();
            await transaction.CommitAsync();
        }
        ModerationCaseIssuedReceipt issued;
        await using (var context = database.Context())
        {
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var owner = database.Owner(context);
            issued = (await owner.IssueReadAsync(grantId, default))!;
            await Assert.That(issued).IsNotNull();
            await context.SaveChangesAsync();
            await Assert.That(await owner.RecheckIssuanceAsync(default)).IsTrue();
            await transaction.CommitAsync();
        }
        await using (var context = database.Context())
        {
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var intake = new ModerationCaseIntakeRepository(context,
                new ModerationCaseTargetReceiptRepository(context, database.Caller, database.Machine, database.Clock),
                database.Owner(context), database.Clock);
            await Assert.That(await intake.ReadAsync(new(ModerationCaseAccessPurpose.ReadAllegation,
                caseId, Guid.Empty, 1, issued.Receipt.Id, issued.Token), default)).IsNotNull();
        }
        await using (var context = database.Context())
        {
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var owner = database.Owner(context);
            await Assert.That(await owner.RevokeReadAsync(grantId, Guid.CreateVersion7(), 1, default)).IsNotNull();
            await context.SaveChangesAsync();
            await Assert.That(await owner.RecheckIssuanceAsync(default)).IsTrue();
            await transaction.CommitAsync();
        }
        await using (var context = database.Context())
        {
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            await Assert.That(await database.Owner(context).IssueReadAsync(grantId, default)).IsNull();
        }
    }

    [Test]
    [Arguments("foreign-target")]
    [Arguments("foreign-relationship")]
    [Arguments("machine")]
    [Arguments("tenant-bypass")]
    public async Task ExistenceAndCapabilitiesDoNotReplaceNativeCaseRelationship(string defect)
    {
        await using var database = await NativeDatabase.CreateAsync(ModerationCaseTargetKind.Resource);
        await database.IssueAsync();
        await using var context = database.Context();
        var request = database.Request();
        switch (defect)
        {
            case "foreign-target": request = request with { NativeTargetId = database.FormId }; break;
            case "foreign-relationship": request = request with { RelationshipSourceId = database.OrderId }; break;
            case "machine":
                database.Machine.Current = new ApiKeyPrincipalContext($"test-{Guid.CreateVersion7():N}",
                    database.TenantId, ExternalApiKeyOwnerType.User, database.CallerId, []);
                break;
            case "tenant-bypass": context.EnableTenantFilterBypass("Adversarial native case proof test."); break;
            default: throw new ArgumentOutOfRangeException(nameof(defect));
        }
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await Assert.That(await database.Owner(context).IssueSubmissionAsync(request, default)).IsNull();
    }

    private sealed class NativeDatabase(string path, ModerationCaseTargetKind kind, bool standalone) : IAsyncDisposable
    {
        public Guid TenantId { get; } = Guid.CreateVersion7();
        public Guid CallerId { get; } = Guid.CreateVersion7();
        public Guid EventId { get; } = Guid.CreateVersion7();
        public Guid ActorId { get; } = Guid.CreateVersion7();
        public Guid ServiceId { get; } = Guid.CreateVersion7();
        public Guid RoleId { get; private set; }
        public Guid AdminId { get; private set; }
        public Guid OrderId { get; private set; }
        public Guid ParticipantId { get; private set; }
        public Guid ResourceId { get; private set; }
        public Guid FormId { get; private set; }
        public Guid ProviderId { get; private set; }
        public Guid TrustId { get; } = Guid.CreateVersion7();
        public NativeClock Clock { get; } = new();
        public NativeMachine Machine { get; } = new();
        public IDataProtectionProvider NarrativeProtection { get; } = new EphemeralDataProtectionProvider();
        public NativeCaller Caller => new(CallerId);

        public ExploreDbContext Context(params IInterceptor[] interceptors) => new(TestDbContextOptions.Create<ExploreDbContext>()
            .UseSqlite(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString())
            .AddInterceptors(interceptors)
            .UseSnakeCaseNamingConvention().Options)
        {
            TenantContext = new SelectedTenant(TenantId), CurrentUserService = Caller, ModerationTrustDomainId = TrustId
        };

        public ExploreDbContext RetryingContext(params IInterceptor[] interceptors) => new(
            TestDbContextOptions.Create<ExploreDbContext>()
                .UseSqlite(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString())
                .ReplaceService<IExecutionStrategyFactory, ReceiptRetryingStrategyFactory>()
                .AddInterceptors(interceptors)
                .UseSnakeCaseNamingConvention().Options)
        {
            TenantContext = new SelectedTenant(TenantId), CurrentUserService = Caller, ModerationTrustDomainId = TrustId
        };

        public ModerationCaseTargetProofOwner Owner(ExploreDbContext context) => new(context, Caller, Machine, Clock);

        public ModerationCaseTargetIssueRequest Request(
            ModerationCaseRelationshipKind relationship = ModerationCaseRelationshipKind.EventRole,
            string material = "First-hand observation about the named occurrence")
        {
            var nativeKind = kind == ModerationCaseTargetKind.HiddenContentReceipt ? ModerationCaseTargetKind.Resource
                : kind == ModerationCaseTargetKind.LocalIncident ? ModerationCaseTargetKind.EventVersion : kind;
            var targetId = nativeKind switch
            {
                ModerationCaseTargetKind.ParticipantIncident => ParticipantId,
                ModerationCaseTargetKind.Actor => ActorId,
                ModerationCaseTargetKind.Service => ServiceId,
                ModerationCaseTargetKind.Resource or ModerationCaseTargetKind.Destination => ResourceId,
                ModerationCaseTargetKind.Form => FormId,
                _ => EventId
            };
            var relationshipId = relationship switch
            {
                ModerationCaseRelationshipKind.EventSubmitter or ModerationCaseRelationshipKind.PublicEvent => EventId,
                ModerationCaseRelationshipKind.RegistrationOrder => OrderId,
                ModerationCaseRelationshipKind.RegistrationParticipant => ParticipantId,
                ModerationCaseRelationshipKind.InstanceAdministrator => AdminId,
                _ => RoleId
            };
            return new(kind, nativeKind, standalone ? null : EventId, targetId, relationship, relationshipId,
                Encoding.UTF8.GetBytes("A native occurrence at the event"), Encoding.UTF8.GetBytes(material));
        }

        public async Task<ModerationCaseIssuedReceipt> IssueAsync(
            ModerationCaseRelationshipKind relationship = ModerationCaseRelationshipKind.EventRole,
            string material = "First-hand observation about the named occurrence")
        {
            await using var context = Context();
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var owner = Owner(context);
            var issued = await owner.IssueSubmissionAsync(Request(relationship, material), default);
            await Assert.That(issued).IsNotNull();
            await context.SaveChangesAsync();
            await Assert.That(await owner.RecheckIssuanceAsync(default)).IsTrue();
            await transaction.CommitAsync();
            return issued!;
        }

        public static async Task<NativeDatabase> CreateAsync(ModerationCaseTargetKind kind, bool standalone = false)
        {
            var path = Path.Combine(Path.GetTempPath(), $"native-case-proof-{Guid.CreateVersion7():N}.db");
            await LocalIdentitySqliteTemplate.CopyAsync(path, null, true, default);
            var database = new NativeDatabase(path, kind, standalone);
            await using (var context = database.Context())
            {
                var now = database.Clock.Now.UtcDateTime;
                var tenant = new Tenant
                {
                    Id = database.TenantId, Slug = $"native-{database.TenantId:N}", FullName = "Native target proof",
                    TenantStatusId = (int)TenantStatusEnum.Active, TenantStatus = null!
                };
                var user = new User { Id = database.CallerId, Pii = new UserPii
                {
                    UserId = database.CallerId, Email = $"{database.CallerId:N}@example.invalid",
                    FirstName = "Native", LastName = "Reporter"
                }};
                var maintenance = new ServicePrincipal
                {
                    Id = Guid.CreateVersion7(), Code = $"maintenance-{Guid.CreateVersion7():N}", DisplayName = "Maintenance"
                };
                var publisher = new ServicePrincipal
                {
                    Id = database.ServiceId, Code = $"publisher-{Guid.CreateVersion7():N}", DisplayName = "Publisher"
                };
                var host = ModerationHostBinding.Establish(Guid.CreateVersion7(), database.TrustId,
                    Guid.CreateVersion7(), Guid.CreateVersion7(), maintenance.Id);
                var epoch = ModerationQualificationEpoch.Initialize(Guid.CreateVersion7(), host, tenant);
                var actor = new Actor
                {
                    Id = database.ActorId, ActorTypeId = (int)ActorTypeEnum.Bot, ActorType = null!,
                    ServicePrincipalId = publisher.Id, Pii = new ActorPii { ActorId = database.ActorId, DisplayName = "Native publisher" }
                };
                var admin = new PlatformUserRole
                {
                    Id = Guid.CreateVersion7(), UserId = user.Id, User = user, RoleId = (int)RoleEnum.Admin,
                    Role = null!, GrantedAt = now
                };
                database.AdminId = admin.Id;
                context.AddRange(tenant, user, maintenance, publisher, host, epoch, admin);
                if (!standalone || kind == ModerationCaseTargetKind.Actor)
                    context.Actors.Add(actor);
                if (!standalone)
                {
                    var parent = new DomainEvent(EventStatusEnum.Draft)
                    {
                        Id = database.EventId, TenantId = tenant.Id, Tenant = tenant, ActorId = actor.Id, Actor = actor,
                        Title = "Private draft", SubmittedByUserId = user.Id,
                        EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
                        VisibilityTypeId = (int)VisibilityTypeEnum.Private, VisibilityType = null!,
                        EventFormatId = (int)EventFormatEnum.Local, EventFormat = null!, EventStatus = null!
                    };
                    var role = EventRoleAssignment.Create(tenant.Id, parent.Id, user.Id, (int)RoleEnum.EventOwner,
                        EventRoleAssignmentStatus.Active, now.AddHours(-1), now.AddHours(1), user.Id);
                    role.Version = 7;
                    database.RoleId = role.Id;
                    var catalog = EventTicketCatalogVersion.Create(tenant.Id, parent.Id, "USD", 1);
                    var order = RegistrationOrder.Create(tenant.Id, parent.Id, user.Id, null,
                        BookingPartyTypeEnum.Individual, catalog.Id,
                        RegistrationParticipationSnapshot.Create(Guid.CreateVersion7(), 1, 1, 1, null),
                        null, null, "USD", now, null);
                    database.OrderId = order.Id;
                    var participant = RegistrationParticipant.Create(tenant.Id, order.Id, user.Id, ParticipantTypeEnum.Adult, null);
                    database.ParticipantId = participant.Id;
                    order.AddParticipant(participant);
                    var resourceId = Guid.CreateVersion7();
                    var resource = EventResource.CreateDraft(resourceId, tenant.Id, parent.Id, null,
                        new EventResourceMetadata
                        {
                            Title = "Protected destination", Kind = EventResourceKindEnum.GeneralDocument,
                            DisclosureMode = EventResourceDisclosureModeEnum.EligibleOnly
                        }, EventResourceDeliveryTypeEnum.ExternalLink, EventResourceAvailability.Create(),
                        [EventResourceAudienceRule.Create(tenant.Id, parent.Id, resourceId, EventResourceAudienceKindEnum.Organizer)],
                        user.Id, now);
                    resource.SetExternalDestination("encrypted-test-envelope", 1, "https://example.invalid",
                        resource.ConcurrencyStamp, user.Id, now);
                    database.ResourceId = resource.Id;
                    var form = RegistrationForm.Create(tenant.Id, parent.Id, "native", "case-form", "Private form", now);
                    var provider = RegistrationProviderConnection.Create(tenant.Id, "Case form provider",
                        RegistrationProviderKindEnum.ExternalForm, RegistrationProviderDeploymentKindEnum.SelfHosted,
                        "native-test", "self-hosted", "1", "1", "1", "https://example.invalid", "https://example.invalid",
                        "native-test", null, null, now);
                    database.ProviderId = provider.Id;
                    var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes("{}")));
                    var schema = RegistrationProviderSchemaRevision.Create(tenant.Id, provider.Id,
                        RegistrationProviderSchemaAuthorityEnum.ProviderDiscovered, RegistrationEvidenceHash.Create(hash),
                        "native-survey", "1", "{}", hash, RegistrationProviderDriftClassEnum.NoDrift, now);
                    var version = RegistrationFormVersion.CreateExternalImported(form, 1, "en", provider.Id,
                        schema.Id, "native-survey", "1", hash, now);
                    database.FormId = version.Id;
                    context.AddRange(parent, role, catalog, order, resource, form, provider, schema, version);
                }
                await context.SaveChangesAsync();
            }
            await QualifyNarrativeCustodyAsync(database, kind);
            await using (var context = database.Context())
            {
                await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
                var owner = database.Owner(context);
                await Assert.That(await owner.SetLifetimePolicyAsync(kind, 0,
                    TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(2), true, default)).IsNotNull();
                await context.SaveChangesAsync();
                await Assert.That(await owner.RecheckIssuanceAsync(default)).IsTrue();
                await transaction.CommitAsync();
            }
            return database;
        }

        public ValueTask DisposeAsync()
        {
            SqliteConnection.ClearAllPools();
            foreach (var suffix in new[] { "", "-wal", "-shm" }) File.Delete(path + suffix);
            return ValueTask.CompletedTask;
        }
    }

    private sealed record SelectedTenant(Guid TenantId) : ITenantContext;
    private sealed class NativeCaller(Guid userId) : ICurrentUserService
    {
        public Guid? UserId { get; set; } = userId;
        public bool IsAuthenticated { get; set; } = true;
    }
    private sealed class NativeMachine : IMachinePrincipalAccessor
    {
        public ApiKeyPrincipalContext? Current { get; set; }
        public bool IsMachineCaller => Current is not null;
    }
    private sealed class NativeClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
