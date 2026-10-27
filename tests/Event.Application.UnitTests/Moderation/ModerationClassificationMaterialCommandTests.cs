using Explore.Application.Features.Moderation.Handlers.Commands;
using Explore.Application.Features.Moderation.Requests.Commands;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using ISLAMU.Moderation.Domain;

namespace Event.Application.UnitTests.Moderation;

public sealed class ModerationClassificationMaterialCommandTests
{
    [Test]
    public async Task CompleteInventoryIsValidIntentNotPermissionOrReviewerAcceptance()
    {
        await Assert.That((await new ModerationClassificationMaterialCommandValidator<RecordModerationClassificationMaterialCommand>()
            .ValidateAsync(Record())).IsValid).IsTrue();
    }

    [Test]
    [Arguments("instance-target")]
    [Arguments("source-id")]
    [Arguments("negative-head")]
    [Arguments("future-head")]
    [Arguments("default-materials")]
    [Arguments("empty-components")]
    [Arguments("duplicate-material")]
    [Arguments("duplicate-review-id")]
    [Arguments("duplicate-component")]
    [Arguments("missing-category")]
    [Arguments("duplicate-category")]
    [Arguments("unknown-component")]
    [Arguments("missing-digest")]
    [Arguments("shared-controller")]
    [Arguments("non-utc")]
    public async Task MalformedInventoryCannotReachNativeMaterialQualification(string defect)
    {
        var command = Record();
        command = defect switch
        {
            "instance-target" => command with { Target = command.Target.Parent! },
            "source-id" => command with { SourceId = Guid.NewGuid() },
            "negative-head" => command with { ExpectedMaterialGeneration = -1 },
            "future-head" => command with { ExpectedMaterialGeneration = command.ExpectedQualificationRevision + 1 },
            "default-materials" => command with { Materials = default },
            "empty-components" => command with { Components = [] },
            "duplicate-material" => command with
            {
                Materials = [command.Materials[0], command.Materials[0] with
                { ReviewId = Guid.CreateVersion7() }]
            },
            "duplicate-review-id" => command with
            {
                Materials = [command.Materials[0], command.Materials[0] with
                { Material = command.Materials[0].Material with { SessionId = Guid.NewGuid(), SessionStamp = Guid.NewGuid() } }]
            },
            "duplicate-component" => command with
            {
                Components = [command.Components[0], command.Components[0] with
                { Digest = new string('b', 64) }]
            },
            "missing-category" => command with { Materials = [command.Materials[0] with { CategoryVersionIds = [] }] },
            "duplicate-category" => command with
            {
                Materials = [command.Materials[0] with
                { CategoryVersionIds = [command.Materials[0].CategoryVersionIds[0], command.Materials[0].CategoryVersionIds[0]] }]
            },
            "unknown-component" => command with
            {
                Components = [command.Components[0] with
                { Kind = (ModerationClassificationMaterialComponentKind)999 }]
            },
            "missing-digest" => command with { Components = [command.Components[0] with { Digest = "" }] },
            "shared-controller" => command with
            {
                Mandate = command.Mandate with
                { ReviewerControllerHumanId = command.Mandate.OwnerControllerHumanId }
            },
            "non-utc" => command with { ValidUntil = command.ValidUntil.ToOffset(TimeSpan.FromHours(1)) },
            _ => throw new ArgumentOutOfRangeException(nameof(defect))
        };
        await Assert.That((await new ModerationClassificationMaterialCommandValidator<RecordModerationClassificationMaterialCommand>()
            .ValidateAsync(command)).IsValid).IsFalse();
    }

