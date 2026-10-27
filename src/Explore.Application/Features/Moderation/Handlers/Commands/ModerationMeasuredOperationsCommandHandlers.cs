using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Requests.Commands;
using Explore.Domain;
using FluentValidation;
using ISLAMU.Moderation.Domain;

namespace Explore.Application.Features.Moderation.Handlers.Commands;

public sealed class RecordModerationMeasuredOperationsCommandHandler(IModerationQualificationRepository sources)
    : ICommandHandler<RecordModerationMeasuredOperationsCommand, ModerationQualificationResponse>
{
    public async Task<ModerationQualificationResponse> ExecuteAsync(
        RecordModerationMeasuredOperationsCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = await new ModerationMeasuredOperationsCommandValidator<RecordModerationMeasuredOperationsCommand>()
            .ValidateAsync(command, cancellationToken);
        if (!validation.IsValid) return ModerationQualificationResponse.Invalid(validation.Errors.Select(error => error.ErrorMessage));
        return ModerationQualificationResponse.FromOutcome(command.OperationId, await sources.ExecuteAsync(command.ToMutation(), cancellationToken));
    }
}

public sealed class AcceptModerationMeasuredOperationsCommandHandler(IModerationQualificationRepository sources)
    : ICommandHandler<AcceptModerationMeasuredOperationsCommand, ModerationQualificationResponse>
{
    public async Task<ModerationQualificationResponse> ExecuteAsync(
        AcceptModerationMeasuredOperationsCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = await new ModerationMeasuredOperationsCommandValidator<AcceptModerationMeasuredOperationsCommand>()
            .ValidateAsync(command, cancellationToken);
        if (!validation.IsValid) return ModerationQualificationResponse.Invalid(validation.Errors.Select(error => error.ErrorMessage));
        return ModerationQualificationResponse.FromOutcome(command.OperationId, await sources.ExecuteAsync(command.ToMutation(), cancellationToken));
    }
}

public sealed class WithdrawModerationMeasuredOperationsCommandHandler(IModerationQualificationRepository sources)
    : ICommandHandler<WithdrawModerationMeasuredOperationsCommand, ModerationQualificationResponse>
{
    public async Task<ModerationQualificationResponse> ExecuteAsync(
        WithdrawModerationMeasuredOperationsCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = await new ModerationMeasuredOperationsCommandValidator<WithdrawModerationMeasuredOperationsCommand>()
            .ValidateAsync(command, cancellationToken);
        if (!validation.IsValid) return ModerationQualificationResponse.Invalid(validation.Errors.Select(error => error.ErrorMessage));
        return ModerationQualificationResponse.FromOutcome(command.OperationId, await sources.ExecuteAsync(command.ToMutation(), cancellationToken));
    }
}

public sealed class ModerationMeasuredOperationsCommandValidator<TCommand> : AbstractValidator<TCommand>
    where TCommand : ModerationQualificationCommand
{
    public ModerationMeasuredOperationsCommandValidator()
    {
        RuleFor(command => command.OperationId).Must(Uuid);
        RuleFor(command => command.ExpectedQualificationRevision).InclusiveBetween(1, long.MaxValue - 2);
        RuleFor(command => command.Target).Must(scope => scope is not null
            && scope.Kind is ModerationScopeKind.Instance or ModerationScopeKind.Tenant);
        RuleFor(command => command).Custom((command, validation) =>
        {
            var valid = command switch
            {
                RecordModerationMeasuredOperationsCommand source => Uuid(source.SourceId) && Uuid(source.ProfileId)
                    && source.Binding is { } binding && binding.Jurisdiction == source.Target
                    && Uuid(binding.PolicyId) && Uuid(binding.PublicationId) && binding.Rule is not null
                    && binding.PolicyRevision is > 0 and < long.MaxValue && Enum.IsDefined(binding.Mode)
                    && Enum.IsDefined(binding.Capability) && source.ReviewerHumanId != Guid.Empty
                    && source.ReviewerControllerHumanId != Guid.Empty && source.RecorderControllerHumanId != Guid.Empty
                    && source.ReviewerControllerHumanId != source.RecorderControllerHumanId
                    && source.ReviewerHumanId != source.RecorderControllerHumanId && Finite(source.ValidUntil)
                    && Original(source.OriginalEffect),
                AcceptModerationMeasuredOperationsCommand source => Uuid(source.SourceId) && Uuid(source.AcceptanceId)
                    && source.SourceId != source.AcceptanceId && source.ExpectedSourceRevision == 1
                    && source.ExpectedSourceWriterGeneration is > 0 and < long.MaxValue && Finite(source.ValidUntil)
                    && Original(source.OriginalEffect),
                WithdrawModerationMeasuredOperationsCommand source => Uuid(source.AcceptanceId) && source.ExpectedAcceptanceRevision == 1,
                _ => false
            };
            if (!valid) validation.AddFailure("Measured operations require exact native binding, independent review and finite observations.");
        });
    }
    private static bool Uuid(Guid id) => id != Guid.Empty && id.Version == 7 && (id.ToByteArray(bigEndian: true)[8] & 0xc0) == 0x80;
    private static bool Original(ModerationOriginalEffectAnchor? anchor) => anchor is null
        || Uuid(anchor.EffectId) && ModerationDeploymentSource.Digest(anchor.SnapshotHash)
            && ModerationDeploymentSource.Digest(anchor.EffectBindingHash);
    private static bool Finite(DateTimeOffset value) =>
        value.Offset == TimeSpan.Zero && value != DateTimeOffset.MinValue && value != DateTimeOffset.MaxValue;
}
