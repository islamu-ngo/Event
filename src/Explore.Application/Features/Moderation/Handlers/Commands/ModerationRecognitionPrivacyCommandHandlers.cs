using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Requests.Commands;
using Explore.Domain;
using FluentValidation;
using ISLAMU.Moderation.Domain;

namespace Explore.Application.Features.Moderation.Handlers.Commands;

public sealed class RecordModerationRecognitionPrivacyCommandHandler(IModerationQualificationRepository sources)
    : ICommandHandler<RecordModerationRecognitionPrivacyCommand, ModerationQualificationResponse>
{
    public async Task<ModerationQualificationResponse> ExecuteAsync(
        RecordModerationRecognitionPrivacyCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = await new ModerationRecognitionPrivacyCommandValidator<RecordModerationRecognitionPrivacyCommand>()
            .ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return ModerationQualificationResponse.Invalid(validation.Errors.Select(error => error.ErrorMessage));
        return ModerationQualificationResponse.FromOutcome(command.OperationId,
            await sources.ExecuteAsync(command.ToMutation(), cancellationToken));
    }
}

public sealed class AdoptModerationRecognitionPrivacyCommandHandler(IModerationQualificationRepository sources)
    : ICommandHandler<AdoptModerationRecognitionPrivacyCommand, ModerationQualificationResponse>
{
    public async Task<ModerationQualificationResponse> ExecuteAsync(
        AdoptModerationRecognitionPrivacyCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = await new ModerationRecognitionPrivacyCommandValidator<AdoptModerationRecognitionPrivacyCommand>()
            .ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return ModerationQualificationResponse.Invalid(validation.Errors.Select(error => error.ErrorMessage));
        return ModerationQualificationResponse.FromOutcome(command.OperationId,
            await sources.ExecuteAsync(command.ToMutation(), cancellationToken));
    }
}

public sealed class WithdrawModerationRecognitionPrivacyCommandHandler(IModerationQualificationRepository sources)
    : ICommandHandler<WithdrawModerationRecognitionPrivacyCommand, ModerationQualificationResponse>
{
    public async Task<ModerationQualificationResponse> ExecuteAsync(
        WithdrawModerationRecognitionPrivacyCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = await new ModerationRecognitionPrivacyCommandValidator<WithdrawModerationRecognitionPrivacyCommand>()
            .ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return ModerationQualificationResponse.Invalid(validation.Errors.Select(error => error.ErrorMessage));
        return ModerationQualificationResponse.FromOutcome(command.OperationId,
            await sources.ExecuteAsync(command.ToMutation(), cancellationToken));
    }
}

public sealed class ModerationRecognitionPrivacyCommandValidator<TCommand> : AbstractValidator<TCommand>
    where TCommand : ModerationQualificationCommand
{
    public ModerationRecognitionPrivacyCommandValidator()
    {
        RuleFor(command => command.OperationId).Must(Uuid);
        RuleFor(command => command.ExpectedQualificationRevision).InclusiveBetween(1, long.MaxValue - 2);
        RuleFor(command => command.Target).Must(scope =>
            scope is not null && scope.Kind is ModerationScopeKind.Instance or ModerationScopeKind.Tenant);
        RuleFor(command => command).Custom((command, validation) =>
        {
            var valid = command switch
            {
                RecordModerationRecognitionPrivacyCommand source => Uuid(source.SourceId) && Uuid(source.PolicyId)
                    && Uuid(source.PublicationId) && Uuid(source.ProfileId) && Enum.IsDefined(source.Mode)
                    && source.ExpectedPolicyRevision is > 0 and < long.MaxValue && Finite(source.ValidUntil)
                    && Substantive(source),
                AdoptModerationRecognitionPrivacyCommand source => Uuid(source.SourceId) && Uuid(source.AdoptionId)
                    && source.SourceId != source.AdoptionId && source.ExpectedSourceRevision == 1
                    && source.ExpectedSourceWriterGeneration is > 0 and < long.MaxValue && Finite(source.ValidUntil),
                WithdrawModerationRecognitionPrivacyCommand source => Uuid(source.AdoptionId) && source.ExpectedAdoptionRevision == 1,
                _ => false
            };
            if (!valid) validation.AddFailure("Recognition privacy requires exact coordinates, finite leases and substantive qualified terms.");
        });
    }

    private static bool Substantive(RecordModerationRecognitionPrivacyCommand source)
    {
        if (source.Terms is null || source.Ownership is null || source.Retention.IsDefaultOrEmpty
            || source.Retention.Any(row => row is null)
            || !source.Retention.Select(row => row.RecordClass).SequenceEqual(Enum.GetValues<ModerationRecognitionPrivacyRecordClass>()))
            return false;
        try
        {
            source.Terms.Validate();
            // The real authenticated recorder is independently checked by the native writer.
            source.Ownership.Validate(source.Ownership.GovernanceControllerHumanId);
            foreach (var row in source.Retention) row.Validate();
            return true;
        }
        catch (ArgumentException) { return false; }
    }
    private static bool Uuid(Guid id) =>
        id != Guid.Empty && id.Version == 7 && (id.ToByteArray(bigEndian: true)[8] & 0xc0) == 0x80;
    private static bool Finite(DateTimeOffset value) =>
        value.Offset == TimeSpan.Zero && value != DateTimeOffset.MinValue && value != DateTimeOffset.MaxValue;
}
