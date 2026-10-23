using Explore.Application.Contracts.Moderation;
using Explore.Application.Features.Moderation.Handlers.Commands;
using Explore.Application.Features.Moderation.Requests.Commands;
using ISLAMU.Moderation.Domain;

namespace Event.Application.UnitTests.Moderation;

public sealed class ModerationParticipantInvestigationContractTests
{
    [Test]
    public async Task TypedInvestigationAcceptsOpaqueMaterialAndSessionGenerationsWithoutGrantingPermission()
    {
        var request = Request();
        await Assert.That(request.EvidenceSourceVersion.Version).IsEqualTo(4);
        await Assert.That(request.ExpectedSessionGeneration.Version).IsEqualTo(4);
        await Assert.That((await new ModerationQualificationValidator().ValidateAsync(
            new ApplyModerationQualificationCommand(request))).IsValid).IsTrue();
        await Assert.That(request.ToString()).DoesNotContain(request.Grounds.IdentificationGrounds);
    }

    [Test]
    [Arguments("identity")]
    [Arguments("material-generation")]
    [Arguments("session-generation")]
    [Arguments("evidence-revision")]
    [Arguments("scope")]
    [Arguments("epoch")]
    [Arguments("window")]
    [Arguments("utc")]
    [Arguments("infinite")]
    [Arguments("precision")]
    [Arguments("retention")]
    [Arguments("grounds")]
    public async Task TypedInvestigationRejectsIncompleteOrUnboundedIntentBeforeNativeWriter(string defect)
    {
        var request = Request();
        request = defect switch
        {
            "identity" => request with { SourceId = Guid.NewGuid() },
            "material-generation" => request with { EvidenceSourceVersion = Guid.Empty },
            "session-generation" => request with { ExpectedSessionGeneration = Guid.Empty },
            "evidence-revision" => request with { EvidenceCaseRevision = request.ExpectedCaseRevision + 1 },
            "scope" => request with { Target = request.Target.Parent! },
            "epoch" => request with { ExpectedQualificationRevision = long.MaxValue - 1 },
            "window" => request with { IncidentWindowUntil = request.IncidentWindowFrom },
            "utc" => request with { ValidUntil = request.ValidUntil.ToOffset(TimeSpan.FromHours(1)) },
            "infinite" => request with { PurgeAt = DateTimeOffset.MaxValue },
            "precision" => request with { IncidentWindowFrom = request.IncidentWindowFrom.AddTicks(1) },
            "retention" => request with { ValidUntil = request.PurgeAt.AddMicroseconds(1) },
            "grounds" => request with { Grounds = request.Grounds with { EvidenceProvenance = " " } },
            _ => throw new ArgumentOutOfRangeException(nameof(defect))
        };
        await Assert.That((await new ModerationQualificationValidator().ValidateAsync(
            new ApplyModerationQualificationCommand(request))).IsValid).IsFalse();
    }

    private static RecordModerationParticipantIncidentResponsibilitySource Request()
    {
        var scope = AuthorityScope.Tenant(AuthorityScope.Instance(AuthorityScope.Solution(
            AuthorityScope.TrustDomain(Guid.CreateVersion7()), Guid.CreateVersion7()), Guid.CreateVersion7()),
            Guid.CreateVersion7());
        var at = new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);
        return new()
        {
            OperationId = Guid.CreateVersion7(), Target = scope, ExpectedQualificationRevision = 5,
            SourceId = Guid.CreateVersion7(), OccurrenceCaseId = Guid.CreateVersion7(),
            ExpectedCaseRevision = 2, EvidenceCaseRevision = 1, ReadGrantId = Guid.CreateVersion7(),
            EvidenceSourceId = Guid.CreateVersion7(), EvidenceSourceVersion = Guid.NewGuid(),
            SubjectHumanId = Guid.CreateVersion7(), SessionId = Guid.CreateVersion7(),
            ExpectedSessionGeneration = Guid.NewGuid(), IncidentWindowFrom = at.AddMinutes(-10),
            IncidentWindowUntil = at.AddMinutes(-5), ValidUntil = at.AddMinutes(1), PurgeAt = at.AddMinutes(2),
            Grounds = new("Authenticated identification grounds.", "Specific alleged conduct.",
                "Authenticated evidence provenance.", "Contradiction and credential-use assessment.")
        };
    }
}
