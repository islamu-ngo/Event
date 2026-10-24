using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Requests.Commands;
using Explore.Application.Responses;
using FluentValidation;
using ISLAMU.Moderation.Domain;

namespace Explore.Application.Features.Moderation.Handlers.Commands;

public sealed class RecordModerationProfileCollectionAssessmentCommandHandler(
    IModerationProfileCollectionAssessmentRepository assessments)
    : ICommandHandler<RecordModerationProfileCollectionAssessmentCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(
        RecordModerationProfileCollectionAssessmentCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();
        var validation = await new ModerationProfileCollectionAssessmentCommandValidator<RecordModerationProfileCollectionAssessmentCommand>()
            .ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return BaseCommandResponse.Validation<Guid>(validation.Errors.Select(error => error.ErrorMessage));
        var source = await assessments.RecordAsync(command.ToRecord(), cancellationToken);
        return source is null ? BaseCommandResponse.NotFound<Guid>() : BaseCommandResponse.Success(source.Id);
    }
}

public sealed class AdoptModerationProfileCollectionAssessmentCommandHandler(
    IModerationProfileCollectionAssessmentRepository assessments)
    : ICommandHandler<AdoptModerationProfileCollectionAssessmentCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(
        AdoptModerationProfileCollectionAssessmentCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();
        var validation = await new ModerationProfileCollectionAssessmentCommandValidator<AdoptModerationProfileCollectionAssessmentCommand>()
            .ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return BaseCommandResponse.Validation<Guid>(validation.Errors.Select(error => error.ErrorMessage));
        var adoption = await assessments.AdoptAsync(command.ToAdoption(), cancellationToken);
        return adoption is null ? BaseCommandResponse.NotFound<Guid>() : BaseCommandResponse.Success(adoption.Id);
    }
}

public sealed class ModerationProfileCollectionAssessmentCommandValidator<TCommand> : AbstractValidator<TCommand>
    where TCommand : ICommand<BaseCommandResponse<Guid>>
{
    public ModerationProfileCollectionAssessmentCommandValidator()
    {
        RuleFor(command => command).Custom((command, validation) =>
        {
            var valid = command switch
            {
                RecordModerationProfileCollectionAssessmentCommand source =>
                    source.Original is not null && Original(source.Original)
                    && Uuid(source.SourceId) && !AliasesOriginal(source.SourceId, source.Original)
                    && source.ExpectedGeneration is >= 0 and < long.MaxValue - 1
                    && source.Required is not null && source.Required.Count <= 3
                    && source.Required.All(kind => Enum.IsDefined(kind))
                    && source.Required.Distinct().Count() == source.Required.Count && Finite(source.ValidUntil),
                AdoptModerationProfileCollectionAssessmentCommand adoption =>
                    adoption.Original is not null && Original(adoption.Original)
                    && Uuid(adoption.SourceId) && Uuid(adoption.AdoptionId) && adoption.SourceId != adoption.AdoptionId
                    && !AliasesOriginal(adoption.SourceId, adoption.Original)
                    && !AliasesOriginal(adoption.AdoptionId, adoption.Original)
                    && adoption.ExpectedSourceRevision == 1
                    && adoption.ExpectedSourceGeneration is > 0 and < long.MaxValue - 1
                    && adoption.ExpectedGeneration == adoption.ExpectedSourceGeneration && Finite(adoption.ValidUntil),
                _ => false
            };
            if (!valid)
                validation.AddFailure("Assessment intent requires explicit original coordinates, complete requirements, exact slot generations and a finite UTC window.");
        });
    }

    internal static bool Original(ModerationProfileCollectionAssessmentOriginalIntent original) =>
        Uuid(original.ObservationId) && original.ExpectedObservationRevision == 1
        && original.ExpectedObservationGeneration is > 0 and < long.MaxValue
        && original.Jurisdiction is { Kind: ModerationScopeKind.Instance or ModerationScopeKind.Tenant }
        && Uuid(original.PolicyId) && original.PolicyRevision is > 0 and < long.MaxValue
        && Uuid(original.PublicationId) && Uuid(original.ProfileId) && Enum.IsDefined(original.Mode)
        && Uuid(original.ProcedureSourceId) && original.ExpectedProcedureSourceRevision == 1
        && original.ExpectedProcedureSourceWriterGeneration is > 0 and < long.MaxValue
        && Uuid(original.ProcedureAdoptionId) && original.ExpectedProcedureAdoptionRevision == 1
        && original.ExpectedProcedureAdoptionWriterGeneration is > 0 and < long.MaxValue
        && original.ExpectedProcedureAdoptionWriterGeneration > original.ExpectedProcedureSourceWriterGeneration
        && original.ProcedureSourceId != original.ProcedureAdoptionId;

    internal static bool AliasesOriginal(Guid id, ModerationProfileCollectionAssessmentOriginalIntent original) =>
        id == original.ObservationId || id == original.PolicyId || id == original.PublicationId
        || id == original.ProfileId || id == original.ProcedureSourceId || id == original.ProcedureAdoptionId
        || id == original.Jurisdiction.Id || id == original.Jurisdiction.TrustDomainId;

    private static bool Uuid(Guid id) =>
        id != Guid.Empty && id.Version == 7 && (id.ToByteArray(bigEndian: true)[8] & 0xc0) == 0x80;

    private static bool Finite(DateTimeOffset value) =>
        value.Offset == TimeSpan.Zero && value != DateTimeOffset.MinValue && value != DateTimeOffset.MaxValue;
}
