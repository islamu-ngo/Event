using Explore.Application.Contracts.Moderation;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.Moderation.Requests.Commands;
using FluentValidation;

namespace Explore.Application.Features.Moderation.Handlers.Commands;

public sealed class IssueModerationReadReceiptCommandHandler(IModerationCaseIntakeCommitter intake)
    : ICommandHandler<IssueModerationReadReceiptCommand, ModerationReadReceiptResponse>
{
    public async Task<ModerationReadReceiptResponse> ExecuteAsync(
        IssueModerationReadReceiptCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = await new IssueModerationReadReceiptCommandValidator()
            .ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return ModerationReadReceiptResponse.Invalid(validation.Errors.Select(error => error.ErrorMessage));
        var committed = await intake.IssueReadReceiptAsync(command.ReadGrantId, cancellationToken);
        return ModerationReadReceiptResponse.FromCommitted(committed);
    }
}

public sealed class IssueModerationReadReceiptCommandValidator : AbstractValidator<IssueModerationReadReceiptCommand>
{
    public IssueModerationReadReceiptCommandValidator() =>
        RuleFor(command => command.ReadGrantId).Must(value =>
            value != Guid.Empty && value.Version == 7 && value.Variant is >= 8 and <= 11);
}
