using Explore.Application.DTOs.Actor;
using Explore.Application.DTOs.StorageObject;
using Explore.Domain;
using Riok.Mapperly.Abstractions;

namespace Explore.Application.Mappings;

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Both, AutoUserMappings = false)]
public static partial class ActorFederationMapper
{
    // Public profile scalars only. Repository queries own identity visibility and tenant fences.
    // Pii selectors avoid proxy getters after hard erasure; no owner, audit or moderation graph is copied.
    [MapperIgnoreSource(nameof(Actor.DisplayName))]
    [MapperIgnoreSource(nameof(Actor.ProfilePictureUri))]
    [MapperIgnoreSource(nameof(Actor.User))]
    [MapperIgnoreSource(nameof(Actor.Organization))]
    [MapperIgnoreSource(nameof(Actor.Group))]
    [MapperIgnoreSource(nameof(Actor.ExternalActorSubjectId))]
    [MapperIgnoreSource(nameof(Actor.ExternalActorSubject))]
    [MapperIgnoreSource(nameof(Actor.ServicePrincipalId))]
    [MapperIgnoreSource(nameof(Actor.ServicePrincipal))]
    [MapperIgnoreSource(nameof(Actor.ModerationRecords))]
    [MapperIgnoreSource(nameof(Actor.MergesFrom))]
    [MapperIgnoreSource(nameof(Actor.MergesInto))]
    [MapperIgnoreSource(nameof(Actor.IsSuspended))]
    [MapperIgnoreSource(nameof(Actor.SuspendedAt))]
    [MapperIgnoreSource(nameof(Actor.SuspendedBy))]
    [MapperIgnoreSource(nameof(Actor.ModerationReasonCode))]
    [MapperIgnoreSource(nameof(Actor.CreatedAt))]
    [MapperIgnoreSource(nameof(Actor.CreatedBy))]
    [MapperIgnoreSource(nameof(Actor.UpdatedAt))]
    [MapperIgnoreSource(nameof(Actor.UpdatedBy))]
    [MapperIgnoreSource(nameof(Actor.IsDeleted))]
    [MapperIgnoreSource(nameof(Actor.DeletedAt))]
    [MapperIgnoreSource(nameof(Actor.DeletedBy))]
    // Local context is assigned by handlers; no custody or storage-object authority is inferred.
    [MapperIgnoreTarget(nameof(ActorDto.TenantId))]
    [MapperIgnoreTarget(nameof(ActorDto.IsLocallyDiscoverable))]
    [MapperIgnoreTarget(nameof(ActorDto.DidCustodyTypeId))]
    [MapperIgnoreTarget(nameof(ActorDto.DidCustodyTypeMasterCode))]
    [MapperIgnoreTarget(nameof(ActorDto.DidCustodyTypeFullName))]
    [MapperIgnoreTarget(nameof(ActorDto.ProfilePictureId))]
    [MapperIgnoreTarget(nameof(ActorDto.BannerPictureId))]
    [MapperIgnoreTarget(nameof(ActorDto.BannerPictureUri))]
    [MapperIgnoreTarget(nameof(ActorDto.BackgroundImageId))]
    [MapperIgnoreTarget(nameof(ActorDto.BackgroundImageUri))]
    [MapProperty(nameof(Actor.ActorType), nameof(ActorDto.ActorTypeMasterCode), Use = nameof(ActorTypeCode))]
    [MapProperty(nameof(Actor.ActorType), nameof(ActorDto.ActorTypeFullName), Use = nameof(ActorTypeName))]
    [MapProperty(nameof(Actor.Pii), nameof(ActorDto.DisplayName), Use = nameof(DisplayName))]
    [MapProperty(nameof(Actor.Pii), nameof(ActorDto.ProfilePictureUri), Use = nameof(ProfilePictureUri))]
    [MapProperty(nameof(Actor.AtprotoIdentities), nameof(ActorDto.Did), Use = nameof(FirstDid))]
    [MapProperty(nameof(Actor.AtprotoIdentities), nameof(ActorDto.Handle), Use = nameof(FirstHandle))]
    [MapProperty(nameof(Actor.AtprotoIdentities), nameof(ActorDto.PdsHost), Use = nameof(FirstPdsHost))]
    [MapProperty(nameof(Actor.AtprotoIdentities), nameof(ActorDto.IndexedAt), Use = nameof(FirstIndexedAt))]
    public static partial ActorDto ToActorDetail(Actor source);

