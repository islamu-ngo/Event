using Explore.Application.Contracts.Moderation;
using Explore.Application.Features.Moderation.Handlers.Commands;
using Explore.Application.Features.Moderation.Requests.Commands;
using Explore.Domain;
using ISLAMU.Moderation.Domain;

namespace Event.Application.UnitTests.Moderation;

public sealed class ModerationParticipantInvestigationAttributionCommandTests
{
    [Test]
    public async Task HumanInvestigationWithExactOriginalSourceRevisionIsValid()
    {
        await Assert.That((await new AttributeModerationCaseCommandValidator().ValidateAsync(Command())).IsValid).IsTrue();
    }

    [Test]
    [Arguments("missing-id")]
    [Arguments("missing-revision")]
    [Arguments("non-v7")]
    [Arguments("empty-id")]
    [Arguments("zero-revision")]
    [Arguments("withdrawn-revision")]
    [Arguments("nonhuman")]
    [Arguments("unknown-basis")]
    public async Task IncompleteInvestigationCannotReachNativeQualification(string defect)
    {
        var command = Command();
        command = defect switch
        {
            "missing-id" => command with { InvestigationSourceId = null },
            "missing-revision" => command with { InvestigationSourceRevision = null },
            "non-v7" => command with { InvestigationSourceId = Guid.NewGuid() },
            "empty-id" => command with { InvestigationSourceId = Guid.Empty },
            "zero-revision" => command with { InvestigationSourceRevision = 0 },
            "withdrawn-revision" => command with { InvestigationSourceRevision = 2 },
            "nonhuman" => command with { SubjectKind = ModerationSubjectKind.Organization },
            "unknown-basis" => command with { Basis = (ModerationResponsibilityBasis)6 },
            _ => throw new ArgumentOutOfRangeException(nameof(defect))
        };
        await Assert.That((await new AttributeModerationCaseCommandValidator().ValidateAsync(command)).IsValid).IsFalse();
    }

    [Test]
    [Arguments(ModerationResponsibilityBasis.EventSubmission, ModerationSubjectKind.Human)]
    [Arguments(ModerationResponsibilityBasis.EventContent, ModerationSubjectKind.Content)]
    [Arguments(ModerationResponsibilityBasis.OrganizationalActor, ModerationSubjectKind.Organization)]
    [Arguments(ModerationResponsibilityBasis.ServiceIdentity, ModerationSubjectKind.Service)]
    public async Task OtherBasesRequireBothInvestigationCoordinatesAbsent(
        ModerationResponsibilityBasis basis, ModerationSubjectKind kind)
    {
        var command = Command() with { Basis = basis, SubjectKind = kind };
        var validator = new AttributeModerationCaseCommandValidator();
        await Assert.That((await validator.ValidateAsync(command)).IsValid).IsFalse();
        await Assert.That((await validator.ValidateAsync(command with { InvestigationSourceId = null })).IsValid).IsFalse();
        await Assert.That((await validator.ValidateAsync(command with { InvestigationSourceRevision = null })).IsValid).IsFalse();
        await Assert.That((await validator.ValidateAsync(command with
        {
            InvestigationSourceId = null, InvestigationSourceRevision = null
        })).IsValid).IsTrue();
    }

    [Test]
    public async Task HandlerPreservesExactInvestigationIntentForNativeOwner()
    {
        var command = Command();
        var repository = new IntentRepository();
        await new AttributeModerationCaseCommandHandler(repository).ExecuteAsync(command);
        await Assert.That(repository.Intent).IsEqualTo(new ModerationCaseAttributionRequest(
            command.OperationId, command.OccurrenceCaseId, command.ExpectedCaseRevision, command.EvidenceCaseRevision,
            command.ReadGrantId, command.Basis, command.SubjectKind, command.SubjectId, command.ValidUntil)
        {
            InvestigationSourceId = command.InvestigationSourceId,
            InvestigationSourceRevision = command.InvestigationSourceRevision
        });
    }

    [Test]
    public async Task MissingInvestigationCannotInvokeNativeAttributionOwner()
    {
        var repository = new IntentRepository();
        await new AttributeModerationCaseCommandHandler(repository)
            .ExecuteAsync(Command() with { InvestigationSourceId = null });
        await Assert.That(repository.Intent).IsNull();
    }

    private static AttributeModerationCaseCommand Command() => new()
    {
        OperationId = Guid.CreateVersion7(), OccurrenceCaseId = Guid.CreateVersion7(),
        ExpectedCaseRevision = 1, EvidenceCaseRevision = 1, ReadGrantId = Guid.CreateVersion7(),
        Basis = ModerationResponsibilityBasis.ParticipantInvestigation, SubjectKind = ModerationSubjectKind.Human,
        SubjectId = Guid.CreateVersion7(), ValidUntil = new(2026, 10, 13, 12, 2, 0, TimeSpan.Zero),
        InvestigationSourceId = Guid.CreateVersion7(), InvestigationSourceRevision = 1
    };

    private sealed class IntentRepository : IModerationCaseAttributionRepository
    {
        public ModerationCaseAttributionRequest? Intent { get; private set; }

        public Task<ModerationCase?> AttributeAsync(ModerationCaseAttributionRequest request, CancellationToken cancellationToken)
        {
            Intent = request;
            return Task.FromResult<ModerationCase?>(null);
        }

        public Task<ModerationCaseAttributionSource?> RenewAsync(
            ModerationCaseAttributionRenewalRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ModerationCaseAttributionSource?> WithdrawAsync(
            Guid sourceId, Guid operationId, long expectedRevision, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
