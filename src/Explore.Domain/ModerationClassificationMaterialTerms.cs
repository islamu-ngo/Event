using System.Collections.Immutable;

namespace Explore.Domain;

public sealed record ModerationClassificationMaterialMandate(
    Guid OwnerHumanId, Guid OwnerControllerHumanId, Guid OwnerAssignmentId, long OwnerAssignmentRevision,
    Guid ReviewerHumanId, Guid ReviewerControllerHumanId, string OwnershipGrounds, string ReviewGrounds)
{
    public void Validate()
    {
        EventContentClassificationVersion.RequireIndependentReview(
            OwnerHumanId, OwnerControllerHumanId, ReviewerHumanId, ReviewerControllerHumanId);
        if (OwnerAssignmentId == Guid.Empty || OwnerAssignmentRevision is < 1 or long.MaxValue
            || !ModerationChildAgePolicyTerms.Grounds(OwnershipGrounds)
            || !ModerationChildAgePolicyTerms.Grounds(ReviewGrounds))
            throw new ArgumentException("Material review requires exact native ownership and substantive independent review grounds.");
    }
}

/// <summary>Finite native carrier kinds, not arbitrary client-supplied completeness declarations.</summary>
public enum ModerationClassificationMaterialComponentKind
{
    Event = 1, Session = 2, Resource = 3, ResourceAudience = 4,
    Form = 5, FormVersion = 6, FormSection = 7, FormField = 8, FormOption = 9, FormRule = 10,
    ProviderBinding = 11, ProviderConnection = 12, ProviderOrigin = 13, ProviderField = 14,
    ProviderOption = 15, ProviderCapability = 16, ProviderSchema = 17,
    Workflow = 18, Requirement = 19, Channel = 20, Participation = 21, Attachment = 22,
    EventProperty = 23, EventOption = 24, EventValue = 25,
    SessionProperty = 26, SessionOption = 27, SessionValue = 28,
    PublicAction = 29, Agenda = 30, Day = 31, SessionGroup = 32, SessionGroupMember = 33,
    Speaker = 34, Storage = 35, Actor = 36, EventIslamic = 37, EventTech = 38,
    SessionIslamic = 39, Location = 40, ActorProfile = 41
}

/// <summary>
/// Payload-free native scalar version. The optional GUID stamp is never a revision counter.
/// Digest binds every persisted scalar of an explicitly inventoried carrier, including native
/// rows without a concurrency stamp and raw mutations that did not advance an ancestor.
/// </summary>
public sealed record ModerationClassificationMaterialComponentVersion(
    ModerationClassificationMaterialComponentKind Kind, Guid NativeId, Guid? NativeStamp, string Digest)
{
    public void Validate()
    {
        if (!Enum.IsDefined(Kind) || NativeId == Guid.Empty || NativeStamp == Guid.Empty
            || !ModerationDeploymentSource.Digest(Digest))
            throw new ArgumentException("Material component requires exact native identity and a scalar version digest.");
    }
}

public sealed record ModerationClassificationMaterialSelection(
    Guid ReviewId, ModerationClassificationMaterial Material, ImmutableArray<Guid> CategoryVersionIds)
{
    public void Validate()
    {
        ModerationHostBinding.RequireUuidV7(ReviewId, nameof(ReviewId));
        ArgumentNullException.ThrowIfNull(Material);
        Material.Validate();
        if (CategoryVersionIds.IsDefaultOrEmpty || CategoryVersionIds.Length > 64
            || CategoryVersionIds.Any(id => id == Guid.Empty)
            || CategoryVersionIds.Distinct().Count() != CategoryVersionIds.Length)
            throw new ArgumentException("Each material requires unique exact accepted category versions.");
    }
}
