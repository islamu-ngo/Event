using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Requests.Commands;
using Explore.Domain;
using FluentValidation;
using ISLAMU.Moderation.Domain;

namespace Explore.Application.Features.Moderation.Handlers.Commands;

public sealed class RecordModerationConstitutionCommandHandler(IModerationQualificationRepository sources)
    : ICommandHandler<RecordModerationConstitutionCommand, ModerationQualificationResponse>
{
    public async Task<ModerationQualificationResponse> ExecuteAsync(RecordModerationConstitutionCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = await new ModerationConstitutionCommandValidator<RecordModerationConstitutionCommand>().ValidateAsync(command, cancellationToken);
        if (!validation.IsValid) return ModerationQualificationResponse.Invalid(validation.Errors.Select(error => error.ErrorMessage));
        return ModerationQualificationResponse.FromOutcome(command.OperationId, await sources.ExecuteAsync(command.ToMutation(), cancellationToken));
    }
}

public sealed class AdoptModerationConstitutionCommandHandler(IModerationQualificationRepository sources)
    : ICommandHandler<AdoptModerationConstitutionCommand, ModerationQualificationResponse>
{
    public async Task<ModerationQualificationResponse> ExecuteAsync(AdoptModerationConstitutionCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = await new ModerationConstitutionCommandValidator<AdoptModerationConstitutionCommand>().ValidateAsync(command, cancellationToken);
        if (!validation.IsValid) return ModerationQualificationResponse.Invalid(validation.Errors.Select(error => error.ErrorMessage));
        return ModerationQualificationResponse.FromOutcome(command.OperationId, await sources.ExecuteAsync(command.ToMutation(), cancellationToken));
    }
}

public sealed class WithdrawModerationConstitutionCommandHandler(IModerationQualificationRepository sources)
    : ICommandHandler<WithdrawModerationConstitutionCommand, ModerationQualificationResponse>
{
    public async Task<ModerationQualificationResponse> ExecuteAsync(WithdrawModerationConstitutionCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = await new ModerationConstitutionCommandValidator<WithdrawModerationConstitutionCommand>().ValidateAsync(command, cancellationToken);
        if (!validation.IsValid) return ModerationQualificationResponse.Invalid(validation.Errors.Select(error => error.ErrorMessage));
        return ModerationQualificationResponse.FromOutcome(command.OperationId, await sources.ExecuteAsync(command.ToMutation(), cancellationToken));
    }
}

public sealed class RecordModerationConstitutionAvailabilityCommandHandler(IModerationQualificationRepository sources)
    : ICommandHandler<RecordModerationConstitutionAvailabilityCommand, ModerationQualificationResponse>
{
    public async Task<ModerationQualificationResponse> ExecuteAsync(RecordModerationConstitutionAvailabilityCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = await new ModerationConstitutionCommandValidator<RecordModerationConstitutionAvailabilityCommand>().ValidateAsync(command, cancellationToken);
        if (!validation.IsValid) return ModerationQualificationResponse.Invalid(validation.Errors.Select(error => error.ErrorMessage));
        return ModerationQualificationResponse.FromOutcome(command.OperationId, await sources.ExecuteAsync(command.ToMutation(), cancellationToken));
    }
}

public sealed class WithdrawModerationConstitutionAvailabilityCommandHandler(IModerationQualificationRepository sources)
    : ICommandHandler<WithdrawModerationConstitutionAvailabilityCommand, ModerationQualificationResponse>
{
    public async Task<ModerationQualificationResponse> ExecuteAsync(WithdrawModerationConstitutionAvailabilityCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = await new ModerationConstitutionCommandValidator<WithdrawModerationConstitutionAvailabilityCommand>().ValidateAsync(command, cancellationToken);
        if (!validation.IsValid) return ModerationQualificationResponse.Invalid(validation.Errors.Select(error => error.ErrorMessage));
        return ModerationQualificationResponse.FromOutcome(command.OperationId, await sources.ExecuteAsync(command.ToMutation(), cancellationToken));
    }
}

public sealed class ModerationConstitutionCommandValidator<TCommand> : AbstractValidator<TCommand> where TCommand : ModerationQualificationCommand
{
    public ModerationConstitutionCommandValidator()
    {
        RuleFor(command => command.OperationId).Must(Uuid);
        RuleFor(command => command.ExpectedQualificationRevision).InclusiveBetween(1, long.MaxValue - 2);
        RuleFor(command => command.Target).Must(scope => scope is not null && scope.Kind is ModerationScopeKind.Instance or ModerationScopeKind.Tenant);
        RuleFor(command => command).Custom((command, validation) =>
        {
            var valid = command switch
            {
                RecordModerationConstitutionCommand source => Uuid(source.SourceId) && Uuid(source.PolicyId)
                    && Uuid(source.PublicationId) && Uuid(source.ProfileId) && Enum.IsDefined(source.Mode)
                    && source.ExpectedPolicyRevision is > 0 and < long.MaxValue && Finite(source.ValidUntil)
                    && Original(source.OriginalEffect)
                    && source.Terms is not null && !source.Reviewers.IsDefaultOrEmpty && source.Reviewers.Length <= 32,
                AdoptModerationConstitutionCommand source => Uuid(source.SourceId) && Uuid(source.AdoptionId)
                    && source.SourceId != source.AdoptionId && source.ExpectedSourceRevision == 1
                    && source.ExpectedSourceWriterGeneration is > 0 and < long.MaxValue && Finite(source.ValidUntil)
                    && Original(source.OriginalEffect),
                WithdrawModerationConstitutionCommand source => Uuid(source.AdoptionId) && source.ExpectedAdoptionRevision == 1,
                RecordModerationConstitutionAvailabilityCommand source => Uuid(source.AvailabilityId)
                    && source.MaximumCaseload > 0 && Finite(source.ValidUntil),
                WithdrawModerationConstitutionAvailabilityCommand source => Uuid(source.AvailabilityId) && source.ExpectedAvailabilityRevision == 1,
                _ => false
            };
            if (valid && command is RecordModerationConstitutionCommand record)
            {
                try
                {
                    record.Terms.Validate();
                    foreach (var reviewer in record.Reviewers)
                    {
                        if (reviewer is null) { valid = false; break; }
                        reviewer.Validate();
                    }
                }
                catch (ArgumentException) { valid = false; }
            }
            if (!valid) validation.AddFailure("Constitution governance requires substantive terms, exact native coordinates and finite UTC validity.");
        });
    }
    private static bool Uuid(Guid id) => id != Guid.Empty && id.Version == 7;
    private static bool Original(ModerationOriginalEffectAnchor? anchor) => anchor is null
        || Uuid(anchor.EffectId) && anchor.EffectId.Variant is >= 8 and <= 11
            && ModerationDeploymentSource.Digest(anchor.SnapshotHash)
            && ModerationDeploymentSource.Digest(anchor.EffectBindingHash);
    private static bool Finite(DateTimeOffset time) => time.Offset == TimeSpan.Zero && time != DateTimeOffset.MinValue && time != DateTimeOffset.MaxValue;
}