    [Test]
    [Arguments("same-id")]
    [Arguments("source-revision")]
    [Arguments("writer-generation")]
    [Arguments("non-v7")]
    [Arguments("expiry")]
    public async Task AcceptanceRequiresExactFiniteImmutableSourceIntent(string defect)
    {
        var record = Record();
        var command = new AcceptModerationClassificationMaterialCommand
        {
            OperationId = Guid.CreateVersion7(),
            Target = record.Target,
            ExpectedQualificationRevision = 3,
            SourceId = record.SourceId,
            ReviewId = Guid.CreateVersion7(),
            ExpectedSourceRevision = 1,
            ExpectedSourceWriterGeneration = 2,
            ValidUntil = record.ValidUntil
        };
        command = defect switch
        {
            "same-id" => command with { ReviewId = command.SourceId },
            "source-revision" => command with { ExpectedSourceRevision = 2 },
            "writer-generation" => command with { ExpectedSourceWriterGeneration = 0 },
            "non-v7" => command with { ReviewId = Guid.NewGuid() },
            "expiry" => command with { ValidUntil = DateTimeOffset.MaxValue },
            _ => throw new ArgumentOutOfRangeException(nameof(defect))
        };
        await Assert.That((await new ModerationClassificationMaterialCommandValidator<AcceptModerationClassificationMaterialCommand>()
            .ValidateAsync(command)).IsValid).IsFalse();
    }

    [Test]
    [Arguments(ModerationClassificationMaterialEnd.Corrected, 1L, true)]
    [Arguments(ModerationClassificationMaterialEnd.Withdrawn, 1L, true)]
    [Arguments(ModerationClassificationMaterialEnd.Withdrawn, 2L, false)]
    [Arguments((ModerationClassificationMaterialEnd)999, 1L, false)]
    public async Task AccountableEndingRequiresNoReplacementLeaseOrCollectionConsent(
        ModerationClassificationMaterialEnd ending, long revision, bool valid)
    {
        var command = new EndModerationClassificationMaterialCommand
        {
            OperationId = Guid.CreateVersion7(),
            Target = Record().Target,
            ExpectedQualificationRevision = 4,
            ReviewId = Guid.CreateVersion7(),
            ExpectedReviewRevision = revision,
            End = ending,
            Grounds = "Accountable correction of exact reviewed material."
        };
        await Assert.That((await new ModerationClassificationMaterialCommandValidator<EndModerationClassificationMaterialCommand>()
            .ValidateAsync(command)).IsValid).IsEqualTo(valid);
    }

    private static RecordModerationClassificationMaterialCommand Record()
    {
        var owner = Guid.CreateVersion7();
        var reviewer = Guid.CreateVersion7();
        var material = new ModerationClassificationMaterial(Guid.NewGuid(), Guid.NewGuid(), null, null, null, null);
        return new()
        {
            OperationId = Guid.CreateVersion7(),
            ExpectedQualificationRevision = 1,
            Target = AuthorityScope.Tenant(AuthorityScope.Instance(AuthorityScope.Solution(
                AuthorityScope.TrustDomain(Guid.CreateVersion7()), Guid.CreateVersion7()), Guid.CreateVersion7()),
                Guid.CreateVersion7()),
            SourceId = Guid.CreateVersion7(),
            ExpectedMaterialGeneration = 0,
            EventGenerationId = Guid.CreateVersion7(),
            PolicyId = Guid.CreateVersion7(),
            ExpectedPolicyRevision = 1,
            PublicationId = Guid.CreateVersion7(),
            ProfileId = Guid.CreateVersion7(),
            Mode = ModerationNativeMode.Embedded,
            TaxonomyId = Guid.CreateVersion7(),
            ExpectedTaxonomySourceId = Guid.CreateVersion7(),
            ExpectedTaxonomyAdoptionId = Guid.CreateVersion7(),
            Mandate = new(owner, owner, Guid.CreateVersion7(), 1, reviewer, reviewer,
                "Current event ownership assignment.", "Independent material review mandate."),
            Materials = [new(Guid.CreateVersion7(), material, [Guid.CreateVersion7()])],
            Components = [new(ModerationClassificationMaterialComponentKind.Event, material.EventId,
                material.EventStamp, new string('a', 64))],
            ValidUntil = new(2026, 10, 14, 12, 5, 0, TimeSpan.Zero)
        };
    }
}
