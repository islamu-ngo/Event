using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Requests.Commands;
using FluentValidation;
using ISLAMU.Moderation.Domain;

namespace Explore.Application.Features.Moderation.Handlers.Commands;

public sealed class RecordModerationChildAgePolicyCommandHandler(IModerationQualificationRepository sources)
    : ICommandHandler<RecordModerationChildAgePolicyCommand, ModerationQualificationResponse>
{
    public async Task<ModerationQualificationResponse> ExecuteAsync(
        RecordModerationChildAgePolicyCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = await new ModerationChildAgePolicyCommandValidator<RecordModerationChildAgePolicyCommand>()
            .ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return ModerationQualificationResponse.Invalid(validation.Errors.Select(error => error.ErrorMessage));
        return ModerationQualificationResponse.FromOutcome(command.OperationId,
            await sources.ExecuteAsync(command.ToMutation(), cancellationToken));
    }
}

public sealed class AdoptModerationChildAgePolicyCommandHandler(IModerationQualificationRepository sources)
    : ICommandHandler<AdoptModerationChildAgePolicyCommand, ModerationQualificationResponse>
{
    public async Task<ModerationQualificationResponse> ExecuteAsync(
        AdoptModerationChildAgePolicyCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = await new ModerationChildAgePolicyCommandValidator<AdoptModerationChildAgePolicyCommand>()
            .ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return ModerationQualificationResponse.Invalid(validation.Errors.Select(error => error.ErrorMessage));
        return ModerationQualificationResponse.FromOutcome(command.OperationId,
            await sources.ExecuteAsync(command.ToMutation(), cancellationToken));
    }
}

public sealed class WithdrawModerationChildAgePolicyCommandHandler(IModerationQualificationRepository sources)
    : ICommandHandler<WithdrawModerationChildAgePolicyCommand, ModerationQualificationResponse>
{
    public async Task<ModerationQualificationResponse> ExecuteAsync(
        WithdrawModerationChildAgePolicyCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = await new ModerationChildAgePolicyCommandValidator<WithdrawModerationChildAgePolicyCommand>()
            .ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return ModerationQualificationResponse.Invalid(validation.Errors.Select(error => error.ErrorMessage));
        return ModerationQualificationResponse.FromOutcome(command.OperationId,
            await sources.ExecuteAsync(command.ToMutation(), cancellationToken));
    }
}

public sealed class ModerationChildAgePolicyCommandValidator<TCommand> : AbstractValidator<TCommand>
    where TCommand : ModerationQualificationCommand
{
    public ModerationChildAgePolicyCommandValidator()
    {
        RuleFor(command => command.OperationId).Must(Uuid);
        RuleFor(command => command.ExpectedQualificationRevision).InclusiveBetween(1, long.MaxValue - 2);
        RuleFor(command => command.Target).Must(scope =>
            scope is not null && scope.Kind is ModerationScopeKind.Instance or ModerationScopeKind.Tenant);
        RuleFor(command => command).Custom((command, validation) =>
        {
            var valid = command switch
            {
                RecordModerationChildAgePolicyCommand source => Uuid(source.SourceId) && Uuid(source.PolicyId)
                    && Uuid(source.PublicationId) && Uuid(source.ProfileId) && Enum.IsDefined(source.Mode)
                    && source.ExpectedPolicyRevision is > 0 and < long.MaxValue && Finite(source.ValidUntil)
                    && Substantive(source),
                AdoptModerationChildAgePolicyCommand source => Uuid(source.SourceId) && Uuid(source.AdoptionId)
                    && source.SourceId != source.AdoptionId && source.ExpectedSourceRevision == 1
                    && source.ExpectedSourceWriterGeneration is > 0 and < long.MaxValue && Finite(source.ValidUntil),
                WithdrawModerationChildAgePolicyCommand source => Uuid(source.AdoptionId) && source.ExpectedAdoptionRevision == 1,
                _ => false
            };
            if (!valid) validation.AddFailure("Child-age governance requires exact coordinates, finite leases and substantive qualified terms.");
        });
    }

    private static bool Substantive(RecordModerationChildAgePolicyCommand source)
    {
        if (source.Terms is null || source.Ownership is null) return false;
        try
        {
            source.Terms.Validate();
            // The writer independently checks the actual authenticated recorder.
            source.Ownership.Validate(source.Ownership.GovernanceControllerHumanId);
            return true;
        }
        catch (ArgumentException) { return false; }
    }
    private static bool Uuid(Guid id) =>
        id != Guid.Empty && id.Version == 7 && (id.ToByteArray(bigEndian: true)[8] & 0xc0) == 0x80;
    private static bool Finite(DateTimeOffset value) =>
        value.Offset == TimeSpan.Zero && value != DateTimeOffset.MinValue && value != DateTimeOffset.MaxValue;
}
