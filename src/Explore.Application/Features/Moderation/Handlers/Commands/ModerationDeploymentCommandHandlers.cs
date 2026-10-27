using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Requests.Commands;
using FluentValidation;
using ISLAMU.Moderation.Domain;

namespace Explore.Application.Features.Moderation.Handlers.Commands;

public sealed class RecordModerationDeploymentCommandHandler(IModerationQualificationRepository sources)
    : ICommandHandler<RecordModerationDeploymentCommand, ModerationQualificationResponse>
{
    public async Task<ModerationQualificationResponse> ExecuteAsync(
        RecordModerationDeploymentCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = await new ModerationDeploymentCommandValidator<RecordModerationDeploymentCommand>()
            .ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return ModerationQualificationResponse.Invalid(validation.Errors.Select(error => error.ErrorMessage));
        return ModerationQualificationResponse.FromOutcome(command.OperationId,
            await sources.ExecuteAsync(command.ToMutation(), cancellationToken));
    }
}

public sealed class AdoptModerationDeploymentCommandHandler(IModerationQualificationRepository sources)
    : ICommandHandler<AdoptModerationDeploymentCommand, ModerationQualificationResponse>
{
    public async Task<ModerationQualificationResponse> ExecuteAsync(
        AdoptModerationDeploymentCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = await new ModerationDeploymentCommandValidator<AdoptModerationDeploymentCommand>()
            .ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return ModerationQualificationResponse.Invalid(validation.Errors.Select(error => error.ErrorMessage));
        return ModerationQualificationResponse.FromOutcome(command.OperationId,
            await sources.ExecuteAsync(command.ToMutation(), cancellationToken));
    }
}

public sealed class WithdrawModerationDeploymentCommandHandler(IModerationQualificationRepository sources)
    : ICommandHandler<WithdrawModerationDeploymentCommand, ModerationQualificationResponse>
{
    public async Task<ModerationQualificationResponse> ExecuteAsync(
        WithdrawModerationDeploymentCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = await new ModerationDeploymentCommandValidator<WithdrawModerationDeploymentCommand>()
            .ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return ModerationQualificationResponse.Invalid(validation.Errors.Select(error => error.ErrorMessage));
        return ModerationQualificationResponse.FromOutcome(command.OperationId,
            await sources.ExecuteAsync(command.ToMutation(), cancellationToken));
    }
}

/// <summary>Authorable coordinates only; the native owner derives all database, artifact and authority facts.</summary>
public sealed class ModerationDeploymentCommandValidator<TCommand> : AbstractValidator<TCommand>
    where TCommand : ModerationQualificationCommand
{
    public ModerationDeploymentCommandValidator()
    {
        RuleFor(command => command.OperationId).Must(Uuid);
        RuleFor(command => command.ExpectedQualificationRevision).InclusiveBetween(1, long.MaxValue - 2);
        RuleFor(command => command.Target).Must(scope =>
            scope is not null && scope.Kind is ModerationScopeKind.Instance or ModerationScopeKind.Tenant);
        RuleFor(command => command).Custom((command, validation) =>
        {
            var valid = command switch
            {
                RecordModerationDeploymentCommand source => Uuid(source.SourceId) && Uuid(source.PolicyId)
                    && Uuid(source.PublicationId) && Uuid(source.ProfileId) && Enum.IsDefined(source.Mode)
                    && source.ExpectedPolicyRevision is > 0 and < long.MaxValue && Finite(source.ValidUntil),
                AdoptModerationDeploymentCommand source => Uuid(source.SourceId) && Uuid(source.AdoptionId)
                    && source.SourceId != source.AdoptionId && source.ExpectedSourceRevision == 1
                    && source.ExpectedSourceWriterGeneration is > 0 and < long.MaxValue && Finite(source.ValidUntil),
                WithdrawModerationDeploymentCommand source => Uuid(source.AdoptionId) && source.ExpectedAdoptionRevision == 1,
                _ => false
            };
            if (!valid) validation.AddFailure("Deployment requires exact native coordinates and a finite UTC validity interval.");
        });
    }

    private static bool Uuid(Guid id) => id != Guid.Empty && id.Version == 7;
    private static bool Finite(DateTimeOffset time) =>
        time.Offset == TimeSpan.Zero && time != DateTimeOffset.MinValue && time != DateTimeOffset.MaxValue;
}