    // Lists additionally omit ownership identifiers, profile CID and description.
    [MapperIgnoreSource(nameof(Actor.UserId))]
    [MapperIgnoreSource(nameof(Actor.OrganizationId))]
    [MapperIgnoreSource(nameof(Actor.GroupId))]
    [MapperIgnoreSource(nameof(Actor.ProfilePictureCid))]
    [MapperIgnoreSource(nameof(Actor.Description))]
    [MapperIgnoreSource(nameof(Actor.DisplayName))]
    [MapperIgnoreSource(nameof(Actor.ProfilePictureUri))]
    [MapperIgnoreSource(nameof(Actor.User))]
    [MapperIgnoreSource(nameof(Actor.Organization))]
    [MapperIgnoreSource(nameof(Actor.Group))]
    [MapperIgnoreSource(nameof(Actor.ExternalActorSubjectId))]
    [MapperIgnoreSource(nameof(Actor.ExternalActorSubject))]
    [MapperIgnoreSource(nameof(Actor.ServicePrincipalId))]
    [MapperIgnoreSource(nameof(Actor.ServicePrincipal))]
    [MapperIgnoreSource(nameof(Actor.ModerationRecords))]
    [MapperIgnoreSource(nameof(Actor.MergesFrom))]
    [MapperIgnoreSource(nameof(Actor.MergesInto))]
    [MapperIgnoreSource(nameof(Actor.IsSuspended))]
    [MapperIgnoreSource(nameof(Actor.SuspendedAt))]
    [MapperIgnoreSource(nameof(Actor.SuspendedBy))]
    [MapperIgnoreSource(nameof(Actor.ModerationReasonCode))]
    [MapperIgnoreSource(nameof(Actor.CreatedAt))]
    [MapperIgnoreSource(nameof(Actor.CreatedBy))]
    [MapperIgnoreSource(nameof(Actor.UpdatedAt))]
    [MapperIgnoreSource(nameof(Actor.UpdatedBy))]
    [MapperIgnoreSource(nameof(Actor.IsDeleted))]
    [MapperIgnoreSource(nameof(Actor.DeletedAt))]
    [MapperIgnoreSource(nameof(Actor.DeletedBy))]
    [MapperIgnoreTarget(nameof(ActorListDto.TenantId))]
    [MapperIgnoreTarget(nameof(ActorListDto.IsLocallyDiscoverable))]
    [MapperIgnoreTarget(nameof(ActorListDto.DidCustodyTypeId))]
    [MapperIgnoreTarget(nameof(ActorListDto.DidCustodyTypeMasterCode))]
    [MapperIgnoreTarget(nameof(ActorListDto.DidCustodyTypeFullName))]
    [MapperIgnoreTarget(nameof(ActorListDto.ProfilePictureId))]
    [MapperIgnoreTarget(nameof(ActorListDto.BannerPictureId))]
    [MapperIgnoreTarget(nameof(ActorListDto.BannerPictureUri))]
    [MapperIgnoreTarget(nameof(ActorListDto.BackgroundImageId))]
    [MapperIgnoreTarget(nameof(ActorListDto.BackgroundImageUri))]
    [MapProperty(nameof(Actor.ActorType), nameof(ActorListDto.ActorTypeMasterCode), Use = nameof(ActorTypeCode))]
    [MapProperty(nameof(Actor.ActorType), nameof(ActorListDto.ActorTypeFullName), Use = nameof(ActorTypeName))]
    [MapProperty(nameof(Actor.Pii), nameof(ActorListDto.DisplayName), Use = nameof(DisplayName))]
    [MapProperty(nameof(Actor.Pii), nameof(ActorListDto.ProfilePictureUri), Use = nameof(ProfilePictureUri))]
    [MapProperty(nameof(Actor.AtprotoIdentities), nameof(ActorListDto.Did), Use = nameof(FirstDid))]
    [MapProperty(nameof(Actor.AtprotoIdentities), nameof(ActorListDto.Handle), Use = nameof(FirstHandle))]
    [MapProperty(nameof(Actor.AtprotoIdentities), nameof(ActorListDto.PdsHost), Use = nameof(FirstPdsHost))]
    [MapProperty(nameof(Actor.AtprotoIdentities), nameof(ActorListDto.IndexedAt), Use = nameof(FirstIndexedAt))]
    public static partial ActorListDto ToActorListItem(Actor source);

