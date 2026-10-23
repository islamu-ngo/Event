using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests;

/// <summary>Pure persistence-vocabulary checks; no database or authority is constructed.</summary>
public sealed class HumanSecuritySourceMutationClassifierTests
{
    [Test]
    [Arguments(typeof(User), nameof(User.IsDeleted))]
    [Arguments(typeof(UserExternalLogin), nameof(UserExternalLogin.ProviderKey))]
    [Arguments(typeof(PlatformUserRole), nameof(PlatformUserRole.RoleId))]
    [Arguments(typeof(TenantUser), nameof(TenantUser.StatusId))]
    [Arguments(typeof(TenantUserRoleGrant), nameof(TenantUserRoleGrant.RevokedAt))]
    [Arguments(typeof(OrganizationMember), nameof(OrganizationMember.RoleId))]
    [Arguments(typeof(GroupMember), nameof(GroupMember.IsDeleted))]
    [Arguments(typeof(Role), nameof(Role.MasterCode))]
    [Arguments(typeof(RolePermission), nameof(RolePermission.PermissionId))]
    [Arguments(typeof(Permission), nameof(Permission.IsActive))]
    [Arguments(typeof(Tenant), nameof(Tenant.TenantStatusId))]
    [Arguments(typeof(TenantStatus), nameof(TenantStatus.IsActiveState))]
    [Arguments(typeof(OrganizationTenant), nameof(OrganizationTenant.ApprovalStatusId))]
    [Arguments(typeof(Organization), nameof(Organization.IsDeleted))]
    [Arguments(typeof(GroupTenant), nameof(GroupTenant.ApprovalStatusId))]
    [Arguments(typeof(Group), nameof(Group.IsDeleted))]
    [Arguments(typeof(Actor), nameof(Actor.IsSuspended))]
    public async Task ExactSourceFieldsAndMembershipChangesRequireTheGate(Type entity, string field)
    {
        await Assert.That(HumanSecuritySourceMutationClassifier.RequiresGate(entity, EntityState.Modified, [field])).IsTrue();
        await Assert.That(HumanSecuritySourceMutationClassifier.RequiresGate(entity, EntityState.Added, [])).IsTrue();
        await Assert.That(HumanSecuritySourceMutationClassifier.RequiresGate(entity, EntityState.Deleted, [])).IsTrue();
        await Assert.That(HumanSecuritySourceMutationClassifier.RequiresGate(entity, EntityState.Unchanged, [field])).IsFalse();
        await Assert.That(HumanSecuritySourceMutationClassifier.RequiresGate(entity, EntityState.Modified, ["UpdatedAt"])).IsFalse();
    }

    [Test]
    [Arguments(typeof(SystemSetting))]
    [Arguments(typeof(TenantSetting))]
    public async Task RenamingEitherIntoOrOutOfAnyRouteKeyCannotEscapeClassification(Type entity)
    {
        foreach (string key in HumanAuthorizationRouteFacts.Keys)
        {
            await Assert.That(HumanSecuritySourceMutationClassifier.RequiresGate(entity, EntityState.Modified,
                [nameof(SystemSetting.SettingKey)], key, "unrelated.setting")).IsTrue();
            await Assert.That(HumanSecuritySourceMutationClassifier.RequiresGate(entity, EntityState.Modified,
                [nameof(SystemSetting.SettingKey)], "unrelated.setting", key)).IsTrue();
            await Assert.That(HumanSecuritySourceMutationClassifier.RequiresGate(entity, EntityState.Deleted, [], key, key)).IsTrue();
            await Assert.That(HumanSecuritySourceMutationClassifier.RequiresGate(entity, EntityState.Added, [], null, key)).IsTrue();
        }
    }

    [Test]
    public async Task RouteMetadataAndTenantPlacementAreSourceFactsButUnrelatedAuditIsNot()
    {
        string key = GovernanceSettingKeys.Security.AuthorizationProvider;
        foreach (string field in new[] { "Value", "SettingValueTypeId", "IsLocked", "Description", "Category", "AllowedValues" })
            await Assert.That(HumanSecuritySourceMutationClassifier.RequiresGate(typeof(SystemSetting), EntityState.Modified, [field], key, key)).IsTrue();
        await Assert.That(HumanSecuritySourceMutationClassifier.RequiresGate(typeof(TenantSetting), EntityState.Modified, ["TenantId"], key, key)).IsTrue();
        await Assert.That(HumanSecuritySourceMutationClassifier.RequiresGate(typeof(SystemSetting), EntityState.Modified, ["UpdatedBy"], key, key)).IsFalse();
    }

    [Test]
    public async Task EmailDeliveryUsesOnlyItsInstanceValueAndKeyNotTenantOrUnrelatedMetadata()
    {
        string key = GovernanceSettingKeys.Email.DeliveryEnabled;
        await Assert.That(HumanSecuritySourceMutationClassifier.RequiresGate(typeof(SystemSetting), EntityState.Modified, ["Value"], key, key)).IsTrue();
        await Assert.That(HumanSecuritySourceMutationClassifier.RequiresGate(typeof(SystemSetting), EntityState.Modified, ["SettingKey"], key, "other")).IsTrue();
        await Assert.That(HumanSecuritySourceMutationClassifier.RequiresGate(typeof(SystemSetting), EntityState.Modified, ["IsLocked"], key, key)).IsFalse();
        await Assert.That(HumanSecuritySourceMutationClassifier.RequiresGate(typeof(TenantSetting), EntityState.Modified, ["Value"], key, key)).IsFalse();
    }

    [Test]
    public async Task UnrelatedValuesPiiAndPresentationChangesDoNotSerializeSecurityReaders()
    {
        await Assert.That(HumanSecuritySourceMutationClassifier.RequiresGate(typeof(SystemSetting), EntityState.Modified, ["Value"], "other", "other")).IsFalse();
        await Assert.That(HumanSecuritySourceMutationClassifier.RequiresGate(typeof(UserPii), EntityState.Added, [])).IsFalse();
        await Assert.That(HumanSecuritySourceMutationClassifier.RequiresGate(typeof(Actor), EntityState.Modified, [nameof(Actor.Description)])).IsFalse();
        await Assert.That(HumanSecuritySourceMutationClassifier.RequiresGate(typeof(Permission), EntityState.Modified, [nameof(Permission.FullName)])).IsFalse();
    }
}
