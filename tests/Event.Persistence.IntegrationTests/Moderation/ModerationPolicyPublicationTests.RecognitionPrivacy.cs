using System.Data;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Policy;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Repositories;
using Explore.Persistence.Services;
using ISLAMU.Moderation.Domain;
using ISLAMU.Moderation.Domain.Policies;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationPolicyPublicationTests
{
    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    [Arguments(5)]
    [Arguments(6)]
    [Arguments(7)]
    [Arguments(8)]
    [Arguments(9)]
    [Arguments(10)]
    [Arguments(11)]
    [Arguments(12)]
    [Arguments(13)]
    [Arguments(14)]
    [Arguments(15)]
    [Arguments(16)]
    [Arguments(17)]
    [Arguments(18)]
    [Arguments(19)]
    [Arguments(20)]
    [Arguments(21)]
    [Arguments(22)]
    [Arguments(23)]
    [Arguments(24)]
    public async Task RecognitionPrivacy_RequiresSubstantiveAdoptionOnOriginalNativeAcquisition(int change)
    {
        await using var database = await Database.CreateAsync();
        var clock = new DeploymentClock();
        var flush = new RecognitionPrivacyFlushObserver(clock);
        await using var context = database.Context(flush);
        var request = database.Request();
        request = request with { Proposed = request.Proposed with
        {
            RequestedState = ModerationPolicyRequestedState.Active,
            Profiles = [request.Proposed.Profiles.Single() with
            { Recognition = ModerationReferenceRecognition.SeparatelyQualifiedFinalAction }]
        }};
        var (admin, machine) = DeploymentIdentity(context, database.AdminId);
        var publications = new ModerationPolicyPublicationRepository(context, admin, machine, database.Inventory, null, clock);
        var prepared = await publications.PrepareAsync(request, Now.AddMinutes(10));
        await Assert.That(prepared.Outcome).IsEqualTo(ModerationPolicyPublicationOutcome.Known);
        var policy = ModerationPolicyPublicationProjection.RestoreCandidate(prepared.Candidate!, database.Inventory)!;
        var privacy = Guid.CreateVersion7();
        var recovery = Guid.CreateVersion7();
        var role = await context.Roles.SingleAsync(row =>
            row.RoleScopeId == (int)RoleScopeEnum.Platform && row.MasterCode == "platform.admin");
        foreach (var id in new[] { privacy, recovery })
        {
            var user = new User { Id = id, Pii = new UserPii
            { UserId = id, Email = $"{id:N}@example.invalid", FirstName = "Privacy", LastName = "Owner" }};
            context.Users.Add(user);
            context.PlatformUserRoles.Add(new PlatformUserRole
            { Id = Guid.CreateVersion7(), UserId = id, User = user, RoleId = role.Id, Role = role });
        }
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        ModerationQualificationRepository Writer(Guid human)
        {
            var identity = DeploymentIdentity(context, human);
            return new(context, identity.Admin, identity.Machine, publications,
                new ModerationNativeActivationQualification(context, database.Inventory, null, null, clock),
                clock, database.Privacy.Authority(context, clock), deploymentInventory: database.Inventory);
        }
        var record = new RecordModerationRecognitionPrivacySource
        {
            OperationId = Guid.CreateVersion7(), Target = database.Scope, ExpectedQualificationRevision = 2,
            SourceId = Guid.CreateVersion7(), PolicyId = request.Proposed.PolicyId, ExpectedPolicyRevision = 1,
            PublicationId = request.Proposed.PublicationId, ProfileId = request.Proposed.Profiles.Single().Id,
            Mode = ModerationNativeMode.Embedded, ValidUntil = Now.AddMinutes(5),
            Terms = RecognitionPrivacyTerms(),
            Ownership = new(privacy, privacy, recovery, recovery, database.AdminId,
                "Canonical distinct controllers independently reviewed.",
                "Appointed owner of lawful recognition purpose, retention, correction and erasure deadline.",
                "Appointed recovery owner for finite qualified recovery and legal disposition."),
            Retention = [
                new(ModerationRecognitionPrivacyRecordClass.PendingComputedRecognition, TimeSpan.FromDays(30),
                    ModerationRecognitionPrivacyHold.None, TimeSpan.Zero, TimeSpan.Zero),
                new(ModerationRecognitionPrivacyRecordClass.EligibleMatchingMaterial, TimeSpan.FromDays(365),
                    ModerationRecognitionPrivacyHold.None, TimeSpan.Zero, TimeSpan.Zero),
                new(ModerationRecognitionPrivacyRecordClass.ProtectedCorrectionEvidence, TimeSpan.FromDays(366),
                    ModerationRecognitionPrivacyHold.IndividuallyJustifiedLegalClaim, TimeSpan.FromDays(30), TimeSpan.FromDays(7)),
                new(ModerationRecognitionPrivacyRecordClass.DispositionAndReplayReceipt, TimeSpan.FromDays(90),
                    ModerationRecognitionPrivacyHold.IndividuallyJustifiedLegalClaim, TimeSpan.FromDays(30), TimeSpan.FromDays(7))]
        };
        var writer = Writer(database.AdminId);
        if (change is 11 or 21)
        {
            if (change == 11) flush.SourceDeadline = record.ValidUntil;
            else flush.RevokeSourceOwner = recovery;
            await Assert.That((await writer.ExecuteAsync(record)).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Unavailable);
            context.ModerationTrustDomainId = database.Scope.TrustDomainId;
            await Assert.That(await context.Set<ModerationRecognitionPrivacySource>().CountAsync()).IsEqualTo(0);
            await Assert.That(await context.ModerationQualificationMutationReceipts.AnyAsync(row => row.Id == record.OperationId)).IsFalse();
            await Assert.That((await context.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(2);
            return;
        }
        if (change == 17)
        {
            context.PlatformUserRoles.Remove(await context.PlatformUserRoles.SingleAsync(row => row.UserId == recovery));
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            await Assert.That((await writer.ExecuteAsync(record)).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Unavailable);
            context.ModerationTrustDomainId = database.Scope.TrustDomainId;
            await Assert.That(await context.Set<ModerationRecognitionPrivacySource>().CountAsync()).IsEqualTo(0);
            return;
        }
        await Assert.That((await writer.ExecuteAsync(record)).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
        await Assert.That((await writer.ExecuteAsync(record)).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
        var adopt = new AdoptModerationRecognitionPrivacySource
        {
            OperationId = Guid.CreateVersion7(), Target = database.Scope, ExpectedQualificationRevision = 3,
            SourceId = record.SourceId, ExpectedSourceRevision = 1, ExpectedSourceWriterGeneration = 3,
            AdoptionId = Guid.CreateVersion7(), ValidUntil = Now.AddMinutes(4)
        };
        await Assert.That((await writer.ExecuteAsync(adopt)).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Denied);
        await Assert.That((await Writer(recovery).ExecuteAsync(adopt)).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Denied);
        if (change is 12 or 22)
        {
            if (change == 12) flush.AdoptionDeadline = adopt.ValidUntil;
            else flush.RevokeAdoptingOwner = privacy;
            await Assert.That((await Writer(privacy).ExecuteAsync(adopt)).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Unavailable);
            context.ModerationTrustDomainId = database.Scope.TrustDomainId;
            await Assert.That(await context.Set<ModerationRecognitionPrivacyAdoption>().CountAsync()).IsEqualTo(0);
            await Assert.That(await context.ModerationQualificationMutationReceipts.AnyAsync(row => row.Id == adopt.OperationId)).IsFalse();
            await Assert.That((await context.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(3);
            return;
        }
        await Assert.That((await Writer(privacy).ExecuteAsync(adopt)).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
        await Assert.That((await Writer(privacy).ExecuteAsync(adopt)).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
        await Assert.That((await writer.ExecuteAsync(record with
        { Terms = record.Terms with { BackupSafetyMargin = TimeSpan.FromDays(8) } })).Outcome)
            .IsEqualTo(ModerationQualificationWriteOutcome.Conflict);
        context.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await Assert.That(await context.ModerationPolicySelections.CountAsync()).IsEqualTo(0);
        await Assert.That(await context.ModerationConsumerMeasurementReceipts.CountAsync()).IsEqualTo(0);
        if (change is 1 or 2 or 3 or 4 or 5)
        {
            if (change is 1 or 2)
                context.ModerationQualificationMutationReceipts.Remove(await context.ModerationQualificationMutationReceipts
                    .SingleAsync(row => row.Id == (change == 1 ? record.OperationId : adopt.OperationId)));
            else
                context.PlatformUserRoles.Remove(await context.PlatformUserRoles.SingleAsync(row =>
                    row.UserId == (change == 3 ? privacy : change == 4 ? recovery : database.AdminId)));
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
        }
        if (change == 6) clock.UtcNow = adopt.ValidUntil;
        if (change == 7)
            await Assert.That((await writer.ExecuteAsync(record with
            { OperationId = Guid.CreateVersion7(), SourceId = Guid.CreateVersion7(), ExpectedQualificationRevision = 4 })).Outcome)
                .IsEqualTo(ModerationQualificationWriteOutcome.Known);
        if (change == 23)
        {
            await context.ModerationQualificationMutationReceipts.Where(row => row.Id == record.OperationId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.RequestDigest, new string('a', 64)));
        }
        if (change is 8 or 18)
        {
            if (change == 8)
                await Assert.That((await writer.ExecuteAsync(record with
                {
                    OperationId = Guid.CreateVersion7(), SourceId = Guid.CreateVersion7(),
                    ExpectedQualificationRevision = 4
                })).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
            clock.UtcNow = Now.AddMinutes(20);
            var withdraw = new WithdrawModerationRecognitionPrivacyAdoption
            {
                OperationId = Guid.CreateVersion7(), Target = database.Scope, ExpectedQualificationRevision = change == 8 ? 5 : 4,
                AdoptionId = adopt.AdoptionId, ExpectedAdoptionRevision = 1
            };
            if (change == 18)
            {
                flush.RevokeWithdrawingActor = database.AdminId;
                await Assert.That((await writer.ExecuteAsync(withdraw)).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Unavailable);
                context.ModerationTrustDomainId = database.Scope.TrustDomainId;
                await Assert.That((await context.Set<ModerationRecognitionPrivacyAdoption>().AsNoTracking().SingleAsync()).Revision).IsEqualTo(1);
                await Assert.That(await context.ModerationQualificationMutationReceipts.AnyAsync(row => row.Id == withdraw.OperationId)).IsFalse();
                await Assert.That((await context.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(4);
                return;
            }
            await Assert.That((await writer.ExecuteAsync(withdraw)).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
            await Assert.That((await writer.ExecuteAsync(withdraw)).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
        }
        if (change == 19)
        {
            clock.UtcNow = record.ValidUntil;
            await Assert.That((await writer.ExecuteAsync(record)).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Unavailable);
        }
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        // Loss of the original writer's role must fail even when a different current actor observes.
        var observer = DeploymentIdentity(context, change == 5 ? privacy : database.AdminId);
        var authority = await new ModerationQualificationAuthority(context, observer.Admin, observer.Machine)
            .AcquireAsync(database.Scope, request.Proposed.PublicationId, default);
        await Assert.That(authority).IsNotNull();
        var observerPublications = new ModerationPolicyPublicationRepository(context, observer.Admin, observer.Machine,
            database.Inventory, null, clock);
        using var evidence = observerPublications.BorrowConsumerEvidence(authority!, policy);
        if (change == 9) evidence.Dispose();
        if (change == 10) await transaction.RollbackAsync();
        await using var foreign = database.Context();
        var ownerContext = change == 13 ? foreign : context;
        var owner = new ModerationRecognitionPrivacySourceOwner(ownerContext,
            new ModerationPolicyDefinitionSourceOwner(ownerContext, observer.Admin, observer.Machine, database.Inventory, clock), clock);
        if (change == 24)
        {
            var before = await owner.AcquireSourceAsync(evidence, transaction.TransactionId,
                record.ProfileId, record.Mode, clock.UtcNow, default);
            await Assert.That(before).IsNotNull();
            evidence.Dispose();
            await Assert.That(await owner.ReadAdoptionAsync(evidence, transaction.TransactionId,
                record.ProfileId, record.Mode, clock.UtcNow, default)).IsNull();
        }
        var binding = change == 15 ? policy.Consumers[0].Binding with { Mode = ModerationNativeMode.RemoteStrict }
            : policy.Consumers[0].Binding;
        if (change == 16) context.ModerationTrustDomainId = Guid.CreateVersion7();
        if (change == 20) context.TenantContext = new RecognitionPrivacyTenantContext(Guid.CreateVersion7());
        var observation = await owner.AcquirePrerequisiteAsync(evidence,
            change == 14 ? Guid.CreateVersion7() : transaction.TransactionId, binding, clock.UtcNow, default);
        if (change == 0)
        {
            await Assert.That(observation!.SourceId).IsEqualTo(record.SourceId);
            await Assert.That(observation.ApprovalId).IsEqualTo(adopt.AdoptionId);
            await Assert.That(observation.ValidUntil).IsEqualTo(adopt.ValidUntil);
            await Assert.That(observation.Prerequisite).IsEqualTo(ModerationActivationPrerequisite.RecognitionAndPrivacy);
        }
        else await Assert.That(observation).IsNull();
    }

    private static ModerationRecognitionPrivacyTerms RecognitionPrivacyTerms() => new(
        ModerationRecognitionPrivacyPurpose.ScopedActionContinuity, ModerationScopeKind.Tenant,
        ModerationRecognitionPrivacyLinkage.ExactFinalActiveSanctionScopeRevisionAndInterval,
        ModerationRecognitionPrivacyOrdinaryErasure.IndependentExistingAuthority,
        ModerationRecognitionPrivacyCorrection.RevisionBoundRemoveOnlyAffectedContributionAndPreserveRemedies,
        ModerationRecognitionPrivacyCatastrophicDisposition.QualifiedRecoveryOrLegalErasureByDeadline,
        ModerationRecognitionPrivacyKeyObligations.OriginalComputedTokensAndAcknowledgedReplicaBackupHorizon,
        6, TimeSpan.FromHours(1), TimeSpan.FromDays(30), TimeSpan.FromDays(30), TimeSpan.FromDays(7));

    private sealed record RecognitionPrivacyTenantContext(Guid TenantId)
        : Explore.Application.Contracts.Infrastructure.ITenantContext;

    private sealed class RecognitionPrivacyFlushObserver(DeploymentClock clock) : SaveChangesInterceptor
    {
        public DateTimeOffset? SourceDeadline { get; set; }
        public DateTimeOffset? AdoptionDeadline { get; set; }
        public Guid? RevokeWithdrawingActor { get; set; }
        public Guid? RevokeSourceOwner { get; set; }
        public Guid? RevokeAdoptingOwner { get; set; }
        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData,
            int result, CancellationToken cancellationToken = default)
        {
            if (SourceDeadline is { } source && eventData.Context!.ChangeTracker.Entries<ModerationRecognitionPrivacySource>().Any())
                clock.UtcNow = source;
            if (AdoptionDeadline is { } adoption && eventData.Context!.ChangeTracker.Entries<ModerationRecognitionPrivacyAdoption>().Any())
                clock.UtcNow = adoption;
            if (RevokeSourceOwner is { } sourceOwner && eventData.Context!.ChangeTracker.Entries<ModerationRecognitionPrivacySource>().Any())
                await eventData.Context.Set<PlatformUserRole>().Where(row => row.UserId == sourceOwner).ExecuteDeleteAsync(cancellationToken);
            if (RevokeAdoptingOwner is { } adopter && eventData.Context!.ChangeTracker.Entries<ModerationRecognitionPrivacyAdoption>()
                .Any(entry => entry.Entity.Revision == 1))
                await eventData.Context.Set<PlatformUserRole>().Where(row => row.UserId == adopter).ExecuteDeleteAsync(cancellationToken);
            if (RevokeWithdrawingActor is { } actor && eventData.Context!.ChangeTracker
                .Entries<ModerationRecognitionPrivacyAdoption>().Any(entry => entry.Entity.Revision == 2))
                await eventData.Context.Set<PlatformUserRole>().Where(row => row.UserId == actor).ExecuteDeleteAsync(cancellationToken);
            return result;
        }
    }
}
