using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Requests.Commands;

namespace Explore.Application.Features.Moderation.Handlers.Commands;

public sealed class ApproveModerationNoticeDisclosureCommandHandler(IModerationQualificationRepository sources)
    : ICommandHandler<ApproveModerationNoticeDisclosureCommand, ModerationQualificationResponse>
{
    public async Task<ModerationQualificationResponse> ExecuteAsync(
        ApproveModerationNoticeDisclosureCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = await new ModerationQualificationPolicyCommandValidator<ApproveModerationNoticeDisclosureCommand>()
            .ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return ModerationQualificationResponse.Invalid(validation.Errors.Select(error => error.ErrorMessage));
        var result = await sources.ExecuteAsync(command.ToMutation(), cancellationToken);
        return ModerationQualificationResponse.FromOutcome(command.OperationId, result);
    }
}

public sealed class RevokeModerationNoticeDisclosureCommandHandler(IModerationQualificationRepository sources)
    : ICommandHandler<RevokeModerationNoticeDisclosureCommand, ModerationQualificationResponse>
{
    public async Task<ModerationQualificationResponse> ExecuteAsync(
        RevokeModerationNoticeDisclosureCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = await new ModerationQualificationPolicyCommandValidator<RevokeModerationNoticeDisclosureCommand>()
            .ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return ModerationQualificationResponse.Invalid(validation.Errors.Select(error => error.ErrorMessage));
        var result = await sources.ExecuteAsync(command.ToMutation(), cancellationToken);
        return ModerationQualificationResponse.FromOutcome(command.OperationId, result);
    }
}
