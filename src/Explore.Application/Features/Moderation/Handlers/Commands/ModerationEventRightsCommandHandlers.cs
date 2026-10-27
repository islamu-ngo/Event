using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Requests.Commands;
using FluentValidation;
using ISLAMU.Moderation.Domain;

namespace Explore.Application.Features.Moderation.Handlers.Commands;

public sealed class RecordModerationEventRightsCommandHandler(IModerationQualificationRepository sources)
    : ICommandHandler<RecordModerationEventRightsCommand, ModerationQualificationResponse>
{
    public async Task<ModerationQualificationResponse> ExecuteAsync(
        RecordModerationEventRightsCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = await new ModerationEventRightsCommandValidator<RecordModerationEventRightsCommand>()
            .ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return ModerationQualificationResponse.Invalid(validation.Errors.Select(error => error.ErrorMessage));
        return ModerationQualificationResponse.FromOutcome(command.OperationId,
            await sources.ExecuteAsync(command.ToMutation(), cancellationToken));
    }
}

public sealed class AdoptModerationEventRightsCommandHandler(IModerationQualificationRepository sources)
    : ICommandHandler<AdoptModerationEventRightsCommand, ModerationQualificationResponse>
{
    public async Task<ModerationQualificationResponse> ExecuteAsync(
        AdoptModerationEventRightsCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = await new ModerationEventRightsCommandValidator<AdoptModerationEventRightsCommand>()
            .ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return ModerationQualificationResponse.Invalid(validation.Errors.Select(error => error.ErrorMessage));
        return ModerationQualificationResponse.FromOutcome(command.OperationId,
            await sources.ExecuteAsync(command.ToMutation(), cancellationToken));
    }
}

public sealed class WithdrawModerationEventRightsCommandHandler(IModerationQualificationRepository sources)
    : ICommandHandler<WithdrawModerationEventRightsCommand, ModerationQualificationResponse>
{
    public async Task<ModerationQualificationResponse> ExecuteAsync(
        WithdrawModerationEventRightsCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = await new ModerationEventRightsCommandValidator<WithdrawModerationEventRightsCommand>()
            .ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return ModerationQualificationResponse.Invalid(validation.Errors.Select(error => error.ErrorMessage));
        return ModerationQualificationResponse.FromOutcome(command.OperationId,
            await sources.ExecuteAsync(command.ToMutation(), cancellationToken));
    }
}

public sealed class ModerationEventRightsCommandValidator<TCommand> : AbstractValidator<TCommand>
    where TCommand : ModerationQualificationCommand
{
    public ModerationEventRightsCommandValidator()
    {
        RuleFor(command => command.OperationId).Must(Uuid);
        RuleFor(command => command.ExpectedQualificationRevision).InclusiveBetween(1, long.MaxValue - 2);
        RuleFor(command => command.Target).Must(scope =>
            scope is not null && scope.Kind is ModerationScopeKind.Instance or ModerationScopeKind.Tenant);
        RuleFor(command => command).Custom((command, validation) =>
        {
            var valid = command switch
            {
                RecordModerationEventRightsCommand source => Uuid(source.SourceId) && Uuid(source.PolicyId)
                    && Uuid(source.PublicationId) && Uuid(source.ProfileId) && Enum.IsDefined(source.Mode)
                    && source.ExpectedPolicyRevision is > 0 and < long.MaxValue && Finite(source.ValidUntil)
                    && Substantive(source),
                AdoptModerationEventRightsCommand source => Uuid(source.SourceId) && Uuid(source.AdoptionId)
                    && source.SourceId != source.AdoptionId && source.ExpectedSourceRevision == 1
                    && source.ExpectedSourceWriterGeneration is > 0 and < long.MaxValue && Finite(source.ValidUntil),
                WithdrawModerationEventRightsCommand source => Uuid(source.AdoptionId) && source.ExpectedAdoptionRevision == 1,
                _ => false
            };
            if (!valid) validation.AddFailure("Event-rights governance requires exact coordinates, finite leases and substantive independent mandates.");
        });
    }

    private static bool Substantive(RecordModerationEventRightsCommand source)
    {
        if (source.Terms is null || source.Ownership is null) return false;
        try
        {
            source.Terms.Validate();
            // The mapping owner is already required to differ from the reviewer. Using that
            // identity checks submitted structure without inventing an extra controller constraint;
            // the writer separately checks the actual authenticated recorder.
            source.Ownership.Validate(source.Ownership.ContinuityMappingOwnerHumanId);
            return true;
        }
        catch (ArgumentException) { return false; }
    }
    private static bool Uuid(Guid id) =>
        id != Guid.Empty && id.Version == 7 && (id.ToByteArray(bigEndian: true)[8] & 0xc0) == 0x80;
    private static bool Finite(DateTimeOffset value) =>
        value.Offset == TimeSpan.Zero && value != DateTimeOffset.MinValue && value != DateTimeOffset.MaxValue;
}
