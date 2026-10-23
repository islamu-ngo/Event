using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Policy;
using Explore.Application.Features.Moderation.Requests.Commands;

namespace Explore.Application.Features.Moderation.Handlers.Commands;

public sealed class PrepareModerationPolicyPublicationCommandHandler(
    IModerationPolicyPublicationRepository publications)
    : ICommandHandler<PrepareModerationPolicyPublicationCommand, ModerationPolicyPublicationCandidateResponse>
{
    public async Task<ModerationPolicyPublicationCandidateResponse> ExecuteAsync(
        PrepareModerationPolicyPublicationCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = await new ModerationPolicyPublicationCommandValidator()
            .ValidateAsync(new PublishModerationPolicyCommand(command.Request), cancellationToken);
        if (!validation.IsValid)
            return ModerationPolicyPublicationCandidateResponse.Invalid(validation.Errors.Select(error => error.ErrorMessage));
        if (command.Request.Proposed.RequestedState != ModerationPolicyRequestedState.Active
            || command.ValidUntil.Offset != TimeSpan.Zero
            || command.ValidUntil == DateTimeOffset.MinValue || command.ValidUntil == DateTimeOffset.MaxValue)
            return ModerationPolicyPublicationCandidateResponse.Invalid(
                ["Preparation requires an exact active intent and a finite UTC deadline."]);
        var result = await publications.PrepareAsync(command.Request, command.ValidUntil, cancellationToken);
        return ModerationPolicyPublicationCandidateResponse.FromOutcome(command.Request.OperationId, result);
    }
}
