using Explore.Application.Contracts.Moderation;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.Moderation.Requests.Commands;
using FluentValidation;

namespace Explore.Application.Features.Moderation.Handlers.Commands;

public sealed class SubmitModerationAllegationCommandHandler(IModerationCaseIntakeCommitter intake)
    : ICommandHandler<SubmitModerationAllegationCommand, ModerationAllegationResponse>
{
    public async Task<ModerationAllegationResponse> ExecuteAsync(
        SubmitModerationAllegationCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = await new SubmitModerationAllegationCommandValidator()
            .ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return ModerationAllegationResponse.Invalid(validation.Errors.Select(error => error.ErrorMessage));
        var result = await intake.SubmitAsync(command.ToRequest(), cancellationToken);
        return ModerationAllegationResponse.FromOutcome(command.OperationId, result);
    }
}

public sealed class SubmitModerationAllegationCommandValidator : AbstractValidator<SubmitModerationAllegationCommand>
{
    public SubmitModerationAllegationCommandValidator()
    {
        RuleFor(command => command.OperationId).Must(IsUuidV7);
        RuleFor(command => command.ReceiptId).Must(IsUuidV7);
        RuleFor(command => command.PresentedToken).Must(token => token.Length == 32);
        RuleFor(command => command.Narrative).Must(material => material.Length is > 0 and <= 65536)
            .WithMessage("A bounded receipt-qualified allegation is required.");
    }

    private static bool IsUuidV7(Guid value) => value != Guid.Empty && value.Version == 7 && value.Variant is >= 8 and <= 11;
}
