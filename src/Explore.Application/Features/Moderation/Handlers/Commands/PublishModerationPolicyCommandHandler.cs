using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Requests.Commands;
using FluentValidation;
using ISLAMU.Moderation.Domain;

namespace Explore.Application.Features.Moderation.Handlers.Commands;

public sealed class PublishModerationPolicyCommandHandler(IModerationPolicyPublicationRepository publications)
    : ICommandHandler<PublishModerationPolicyCommand, ModerationPolicyPublicationResponse>
{
    public async Task<ModerationPolicyPublicationResponse> ExecuteAsync(
        PublishModerationPolicyCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = await new ModerationPolicyPublicationCommandValidator()
            .ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return ModerationPolicyPublicationResponse.Invalid(validation.Errors.Select(error => error.ErrorMessage));
        var result = await publications.PublishAsync(command.Request, cancellationToken);
        return ModerationPolicyPublicationResponse.FromOutcome(command.Request.OperationId, result);
    }
}

/// <summary>
/// Bounds the complete request before native acquisition and serialization. Historical semantic
/// comparison requires the writer's fenced publication, not an invented validator history.
/// </summary>
public sealed class ModerationPolicyPublicationCommandValidator : AbstractValidator<PublishModerationPolicyCommand>
{
    public ModerationPolicyPublicationCommandValidator()
    {
        RuleFor(command => command.Request).NotNull().Custom((request, context) =>
        {
            if (request is null) return;
            var proposed = request.Proposed;
            if (!Version7(request.OperationId) || !Version7(request.ExpectedHostBindingId)
                || !Version7(request.ExpectedAuthoritySourceId)
                || request.ExpectedQualificationRevision is < 1 or long.MaxValue
                || request.ExpectedWriterGeneration != ModerationPolicyPublicationProjection.WriterGeneration
                || proposed is null || !Version7(proposed.PolicyId) || !Version7(proposed.PublicationId)
                || proposed.ExpectedRevision is < 0 or long.MaxValue
                || (proposed.ExpectedRevision == 0 ? request.ExpectedPublicationId is not null
                    : request.ExpectedPublicationId is not { } previous || !Version7(previous))
                || proposed.Jurisdiction is null
                || proposed.Jurisdiction.Kind is not (ModerationScopeKind.Instance or ModerationScopeKind.Tenant)
                || !Enum.IsDefined(proposed.RequestedState)
                || proposed.CatalogueRevision < 1 || proposed.InventoryRevision < 1
                || string.IsNullOrWhiteSpace(proposed.InventoryFingerprint)
                || proposed.InventoryFingerprint.Length > 64
                || proposed.Modes.IsDefaultOrEmpty || proposed.Modes.Length > 2
                || proposed.Classes.IsDefaultOrEmpty || proposed.Classes.Length > 256
                || proposed.Reasons.IsDefaultOrEmpty || proposed.Reasons.Length > 256
                || proposed.Profiles.IsDefaultOrEmpty || proposed.Profiles.Length > 256
                || proposed.SupportedProfiles.IsDefault || proposed.SupportedProfiles.Length > 256
                || proposed.Classes.Any(item => item is null || item.Reference is null
                    || string.IsNullOrWhiteSpace(item.Meaning) || item.Meaning.Length > 4000)
                || proposed.Reasons.Any(item => item is null || item.Reference is null || item.Class is null
                    || string.IsNullOrWhiteSpace(item.Meaning) || item.Meaning.Length > 4000)
                || proposed.Profiles.Any(item => item is null || item.ScopeKinds.IsDefault
                    || item.SubjectKinds.IsDefault || item.Effects.IsDefault || item.Strategies.IsDefault
                    || item.ScopeKinds.Length > 6 || item.SubjectKinds.Length > 4
                    || item.Effects.Length > 26 || item.Strategies.Length > 3))
                context.AddFailure("Publication requires complete bounded authoring state and exact expected native coordinates.");
        });
    }

    private static bool Version7(Guid value) =>
        value != Guid.Empty && value.Version == 7 && value.Variant is >= 8 and <= 11;
}
