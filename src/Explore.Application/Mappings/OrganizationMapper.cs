using Explore.Application.DTOs.Group;
using Explore.Application.DTOs.GroupMember;
using Explore.Application.DTOs.Organization;
using Explore.Application.DTOs.OrganizationMember;
using Explore.Application.DTOs.OrganizationReview;
using Explore.Application.DTOs.StatusType;
using Explore.Domain.Enums;
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

    // Membership reads expose only the existing contact/role/position scalars, never tenant or audit graphs.
    [MapperIgnoreSource(nameof(GroupMember.GroupTenantId))]
    [MapperIgnoreSource(nameof(GroupMember.TenantId))]
    [MapperIgnoreSource(nameof(GroupMember.Tenant))]
    [MapperIgnoreSource(nameof(GroupMember.CreatedAt))]
    [MapperIgnoreSource(nameof(GroupMember.CreatedBy))]
    [MapperIgnoreSource(nameof(GroupMember.UpdatedAt))]
    [MapperIgnoreSource(nameof(GroupMember.UpdatedBy))]
    [MapperIgnoreSource(nameof(GroupMember.IsDeleted))]
    [MapperIgnoreSource(nameof(GroupMember.DeletedAt))]
    [MapperIgnoreSource(nameof(GroupMember.DeletedBy))]
    // GroupId was not populated by the old profile; participation is used only for the name.
    [MapperIgnoreTarget(nameof(GroupMemberDto.GroupId))]
    [MapProperty(nameof(GroupMember.GroupTenant), nameof(GroupMemberDto.GroupFullName), Use = nameof(GroupName))]
    [MapProperty(nameof(GroupMember.User), nameof(GroupMemberDto.UserEmail), Use = nameof(MemberEmail))]
    [MapProperty(nameof(GroupMember.User), nameof(GroupMemberDto.UserFullName), Use = nameof(MemberName))]
    [MapProperty(nameof(GroupMember.Role), nameof(GroupMemberDto.RoleName), Use = nameof(RoleName))]
    [MapProperty(nameof(GroupMember.GroupPosition), nameof(GroupMemberDto.GroupPositionFullName), Use = nameof(GroupPositionName))]
    public static partial GroupMemberDto ToGroupMember(GroupMember source);

    // Absent contact PII projects to null without invoking proxy getters or reconstructing erased data.
    [MapperIgnoreSource(nameof(Organization.FullName))]
    [MapperIgnoreSource(nameof(Organization.Email))]
    [MapperIgnoreSource(nameof(Organization.Country))]
    [MapperIgnoreSource(nameof(Organization.City))]
    [MapperIgnoreSource(nameof(Organization.Postcode))]
    [MapperIgnoreSource(nameof(Organization.Address))]
    [MapperIgnoreSource(nameof(Organization.TenantParticipations))]
    [MapperIgnoreSource(nameof(Organization.CreatedAt))]
    [MapperIgnoreSource(nameof(Organization.CreatedBy))]
    [MapperIgnoreSource(nameof(Organization.UpdatedAt))]
    [MapperIgnoreSource(nameof(Organization.UpdatedBy))]
    [MapperIgnoreSource(nameof(Organization.IsDeleted))]
    [MapperIgnoreSource(nameof(Organization.DeletedAt))]
    [MapperIgnoreSource(nameof(Organization.DeletedBy))]
    [MapperIgnoreTarget(nameof(OrganizationDto.TenantId))]
    [MapperIgnoreTarget(nameof(OrganizationDto.TenantFullName))]
    [MapperIgnoreTarget(nameof(OrganizationDto.ApprovalStatusId))]
    [MapperIgnoreTarget(nameof(OrganizationDto.ApprovalStatusFullName))]
    [MapperIgnoreTarget(nameof(OrganizationDto.ApprovalStatusMasterCode))]
    [MapperIgnoreTarget(nameof(OrganizationDto.ActorProfilePictureId))]
    [MapperIgnoreTarget(nameof(OrganizationDto.ActorBannerPictureId))]
    [MapperIgnoreTarget(nameof(OrganizationDto.ActorBannerPictureUri))]
    [MapperIgnoreTarget(nameof(OrganizationDto.ActorBackgroundImageId))]
    [MapperIgnoreTarget(nameof(OrganizationDto.ActorBackgroundImageUri))]
    [MapProperty(nameof(Organization.Pii), nameof(OrganizationDto.FullName), Use = nameof(OrganizationName))]
    [MapProperty(nameof(Organization.Pii), nameof(OrganizationDto.Email), Use = nameof(OrganizationEmail))]
    [MapProperty(nameof(Organization.Pii), nameof(OrganizationDto.Country), Use = nameof(OrganizationCountry))]
    [MapProperty(nameof(Organization.Pii), nameof(OrganizationDto.City), Use = nameof(OrganizationCity))]
    [MapProperty(nameof(Organization.Pii), nameof(OrganizationDto.Postcode), Use = nameof(OrganizationPostcode))]
    [MapProperty(nameof(Organization.Pii), nameof(OrganizationDto.Address), Use = nameof(OrganizationAddress))]
    [MapProperty(nameof(Organization.Actor), nameof(OrganizationDto.ActorId), Use = nameof(ProfileId))]
    [MapProperty(nameof(Organization.Actor), nameof(OrganizationDto.ActorDisplayName), Use = nameof(ProfileName))]
    [MapProperty(nameof(Organization.Actor), nameof(OrganizationDto.ActorHandle), Use = nameof(ProfileHandle))]
    [MapProperty(nameof(Organization.Actor), nameof(OrganizationDto.ActorProfilePictureUri), Use = nameof(ProfilePicture))]
    [MapProperty(nameof(Organization.Actor), nameof(OrganizationDto.ActorBackgroundColor), Use = nameof(BackgroundColor))]
    [MapProperty(nameof(Organization.Actor), nameof(OrganizationDto.ActorBackgroundEffect), Use = nameof(BackgroundEffect))]
    [MapProperty(nameof(Organization.Actor), nameof(OrganizationDto.ActorBannerColor), Use = nameof(BannerColor))]
    public static partial OrganizationDto ToOrganizationDetail(Organization source);

    // List creation time is public; membership role enrichment and tenant/approval authority are not inferred from navigations.
    [MapperIgnoreSource(nameof(Organization.FullName))]
    [MapperIgnoreSource(nameof(Organization.Email))]
    [MapperIgnoreSource(nameof(Organization.Country))]
    [MapperIgnoreSource(nameof(Organization.City))]
    [MapperIgnoreSource(nameof(Organization.Postcode))]
    [MapperIgnoreSource(nameof(Organization.Address))]
    [MapperIgnoreSource(nameof(Organization.TenantParticipations))]
    [MapperIgnoreSource(nameof(Organization.CreatedBy))]
    [MapperIgnoreSource(nameof(Organization.UpdatedAt))]
    [MapperIgnoreSource(nameof(Organization.UpdatedBy))]
    [MapperIgnoreSource(nameof(Organization.IsDeleted))]
    [MapperIgnoreSource(nameof(Organization.DeletedAt))]
    [MapperIgnoreSource(nameof(Organization.DeletedBy))]
    [MapperIgnoreTarget(nameof(OrganizationListDto.TenantId))]
    [MapperIgnoreTarget(nameof(OrganizationListDto.ApprovalStatusId))]
    [MapValue(nameof(OrganizationListDto.ApprovalStatusFullName), Use = nameof(UnresolvedApprovalName))]
    [MapperIgnoreTarget(nameof(OrganizationListDto.CurrentUserRoleId))]
    [MapperIgnoreTarget(nameof(OrganizationListDto.ActorProfilePictureId))]
    [MapperIgnoreTarget(nameof(OrganizationListDto.ActorBannerPictureId))]
    [MapperIgnoreTarget(nameof(OrganizationListDto.ActorBannerPictureUri))]
    [MapperIgnoreTarget(nameof(OrganizationListDto.ActorBackgroundImageId))]
    [MapperIgnoreTarget(nameof(OrganizationListDto.ActorBackgroundImageUri))]
    [MapProperty(nameof(Organization.Pii), nameof(OrganizationListDto.FullName), Use = nameof(OrganizationName))]
    [MapProperty(nameof(Organization.Pii), nameof(OrganizationListDto.Email), Use = nameof(OrganizationEmail))]
    [MapProperty(nameof(Organization.Pii), nameof(OrganizationListDto.Country), Use = nameof(OrganizationCountry))]
    [MapProperty(nameof(Organization.Pii), nameof(OrganizationListDto.City), Use = nameof(OrganizationCity))]
    [MapProperty(nameof(Organization.Pii), nameof(OrganizationListDto.Postcode), Use = nameof(OrganizationPostcode))]
    [MapProperty(nameof(Organization.Pii), nameof(OrganizationListDto.Address), Use = nameof(OrganizationAddress))]
    [MapProperty(nameof(Organization.Actor), nameof(OrganizationListDto.ActorProfilePictureUri), Use = nameof(ProfilePicture))]
    [MapProperty(nameof(Organization.Actor), nameof(OrganizationListDto.ActorBackgroundColor), Use = nameof(BackgroundColor))]
    [MapProperty(nameof(Organization.Actor), nameof(OrganizationListDto.ActorBackgroundEffect), Use = nameof(BackgroundEffect))]
    [MapProperty(nameof(Organization.Actor), nameof(OrganizationListDto.ActorBannerColor), Use = nameof(BannerColor))]
    public static partial OrganizationListDto ToOrganizationListItem(Organization source);

    // Member detail retains tenant identity but not participation identifiers, navigation graphs or audit state.
    [MapperIgnoreSource(nameof(OrganizationMember.OrganizationTenantId))]
    [MapperIgnoreSource(nameof(OrganizationMember.Tenant))]
    [MapperIgnoreSource(nameof(OrganizationMember.CreatedAt))]
    [MapperIgnoreSource(nameof(OrganizationMember.CreatedBy))]
    [MapperIgnoreSource(nameof(OrganizationMember.UpdatedAt))]
    [MapperIgnoreSource(nameof(OrganizationMember.UpdatedBy))]
    [MapperIgnoreSource(nameof(OrganizationMember.IsDeleted))]
    [MapperIgnoreSource(nameof(OrganizationMember.DeletedAt))]
    [MapperIgnoreSource(nameof(OrganizationMember.DeletedBy))]
    // Unlike invitations, the old member detail did not populate OrganizationId.
    [MapperIgnoreTarget(nameof(OrganizationMemberDto.OrganizationId))]
    [MapProperty(nameof(OrganizationMember.OrganizationTenant), nameof(OrganizationMemberDto.OrganizationFullName), Use = nameof(ParticipationOrganizationName))]
    [MapProperty(nameof(OrganizationMember.User), nameof(OrganizationMemberDto.UserEmail), Use = nameof(MemberEmail))]
    [MapProperty(nameof(OrganizationMember.User), nameof(OrganizationMemberDto.UserFullName), Use = nameof(MemberName))]
    [MapProperty(nameof(OrganizationMember.Role), nameof(OrganizationMemberDto.RoleName), Use = nameof(RoleName))]
    [MapProperty(nameof(OrganizationMember.OrganizationPosition), nameof(OrganizationMemberDto.OrganizationPositionFullName), Use = nameof(OrganizationPositionName))]
    public static partial OrganizationMemberDto ToOrganizationMember(OrganizationMember source);

    // Invitations have a separate minimal disclosure contract and take the organization identity from participation.
    [MapperIgnoreSource(nameof(OrganizationMember.OrganizationTenantId))]
    [MapperIgnoreSource(nameof(OrganizationMember.UserId))]
    [MapperIgnoreSource(nameof(OrganizationMember.Role))]
    [MapperIgnoreSource(nameof(OrganizationMember.OrganizationPositionId))]
    [MapperIgnoreSource(nameof(OrganizationMember.OrganizationPosition))]
    [MapperIgnoreSource(nameof(OrganizationMember.TenantId))]
    [MapperIgnoreSource(nameof(OrganizationMember.Tenant))]
    [MapperIgnoreSource(nameof(OrganizationMember.CreatedAt))]
    [MapperIgnoreSource(nameof(OrganizationMember.CreatedBy))]
    [MapperIgnoreSource(nameof(OrganizationMember.UpdatedAt))]
    [MapperIgnoreSource(nameof(OrganizationMember.UpdatedBy))]
    [MapperIgnoreSource(nameof(OrganizationMember.IsDeleted))]
    [MapperIgnoreSource(nameof(OrganizationMember.DeletedAt))]
    [MapperIgnoreSource(nameof(OrganizationMember.DeletedBy))]
    [MapProperty(nameof(OrganizationMember.OrganizationTenant), nameof(OrganizationInvitationDto.OrganizationId), Use = nameof(ParticipationOrganizationId))]
    [MapProperty(nameof(OrganizationMember.OrganizationTenant), nameof(OrganizationInvitationDto.OrganizationName), Use = nameof(ParticipationOrganizationName))]
    [MapProperty(nameof(OrganizationMember.User), nameof(OrganizationInvitationDto.Email), Use = nameof(MemberEmail))]
    [MapProperty(nameof(OrganizationMember.RoleId), nameof(OrganizationInvitationDto.Role), Use = nameof(InvitationRole))]
    public static partial OrganizationInvitationDto ToOrganizationInvitation(OrganizationMember source);

    // Review transport omits the submitted reviewer name, event, tenant and audit authors.
    [MapperIgnoreSource(nameof(OrganizationReview.EventId))]
    [MapperIgnoreSource(nameof(OrganizationReview.Event))]
    [MapperIgnoreSource(nameof(OrganizationReview.ReviewerName))]
    [MapperIgnoreSource(nameof(OrganizationReview.TenantId))]
    [MapperIgnoreSource(nameof(OrganizationReview.Tenant))]
    [MapperIgnoreSource(nameof(OrganizationReview.CreatedBy))]
    [MapperIgnoreSource(nameof(OrganizationReview.UpdatedAt))]
    [MapperIgnoreSource(nameof(OrganizationReview.UpdatedBy))]
    [MapperIgnoreSource(nameof(OrganizationReview.IsDeleted))]
    [MapperIgnoreSource(nameof(OrganizationReview.DeletedAt))]
    [MapperIgnoreSource(nameof(OrganizationReview.DeletedBy))]
    [MapProperty(nameof(OrganizationReview.Organization), nameof(OrganizationReviewDto.OrganizationFullName), Use = nameof(ReviewOrganizationName))]
    [MapProperty(nameof(OrganizationReview.User), nameof(OrganizationReviewDto.UserFullName), Use = nameof(MemberName))]
    public static partial OrganizationReviewDto ToOrganizationReview(OrganizationReview source);

    public static partial StatusTypeListDto ToApprovalStatus(ApprovalStatus source);

    private static string? ReviewOrganizationName(Organization? organization) => organization?.Pii?.FullName;

    private static string? ParticipationOrganizationName(OrganizationTenant? participation) => participation?.Organization?.Pii?.FullName;
    private static string? OrganizationPositionName(OrganizationPosition? position) => position?.FullName;
    private static Guid ParticipationOrganizationId(OrganizationTenant? participation) => participation?.OrganizationId ?? Guid.Empty;
    private static RoleEnum InvitationRole(int roleId) => (RoleEnum)roleId;

    // Absent PII remains absent in every contact projection.
    private static string? OrganizationName(OrganizationPii? pii) => pii?.FullName;
    private static string? OrganizationEmail(OrganizationPii? pii) => pii?.Email;
    private static string? OrganizationCountry(OrganizationPii? pii) => pii?.Country;
    private static string? OrganizationCity(OrganizationPii? pii) => pii?.City;
    private static string? OrganizationPostcode(OrganizationPii? pii) => pii?.Postcode;
    private static string? OrganizationAddress(OrganizationPii? pii) => pii?.Address;

    private static string? GroupName(GroupTenant? participation) => participation?.Group?.FullName;
    private static string? MemberEmail(User? user) => user?.Pii?.Email;
    private static string? MemberName(User? user) => user?.Pii is { } pii ? $"{pii.FirstName} {pii.LastName}" : null;
    private static string? RoleName(Role? role) => role?.FullName;
    private static string? GroupPositionName(GroupPosition? position) => position?.FullName;

    private static Guid? ProfileId(Actor? actor) => actor?.Id;
    private static string? ProfileName(Actor? actor) => actor?.Pii?.DisplayName;
    private static string? ProfileHandle(Actor? actor) => actor?.AtprotoIdentities.FirstOrDefault()?.Handle;
    private static string? ProfilePicture(Actor? actor) => actor?.Pii?.ProfilePictureUri;
    private static string? BackgroundColor(Actor? actor) => actor?.BackgroundColor;
    private static string? BackgroundEffect(Actor? actor) => actor?.BackgroundEffect;
    private static string? BannerColor(Actor? actor) => actor?.BannerColor;
    // Approval labels are unresolved in these base projections.
    private static string? UnresolvedApprovalName() => null;
}
