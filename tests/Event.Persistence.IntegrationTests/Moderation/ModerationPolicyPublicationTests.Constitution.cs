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
    public async Task Constitution_RequiresSubstanceIndependentAdoptionAndCurrentOriginalAuthority(int change)
    {
        await using var database = await Database.CreateAsync();
        var clock = new DeploymentClock();
        var flush = new ConstitutionFlushObserver(clock);
        await using var context = database.Context(flush);
        var request = database.Request();
        request = request with { Proposed = request.Proposed with { RequestedState = ModerationPolicyRequestedState.Active } };
        var (admin, machine) = DeploymentIdentity(context, database.AdminId);
        var publications = new ModerationPolicyPublicationRepository(context, admin, machine,
            database.Inventory, null, clock);
        var prepared = await publications.PrepareAsync(request, Now.AddMinutes(10));
        await Assert.That(prepared.Outcome).IsEqualTo(ModerationPolicyPublicationOutcome.Known);
        var policy = ModerationPolicyPublicationProjection.RestoreCandidate(prepared.Candidate!, database.Inventory)!;
        var ids = new[] { Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7() };
        var role = await context.Roles.SingleAsync(row =>
            row.RoleScopeId == (int)RoleScopeEnum.Platform && row.MasterCode == "platform.admin");
        foreach (var id in ids)
        {
            var user = new User
            {
                Id = id,
                Pii = new UserPii
                {
                    UserId = id,
                    Email = $"{id:N}@example.invalid",
                    FirstName = "Governance",
                    LastName = "Reviewer"
                }
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
        var availability = new RecordModerationConstitutionAvailability
        {
            OperationId = Guid.CreateVersion7(),
            Target = database.Scope,
            ExpectedQualificationRevision = 2,
            AvailabilityId = Guid.CreateVersion7(),
            MaximumCaseload = 1,
            ValidUntil = Now.AddMinutes(8)
        };
        await Assert.That((await Writer(ids[0]).ExecuteAsync(availability)).Outcome)
            .IsEqualTo(ModerationQualificationWriteOutcome.Known);
        var terms = new ModerationConstitutionTerms(ModerationScopeKind.Tenant,
            ModerationConstitutionEvidence.QualifiedFindingsAndResponse,
            ModerationConstitutionRemedies.RestrictedAuthenticationAndProofBoundRecovery,
            ModerationConstitutionIndependence.DistinctControllersAndImplicatedPartyExclusion,
            ModerationConstitutionExposure.ConcealedPurposeAuthorizedReveal,
            ModerationConstitutionOverload.AcknowledgeAndEscalate, 1, 6,
            TimeSpan.FromDays(30), TimeSpan.FromHours(1),
            "https://example.invalid/support", "https://example.invalid/redress", ids[1], ids[2]);
        var record = new RecordModerationConstitutionSource
        {
            OperationId = Guid.CreateVersion7(),
            Target = database.Scope,
            ExpectedQualificationRevision = 3,
            SourceId = Guid.CreateVersion7(),
            PolicyId = request.Proposed.PolicyId,
            ExpectedPolicyRevision = 1,
            PublicationId = request.Proposed.PublicationId,
            ProfileId = request.Proposed.Profiles.Single().Id,
            Mode = ModerationNativeMode.Embedded,
            Terms = terms,
            ValidUntil = Now.AddMinutes(5),
            Reviewers = [new(availability.AvailabilityId, 1, 3, ids[0], ids[0],
                "Native canonical identity reviewed without delegated aliases.",
                "Qualified to assess evidence and protected remedies.",
                "Independent from implicated controllers; recuse on case conflict.")]
        };
        var writer = Writer(database.AdminId);
        if (change == 13)
        {
            flush.SourceDeadline = record.ValidUntil;
            await Assert.That((await writer.ExecuteAsync(record)).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Unavailable);
            context.ModerationTrustDomainId = database.Scope.TrustDomainId;
            await Assert.That(await context.Set<ModerationConstitutionSource>().CountAsync()).IsEqualTo(0);
            await Assert.That(await context.ModerationQualificationMutationReceipts.AnyAsync(row => row.Id == record.OperationId)).IsFalse();
            await Assert.That((await context.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(3);
            return;
        }
        if (change == 22)
        {
            var shortened = record with { Terms = terms with { ProtectedComplaintCalendarMonths = 5 } };
            await Assert.That((await writer.ExecuteAsync(shortened)).Outcome)
                .IsEqualTo(ModerationQualificationWriteOutcome.Denied);
            context.ModerationTrustDomainId = database.Scope.TrustDomainId;
            await Assert.That(await context.Set<ModerationConstitutionSource>().CountAsync()).IsEqualTo(0);
            await Assert.That(await context.ModerationQualificationMutationReceipts.AnyAsync(row => row.Id == record.OperationId)).IsFalse();
            await Assert.That((await context.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(3);
            return;
        }
        if (change is 18 or 19)
        {
            var invalid = change == 18 ? record with { Reviewers = [] }
                : record with { Terms = terms with { MinimumIndependentReviewers = 2 } };
            await Assert.That((await writer.ExecuteAsync(invalid)).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Unavailable);
            context.ModerationTrustDomainId = database.Scope.TrustDomainId;
            await Assert.That(await context.Set<ModerationConstitutionSource>().CountAsync()).IsEqualTo(0);
            await Assert.That((await context.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(3);
            return;
        }
        await Assert.That((await writer.ExecuteAsync(record)).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
        await Assert.That((await writer.ExecuteAsync(record)).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
        var adopt = new AdoptModerationConstitutionSource
        {
            OperationId = Guid.CreateVersion7(),
            Target = database.Scope,
            ExpectedQualificationRevision = 4,
            SourceId = record.SourceId,
            ExpectedSourceRevision = 1,
            ExpectedSourceWriterGeneration = 4,
            AdoptionId = Guid.CreateVersion7(),
            ValidUntil = Now.AddMinutes(4)
        };
        await Assert.That((await writer.ExecuteAsync(adopt)).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Denied);
        if (change == 14)
        {
            flush.AdoptionDeadline = adopt.ValidUntil;
            await Assert.That((await Writer(ids[1]).ExecuteAsync(adopt)).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Unavailable);
            context.ModerationTrustDomainId = database.Scope.TrustDomainId;
            await Assert.That(await context.Set<ModerationConstitutionAdoption>().CountAsync()).IsEqualTo(0);
            await Assert.That(await context.ModerationQualificationMutationReceipts.AnyAsync(row => row.Id == adopt.OperationId)).IsFalse();
            await Assert.That((await context.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(4);
            return;
        }
        await Assert.That((await Writer(ids[1]).ExecuteAsync(adopt)).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
        await Assert.That((await Writer(ids[1]).ExecuteAsync(adopt)).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
        await Assert.That((await writer.ExecuteAsync(record with { Terms = terms with { MinimumIndependentReviewers = 2 } })).Outcome)
            .IsEqualTo(ModerationQualificationWriteOutcome.Conflict);
        context.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await Assert.That(await context.ModerationPolicySelections.CountAsync()).IsEqualTo(0);
        await Assert.That(await context.ModerationConsumerMeasurementReceipts.CountAsync()).IsEqualTo(0);
        if (change is 1 or 2 or 3 or 4 or 15 or 16)
        {
            if (change == 1) context.ModerationQualificationMutationReceipts.Remove(
                await context.ModerationQualificationMutationReceipts.SingleAsync(row => row.Id == record.OperationId));
            if (change == 2) context.ModerationQualificationMutationReceipts.Remove(
                await context.ModerationQualificationMutationReceipts.SingleAsync(row => row.Id == availability.OperationId));
            if (change == 3) context.PlatformUserRoles.Remove(await context.PlatformUserRoles.SingleAsync(row => row.UserId == ids[0]));
            if (change == 4) context.PlatformUserRoles.Remove(await context.PlatformUserRoles.SingleAsync(row => row.UserId == ids[1]));
            if (change == 15) context.ModerationQualificationMutationReceipts.Remove(
                await context.ModerationQualificationMutationReceipts.SingleAsync(row => row.Id == adopt.OperationId));
            if (change == 16) context.PlatformUserRoles.Remove(await context.PlatformUserRoles.SingleAsync(row => row.UserId == ids[2]));
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
        }
        if (change == 5) clock.UtcNow = Now.AddMinutes(4);
        if (change == 6)
            await Assert.That((await writer.ExecuteAsync(record with
            { OperationId = Guid.CreateVersion7(), SourceId = Guid.CreateVersion7(), ExpectedQualificationRevision = 5 })).Outcome)
                .IsEqualTo(ModerationQualificationWriteOutcome.Known);
        if (change is 7 or 21)
        {
            clock.UtcNow = Now.AddMinutes(20);
            var withdrawal = new WithdrawModerationConstitutionAdoption
            {
                OperationId = Guid.CreateVersion7(),
                Target = database.Scope,
                ExpectedQualificationRevision = 5,
                AdoptionId = adopt.AdoptionId,
                ExpectedAdoptionRevision = 1
            };
            if (change == 21)
            {
                flush.RevokeWithdrawingActor = database.AdminId;
                await Assert.That((await writer.ExecuteAsync(withdrawal)).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Unavailable);
                context.ModerationTrustDomainId = database.Scope.TrustDomainId;
                await Assert.That((await context.Set<ModerationConstitutionAdoption>().AsNoTracking().SingleAsync()).Revision).IsEqualTo(1);
                await Assert.That(await context.ModerationQualificationMutationReceipts.AnyAsync(row => row.Id == withdrawal.OperationId)).IsFalse();
                await Assert.That((await context.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(5);
                await Assert.That(await context.PlatformUserRoles.AnyAsync(row => row.UserId == database.AdminId)).IsTrue();
                return;
            }
            await Assert.That((await writer.ExecuteAsync(withdrawal)).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
            await Assert.That((await writer.ExecuteAsync(withdrawal)).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
        }
        if (change == 8)
            await Assert.That((await Writer(ids[0]).ExecuteAsync(new WithdrawModerationConstitutionAvailability
            {
                OperationId = Guid.CreateVersion7(),
                Target = database.Scope,
                ExpectedQualificationRevision = 5,
                AvailabilityId = availability.AvailabilityId,
                ExpectedAvailabilityRevision = 1
            })).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
        if (change == 20)
        {
            var caseId = Guid.CreateVersion7();
            await Assert.That((await writer.ExecuteAsync(new OpenModerationCaseSource
            {
                OperationId = Guid.CreateVersion7(),
                Target = database.Scope,
                ExpectedQualificationRevision = 5,
                CaseId = caseId,
                SubjectKind = ModerationSubjectKind.Human,
                SubjectId = database.AdminId,
                OccurrenceId = Guid.CreateVersion7()
            })).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
            await Assert.That((await writer.ExecuteAsync(new AssignModerationReviewerSource
            {
                OperationId = Guid.CreateVersion7(),
                Target = database.Scope,
                ExpectedQualificationRevision = 6,
                CaseId = caseId,
                ExpectedCaseRevision = 1,
                AssignmentId = Guid.CreateVersion7(),
                ReviewerHumanId = ids[0]
            })).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
        }
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var authority = await new ModerationQualificationAuthority(context, admin, machine)
            .AcquireAsync(database.Scope, request.Proposed.PublicationId, default);
        await Assert.That(authority).IsNotNull();
        using var evidence = publications.BorrowConsumerEvidence(authority!, policy);
        if (change == 9) evidence.Dispose();
        if (change == 10) await transaction.RollbackAsync();
        await using var foreign = database.Context();
        var ownerContext = change == 11 ? foreign : context;
        var definitions = new ModerationPolicyDefinitionSourceOwner(ownerContext, admin, machine, database.Inventory, clock);
        var owner = new ModerationConstitutionSourceOwner(ownerContext, definitions, clock);
        var observation = await owner.AcquirePrerequisiteAsync(evidence,
            change == 12 ? Guid.CreateVersion7() : transaction.TransactionId,
            change == 17 ? policy.Consumers[0].Binding with { Mode = ModerationNativeMode.RemoteStrict } : policy.Consumers[0].Binding,
            clock.UtcNow, default);
        if (change == 0)
        {
            await Assert.That(observation!.SourceId).IsEqualTo(record.SourceId);
            await Assert.That(observation.ApprovalId).IsEqualTo(adopt.AdoptionId);
            await Assert.That(observation.ValidUntil).IsEqualTo(adopt.ValidUntil);
        }
        else await Assert.That(observation).IsNull();
    }

    private sealed class ConstitutionFlushObserver(DeploymentClock clock) : SaveChangesInterceptor
    {
        public DateTimeOffset? SourceDeadline { get; set; }
        public DateTimeOffset? AdoptionDeadline { get; set; }
        public Guid? RevokeWithdrawingActor { get; set; }
        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData,
            int result, CancellationToken cancellationToken = default)
        {
            if (SourceDeadline is { } source && eventData.Context!.ChangeTracker.Entries<ModerationConstitutionSource>().Any())
                clock.UtcNow = source;
            if (AdoptionDeadline is { } adoption && eventData.Context!.ChangeTracker.Entries<ModerationConstitutionAdoption>().Any())
                clock.UtcNow = adoption;
            if (RevokeWithdrawingActor is { } actor && eventData.Context!.ChangeTracker
                .Entries<ModerationConstitutionAdoption>().Any(entry => entry.Entity.Revision == 2))
                await eventData.Context.Set<PlatformUserRole>().Where(row => row.UserId == actor).ExecuteDeleteAsync(cancellationToken);
            return result;
        }
    }
}
