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

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationPolicyPublicationTests
{
    [Test]
    [Arguments("current")]
    [Arguments("withdrawn")]
    [Arguments("receipt-missing")]
    [Arguments("superseded")]
    [Arguments("reviewer-role")]
    [Arguments("acceptance-missing")]
    public async Task Measured_staffing_requalifies_actual_finite_native_availability_and_independent_constitution(string change)
    {
        await using var database = await Database.CreateAsync();
        var clock = new DeploymentClock();
        await using var context = database.Context();
        var request = database.Request();
        request = request with { Proposed = request.Proposed with { RequestedState = ModerationPolicyRequestedState.Active } };
        var (admin, machine) = DeploymentIdentity(context, database.AdminId);
        var publications = new ModerationPolicyPublicationRepository(context, admin, machine, database.Inventory, null, clock);
        var prepared = await publications.PrepareAsync(request, Now.AddMinutes(10));
        await Assert.That(prepared.Outcome).IsEqualTo(ModerationPolicyPublicationOutcome.Known);
        var policy = ModerationPolicyPublicationProjection.RestoreCandidate(prepared.Candidate!, database.Inventory)!;
        var ids = new[] { Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7() };
        var role = await context.Roles.SingleAsync(row => row.RoleScopeId == (int)RoleScopeEnum.Platform && row.MasterCode == "platform.admin");
        foreach (var id in ids)
        {
            var user = new User
            {
                Id = id,
                Pii = new UserPii
                { UserId = id, Email = $"{id:N}@example.invalid", FirstName = "Actual", LastName = "Staffing" }
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
        await Assert.That((await Writer(ids[0]).ExecuteAsync(availability)).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
        var constitution = new RecordModerationConstitutionSource
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
            ValidUntil = Now.AddMinutes(5),
            Terms = new(ModerationScopeKind.Tenant, ModerationConstitutionEvidence.QualifiedFindingsAndResponse,
                ModerationConstitutionRemedies.RestrictedAuthenticationAndProofBoundRecovery,
                ModerationConstitutionIndependence.DistinctControllersAndImplicatedPartyExclusion,
                ModerationConstitutionExposure.ConcealedPurposeAuthorizedReveal, ModerationConstitutionOverload.AcknowledgeAndEscalate,
                1, 6, TimeSpan.FromDays(30), TimeSpan.FromHours(1),
                "https://example.invalid/support", "https://example.invalid/redress", ids[1], ids[2]),
            Reviewers = [new(availability.AvailabilityId, 1, 3, ids[0], ids[0],
                "Independent canonical controller.", "Qualified evidence reviewer.", "Conflicts require recusal.")]
        };
        await Assert.That((await Writer(database.AdminId).ExecuteAsync(constitution)).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
        var adopted = new AdoptModerationConstitutionSource
        {
            OperationId = Guid.CreateVersion7(),
            Target = database.Scope,
            ExpectedQualificationRevision = 4,
            SourceId = constitution.SourceId,
            ExpectedSourceRevision = 1,
            ExpectedSourceWriterGeneration = 4,
            AdoptionId = Guid.CreateVersion7(),
            ValidUntil = Now.AddMinutes(4)
        };
        await Assert.That((await Writer(ids[1]).ExecuteAsync(adopted)).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
        var record = new RecordModerationMeasuredOperationsSource
        {
            OperationId = Guid.CreateVersion7(),
            Target = database.Scope,
            ExpectedQualificationRevision = 5,
            SourceId = Guid.CreateVersion7(),
            ProfileId = constitution.ProfileId,
            Binding = policy.Consumers[0].Binding,
            ReviewerHumanId = ids[1],
            ReviewerControllerHumanId = ids[1],
            RecorderControllerHumanId = database.AdminId,
            ValidUntil = Now.AddMinutes(3)
        };
        await Assert.That((await Writer(database.AdminId).ExecuteAsync(record)).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
        context.ModerationTrustDomainId = database.Scope.TrustDomainId;
        var stored = await context.Set<ModerationMeasuredOperationsSource>().AsNoTracking().SingleAsync();
        await Assert.That(stored.Evidence.Staffing!.Id).IsEqualTo(adopted.AdoptionId);
        if (change == "withdrawn")
            await Assert.That((await Writer(ids[0]).ExecuteAsync(new WithdrawModerationConstitutionAvailability
            {
                OperationId = Guid.CreateVersion7(),
                Target = database.Scope,
                ExpectedQualificationRevision = 6,
                AvailabilityId = availability.AvailabilityId,
                ExpectedAvailabilityRevision = 1
            })).Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
        if (change == "superseded")
            await Assert.That((await Writer(ids[0]).ExecuteAsync(availability with
            { OperationId = Guid.CreateVersion7(), AvailabilityId = Guid.CreateVersion7(), ExpectedQualificationRevision = 6 })).Outcome)
                .IsEqualTo(ModerationQualificationWriteOutcome.Known);
        context.ModerationTrustDomainId = database.Scope.TrustDomainId;
        if (change is "receipt-missing" or "acceptance-missing")
        {
            var receipt = change == "receipt-missing" ? availability.OperationId : adopted.OperationId;
            await context.ModerationQualificationMutationReceipts.Where(row => row.Id == receipt).ExecuteDeleteAsync();
        }
        if (change == "reviewer-role") await context.PlatformUserRoles.Where(row => row.UserId == ids[0]).ExecuteDeleteAsync();
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var authority = await new ModerationQualificationAuthority(context, admin, machine)
            .AcquireAsync(database.Scope, request.Proposed.PublicationId, default);
        using var session = publications.BorrowConsumerEvidence(authority!, policy);
        var owner = new ModerationMeasuredOperationsSourceOwner(context,
            new ModerationPolicyDefinitionSourceOwner(context, admin, machine, database.Inventory, clock), database.Inventory, clock);
        var current = await owner.AcquireSourceAsync(session, transaction.TransactionId, record.ProfileId, record.Binding, Now, default);
        await Assert.That(current is not null).IsEqualTo(change == "current");
        await Assert.That(await owner.AcquirePrerequisiteAsync(session, transaction.TransactionId, record.Binding, Now, default)).IsNull();
    }
}
