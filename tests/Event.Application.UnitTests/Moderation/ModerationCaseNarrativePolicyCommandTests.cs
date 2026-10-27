using Explore.Application.Features.Moderation.Handlers.Commands;
using Explore.Application.Features.Moderation.Requests.Commands;
using Explore.Domain;
using ISLAMU.Moderation.Domain;

namespace Event.Application.UnitTests.Moderation;

public sealed class ModerationCaseNarrativePolicyCommandTests
{
    [Test]
    [Arguments("operation")]
    [Arguments("scope")]
    [Arguments("source")]
    [Arguments("kind")]
    [Arguments("retention")]
    [Arguments("size")]
    [Arguments("controller")]
    [Arguments("deadline")]
    public async Task CustodyRecordingRejectsUnqualifiedCoordinatesOrTerms(string defect)
    {
        var command = Record();
        await Assert.That((await new ModerationCaseNarrativePolicyCommandValidator<RecordModerationCaseNarrativePolicyCommand>()
            .ValidateAsync(command)).IsValid).IsTrue();
        command = defect switch
        {
            "operation" => command with { OperationId = Guid.Empty },
            "scope" => command with { Target = command.Target.Parent! },
            "source" => command with { SourceId = Guid.Empty },
            "kind" => command with { TargetKind = (ModerationCaseTargetKind)0 },
            "retention" => command with { Terms = command.Terms with { MaximumRetention = TimeSpan.Zero } },
            "size" => command with { Terms = command.Terms with { MaximumMaterialBytes = 65537 } },
            "controller" => command with
            {
                Ownership = command.Ownership with { PrivacyControllerHumanId = command.Ownership.GovernanceControllerHumanId }
            },
            _ => command with { ValidUntil = DateTimeOffset.MaxValue }
        };
        await Assert.That((await new ModerationCaseNarrativePolicyCommandValidator<RecordModerationCaseNarrativePolicyCommand>()
            .ValidateAsync(command)).IsValid).IsFalse();
    }

    [Test]
    public async Task AdoptionCannotAliasItsSourceOrClaimAnotherSourceRevision()
    {
        var source = Record();
        var command = new AdoptModerationCaseNarrativePolicyCommand
        {
            OperationId = Guid.CreateVersion7(),
            Target = source.Target,
            ExpectedQualificationRevision = 2,
            SourceId = source.SourceId,
            AdoptionId = Guid.CreateVersion7(),
            ExpectedSourceRevision = 1,
            ExpectedSourceWriterGeneration = 2,
            ValidUntil = source.ValidUntil
        };
        var validator = new ModerationCaseNarrativePolicyCommandValidator<AdoptModerationCaseNarrativePolicyCommand>();
        await Assert.That((await validator.ValidateAsync(command)).IsValid).IsTrue();
        await Assert.That((await validator.ValidateAsync(command with { AdoptionId = command.SourceId })).IsValid).IsFalse();
        await Assert.That((await validator.ValidateAsync(command with { ExpectedSourceRevision = 2 })).IsValid).IsFalse();
    }

    [Test]
    public async Task WithdrawalRequiresTheExactOriginalAdoptionRevision()
    {
        var source = Record();
        var command = new WithdrawModerationCaseNarrativePolicyCommand
        {
            OperationId = Guid.CreateVersion7(),
            Target = source.Target,
            ExpectedQualificationRevision = 3,
            AdoptionId = Guid.CreateVersion7(),
            ExpectedAdoptionRevision = 1
        };
        var validator = new ModerationCaseNarrativePolicyCommandValidator<WithdrawModerationCaseNarrativePolicyCommand>();
        await Assert.That((await validator.ValidateAsync(command)).IsValid).IsTrue();
        await Assert.That((await validator.ValidateAsync(command with { ExpectedAdoptionRevision = 0 })).IsValid).IsFalse();
    }

    private static RecordModerationCaseNarrativePolicyCommand Record() => new()
    {
        OperationId = Guid.CreateVersion7(),
        Target = AuthorityScope.Tenant(AuthorityScope.Instance(
            AuthorityScope.Solution(AuthorityScope.TrustDomain(Guid.CreateVersion7()), Guid.CreateVersion7()),
            Guid.CreateVersion7()), Guid.CreateVersion7()),
        ExpectedQualificationRevision = 1,
        SourceId = Guid.CreateVersion7(),
        TargetKind = ModerationCaseTargetKind.Service,
        Terms = new(ModerationCaseNarrativePurpose.ConfidentialAllegation, TimeSpan.FromHours(1), 4096),
        Ownership = new(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            "Independent canonical controllers.", "Finite confidential custody and independent ordinary erasure."),
        ValidUntil = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero)
    };
}
