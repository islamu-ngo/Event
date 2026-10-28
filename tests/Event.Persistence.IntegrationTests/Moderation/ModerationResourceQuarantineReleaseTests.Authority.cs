using Explore.Application.Authentication;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Repositories;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationResourceQuarantineReleaseTests
{
    [Test]
    public async Task SeparateAccountsWithOneQualifiedControllerCannotAdoptEachOthersPurpose()
    {
        await using var fixture = await Fixture.CreateAsync(prepare: false);
        await fixture.SourceAsync(fixture.Database.AdministratorId, new QualifyModerationReviewerController
        {
            OperationId = Guid.CreateVersion7(),
            Target = fixture.Scope,
            ExpectedQualificationRevision = 1,
            AssignmentId = fixture.Assignments[fixture.Approver],
            ExpectedAssignmentRevision = 3,
            ControllerHumanId = fixture.Author,
            EvidenceId = Guid.CreateVersion7(),
            Justification = "Native evidence identifies one controller behind both reviewer accounts.",
            QualifiedUntil = fixture.Clock.Now.AddMinutes(30)
        });
        await fixture.SourceAsync(fixture.Database.AdministratorId, new AssessModerationReviewerConflict
        {
            OperationId = Guid.CreateVersion7(),
            Target = fixture.Scope,
            ExpectedQualificationRevision = 1,
            AssignmentId = fixture.Assignments[fixture.Approver],
            ExpectedAssignmentRevision = 4,
            State = ModerationReviewerConflictState.Independent,
            EvidenceId = Guid.CreateVersion7(),
            Justification = "Case conflict assessment does not make these two controllers independent."
        });
        var purpose = fixture.Intent(fixture.Author, ResourceSafetyReviewStep.ProposePurpose, null) with
        {
            RequiredApprovals = 1,
            MaximumEvidenceLifetime = TimeSpan.FromMinutes(10)
        };
        await fixture.AcceptAsync(fixture.Author, purpose);
        var adopt = fixture.Intent(fixture.Approver, ResourceSafetyReviewStep.AdoptPurpose, purpose.OperationId) with
        {
            ExpectedReviewerRevision = 5
        };
        await Assert.That((await fixture.ReviewAsync(fixture.Approver, adopt)).Outcome)
            .IsEqualTo(ModerationResourceSafetyOutcome.Unavailable);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task FreshInspectionRequiresAdoptedPurposeAndItsOwnEvidenceLifetime(bool adoptPurpose)
    {
        await using var fixture = await Fixture.CreateAsync(prepare: false);
        var purpose = fixture.Intent(fixture.Author, ResourceSafetyReviewStep.ProposePurpose, null) with
        {
            RequiredApprovals = 1,
            MaximumEvidenceLifetime = TimeSpan.FromMinutes(1)
        };
        await fixture.AcceptAsync(fixture.Author, purpose);
        var predecessor = purpose.OperationId;
        if (adoptPurpose)
        {
            var adoption = fixture.Intent(fixture.Approver, ResourceSafetyReviewStep.AdoptPurpose, predecessor);
            await fixture.AcceptAsync(fixture.Approver, adoption);
            predecessor = adoption.OperationId;
        }
        var evidence = fixture.Intent(fixture.Author, ResourceSafetyReviewStep.AttestMaterialSafety, predecessor) with
        {
            Attestation = ResourceSafetyAttestation.IndependentlyInspectedSafeForCurrentAudience
        };
        await Assert.That((await fixture.ReviewAsync(fixture.Author, evidence)).Outcome)
            .IsEqualTo(ModerationResourceSafetyOutcome.Unavailable);
        await using var reader = fixture.Context();
        await Assert.That(await reader.Set<EventResourceModerationQuarantineReview>()
            .AnyAsync(row => row.Step == ResourceSafetyReviewStep.AttestMaterialSafety)).IsFalse();
    }

    [Test]
    public async Task ClaimedAdministratorWithoutCurrentNativeRoleCannotRelease()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using (var writer = fixture.Context())
            await writer.PlatformUserRoles.Where(row => row.UserId == fixture.Approver).ExecuteDeleteAsync();
        await Assert.That((await fixture.ReleaseAsync(fixture.ReleaseIntent())).Outcome)
            .IsEqualTo(ModerationResourceSafetyOutcome.Unavailable);
        await RequireUnreleasedAsync(fixture);
    }

    [Test]
    public async Task NativeUrgencyEvidenceCannotBeRelabeledAsMaterialSafetyApproval()
    {
        await using var fixture = await Fixture.CreateAsync();
        var urgencyCaseId = Guid.CreateVersion7();
        Guid eventId;
        await using (var reader = fixture.Context())
            eventId = await reader.EventResources.Where(row => row.Id == fixture.ResourceId)
                .Select(row => row.EventId).SingleAsync();
        await fixture.SourceAsync(fixture.Database.AdministratorId, new OpenModerationCaseSource
        {
            OperationId = Guid.CreateVersion7(),
            Target = fixture.Database.Scope,
            ExpectedQualificationRevision = 1,
            CaseId = urgencyCaseId,
            SubjectKind = ModerationSubjectKind.Content,
            SubjectId = eventId,
            OccurrenceId = Guid.CreateVersion7()
        });
        var policyId = Guid.CreateVersion7();
        await fixture.SourceAsync(fixture.Database.AdministratorId, new PublishModerationQualificationPolicySource
        {
            OperationId = Guid.CreateVersion7(),
            Target = fixture.Database.Scope,
            ExpectedQualificationRevision = 1,
            SourceId = policyId,
            CaseId = urgencyCaseId,
            ExpectedCaseRevision = 1,
            ExpectedSourceRevision = 0,
            MaximumReviewerLifetime = TimeSpan.FromMinutes(10),
            MaximumUrgencyLifetime = TimeSpan.FromMinutes(1),
            ValidUntil = fixture.Clock.Now.AddMinutes(10)
        });
        long writerGeneration;
        await using (var reader = fixture.Context())
        {
            reader.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
            writerGeneration = await reader.ModerationQualificationPolicySources.Where(row => row.Id == policyId)
                .Select(row => row.WriterGeneration).SingleAsync();
        }
        await fixture.SourceAsync(fixture.Database.UrgencyAssessorId, new AdoptModerationQualificationPolicySource
        {
            OperationId = Guid.CreateVersion7(),
            Target = fixture.Database.Scope,
            ExpectedQualificationRevision = 1,
            SourceId = policyId,
            ExpectedSourceRevision = 1,
            ExpectedWriterGeneration = writerGeneration,
            AdoptionId = Guid.CreateVersion7(),
            CaseId = urgencyCaseId,
            ExpectedCaseRevision = 1,
            ValidUntil = fixture.Clock.Now.AddMinutes(5)
        });
        var evidenceId = Guid.CreateVersion7();
        await fixture.SourceAsync(fixture.Database.AdministratorId, new QualifyModerationCaseUrgency
        {
            OperationId = Guid.CreateVersion7(),
            Target = fixture.Database.Scope,
            ExpectedQualificationRevision = 1,
            CaseId = urgencyCaseId,
            ExpectedCaseRevision = 1,
            EvidenceId = evidenceId,
            Justification = "Current urgency is a separate purpose, not a material-safety finding.",
            ValidUntil = fixture.Clock.Now.AddMinutes(1)
        });
        var borrowed = fixture.Intent(fixture.Approver, ResourceSafetyReviewStep.ApproveSafety, evidenceId);
        await Assert.That((await fixture.ReviewAsync(fixture.Approver, borrowed)).Outcome)
            .IsEqualTo(ModerationResourceSafetyOutcome.Unavailable);
        await RequireUnreleasedAsync(fixture);
    }

    [Test]
    public async Task MachineOwnershipOfAReviewerAccountIsNotFreshHumanAuthority()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var context = fixture.Context();
        var identity = Identity(context, fixture.Approver);
        identity.Machine.SetPrincipal(new ApiKeyPrincipalContext(Guid.CreateVersion7().ToString(),
            fixture.Database.TenantId, ExternalApiKeyOwnerType.User, fixture.Approver, []));
        var result = await new ModerationResourceQuarantineReleaseRepository(context, identity.Admin, identity.Machine, fixture.Clock)
            .ReleaseAsync(fixture.ReleaseIntent());
        await Assert.That(result.Outcome).IsEqualTo(ModerationResourceSafetyOutcome.Unavailable);
        await RequireUnreleasedAsync(fixture);
    }

    [Test]
    public async Task AnotherTenantCannotReadOrSpendTheOpaqueApproval()
    {
        await using var fixture = await Fixture.CreateAsync();
        var intent = fixture.ReleaseIntent();
        await Assert.That((await fixture.ReleaseAsync(intent)).Outcome).IsEqualTo(ModerationResourceSafetyOutcome.Known);
        await using var context = fixture.Context();
        context.TenantContext = new OtherTenant(Guid.CreateVersion7());
        var identity = Identity(context, fixture.Approver);
        var result = await new ModerationResourceQuarantineReleaseRepository(context, identity.Admin, identity.Machine, fixture.Clock)
            .ReleaseAsync(intent);
        await Assert.That(result.Outcome).IsEqualTo(ModerationResourceSafetyOutcome.Unavailable);
        await Assert.That(result.Receipt).IsNull();
    }

    [Test]
    public async Task ANewOperationCannotRetireAnAlreadyReleasedCause()
    {
        await using var fixture = await Fixture.CreateAsync();
        var intent = fixture.ReleaseIntent();
        await Assert.That((await fixture.ReleaseAsync(intent)).Outcome).IsEqualTo(ModerationResourceSafetyOutcome.Known);
        var another = await fixture.ReleaseAsync(intent with { OperationId = Guid.CreateVersion7() });
        await Assert.That(another.Outcome).IsNotEqualTo(ModerationResourceSafetyOutcome.Known);
        await using var reader = fixture.Context();
        await Assert.That(await reader.Set<EventResourceModerationQuarantineRelease>().CountAsync()).IsEqualTo(1);
        await Assert.That((await reader.EventResources.SingleAsync(row => row.Id == fixture.ResourceId)).ModerationQuarantineCount)
            .IsEqualTo(1);
    }

    private sealed record OtherTenant(Guid TenantId) : ITenantContext;
}
