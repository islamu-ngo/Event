using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.Moderation.Requests.Commands;

namespace Explore.Application.Features.Moderation.Handlers.Commands;

public sealed class RecordModerationContributionSelectionCommandHandler(IModerationQualificationRepository sources)
    : ICommandHandler<RecordModerationContributionSelectionCommand, ModerationQualificationResponse>
{
    public async Task<ModerationQualificationResponse> ExecuteAsync(
        RecordModerationContributionSelectionCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = await new ModerationQualificationPolicyCommandValidator<RecordModerationContributionSelectionCommand>()
            .ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return ModerationQualificationResponse.Invalid(validation.Errors.Select(error => error.ErrorMessage));
        var result = await sources.ExecuteAsync(command.ToMutation(), cancellationToken);
        return ModerationQualificationResponse.FromOutcome(command.OperationId, result);
    }
}
