using Explore.Domain;
using Explore.Domain.Settings.Documents;
using Explore.Persistence.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Security;

internal static partial class ManagedProviderProvisioningDataModel
{
    internal sealed record Relation(Type Type, string Key, string[] Columns, bool Insert, string[] Update);
    private static string[] Audited(params string[] columns) =>
        [.. columns, "CreatedAt", "CreatedBy", "UpdatedAt", "UpdatedBy"];
    private static string[] Soft(params string[] columns) =>
        Audited([.. columns, "IsDeleted", "DeletedAt", "DeletedBy"]);

    internal static readonly Relation[] Inventory =
    [
        new(typeof(Tenant), "Id", Audited("Id", "FullName", "Slug", "Description", "TenantStatusId"), true, ["Id"]),
        new(typeof(TenantSettingsDocument), "Id", Audited("Id", "TenantId", "DocumentKey", "SchemaVersion",
            "DefaultsVersion", "PayloadJson", "ConcurrencyStamp"), true, []),
        new(typeof(User), "Id", Soft("Id", "EmailVerified", "ConcurrencyStamp", "LastActiveTenantId"), true,
            ["Id", "EmailVerified", "ConcurrencyStamp", "UpdatedAt", "UpdatedBy"]),
        new(typeof(UserPii), "UserId", ["UserId", "Email", "FirstName", "LastName"], true, []),
        new(typeof(Actor), "Id", Soft("Id", "ActorTypeId", "UserId", "OrganizationId", "GroupId", "ExternalActorSubjectId",
            "ServicePrincipalId", "IsSuspended", "SuspendedAt", "SuspendedBy", "ModerationReasonCode", "Description",
            "ProfilePictureCid", "BackgroundColor", "BackgroundEffect", "BannerColor", "ConcurrencyStamp"), true, ["Id"]),
        new(typeof(ActorPii), "ActorId", ["ActorId", "DisplayName", "ProfilePictureStorageObjectId", "ExternalProfilePictureUri"], true, []),
        new(typeof(UserExternalLogin), "Id", Audited("Id", "UserId", "AuthenticationProviderId", "ProviderKey", "ProviderDisplayName"), true, []),
        new(typeof(TenantUser), "Id", Soft("Id", "TenantId", "UserId", "ActorId", "StatusId", "JoinedAt",
            "SuspendedAt", "SuspendedBy", "BanExpiresAt", "RemovedAt", "RemovedBy", "ModerationNote"), true, []),
        new(typeof(TenantUserProfile), "Id", Audited("Id", "TenantId", "TenantUserId", "DisplayNameOverride", "ContactEmailOverride",
            "Locale", "TimeZone", "PreferencesJson", "ConsentJson", "AdminNote"), true, []),
        new(typeof(TenantUserRoleGrant), "Id", Audited("Id", "TenantId", "TenantUserId", "RoleId", "RoleScopeId",
            "GrantedAt", "GrantedBy", "RevokedAt", "RevokedBy", "RevocationReason"), true, []),
        new(typeof(Organization), "Id", Soft("Id", "WebsiteUrl", "ConcurrencyStamp"), true, ["Id"]),
        new(typeof(OrganizationPii), "OrganizationId", ["OrganizationId", "FullName", "Email", "Country", "City", "Address", "Postcode"], true, []),
        new(typeof(Group), "Id", Soft("Id", "FullName", "Description", "ConcurrencyStamp"), true, ["Id"]),
        new(typeof(OrganizationTenant), "Id", Soft("Id", "TenantId", "OrganizationId", "ApprovalStatusId", "IsVisible",
            "IsOrganizerEligible", "IsSuspended", "SuspendedAt", "SuspendedBy", "ModerationNote", "DisplayNameOverride",
            "DescriptionOverride", "WebsiteUrlOverride", "ContactEmailOverride", "ProfilePictureId", "BannerPictureId",
            "BackgroundImageId", "BackgroundColor", "BackgroundEffect", "BannerColor", "ApprovedAt", "ApprovedBy",
            "ApprovalNotes", "ConcurrencyStamp"), true, []),
        new(typeof(GroupTenant), "Id", Soft("Id", "TenantId", "GroupId", "ApprovalStatusId", "IsVisible",
            "IsOrganizerEligible", "IsSuspended", "SuspendedAt", "SuspendedBy", "ModerationNote", "DisplayNameOverride",
            "DescriptionOverride", "ProfilePictureId", "BannerPictureId", "BackgroundImageId", "BackgroundColor",
            "BackgroundEffect", "BannerColor", "ParentOrganizationTenantId", "ParentGroupTenantId", "ApprovedAt",
            "ApprovedBy", "ApprovalNotes", "ConcurrencyStamp"), true, []),
        new(typeof(OrganizationMember), "Id", Soft("Id", "OrganizationTenantId", "UserId", "RoleId", "OrganizationPositionId", "TenantId"), true, []),
        new(typeof(GroupMember), "Id", Soft("Id", "GroupTenantId", "UserId", "RoleId", "GroupPositionId", "TenantId"), true, []),
        new(typeof(ExternalBinding), "Id", Audited("Id", "ProviderKey", "ExternalSystem", "ExternalType", "ExternalId",
            "InternalType", "InternalId", "ScopeTenantId", "ExternalBindingStatusId", "MetadataJson", "LastSeenAt"), true,
            ["LastSeenAt", "UpdatedAt", "UpdatedBy"]),
        new(typeof(EventDiscoveryRevision), "Id", ["Id", "TenantId", "IdentityEpoch", "DisclosureEpoch"], true,
            ["IdentityEpoch", "DisclosureEpoch"]),
        new(typeof(Role), "Id", ["Id", "MasterCode", "FullName", "Description", "RoleScopeId", "IsSystem"], false, []),
        new(typeof(LocalIdentityUser), "Id", ["Id", "AccessFailedCount", "ConcurrencyStamp", "CreatedAt", "Email", "EmailConfirmed",
            "FirstName", "LastName", "LockoutEnabled", "LockoutEnd", "NormalizedEmail", "NormalizedUserName", "PasswordHash",
            "PhoneNumber", "PhoneNumberConfirmed", "SecurityStamp", "TwoFactorEnabled", "UpdatedAt", "UserName"], false, []),
        new(typeof(IdentityUserToken<Guid>), "UserId", ["UserId", "LoginProvider", "Name", "Value"], false, []),
        new(typeof(LocalIdentityCredentialOperation), "Id", ["Id", "ConcurrencyStamp", "CreatedAt", "ExternalLoginId",
            "InitiatingApplicationUserId", "Kind", "LocalSubjectId", "PersonalActorId", "PreviousOperationConcurrencyStamp",
            "PreviousOperationId", "ResetReason", "Stage", "UpdatedAt", "VerifiedAt", "VerifiedByApplicationUserId"], false, [])
    ];

    // Read-only aggregate inputs. No worker payload, generation or reservation write
    // capability is exposed to human provisioning.
    internal static readonly Relation[] CapacitySources =
    [
        new(typeof(InstanceBootstrapState), "Id", ["Id", "Status", "DeploymentMode", "Generation", "CreatedAt"], false, []),
        new(typeof(ManagedTenantProvisioningOperation), "Id", ["Id", "Status"], false, []),
        new(typeof(TenantStatus), "Id", ["Id", "IsActiveState"], false, [])
    ];

    internal static void RequireInventory(ExploreDbContext context)
    {
        foreach (var relation in Inventory)
        {
            var entity = context.Model.FindEntityType(relation.Type)
                ?? throw new NotSupportedException("provisioning_data_colocated_model_required");
            if (entity.GetTableName() is null || relation.Columns.Any(column => entity.FindProperty(column) is null)
                || !entity.GetProperties().Select(property => property.Name).Order(StringComparer.Ordinal)
                    .SequenceEqual(relation.Columns.Order(StringComparer.Ordinal), StringComparer.Ordinal)
                || relation.Columns.Any(column => entity.FindProperty(column)!.IsShadowProperty()))
                throw new InvalidOperationException("provisioning_data_inventory_drift");
        }
    }

}
