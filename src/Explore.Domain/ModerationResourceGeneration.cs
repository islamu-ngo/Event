using Explore.Domain.Enums;

namespace Explore.Domain;

/// <summary>
/// Immutable payload-free observation of actual native resource ancestry. Revision is allocated
/// by the serialized native writer; GUID concurrency stamps retain their original meaning.
/// An observation is not target adoption or a sanction.
/// </summary>
public sealed class ModerationResourceGeneration
{
    public required Guid Id { get; init; }
    public required Guid HostBindingId { get; init; }
    public required Guid TrustDomainId { get; init; }
    public required Guid TenantId { get; init; }
    public required Guid ResourceId { get; init; }
    public required long Revision { get; init; }
    public required Guid ObservedByHumanId { get; init; }
    public required long WriterGeneration { get; init; }
    public required DateTimeOffset ObservedAt { get; init; }
    public required DateTimeOffset ValidUntil { get; init; }
    public required Guid ResourceStamp { get; init; }
    public required Guid MaterialVersionId { get; init; }
    public required Guid EventId { get; init; }
    public required Guid EventStamp { get; init; }
    public required Guid ActorId { get; init; }
    public required Guid ActorStamp { get; init; }
    public required Guid? OrganizerActorId { get; init; }
    public required Guid? OrganizerActorStamp { get; init; }
    public required EventResourceDeliveryTypeEnum DeliveryType { get; init; }
    public required Guid? StorageObjectId { get; init; }
    public required Guid? StorageObjectStamp { get; init; }
    public required Guid? StorageProviderBindingId { get; init; }
    public required string? StorageProvider { get; init; }

    public void ValidatePersistedState()
    {
        foreach (var id in new[] { Id, HostBindingId, TrustDomainId, TenantId })
            ModerationHostBinding.RequireUuidV7(id, nameof(Id));
        ModerationHostBinding.RequireUuidV7(MaterialVersionId, nameof(MaterialVersionId));
        if (ResourceId == Guid.Empty || ResourceStamp == Guid.Empty || EventId == Guid.Empty || EventStamp == Guid.Empty
            || ActorId == Guid.Empty || ActorStamp == Guid.Empty || ObservedByHumanId == Guid.Empty
            || Revision is < 1 or long.MaxValue || WriterGeneration is < 1 or long.MaxValue
            || ObservedAt.Offset != TimeSpan.Zero || ObservedAt == DateTimeOffset.MinValue
            || ValidUntil.Offset != TimeSpan.Zero || ValidUntil == DateTimeOffset.MaxValue || ValidUntil <= ObservedAt
            || OrganizerActorId == Guid.Empty || OrganizerActorStamp == Guid.Empty
            || OrganizerActorId.HasValue != OrganizerActorStamp.HasValue || !Enum.IsDefined(DeliveryType)
            || (DeliveryType == EventResourceDeliveryTypeEnum.StoredFile
                ? StorageObjectId is null || StorageObjectId == Guid.Empty
                    || StorageObjectStamp is null || StorageObjectStamp == Guid.Empty
                    || StorageProviderBindingId is null || StorageProviderBindingId == Guid.Empty
                    || StorageProvider is not (StorageProviders.Local or StorageProviders.S3Compatible)
                : StorageObjectId is not null || StorageObjectStamp is not null
                    || StorageProviderBindingId is not null || StorageProvider is not null))
            throw new ArgumentException("A resource generation requires exact native ancestry and a finite observation window.");
    }

    public override string ToString() => nameof(ModerationResourceGeneration);

    public bool HasSameNativeVersion(ModerationResourceGeneration current) =>
        HostBindingId == current.HostBindingId && TrustDomainId == current.TrustDomainId && TenantId == current.TenantId
        && ResourceId == current.ResourceId && ResourceStamp == current.ResourceStamp
        && MaterialVersionId == current.MaterialVersionId
        && EventId == current.EventId && EventStamp == current.EventStamp && ActorId == current.ActorId
        && ActorStamp == current.ActorStamp && OrganizerActorId == current.OrganizerActorId
        && OrganizerActorStamp == current.OrganizerActorStamp && DeliveryType == current.DeliveryType
        && StorageObjectId == current.StorageObjectId && StorageObjectStamp == current.StorageObjectStamp
        && StorageProviderBindingId == current.StorageProviderBindingId && StorageProvider == current.StorageProvider;
}
