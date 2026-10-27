using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Requests.Commands;
using FluentValidation;
using ISLAMU.Moderation.Domain;

namespace Explore.Application.Features.Moderation.Handlers.Commands;

public sealed class RecordModerationProfileCollectionProcedureCommandHandler(IModerationQualificationRepository sources)
    : ICommandHandler<RecordModerationProfileCollectionProcedureCommand, ModerationQualificationResponse>
{
    public async Task<ModerationQualificationResponse> ExecuteAsync(
        RecordModerationProfileCollectionProcedureCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = await new ModerationProfileCollectionProcedureCommandValidator<RecordModerationProfileCollectionProcedureCommand>()
            .ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return ModerationQualificationResponse.Invalid(validation.Errors.Select(error => error.ErrorMessage));
        return ModerationQualificationResponse.FromOutcome(command.OperationId,
            await sources.ExecuteAsync(command.ToMutation(), cancellationToken));
    }
}

public sealed class AdoptModerationProfileCollectionProcedureCommandHandler(IModerationQualificationRepository sources)
    : ICommandHandler<AdoptModerationProfileCollectionProcedureCommand, ModerationQualificationResponse>
{
    public async Task<ModerationQualificationResponse> ExecuteAsync(
        AdoptModerationProfileCollectionProcedureCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = await new ModerationProfileCollectionProcedureCommandValidator<AdoptModerationProfileCollectionProcedureCommand>()
            .ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return ModerationQualificationResponse.Invalid(validation.Errors.Select(error => error.ErrorMessage));
        return ModerationQualificationResponse.FromOutcome(command.OperationId,
            await sources.ExecuteAsync(command.ToMutation(), cancellationToken));
    }
}

public sealed class WithdrawModerationProfileCollectionProcedureCommandHandler(IModerationQualificationRepository sources)
    : ICommandHandler<WithdrawModerationProfileCollectionProcedureCommand, ModerationQualificationResponse>
{
    public async Task<ModerationQualificationResponse> ExecuteAsync(
        WithdrawModerationProfileCollectionProcedureCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = await new ModerationProfileCollectionProcedureCommandValidator<WithdrawModerationProfileCollectionProcedureCommand>()
            .ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return ModerationQualificationResponse.Invalid(validation.Errors.Select(error => error.ErrorMessage));
        return ModerationQualificationResponse.FromOutcome(command.OperationId,
            await sources.ExecuteAsync(command.ToMutation(), cancellationToken));
    }
}

public sealed class ModerationProfileCollectionProcedureCommandValidator<TCommand> : AbstractValidator<TCommand>
    where TCommand : ModerationQualificationCommand
{
    public ModerationProfileCollectionProcedureCommandValidator()
    {
        RuleFor(command => command.OperationId).Must(Uuid);
        RuleFor(command => command.ExpectedQualificationRevision).InclusiveBetween(1, long.MaxValue - 2);
        RuleFor(command => command.Target).Must(scope =>
            scope is not null && scope.Kind is ModerationScopeKind.Instance or ModerationScopeKind.Tenant);
        RuleFor(command => command).Custom((command, validation) =>
        {
            var valid = command switch
            {
                RecordModerationProfileCollectionProcedureCommand source =>
                    Uuid(source.SourceId) && Uuid(source.ChildAgeSourceId) && Uuid(source.ChildAgeAdoptionId)
                    && source.SourceId != source.ChildAgeSourceId && source.SourceId != source.ChildAgeAdoptionId
                    && source.ChildAgeSourceId != source.ChildAgeAdoptionId
                    && source.ExpectedChildAgeSourceRevision == 1 && source.ExpectedChildAgeAdoptionRevision == 1
                    && source.ExpectedChildAgeSourceWriterGeneration is > 0 and < long.MaxValue
                    && source.ExpectedChildAgeAdoptionWriterGeneration is > 0 and < long.MaxValue
                    && source.ExpectedChildAgeAdoptionWriterGeneration > source.ExpectedChildAgeSourceWriterGeneration
                    && source.Procedure is not null && Finite(source.ValidUntil),
                AdoptModerationProfileCollectionProcedureCommand source =>
                    Uuid(source.SourceId) && Uuid(source.AdoptionId) && source.SourceId != source.AdoptionId
                    && source.ExpectedSourceRevision == 1
                    && source.ExpectedSourceWriterGeneration is > 0 and < long.MaxValue && Finite(source.ValidUntil),
                WithdrawModerationProfileCollectionProcedureCommand source =>
                    Uuid(source.AdoptionId) && source.ExpectedAdoptionRevision == 1,
                _ => false
            };
            if (!valid)
                validation.AddFailure("Profile procedure governance requires exact original coordinates, ordered writer generations, typed procedure and finite UTC leases.");
        });
    }

    private static bool Uuid(Guid id) =>
        id != Guid.Empty && id.Version == 7 && (id.ToByteArray(bigEndian: true)[8] & 0xc0) == 0x80;

    private static bool Finite(DateTimeOffset value) =>
        value.Offset == TimeSpan.Zero && value != DateTimeOffset.MinValue && value != DateTimeOffset.MaxValue;
}
