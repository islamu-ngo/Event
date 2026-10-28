using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Requests.Commands;

namespace Explore.Application.Features.Moderation.Handlers.Commands;

public sealed class AdoptModerationResourceTargetCommandHandler(IModerationQualificationRepository sources)
    : ICommandHandler<AdoptModerationResourceTargetCommand, ModerationQualificationResponse>
{
    public async Task<ModerationQualificationResponse> ExecuteAsync(
        AdoptModerationResourceTargetCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = await new ModerationQualificationPolicyCommandValidator<AdoptModerationResourceTargetCommand>()
            .ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return ModerationQualificationResponse.Invalid(validation.Errors.Select(error => error.ErrorMessage));
        return ModerationQualificationResponse.FromOutcome(command.OperationId,
            await sources.ExecuteAsync(command.ToMutation(), cancellationToken));
    }
}

public sealed class RevokeModerationResourceTargetCommandHandler(IModerationQualificationRepository sources)
    : ICommandHandler<RevokeModerationResourceTargetCommand, ModerationQualificationResponse>
{
    public async Task<ModerationQualificationResponse> ExecuteAsync(
        RevokeModerationResourceTargetCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = await new ModerationQualificationPolicyCommandValidator<RevokeModerationResourceTargetCommand>()
            .ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return ModerationQualificationResponse.Invalid(validation.Errors.Select(error => error.ErrorMessage));
        return ModerationQualificationResponse.FromOutcome(command.OperationId,
            await sources.ExecuteAsync(command.ToMutation(), cancellationToken));
    }
}
