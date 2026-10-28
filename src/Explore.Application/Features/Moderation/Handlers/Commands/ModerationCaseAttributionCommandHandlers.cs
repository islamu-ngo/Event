using Explore.Application.Contracts.Moderation;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.Moderation.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using FluentValidation;
using ISLAMU.Moderation.Domain;

namespace Explore.Application.Features.Moderation.Handlers.Commands;

public sealed class AttributeModerationCaseCommandHandler(IModerationCaseAttributionRepository attribution)
    : ICommandHandler<AttributeModerationCaseCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(
        AttributeModerationCaseCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = await new AttributeModerationCaseCommandValidator().ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return BaseCommandResponse.Validation<Guid>(validation.Errors.Select(error => error.ErrorMessage));
        var result = await attribution.AttributeAsync(command.ToRequest(), cancellationToken);
        return result is null
            ? BaseCommandResponse.Failure<Guid>("ModerationCaseAttributionUnavailable")
            : BaseCommandResponse.Success(result.Id);
    }
}

public sealed class RenewModerationCaseAttributionCommandHandler(IModerationCaseAttributionRepository attribution)
    : ICommandHandler<RenewModerationCaseAttributionCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(
        RenewModerationCaseAttributionCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = await new RenewModerationCaseAttributionCommandValidator().ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return BaseCommandResponse.Validation<Guid>(validation.Errors.Select(error => error.ErrorMessage));
        var result = await attribution.RenewAsync(command.ToRequest(), cancellationToken);
        return result is null
            ? BaseCommandResponse.Failure<Guid>("ModerationCaseAttributionUnavailable")
            : BaseCommandResponse.Success(result.Id);
    }
}

public sealed class WithdrawModerationCaseAttributionCommandHandler(IModerationCaseAttributionRepository attribution)
    : ICommandHandler<WithdrawModerationCaseAttributionCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(
        WithdrawModerationCaseAttributionCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = await new WithdrawModerationCaseAttributionCommandValidator().ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return BaseCommandResponse.Validation<Guid>(validation.Errors.Select(error => error.ErrorMessage));
        var result = await attribution.WithdrawAsync(
            command.SourceId, command.OperationId, command.ExpectedSourceRevision, cancellationToken);
        return result is null
            ? BaseCommandResponse.Failure<Guid>("ModerationCaseAttributionUnavailable")
            : BaseCommandResponse.Success(result.Id);
    }
}

public sealed class AttributeModerationCaseCommandValidator : AbstractValidator<AttributeModerationCaseCommand>
{
    public AttributeModerationCaseCommandValidator()
    {
        RuleFor(command => command.OperationId).Must(IsUuidV7);
        RuleFor(command => command.OccurrenceCaseId).Must(IsUuidV7);
        RuleFor(command => command.ReadGrantId).Must(IsUuidV7);
        RuleFor(command => command.SubjectId).Must(IsUuidV7);
        RuleFor(command => command.ExpectedCaseRevision).InclusiveBetween(1, long.MaxValue - 1);
        RuleFor(command => command.EvidenceCaseRevision).GreaterThan(0)
            .LessThanOrEqualTo(command => command.ExpectedCaseRevision);
        RuleFor(command => command.Basis).IsInEnum();
        RuleFor(command => command.SubjectKind).IsInEnum();
        RuleFor(command => command).Must(command =>
            command.Basis == ModerationResponsibilityBasis.ParticipantInvestigation
                ? command.SubjectKind == ModerationSubjectKind.Human
                    && command.InvestigationSourceId is { } sourceId && IsUuidV7(sourceId)
                    && command.InvestigationSourceRevision == 1
                : command.InvestigationSourceId is null && command.InvestigationSourceRevision is null);
        RuleFor(command => command.ValidUntil).Must(value =>
            value.Offset == TimeSpan.Zero && value != DateTimeOffset.MinValue && value != DateTimeOffset.MaxValue);
    }

    private static bool IsUuidV7(Guid value) => value != Guid.Empty && value.Version == 7 && value.Variant is >= 8 and <= 11;
}

public sealed class RenewModerationCaseAttributionCommandValidator : AbstractValidator<RenewModerationCaseAttributionCommand>
{
    public RenewModerationCaseAttributionCommandValidator()
    {
        RuleFor(command => command.CaseId).Must(IsUuidV7);
        RuleFor(command => command.ExpectedSourceId).Must(IsUuidV7);
        RuleFor(command => command.ExpectedCaseRevision).InclusiveBetween(1, long.MaxValue - 1);
        RuleFor(command => command.ExpectedGeneration).InclusiveBetween(1, long.MaxValue - 1);
        RuleFor(command => command.ExpectedHeadRevision).Equal(command => command.ExpectedGeneration);
        RuleFor(command => command.Qualification).NotNull().SetValidator(new AttributeModerationCaseCommandValidator());
    }

    private static bool IsUuidV7(Guid value) => value != Guid.Empty && value.Version == 7 && value.Variant is >= 8 and <= 11;
}

public sealed class WithdrawModerationCaseAttributionCommandValidator : AbstractValidator<WithdrawModerationCaseAttributionCommand>
{
    public WithdrawModerationCaseAttributionCommandValidator()
    {
        RuleFor(command => command.SourceId).Must(value =>
            value != Guid.Empty && value.Version == 7 && value.Variant is >= 8 and <= 11);
        RuleFor(command => command.OperationId).Must(value =>
            value != Guid.Empty && value.Version == 7 && value.Variant is >= 8 and <= 11);
        RuleFor(command => command.ExpectedSourceRevision).Equal(1);
    }
}
