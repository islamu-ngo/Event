using Explore.Application.Contracts.Moderation;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.Moderation.Requests.Commands;
using Explore.Domain;
using FluentValidation;
using ISLAMU.Moderation.Domain;

namespace Explore.Application.Features.Moderation.Handlers.Commands;

public sealed class IssueModerationSubmissionReceiptCommandHandler(IModerationCaseIntakeCommitter intake)
    : ICommandHandler<IssueModerationSubmissionReceiptCommand, ModerationSubmissionReceiptResponse>
{
    public async Task<ModerationSubmissionReceiptResponse> ExecuteAsync(
        IssueModerationSubmissionReceiptCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = await new IssueModerationSubmissionReceiptCommandValidator()
            .ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return ModerationSubmissionReceiptResponse.Invalid(validation.Errors.Select(error => error.ErrorMessage));
        var committed = await intake.IssueSubmissionReceiptAsync(command.ToRequest(), cancellationToken);
        return ModerationSubmissionReceiptResponse.FromCommitted(committed);
    }
}

public sealed class IssueModerationSubmissionReceiptCommandValidator : AbstractValidator<IssueModerationSubmissionReceiptCommand>
{
    public IssueModerationSubmissionReceiptCommandValidator()
    {
        RuleFor(command => command.Kind).IsInEnum();
        RuleFor(command => command.NativeKind).Must(kind =>
            kind is >= ModerationCaseTargetKind.EventVersion and <= ModerationCaseTargetKind.Destination);
        RuleFor(command => command.NativeTargetId).NotEmpty();
        RuleFor(command => command.EventId).Must(value => value is null || value != Guid.Empty);
        RuleFor(command => command.RelationshipKind).IsInEnum()
            .NotEqual(ModerationCaseRelationshipKind.CaseReadGrant);
        RuleFor(command => command.RelationshipSourceId).NotEmpty();
        RuleFor(command => command.OccurrenceMaterial).Must(value => value.Length is > 0 and <= 65536);
        RuleFor(command => command.Material).Must(value => value.Length is > 0 and <= 65536);
    }

}
