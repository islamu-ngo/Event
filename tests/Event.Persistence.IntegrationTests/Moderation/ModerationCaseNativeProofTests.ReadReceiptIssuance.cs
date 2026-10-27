using IsolationLevel = System.Data.IsolationLevel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Transactions;
using Explore.Application.Contracts.Moderation;
using Explore.Application.Features.Moderation.Handlers.Commands;
using Explore.Domain;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationCaseNativeProofTests
{
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
    public async Task CommittedReadTokenRequiresSeparateGrantAndCannotBecomeSubmissionAuthority(ModerationCaseTargetKind kind)
    {
        await using var database = await NativeDatabase.CreateAsync(kind);
        var (caseId, grantId) = await GrantedReadAsync(database);
        await using var context = database.Context();
        var intake = IssuanceCommitter(context, database, database.Caller);
        var issued = await intake.IssueReadReceiptAsync(grantId, default);
        await Assert.That(issued).IsNotNull();
        await Assert.That(issued!.Token.Length).IsEqualTo(32);
        await Assert.That(issued.Receipt.RelationshipKind).IsEqualTo(ModerationCaseRelationshipKind.CaseReadGrant);
        await Assert.That(issued.Receipt.RelationshipSourceId).IsEqualTo(grantId);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
        await Assert.That(context.ChangeTracker.Entries().Any()).IsFalse();
        await using (var verify = database.Context())
        {
            var receipt = await verify.ModerationCaseTargetReceipts.SingleAsync(row => row.Id == issued.Receipt.Id);
            var grant = await verify.ModerationCaseReadGrants.SingleAsync(row => row.Id == grantId);
            await Assert.That(receipt.ExpiresAt <= grant.ExpiresAt).IsTrue();
            await Assert.That(receipt.ConsumedAt).IsNull();
        }
        var rejected = await intake.SubmitAsync(new(ModerationCaseAccessPurpose.SubmitAllegation,
            Guid.Empty, Guid.CreateVersion7(), 0, issued.Receipt.Id, issued.Token)
        { Narrative = database.Request().Material }, default);
        await Assert.That(rejected.Outcome).IsEqualTo(ModerationCaseIntakeCommitOutcome.Unavailable);
        await using var read = database.Context();
        await using var transaction = await read.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var owner = database.Owner(read);
        var request = new ModerationCaseAccessRequest(ModerationCaseAccessPurpose.ReadAllegation,
            caseId, Guid.Empty, 1, issued.Receipt.Id, issued.Token);
        await Assert.That(await owner.AcquireAsync(request, issued.Receipt,
            transaction.TransactionId, database.Clock.Now, default)).IsNotNull();
    }

    [Test]
    [Arguments("grant")]
    [Arguments("case")]
    [Arguments("account")]
    [Arguments("policy")]
    [Arguments("expiry")]
    [Arguments("native")]
    public async Task CommittedReadTokenIsWithheldWhenOriginalGrantAuthorityChangesAfterSave(string defect)
    {
        await using var database = await NativeDatabase.CreateAsync(ModerationCaseTargetKind.Form);
        var (caseId, grantId) = await GrantedReadAsync(database);
        var checkpoint = new ReceiptSavedCheckpoint(async context =>
        {
            switch (defect)
            {
                case "grant":
                    await context.ModerationCaseReadGrants.Where(row => row.Id == grantId)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.RevokedAt, database.Clock.Now));
                    break;
                case "case":
                    await context.ModerationCases.Where(row => row.Id == caseId).ExecuteUpdateAsync(
                        update => update.SetProperty(row => row.Revision, row => row.Revision + 1));
                    break;
                case "account":
                    await context.Users.Where(row => row.Id == database.CallerId)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsDeleted, true));
                    break;
                case "policy":
                    await context.ModerationCaseTargetPolicies.ExecuteUpdateAsync(update => update.SetProperty(row => row.Enabled, false));
                    break;
                case "expiry": database.Clock.Now = database.Clock.Now.AddMinutes(1); break;
                case "native":
                    await context.Events.Where(row => row.Id == database.EventId)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.ConcurrencyStamp, Guid.CreateVersion7()));
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(defect));
            }
        });
        await using var context = database.Context(checkpoint);
        var intake = IssuanceCommitter(context, database, database.Caller);
        await Assert.That(await intake.IssueReadReceiptAsync(grantId, default)).IsNull();
        await Assert.That(checkpoint.ObservedReceiptSave).IsTrue();
        await Assert.That(context.Database.CurrentTransaction).IsNull();
        await Assert.That(context.ChangeTracker.Entries().Any()).IsFalse();
        await using var verify = database.Context();
        await Assert.That(await verify.ModerationCaseTargetReceipts
            .AnyAsync(row => row.RelationshipKind == ModerationCaseRelationshipKind.CaseReadGrant)).IsFalse();
        await Assert.That(await verify.ModerationCaseReadGrants.AnyAsync(row => row.Id == grantId && row.RevokedAt == null)).IsTrue();
    }

    [Test]
    [Arguments(ModerationCaseTargetKind.EventVersion, false)]
    [Arguments(ModerationCaseTargetKind.EventVersion, true)]
    [Arguments(ModerationCaseTargetKind.LocalIncident, false)]
    [Arguments(ModerationCaseTargetKind.LocalIncident, true)]
    public async Task Read_receipt_retains_original_publisher_roles_even_when_actor_set_and_event_stamp_are_unchanged(
        ModerationCaseTargetKind kind, bool swapRoles)
    {
        await using var database = await NativeDatabase.CreateAsync(kind);
        var organizerId = Guid.CreateVersion7();
        await using (var seed = database.Context())
        {
            var service = new ServicePrincipal
            {
                Id = Guid.CreateVersion7(),
                Code = $"read-plan-{organizerId:N}",
                DisplayName = "Read receipt organizer"
            };
            seed.AddRange(service, new Actor
            {
                Id = organizerId,
                ActorTypeId = (int)Explore.Domain.Enums.ActorTypeEnum.Bot,
                ActorType = null!,
                ServicePrincipalId = service.Id,
                Pii = new ActorPii { ActorId = organizerId, DisplayName = "Read receipt organizer" }
            });
            (await seed.Events.SingleAsync(row => row.Id == database.EventId)).OrganizerActorId = organizerId;
            await seed.SaveChangesAsync();
        }
        var (_, grantId) = await GrantedReadAsync(database);
        await using var before = database.Context();
        var original = await before.Events.AsNoTracking().SingleAsync(row => row.Id == database.EventId);
        var grantBefore = await before.ModerationCaseReadGrants.AsNoTracking().SingleAsync(row => row.Id == grantId);
        var checkpoint = new ReceiptSavedCheckpoint(async context =>
        {
            if (swapRoles)
                await context.Events.Where(row => row.Id == database.EventId).ExecuteUpdateAsync(update => update
                    .SetProperty(row => row.ActorId, organizerId)
                    .SetProperty(row => row.OrganizerActorId, database.ActorId));
        });
        await using (var context = database.Context(checkpoint))
        {
            var issued = await IssuanceCommitter(context, database, database.Caller).IssueReadReceiptAsync(grantId, default);
            await Assert.That(issued is not null).IsEqualTo(!swapRoles);
            await Assert.That(checkpoint.ObservedReceiptSave).IsTrue();
            await Assert.That(context.Database.CurrentTransaction).IsNull();
            await Assert.That(context.ChangeTracker.Entries().Any()).IsFalse();
        }
        await using var inspect = database.Context();
        var parent = await inspect.Events.AsNoTracking().SingleAsync(row => row.Id == database.EventId);
        await Assert.That(parent.ActorId).IsEqualTo(original.ActorId);
        await Assert.That(parent.OrganizerActorId).IsEqualTo(original.OrganizerActorId);
        await Assert.That(parent.ConcurrencyStamp).IsEqualTo(original.ConcurrencyStamp);
        await Assert.That(await inspect.ModerationCaseTargetReceipts.CountAsync(
            row => row.RelationshipKind == ModerationCaseRelationshipKind.CaseReadGrant)).IsEqualTo(swapRoles ? 0 : 1);
        var grantAfter = await inspect.ModerationCaseReadGrants.AsNoTracking().SingleAsync(row => row.Id == grantId);
        await Assert.That(grantAfter.Revision).IsEqualTo(grantBefore.Revision);
        await Assert.That(grantAfter.ExpiresAt).IsEqualTo(grantBefore.ExpiresAt);
        await Assert.That(grantAfter.RevokedAt).IsEqualTo(grantBefore.RevokedAt);
        await Assert.That(grantAfter.SourceId).IsEqualTo(grantBefore.SourceId);
        await Assert.That(grantAfter.CallerUserId).IsEqualTo(grantBefore.CallerUserId);
    }

    [Test]
    public async Task UncommittedOrForeignReadGrantCannotProduceABearer()
    {
        await using var database = await NativeDatabase.CreateAsync(ModerationCaseTargetKind.Form);
        var (_, grantId) = await GrantedReadAsync(database);
        await using var context = database.Context();
        var intake = IssuanceCommitter(context, database, database.Caller);
        await Assert.That(await intake.IssueReadReceiptAsync(Guid.CreateVersion7(), default)).IsNull();
        var otherCaller = Guid.CreateVersion7();
        context.Users.Add(new User
        {
            Id = otherCaller,
            Pii = new UserPii
            {
                UserId = otherCaller,
                Email = $"{otherCaller:N}@example.invalid",
                FirstName = "Other",
                LastName = "Caller"
            }
        });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        await context.ModerationCaseReadGrants.Where(row => row.Id == grantId).ExecuteUpdateAsync(
            update => update.SetProperty(row => row.CallerUserId, otherCaller));
        await Assert.That(await intake.IssueReadReceiptAsync(grantId, default)).IsNull();
        await Assert.That(await context.ModerationCaseTargetReceipts
            .AnyAsync(row => row.RelationshipKind == ModerationCaseRelationshipKind.CaseReadGrant)).IsFalse();
    }

    [Test]
    public async Task Native_read_handler_exposes_only_a_defensive_committed_bearer_snapshot()
    {
        await using var database = await NativeDatabase.CreateAsync(ModerationCaseTargetKind.Form);
        var (_, grantId) = await GrantedReadAsync(database);
        await using var context = database.Context();
        var handler = new IssueModerationReadReceiptCommandHandler(IssuanceCommitter(context, database, database.Caller));
        var response = await handler.ExecuteAsync(new() { ReadGrantId = grantId });
        await Assert.That(response.IsSuccess).IsTrue();
        var original = response.PresentedToken.ToArray();
        var copy = response with { };
        if (MemoryMarshal.TryGetArray(response.PresentedToken, out var exposed))
            CryptographicOperations.ZeroMemory(exposed.Array!);
        await Assert.That(response.PresentedToken.ToArray()).IsEquivalentTo(original);
        await Assert.That(copy.PresentedToken.ToArray()).IsEquivalentTo(original);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
        await Assert.That(context.ChangeTracker.Entries().Any()).IsFalse();
        await using var verify = database.Context();
        var persisted = await verify.ModerationCaseTargetReceipts.SingleAsync(row => row.Id == response.Id);
        await Assert.That(persisted.RelationshipSourceId).IsEqualTo(grantId);
        await Assert.That(persisted.ExpiresAt).IsEqualTo(response.ExpiresAt!.Value);
        await Assert.That(persisted.TokenDigest)
            .IsEqualTo(Convert.ToHexString(SHA256.HashData(original)).ToLowerInvariant());
        var invalid = await handler.ExecuteAsync(new() { ReadGrantId = Guid.Empty });
        await Assert.That(invalid.IsSuccess).IsFalse();
        await Assert.That(invalid.PresentedToken.Length).IsEqualTo(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ReadReceiptCommitterNeverRemintsOrPublishesAfterUncertainCommit(bool commitReachedDatabase)
    {
        await using var database = await NativeDatabase.CreateAsync(ModerationCaseTargetKind.Form);
        var (_, grantId) = await GrantedReadAsync(database);
        var checkpoint = new ReceiptCommitFailureCheckpoint(commitReachedDatabase);
        await using var context = database.RetryingContext(checkpoint);
        var intake = IssuanceCommitter(context, database, database.Caller);
        await Assert.That(context.Database.CreateExecutionStrategy().RetriesOnFailure).IsTrue();
        await Assert.That(await intake.IssueReadReceiptAsync(grantId, default)).IsNull();
        await Assert.That(checkpoint.CommitAttempts).IsEqualTo(1);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
        await Assert.That(context.ModerationTrustDomainId).IsNull();
        await Assert.That(context.ChangeTracker.Entries().Any()).IsFalse();
        await using var verify = database.Context();
        await Assert.That(await verify.ModerationCaseTargetReceipts
            .CountAsync(row => row.RelationshipKind == ModerationCaseRelationshipKind.CaseReadGrant))
            .IsEqualTo(commitReachedDatabase ? 1 : 0);
        await Assert.That(await verify.ModerationCaseReadGrants.CountAsync(row => row.Id == grantId)).IsEqualTo(1);
    }

    [Test]
    public async Task ReadReceiptCommitterCancelsAfterSaveWithoutRetainingABearerOrWrites()
    {
        await using var database = await NativeDatabase.CreateAsync(ModerationCaseTargetKind.Form);
        var (_, grantId) = await GrantedReadAsync(database);
        using var cancellation = new CancellationTokenSource();
        var checkpoint = new ReceiptSavedCheckpoint(_ =>
        {
            cancellation.Cancel();
            return Task.CompletedTask;
        });
        await using var context = database.Context(checkpoint);
        var intake = IssuanceCommitter(context, database, database.Caller);
        await Assert.That(() => intake.IssueReadReceiptAsync(grantId, cancellation.Token))
            .Throws<OperationCanceledException>();
        await Assert.That(checkpoint.ObservedReceiptSave).IsTrue();
        await Assert.That(context.Database.CurrentTransaction).IsNull();
        await Assert.That(context.ModerationTrustDomainId).IsNull();
        await Assert.That(context.ChangeTracker.Entries().Any()).IsFalse();
        await using var verify = database.Context();
        await Assert.That(await verify.ModerationCaseTargetReceipts
            .AnyAsync(row => row.RelationshipKind == ModerationCaseRelationshipKind.CaseReadGrant)).IsFalse();
    }

    [Test]
    public async Task ReadReceiptCommitterRefusesCallerOwnedOrAmbientTransactionsWithoutClearingThem()
    {
        await using var database = await NativeDatabase.CreateAsync(ModerationCaseTargetKind.Form);
        var (_, grantId) = await GrantedReadAsync(database);
        await using var context = database.Context();
        var intake = IssuanceCommitter(context, database, database.Caller);
        await using (var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            await Assert.That(await intake.IssueReadReceiptAsync(grantId, default)).IsNull();
            await Assert.That(context.Database.CurrentTransaction).IsEqualTo(transaction);
            await transaction.RollbackAsync();
        }
        using (var ambient = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
        {
            var originalAmbient = Transaction.Current;
            await Assert.That(await intake.IssueReadReceiptAsync(grantId, default)).IsNull();
            await Assert.That(originalAmbient).IsNotNull();
            await Assert.That(Transaction.Current).IsEqualTo(originalAmbient);
            await Assert.That(context.Database.CurrentTransaction).IsNull();
        }
        await using var verify = database.Context();
        await Assert.That(await verify.ModerationCaseTargetReceipts
            .AnyAsync(row => row.RelationshipKind == ModerationCaseRelationshipKind.CaseReadGrant)).IsFalse();
    }

    private static async Task<(Guid CaseId, Guid GrantId)> GrantedReadAsync(NativeDatabase database)
    {
        using var progress = new StreamWriter(Console.OpenStandardError()) { AutoFlush = true };
        progress.WriteLine("[DEBUG-GRANT-SETTLEMENT] ordinary-intake:start");
        Guid caseId;
        Guid sourceId;
        await using (var context = database.Context())
        {
            var intake = IssuanceCommitter(context, database, database.Caller);
            var issued = await intake.IssueSubmissionReceiptAsync(database.Request(), default);
            await Assert.That(issued).IsNotNull();
            sourceId = issued!.Receipt.SourceId;
            var accepted = await intake.SubmitAsync(new(ModerationCaseAccessPurpose.SubmitAllegation,
                Guid.Empty, Guid.CreateVersion7(), 0, issued.Receipt.Id, issued.Token)
            { Narrative = database.Request().Material }, default);
            await Assert.That(accepted.Outcome).IsEqualTo(ModerationCaseIntakeCommitOutcome.Accepted);
            caseId = accepted.Acknowledgement!.CaseId;
        }
        progress.WriteLine("[DEBUG-GRANT-SETTLEMENT] ordinary-intake:complete");
        await using var setup = database.Context();
        await using var transaction = await setup.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var owner = database.Owner(setup);
        var grant = await owner.GrantReadAsync(new(Guid.CreateVersion7(), caseId, 1, sourceId,
            database.CallerId, database.Clock.Now.AddMinutes(1)), default);
        await Assert.That(grant).IsNotNull();
        await setup.SaveChangesAsync();
        await Assert.That(await owner.RecheckIssuanceAsync(default)).IsTrue();
        await transaction.CommitAsync();
        progress.WriteLine("[DEBUG-GRANT-SETTLEMENT] ordinary-initial-grant:complete");
        return (caseId, grant!.Id);
    }
}
