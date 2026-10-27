using System.Data;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Policy;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Repositories;
using Explore.Persistence.Services;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationPolicyPublicationTests
{
    [Test]
    [Arguments("exact")]
    [Arguments("source-receipt")]
    [Arguments("acceptance-receipt")]
    [Arguments("source-expiry-after-save")]
    [Arguments("acceptance-expiry-after-save")]
    [Arguments("acceptance-epoch-after-save")]
    [Arguments("foreign-case-after-source-save")]
    [Arguments("foreign-case-after-acceptance-save")]
    [Arguments("epoch-after-save")]
    [Arguments("reviewer-departed")]
    [Arguments("wrong-mode")]
    [Arguments("wrong-binding")]
    [Arguments("disposed")]
    [Arguments("replaced-transaction")]
    [Arguments("source-replay-expired")]
    [Arguments("withdraw-expired-departed")]
    [Arguments("withdraw-deselected")]
    [Arguments("withdraw-receipt-missing")]
    [Arguments("withdraw-role-after-save")]
    public async Task Measured_report_is_native_immutable_receipted_and_never_promotes_missing_operations(string change)
    {
        await using var database = await Database.CreateAsync();
        var clock = new DeploymentClock();
        var flush = new MeasuredOperationsFlush(clock);
        await using var context = database.Context(flush);
        var request = database.Request();
        request = request with { Proposed = request.Proposed with { RequestedState = ModerationPolicyRequestedState.Active } };
        var (admin, machine) = DeploymentIdentity(context, database.AdminId);
        var publications = new ModerationPolicyPublicationRepository(context, admin, machine, database.Inventory, null, clock);
        var prepared = await publications.PrepareAsync(request, Now.AddMinutes(10));
        await Assert.That(prepared.Outcome).IsEqualTo(ModerationPolicyPublicationOutcome.Known);
        var policy = ModerationPolicyPublicationProjection.RestoreCandidate(prepared.Candidate!, database.Inventory)!;
        var reviewer = Guid.CreateVersion7();
        var role = await context.Roles.SingleAsync(row =>
            row.RoleScopeId == (int)RoleScopeEnum.Platform && row.MasterCode == "platform.admin");
        var user = new User
        {
            Id = reviewer,
            Pii = new UserPii
            { UserId = reviewer, Email = $"{reviewer:N}@example.invalid", FirstName = "Measured", LastName = "Reviewer" }
        };
        context.Users.Add(user);
        context.PlatformUserRoles.Add(new PlatformUserRole
        { Id = Guid.CreateVersion7(), UserId = reviewer, User = user, RoleId = role.Id, Role = role });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        ModerationQualificationRepository Writer(Guid human)
        {
            var identity = DeploymentIdentity(context, human);
            return new(context, identity.Admin, identity.Machine, publications,
                new ModerationNativeActivationQualification(context, database.Inventory, null, null, clock),
                clock, database.Privacy.Authority(context, clock), deploymentInventory: database.Inventory);
        }
        var binding = policy.Consumers[0].Binding;
        var record = new RecordModerationMeasuredOperationsSource
        {
            OperationId = Guid.CreateVersion7(),
            Target = database.Scope,
            ExpectedQualificationRevision = 2,
            SourceId = Guid.CreateVersion7(),
            Binding = binding,
            ProfileId = request.Proposed.Profiles.Single().Id,
            ReviewerHumanId = reviewer,
            ReviewerControllerHumanId = reviewer,
            RecorderControllerHumanId = database.AdminId,
            ValidUntil = Now.AddMinutes(5)
        };
        var writer = Writer(database.AdminId);
        if (change is "source-expiry-after-save" or "epoch-after-save" or "foreign-case-after-source-save")
        {
            context.ModerationTrustDomainId = database.Scope.TrustDomainId;
            var originalHead = await context.Set<ModerationRecoveryStoreState>().AsNoTracking().SingleAsync();
            flush.SourceDeadline = change == "source-expiry-after-save" ? record.ValidUntil : null;
            flush.ChangeEpoch = change == "epoch-after-save";
            var foreignId = Guid.CreateVersion7();
            if (change == "foreign-case-after-source-save")
                flush.SourceForeignCase = ModerationCase.Open(foreignId,
                    SubjectReference.Create(ModerationSubjectKind.Human, database.AdminId, database.Scope),
                    database.Scope, Guid.CreateVersion7(), Now);
            await Assert.That((await writer.ExecuteAsync(record)).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Unavailable);
            context.ModerationTrustDomainId = database.Scope.TrustDomainId;
            await Assert.That(await context.Set<ModerationMeasuredOperationsSource>().CountAsync()).IsEqualTo(0);
            await Assert.That((await context.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(2L);
            await Assert.That(await context.ModerationQualificationMutationReceipts.AnyAsync(row => row.Id == record.OperationId)).IsFalse();
            await Assert.That(await context.ModerationCases.AnyAsync(row => row.Id == foreignId)).IsFalse();
            var retainedHead = await context.Set<ModerationRecoveryStoreState>().AsNoTracking().SingleAsync();
            await Assert.That((retainedHead.Id, retainedHead.Revision, retainedHead.Generation, retainedHead.Position))
                .IsEqualTo((originalHead.Id, originalHead.Revision, originalHead.Generation, originalHead.Position));
            return;
        }
        await Assert.That((await writer.ExecuteAsync(record)).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
        await Assert.That((await writer.ExecuteAsync(record)).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
        var accept = new AcceptModerationMeasuredOperationsSource
        {
            OperationId = Guid.CreateVersion7(),
            Target = database.Scope,
            ExpectedQualificationRevision = 3,
            SourceId = record.SourceId,
            ExpectedSourceRevision = 1,
            ExpectedSourceWriterGeneration = 3,
            AcceptanceId = Guid.CreateVersion7(),
            ValidUntil = Now.AddMinutes(4)
        };
        await Assert.That((await writer.ExecuteAsync(accept)).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Denied);
        if (change is "acceptance-expiry-after-save" or "acceptance-epoch-after-save" or "foreign-case-after-acceptance-save")
        {
            context.ModerationTrustDomainId = database.Scope.TrustDomainId;
            var originalHead = await context.Set<ModerationRecoveryStoreState>().AsNoTracking().SingleAsync();
            var originalSource = await context.Set<ModerationMeasuredOperationsSource>().AsNoTracking().SingleAsync();
            flush.AcceptanceDeadline = change == "acceptance-expiry-after-save" ? accept.ValidUntil : null;
            flush.ChangeAcceptanceEpoch = change == "acceptance-epoch-after-save";
            var foreignId = Guid.CreateVersion7();
            if (change == "foreign-case-after-acceptance-save")
                flush.AcceptanceForeignCase = ModerationCase.Open(foreignId,
                    SubjectReference.Create(ModerationSubjectKind.Human, database.AdminId, database.Scope),
                    database.Scope, Guid.CreateVersion7(), Now);
            await Assert.That((await Writer(reviewer).ExecuteAsync(accept)).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Unavailable);
            context.ModerationTrustDomainId = database.Scope.TrustDomainId;
            await Assert.That(await context.Set<ModerationMeasuredOperationsAcceptance>().CountAsync()).IsEqualTo(0);
            await Assert.That((await context.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(3L);
            await Assert.That(await context.ModerationQualificationMutationReceipts.AnyAsync(row => row.Id == accept.OperationId)).IsFalse();
            await Assert.That(await context.ModerationCases.AnyAsync(row => row.Id == foreignId)).IsFalse();
            var retainedHead = await context.Set<ModerationRecoveryStoreState>().AsNoTracking().SingleAsync();
            await Assert.That((retainedHead.Id, retainedHead.Revision, retainedHead.Generation, retainedHead.Position))
                .IsEqualTo((originalHead.Id, originalHead.Revision, originalHead.Generation, originalHead.Position));
            var retainedSource = await context.Set<ModerationMeasuredOperationsSource>().AsNoTracking().SingleAsync();
            await Assert.That((retainedSource.Id, retainedSource.Revision, retainedSource.WriterGeneration,
                retainedSource.EvidenceJson, retainedSource.ValidUntil))
                .IsEqualTo((originalSource.Id, originalSource.Revision, originalSource.WriterGeneration,
                    originalSource.EvidenceJson, originalSource.ValidUntil));
            return;
        }
        await Assert.That((await Writer(reviewer).ExecuteAsync(accept)).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
        context.ModerationTrustDomainId = database.Scope.TrustDomainId;
        var saved = await context.Set<ModerationMeasuredOperationsSource>().AsNoTracking().SingleAsync();
        await Assert.That(saved.Evidence.Consumers.Select(row => row.ConsumerId).ToArray())
            .IsEquivalentTo(database.Inventory.RequiredConsumers(binding.Capability, binding.Mode).ToArray());
        await Assert.That(saved.Evidence.Consumers.All(row => row.Measurement is null)).IsTrue();
        await Assert.That(saved.Evidence.Recovery).IsNull();
        await Assert.That(saved.Evidence.Staffing).IsNull();
        await Assert.That(await context.ModerationPolicySelections.CountAsync()).IsEqualTo(0);
        if (change is "source-receipt" or "acceptance-receipt" or "withdraw-receipt-missing")
        {
            var receipt = change == "acceptance-receipt" ? accept.OperationId : record.OperationId;
            await context.ModerationQualificationMutationReceipts.Where(row => row.Id == receipt).ExecuteDeleteAsync();
        }
        if (change == "reviewer-departed")
            await context.PlatformUserRoles.Where(row => row.UserId == reviewer).ExecuteDeleteAsync();
        if (change.StartsWith("withdraw", StringComparison.Ordinal))
        {
            if (change == "withdraw-deselected")
                await Assert.That((await publications.PublishAsync(database.Request() with { ExpectedQualificationRevision = 4 })).Outcome)
                    .IsEqualTo(ModerationPolicyPublicationOutcome.Known);
            clock.UtcNow = Now.AddMinutes(20);
            if (change == "withdraw-expired-departed")
                await context.PlatformUserRoles.Where(row => row.UserId == reviewer).ExecuteDeleteAsync();
            if (change == "withdraw-role-after-save") flush.RevokeWithdrawer = database.AdminId;
            var withdrawal = new WithdrawModerationMeasuredOperationsAcceptance
            {
                OperationId = Guid.CreateVersion7(),
                Target = database.Scope,
                ExpectedQualificationRevision = change == "withdraw-deselected" ? 5 : 4,
                AcceptanceId = accept.AcceptanceId,
                ExpectedAcceptanceRevision = 1
            };
            var result = await writer.ExecuteAsync(withdrawal);
            var denied = change is "withdraw-receipt-missing" or "withdraw-role-after-save";
            await Assert.That(result.Outcome).IsEqualTo(denied
                ? ModerationQualificationWriteOutcome.Unavailable : ModerationQualificationWriteOutcome.Known);
            if (!denied)
                await Assert.That((await writer.ExecuteAsync(withdrawal)).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
            context.ModerationTrustDomainId = database.Scope.TrustDomainId;
            await Assert.That((await context.Set<ModerationMeasuredOperationsAcceptance>().AsNoTracking().SingleAsync()).Revision)
                .IsEqualTo(denied ? 1L : 2L);
            return;
        }
        if (change == "source-replay-expired")
        {
            clock.UtcNow = record.ValidUntil;
            await Assert.That((await writer.ExecuteAsync(record)).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Unavailable);
        }
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var authority = await new ModerationQualificationAuthority(context, admin, machine)
            .AcquireAsync(database.Scope, request.Proposed.PublicationId, default);
        using var session = publications.BorrowConsumerEvidence(authority!, policy);
        var owner = new ModerationMeasuredOperationsSourceOwner(context,
            new ModerationPolicyDefinitionSourceOwner(context, admin, machine, database.Inventory, clock),
            database.Inventory, clock);
        if (change == "disposed") session.Dispose();
        if (change == "replaced-transaction") await transaction.RollbackAsync();
        if (change == "wrong-mode") binding = binding with { Mode = ModerationNativeMode.RemoteStrict };
        if (change == "wrong-binding") binding = binding with { PublicationId = Guid.CreateVersion7() };
        var observed = await owner.AcquireSourceAsync(session, transaction.TransactionId,
            record.ProfileId, binding, clock.UtcNow, default);
        if (change is "exact" or "acceptance-receipt") await Assert.That(observed).IsNotNull();
        else await Assert.That(observed).IsNull();
        await Assert.That(await owner.AcquirePrerequisiteAsync(session, transaction.TransactionId, binding, clock.UtcNow, default)).IsNull();
    }

    private sealed class MeasuredOperationsFlush(DeploymentClock clock) : SaveChangesInterceptor
    {
        public DateTimeOffset? SourceDeadline { get; set; }
        public DateTimeOffset? AcceptanceDeadline { get; set; }
        public bool ChangeEpoch { get; set; }
        public bool ChangeAcceptanceEpoch { get; set; }
        public ModerationCase? SourceForeignCase { get; set; }
        public ModerationCase? AcceptanceForeignCase { get; set; }
        public Guid? RevokeWithdrawer { get; set; }
        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data, int result,
            CancellationToken cancellationToken = default)
        {
            var context = data.Context!;
            if (context.ChangeTracker.Entries<ModerationMeasuredOperationsSource>().Any())
            {
                if (SourceDeadline is { } expiry) clock.UtcNow = expiry;
                if (ChangeEpoch)
                    await context.Set<ModerationQualificationEpoch>().ExecuteUpdateAsync(
                        setters => setters.SetProperty(row => row.Revision, row => row.Revision + 1), cancellationToken);
                if (SourceForeignCase is { } foreign)
                {
                    context.Add(foreign);
                    SourceForeignCase = null;
                }
            }
            if (context.ChangeTracker.Entries<ModerationMeasuredOperationsAcceptance>().Any(row => row.Entity.Revision == 1))
            {
                if (AcceptanceDeadline is { } deadline) clock.UtcNow = deadline;
                if (ChangeAcceptanceEpoch)
                    await context.Set<ModerationQualificationEpoch>().ExecuteUpdateAsync(
                        setters => setters.SetProperty(row => row.Revision, row => row.Revision + 1), cancellationToken);
                if (AcceptanceForeignCase is { } foreign)
                {
                    context.Add(foreign);
                    AcceptanceForeignCase = null;
                }
            }
            if (context.ChangeTracker.Entries<ModerationMeasuredOperationsAcceptance>().Any(row => row.Entity.Revision == 2)
                && RevokeWithdrawer is { } human)
                await context.Set<PlatformUserRole>().Where(row => row.UserId == human).ExecuteDeleteAsync(cancellationToken);
            return result;
        }
    }
}
