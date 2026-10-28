using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Requests.Commands;
using FluentValidation;
using ISLAMU.Moderation.Domain;

namespace Explore.Application.Features.Moderation.Handlers.Commands;

public sealed class RecordModerationClassificationTaxonomyCommandHandler(IModerationQualificationRepository sources)
    : ICommandHandler<RecordModerationClassificationTaxonomyCommand, ModerationQualificationResponse>
{
    public async Task<ModerationQualificationResponse> ExecuteAsync(
        RecordModerationClassificationTaxonomyCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = await new ModerationClassificationTaxonomyCommandValidator<RecordModerationClassificationTaxonomyCommand>()
            .ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return ModerationQualificationResponse.Invalid(validation.Errors.Select(error => error.ErrorMessage));
        return ModerationQualificationResponse.FromOutcome(command.OperationId,
            await sources.ExecuteAsync(command.ToMutation(), cancellationToken));
    }
}

public sealed class AdoptModerationClassificationTaxonomyCommandHandler(IModerationQualificationRepository sources)
    : ICommandHandler<AdoptModerationClassificationTaxonomyCommand, ModerationQualificationResponse>
{
    public async Task<ModerationQualificationResponse> ExecuteAsync(
        AdoptModerationClassificationTaxonomyCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = await new ModerationClassificationTaxonomyCommandValidator<AdoptModerationClassificationTaxonomyCommand>()
            .ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return ModerationQualificationResponse.Invalid(validation.Errors.Select(error => error.ErrorMessage));
        return ModerationQualificationResponse.FromOutcome(command.OperationId,
            await sources.ExecuteAsync(command.ToMutation(), cancellationToken));
    }
}

public sealed class WithdrawModerationClassificationTaxonomyCommandHandler(IModerationQualificationRepository sources)
    : ICommandHandler<WithdrawModerationClassificationTaxonomyCommand, ModerationQualificationResponse>
{
    public async Task<ModerationQualificationResponse> ExecuteAsync(
        WithdrawModerationClassificationTaxonomyCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = await new ModerationClassificationTaxonomyCommandValidator<WithdrawModerationClassificationTaxonomyCommand>()
            .ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return ModerationQualificationResponse.Invalid(validation.Errors.Select(error => error.ErrorMessage));
        return ModerationQualificationResponse.FromOutcome(command.OperationId,
            await sources.ExecuteAsync(command.ToMutation(), cancellationToken));
    }
}

public sealed class ModerationClassificationTaxonomyCommandValidator<TCommand> : AbstractValidator<TCommand>
    where TCommand : ModerationQualificationCommand
{
    public ModerationClassificationTaxonomyCommandValidator()
    {
        RuleFor(command => command.OperationId).Must(Uuid);
        RuleFor(command => command.ExpectedQualificationRevision).InclusiveBetween(1, long.MaxValue - 2);
        RuleFor(command => command.Target).Must(scope =>
            scope is not null && scope.Kind is ModerationScopeKind.Instance or ModerationScopeKind.Tenant);
        RuleFor(command => command).Custom((command, validation) =>
        {
            var valid = command switch
            {
                RecordModerationClassificationTaxonomyCommand source => Uuid(source.SourceId) && Uuid(source.TaxonomyId)
                    && Uuid(source.PolicyId) && Uuid(source.PublicationId) && Uuid(source.ProfileId)
                    && source.ExpectedTaxonomyGeneration >= 0
                    && source.ExpectedTaxonomyGeneration <= source.ExpectedQualificationRevision
                    && source.ExpectedPolicyRevision is > 0 and < long.MaxValue
                    && Enum.IsDefined(source.Mode) && Finite(source.ValidUntil) && Substantive(source),
                AdoptModerationClassificationTaxonomyCommand source => Uuid(source.SourceId) && Uuid(source.AdoptionId)
                    && source.SourceId != source.AdoptionId && source.ExpectedSourceRevision == 1
                    && source.ExpectedSourceWriterGeneration is > 0 and < long.MaxValue && Finite(source.ValidUntil),
                WithdrawModerationClassificationTaxonomyCommand source =>
                    Uuid(source.AdoptionId) && source.ExpectedAdoptionRevision == 1,
                _ => false
            };
            if (!valid) validation.AddFailure("Taxonomy intent requires exact coordinates, a finite unique category inventory and independent review obligations.");
        });
    }

    private static bool Substantive(RecordModerationClassificationTaxonomyCommand source)
    {
        if (source.Mandate is null || source.Categories.IsDefaultOrEmpty || source.Categories.Length > 64
            || source.Categories.Any(category => category is null)) return false;
        try
        {
            source.Mandate.Validate();
            foreach (var category in source.Categories)
                // The native writer separately enforces the actually adopted Debate floor.
                category.Validate(1);
            return source.Categories.Select(category => category.VersionId).Distinct().Count() == source.Categories.Length
                && source.Categories.Select(category => category.CategoryId).Distinct().Count() == source.Categories.Length
                && source.Categories.Select(category => category.Code).Distinct(StringComparer.Ordinal).Count() == source.Categories.Length;
        }
        catch (ArgumentException) { return false; }
    }

    private static bool Uuid(Guid id) =>
        id != Guid.Empty && id.Version == 7 && (id.ToByteArray(bigEndian: true)[8] & 0xc0) == 0x80;

    private static bool Finite(DateTimeOffset value) =>
        value.Offset == TimeSpan.Zero && value != DateTimeOffset.MinValue && value != DateTimeOffset.MaxValue;
}
