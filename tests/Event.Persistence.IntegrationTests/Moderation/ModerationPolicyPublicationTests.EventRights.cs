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
    public enum EventRightsChange
    {
        None, MissingSourceReceipt, MissingAdoptionReceipt, ReviewerRoleLost, ContinuityRoleLost,
        SettlementRoleLost, RecorderRoleLost, ControllerDeparted, AdoptionExpired, NewerSource,
        WithdrawAfterExpiryReplacementAndOwnerDeparture, DisposedSession, RolledBackTransaction,
        SourceExpiresAfterSave, AdoptionExpiresAfterSave, ForeignContext, WrongTransaction, WrongMode,
        TrustDrift, ProviderTenantDrift, ContinuityRoleMissingBeforeRecord, SettlementRoleMissingBeforeRecord,
        WithdrawerRoleLostAfterSave, ExpiredSourceReplay, SourceOwnerLostAfterSave, ReviewerLostAfterAdoptionSave,
        SourceReceiptDigestDrift, DisposalBetweenSourceAndAdoption, WrongProfile, WrongPolicy,
        WrongPolicyRevision, UnrelatedProfile, AdoptionReceiptDigestDrift, SourceEpochDriftAfterSave,
        AdoptionEpochDriftAfterSave, WithdrawalEpochDriftAfterSave, ObservationEpochDrift,
        WithdrawalMissingSourceReceipt, WithdrawalMissingAdoptionReceipt, DeselectedDefinition,
        WithdrawalAfterDeselection, MissingRightsMandate, MissingContinuityMandate, MissingSettlementMandate
    }

    [Test]
    [Arguments(EventRightsChange.None)]
    [Arguments(EventRightsChange.MissingSourceReceipt)]
    [Arguments(EventRightsChange.MissingAdoptionReceipt)]
    [Arguments(EventRightsChange.ReviewerRoleLost)]
    [Arguments(EventRightsChange.ContinuityRoleLost)]
    [Arguments(EventRightsChange.SettlementRoleLost)]
    [Arguments(EventRightsChange.RecorderRoleLost)]
    [Arguments(EventRightsChange.ControllerDeparted)]
    [Arguments(EventRightsChange.AdoptionExpired)]
    [Arguments(EventRightsChange.NewerSource)]
    [Arguments(EventRightsChange.WithdrawAfterExpiryReplacementAndOwnerDeparture)]
    [Arguments(EventRightsChange.DisposedSession)]
    [Arguments(EventRightsChange.RolledBackTransaction)]
    [Arguments(EventRightsChange.SourceExpiresAfterSave)]
    [Arguments(EventRightsChange.AdoptionExpiresAfterSave)]
    [Arguments(EventRightsChange.ForeignContext)]
    [Arguments(EventRightsChange.WrongTransaction)]
    [Arguments(EventRightsChange.WrongMode)]
    [Arguments(EventRightsChange.TrustDrift)]
    [Arguments(EventRightsChange.ProviderTenantDrift)]
    [Arguments(EventRightsChange.ContinuityRoleMissingBeforeRecord)]
    [Arguments(EventRightsChange.SettlementRoleMissingBeforeRecord)]
    [Arguments(EventRightsChange.WithdrawerRoleLostAfterSave)]
    [Arguments(EventRightsChange.ExpiredSourceReplay)]
    [Arguments(EventRightsChange.SourceOwnerLostAfterSave)]
    [Arguments(EventRightsChange.ReviewerLostAfterAdoptionSave)]
    [Arguments(EventRightsChange.SourceReceiptDigestDrift)]
    [Arguments(EventRightsChange.DisposalBetweenSourceAndAdoption)]
    [Arguments(EventRightsChange.WrongProfile)]
    [Arguments(EventRightsChange.WrongPolicy)]
    [Arguments(EventRightsChange.WrongPolicyRevision)]
    [Arguments(EventRightsChange.UnrelatedProfile)]
    [Arguments(EventRightsChange.AdoptionReceiptDigestDrift)]
    [Arguments(EventRightsChange.SourceEpochDriftAfterSave)]
    [Arguments(EventRightsChange.AdoptionEpochDriftAfterSave)]
    [Arguments(EventRightsChange.WithdrawalEpochDriftAfterSave)]
    [Arguments(EventRightsChange.ObservationEpochDrift)]
    [Arguments(EventRightsChange.WithdrawalMissingSourceReceipt)]
    [Arguments(EventRightsChange.WithdrawalMissingAdoptionReceipt)]
    [Arguments(EventRightsChange.DeselectedDefinition)]
    [Arguments(EventRightsChange.WithdrawalAfterDeselection)]
    [Arguments(EventRightsChange.MissingRightsMandate)]
    [Arguments(EventRightsChange.MissingContinuityMandate)]
    [Arguments(EventRightsChange.MissingSettlementMandate)]
    public async Task EventRights_requires_native_receipts_independent_adoption_and_original_current_acquisition(EventRightsChange change)
    {
        await using var database = await Database.CreateAsync();
        var clock = new DeploymentClock();
        var flush = new EventRightsFlushObserver(clock);
        await using var context = database.Context(flush);
        var request = database.Request(change == EventRightsChange.UnrelatedProfile
            ? ModerationReferenceProfileId.CFG05 : ModerationReferenceProfileId.CFG11);
        request = request with { Proposed = request.Proposed with { RequestedState = ModerationPolicyRequestedState.Active } };
        var (admin, machine) = DeploymentIdentity(context, database.AdminId);
        var publications = new ModerationPolicyPublicationRepository(context, admin, machine, database.Inventory, null, clock);
        var prepared = await publications.PrepareAsync(request, Now.AddMinutes(10));
        await Assert.That(prepared.Outcome).IsEqualTo(ModerationPolicyPublicationOutcome.Known);
        var policy = ModerationPolicyPublicationProjection.RestoreCandidate(prepared.Candidate!, database.Inventory)!;
        var reviewer = Guid.CreateVersion7();
        var continuity = Guid.CreateVersion7();
        var settlement = Guid.CreateVersion7();
        var controller = Guid.CreateVersion7();
        var role = await context.Roles.SingleAsync(row =>
            row.RoleScopeId == (int)RoleScopeEnum.Platform && row.MasterCode == "platform.admin");
        foreach (var id in new[] { reviewer, continuity, settlement, controller })
        {
            var user = new User
            {
                Id = id,
                Pii = new UserPii
                { UserId = id, Email = $"{id:N}@example.invalid", FirstName = "Governance", LastName = "Owner" }
            };
            context.Users.Add(user);
            if (id != controller)
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
        var record = new RecordModerationEventRightsSource
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
            Terms = EventRightsTerms(),
            Ownership = new(reviewer, controller, continuity, continuity, settlement, settlement, database.AdminId,
                "Distinct canonical controllers reviewed with conflicts excluded.",
                "Independent event rights and premises review mandate.",
                "Accountable governance of continuity mapping, not actual host permission.",
                "Accountable governance of settlement mapping, not merchant or finance permission.")
        };
        var writer = Writer(database.AdminId);
        if (change is EventRightsChange.MissingRightsMandate or EventRightsChange.MissingContinuityMandate
            or EventRightsChange.MissingSettlementMandate)
        {
            record = record with
            {
                Ownership = change switch
                {
                    EventRightsChange.MissingRightsMandate => record.Ownership with { RightsReviewMandateGrounds = "" },
                    EventRightsChange.MissingContinuityMandate => record.Ownership with { ContinuityMappingMandateGrounds = "" },
                    _ => record.Ownership with { SettlementMappingMandateGrounds = "" }
                }
            };
            await Assert.That(async () => await writer.ExecuteAsync(record)).Throws<ArgumentException>();
            context.ModerationTrustDomainId = database.Scope.TrustDomainId;
            await Assert.That(await context.Set<ModerationEventRightsSource>().CountAsync()).IsEqualTo(0);
            await Assert.That((await context.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(2);
            return;
        }
        if (change is EventRightsChange.WrongProfile or EventRightsChange.WrongPolicy or EventRightsChange.WrongPolicyRevision
            or EventRightsChange.UnrelatedProfile or EventRightsChange.ContinuityRoleMissingBeforeRecord
            or EventRightsChange.SettlementRoleMissingBeforeRecord)
        {
            if (change == EventRightsChange.WrongProfile) record = record with { ProfileId = Guid.CreateVersion7() };
            if (change == EventRightsChange.WrongPolicy) record = record with { PolicyId = Guid.CreateVersion7() };
            if (change == EventRightsChange.WrongPolicyRevision) record = record with { ExpectedPolicyRevision = 2 };
            if (change is EventRightsChange.ContinuityRoleMissingBeforeRecord or EventRightsChange.SettlementRoleMissingBeforeRecord)
            {
                var human = change == EventRightsChange.ContinuityRoleMissingBeforeRecord ? continuity : settlement;
                await context.PlatformUserRoles.Where(row => row.UserId == human).ExecuteDeleteAsync();
            }
            await Assert.That((await writer.ExecuteAsync(record)).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Unavailable);
            context.ModerationTrustDomainId = database.Scope.TrustDomainId;
            await Assert.That(await context.Set<ModerationEventRightsSource>().CountAsync()).IsEqualTo(0);
            return;
        }
        if (change is EventRightsChange.SourceExpiresAfterSave or EventRightsChange.SourceOwnerLostAfterSave
            or EventRightsChange.SourceEpochDriftAfterSave)
        {
            if (change == EventRightsChange.SourceExpiresAfterSave) flush.SourceDeadline = record.ValidUntil;
            else if (change == EventRightsChange.SourceOwnerLostAfterSave) flush.RevokeSourceOwner = settlement;
            else flush.EpochDrift = change;
            await Assert.That((await writer.ExecuteAsync(record)).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Unavailable);
            context.ModerationTrustDomainId = database.Scope.TrustDomainId;
            await Assert.That(await context.Set<ModerationEventRightsSource>().CountAsync()).IsEqualTo(0);
            await Assert.That(await context.ModerationQualificationMutationReceipts.AnyAsync(row => row.Id == record.OperationId)).IsFalse();
            await Assert.That((await context.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(2);
            return;
        }
        await Assert.That((await writer.ExecuteAsync(record)).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
        await Assert.That((await writer.ExecuteAsync(record)).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
        if (change == EventRightsChange.None)
        {
            await using var sourceTransaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var sourceAuthority = await new ModerationQualificationAuthority(context, admin, machine)
                .AcquireAsync(database.Scope, request.Proposed.PublicationId, default);
            using var sourceSession = publications.BorrowConsumerEvidence(sourceAuthority!, policy);
            var sourceOwner = new ModerationEventRightsSourceOwner(context,
                new ModerationPolicyDefinitionSourceOwner(context, admin, machine, database.Inventory, clock), clock);
            await Assert.That(await sourceOwner.AcquireSourceAsync(sourceSession, sourceTransaction.TransactionId,
                record.ProfileId, record.Mode, clock.UtcNow, default)).IsNotNull();
            await Assert.That(await sourceOwner.ReadAdoptionAsync(sourceSession, sourceTransaction.TransactionId,
                record.ProfileId, record.Mode, clock.UtcNow, default)).IsNull();
            await Assert.That(await sourceOwner.AcquirePrerequisiteAsync(sourceSession, sourceTransaction.TransactionId,
                policy.Consumers[0].Binding, clock.UtcNow, default)).IsNull();
            await sourceTransaction.RollbackAsync();
        }
        var adopt = new AdoptModerationEventRightsSource
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
        foreach (var human in new[] { database.AdminId, continuity, settlement })
            await Assert.That((await Writer(human).ExecuteAsync(adopt)).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Denied);
        await Assert.That((await Writer(reviewer).ExecuteAsync(adopt with { ValidUntil = record.ValidUntil.AddTicks(1) })).Outcome)
            .IsEqualTo(ModerationQualificationWriteOutcome.Denied);
        if (change is EventRightsChange.AdoptionExpiresAfterSave or EventRightsChange.ReviewerLostAfterAdoptionSave
            or EventRightsChange.AdoptionEpochDriftAfterSave)
        {
            if (change == EventRightsChange.AdoptionExpiresAfterSave) flush.AdoptionDeadline = adopt.ValidUntil;
            else if (change == EventRightsChange.ReviewerLostAfterAdoptionSave) flush.RevokeAdopter = reviewer;
            else flush.EpochDrift = change;
            await Assert.That((await Writer(reviewer).ExecuteAsync(adopt)).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Unavailable);
            context.ModerationTrustDomainId = database.Scope.TrustDomainId;
            await Assert.That(await context.Set<ModerationEventRightsAdoption>().CountAsync()).IsEqualTo(0);
            await Assert.That(await context.ModerationQualificationMutationReceipts.AnyAsync(row => row.Id == adopt.OperationId)).IsFalse();
            await Assert.That((await context.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(3);
            return;
        }
        await Assert.That((await Writer(reviewer).ExecuteAsync(adopt)).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
        await Assert.That((await Writer(reviewer).ExecuteAsync(adopt)).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
        await Assert.That((await writer.ExecuteAsync(record with
        { Terms = record.Terms with { SettlementMappingGrounds = "Different substantive mandate." } })).Outcome)
            .IsEqualTo(ModerationQualificationWriteOutcome.Conflict);
        context.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await Assert.That(await context.ModerationPolicySelections.CountAsync()).IsEqualTo(0);
        await Assert.That(await context.ModerationConsumerMeasurementReceipts.CountAsync()).IsEqualTo(0);
        if (change is EventRightsChange.MissingSourceReceipt or EventRightsChange.MissingAdoptionReceipt
            or EventRightsChange.WithdrawalMissingSourceReceipt or EventRightsChange.WithdrawalMissingAdoptionReceipt)
        {
            var id = change is EventRightsChange.MissingSourceReceipt or EventRightsChange.WithdrawalMissingSourceReceipt
                ? record.OperationId : adopt.OperationId;
            await context.ModerationQualificationMutationReceipts.Where(row => row.Id == id).ExecuteDeleteAsync();
        }
        if (change is EventRightsChange.ReviewerRoleLost or EventRightsChange.ContinuityRoleLost
            or EventRightsChange.SettlementRoleLost or EventRightsChange.RecorderRoleLost)
        {
            var human = change switch
            {
                EventRightsChange.ReviewerRoleLost => reviewer,
                EventRightsChange.ContinuityRoleLost => continuity,
                EventRightsChange.SettlementRoleLost => settlement,
                _ => database.AdminId
            };
            await context.PlatformUserRoles.Where(row => row.UserId == human).ExecuteDeleteAsync();
        }
        if (change == EventRightsChange.ControllerDeparted)
            await context.Users.Where(row => row.Id == controller).ExecuteUpdateAsync(setters => setters.SetProperty(row => row.IsDeleted, true));
        if (change == EventRightsChange.AdoptionExpired) clock.UtcNow = adopt.ValidUntil;
        if (change is EventRightsChange.NewerSource or EventRightsChange.WithdrawAfterExpiryReplacementAndOwnerDeparture)
            await Assert.That((await writer.ExecuteAsync(record with
            { OperationId = Guid.CreateVersion7(), SourceId = Guid.CreateVersion7(), ExpectedQualificationRevision = 4 })).Outcome)
                .IsEqualTo(ModerationQualificationWriteOutcome.Known);
        if (change is EventRightsChange.SourceReceiptDigestDrift or EventRightsChange.AdoptionReceiptDigestDrift)
        {
            var id = change == EventRightsChange.SourceReceiptDigestDrift ? record.OperationId : adopt.OperationId;
            await context.ModerationQualificationMutationReceipts.Where(row => row.Id == id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.RequestDigest, new string('a', 64)));
        }
        if (change is EventRightsChange.DeselectedDefinition or EventRightsChange.WithdrawalAfterDeselection)
        {
            // A native draft publication changes the selected policy without manufacturing activation.
            var draft = database.Request(ModerationReferenceProfileId.CFG05) with { ExpectedQualificationRevision = 4 };
            await Assert.That((await publications.PublishAsync(draft)).Outcome).IsEqualTo(ModerationPolicyPublicationOutcome.Known);
        }
        if (change is EventRightsChange.WithdrawAfterExpiryReplacementAndOwnerDeparture or EventRightsChange.WithdrawerRoleLostAfterSave
            or EventRightsChange.WithdrawalEpochDriftAfterSave or EventRightsChange.WithdrawalMissingSourceReceipt
            or EventRightsChange.WithdrawalMissingAdoptionReceipt or EventRightsChange.WithdrawalAfterDeselection)
        {
            clock.UtcNow = Now.AddMinutes(20);
            var withdraw = new WithdrawModerationEventRightsAdoption
            {
                OperationId = Guid.CreateVersion7(),
                Target = database.Scope,
                ExpectedQualificationRevision = change is EventRightsChange.WithdrawAfterExpiryReplacementAndOwnerDeparture
                    or EventRightsChange.WithdrawalAfterDeselection ? 5 : 4,
                AdoptionId = adopt.AdoptionId,
                ExpectedAdoptionRevision = 1
            };
            if (change == EventRightsChange.WithdrawerRoleLostAfterSave) flush.RevokeWithdrawer = database.AdminId;
            else if (change == EventRightsChange.WithdrawalEpochDriftAfterSave) flush.EpochDrift = change;
            else if (change == EventRightsChange.WithdrawAfterExpiryReplacementAndOwnerDeparture)
                await context.PlatformUserRoles.Where(row => row.UserId == reviewer || row.UserId == continuity
                    || row.UserId == settlement).ExecuteDeleteAsync();
            var withdrawn = await writer.ExecuteAsync(withdraw);
            context.ModerationTrustDomainId = database.Scope.TrustDomainId;
            if (change is EventRightsChange.WithdrawerRoleLostAfterSave or EventRightsChange.WithdrawalEpochDriftAfterSave
                or EventRightsChange.WithdrawalMissingSourceReceipt or EventRightsChange.WithdrawalMissingAdoptionReceipt)
            {
                await Assert.That(withdrawn.Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Unavailable);
                await Assert.That((await context.Set<ModerationEventRightsAdoption>().AsNoTracking().SingleAsync()).Revision).IsEqualTo(1);
                await Assert.That(await context.ModerationQualificationMutationReceipts.AnyAsync(row => row.Id == withdraw.OperationId)).IsFalse();
                await Assert.That((await context.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(4);
                return;
            }
            await Assert.That(withdrawn.Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
            await Assert.That((await writer.ExecuteAsync(withdraw)).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
        }
        if (change == EventRightsChange.ExpiredSourceReplay)
        {
            clock.UtcNow = record.ValidUntil;
            await Assert.That((await writer.ExecuteAsync(record)).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Unavailable);
        }
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var observer = DeploymentIdentity(context, change == EventRightsChange.RecorderRoleLost ? reviewer : database.AdminId);
        var authority = await new ModerationQualificationAuthority(context, observer.Admin, observer.Machine)
            .AcquireAsync(database.Scope, request.Proposed.PublicationId, default);
        await Assert.That(authority).IsNotNull();
        var observerPublications = new ModerationPolicyPublicationRepository(context, observer.Admin, observer.Machine,
            database.Inventory, null, clock);
        using var evidence = observerPublications.BorrowConsumerEvidence(authority!, policy);
        if (change == EventRightsChange.DisposedSession) evidence.Dispose();
        if (change == EventRightsChange.RolledBackTransaction) await transaction.RollbackAsync();
        await using var foreign = database.Context();
        var ownerContext = change == EventRightsChange.ForeignContext ? foreign : context;
        var owner = new ModerationEventRightsSourceOwner(ownerContext,
            new ModerationPolicyDefinitionSourceOwner(ownerContext, observer.Admin, observer.Machine, database.Inventory, clock), clock);
        if (change == EventRightsChange.DisposalBetweenSourceAndAdoption)
        {
            await Assert.That(await owner.AcquireSourceAsync(evidence, transaction.TransactionId,
                record.ProfileId, record.Mode, clock.UtcNow, default)).IsNotNull();
            evidence.Dispose();
            await Assert.That(await owner.ReadAdoptionAsync(evidence, transaction.TransactionId,
                record.ProfileId, record.Mode, clock.UtcNow, default)).IsNull();
        }
        if (change == EventRightsChange.TrustDrift) context.ModerationTrustDomainId = Guid.CreateVersion7();
        if (change == EventRightsChange.ProviderTenantDrift) context.TenantContext = new EventRightsTenantContext(Guid.CreateVersion7());
        if (change == EventRightsChange.ObservationEpochDrift)
            await context.ModerationQualificationEpochs.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Revision, row => row.Revision + 1));
        var binding = change == EventRightsChange.WrongMode
            ? policy.Consumers[0].Binding with { Mode = ModerationNativeMode.RemoteStrict } : policy.Consumers[0].Binding;
        var observation = await owner.AcquirePrerequisiteAsync(evidence,
            change == EventRightsChange.WrongTransaction ? Guid.CreateVersion7() : transaction.TransactionId,
            binding, clock.UtcNow, default);
        if (change == EventRightsChange.None)
        {
            await Assert.That(observation!.SourceId).IsEqualTo(record.SourceId);
            await Assert.That(observation.ApprovalId).IsEqualTo(adopt.AdoptionId);
            await Assert.That(observation.ValidUntil).IsEqualTo(adopt.ValidUntil);
            await Assert.That(observation.Prerequisite).IsEqualTo(ModerationActivationPrerequisite.EventRightsAndPremises);
            // This successful normative observation has no concrete event/participant to certify.
            await Assert.That(await context.Set<ModerationEventTargetAdoption>().CountAsync()).IsEqualTo(0);
            await Assert.That(await context.ModerationConsumerMeasurementReceipts.CountAsync()).IsEqualTo(0);
            await Assert.That(await context.ModerationPolicySelections.CountAsync()).IsEqualTo(0);
        }
        else await Assert.That(observation).IsNull();
    }

    private static ModerationEventRightsTerms EventRightsTerms() => new(ModerationScopeKind.Tenant,
        ModerationEventContinuity.ConsentingQualifiedStewardThenAccountableFallback,
        ModerationEventStewardConsent.SelfRecordedAndCurrentNativeDutyPermission,
        ModerationEventMissingSteward.PauseOnlyDependentRegistrationsAndEscalate,
        ModerationEventPhysicalJurisdiction.ExplicitPremisesEventSessionScopeAndCurrentHostPermission,
        ModerationEventSettlement.QualifiedActualRightsPartitionAndCompleteNativeCauses,
        ModerationEventMissingSettlement.NonurgentUnavailableUrgentNecessaryProtectionWithOwnedPendingSettlement,
        ModerationEventCompanionRights.PreserveInnocentCompanionsAndUnrelatedRights,
        ModerationEventHistoricalCommerce.AcceptedMerchantCurrencyMinorUnitsAndNativeIdempotency,
        ModerationEventRightsCorrection.RemoveOnlyAffectedContributionWithoutRightsResurrection,
        "Reviewed existing native rights and innocent companion partition.",
        "Current willing qualified steward, original deadline and only dependent registration pause.",
        "Explicit premises, event and session scope with current host permission.",
        "Existing native settlement owners, complete causes and historical commerce.");

    private sealed record EventRightsTenantContext(Guid TenantId) : Explore.Application.Contracts.Infrastructure.ITenantContext;

    private sealed class EventRightsFlushObserver(DeploymentClock clock) : SaveChangesInterceptor
    {
        public DateTimeOffset? SourceDeadline { get; set; }
        public DateTimeOffset? AdoptionDeadline { get; set; }
        public Guid? RevokeSourceOwner { get; set; }
        public Guid? RevokeAdopter { get; set; }
        public Guid? RevokeWithdrawer { get; set; }
        public EventRightsChange? EpochDrift { get; set; }
        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData,
            int result, CancellationToken cancellationToken = default)
        {
            var context = eventData.Context!;
            if (context.ChangeTracker.Entries<ModerationEventRightsSource>().Any())
            {
                if (SourceDeadline is { } deadline) clock.UtcNow = deadline;
                if (RevokeSourceOwner is { } human)
                    await context.Set<PlatformUserRole>().Where(row => row.UserId == human).ExecuteDeleteAsync(cancellationToken);
            }
            if (context.ChangeTracker.Entries<ModerationEventRightsAdoption>().Any(entry => entry.Entity.Revision == 1))
            {
                if (AdoptionDeadline is { } deadline) clock.UtcNow = deadline;
                if (RevokeAdopter is { } human)
                    await context.Set<PlatformUserRole>().Where(row => row.UserId == human).ExecuteDeleteAsync(cancellationToken);
            }
            if (RevokeWithdrawer is { } withdrawer
                && context.ChangeTracker.Entries<ModerationEventRightsAdoption>().Any(entry => entry.Entity.Revision == 2))
                await context.Set<PlatformUserRole>().Where(row => row.UserId == withdrawer).ExecuteDeleteAsync(cancellationToken);
            if (EpochDrift == EventRightsChange.SourceEpochDriftAfterSave && context.ChangeTracker.Entries<ModerationEventRightsSource>().Any()
                || EpochDrift == EventRightsChange.AdoptionEpochDriftAfterSave
                    && context.ChangeTracker.Entries<ModerationEventRightsAdoption>().Any(entry => entry.Entity.Revision == 1)
                || EpochDrift == EventRightsChange.WithdrawalEpochDriftAfterSave
                    && context.ChangeTracker.Entries<ModerationEventRightsAdoption>().Any(entry => entry.Entity.Revision == 2))
                await context.Set<ModerationQualificationEpoch>().ExecuteUpdateAsync(
                    setters => setters.SetProperty(row => row.Revision, row => row.Revision + 1), cancellationToken);
            return result;
        }
    }
}
