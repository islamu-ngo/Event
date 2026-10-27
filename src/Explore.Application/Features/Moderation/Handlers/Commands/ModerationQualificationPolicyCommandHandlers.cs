using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Requests.Commands;
using FluentValidation;

namespace Explore.Application.Features.Moderation.Handlers.Commands;

public sealed class PublishModerationQualificationPolicyCommandHandler(IModerationQualificationRepository sources)
    : ICommandHandler<PublishModerationQualificationPolicyCommand, ModerationQualificationResponse>
{
    public async Task<ModerationQualificationResponse> ExecuteAsync(
        PublishModerationQualificationPolicyCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = await new ModerationQualificationPolicyCommandValidator<PublishModerationQualificationPolicyCommand>()
            .ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return ModerationQualificationResponse.Invalid(validation.Errors.Select(error => error.ErrorMessage));
        var result = await sources.ExecuteAsync(command.ToMutation(), cancellationToken);
        return ModerationQualificationResponse.FromOutcome(command.OperationId, result);
    }
}

public sealed class AdoptModerationQualificationPolicyCommandHandler(IModerationQualificationRepository sources)
    : ICommandHandler<AdoptModerationQualificationPolicyCommand, ModerationQualificationResponse>
{
    public async Task<ModerationQualificationResponse> ExecuteAsync(
        AdoptModerationQualificationPolicyCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = await new ModerationQualificationPolicyCommandValidator<AdoptModerationQualificationPolicyCommand>()
            .ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return ModerationQualificationResponse.Invalid(validation.Errors.Select(error => error.ErrorMessage));
        var result = await sources.ExecuteAsync(command.ToMutation(), cancellationToken);
        return ModerationQualificationResponse.FromOutcome(command.OperationId, result);
    }
}

public sealed class RevokeModerationQualificationPolicyAdoptionCommandHandler(IModerationQualificationRepository sources)
    : ICommandHandler<RevokeModerationQualificationPolicyAdoptionCommand, ModerationQualificationResponse>
{
    public async Task<ModerationQualificationResponse> ExecuteAsync(
        RevokeModerationQualificationPolicyAdoptionCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = await new ModerationQualificationPolicyCommandValidator<RevokeModerationQualificationPolicyAdoptionCommand>()
            .ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return ModerationQualificationResponse.Invalid(validation.Errors.Select(error => error.ErrorMessage));
        var result = await sources.ExecuteAsync(command.ToMutation(), cancellationToken);
        return ModerationQualificationResponse.FromOutcome(command.OperationId, result);
    }
}

/// <summary>Both typed and generic policy entrypoints use the same authorable-shape rules, never authority claims.</summary>
public sealed class ModerationQualificationPolicyCommandValidator<TCommand> : AbstractValidator<TCommand>
    where TCommand : ModerationQualificationCommand
{
    public ModerationQualificationPolicyCommandValidator()
    {
        RuleFor(command => command).Custom((command, context) =>
        {
            var validation = new ModerationQualificationValidator().Validate(
                new ApplyModerationQualificationCommand(command.ToMutation()));
            foreach (var error in validation.Errors)
                context.AddFailure(error);
        });
    }
}
