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
    public enum ChildAgeChange
    {
        None, MissingSourceReceipt, MissingAdoptionReceipt, ReviewerRoleLost, CollectionRoleLost,
        RecorderRoleLost, AdoptionExpired, NewerSource, WithdrawAfterExpiryAndOwnerDeparture,
        DisposedSession, RolledBackTransaction, SourceExpiresAfterSave, AdoptionExpiresAfterSave,
        ForeignContext, WrongTransaction, WrongMode, TrustDrift, CollectionRoleMissingBeforeRecord,
        WithdrawerRoleLostAfterSave, ExpiredSourceReplay, ProviderTenantDrift, SourceOwnerLostAfterSave,
        ReviewerLostAfterAdoptionSave, SourceReceiptDigestDrift, DisposalBetweenSourceAndAdoption,
        WrongProfile, WrongPolicy, WrongPolicyRevision, NonAgeProfile, AdoptionReceiptDigestDrift,
        SourceEpochDriftAfterSave, AdoptionEpochDriftAfterSave, WithdrawalEpochDriftAfterSave
    }

    [Test]
    [Arguments(ChildAgeChange.None)]
    [Arguments(ChildAgeChange.MissingSourceReceipt)]
    [Arguments(ChildAgeChange.MissingAdoptionReceipt)]
    [Arguments(ChildAgeChange.ReviewerRoleLost)]
    [Arguments(ChildAgeChange.CollectionRoleLost)]
    [Arguments(ChildAgeChange.RecorderRoleLost)]
    [Arguments(ChildAgeChange.AdoptionExpired)]
    [Arguments(ChildAgeChange.NewerSource)]
    [Arguments(ChildAgeChange.WithdrawAfterExpiryAndOwnerDeparture)]
    [Arguments(ChildAgeChange.DisposedSession)]
    [Arguments(ChildAgeChange.RolledBackTransaction)]
    [Arguments(ChildAgeChange.SourceExpiresAfterSave)]
    [Arguments(ChildAgeChange.AdoptionExpiresAfterSave)]
    [Arguments(ChildAgeChange.ForeignContext)]
    [Arguments(ChildAgeChange.WrongTransaction)]
    [Arguments(ChildAgeChange.WrongMode)]
    [Arguments(ChildAgeChange.TrustDrift)]
    [Arguments(ChildAgeChange.CollectionRoleMissingBeforeRecord)]
    [Arguments(ChildAgeChange.WithdrawerRoleLostAfterSave)]
    [Arguments(ChildAgeChange.ExpiredSourceReplay)]
    [Arguments(ChildAgeChange.ProviderTenantDrift)]
    [Arguments(ChildAgeChange.SourceOwnerLostAfterSave)]
    [Arguments(ChildAgeChange.ReviewerLostAfterAdoptionSave)]
    [Arguments(ChildAgeChange.SourceReceiptDigestDrift)]
    [Arguments(ChildAgeChange.DisposalBetweenSourceAndAdoption)]
    [Arguments(ChildAgeChange.WrongProfile)]
    [Arguments(ChildAgeChange.WrongPolicy)]
    [Arguments(ChildAgeChange.WrongPolicyRevision)]
    [Arguments(ChildAgeChange.NonAgeProfile)]
    [Arguments(ChildAgeChange.AdoptionReceiptDigestDrift)]
    [Arguments(ChildAgeChange.SourceEpochDriftAfterSave)]
    [Arguments(ChildAgeChange.AdoptionEpochDriftAfterSave)]
    [Arguments(ChildAgeChange.WithdrawalEpochDriftAfterSave)]
    public async Task ChildAgePolicy_RequiresCurrentNativeSourceAndIndependentAdoption(ChildAgeChange change)
    {
        await using var database = await Database.CreateAsync();
        var clock = new DeploymentClock();
        var flush = new ChildAgeFlushObserver(clock);
        await using var context = database.Context(flush);
        var request = database.Request(change == ChildAgeChange.NonAgeProfile
            ? ModerationReferenceProfileId.CFG05 : ModerationReferenceProfileId.CFG28);
        request = request with { Proposed = request.Proposed with { RequestedState = ModerationPolicyRequestedState.Active } };
        var (admin, machine) = DeploymentIdentity(context, database.AdminId);
        var publications = new ModerationPolicyPublicationRepository(context, admin, machine, database.Inventory, null, clock);
        var prepared = await publications.PrepareAsync(request, Now.AddMinutes(10));
        await Assert.That(prepared.Outcome).IsEqualTo(ModerationPolicyPublicationOutcome.Known);
        var policy = ModerationPolicyPublicationProjection.RestoreCandidate(prepared.Candidate!, database.Inventory)!;
        var reviewer = Guid.CreateVersion7();
        var collection = Guid.CreateVersion7();
        var role = await context.Roles.SingleAsync(row =>
            row.RoleScopeId == (int)RoleScopeEnum.Platform && row.MasterCode == "platform.admin");
        foreach (var id in new[] { reviewer, collection })
        {
            var user = new User
            {
                Id = id,
                Pii = new UserPii
                { UserId = id, Email = $"{id:N}@example.invalid", FirstName = "Governance", LastName = "Owner" }
            };
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
        var record = new RecordModerationChildAgePolicySource
        {
            OperationId = Guid.CreateVersion7(),
            Target = database.Scope,
            ExpectedQualificationRevision = 2,
            SourceId = Guid.CreateVersion7(),
            PolicyId = request.Proposed.PolicyId,
            ExpectedPolicyRevision = 1,
            PublicationId = request.Proposed.PublicationId,
            ProfileId = request.Proposed.Profiles.Single().Id,
            Mode = ModerationNativeMode.Embedded,
            ValidUntil = Now.AddMinutes(5),
            Terms = ChildAgeTerms(),
            Ownership = new(reviewer, reviewer, collection, collection, database.AdminId,
                "Distinct canonical controllers reviewed with conflicts excluded.",
                "Independent privacy and legal purpose review mandate.",
                "Independent child-risk and safeguarding review mandate.",
                "Accountable collection, correction and erasure implementation owner.")
        };
        var writer = Writer(database.AdminId);
        if (change is ChildAgeChange.WrongProfile or ChildAgeChange.WrongPolicy or ChildAgeChange.WrongPolicyRevision
            or ChildAgeChange.NonAgeProfile or ChildAgeChange.CollectionRoleMissingBeforeRecord)
        {
            if (change == ChildAgeChange.WrongProfile) record = record with { ProfileId = Guid.CreateVersion7() };
            if (change == ChildAgeChange.WrongPolicy) record = record with { PolicyId = Guid.CreateVersion7() };
            if (change == ChildAgeChange.WrongPolicyRevision) record = record with { ExpectedPolicyRevision = 2 };
            if (change == ChildAgeChange.CollectionRoleMissingBeforeRecord)
            {
                context.PlatformUserRoles.Remove(await context.PlatformUserRoles.SingleAsync(row => row.UserId == collection));
                await context.SaveChangesAsync();
                context.ChangeTracker.Clear();
            }
            await Assert.That((await writer.ExecuteAsync(record)).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Unavailable);
            context.ModerationTrustDomainId = database.Scope.TrustDomainId;
            await Assert.That(await context.Set<ModerationChildAgePolicySource>().CountAsync()).IsEqualTo(0);
            return;
        }
        if (change is ChildAgeChange.SourceExpiresAfterSave or ChildAgeChange.SourceOwnerLostAfterSave
            or ChildAgeChange.SourceEpochDriftAfterSave)
        {
            if (change == ChildAgeChange.SourceExpiresAfterSave) flush.SourceDeadline = record.ValidUntil;
            else if (change == ChildAgeChange.SourceOwnerLostAfterSave) flush.RevokeSourceOwner = collection;
            else flush.EpochDrift = change;
            await Assert.That((await writer.ExecuteAsync(record)).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Unavailable);
            context.ModerationTrustDomainId = database.Scope.TrustDomainId;
            await Assert.That(await context.Set<ModerationChildAgePolicySource>().CountAsync()).IsEqualTo(0);
            await Assert.That(await context.ModerationQualificationMutationReceipts.AnyAsync(row => row.Id == record.OperationId)).IsFalse();
            await Assert.That((await context.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(2);
            return;
        }
        await Assert.That((await writer.ExecuteAsync(record)).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
        await Assert.That((await writer.ExecuteAsync(record)).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
        if (change == ChildAgeChange.None)
        {
            await using var sourceTransaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var sourceAuthority = await new ModerationQualificationAuthority(context, admin, machine)
                .AcquireAsync(database.Scope, request.Proposed.PublicationId, default);
            using var sourceSession = publications.BorrowConsumerEvidence(sourceAuthority!, policy);
            var sourceOwner = new ModerationChildAgePolicySourceOwner(context,
                new ModerationPolicyDefinitionSourceOwner(context, admin, machine, database.Inventory, clock), clock);
            await Assert.That(await sourceOwner.AcquireSourceAsync(sourceSession, sourceTransaction.TransactionId,
                record.ProfileId, record.Mode, clock.UtcNow, default)).IsNotNull();
            await Assert.That(await sourceOwner.ReadAdoptionAsync(sourceSession, sourceTransaction.TransactionId,
                record.ProfileId, record.Mode, clock.UtcNow, default)).IsNull();
            await Assert.That(await sourceOwner.AcquireCalendarAsync(sourceSession, sourceTransaction.TransactionId,
                record.ProfileId, record.Mode, clock.UtcNow, default)).IsNull();
            await sourceTransaction.RollbackAsync();
        }
        var adopt = new AdoptModerationChildAgePolicySource
        {
            OperationId = Guid.CreateVersion7(),
            Target = database.Scope,
            ExpectedQualificationRevision = 3,
            SourceId = record.SourceId,
            ExpectedSourceRevision = 1,
            ExpectedSourceWriterGeneration = 3,
            AdoptionId = Guid.CreateVersion7(),
            ValidUntil = Now.AddMinutes(4)
        };
        await Assert.That((await writer.ExecuteAsync(adopt)).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Denied);
        await Assert.That((await Writer(collection).ExecuteAsync(adopt)).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Denied);
        await Assert.That((await Writer(reviewer).ExecuteAsync(adopt with { ValidUntil = record.ValidUntil.AddTicks(1) })).Outcome)
            .IsEqualTo(ModerationQualificationWriteOutcome.Denied);
        if (change is ChildAgeChange.AdoptionExpiresAfterSave or ChildAgeChange.ReviewerLostAfterAdoptionSave
            or ChildAgeChange.AdoptionEpochDriftAfterSave)
        {
            if (change == ChildAgeChange.AdoptionExpiresAfterSave) flush.AdoptionDeadline = adopt.ValidUntil;
            else if (change == ChildAgeChange.ReviewerLostAfterAdoptionSave) flush.RevokeAdopter = reviewer;
            else flush.EpochDrift = change;
            await Assert.That((await Writer(reviewer).ExecuteAsync(adopt)).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Unavailable);
            context.ModerationTrustDomainId = database.Scope.TrustDomainId;
            await Assert.That(await context.Set<ModerationChildAgePolicyAdoption>().CountAsync()).IsEqualTo(0);
            await Assert.That(await context.ModerationQualificationMutationReceipts.AnyAsync(row => row.Id == adopt.OperationId)).IsFalse();
            await Assert.That((await context.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(3);
            return;
        }
        await Assert.That((await Writer(reviewer).ExecuteAsync(adopt)).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
        await Assert.That((await Writer(reviewer).ExecuteAsync(adopt)).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
        await Assert.That((await writer.ExecuteAsync(record with { Terms = record.Terms with { AdulthoodAge = 21 } })).Outcome)
            .IsEqualTo(ModerationQualificationWriteOutcome.Conflict);
        context.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await Assert.That(await context.ModerationPolicySelections.CountAsync()).IsEqualTo(0);
        await Assert.That(await context.ModerationConsumerMeasurementReceipts.CountAsync()).IsEqualTo(0);
        if (change is ChildAgeChange.MissingSourceReceipt or ChildAgeChange.MissingAdoptionReceipt)
        {
            context.ModerationQualificationMutationReceipts.Remove(await context.ModerationQualificationMutationReceipts
                .SingleAsync(row => row.Id == (change == ChildAgeChange.MissingSourceReceipt ? record.OperationId : adopt.OperationId)));
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
        }
        if (change is ChildAgeChange.ReviewerRoleLost or ChildAgeChange.CollectionRoleLost or ChildAgeChange.RecorderRoleLost)
        {
            var human = change == ChildAgeChange.ReviewerRoleLost ? reviewer
                : change == ChildAgeChange.CollectionRoleLost ? collection : database.AdminId;
            context.PlatformUserRoles.Remove(await context.PlatformUserRoles.SingleAsync(row => row.UserId == human));
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
        }
        if (change == ChildAgeChange.AdoptionExpired) clock.UtcNow = adopt.ValidUntil;
        if (change is ChildAgeChange.NewerSource or ChildAgeChange.WithdrawAfterExpiryAndOwnerDeparture)
            await Assert.That((await writer.ExecuteAsync(record with
            { OperationId = Guid.CreateVersion7(), SourceId = Guid.CreateVersion7(), ExpectedQualificationRevision = 4 })).Outcome)
                .IsEqualTo(ModerationQualificationWriteOutcome.Known);
        if (change is ChildAgeChange.SourceReceiptDigestDrift or ChildAgeChange.AdoptionReceiptDigestDrift)
        {
            var id = change == ChildAgeChange.SourceReceiptDigestDrift ? record.OperationId : adopt.OperationId;
            await context.ModerationQualificationMutationReceipts.Where(row => row.Id == id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.RequestDigest, new string('a', 64)));
        }
        if (change is ChildAgeChange.WithdrawAfterExpiryAndOwnerDeparture or ChildAgeChange.WithdrawerRoleLostAfterSave
            or ChildAgeChange.WithdrawalEpochDriftAfterSave)
        {
            clock.UtcNow = Now.AddMinutes(20);
            var withdraw = new WithdrawModerationChildAgePolicyAdoption
            {
                OperationId = Guid.CreateVersion7(),
                Target = database.Scope,
                ExpectedQualificationRevision = change == ChildAgeChange.WithdrawAfterExpiryAndOwnerDeparture ? 5 : 4,
                AdoptionId = adopt.AdoptionId,
                ExpectedAdoptionRevision = 1
            };
            if (change == ChildAgeChange.WithdrawerRoleLostAfterSave) flush.RevokeWithdrawer = database.AdminId;
            else if (change == ChildAgeChange.WithdrawalEpochDriftAfterSave) flush.EpochDrift = change;
            else
            {
                await context.PlatformUserRoles.Where(row => row.UserId == reviewer || row.UserId == collection).ExecuteDeleteAsync();
            }
            var withdrawn = await writer.ExecuteAsync(withdraw);
            context.ModerationTrustDomainId = database.Scope.TrustDomainId;
            if (change is ChildAgeChange.WithdrawerRoleLostAfterSave or ChildAgeChange.WithdrawalEpochDriftAfterSave)
            {
                await Assert.That(withdrawn.Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Unavailable);
                await Assert.That((await context.Set<ModerationChildAgePolicyAdoption>().AsNoTracking().SingleAsync()).Revision).IsEqualTo(1);
                await Assert.That(await context.ModerationQualificationMutationReceipts.AnyAsync(row => row.Id == withdraw.OperationId)).IsFalse();
                await Assert.That((await context.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(4);
                return;
            }
            await Assert.That(withdrawn.Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
            await Assert.That((await writer.ExecuteAsync(withdraw)).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
        }
        if (change == ChildAgeChange.ExpiredSourceReplay)
        {
            clock.UtcNow = record.ValidUntil;
            await Assert.That((await writer.ExecuteAsync(record)).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Unavailable);
        }
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var observer = DeploymentIdentity(context, change == ChildAgeChange.RecorderRoleLost ? reviewer : database.AdminId);
        var authority = await new ModerationQualificationAuthority(context, observer.Admin, observer.Machine)
            .AcquireAsync(database.Scope, request.Proposed.PublicationId, default);
        await Assert.That(authority).IsNotNull();
        var observerPublications = new ModerationPolicyPublicationRepository(context, observer.Admin, observer.Machine,
            database.Inventory, null, clock);
        using var evidence = observerPublications.BorrowConsumerEvidence(authority!, policy);
        if (change == ChildAgeChange.DisposedSession) evidence.Dispose();
        if (change == ChildAgeChange.RolledBackTransaction) await transaction.RollbackAsync();
        await using var foreign = database.Context();
        var ownerContext = change == ChildAgeChange.ForeignContext ? foreign : context;
        var owner = new ModerationChildAgePolicySourceOwner(ownerContext,
            new ModerationPolicyDefinitionSourceOwner(ownerContext, observer.Admin, observer.Machine, database.Inventory, clock), clock);
        if (change == ChildAgeChange.DisposalBetweenSourceAndAdoption)
        {
            await Assert.That(await owner.AcquireSourceAsync(evidence, transaction.TransactionId,
                record.ProfileId, record.Mode, clock.UtcNow, default)).IsNotNull();
            evidence.Dispose();
            await Assert.That(await owner.ReadAdoptionAsync(evidence, transaction.TransactionId,
                record.ProfileId, record.Mode, clock.UtcNow, default)).IsNull();
        }
        if (change == ChildAgeChange.TrustDrift) context.ModerationTrustDomainId = Guid.CreateVersion7();
        if (change == ChildAgeChange.ProviderTenantDrift) context.TenantContext = new ChildAgeTenantContext(Guid.CreateVersion7());
        var binding = change == ChildAgeChange.WrongMode
            ? policy.Consumers[0].Binding with { Mode = ModerationNativeMode.RemoteStrict } : policy.Consumers[0].Binding;
        var observation = await owner.AcquirePrerequisiteAsync(evidence,
            change == ChildAgeChange.WrongTransaction ? Guid.CreateVersion7() : transaction.TransactionId,
            binding, clock.UtcNow, default);
        var calendar = await owner.AcquireCalendarAsync(evidence,
            change == ChildAgeChange.WrongTransaction ? Guid.CreateVersion7() : transaction.TransactionId,
            record.ProfileId, binding.Mode, clock.UtcNow, default);
        if (change == ChildAgeChange.None)
        {
            await Assert.That(observation!.SourceId).IsEqualTo(record.SourceId);
            await Assert.That(observation.ApprovalId).IsEqualTo(adopt.AdoptionId);
            await Assert.That(observation.ValidUntil).IsEqualTo(adopt.ValidUntil);
            await Assert.That(observation.Prerequisite).IsEqualTo(ModerationActivationPrerequisite.ChildAndAgePolicy);
            await Assert.That(calendar).IsNotNull();
            await Assert.That(calendar!.TransactionId).IsEqualTo(transaction.TransactionId);
            await Assert.That(calendar.Policy.PolicyId).IsEqualTo(record.PolicyId);
            await Assert.That(calendar.Policy.Revision).IsEqualTo(record.ExpectedPolicyRevision);
            await Assert.That(calendar.Policy.Jurisdiction).IsEqualTo(database.Scope);
            await Assert.That(calendar.Policy.AuthorityTimeZone.Id).IsEqualTo(TimeZoneInfo.Utc.Id);
            await Assert.That(calendar.Policy.LeapDayAnniversary).IsEqualTo(ModerationLeapDayAnniversary.March1);
            await Assert.That(calendar.SourceId).IsEqualTo(record.SourceId);
            await Assert.That(calendar.SourceRevision).IsEqualTo(1);
            await Assert.That(calendar.SourceWriterGeneration).IsEqualTo(3);
            await Assert.That(calendar.AdoptionId).IsEqualTo(adopt.AdoptionId);
            await Assert.That(calendar.AdoptionRevision).IsEqualTo(1);
            await Assert.That(calendar.AdoptionWriterGeneration).IsEqualTo(4);
            await Assert.That(calendar.AdulthoodAge).IsEqualTo(record.Terms.AdulthoodAge);
            await Assert.That(calendar.DebateMinimumAge).IsEqualTo(record.Terms.DebateMinimumAge);
            await Assert.That(calendar.ValidUntil).IsEqualTo(adopt.ValidUntil);
        }
        else
        {
            await Assert.That(observation).IsNull();
            await Assert.That(calendar).IsNull();
        }
    }

    private static ModerationChildAgePolicyTerms ChildAgeTerms() => new(
        ModerationScopeKind.Tenant, ModerationChildAgeCollection.LawfulMinimumBeforeSensitiveBodyHandling,
        ModerationChildAgeConsent.ActualSubjectPurposePolicyBoundPrerequisites,
        ModerationChildAgeAssurance.QualifiedLocalManualThresholdNotDeclaredDob,
        ModerationChildAgeProviderAssurance.NotAccepted,
        ModerationChildAgeEvidence.UnknownContradictoryExpiredRemainRestrictedNotGuilty,
        ModerationChildAgeClassification.ReviewedContentNotEventFormat, 18,
        ModerationChildAgeAudience.WholeEventUnlessCompleteSeparatelyQualifiedSegregation,
        ModerationChildAgeParticipant.ActualViewerAndParticipantNeverPurchaser,
        ModerationChildAgeCorrection.InvalidateDerivedAgeWithoutStrikeOrRecognitionRestoration,
        ModerationChildAgeProtectedAccess.CatalogueRemedyOrdinaryErasureIndependent,
        ModerationChildAgeProhibitedInference.NoChildReligionBehaviourOrUniversalIdentityFace,
        ModerationChildAgeCalendar.Gregorian, ModerationChildAgeTimeZone.Utc,
        ModerationChildAgeLeapDay.MarchFirstInNonLeapYear,
        ModerationChildAgeAdulthood.ReconsiderGuardianGrantsAndPurposes, 18,
        "Reviewed age-inappropriate exposure and child risk.",
        "Reviewed jurisdiction, lawful basis and minimization.",
        "Minimum private DOB or threshold for actual subject audience eligibility.",
        "Subject-specific consent or guardian prerequisites reviewed for each applicable purpose.",
        "Qualified local manual threshold assurance accepted with finite minimized provenance.", null,
        "ISLAMU Debate floor 18 is adopted policy, not universal legal or religious certification.",
        "Qualified jurisdiction review adopts Gregorian UTC and March 1 leap-day eligibility.");

    private sealed record ChildAgeTenantContext(Guid TenantId) : Explore.Application.Contracts.Infrastructure.ITenantContext;

    private sealed class ChildAgeFlushObserver(DeploymentClock clock) : SaveChangesInterceptor
    {
        public DateTimeOffset? SourceDeadline { get; set; }
        public DateTimeOffset? AdoptionDeadline { get; set; }
        public Guid? RevokeSourceOwner { get; set; }
        public Guid? RevokeAdopter { get; set; }
        public Guid? RevokeWithdrawer { get; set; }
        public ChildAgeChange? EpochDrift { get; set; }
        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData,
            int result, CancellationToken cancellationToken = default)
        {
            var context = eventData.Context!;
            if (context.ChangeTracker.Entries<ModerationChildAgePolicySource>().Any())
            {
                if (SourceDeadline is { } deadline) clock.UtcNow = deadline;
                if (RevokeSourceOwner is { } human)
                    await context.Set<PlatformUserRole>().Where(row => row.UserId == human).ExecuteDeleteAsync(cancellationToken);
            }
            if (context.ChangeTracker.Entries<ModerationChildAgePolicyAdoption>().Any(entry => entry.Entity.Revision == 1))
            {
                if (AdoptionDeadline is { } deadline) clock.UtcNow = deadline;
                if (RevokeAdopter is { } human)
                    await context.Set<PlatformUserRole>().Where(row => row.UserId == human).ExecuteDeleteAsync(cancellationToken);
            }
            if (RevokeWithdrawer is { } withdrawer
                && context.ChangeTracker.Entries<ModerationChildAgePolicyAdoption>().Any(entry => entry.Entity.Revision == 2))
                await context.Set<PlatformUserRole>().Where(row => row.UserId == withdrawer).ExecuteDeleteAsync(cancellationToken);
            if (EpochDrift == ChildAgeChange.SourceEpochDriftAfterSave && context.ChangeTracker.Entries<ModerationChildAgePolicySource>().Any()
                || EpochDrift == ChildAgeChange.AdoptionEpochDriftAfterSave
                    && context.ChangeTracker.Entries<ModerationChildAgePolicyAdoption>().Any(entry => entry.Entity.Revision == 1)
                || EpochDrift == ChildAgeChange.WithdrawalEpochDriftAfterSave
                    && context.ChangeTracker.Entries<ModerationChildAgePolicyAdoption>().Any(entry => entry.Entity.Revision == 2))
                await context.Set<ModerationQualificationEpoch>().ExecuteUpdateAsync(
                    setters => setters.SetProperty(row => row.Revision, row => row.Revision + 1), cancellationToken);
            return result;
        }
    }
}
