using System.Collections.Immutable;
using Explore.Domain;
using Explore.Domain.Settings.Documents;
using Explore.Domain.Modules;

namespace Explore.Persistence.Security;

/// <summary>
/// Closed model-property manifest for the dedicated admitted-worker creator.
/// These declarations are storage permissions, not caller or graph authority.
/// New model properties never enter this inventory through reflection or live ACLs.
/// </summary>
internal static class ManagedTenantWorkerCreationInventory
{
    internal sealed record Source(Type Entity, ImmutableArray<string> Read,
        ImmutableArray<string> Insert, ImmutableArray<string> Update);

    internal static ImmutableArray<Source> Sources { get; } =
    [
        new(typeof(Tenant), [nameof(Tenant.Id), nameof(Tenant.Slug), nameof(Tenant.TenantStatusId)],
            [nameof(Tenant.Id), nameof(Tenant.FullName), nameof(Tenant.Slug), nameof(Tenant.Description), nameof(Tenant.TenantStatusId), nameof(Tenant.CreatedAt), nameof(Tenant.CreatedBy), nameof(Tenant.UpdatedAt), nameof(Tenant.UpdatedBy)], []),
        new(typeof(TenantStatus), [nameof(TenantStatus.Id), nameof(TenantStatus.IsActiveState)], [], []),
        new(typeof(InstanceBootstrapState), [nameof(InstanceBootstrapState.Id), nameof(InstanceBootstrapState.Status),
            nameof(InstanceBootstrapState.DeploymentMode), nameof(InstanceBootstrapState.Generation), nameof(InstanceBootstrapState.CreatedAt)], [], []),
        new(typeof(Role), [nameof(Role.Id), nameof(Role.MasterCode), nameof(Role.RoleScopeId)], [], []),
        new(typeof(TenantSettingsDocument), [], [nameof(TenantSettingsDocument.Id), nameof(TenantSettingsDocument.TenantId), nameof(TenantSettingsDocument.DocumentKey), nameof(TenantSettingsDocument.SchemaVersion), nameof(TenantSettingsDocument.DefaultsVersion),
            nameof(TenantSettingsDocument.PayloadJson), nameof(TenantSettingsDocument.ConcurrencyStamp), nameof(TenantSettingsDocument.CreatedAt), nameof(TenantSettingsDocument.CreatedBy), nameof(TenantSettingsDocument.UpdatedAt), nameof(TenantSettingsDocument.UpdatedBy)], []),
        new(typeof(EventDiscoveryRevision), [], [nameof(EventDiscoveryRevision.Id), nameof(EventDiscoveryRevision.TenantId), nameof(EventDiscoveryRevision.IdentityEpoch), nameof(EventDiscoveryRevision.DisclosureEpoch)], []),
        new(typeof(TenantUser), [], [nameof(TenantUser.Id), nameof(TenantUser.TenantId), nameof(TenantUser.UserId), nameof(TenantUser.ActorId), nameof(TenantUser.StatusId), nameof(TenantUser.JoinedAt), nameof(TenantUser.SuspendedAt), nameof(TenantUser.SuspendedBy),
            nameof(TenantUser.BanExpiresAt), nameof(TenantUser.RemovedAt), nameof(TenantUser.RemovedBy), nameof(TenantUser.ModerationNote), nameof(TenantUser.CreatedAt), nameof(TenantUser.CreatedBy), nameof(TenantUser.UpdatedAt), nameof(TenantUser.UpdatedBy),
            nameof(TenantUser.IsDeleted), nameof(TenantUser.DeletedAt), nameof(TenantUser.DeletedBy)], []),
        new(typeof(TenantUserRoleGrant), [], [nameof(TenantUserRoleGrant.Id), nameof(TenantUserRoleGrant.TenantId), nameof(TenantUserRoleGrant.TenantUserId), nameof(TenantUserRoleGrant.RoleId), nameof(TenantUserRoleGrant.RoleScopeId), nameof(TenantUserRoleGrant.GrantedAt), nameof(TenantUserRoleGrant.GrantedBy),
            nameof(TenantUserRoleGrant.RevokedAt), nameof(TenantUserRoleGrant.RevokedBy), nameof(TenantUserRoleGrant.RevocationReason), nameof(TenantUserRoleGrant.CreatedAt), nameof(TenantUserRoleGrant.CreatedBy), nameof(TenantUserRoleGrant.UpdatedAt), nameof(TenantUserRoleGrant.UpdatedBy)], []),
        new(typeof(TenantUserProfile), [], [nameof(TenantUserProfile.Id), nameof(TenantUserProfile.TenantId), nameof(TenantUserProfile.TenantUserId), nameof(TenantUserProfile.DisplayNameOverride), nameof(TenantUserProfile.ContactEmailOverride), nameof(TenantUserProfile.Locale),
            nameof(TenantUserProfile.TimeZone), nameof(TenantUserProfile.PreferencesJson), nameof(TenantUserProfile.ConsentJson), nameof(TenantUserProfile.AdminNote), nameof(TenantUserProfile.CreatedAt), nameof(TenantUserProfile.CreatedBy), nameof(TenantUserProfile.UpdatedAt), nameof(TenantUserProfile.UpdatedBy)], []),
        new(typeof(TenantPlanAssignment), [], [nameof(TenantPlanAssignment.Id), nameof(TenantPlanAssignment.TenantId), nameof(TenantPlanAssignment.TenantPlanId), nameof(TenantPlanAssignment.TenantPlanVersionId), nameof(TenantPlanAssignment.TenantPlanAssignmentStatusId),
            nameof(TenantPlanAssignment.AssignedByUserId), nameof(TenantPlanAssignment.AssignedAt), nameof(TenantPlanAssignment.EndedAt), nameof(TenantPlanAssignment.CreatedAt), nameof(TenantPlanAssignment.CreatedBy), nameof(TenantPlanAssignment.UpdatedAt), nameof(TenantPlanAssignment.UpdatedBy)], []),
        new(typeof(TenantOnboardingState), [], [nameof(TenantOnboardingState.Id), nameof(TenantOnboardingState.TenantId), nameof(TenantOnboardingState.IsCompleted), nameof(TenantOnboardingState.CurrentStep), nameof(TenantOnboardingState.TotalSteps), nameof(TenantOnboardingState.CompletedStepsJson),
            nameof(TenantOnboardingState.CreatedAt), nameof(TenantOnboardingState.CompletedAt), nameof(TenantOnboardingState.CompletedByUserId)], []),
        new(typeof(TenantCapability), [], [nameof(TenantCapability.Id), nameof(TenantCapability.TenantId), nameof(TenantCapability.ModuleId), nameof(TenantCapability.IsEnabled), nameof(TenantCapability.EnabledAt), nameof(TenantCapability.EnabledBy), nameof(TenantCapability.ConfigurationJson),
            nameof(TenantCapability.CreatedAt), nameof(TenantCapability.CreatedBy), nameof(TenantCapability.UpdatedAt), nameof(TenantCapability.UpdatedBy)], []),
        new(typeof(TenantSetting), [], [nameof(TenantSetting.Id), nameof(TenantSetting.TenantId), nameof(TenantSetting.SettingKey), nameof(TenantSetting.Value), nameof(TenantSetting.IsLocked), nameof(TenantSetting.CreatedAt), nameof(TenantSetting.CreatedBy), nameof(TenantSetting.UpdatedAt), nameof(TenantSetting.UpdatedBy)], []),
        new(typeof(AtprotoFederationTenantSetting), [], [nameof(AtprotoFederationTenantSetting.Id), nameof(AtprotoFederationTenantSetting.TenantId), nameof(AtprotoFederationTenantSetting.SettingKey), nameof(AtprotoFederationTenantSetting.Value), nameof(AtprotoFederationTenantSetting.IsLocked), nameof(AtprotoFederationTenantSetting.CreatedAt), nameof(AtprotoFederationTenantSetting.CreatedBy), nameof(AtprotoFederationTenantSetting.UpdatedAt), nameof(AtprotoFederationTenantSetting.UpdatedBy)], []),
        new(typeof(ExternalBinding), [nameof(ExternalBinding.Id), nameof(ExternalBinding.ProviderKey), nameof(ExternalBinding.ExternalSystem), nameof(ExternalBinding.ExternalType), nameof(ExternalBinding.ExternalId), nameof(ExternalBinding.InternalType), nameof(ExternalBinding.InternalId),
                nameof(ExternalBinding.ScopeTenantId), nameof(ExternalBinding.ExternalBindingStatusId)],
            [nameof(ExternalBinding.Id), nameof(ExternalBinding.ProviderKey), nameof(ExternalBinding.ExternalSystem), nameof(ExternalBinding.ExternalType), nameof(ExternalBinding.ExternalId), nameof(ExternalBinding.InternalType), nameof(ExternalBinding.InternalId), nameof(ExternalBinding.ScopeTenantId),
                nameof(ExternalBinding.ExternalBindingStatusId), nameof(ExternalBinding.MetadataJson), nameof(ExternalBinding.LastSeenAt), nameof(ExternalBinding.CreatedAt), nameof(ExternalBinding.CreatedBy), nameof(ExternalBinding.UpdatedAt), nameof(ExternalBinding.UpdatedBy)], []),
        new(typeof(AuditLog), [], [nameof(AuditLog.Id), nameof(AuditLog.EntityType), nameof(AuditLog.EntityId), nameof(AuditLog.Action), nameof(AuditLog.OldValues), nameof(AuditLog.NewValues), nameof(AuditLog.AffectedColumns), nameof(AuditLog.ActorId), nameof(AuditLog.Timestamp), nameof(AuditLog.TenantId)], []),
        new(typeof(User), [nameof(User.Id), nameof(User.EmailVerified), nameof(User.ConcurrencyStamp), nameof(User.IsDeleted), nameof(User.DeletedAt), nameof(User.DeletedBy), nameof(User.UpdatedAt), nameof(User.UpdatedBy)],
            [nameof(User.Id), nameof(User.EmailVerified), nameof(User.ConcurrencyStamp), nameof(User.LastActiveTenantId), nameof(User.CreatedAt), nameof(User.CreatedBy), nameof(User.UpdatedAt), nameof(User.UpdatedBy), nameof(User.IsDeleted), nameof(User.DeletedAt), nameof(User.DeletedBy)],
            [nameof(User.EmailVerified), nameof(User.ConcurrencyStamp), nameof(User.UpdatedAt), nameof(User.UpdatedBy)]),
        new(typeof(UserPii), [], [nameof(UserPii.UserId), nameof(UserPii.Email), nameof(UserPii.FirstName), nameof(UserPii.LastName),
            "_dateOfBirth", "_birthDateCollectionReceiptId", "_birthDateRevision"], []),
        new(typeof(Actor), [nameof(Actor.Id), nameof(Actor.UserId), nameof(Actor.ActorTypeId), nameof(Actor.IsDeleted), nameof(Actor.IsSuspended), nameof(Actor.ConcurrencyStamp)],
            [nameof(Actor.Id), nameof(Actor.ActorTypeId), nameof(Actor.UserId), nameof(Actor.OrganizationId), nameof(Actor.GroupId), nameof(Actor.ExternalActorSubjectId), nameof(Actor.ServicePrincipalId), nameof(Actor.IsSuspended),
                nameof(Actor.SuspendedAt), nameof(Actor.SuspendedBy), nameof(Actor.ModerationReasonCode), nameof(Actor.Description), nameof(Actor.ProfilePictureCid), nameof(Actor.BackgroundColor), nameof(Actor.BackgroundEffect),
                nameof(Actor.BannerColor), nameof(Actor.CreatedAt), nameof(Actor.CreatedBy), nameof(Actor.UpdatedAt), nameof(Actor.UpdatedBy), nameof(Actor.IsDeleted), nameof(Actor.DeletedAt), nameof(Actor.DeletedBy), nameof(Actor.ConcurrencyStamp)], []),
        new(typeof(ActorPii), [], [nameof(ActorPii.ActorId), nameof(ActorPii.DisplayName), nameof(ActorPii.ProfilePictureStorageObjectId), nameof(ActorPii.ExternalProfilePictureUri)], []),
        new(typeof(UserExternalLogin), [nameof(UserExternalLogin.Id), nameof(UserExternalLogin.UserId), nameof(UserExternalLogin.AuthenticationProviderId), nameof(UserExternalLogin.ProviderKey)],
            [nameof(UserExternalLogin.Id), nameof(UserExternalLogin.UserId), nameof(UserExternalLogin.AuthenticationProviderId), nameof(UserExternalLogin.ProviderKey), nameof(UserExternalLogin.ProviderDisplayName), nameof(UserExternalLogin.CreatedAt), nameof(UserExternalLogin.CreatedBy), nameof(UserExternalLogin.UpdatedAt), nameof(UserExternalLogin.UpdatedBy)], []),
        new(typeof(ManagedTenantProvisioningOperation),
            [nameof(ManagedTenantProvisioningOperation.Id), nameof(ManagedTenantProvisioningOperation.ManagedInstanceId), nameof(ManagedTenantProvisioningOperation.ExternalRequestId), nameof(ManagedTenantProvisioningOperation.ExternalCustomerReference), nameof(ManagedTenantProvisioningOperation.RequestHash), nameof(ManagedTenantProvisioningOperation.RequestJson), nameof(ManagedTenantProvisioningOperation.TenantSlug),
                nameof(ManagedTenantProvisioningOperation.CurrentOutboxMessageId), nameof(ManagedTenantProvisioningOperation.CorrelationId), nameof(ManagedTenantProvisioningOperation.Status), nameof(ManagedTenantProvisioningOperation.TenantId), nameof(ManagedTenantProvisioningOperation.TenantAdministratorUserId), nameof(ManagedTenantProvisioningOperation.FailureCode), nameof(ManagedTenantProvisioningOperation.StartedAt),
                nameof(ManagedTenantProvisioningOperation.CompletedAt), nameof(ManagedTenantProvisioningOperation.FailedAt), nameof(ManagedTenantProvisioningOperation.CancelledAt), nameof(ManagedTenantProvisioningOperation.RowVersion), nameof(ManagedTenantProvisioningOperation.CreatedAt), nameof(ManagedTenantProvisioningOperation.CreatedBy), nameof(ManagedTenantProvisioningOperation.UpdatedAt), nameof(ManagedTenantProvisioningOperation.UpdatedBy)],
            [nameof(ManagedTenantProvisioningOperation.Id), nameof(ManagedTenantProvisioningOperation.ManagedInstanceId),
                nameof(ManagedTenantProvisioningOperation.ExternalRequestId), nameof(ManagedTenantProvisioningOperation.ExternalCustomerReference),
                nameof(ManagedTenantProvisioningOperation.RequestHash), nameof(ManagedTenantProvisioningOperation.RequestJson),
                nameof(ManagedTenantProvisioningOperation.TenantSlug), nameof(ManagedTenantProvisioningOperation.CurrentOutboxMessageId),
                nameof(ManagedTenantProvisioningOperation.CorrelationId), nameof(ManagedTenantProvisioningOperation.Status),
                nameof(ManagedTenantProvisioningOperation.TenantId), nameof(ManagedTenantProvisioningOperation.TenantAdministratorUserId),
                nameof(ManagedTenantProvisioningOperation.FailureCode), nameof(ManagedTenantProvisioningOperation.StartedAt),
                nameof(ManagedTenantProvisioningOperation.CompletedAt), nameof(ManagedTenantProvisioningOperation.FailedAt),
                nameof(ManagedTenantProvisioningOperation.CancelledAt), nameof(ManagedTenantProvisioningOperation.CreatedAt),
                nameof(ManagedTenantProvisioningOperation.CreatedBy), nameof(ManagedTenantProvisioningOperation.UpdatedAt),
                nameof(ManagedTenantProvisioningOperation.UpdatedBy)],
            [nameof(ManagedTenantProvisioningOperation.Status), nameof(ManagedTenantProvisioningOperation.TenantId), nameof(ManagedTenantProvisioningOperation.TenantAdministratorUserId), nameof(ManagedTenantProvisioningOperation.CompletedAt), nameof(ManagedTenantProvisioningOperation.FailureCode), nameof(ManagedTenantProvisioningOperation.RequestJson), nameof(ManagedTenantProvisioningOperation.UpdatedAt), nameof(ManagedTenantProvisioningOperation.UpdatedBy),
                nameof(ManagedTenantProvisioningOperation.CurrentOutboxMessageId), nameof(ManagedTenantProvisioningOperation.CorrelationId),
                nameof(ManagedTenantProvisioningOperation.StartedAt), nameof(ManagedTenantProvisioningOperation.FailedAt),
                nameof(ManagedTenantProvisioningOperation.CancelledAt)]),
        new(typeof(OutboxMessage), [],
            [nameof(OutboxMessage.Id), nameof(OutboxMessage.AggregateType), nameof(OutboxMessage.AggregateId),
                nameof(OutboxMessage.EventType), nameof(OutboxMessage.Payload), nameof(OutboxMessage.Status),
                nameof(OutboxMessage.CreatedAt), nameof(OutboxMessage.ProcessedAt), nameof(OutboxMessage.RetryCount),
                nameof(OutboxMessage.LastError), nameof(OutboxMessage.NextRetryAt), nameof(OutboxMessage.MaxRetries),
                nameof(OutboxMessage.DeadLetteredAt)], []),
        new(typeof(ManagedControlPlaneRegistration), [nameof(ManagedControlPlaneRegistration.Id), nameof(ManagedControlPlaneRegistration.ManagedInstanceId), nameof(ManagedControlPlaneRegistration.Status), nameof(ManagedControlPlaneRegistration.ManagementApiVersion), nameof(ManagedControlPlaneRegistration.DeploymentMode), nameof(ManagedControlPlaneRegistration.RowVersion), nameof(ManagedControlPlaneRegistration.CreatedAt)], [], []),
        new(typeof(TenantPlan), [nameof(TenantPlan.Id), nameof(TenantPlan.Key), nameof(TenantPlan.DisplayName), nameof(TenantPlan.Description), nameof(TenantPlan.CreatedAt), nameof(TenantPlan.CreatedBy), nameof(TenantPlan.UpdatedAt), nameof(TenantPlan.UpdatedBy)], [], []),
        new(typeof(TenantPlanVersion), [nameof(TenantPlanVersion.Id), nameof(TenantPlanVersion.TenantPlanId), nameof(TenantPlanVersion.VersionNumber), nameof(TenantPlanVersion.TenantPlanStatusId), nameof(TenantPlanVersion.PriceAmount), nameof(TenantPlanVersion.CurrencyCode),
            nameof(TenantPlanVersion.BillingPeriod), nameof(TenantPlanVersion.IsActiveForProvisioning), nameof(TenantPlanVersion.CreatedAt), nameof(TenantPlanVersion.CreatedBy), nameof(TenantPlanVersion.UpdatedAt), nameof(TenantPlanVersion.UpdatedBy)], [], []),
        new(typeof(TenantPlanVersionSetting), [nameof(TenantPlanVersionSetting.Id), nameof(TenantPlanVersionSetting.TenantPlanVersionId), nameof(TenantPlanVersionSetting.SettingKey), nameof(TenantPlanVersionSetting.JsonValue), nameof(TenantPlanVersionSetting.IsLocked), nameof(TenantPlanVersionSetting.CreatedAt), nameof(TenantPlanVersionSetting.CreatedBy), nameof(TenantPlanVersionSetting.UpdatedAt), nameof(TenantPlanVersionSetting.UpdatedBy)], [], []),
        new(typeof(TenantPlanVersionQuota), [nameof(TenantPlanVersionQuota.Id), nameof(TenantPlanVersionQuota.TenantPlanVersionId), nameof(TenantPlanVersionQuota.QuotaKey), nameof(TenantPlanVersionQuota.Limit), nameof(TenantPlanVersionQuota.CreatedAt), nameof(TenantPlanVersionQuota.CreatedBy), nameof(TenantPlanVersionQuota.UpdatedAt), nameof(TenantPlanVersionQuota.UpdatedBy)], [], []),
        new(typeof(ModuleDefinition), [nameof(ModuleDefinition.Id), nameof(ModuleDefinition.ModuleKey), nameof(ModuleDefinition.Name), nameof(ModuleDefinition.Description), nameof(ModuleDefinition.WizardSchemaUrl), nameof(ModuleDefinition.IconName), nameof(ModuleDefinition.DisplayOrder), nameof(ModuleDefinition.IsActive), nameof(ModuleDefinition.Category), nameof(ModuleDefinition.CreatedAt), nameof(ModuleDefinition.UpdatedAt)], [], []),
        new(typeof(ManagedTenantProvisioningPolicySetting), [nameof(ManagedTenantProvisioningPolicySetting.Id), nameof(ManagedTenantProvisioningPolicySetting.SettingKey), nameof(ManagedTenantProvisioningPolicySetting.IsPresent), nameof(ManagedTenantProvisioningPolicySetting.IsLocked), nameof(ManagedTenantProvisioningPolicySetting.Value), nameof(ManagedTenantProvisioningPolicySetting.Revision)], [], [])
    ];
}
