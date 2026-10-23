using Explore.Application.Features.Moderation.Handlers.Commands;
using Explore.Application.Features.Moderation.Requests.Commands;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using ISLAMU.Moderation.Domain;

namespace Event.Application.UnitTests.Moderation;

public sealed class ModerationClassificationTaxonomyCommandTests
{
    [Test]
    public async Task Complete_proposal_is_valid_intent_not_authenticated_approval()
    {
        await Assert.That((await new ModerationClassificationTaxonomyCommandValidator<RecordModerationClassificationTaxonomyCommand>()
            .ValidateAsync(Record())).IsValid).IsTrue();
    }

    [Test]
    [Arguments("taxonomy-id")]
    [Arguments("negative-head")]
    [Arguments("overflow-head")]
    [Arguments("default-categories")]
    [Arguments("empty-categories")]
    [Arguments("duplicate-lookup")]
    [Arguments("duplicate-code")]
    [Arguments("duplicate-version")]
    [Arguments("invalid-code")]
    [Arguments("unknown-kind")]
    [Arguments("shared-controller")]
    [Arguments("missing-meaning")]
    [Arguments("non-utc")]
    [Arguments("overflow-policy")]
    public async Task Malformed_proposal_cannot_reach_native_taxonomy_qualification(string defect)
    {
        var command = Record();
        command = defect switch
        {
            "taxonomy-id" => command with { TaxonomyId = Guid.NewGuid() },
            "negative-head" => command with { ExpectedTaxonomyGeneration = -1 },
            "overflow-head" => command with { ExpectedTaxonomyGeneration = long.MaxValue },
            "default-categories" => command with { Categories = default },
            "empty-categories" => command with { Categories = [] },
            "duplicate-lookup" => command with
            {
                Categories = [command.Categories[0],
                command.Categories[0] with { VersionId = Guid.CreateVersion7(), Code = "other" }]
            },
            "duplicate-code" => command with
            {
                Categories = [command.Categories[0],
                command.Categories[0] with { VersionId = Guid.CreateVersion7(), CategoryId = 2 }]
            },
            "duplicate-version" => command with
            {
                Categories = [command.Categories[0],
                command.Categories[0] with { CategoryId = 2, Code = "other" }]
            },
            "invalid-code" => command with { Categories = [command.Categories[0] with { Code = "general " }] },
            "unknown-kind" => command with
            {
                Categories = [command.Categories[0] with
                { Kind = (EventContentClassificationVersion.CategoryKind)99 }]
            },
            "shared-controller" => command with
            {
                Mandate = command.Mandate with
                { ReviewerControllerHumanId = command.Mandate.OwnerControllerHumanId }
            },
            "missing-meaning" => command with { Categories = [command.Categories[0] with { Meaning = "" }] },
            "non-utc" => command with { ValidUntil = command.ValidUntil.ToOffset(TimeSpan.FromHours(1)) },
            "overflow-policy" => command with { ExpectedPolicyRevision = long.MaxValue },
            _ => throw new ArgumentOutOfRangeException(nameof(defect))
        };
        await Assert.That((await new ModerationClassificationTaxonomyCommandValidator<RecordModerationClassificationTaxonomyCommand>()
            .ValidateAsync(command)).IsValid).IsFalse();
    }

    [Test]
    [Arguments("same-id")]
    [Arguments("source-revision")]
    [Arguments("writer-generation")]
    [Arguments("non-v7")]
    [Arguments("expiry")]
    public async Task Acceptance_requires_exact_immutable_source_coordinates(string defect)
    {
        var record = Record();
        var command = new AdoptModerationClassificationTaxonomyCommand
        {
            OperationId = Guid.CreateVersion7(),
            Target = record.Target,
            ExpectedQualificationRevision = 3,
            SourceId = record.SourceId,
            AdoptionId = Guid.CreateVersion7(),
            ExpectedSourceRevision = 1,
            ExpectedSourceWriterGeneration = 2,
            ValidUntil = record.ValidUntil
        };
        command = defect switch
        {
            "same-id" => command with { AdoptionId = command.SourceId },
            "source-revision" => command with { ExpectedSourceRevision = 2 },
            "writer-generation" => command with { ExpectedSourceWriterGeneration = 0 },
            "non-v7" => command with { AdoptionId = Guid.NewGuid() },
            "expiry" => command with { ValidUntil = DateTimeOffset.MaxValue },
            _ => throw new ArgumentOutOfRangeException(nameof(defect))
        };
        await Assert.That((await new ModerationClassificationTaxonomyCommandValidator<AdoptModerationClassificationTaxonomyCommand>()
            .ValidateAsync(command)).IsValid).IsFalse();
    }

    [Test]
    [Arguments(1L, true)]
    [Arguments(2L, false)]
    public async Task Withdrawal_has_no_new_lease_or_consent_requirement(long revision, bool valid)
    {
        var record = Record();
        var command = new WithdrawModerationClassificationTaxonomyCommand
        {
            OperationId = Guid.CreateVersion7(),
            Target = record.Target,
            ExpectedQualificationRevision = 4,
            AdoptionId = Guid.CreateVersion7(),
            ExpectedAdoptionRevision = revision
        };
        await Assert.That((await new ModerationClassificationTaxonomyCommandValidator<WithdrawModerationClassificationTaxonomyCommand>()
            .ValidateAsync(command)).IsValid).IsEqualTo(valid);
    }

    private static RecordModerationClassificationTaxonomyCommand Record()
    {
        var owner = Guid.CreateVersion7();
        var reviewer = Guid.CreateVersion7();
        return new()
        {
            OperationId = Guid.CreateVersion7(),
            ExpectedQualificationRevision = 1,
            Target = AuthorityScope.Instance(AuthorityScope.Solution(
                AuthorityScope.TrustDomain(Guid.CreateVersion7()), Guid.CreateVersion7()), Guid.CreateVersion7()),
            SourceId = Guid.CreateVersion7(),
            TaxonomyId = Guid.CreateVersion7(),
            ExpectedTaxonomyGeneration = 0,
            PolicyId = Guid.CreateVersion7(),
            ExpectedPolicyRevision = 1,
            PublicationId = Guid.CreateVersion7(),
            ProfileId = Guid.CreateVersion7(),
            Mode = ModerationNativeMode.Embedded,
            Mandate = new(owner, owner, reviewer, reviewer, "Accountable taxonomy ownership.",
                "Independent content meaning and audience-floor review."),
            Categories = [new(Guid.CreateVersion7(), 1, "general", EventContentClassificationVersion.CategoryKind.General,
                6, "Reviewed content meaning.", "Reviewed age floor.")],
            ValidUntil = new(2026, 10, 14, 12, 5, 0, TimeSpan.Zero)
        };
    }
}
