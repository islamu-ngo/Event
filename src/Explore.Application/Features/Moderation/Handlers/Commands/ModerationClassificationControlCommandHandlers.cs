using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Requests.Commands;
using Explore.Domain;
using FluentValidation;
using ISLAMU.Moderation.Domain;

namespace Explore.Application.Features.Moderation.Handlers.Commands;

public sealed class RecordModerationClassificationControlSelectionCommandHandler(IModerationQualificationRepository sources)
    : ICommandHandler<RecordModerationClassificationControlSelectionCommand, ModerationQualificationResponse>
{
    public async Task<ModerationQualificationResponse> ExecuteAsync(
        RecordModerationClassificationControlSelectionCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = await new ModerationClassificationControlCommandValidator<RecordModerationClassificationControlSelectionCommand>()
            .ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return ModerationQualificationResponse.Invalid(validation.Errors.Select(error => error.ErrorMessage));
        return ModerationQualificationResponse.FromOutcome(command.OperationId,
            await sources.ExecuteAsync(command.ToMutation(), cancellationToken));
    }
}

public sealed class AdoptModerationClassificationEventTargetCommandHandler(IModerationQualificationRepository sources)
    : ICommandHandler<AdoptModerationClassificationEventTargetCommand, ModerationQualificationResponse>
{
    public async Task<ModerationQualificationResponse> ExecuteAsync(
        AdoptModerationClassificationEventTargetCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = await new ModerationClassificationControlCommandValidator<AdoptModerationClassificationEventTargetCommand>()
            .ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return ModerationQualificationResponse.Invalid(validation.Errors.Select(error => error.ErrorMessage));
        return ModerationQualificationResponse.FromOutcome(command.OperationId,
            await sources.ExecuteAsync(command.ToMutation(), cancellationToken));
    }
}

public sealed class RevokeModerationClassificationEventTargetCommandHandler(IModerationQualificationRepository sources)
    : ICommandHandler<RevokeModerationClassificationEventTargetCommand, ModerationQualificationResponse>
{
    public async Task<ModerationQualificationResponse> ExecuteAsync(
        RevokeModerationClassificationEventTargetCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = await new ModerationClassificationControlCommandValidator<RevokeModerationClassificationEventTargetCommand>()
            .ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return ModerationQualificationResponse.Invalid(validation.Errors.Select(error => error.ErrorMessage));
        return ModerationQualificationResponse.FromOutcome(command.OperationId,
            await sources.ExecuteAsync(command.ToMutation(), cancellationToken));
    }
}

public sealed class ModerationClassificationControlCommandValidator<TCommand> : AbstractValidator<TCommand>
    where TCommand : ModerationQualificationCommand
{
    public ModerationClassificationControlCommandValidator()
    {
        RuleFor(command => command.OperationId).Must(Uuid);
        RuleFor(command => command.ExpectedQualificationRevision).InclusiveBetween(1, long.MaxValue - 2);
        RuleFor(command => command.Target).Must(scope => scope is not null && scope.Kind == ModerationScopeKind.Event);
        RuleFor(command => command).Custom((command, validation) =>
        {
            var valid = command switch
            {
                RecordModerationClassificationControlSelectionCommand source => Uuid(source.SelectionId)
                    && Uuid(source.MaterialSourceId) && Uuid(source.MaterialReviewId) && Uuid(source.EventGenerationId)
                    && source.ExpectedMaterialSourceRevision == 1 && source.ExpectedMaterialReviewRevision == 1
                    && Positive(source.ExpectedMaterialWriterGeneration) && Positive(source.ExpectedReviewWriterGeneration)
                    && Positive(source.ExpectedEventGenerationRevision) && Positive(source.ExpectedEventWriterGeneration)
                    && Finite(source.ValidUntil) && source.Target is { Kind: ModerationScopeKind.Event }
                    && source.Contribution is { } contribution
                    && contribution.Id == source.SelectionId && contribution.Scope == source.Target
                    && contribution.SubjectKind == ModerationSubjectKind.Content && contribution.SubjectId == source.Target.Id
                    && contribution.EventWindowEnd is null && !contribution.Effects.IsDefaultOrEmpty
                    && !contribution.Consequences.IsDefaultOrEmpty
                    && contribution.Effects.Length + contribution.Consequences.Length <= 64,
                AdoptModerationClassificationEventTargetCommand source => Uuid(source.AdoptionId)
                    && Uuid(source.SelectionId) && Uuid(source.EventGenerationId) && source.ExpectedSelectionRevision == 1
                    && source.BindingOrdinal is >= 0 and < 64 && Positive(source.ExpectedEventGenerationRevision)
                    && Finite(source.ValidUntil),
                RevokeModerationClassificationEventTargetCommand source => Uuid(source.AdoptionId)
                    && source.ExpectedAdoptionRevision == 1 && ModerationChildAgePolicyTerms.Grounds(source.Grounds),
                _ => false
            };
            if (!valid)
                validation.AddFailure("Classification control requires exact independent material provenance and typed Event partitions.");
        });
    }

    private static bool Uuid(Guid id) => id != Guid.Empty && id.Version == 7;
    private static bool Positive(long revision) => revision is > 0 and < long.MaxValue;
    private static bool Finite(DateTimeOffset at) =>
        at.Offset == TimeSpan.Zero && at != DateTimeOffset.MinValue && at != DateTimeOffset.MaxValue;
}
