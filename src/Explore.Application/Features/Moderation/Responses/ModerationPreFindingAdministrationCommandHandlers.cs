using Explore.Application.Contracts.Moderation;
using Explore.Application.Contracts.Operations;
using Explore.Application.Responses;
using Explore.Domain;
using FluentValidation;

namespace Explore.Application.Features.Moderation.Responses;

public sealed class ProposeModerationPreFindingCustodyCommandHandler(IModerationPreFindingRepository repository)
    : ICommandHandler<ProposeModerationPreFindingCustodyCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(
        ProposeModerationPreFindingCustodyCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = await new ProposeModerationPreFindingCustodyCommandValidator().ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return BaseCommandResponse.Validation<Guid>(validation.Errors.Select(error => error.ErrorMessage));
        var result = await repository.ExecutePreFindingAsync(new ProposeModerationPreFindingCustody(
            command.OperationId, command.CaseId, command.ExpectedCaseRevision, command.PrivacyOwnerHumanId,
            command.Terms, command.ValidUntil), cancellationToken);
        return ModerationPreFindingCommandResponse.FromOutcome(result, command.OperationId);
    }
}

public sealed class AcceptModerationPreFindingCustodyCommandHandler(IModerationPreFindingRepository repository)
    : ICommandHandler<AcceptModerationPreFindingCustodyCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(
        AcceptModerationPreFindingCustodyCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = await new AcceptModerationPreFindingCustodyCommandValidator().ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return BaseCommandResponse.Validation<Guid>(validation.Errors.Select(error => error.ErrorMessage));
        var result = await repository.ExecutePreFindingAsync(new AcceptModerationPreFindingCustody(
            command.OperationId, command.CaseId, command.ExpectedCaseRevision,
            command.CustodyId, command.ExpectedCustodyRevision), cancellationToken);
        return ModerationPreFindingCommandResponse.FromOutcome(result, command.OperationId);
    }
}

public sealed class IssueModerationPreFindingNoticeCommandHandler(IModerationPreFindingRepository repository)
    : ICommandHandler<IssueModerationPreFindingNoticeCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(
        IssueModerationPreFindingNoticeCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = await new IssueModerationPreFindingNoticeCommandValidator().ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return BaseCommandResponse.Validation<Guid>(validation.Errors.Select(error => error.ErrorMessage));
        var result = await repository.ExecutePreFindingAsync(new IssueModerationPreFindingNotice(
            command.OperationId, command.CaseId, command.ExpectedCaseRevision, command.CustodyId,
            command.Recipient, command.Text, command.RetainUntil), cancellationToken);
        return ModerationPreFindingCommandResponse.FromOutcome(result, command.OperationId);
    }
}

public sealed class WithdrawModerationPreFindingCustodyCommandHandler(IModerationPreFindingRepository repository)
    : ICommandHandler<WithdrawModerationPreFindingCustodyCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(
        WithdrawModerationPreFindingCustodyCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = await new WithdrawModerationPreFindingCustodyCommandValidator().ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return BaseCommandResponse.Validation<Guid>(validation.Errors.Select(error => error.ErrorMessage));
        var result = await repository.ExecutePreFindingAsync(new WithdrawModerationPreFindingCustody(
            command.OperationId, command.CaseId, command.ExpectedCaseRevision,
            command.CustodyId, command.ExpectedCustodyRevision), cancellationToken);
        return ModerationPreFindingCommandResponse.FromOutcome(result, command.OperationId);
    }
}

public sealed class ProposeModerationPreFindingCustodyCommandValidator
    : AbstractValidator<ProposeModerationPreFindingCustodyCommand>
{
    public ProposeModerationPreFindingCustodyCommandValidator()
    {
        RuleFor(command => command.OperationId).Must(IsUuidV7);
        RuleFor(command => command.CaseId).Must(IsUuidV7);
        RuleFor(command => command.PrivacyOwnerHumanId).Must(IsUuidV7);
        RuleFor(command => command.ExpectedCaseRevision).InclusiveBetween(1, long.MaxValue - 1);
        RuleFor(command => command.ValidUntil).Must(value =>
            value.Offset == TimeSpan.Zero && value != DateTimeOffset.MinValue && value != DateTimeOffset.MaxValue);
        RuleFor(command => command.Terms).Must(ValidTerms);
    }

    private static bool IsUuidV7(Guid value) => value != Guid.Empty && value.Version == 7 && value.Variant is >= 8 and <= 11;

    private static bool ValidTerms(ModerationPreFindingCustodyTerms? terms)
    {
        if (terms is null) return false;
        try { terms.Validate(); return true; }
        catch (ArgumentException) { return false; }
    }
}

public sealed class AcceptModerationPreFindingCustodyCommandValidator
    : AbstractValidator<AcceptModerationPreFindingCustodyCommand>
{
    public AcceptModerationPreFindingCustodyCommandValidator()
    {
        RuleFor(command => command.OperationId).Must(IsUuidV7);
        RuleFor(command => command.CaseId).Must(IsUuidV7);
        RuleFor(command => command.CustodyId).Must(IsUuidV7);
        RuleFor(command => command.ExpectedCaseRevision).InclusiveBetween(1, long.MaxValue - 1);
        RuleFor(command => command.ExpectedCustodyRevision).Equal(1);
    }

    private static bool IsUuidV7(Guid value) => value != Guid.Empty && value.Version == 7 && value.Variant is >= 8 and <= 11;
}

public sealed class IssueModerationPreFindingNoticeCommandValidator
    : AbstractValidator<IssueModerationPreFindingNoticeCommand>
{
    public IssueModerationPreFindingNoticeCommandValidator()
    {
        RuleFor(command => command.OperationId).Must(IsUuidV7);
        RuleFor(command => command.CaseId).Must(IsUuidV7);
        RuleFor(command => command.CustodyId).Must(IsUuidV7);
        RuleFor(command => command.ExpectedCaseRevision).InclusiveBetween(1, long.MaxValue - 1);
        RuleFor(command => command.Recipient).Must(value => value is not null && IsUuidV7(value.HumanId)
            && (value.AssignmentId == Guid.Empty && value.AssignmentRevision == 0
                || IsUuidV7(value.AssignmentId) && value.AssignmentRevision is > 0 and < long.MaxValue));
        RuleFor(command => command.Text).Must(value => value is not null
            && value.Allegation is not null && value.ApplicableRule is not null && value.ResponseInstructions is not null);
        RuleFor(command => command.RetainUntil).Must(value =>
            value.Offset == TimeSpan.Zero && value != DateTimeOffset.MinValue && value != DateTimeOffset.MaxValue);
    }

    private static bool IsUuidV7(Guid value) => value != Guid.Empty && value.Version == 7 && value.Variant is >= 8 and <= 11;
}

public sealed class WithdrawModerationPreFindingCustodyCommandValidator
    : AbstractValidator<WithdrawModerationPreFindingCustodyCommand>
{
    public WithdrawModerationPreFindingCustodyCommandValidator()
    {
        RuleFor(command => command.OperationId).Must(IsUuidV7);
        RuleFor(command => command.CaseId).Must(IsUuidV7);
        RuleFor(command => command.CustodyId).Must(IsUuidV7);
        RuleFor(command => command.ExpectedCaseRevision).InclusiveBetween(1, long.MaxValue - 1);
        RuleFor(command => command.ExpectedCustodyRevision).InclusiveBetween(1, 2);
    }

    private static bool IsUuidV7(Guid value) => value != Guid.Empty && value.Version == 7 && value.Variant is >= 8 and <= 11;
}
