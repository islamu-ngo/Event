using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Requests.Commands;
using Explore.Application.Responses;
using FluentValidation;
using ISLAMU.Moderation.Domain;

namespace Explore.Application.Features.Moderation.Handlers.Commands;

public sealed class WithdrawModerationProfileCollectionAssessmentCommandHandler(
    IModerationProfileCollectionAssessmentRepository assessments)
    : ICommandHandler<WithdrawModerationProfileCollectionAssessmentCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(
        WithdrawModerationProfileCollectionAssessmentCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();
        var validation = await new WithdrawModerationProfileCollectionAssessmentCommandValidator()
            .ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return BaseCommandResponse.Validation<Guid>(validation.Errors.Select(error => error.ErrorMessage));
        var withdrawn = await assessments.WithdrawAsync(command.ToWithdrawal(), cancellationToken);
        return withdrawn is null
            ? BaseCommandResponse.NotFound<Guid>()
            : BaseCommandResponse.Success(withdrawn.Id);
    }
}

public sealed class WithdrawModerationProfileCollectionAssessmentCommandValidator
    : AbstractValidator<WithdrawModerationProfileCollectionAssessmentCommand>
{
    public WithdrawModerationProfileCollectionAssessmentCommandValidator()
    {
        RuleFor(command => command.Jurisdiction).Must(scope =>
            scope is not null && scope.Kind is ModerationScopeKind.Instance or ModerationScopeKind.Tenant);
        RuleFor(command => command.SubjectHumanId).NotEmpty();
        RuleFor(command => command.PolicyId).Must(Uuid);
        RuleFor(command => command.PolicyRevision).InclusiveBetween(1, long.MaxValue - 1);
        RuleFor(command => command.PublicationId).Must(Uuid);
        RuleFor(command => command.ProfileId).Must(Uuid);
        RuleFor(command => command.Mode).IsInEnum();
        RuleFor(command => command.SourceId).Must(Uuid);
        RuleFor(command => command.AdoptionId).Must(Uuid);
        RuleFor(command => command.ExpectedSourceGeneration).InclusiveBetween(1, long.MaxValue - 1);
        RuleFor(command => command.ExpectedAdoptionGeneration).InclusiveBetween(1, long.MaxValue - 1)
            .GreaterThan(command => command.ExpectedSourceGeneration);
        RuleFor(command => command.ExpectedAdoptionRevision).Equal(1);
        RuleFor(command => command).Must(command =>
            command.SourceId != command.AdoptionId && command.SourceId != command.SubjectHumanId
            && command.AdoptionId != command.SubjectHumanId);
    }

    private static bool Uuid(Guid id) =>
        id != Guid.Empty && id.Version == 7 && (id.ToByteArray(bigEndian: true)[8] & 0xc0) == 0x80;
}
