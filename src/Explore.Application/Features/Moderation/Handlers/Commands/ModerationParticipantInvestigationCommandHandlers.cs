using Explore.Application.Contracts.Moderation;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Requests.Commands;
using FluentValidation;

namespace Explore.Application.Features.Moderation.Handlers.Commands;

public sealed class RecordModerationParticipantInvestigationCommandHandler(IModerationQualificationRepository sources)
    : ICommandHandler<RecordModerationParticipantInvestigationCommand, ModerationQualificationResponse>
{
    public async Task<ModerationQualificationResponse> ExecuteAsync(
        RecordModerationParticipantInvestigationCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = await new ModerationParticipantInvestigationCommandValidator()
            .ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return ModerationQualificationResponse.Invalid(validation.Errors.Select(error => error.ErrorMessage));
        return ModerationQualificationResponse.FromOutcome(command.OperationId,
            await sources.ExecuteAsync(command.ToMutation(), cancellationToken));
    }
}

public sealed class ModerationParticipantInvestigationCommandValidator
    : AbstractValidator<RecordModerationParticipantInvestigationCommand>
{
    public ModerationParticipantInvestigationCommandValidator()
    {
        RuleFor(command => command).Custom((command, validation) =>
        {
            if (!ModerationParticipantInvestigationQualification.IsValid(
                    (RecordModerationParticipantIncidentResponsibilitySource)command.ToMutation()))
                validation.AddFailure("Investigation capture requires exact tenant, occurrence, evidence and session coordinates, finite custody and four bounded assessment grounds.");
        });
    }
}
