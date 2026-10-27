using Explore.Application.Contracts.Persistence;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationResourceQuarantineReleaseTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ResourceCaseWriterCannotAttributeTheObjectToAHumanOrForeignTenant(bool foreignTenant)
    {
        await using var fixture = await Fixture.CreateAsync(prepare: false);
        Guid eventId;
        await using (var reader = fixture.Context())
            eventId = await reader.EventResources.Where(row => row.Id == fixture.ResourceId)
                .Select(row => row.EventId).SingleAsync();
        var caseId = Guid.CreateVersion7();
        var target = foreignTenant
            ? AuthorityScope.Resource(AuthorityScope.Tenant(fixture.Database.Instance, Guid.CreateVersion7()), fixture.ResourceId)
            : fixture.Scope;
        var request = new OpenModerationCaseSource
        {
            OperationId = Guid.CreateVersion7(),
            Target = target,
            ExpectedQualificationRevision = 1,
            CaseId = caseId,
            OccurrenceId = Guid.CreateVersion7(),
            SubjectKind = foreignTenant ? ModerationSubjectKind.Content : ModerationSubjectKind.Human,
            SubjectId = foreignTenant ? eventId : fixture.Database.SubjectId
        };
        var result = await fixture.WriteSourceAsync(fixture.Database.AdministratorId, request);
        await Assert.That(result.Outcome).IsNotEqualTo(ModerationQualificationWriteOutcome.Known);
        await using var verify = fixture.Context();
        verify.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        await Assert.That(await verify.ModerationCases.AnyAsync(row => row.Id == caseId)).IsFalse();
        await Assert.That(await verify.ModerationQualificationMutationReceipts
            .AnyAsync(row => row.Id == request.OperationId)).IsFalse();
    }

    [Test]
    public async Task ResourceCaseRouteDoesNotBroadenUnrelatedUrgencyAuthority()
    {
        await using var fixture = await Fixture.CreateAsync(prepare: false);
        var request = new QualifyModerationCaseUrgency
        {
            OperationId = Guid.CreateVersion7(),
            Target = fixture.Scope,
            ExpectedQualificationRevision = 1,
            CaseId = fixture.CaseId,
            ExpectedCaseRevision = 1,
            EvidenceId = Guid.CreateVersion7(),
            Justification = "An urgency request is not a fresh safety-review prerequisite.",
            ValidUntil = fixture.Clock.Now.AddMinutes(1)
        };
        var result = await fixture.WriteSourceAsync(fixture.Database.AdministratorId, request);
        await Assert.That(result.Outcome).IsNotEqualTo(ModerationQualificationWriteOutcome.Known);
        await using var verify = fixture.Context();
        verify.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        await Assert.That(await verify.ModerationQualificationEvidence.AnyAsync(row => row.Id == request.EvidenceId)).IsFalse();
        await Assert.That(await verify.ModerationQualificationMutationReceipts
            .AnyAsync(row => row.Id == request.OperationId)).IsFalse();
    }
}
