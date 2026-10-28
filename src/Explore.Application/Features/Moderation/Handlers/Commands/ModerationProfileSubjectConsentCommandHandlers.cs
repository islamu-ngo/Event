using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Requests.Commands;
using Explore.Application.Responses;
using FluentValidation;

namespace Explore.Application.Features.Moderation.Handlers.Commands;

public sealed class GrantModerationProfileSubjectConsentCommandHandler(IModerationProfileSubjectConsentOwner owner)
    : ICommandHandler<GrantModerationProfileSubjectConsentCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(
        GrantModerationProfileSubjectConsentCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();
        var validation = await new ModerationProfileSubjectConsentCommandValidator<GrantModerationProfileSubjectConsentCommand>()
            .ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return BaseCommandResponse.Validation<Guid>(validation.Errors.Select(error => error.ErrorMessage));
        var consent = await owner.GrantAsync(command.ToGrant(), cancellationToken);
        return consent is null ? BaseCommandResponse.NotFound<Guid>() : BaseCommandResponse.Success(consent.Id);
    }
}

public sealed class WithdrawModerationProfileSubjectConsentCommandHandler(IModerationProfileSubjectConsentOwner owner)
    : ICommandHandler<WithdrawModerationProfileSubjectConsentCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(
        WithdrawModerationProfileSubjectConsentCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();
        var validation = await new ModerationProfileSubjectConsentCommandValidator<WithdrawModerationProfileSubjectConsentCommand>()
            .ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return BaseCommandResponse.Validation<Guid>(validation.Errors.Select(error => error.ErrorMessage));
        var consent = await owner.WithdrawAsync(command.ToWithdrawal(), cancellationToken);
        return consent is null ? BaseCommandResponse.NotFound<Guid>() : BaseCommandResponse.Success(consent.Id);
    }
}

public sealed class ModerationProfileSubjectConsentCommandValidator<TCommand> : AbstractValidator<TCommand>
    where TCommand : ICommand<BaseCommandResponse<Guid>>
{
    public ModerationProfileSubjectConsentCommandValidator()
    {
        RuleFor(command => command).Custom((command, validation) =>
        {
            var valid = command switch
            {
                GrantModerationProfileSubjectConsentCommand grant =>
                    grant.Assessment is { Original: { } original } assessment
                    && ModerationProfileCollectionAssessmentCommandValidator<TCommand>.Original(original)
                    && Uuid(assessment.SourceId) && Uuid(assessment.AdoptionId)
                    && assessment.SourceId != assessment.AdoptionId
                    && !ModerationProfileCollectionAssessmentCommandValidator<TCommand>.AliasesOriginal(assessment.SourceId, original)
                    && !ModerationProfileCollectionAssessmentCommandValidator<TCommand>.AliasesOriginal(assessment.AdoptionId, original)
                    && assessment.ExpectedSourceRevision == 1 && assessment.ExpectedAdoptionRevision == 1
                    && assessment.ExpectedSourceGeneration is > 0 and < long.MaxValue - 1
                    && assessment.ExpectedAdoptionGeneration > assessment.ExpectedSourceGeneration
                    && assessment.ExpectedAdoptionGeneration < long.MaxValue
                    && Uuid(grant.ConsentId) && grant.ConsentId != assessment.SourceId && grant.ConsentId != assessment.AdoptionId
                    && !ModerationProfileCollectionAssessmentCommandValidator<TCommand>.AliasesOriginal(grant.ConsentId, original)
                    && grant.ExpectedGeneration is >= 0 and < long.MaxValue - 1 && Finite(grant.ValidUntil),
                WithdrawModerationProfileSubjectConsentCommand withdrawal =>
                    Uuid(withdrawal.ConsentId) && withdrawal.ExpectedConsentRevision == 1
                    && withdrawal.ExpectedConsentGeneration is > 0 and < long.MaxValue
                    && withdrawal.ExpectedGeneration >= withdrawal.ExpectedConsentGeneration
                    && withdrawal.ExpectedGeneration < long.MaxValue,
                _ => false
            };
            if (!valid)
                validation.AddFailure("Subject consent requires exact original assessment coordinates, separate consent-slot generations and a finite UTC grant window.");
        });
    }

    private static bool Uuid(Guid id) =>
        id != Guid.Empty && id.Version == 7 && (id.ToByteArray(bigEndian: true)[8] & 0xc0) == 0x80;

    private static bool Finite(DateTimeOffset value) =>
        value.Offset == TimeSpan.Zero && value != DateTimeOffset.MinValue && value != DateTimeOffset.MaxValue;
}
