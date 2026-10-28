using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Requests.Commands;

namespace Explore.Application.Features.Moderation.Handlers.Commands;

public sealed class PrepareModerationConsumerMeasurementCommandHandler(IModerationQualificationRepository sources)
    : ICommandHandler<PrepareModerationConsumerMeasurementCommand, ModerationQualificationResponse>
{
    public async Task<ModerationQualificationResponse> ExecuteAsync(
        PrepareModerationConsumerMeasurementCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = await new ModerationQualificationPolicyCommandValidator<PrepareModerationConsumerMeasurementCommand>()
            .ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return ModerationQualificationResponse.Invalid(validation.Errors.Select(error => error.ErrorMessage));
        return ModerationQualificationResponse.FromOutcome(command.OperationId,
            await sources.ExecuteAsync(command.ToMutation(), cancellationToken));
    }
}
