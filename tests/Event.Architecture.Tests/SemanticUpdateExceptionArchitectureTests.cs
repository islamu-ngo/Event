using System.Text.Json;

namespace Event.Architecture.Tests;

public sealed class SemanticUpdateExceptionArchitectureTests
{
    private static readonly SemanticException[] Exceptions =
    [
        new("SaveTenantOnboardingStepProgress", "/api/tenantonboarding/steps", "put", "Atomic onboarding progress checkpoint."),
        new("SetControlPlaneTenantSetting", "/api/admin/control-plane/tenants/{tenantId}/settings/{key}", "put", "Exact tenant setting replacement addressed by route key."),
        new("UpdateOrganizationMemberRole", "/api/organizationmember/role", "put", "Authorized member-role replacement with last-admin protection."),
        new("SetOrganizationNotificationPreferenceMute", "/api/organization/{id}/notification-preferences/mute", "put", "Exact organization mute-state replacement."),
        new("UpdateOrganizationApprovalStatus", "/api/organization/{id}/approval-status", "put", "Audited organization approval transition."),
        new("SetActiveAppearanceProfile", "/api/user/appearance/active-profile", "put", "Exact active-profile selection."),
        new("SetAppearanceThemeMode", "/api/user/appearance/mode", "put", "Exact current theme-mode replacement."),
        new("ArchiveAppearanceProfile", "/api/user/appearance/profiles/{profileId}/archive", "put", "Appearance-profile lifecycle transition."),
        new("SetGroupNotificationPreferenceMute", "/api/group/{id}/notification-preferences/mute", "put", "Exact group mute-state replacement."),
        new("UpdateGroupApprovalStatus", "/api/group/{id}/approval-status", "put", "Audited group approval transition."),
        new("PauseEmailDispatchTenant", "/api/admin/email-dispatch/tenants/{tenantId}/pause", "put", "Operational tenant dispatch pause transition."),
        new("ParkEmailDispatch", "/api/admin/email-dispatch/tenants/{tenantId}/outbox/{outboxId}/park", "put", "Operational outbox parking transition."),
        new("PauseEmailDispatchProcessor", "/api/admin/email-dispatch/control/pause", "put", "Operational processor pause transition."),
        new("SetEmailDispatchGlobalRateLimitOverride", "/api/admin/email-dispatch/control/rate-limit", "put", "Exact global rate-limit control replacement."),
        new("UpdateGroupMember", "/api/groupmember/role", "put", "Authorized member-role replacement with last-admin protection."),
        new("SetCurrentUserNotificationPreferenceMute", "/api/notification/preferences/me/mute", "put", "Exact current-user mute-state replacement."),
        new("MarkNotificationAsRead", "/api/notification/{id}/read", "patch", "Idempotent notification read transition."),
        new("ArchiveNotification", "/api/notification/{id}/archive", "patch", "Reversible notification archive transition."),
        new("SnoozeNotification", "/api/notification/{id}/snooze", "patch", "Notification snooze transition to one timestamp."),
        new("ReorderTenantNavigationLinks", "/api/tenant/navigation/reorder", "put", "Complete ordered navigation sequence replacement."),
        new("SetEventSessionCustomPropertyValue", "/api/eventsessioncustomproperty/value", "put", "Complete scalar custom-property value replacement."),
        new("SetEventSessionCustomPropertyMultiValues", "/api/eventsessioncustomproperty/values", "put", "Complete custom-property value-set replacement."),
        new("UpdateUserSettingsBatch", "/api/settings/user/{category}", "put", "Exact user category replacement with registered keys."),
        new("UpdateUserSetting", "/api/settings/user/keys/{key}", "put", "Exact user setting replacement addressed by route key."),
        new("UpdateTenantSettingsBatch", "/api/settings/tenant/{category}", "put", "Exact tenant category replacement with lock enforcement."),
        new("UpdateTenantSetting", "/api/settings/tenant/keys/{key}", "put", "Exact tenant setting replacement addressed by route key."),
        new("UpdateInstanceAtprotoFederationSetting", "/api/settings/instance/atproto-federation/{key}", "put", "Exact governed instance setting replacement addressed by route key."),
        new("UploadStorageUploadSessionContent", "/api/storageobject/upload-sessions/{uploadSessionId}/content", "put", "Complete byte content upload for one reserved session."),
        new("SetEventCustomPropertyValue", "/api/eventcustomproperty/value", "put", "Complete scalar custom-property value replacement."),
        new("SetEventCustomPropertyMultiValues", "/api/eventcustomproperty/values", "put", "Complete custom-property value-set replacement."),
        new("UpdateUserLastActiveTenant", "/api/user/active-tenant/{tenantId}", "post", "Authenticated tenant-context selection action."),
        new("ConfigureEventParticipation", "/api/events/{eventId}/participation", "patch", "Atomic coupled participation-configuration replacement."),
        new("UpdateEventPublicAction", "/api/events/{eventId}/public-actions/{actionId}", "put", "Atomic reviewed public-action replacement."),
        new("UpdateEventTicketType", "/api/events/{eventId}/ticketing/ticket-types/{ticketTypeId}", "put", "Atomic ticket-type replacement within one draft catalog."),
        new("UpdateEventCapacityPool", "/api/events/{eventId}/ticketing/capacity-pools/{capacityPoolId}", "put", "Atomic capacity-pool replacement within one draft catalog."),
        new("UpdateInstancePlatformMonetizationSettings", "/api/instance/settings/platform-monetization", "put", "Complete instance-level fee and contribution settings replacement with expected revision checks."),
        new("SaveInstanceOnboardingProfile", "/api/instanceonboarding/profile", "patch", "Setup-secret-gated non-secret profile save before instance onboarding completes.")
    ];

