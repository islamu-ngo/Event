using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using ISLAMU.Moderation.Domain;

namespace Explore.Application.Features.ConfigurationManifest.Moderation;

/// <summary>
/// Native entities acquired together, not a portable DTO or reusable permission. Application maps
/// their immutable history into a preview; the writer independently reacquires them on apply.
/// </summary>
public sealed record ModerationConfigurationImportDestination(
    Guid PrincipalId, Guid TenantId, AuthorityScope Jurisdiction,
    ModerationHostBinding Host, ModerationQualificationEpoch Epoch,
    ModerationPolicySelection? Selection, ModerationPolicyPublication? Current,
    ModerationPolicyPublication? Baseline, string? AdministrationBinding = null,
    ModerationQualificationEpoch? RequestEpoch = null);

/// <summary>
/// Optimistic preview binding, never authorization. A coordinator persists Binding alongside its
/// artifact, section selection and approval binding. Mapping includes newly reserved identities;
/// retained identities, all revisions and the complete document are recomputed before any write.
/// </summary>
public sealed class ModerationConfigurationImportPreview
{
    internal ModerationConfigurationImportPreview(
        Guid? baselinePublicationId, ModerationConfigurationDocument? document,
        ModerationConfigurationDiff diff, ModerationConfigurationDestinationMapping mapping,
        ModerationConfigurationValidationResult? validation, string binding)
    {
        BaselinePublicationId = baselinePublicationId;
        Document = document;
        Diff = diff;
        Mapping = mapping;
        Validation = validation;
        Binding = binding;
    }

    public Guid? BaselinePublicationId { get; }
    public ModerationConfigurationDocument? Document { get; }
    public ModerationConfigurationDiff Diff { get; }
    public ModerationConfigurationDestinationMapping Mapping { get; }
    public ModerationConfigurationValidationResult? Validation { get; }
    public string Binding { get; }
    public bool CanApply => Document is not null && !Diff.HasConflicts && Validation?.CanImport == true;
    public ModerationConfigurationImportReservation Reserve() =>
        new(BaselinePublicationId, Mapping.PolicyId, Mapping.PublicationId, Binding,
            Mapping.ProfileIds.Select(item =>
                new ModerationConfigurationReservedProfile(item.Key.Code, item.Key.Revision, item.Value)).ToArray());
    public override string ToString() => nameof(ModerationConfigurationImportPreview);
}

public sealed class ModerationConfigurationImportException(ModerationPolicyPublicationOutcome outcome)
    : Exception("The native moderation configuration import was rejected.")
{
    public ModerationPolicyPublicationOutcome Outcome { get; } = outcome;
}
