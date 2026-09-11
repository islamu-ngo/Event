using Explore.Application.DTOs.Group;
using Explore.Domain;
using Riok.Mapperly.Abstractions;

namespace Explore.Application.Mappings;

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Both, AutoUserMappings = false)]
public static partial class OrganizationMapper
{
    // Public group scalars only; tenant participation, approval and image-object authority stay outside this projection.
    [MapperIgnoreSource(nameof(Group.TenantParticipations))]
    [MapperIgnoreSource(nameof(Group.CreatedAt))]
    [MapperIgnoreSource(nameof(Group.CreatedBy))]
    [MapperIgnoreSource(nameof(Group.UpdatedAt))]
    [MapperIgnoreSource(nameof(Group.UpdatedBy))]
    [MapperIgnoreSource(nameof(Group.IsDeleted))]
    [MapperIgnoreSource(nameof(Group.DeletedAt))]
    [MapperIgnoreSource(nameof(Group.DeletedBy))]
    [MapperIgnoreTarget(nameof(GroupDto.TenantId))]
    [MapperIgnoreTarget(nameof(GroupDto.TenantFullName))]
    [MapperIgnoreTarget(nameof(GroupDto.ApprovalStatusId))]
    [MapperIgnoreTarget(nameof(GroupDto.ApprovalStatusFullName))]
    [MapperIgnoreTarget(nameof(GroupDto.ApprovalStatusMasterCode))]
    [MapperIgnoreTarget(nameof(GroupDto.ActorProfilePictureId))]
    [MapperIgnoreTarget(nameof(GroupDto.ActorBannerPictureId))]
    [MapperIgnoreTarget(nameof(GroupDto.ActorBannerPictureUri))]
    [MapperIgnoreTarget(nameof(GroupDto.ActorBackgroundImageId))]
    [MapperIgnoreTarget(nameof(GroupDto.ActorBackgroundImageUri))]
    [MapProperty(nameof(Group.Actor), nameof(GroupDto.ActorId), Use = nameof(ProfileId))]
    [MapProperty(nameof(Group.Actor), nameof(GroupDto.ActorDisplayName), Use = nameof(ProfileName))]
    [MapProperty(nameof(Group.Actor), nameof(GroupDto.ActorHandle), Use = nameof(ProfileHandle))]
    [MapProperty(nameof(Group.Actor), nameof(GroupDto.ActorProfilePictureUri), Use = nameof(ProfilePicture))]
    [MapProperty(nameof(Group.Actor), nameof(GroupDto.ActorBackgroundColor), Use = nameof(BackgroundColor))]
    [MapProperty(nameof(Group.Actor), nameof(GroupDto.ActorBackgroundEffect), Use = nameof(BackgroundEffect))]
    [MapProperty(nameof(Group.Actor), nameof(GroupDto.ActorBannerColor), Use = nameof(BannerColor))]
    public static partial GroupDto ToGroupDetail(Group source);

    // Lists keep creation time but omit actor identity and leave current-user role enrichment to the handler.
    [MapperIgnoreSource(nameof(Group.TenantParticipations))]
    [MapperIgnoreSource(nameof(Group.CreatedBy))]
    [MapperIgnoreSource(nameof(Group.UpdatedAt))]
    [MapperIgnoreSource(nameof(Group.UpdatedBy))]
    [MapperIgnoreSource(nameof(Group.IsDeleted))]
    [MapperIgnoreSource(nameof(Group.DeletedAt))]
    [MapperIgnoreSource(nameof(Group.DeletedBy))]
    [MapperIgnoreTarget(nameof(GroupListDto.TenantId))]
    [MapperIgnoreTarget(nameof(GroupListDto.ApprovalStatusId))]
    [MapValue(nameof(GroupListDto.ApprovalStatusFullName), Use = nameof(UnresolvedApprovalName))]
    [MapperIgnoreTarget(nameof(GroupListDto.CurrentUserRoleId))]
    [MapperIgnoreTarget(nameof(GroupListDto.ActorProfilePictureId))]
    [MapperIgnoreTarget(nameof(GroupListDto.ActorBannerPictureId))]
    [MapperIgnoreTarget(nameof(GroupListDto.ActorBannerPictureUri))]
    [MapperIgnoreTarget(nameof(GroupListDto.ActorBackgroundImageId))]
    [MapperIgnoreTarget(nameof(GroupListDto.ActorBackgroundImageUri))]
    [MapProperty(nameof(Group.Actor), nameof(GroupListDto.ActorProfilePictureUri), Use = nameof(ProfilePicture))]
    [MapProperty(nameof(Group.Actor), nameof(GroupListDto.ActorBackgroundColor), Use = nameof(BackgroundColor))]
    [MapProperty(nameof(Group.Actor), nameof(GroupListDto.ActorBackgroundEffect), Use = nameof(BackgroundEffect))]
    [MapProperty(nameof(Group.Actor), nameof(GroupListDto.ActorBannerColor), Use = nameof(BannerColor))]
    public static partial GroupListDto ToGroupListItem(Group source);

    private static Guid? ProfileId(Actor? actor) => actor?.Id;
    private static string? ProfileName(Actor? actor) => actor?.Pii?.DisplayName;
    private static string? ProfileHandle(Actor? actor) => actor?.AtprotoIdentities.FirstOrDefault()?.Handle;
    private static string? ProfilePicture(Actor? actor) => actor?.Pii?.ProfilePictureUri;
    private static string? BackgroundColor(Actor? actor) => actor?.BackgroundColor;
    private static string? BackgroundEffect(Actor? actor) => actor?.BackgroundEffect;
    private static string? BannerColor(Actor? actor) => actor?.BannerColor;
    // Existing transport contract has a required CLR string whose unresolved runtime value is null.
    private static string UnresolvedApprovalName() => null!;
}