    [Test]
    public async Task PublicSemanticExceptionsMustMatchExactRegistry()
    {
        string root = ResolveRepositoryRoot();
        await using FileStream stream = File.OpenRead(Path.Combine(root, "schemas", "openapi_islamu-event.json"));
        using JsonDocument document = await JsonDocument.ParseAsync(stream);
        JsonElement paths = document.RootElement.GetProperty("paths");

        foreach (SemanticException exception in Exceptions)
        {
            JsonElement operation = paths.GetProperty(exception.Path).GetProperty(exception.Method);
            await Assert.That(operation.GetProperty("operationId").GetString()).IsEqualTo(exception.OperationId);
            await Assert.That(string.IsNullOrWhiteSpace(exception.Rationale)).IsFalse();
        }

        await Assert.That(Exceptions.Select(exception => (exception.Path, exception.Method)).Distinct().Count())
            .IsEqualTo(Exceptions.Length);
    }

    [Test]
    public async Task NestedAndApplicationOnlyUpdatesMustNotHaveDirectControllerOperations()
    {
        string root = ResolveRepositoryRoot();
        string controllers = string.Join('\n', Directory.GetFiles(
                Path.Combine(root, "src", "Explore.API", "Controllers"),
                "*.cs",
                SearchOption.AllDirectories)
            .Select(File.ReadAllText));

        string[] internalTypes =
        [
            "UpdateActorAppearanceDto",
            "UpdateUserProfileImageDto",
            "UpdateUserNamesDto",
            "UpdateEventDraftRequestDto",
            "UpdateEventTemplateDefinitionDto",
            "UpdateEventSessionTemplateDefinitionDto",
            "UpdateEventRoleAssignmentWindowCommand",
            "UpdateRolePermissionsCommand"
        ];

        foreach (string internalType in internalTypes)
        {
            await Assert.That(controllers).DoesNotContain(internalType);
        }
    }

    private static string ResolveRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Explore.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate repository root containing Explore.slnx.");
    }

    private sealed record SemanticException(string OperationId, string Path, string Method, string Rationale);
}
