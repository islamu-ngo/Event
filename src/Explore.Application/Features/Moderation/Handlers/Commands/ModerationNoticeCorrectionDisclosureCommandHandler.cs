using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Requests.Commands;
using Explore.Domain;
using FluentValidation;
using ISLAMU.Moderation.Domain;

namespace Explore.Application.Features.Moderation.Handlers.Commands;

public sealed class ApproveModerationNoticeCorrectionDisclosureCommandHandler(IModerationQualificationRepository sources)
    : ICommandHandler<ApproveModerationNoticeCorrectionDisclosureCommand, ModerationQualificationResponse>
{
    public async Task<ModerationQualificationResponse> ExecuteAsync(
        ApproveModerationNoticeCorrectionDisclosureCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = await new ModerationNoticeCorrectionDisclosureCommandValidator().ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return ModerationQualificationResponse.Invalid(validation.Errors.Select(error => error.ErrorMessage));
        var result = await sources.ExecuteAsync(command.ToMutation(), cancellationToken);
        return ModerationQualificationResponse.FromOutcome(command.OperationId, result);
    }
}

/// <summary>Validates text intent only; the native owner authenticates pending hashes, original audience and retention ceiling.</summary>
public sealed class ModerationNoticeCorrectionDisclosureCommandValidator : AbstractValidator<ApproveModerationNoticeCorrectionDisclosureCommand>
{
    public ModerationNoticeCorrectionDisclosureCommandValidator()
    {
        RuleFor(command => command.OperationId).Must(id => id != Guid.Empty && id.Version == 7 && id.Variant is >= 8 and <= 11)
            .WithMessage("Correction disclosure requires a UUIDv7 operation identity.");
        RuleFor(command => command.Target).Must(scope => scope is not null
            && (scope.Kind == ModerationScopeKind.Tenant
                || scope.Kind is (ModerationScopeKind.Event or ModerationScopeKind.Resource)
                    && scope.Parent?.Kind == ModerationScopeKind.Tenant))
            .WithMessage("Correction disclosure requires a native tenant or its direct event or resource target.");
        RuleFor(command => command.ExpectedQualificationRevision).InclusiveBetween(1, long.MaxValue - 1);
        RuleFor(command => command.ExpectedPendingSnapshotHash).Must(Hash)
            .WithMessage("Correction disclosure requires an exact lowercase pending snapshot hash.");
        RuleFor(command => command.ExpectedPendingEffectBindingHash).Must(Hash)
            .WithMessage("Correction disclosure requires an exact lowercase pending effect binding hash.");
        RuleFor(command => command.Intent).Must(Intent)
            .WithMessage("Correction disclosure requires valid bounded text, an empty representative selection and validity within retention.");
    }

    private static bool Hash(string? value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigitLower);

    private static bool Intent(ModerationNoticeDisclosureIntent? intent)
    {
        if (intent is null || intent.ContentRepresentatives.IsDefault || !intent.ContentRepresentatives.IsEmpty
            || intent.ValidUntil > intent.RetainUntil) return false;
        try
        {
            intent.Validate();
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
