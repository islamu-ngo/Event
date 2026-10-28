using Explore.Application.Contracts.Moderation;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.Moderation.Requests.Commands;
using Explore.Application.Responses;
using FluentValidation;

namespace Explore.Application.Features.Moderation.Handlers.Commands;

public sealed class GrantModerationAdjudicatedCaseReadPurposeCommandHandler(
    IModerationAdjudicatedCaseReadGrantRepository repository) :
    ICommandHandler<GrantModerationAdjudicatedCaseReadPurposeCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(
        GrantModerationAdjudicatedCaseReadPurposeCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = await new GrantModerationAdjudicatedCaseReadPurposeCommandValidator()
            .ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return BaseCommandResponse.Validation<Guid>(validation.Errors.Select(error => error.ErrorMessage));
        var grant = await repository.GrantAsync(new(
            command.OperationId, command.CaseId, command.ExpectedCaseRevision, command.GranteeUserId,
            command.Original, command.ValidUntil), cancellationToken);
        return grant is null
            ? BaseCommandResponse.Failure<Guid>("moderation_adjudicated_case_read_grant_unavailable")
            : BaseCommandResponse.Success(grant.Id);
    }
}

public sealed class RevokeModerationAdjudicatedCaseReadPurposeCommandHandler(
    IModerationAdjudicatedCaseReadGrantRepository repository) :
    ICommandHandler<RevokeModerationAdjudicatedCaseReadPurposeCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(
        RevokeModerationAdjudicatedCaseReadPurposeCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = await new RevokeModerationAdjudicatedCaseReadPurposeCommandValidator()
            .ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return BaseCommandResponse.Validation<Guid>(validation.Errors.Select(error => error.ErrorMessage));
        var grant = await repository.RevokeAsync(
            command.GrantId, command.OperationId, command.ExpectedGrantRevision, cancellationToken);
        return grant is null
            ? BaseCommandResponse.Failure<Guid>("moderation_adjudicated_case_read_grant_unavailable")
            : BaseCommandResponse.Success(grant.Id);
    }
}

public sealed class GrantModerationAdjudicatedCaseReadPurposeCommandValidator :
    AbstractValidator<GrantModerationAdjudicatedCaseReadPurposeCommand>
{
    public GrantModerationAdjudicatedCaseReadPurposeCommandValidator()
    {
        RuleFor(command => command.OperationId).Must(value =>
            value != Guid.Empty && value.Version == 7 && value.Variant is >= 8 and <= 11);
        RuleFor(command => command.CaseId).Must(value =>
            value != Guid.Empty && value.Version == 7 && value.Variant is >= 8 and <= 11);
        RuleFor(command => command.GranteeUserId).Must(value =>
            value != Guid.Empty && value.Version == 7 && value.Variant is >= 8 and <= 11);
        RuleFor(command => command.ExpectedCaseRevision).GreaterThan(0).LessThan(long.MaxValue);
        RuleFor(command => command.Original).NotNull();
        When(command => command.Original is not null, () =>
        {
            RuleFor(command => command.Original.EffectId).Must(value =>
                value != Guid.Empty && value.Version == 7 && value.Variant is >= 8 and <= 11);
            RuleFor(command => command.Original.SnapshotHash).Must(IsCanonicalHash);
            RuleFor(command => command.Original.EffectBindingHash).Must(IsCanonicalHash);
        });
        RuleFor(command => command.ValidUntil).Must(value =>
            value != DateTimeOffset.MinValue && value != DateTimeOffset.MaxValue && value.Offset == TimeSpan.Zero
            && value.UtcTicks % TimeSpan.TicksPerMicrosecond == 0);
    }

    private static bool IsCanonicalHash(string? value) =>
        value is { Length: 64 } && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
}

public sealed class RevokeModerationAdjudicatedCaseReadPurposeCommandValidator :
    AbstractValidator<RevokeModerationAdjudicatedCaseReadPurposeCommand>
{
    public RevokeModerationAdjudicatedCaseReadPurposeCommandValidator()
    {
        RuleFor(command => command.OperationId).Must(value =>
            value != Guid.Empty && value.Version == 7 && value.Variant is >= 8 and <= 11);
        RuleFor(command => command.GrantId).Must(value =>
            value != Guid.Empty && value.Version == 7 && value.Variant is >= 8 and <= 11);
        RuleFor(command => command.ExpectedGrantRevision).Equal(1);
    }
}