    // Operational detail exposes lifecycle and quarantine reason, never provider keys or audit authors.
    // Content eligibility is resolved by the consuming handler, not by the mapper.
    [MapperIgnoreSource(nameof(StorageObject.ObjectKey))]
    [MapperIgnoreSource(nameof(StorageObject.RegistrationContentRetentionUntilUtc))]
    [MapperIgnoreSource(nameof(StorageObject.QuarantinedBy))]
    [MapperIgnoreSource(nameof(StorageObject.CreatedAt))]
    [MapperIgnoreSource(nameof(StorageObject.CreatedBy))]
    [MapperIgnoreSource(nameof(StorageObject.UpdatedAt))]
    [MapperIgnoreSource(nameof(StorageObject.UpdatedBy))]
    [MapperIgnoreSource(nameof(StorageObject.DeletedBy))]
    [MapperIgnoreSource(nameof(StorageObject.ConcurrencyStamp))]
    [MapperIgnoreTarget(nameof(StorageObjectDto.ContentEligibility))]
    [MapProperty(nameof(StorageObject.FileType), nameof(StorageObjectDto.FileTypeFullName), Use = nameof(FileTypeName))]
    [MapProperty(nameof(StorageObject.FileType), nameof(StorageObjectDto.FileTypeMasterCode), Use = nameof(FileTypeCode))]
    [MapProperty(nameof(StorageObject.Tenant), nameof(StorageObjectDto.TenantFullName), Use = nameof(TenantName))]
    [MapProperty(nameof(StorageObject.Actor), nameof(StorageObjectDto.ActorDisplayName), Use = nameof(StorageActorName))]
    public static partial StorageObjectDto ToStorageDetail(StorageObject source);

    // Storage lists omit ownership graphs, checksum, quarantine detail and deletion/audit metadata.
    [MapperIgnoreSource(nameof(StorageObject.ObjectKey))]
    [MapperIgnoreSource(nameof(StorageObject.Sha256Checksum))]
    [MapperIgnoreSource(nameof(StorageObject.OwningResourceKind))]
    [MapperIgnoreSource(nameof(StorageObject.OwningResourceId))]
    [MapperIgnoreSource(nameof(StorageObject.Tenant))]
    [MapperIgnoreSource(nameof(StorageObject.ActorId))]
    [MapperIgnoreSource(nameof(StorageObject.Actor))]
    [MapperIgnoreSource(nameof(StorageObject.RegistrationContentRetentionUntilUtc))]
    [MapperIgnoreSource(nameof(StorageObject.QuarantinedAt))]
    [MapperIgnoreSource(nameof(StorageObject.QuarantinedBy))]
    [MapperIgnoreSource(nameof(StorageObject.QuarantineReason))]
    [MapperIgnoreSource(nameof(StorageObject.CreatedAt))]
    [MapperIgnoreSource(nameof(StorageObject.CreatedBy))]
    [MapperIgnoreSource(nameof(StorageObject.UpdatedAt))]
    [MapperIgnoreSource(nameof(StorageObject.UpdatedBy))]
    [MapperIgnoreSource(nameof(StorageObject.IsDeleted))]
    [MapperIgnoreSource(nameof(StorageObject.DeletedAt))]
    [MapperIgnoreSource(nameof(StorageObject.DeletedBy))]
    [MapperIgnoreSource(nameof(StorageObject.ConcurrencyStamp))]
    [MapperIgnoreTarget(nameof(StorageObjectListDto.ContentEligibility))]
    [MapProperty(nameof(StorageObject.FileType), nameof(StorageObjectListDto.FileTypeFullName), Use = nameof(FileTypeName))]
    public static partial StorageObjectListDto ToStorageListItem(StorageObject source);

    private static string? ActorTypeCode(ActorType? type) => type?.MasterCode;
    private static string? ActorTypeName(ActorType? type) => type?.FullName;
    // An erased profile has the DTO's empty display default. Loaded values pass through unchanged.
    private static string DisplayName(ActorPii? pii) => pii is null ? string.Empty : pii.DisplayName;
    private static string? ProfilePictureUri(ActorPii? pii) => pii?.ProfilePictureUri;
    // Do not sort, filter or skip null handles here: repository-selected enumeration order is authoritative.
    private static string? FirstDid(ICollection<AtprotoIdentity>? identities) => identities?.FirstOrDefault()?.Did;
    private static string? FirstHandle(ICollection<AtprotoIdentity>? identities) => identities?.FirstOrDefault()?.Handle;
    private static string? FirstPdsHost(ICollection<AtprotoIdentity>? identities) => identities?.FirstOrDefault()?.PdsHost;
    private static DateTime? FirstIndexedAt(ICollection<AtprotoIdentity>? identities) => identities?.FirstOrDefault()?.LastResolvedAt;
    private static string? FileTypeName(FileType? type) => type?.FullName;
    private static string? FileTypeCode(FileType? type) => type?.MasterCode;
    private static string? TenantName(Tenant? tenant) => tenant?.FullName;
    private static string? StorageActorName(Actor? actor) => actor?.Pii?.DisplayName;
}
