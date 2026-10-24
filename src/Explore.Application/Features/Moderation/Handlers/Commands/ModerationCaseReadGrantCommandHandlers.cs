using Explore.Application.Contracts.Moderation;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.Moderation.Requests.Commands;
using Explore.Application.Responses;
using FluentValidation;

namespace Explore.Application.Features.Moderation.Handlers.Commands;

public sealed class GrantModerationCaseReadPurposeCommandHandler(IModerationCaseReadGrantRepository repository) :
    ICommandHandler<GrantModerationCaseReadPurposeCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(
        GrantModerationCaseReadPurposeCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = await new GrantModerationCaseReadPurposeCommandValidator()
            .ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return BaseCommandResponse.Validation<Guid>(validation.Errors.Select(error => error.ErrorMessage));
        var grant = await repository.GrantAsync(new(
            command.OperationId, command.CaseId, command.ExpectedCaseRevision, command.SourceId,
            command.GranteeUserId, command.ValidUntil), cancellationToken);
        return grant is null
            ? BaseCommandResponse.Failure<Guid>("ModerationCaseReadGrantUnavailable")
            : BaseCommandResponse.Success(grant.Id);
    }
}

public sealed class RevokeModerationCaseReadPurposeCommandHandler(IModerationCaseReadGrantRepository repository) :
    ICommandHandler<RevokeModerationCaseReadPurposeCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(
        RevokeModerationCaseReadPurposeCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = await new RevokeModerationCaseReadPurposeCommandValidator()
            .ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return BaseCommandResponse.Validation<Guid>(validation.Errors.Select(error => error.ErrorMessage));
        var grant = await repository.RevokeAsync(
            command.GrantId, command.OperationId, command.ExpectedGrantRevision, cancellationToken);
        return grant is null
            ? BaseCommandResponse.Failure<Guid>("ModerationCaseReadGrantUnavailable")
            : BaseCommandResponse.Success(grant.Id);
    }
}

public sealed class GrantModerationCaseReadPurposeCommandValidator : AbstractValidator<GrantModerationCaseReadPurposeCommand>
{
    public GrantModerationCaseReadPurposeCommandValidator()
    {
        RuleFor(command => command.OperationId).Must(value =>
            value != Guid.Empty && value.Version == 7 && value.Variant is >= 8 and <= 11);
        RuleFor(command => command.CaseId).Must(value =>
            value != Guid.Empty && value.Version == 7 && value.Variant is >= 8 and <= 11);
        RuleFor(command => command.SourceId).Must(value =>
            value != Guid.Empty && value.Version == 7 && value.Variant is >= 8 and <= 11);
        RuleFor(command => command.GranteeUserId).Must(value =>
            value != Guid.Empty && value.Version == 7 && value.Variant is >= 8 and <= 11);
        RuleFor(command => command.ExpectedCaseRevision).GreaterThan(0).LessThan(long.MaxValue);
        RuleFor(command => command.ValidUntil).Must(value =>
            value != DateTimeOffset.MinValue && value != DateTimeOffset.MaxValue && value.Offset == TimeSpan.Zero
            && value.UtcTicks % TimeSpan.TicksPerMicrosecond == 0);
    }
}

public sealed class RevokeModerationCaseReadPurposeCommandValidator : AbstractValidator<RevokeModerationCaseReadPurposeCommand>
{
    public RevokeModerationCaseReadPurposeCommandValidator()
    {
        RuleFor(command => command.OperationId).Must(value =>
            value != Guid.Empty && value.Version == 7 && value.Variant is >= 8 and <= 11);
        RuleFor(command => command.GrantId).Must(value =>
            value != Guid.Empty && value.Version == 7 && value.Variant is >= 8 and <= 11);
        RuleFor(command => command.ExpectedGrantRevision).Equal(1);
    }
}
