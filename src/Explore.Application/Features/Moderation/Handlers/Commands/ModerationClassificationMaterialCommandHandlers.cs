using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Requests.Commands;
using FluentValidation;
using ISLAMU.Moderation.Domain;

namespace Explore.Application.Features.Moderation.Handlers.Commands;

public sealed class RecordModerationClassificationMaterialCommandHandler(IModerationQualificationRepository sources)
    : ICommandHandler<RecordModerationClassificationMaterialCommand, ModerationQualificationResponse>
{
    public async Task<ModerationQualificationResponse> ExecuteAsync(
        RecordModerationClassificationMaterialCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = await new ModerationClassificationMaterialCommandValidator<RecordModerationClassificationMaterialCommand>()
            .ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return ModerationQualificationResponse.Invalid(validation.Errors.Select(error => error.ErrorMessage));
        return ModerationQualificationResponse.FromOutcome(command.OperationId,
            await sources.ExecuteAsync(command.ToMutation(), cancellationToken));
    }
}

public sealed class AcceptModerationClassificationMaterialCommandHandler(IModerationQualificationRepository sources)
    : ICommandHandler<AcceptModerationClassificationMaterialCommand, ModerationQualificationResponse>
{
    public async Task<ModerationQualificationResponse> ExecuteAsync(
        AcceptModerationClassificationMaterialCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = await new ModerationClassificationMaterialCommandValidator<AcceptModerationClassificationMaterialCommand>()
            .ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return ModerationQualificationResponse.Invalid(validation.Errors.Select(error => error.ErrorMessage));
        return ModerationQualificationResponse.FromOutcome(command.OperationId,
            await sources.ExecuteAsync(command.ToMutation(), cancellationToken));
    }
}

public sealed class EndModerationClassificationMaterialCommandHandler(IModerationQualificationRepository sources)
    : ICommandHandler<EndModerationClassificationMaterialCommand, ModerationQualificationResponse>
{
    public async Task<ModerationQualificationResponse> ExecuteAsync(
        EndModerationClassificationMaterialCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = await new ModerationClassificationMaterialCommandValidator<EndModerationClassificationMaterialCommand>()
            .ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return ModerationQualificationResponse.Invalid(validation.Errors.Select(error => error.ErrorMessage));
        return ModerationQualificationResponse.FromOutcome(command.OperationId,
            await sources.ExecuteAsync(command.ToMutation(), cancellationToken));
    }
}

public sealed class ModerationClassificationMaterialCommandValidator<TCommand> : AbstractValidator<TCommand>
    where TCommand : ModerationQualificationCommand
{
    public ModerationClassificationMaterialCommandValidator()
    {
        RuleFor(command => command.OperationId).Must(Uuid);
        RuleFor(command => command.ExpectedQualificationRevision).InclusiveBetween(1, long.MaxValue - 2);
        RuleFor(command => command.Target).Must(scope => scope is not null && scope.Kind == ModerationScopeKind.Tenant);
        RuleFor(command => command).Custom((command, validation) =>
        {
            var valid = command switch
            {
                RecordModerationClassificationMaterialCommand source => Uuid(source.SourceId)
                    && Uuid(source.EventGenerationId) && Uuid(source.PolicyId) && Uuid(source.PublicationId)
                    && Uuid(source.ProfileId) && Uuid(source.TaxonomyId) && Uuid(source.ExpectedTaxonomySourceId)
                    && Uuid(source.ExpectedTaxonomyAdoptionId) && source.ExpectedMaterialGeneration >= 0
                    && source.ExpectedMaterialGeneration <= source.ExpectedQualificationRevision
                    && source.ExpectedPolicyRevision is > 0 and < long.MaxValue && Enum.IsDefined(source.Mode)
                    && Finite(source.ValidUntil) && Substantive(source),
                AcceptModerationClassificationMaterialCommand source => Uuid(source.SourceId) && Uuid(source.ReviewId)
                    && source.SourceId != source.ReviewId && source.ExpectedSourceRevision == 1
                    && source.ExpectedSourceWriterGeneration is > 0 and < long.MaxValue && Finite(source.ValidUntil),
                EndModerationClassificationMaterialCommand source => Uuid(source.ReviewId)
                    && source.ExpectedReviewRevision == 1 && Enum.IsDefined(source.End)
                    && !string.IsNullOrWhiteSpace(source.Grounds) && source.Grounds.Length <= 2048,
                _ => false
            };
            if (!valid) validation.AddFailure("Material review intent requires exact native coordinates, unique bounded inventories and independent accountable review.");
        });
    }

    private static bool Substantive(RecordModerationClassificationMaterialCommand source)
    {
        if (source.Mandate is null || source.Materials.IsDefaultOrEmpty || source.Materials.Length > 4096
            || source.Components.IsDefaultOrEmpty || source.Components.Length > 4096
            || source.Materials.Any(row => row is null) || source.Components.Any(row => row is null))
            return false;
        try
        {
            source.Mandate.Validate();
            foreach (var selection in source.Materials) selection.Validate();
            foreach (var component in source.Components) component.Validate();
            // Native acquisition, not these syntactic checks, proves actual complete inventory,
            // current Event ownership, taxonomy selection and reviewer authority.
            return source.Materials.Select(row => row.ReviewId).Distinct().Count() == source.Materials.Length
                && source.Materials.Select(row => row.Material).Distinct().Count() == source.Materials.Length
                && source.Components.Select(row => (row.Kind, row.NativeId)).Distinct().Count() == source.Components.Length;
        }
        catch (ArgumentException) { return false; }
    }

    private static bool Uuid(Guid id) =>
        id != Guid.Empty && id.Version == 7 && (id.ToByteArray(bigEndian: true)[8] & 0xc0) == 0x80;

    private static bool Finite(DateTimeOffset value) =>
        value.Offset == TimeSpan.Zero && value != DateTimeOffset.MinValue && value != DateTimeOffset.MaxValue;
}
