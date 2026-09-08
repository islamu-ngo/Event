using Event.Api.IntegrationTests.Fixtures;
using TUnit.Core;

namespace Event.Api.IntegrationTests.Features;

/// <summary>
/// Data-driven policy contract tests covering all Cerbos resource policies.
/// Uses the Cerbos HTTP API directly — not the API layer — to validate that
/// YAML policies produce correct ALLOW/DENY decisions for every role × resource × action.
/// </summary>
[Category(TestCategories.PolicyContract)]
[NotInParallel("SecurityInfra")]
[ClassDataSource<SecurityInfrastructureFixture>(Shared = SharedType.PerAssembly)]
public class CerbosPolicyContractTests : IDisposable
{
    private readonly CerbosTestClient _cerbos;

    public CerbosPolicyContractTests(SecurityInfrastructureFixture infra)
    {
        _cerbos = new CerbosTestClient(infra.CerbosHttpEndpoint);
    }

    public void Dispose()
    {
        _cerbos.Dispose();
    }

    #region Instance Admin — Full Access Across Administered Resources

    public static IEnumerable<(string ResourceKind, string[] Actions)> GetInstanceAdminFullAccessCases()
    {
        yield return ("islamuevent_organization", (string[])["view", "create", "update", "delete", "manage_members"]);
        yield return ("islamuevent_tenant", (string[])["view", "create", "update", "delete"]);
        yield return ("islamuevent_user", (string[])["view", "create", "update", "delete"]);
        yield return ("islamuevent_category", (string[])["view", "create", "update", "delete"]);
        yield return ("islamuevent_tag", (string[])["view", "create", "update", "delete"]);
        yield return ("islamuevent_location", (string[])["view", "create", "update", "delete"]);
        yield return ("islamuevent_location_room", (string[])["view", "create", "update", "delete"]);
        yield return ("islamuevent_actor", (string[])["view", "create", "update", "delete"]);
        yield return ("islamuevent_tenant_user_role_grant", (string[])["view", "create", "delete"]);
        yield return ("islamuevent_organization_member", (string[])["view", "create", "update", "delete", "manage_members"]);
        yield return ("islamuevent_group", (string[])["view", "create", "update", "delete"]);
        yield return ("islamuevent_group_member", (string[])["view", "create", "update", "delete"]);
        yield return ("islamuevent_storage_object", (string[])["view", "create", "update", "delete", "download", "presigned_download"]);
        yield return ("islamuevent_instance_setting", (string[])["view", "update", "delete", "lock", "unlock"]);
        yield return ("islamuevent_tenant_setting", (string[])["view", "update", "delete"]);
        yield return ("islamuevent_event_session", (string[])["view", "create", "update", "delete"]);
        yield return ("islamuevent_event_day", (string[])["view", "create", "update", "delete"]);
        yield return ("islamuevent_event_agenda_item", (string[])["view", "create", "update", "delete"]);
        yield return ("islamuevent_event_session_agenda_item", (string[])["view", "create", "update", "delete"]);
        yield return ("islamuevent_organization_review", (string[])["view", "create", "update", "delete"]);
        yield return ("islamuevent_notification", (string[])["view", "create", "update", "delete"]);
        yield return ("islamuevent_custom_property_definition", (string[])["view", "create", "update", "delete"]);
        yield return ("islamuevent_custom_property_value", (string[])["view", "create", "update", "delete"]);
        yield return ("islamuevent_email_dispatch", (string[])["view", "manage_tenant", "park", "replay"]);
        yield return ("islamuevent_support_access_session", (string[])["view", "list", "start", "stop", "view_audit", "force_stop"]);
    }

    [Test]
    [MethodDataSource(nameof(GetInstanceAdminFullAccessCases))]
    public async Task InstanceAdmin_ShouldBeAllowed_AllActions((string ResourceKind, string[] Actions) testCase)
    {
        var (resourceKind, actions) = testCase;
        var result = await _cerbos.CheckResourceAsync(
            principalId: "user-instance-admin",
            principalRoles: ["islamuevent_authenticated_user"],
            principalAttrs: new { isInstanceAdmin = true, tenantMemberships = new { }, orgMemberships = new { } },
            resourceKind: resourceKind,
            resourceId: "resource-1",
            resourceAttrs: new { tenantId = "tenant-1", organizationId = "org-1", userId = "user-1" },
            actions: actions);

        foreach (var action in actions)
        {
            await Assert.That(result).ContainsKey(action).And.Value.IsEqualTo("EFFECT_ALLOW").Because($"instance admin should be allowed '{action}' on '{resourceKind}'");
        }
    }

    [Test]
    public async Task InstanceAdmin_ShouldBeAllowed_ViewAndModerateEventsWithoutEditAuthority()
    {
        var result = await _cerbos.CheckResourceAsync(
            principalId: "user-instance-admin",
            principalRoles: ["islamuevent_authenticated_user"],
            principalAttrs: new { isInstanceAdmin = true, tenantMemberships = new { }, orgMemberships = new { } },
            resourceKind: "islamuevent_event",
            resourceId: "event-1",
            resourceAttrs: new { tenantId = "tenant-1", organizationId = "org-1", userId = "user-1" },
            actions: ["view", "view-management", "create", "update", "delete", "moderate-light", "moderate-heavy", "unmoderate"]);

        await Assert.That(result).ContainsKey("view").And.Value.IsEqualTo("EFFECT_ALLOW");
        await Assert.That(result).ContainsKey("view-management").And.Value.IsEqualTo("EFFECT_ALLOW");
        await Assert.That(result).ContainsKey("create").And.Value.IsEqualTo("EFFECT_DENY");
        await Assert.That(result).ContainsKey("update").And.Value.IsEqualTo("EFFECT_DENY");
        await Assert.That(result).ContainsKey("delete").And.Value.IsEqualTo("EFFECT_DENY");
        await Assert.That(result).ContainsKey("moderate-light").And.Value.IsEqualTo("EFFECT_ALLOW");
        await Assert.That(result).ContainsKey("moderate-heavy").And.Value.IsEqualTo("EFFECT_ALLOW");
        await Assert.That(result).ContainsKey("unmoderate").And.Value.IsEqualTo("EFFECT_ALLOW");
    }

    #endregion

    #region Tenant Admin — Scoped Access

    [Test]
    public async Task TenantAdmin_ShouldBeAllowed_ViewAndModerateEventsInOwnTenantWithoutEditAuthority()
    {
        var result = await _cerbos.CheckResourceAsync(
            principalId: "user-tenant-admin",
            principalRoles: ["islamuevent_authenticated_user"],
            principalAttrs: new
            {
                isInstanceAdmin = false,
                tenantMemberships = new Dictionary<string, string> { ["tenant-1"] = "admin" },
                orgMemberships = new { }
            },
            resourceKind: "islamuevent_event",
            resourceId: "event-1",
            resourceAttrs: new { tenantId = "tenant-1", organizationId = "org-1" },
            actions: ["view", "view-management", "create", "update", "delete", "moderate-light", "moderate-heavy", "unmoderate"]);

        await Assert.That(result).ContainsKey("view").And.Value.IsEqualTo("EFFECT_ALLOW");
        await Assert.That(result).ContainsKey("view-management").And.Value.IsEqualTo("EFFECT_ALLOW");
        await Assert.That(result).ContainsKey("create").And.Value.IsEqualTo("EFFECT_DENY");
        await Assert.That(result).ContainsKey("update").And.Value.IsEqualTo("EFFECT_DENY");
        await Assert.That(result).ContainsKey("delete").And.Value.IsEqualTo("EFFECT_DENY");
        await Assert.That(result).ContainsKey("moderate-light").And.Value.IsEqualTo("EFFECT_ALLOW");
        await Assert.That(result).ContainsKey("moderate-heavy").And.Value.IsEqualTo("EFFECT_ALLOW");
        await Assert.That(result).ContainsKey("unmoderate").And.Value.IsEqualTo("EFFECT_ALLOW");
    }

    [Test]
    public async Task TenantAdmin_ShouldBeDenied_ManageEventsInOtherTenant()
    {
        var result = await _cerbos.CheckResourceAsync(
            principalId: "user-other-tenant-admin",
            principalRoles: ["islamuevent_authenticated_user"],
            principalAttrs: new
            {
                isInstanceAdmin = false,
                tenantMemberships = new Dictionary<string, string> { ["tenant-other"] = "admin" },
                orgMemberships = new { }
            },
            resourceKind: "islamuevent_event",
            resourceId: "event-1",
            resourceAttrs: new { tenantId = "tenant-1", organizationId = "org-1" },
            actions: ["view-management", "create", "update", "delete", "moderate-light", "moderate-heavy", "unmoderate"]);

        await Assert.That(result).ContainsKey("view-management").And.Value.IsEqualTo("EFFECT_DENY");
        await Assert.That(result).ContainsKey("create").And.Value.IsEqualTo("EFFECT_DENY");
        await Assert.That(result).ContainsKey("update").And.Value.IsEqualTo("EFFECT_DENY");
        await Assert.That(result).ContainsKey("delete").And.Value.IsEqualTo("EFFECT_DENY");
        await Assert.That(result).ContainsKey("moderate-light").And.Value.IsEqualTo("EFFECT_DENY");
        await Assert.That(result).ContainsKey("moderate-heavy").And.Value.IsEqualTo("EFFECT_DENY");
        await Assert.That(result).ContainsKey("unmoderate").And.Value.IsEqualTo("EFFECT_DENY");
    }

    [Test]
    public async Task UserOwnedEvent_ShouldExposeOwnerLifecycleAuthorityOnlyToOwningUser()
    {
        var ownerResult = await _cerbos.CheckResourceAsync(
            principalId: "user-owner",
            principalRoles: ["islamuevent_authenticated_user"],
            principalAttrs: new
            {
                userId = "user-owner",
                isInstanceAdmin = false,
                tenantMemberships = new { },
                orgMemberships = new { }
            },
            resourceKind: "islamuevent_event",
            resourceId: "event-user-owned",
            resourceAttrs: new { tenantId = "tenant-1", eventId = "event-user-owned", userId = "user-owner" },
            actions: ["view", "view-management", "update", "delete", "publish"]);

        await Assert.That(ownerResult).ContainsKey("view").And.Value.IsEqualTo("EFFECT_ALLOW");
        await Assert.That(ownerResult).ContainsKey("view-management").And.Value.IsEqualTo("EFFECT_ALLOW");
        await Assert.That(ownerResult).ContainsKey("update").And.Value.IsEqualTo("EFFECT_ALLOW");
        await Assert.That(ownerResult).ContainsKey("delete").And.Value.IsEqualTo("EFFECT_ALLOW");
        await Assert.That(ownerResult).ContainsKey("publish").And.Value.IsEqualTo("EFFECT_ALLOW");

        var otherResult = await _cerbos.CheckResourceAsync(
            principalId: "user-other",
            principalRoles: ["islamuevent_authenticated_user"],
            principalAttrs: new
            {
                userId = "user-other",
                isInstanceAdmin = false,
                tenantMemberships = new { },
                orgMemberships = new { }
            },
            resourceKind: "islamuevent_event",
            resourceId: "event-user-owned",
            resourceAttrs: new { tenantId = "tenant-1", eventId = "event-user-owned", userId = "user-owner" },
            actions: ["view", "view-management", "update", "delete", "publish"]);

        await Assert.That(otherResult).ContainsKey("view").And.Value.IsEqualTo("EFFECT_ALLOW");
        await Assert.That(otherResult).ContainsKey("view-management").And.Value.IsEqualTo("EFFECT_DENY");
        await Assert.That(otherResult).ContainsKey("update").And.Value.IsEqualTo("EFFECT_DENY");
        await Assert.That(otherResult).ContainsKey("delete").And.Value.IsEqualTo("EFFECT_DENY");
        await Assert.That(otherResult).ContainsKey("publish").And.Value.IsEqualTo("EFFECT_DENY");
    }

    [Test]
    public async Task TenantAdmin_ShouldBeAllowed_ManageOrganizationsInOwnTenant()
    {
        var result = await _cerbos.CheckResourceAsync(
            principalId: "user-tenant-admin",
            principalRoles: ["islamuevent_authenticated_user"],
            principalAttrs: new
            {
                isInstanceAdmin = false,
                tenantMemberships = new Dictionary<string, string> { ["tenant-1"] = "admin" },
                orgMemberships = new { }
            },
            resourceKind: "islamuevent_organization",
            resourceId: "org-1",
            resourceAttrs: new { tenantId = "tenant-1", organizationId = "org-1" },
            actions: ["view", "create", "update", "delete", "manage_members"]);

        await Assert.That(result).ContainsKey("view").And.Value.IsEqualTo("EFFECT_ALLOW");
        await Assert.That(result).ContainsKey("create").And.Value.IsEqualTo("EFFECT_ALLOW");
        await Assert.That(result).ContainsKey("update").And.Value.IsEqualTo("EFFECT_ALLOW");
        await Assert.That(result).ContainsKey("delete").And.Value.IsEqualTo("EFFECT_ALLOW");
        await Assert.That(result).ContainsKey("manage_members").And.Value.IsEqualTo("EFFECT_ALLOW");
    }

    [Test]
    public async Task TenantAdmin_ShouldBeAllowed_ManageOrganizationMembersInOwnTenant()
    {
        var result = await _cerbos.CheckResourceAsync(
            principalId: "user-tenant-admin",
            principalRoles: ["islamuevent_authenticated_user"],
            principalAttrs: new
            {
                isInstanceAdmin = false,
                tenantMemberships = new Dictionary<string, string> { ["tenant-1"] = "admin" },
                orgMemberships = new { }
            },
            resourceKind: "islamuevent_organization_member",
            resourceId: "member-1",
            resourceAttrs: new { tenantId = "tenant-1", organizationId = "org-1", userId = "user-1" },
            actions: ["view", "create", "update", "delete", "manage_members"]);

        await Assert.That(result).ContainsKey("view").And.Value.IsEqualTo("EFFECT_ALLOW");
        await Assert.That(result).ContainsKey("create").And.Value.IsEqualTo("EFFECT_ALLOW");
        await Assert.That(result).ContainsKey("update").And.Value.IsEqualTo("EFFECT_ALLOW");
        await Assert.That(result).ContainsKey("delete").And.Value.IsEqualTo("EFFECT_ALLOW");
        await Assert.That(result).ContainsKey("manage_members").And.Value.IsEqualTo("EFFECT_ALLOW");
    }

    [Test]
    public async Task TenantAdmin_ShouldBeDenied_CreateOrganizationsInOtherTenant()
    {
        var result = await _cerbos.CheckResourceAsync(
            principalId: "user-other-tenant-admin",
            principalRoles: ["islamuevent_authenticated_user"],
            principalAttrs: new
            {
                isInstanceAdmin = false,
                tenantMemberships = new Dictionary<string, string> { ["tenant-other"] = "admin" },
                orgMemberships = new { }
            },
            resourceKind: "islamuevent_organization",
            resourceId: "org-1",
            resourceAttrs: new { tenantId = "tenant-1", organizationId = "org-1" },
            actions: ["create", "update", "delete"]);

        await Assert.That(result).ContainsKey("create").And.Value.IsEqualTo("EFFECT_DENY");
        await Assert.That(result).ContainsKey("update").And.Value.IsEqualTo("EFFECT_DENY");
        await Assert.That(result).ContainsKey("delete").And.Value.IsEqualTo("EFFECT_DENY");
    }

    [Test]
    public async Task TenantAdmin_ShouldBeAllowed_ViewAndUpdateOwnTenant()
    {
        var result = await _cerbos.CheckResourceAsync(
            principalId: "user-tenant-admin",
            principalRoles: ["islamuevent_authenticated_user"],
            principalAttrs: new
            {
                isInstanceAdmin = false,
                tenantMemberships = new Dictionary<string, string> { ["tenant-1"] = "admin" },
                orgMemberships = new { }
            },
            resourceKind: "islamuevent_tenant",
            resourceId: "tenant-1",
            resourceAttrs: new { tenantId = "tenant-1" },
            actions: ["view", "update"]);

        await Assert.That(result).ContainsKey("view").And.Value.IsEqualTo("EFFECT_ALLOW");
        await Assert.That(result).ContainsKey("update").And.Value.IsEqualTo("EFFECT_ALLOW");
    }

    [Test]
    public async Task TenantAdmin_ShouldBeDenied_ManageTenantSettings_WhenLockedByInstance()
    {
        var result = await _cerbos.CheckResourceAsync(
            principalId: "user-tenant-admin",
            principalRoles: ["islamuevent_authenticated_user"],
            principalAttrs: new
            {
                isInstanceAdmin = false,
                tenantMemberships = new Dictionary<string, string> { ["tenant-1"] = "admin" },
                orgMemberships = new { }
            },
            resourceKind: "islamuevent_tenant_setting",
            resourceId: "setting-1",
            resourceAttrs: new { tenantId = "tenant-1", isLockedByInstance = true },
            actions: ["update"]);

        await Assert.That(result).ContainsKey("update").And.Value.IsEqualTo("EFFECT_DENY").Because("tenant admin must be denied update when instance admin has locked the setting");
    }

    [Test]
    public async Task SupportAccessSession_ShouldEnforceLifecycleAndEvidenceAuthority()
    {
        string[] actions = ["view", "list", "start", "stop", "view_audit", "force_stop"];
        var resourceAttrs = new
        {
            tenantId = "tenant-1",
            sessionId = "session-1",
            actorUserId = "user-instance-admin",
            mode = "ReadOnly",
            status = "Active"
        };

        var instanceAdmin = await _cerbos.CheckResourceAsync(
            principalId: "user-instance-admin",
            principalRoles: ["islamuevent_authenticated_user"],
            principalAttrs: new { isInstanceAdmin = true, tenantMemberships = new { }, orgMemberships = new { } },
            resourceKind: "islamuevent_support_access_session",
            resourceId: "session-1",
            resourceAttrs: resourceAttrs,
            actions: actions);

        foreach (var action in actions)
            await Assert.That(instanceAdmin).ContainsKey(action).And.Value.IsEqualTo("EFFECT_ALLOW");

        var tenantAdmin = await _cerbos.CheckResourceAsync(
            principalId: "user-tenant-admin",
            principalRoles: ["islamuevent_authenticated_user"],
            principalAttrs: new
            {
                isInstanceAdmin = false,
                tenantMemberships = new Dictionary<string, string> { ["tenant-1"] = "admin" },
                orgMemberships = new { }
            },
            resourceKind: "islamuevent_support_access_session",
            resourceId: "session-1",
            resourceAttrs: resourceAttrs,
            actions: actions);

        await Assert.That(tenantAdmin).ContainsKey("view").And.Value.IsEqualTo("EFFECT_ALLOW");
        await Assert.That(tenantAdmin).ContainsKey("list").And.Value.IsEqualTo("EFFECT_ALLOW");
        await Assert.That(tenantAdmin).ContainsKey("view_audit").And.Value.IsEqualTo("EFFECT_ALLOW");
        await Assert.That(tenantAdmin).ContainsKey("start").And.Value.IsEqualTo("EFFECT_DENY");
        await Assert.That(tenantAdmin).ContainsKey("stop").And.Value.IsEqualTo("EFFECT_DENY");
        await Assert.That(tenantAdmin).ContainsKey("force_stop").And.Value.IsEqualTo("EFFECT_DENY");

        var otherTenantAdmin = await _cerbos.CheckResourceAsync(
            principalId: "user-other-tenant-admin",
            principalRoles: ["islamuevent_authenticated_user"],
            principalAttrs: new
            {
                isInstanceAdmin = false,
                tenantMemberships = new Dictionary<string, string> { ["tenant-other"] = "admin" },
                orgMemberships = new { }
            },
            resourceKind: "islamuevent_support_access_session",
            resourceId: "session-1",
            resourceAttrs: resourceAttrs,
            actions: ["view", "list", "view_audit"]);

        await Assert.That(otherTenantAdmin).ContainsKey("view").And.Value.IsEqualTo("EFFECT_DENY");
        await Assert.That(otherTenantAdmin).ContainsKey("list").And.Value.IsEqualTo("EFFECT_DENY");
        await Assert.That(otherTenantAdmin).ContainsKey("view_audit").And.Value.IsEqualTo("EFFECT_DENY");

        var regularUser = await _cerbos.CheckResourceAsync(
            principalId: "user-regular",
            principalRoles: ["islamuevent_authenticated_user"],
            principalAttrs: new { isInstanceAdmin = false, tenantMemberships = new { }, orgMemberships = new { } },
            resourceKind: "islamuevent_support_access_session",
            resourceId: "session-1",
            resourceAttrs: resourceAttrs,
            actions: actions);

        foreach (var action in actions)
            await Assert.That(regularUser).ContainsKey(action).And.Value.IsEqualTo("EFFECT_DENY");
    }

    [Test]
    public async Task TenantAdmin_ShouldBeAllowed_ManageEmailDispatchInOwnTenant()
    {
        var result = await _cerbos.CheckResourceAsync(
            principalId: "user-tenant-admin",
            principalRoles: ["islamuevent_authenticated_user"],
            principalAttrs: new
            {
                isInstanceAdmin = false,
                tenantMemberships = new Dictionary<string, string> { ["tenant-1"] = "admin" },
                orgMemberships = new { }
            },
            resourceKind: "islamuevent_email_dispatch",
            resourceId: "dispatch-1",
            resourceAttrs: new { tenantId = "tenant-1", outboxId = "dispatch-1", deliveryStatus = "DeadLettered" },
            actions: ["view", "manage_tenant", "park", "replay"]);

        foreach (var action in new[] { "view", "manage_tenant", "park", "replay" })
        {
            await Assert.That(result).ContainsKey(action).And.Value.IsEqualTo("EFFECT_ALLOW").Because($"tenant admin should be allowed '{action}' on email dispatch rows in their tenant");
        }
    }

    [Test]
    public async Task RegularUser_ShouldBeDenied_ManageEmailDispatch()
    {
        var result = await _cerbos.CheckResourceAsync(
            principalId: "user-regular",
            principalRoles: ["islamuevent_authenticated_user"],
            principalAttrs: new { isInstanceAdmin = false, tenantMemberships = new { }, orgMemberships = new { } },
            resourceKind: "islamuevent_email_dispatch",
            resourceId: "dispatch-1",
            resourceAttrs: new { tenantId = "tenant-1", outboxId = "dispatch-1", deliveryStatus = "DeadLettered" },
            actions: ["view", "manage_tenant", "park", "replay"]);

        foreach (var action in new[] { "view", "manage_tenant", "park", "replay" })
        {
            await Assert.That(result).ContainsKey(action).And.Value.IsEqualTo("EFFECT_DENY").Because($"regular users must not access email dispatch operator actions");
        }
    }

    [Test]
    public async Task TenantAdmin_ShouldBeAllowed_ManageTenantSettings_WhenNotLocked()
    {
        var result = await _cerbos.CheckResourceAsync(
            principalId: "user-tenant-admin",
            principalRoles: ["islamuevent_authenticated_user"],
            principalAttrs: new
            {
                isInstanceAdmin = false,
                tenantMemberships = new Dictionary<string, string> { ["tenant-1"] = "admin" },
                orgMemberships = new { }
            },
            resourceKind: "islamuevent_tenant_setting",
            resourceId: "setting-1",
            resourceAttrs: new { tenantId = "tenant-1", isLockedByInstance = false },
            actions: ["view", "update"]);

        await Assert.That(result).ContainsKey("view").And.Value.IsEqualTo("EFFECT_ALLOW");
        await Assert.That(result).ContainsKey("update").And.Value.IsEqualTo("EFFECT_ALLOW");
    }


    [Test]
    public async Task TenantAdmin_ShouldBeAllowed_ManageTenantBrandingDocument_WhenLockedForHandlerValidation()
    {
        var result = await _cerbos.CheckResourceAsync(
            principalId: "user-tenant-admin",
            principalRoles: ["islamuevent_authenticated_user"],
            principalAttrs: new
            {
                isInstanceAdmin = false,
                tenantMemberships = new Dictionary<string, string> { ["tenant-1"] = "admin" },
                orgMemberships = new { }
            },
            resourceKind: "islamuevent_tenant_setting",
            resourceId: "setting-1",
            resourceAttrs: new { tenantId = "tenant-1", documentKey = "tenant.branding", isLockedByInstance = true },
            actions: ["update"]);

        await Assert.That(result).ContainsKey("update").And.Value.IsEqualTo("EFFECT_ALLOW").Because("tenant.branding field-level locks are enforced by the replacement handler after authorization");
    }

    #endregion

    #region Org Admin — Scoped Access

    [Test]
    public async Task OrgAdmin_ShouldBeAllowed_ManageEventsInOwnOrg()
    {
        var result = await _cerbos.CheckResourceAsync(
            principalId: "user-org-admin",
            principalRoles: ["islamuevent_authenticated_user"],
            principalAttrs: new
            {
                isInstanceAdmin = false,
                tenantMemberships = new { },
                orgMemberships = new Dictionary<string, string> { ["org-1"] = "admin" }
            },
            resourceKind: "islamuevent_event",
            resourceId: "event-1",
            resourceAttrs: new { tenantId = "tenant-1", organizationId = "org-1" },
            actions: ["view", "create", "update", "delete"]);

        await Assert.That(result).ContainsKey("view").And.Value.IsEqualTo("EFFECT_ALLOW");
        await Assert.That(result).ContainsKey("create").And.Value.IsEqualTo("EFFECT_ALLOW");
        await Assert.That(result).ContainsKey("update").And.Value.IsEqualTo("EFFECT_ALLOW");
        await Assert.That(result).ContainsKey("delete").And.Value.IsEqualTo("EFFECT_ALLOW");
    }

    [Test]
    public async Task OrgAdmin_ShouldBeDenied_ManageEventsInOtherOrg()
    {
        var result = await _cerbos.CheckResourceAsync(
            principalId: "user-other-org-admin",
            principalRoles: ["islamuevent_authenticated_user"],
            principalAttrs: new
            {
                isInstanceAdmin = false,
                tenantMemberships = new { },
                orgMemberships = new Dictionary<string, string> { ["org-other"] = "admin" }
            },
            resourceKind: "islamuevent_event",
            resourceId: "event-1",
            resourceAttrs: new { tenantId = "tenant-1", organizationId = "org-1" },
            actions: ["create", "update", "delete"]);

        await Assert.That(result).ContainsKey("create").And.Value.IsEqualTo("EFFECT_DENY");
        await Assert.That(result).ContainsKey("update").And.Value.IsEqualTo("EFFECT_DENY");
        await Assert.That(result).ContainsKey("delete").And.Value.IsEqualTo("EFFECT_DENY");
    }

    [Test]
    public async Task OrgAdmin_ShouldBeAllowed_ManageOwnOrg()
    {
        var result = await _cerbos.CheckResourceAsync(
            principalId: "user-org-admin",
            principalRoles: ["islamuevent_authenticated_user"],
            principalAttrs: new
            {
                isInstanceAdmin = false,
                tenantMemberships = new { },
                orgMemberships = new Dictionary<string, string> { ["org-1"] = "admin" }
            },
            resourceKind: "islamuevent_organization",
            resourceId: "org-1",
            resourceAttrs: new { tenantId = "tenant-1", organizationId = "org-1" },
            actions: ["view", "update", "manage_members"]);

        await Assert.That(result).ContainsKey("view").And.Value.IsEqualTo("EFFECT_ALLOW");
        await Assert.That(result).ContainsKey("update").And.Value.IsEqualTo("EFFECT_ALLOW");
        await Assert.That(result).ContainsKey("manage_members").And.Value.IsEqualTo("EFFECT_ALLOW");
    }

    [Test]
    public async Task OrgAdmin_ShouldBeDenied_DeleteOwnOrg()
    {
        var result = await _cerbos.CheckResourceAsync(
            principalId: "user-org-admin",
            principalRoles: ["islamuevent_authenticated_user"],
            principalAttrs: new
            {
                isInstanceAdmin = false,
                tenantMemberships = new { },
                orgMemberships = new Dictionary<string, string> { ["org-1"] = "admin" }
            },
            resourceKind: "islamuevent_organization",
            resourceId: "org-1",
            resourceAttrs: new { tenantId = "tenant-1", organizationId = "org-1" },
            actions: ["delete"]);

        await Assert.That(result).ContainsKey("delete").And.Value.IsEqualTo("EFFECT_DENY").Because("org admins can update/manage_members but not delete organizations");
    }

    [Test]
    public async Task OrgAdmin_ShouldBeAllowed_ManageOrgMembers()
    {
        var result = await _cerbos.CheckResourceAsync(
            principalId: "user-org-admin",
            principalRoles: ["islamuevent_authenticated_user"],
            principalAttrs: new
            {
                isInstanceAdmin = false,
                tenantMemberships = new { },
                orgMemberships = new Dictionary<string, string> { ["org-1"] = "admin" }
            },
            resourceKind: "islamuevent_organization_member",
            resourceId: "member-1",
            resourceAttrs: new { tenantId = "tenant-1", organizationId = "org-1", userId = "user-1" },
            actions: ["view", "create", "update", "delete", "manage_members"]);

        await Assert.That(result).ContainsKey("view").And.Value.IsEqualTo("EFFECT_ALLOW");
        await Assert.That(result).ContainsKey("create").And.Value.IsEqualTo("EFFECT_ALLOW");
        await Assert.That(result).ContainsKey("update").And.Value.IsEqualTo("EFFECT_ALLOW");
        await Assert.That(result).ContainsKey("delete").And.Value.IsEqualTo("EFFECT_ALLOW");
        await Assert.That(result).ContainsKey("manage_members").And.Value.IsEqualTo("EFFECT_ALLOW");
    }

    [Test]
    public async Task OrgAdmin_ShouldBeAllowed_ManageGroupsInOwnOrg()
    {
        var result = await _cerbos.CheckResourceAsync(
            principalId: "user-org-admin",
            principalRoles: ["islamuevent_authenticated_user"],
            principalAttrs: new
            {
                isInstanceAdmin = false,
                tenantMemberships = new { },
                orgMemberships = new Dictionary<string, string> { ["org-1"] = "admin" }
            },
            resourceKind: "islamuevent_group",
            resourceId: "group-1",
            resourceAttrs: new { tenantId = "tenant-1", organizationId = "org-1" },
            actions: ["view", "create", "update", "delete"]);

        await Assert.That(result).ContainsKey("view").And.Value.IsEqualTo("EFFECT_ALLOW");
        await Assert.That(result).ContainsKey("create").And.Value.IsEqualTo("EFFECT_ALLOW");
        await Assert.That(result).ContainsKey("update").And.Value.IsEqualTo("EFFECT_ALLOW");
        await Assert.That(result).ContainsKey("delete").And.Value.IsEqualTo("EFFECT_ALLOW");
    }

    [Test]
    public async Task OrgAdmin_ShouldBeAllowed_ViewActors()
    {
        var result = await _cerbos.CheckResourceAsync(
            principalId: "user-org-admin",
            principalRoles: ["islamuevent_authenticated_user"],
            principalAttrs: new
            {
                isInstanceAdmin = false,
                tenantMemberships = new { },
                orgMemberships = new Dictionary<string, string> { ["org-1"] = "admin" }
            },
            resourceKind: "islamuevent_actor",
            resourceId: "actor-1",
            resourceAttrs: new { tenantId = "tenant-1", organizationId = "org-1" },
            actions: ["view", "create"]);

        await Assert.That(result).ContainsKey("view").And.Value.IsEqualTo("EFFECT_ALLOW");
        await Assert.That(result).ContainsKey("create").And.Value.IsEqualTo("EFFECT_DENY").Because("org admins can only view actors, not create them");
    }

    #endregion

    #region Regular User — View Only

    [Test]
    public async Task RegularUser_ShouldBeAllowed_ViewEvents()
    {
        var result = await _cerbos.CheckResourceAsync(
            principalId: "user-regular",
            principalRoles: ["islamuevent_authenticated_user"],
            principalAttrs: new { isInstanceAdmin = false, tenantMemberships = new { }, orgMemberships = new { } },
            resourceKind: "islamuevent_event",
            resourceId: "event-1",
            resourceAttrs: new { tenantId = "tenant-1", organizationId = "org-1" },
            actions: ["view", "view-management"]);

        await Assert.That(result).ContainsKey("view").And.Value.IsEqualTo("EFFECT_ALLOW");
        await Assert.That(result).ContainsKey("view-management").And.Value.IsEqualTo("EFFECT_DENY");
    }

    [Test]
    public async Task RegularUser_ShouldBeDenied_MutateEvents()
    {
        var result = await _cerbos.CheckResourceAsync(
            principalId: "user-regular",
            principalRoles: ["islamuevent_authenticated_user"],
            principalAttrs: new { isInstanceAdmin = false, tenantMemberships = new { }, orgMemberships = new { } },
            resourceKind: "islamuevent_event",
            resourceId: "event-1",
            resourceAttrs: new { tenantId = "tenant-1", organizationId = "org-1" },
            actions: ["create", "update", "delete"]);

        await Assert.That(result).ContainsKey("create").And.Value.IsEqualTo("EFFECT_DENY");
        await Assert.That(result).ContainsKey("update").And.Value.IsEqualTo("EFFECT_DENY");
        await Assert.That(result).ContainsKey("delete").And.Value.IsEqualTo("EFFECT_DENY");
    }

    [Test]
    public async Task RegularUser_ShouldBeAllowed_OrganizationPreCreateGate()
    {
        var result = await _cerbos.CheckResourceAsync(
            principalId: "user-regular",
            principalRoles: ["islamuevent_authenticated_user"],
            principalAttrs: new { isInstanceAdmin = false, tenantMemberships = new { }, orgMemberships = new { } },
            resourceKind: "islamuevent_organization",
            resourceId: "create",
            resourceAttrs: new { tenantId = "tenant-1", authorizationPhase = "pre_create" },
            actions: ["create"]);

        await Assert.That(result).ContainsKey("create").And.Value.IsEqualTo("EFFECT_ALLOW").Because("pre-create organization checks only decide whether the authenticated human request can reach CreateOrganizationCommandHandler");
    }

    [Test]
    public async Task MachineCaller_ShouldBeDenied_OrganizationPreCreateGate()
    {
        var result = await _cerbos.CheckResourceAsync(
            principalId: "api-key-machine",
            principalRoles: ["islamuevent_authenticated_user"],
            principalAttrs: new { isInstanceAdmin = false, tenantMemberships = new { }, orgMemberships = new { }, is_machine = true },
            resourceKind: "islamuevent_organization",
            resourceId: "create",
            resourceAttrs: new { tenantId = "tenant-1", authorizationPhase = "pre_create" },
            actions: ["create"]);

        await Assert.That(result).ContainsKey("create").And.Value.IsEqualTo("EFFECT_DENY").Because("machine/API-key organization creation cannot use the human self-registration pre-create gate");
    }

    [Test]
    public async Task RegularUser_ShouldBeAllowed_EventPreCreateGate()
    {
        var result = await _cerbos.CheckResourceAsync(
            principalId: "user-regular",
            principalRoles: ["islamuevent_authenticated_user"],
            principalAttrs: new { isInstanceAdmin = false, tenantMemberships = new { }, orgMemberships = new { } },
            resourceKind: "islamuevent_event",
            resourceId: "create",
            resourceAttrs: new { tenantId = "tenant-1", authorizationPhase = "pre_create" },
            actions: ["create"]);

        await Assert.That(result).ContainsKey("create").And.Value.IsEqualTo("EFFECT_ALLOW").Because("pre-create event checks only decide whether the authenticated human request can reach CreateEventCommandHandler; EventActorResolver enforces publishing policy");
    }

    [Test]
    public async Task MachineCaller_ShouldBeDenied_EventPreCreateGate()
    {
        var result = await _cerbos.CheckResourceAsync(
            principalId: "api-key-machine",
            principalRoles: ["islamuevent_authenticated_user"],
            principalAttrs: new { isInstanceAdmin = false, tenantMemberships = new { }, orgMemberships = new { }, is_machine = true },
            resourceKind: "islamuevent_event",
            resourceId: "create",
            resourceAttrs: new { tenantId = "tenant-1", authorizationPhase = "pre_create" },
            actions: ["create"]);

        await Assert.That(result).ContainsKey("create").And.Value.IsEqualTo("EFFECT_DENY").Because("machine/API-key event create is governed by scope and owner checks, not the human pre-create rule");
    }

    [Test]
    public async Task IncomingWebhookMachineScopes_ShouldMatchLocalAuthorizationBoundary()
    {
        var dedicatedWorker = await _cerbos.CheckResourceAsync(
            principalId: "incoming-webhook-worker",
            principalRoles: ["islamuevent_authenticated_user"],
            principalAttrs: new
            {
                isInstanceAdmin = false,
                tenantMemberships = new Dictionary<string, string> { ["tenant-1"] = "admin" },
                orgMemberships = new { },
                is_machine = true,
                scopes = new[] { "internal:webhook:process-incoming" }
            },
            resourceKind: "islamuevent_webhook",
            resourceId: "incoming-message-1",
            resourceAttrs: new { tenantId = "tenant-1" },
            actions:
            [
                "webhook:process-incoming",
                "webhook:redrive-incoming",
                "webhook:pause",
                "webhook:resume",
                "webhook:reconcile-publication",
                "webhook:abandon-publication",
                "webhook:manage-provider",
                "webhook:view-payload",
                "webhook:bulk-replay"
            ]);

        await Assert.That(dedicatedWorker).ContainsKey("webhook:process-incoming").And.Value.IsEqualTo("EFFECT_ALLOW");
        await Assert.That(dedicatedWorker).ContainsKey("webhook:redrive-incoming").And.Value.IsEqualTo("EFFECT_DENY");
        await Assert.That(dedicatedWorker).ContainsKey("webhook:pause").And.Value.IsEqualTo("EFFECT_DENY");
        await Assert.That(dedicatedWorker).ContainsKey("webhook:resume").And.Value.IsEqualTo("EFFECT_DENY");
        await Assert.That(dedicatedWorker).ContainsKey("webhook:reconcile-publication").And.Value.IsEqualTo("EFFECT_DENY");
        await Assert.That(dedicatedWorker).ContainsKey("webhook:abandon-publication").And.Value.IsEqualTo("EFFECT_DENY");
        await Assert.That(dedicatedWorker).ContainsKey("webhook:manage-provider").And.Value.IsEqualTo("EFFECT_DENY");
        await Assert.That(dedicatedWorker).ContainsKey("webhook:view-payload").And.Value.IsEqualTo("EFFECT_DENY");
        await Assert.That(dedicatedWorker).ContainsKey("webhook:bulk-replay").And.Value.IsEqualTo("EFFECT_DENY");

        var tenantAdminMachine = await _cerbos.CheckResourceAsync(
            principalId: "tenant-admin-machine",
            principalRoles: ["islamuevent_authenticated_user"],
            principalAttrs: new
            {
                isInstanceAdmin = false,
                tenantMemberships = new Dictionary<string, string> { ["tenant-1"] = "admin" },
                orgMemberships = new { },
                is_machine = true,
                scopes = new[] { "admin:tenant" }
            },
            resourceKind: "islamuevent_webhook",
            resourceId: "incoming-message-1",
            resourceAttrs: new { tenantId = "tenant-1" },
            actions:
            [
                "webhook:process-incoming",
                "webhook:redrive-incoming",
                "webhook:pause",
                "webhook:resume",
                "webhook:reconcile-publication",
                "webhook:abandon-publication",
                "webhook:manage-provider",
                "webhook:view-payload",
                "webhook:bulk-replay"
            ]);

        await Assert.That(tenantAdminMachine).ContainsKey("webhook:process-incoming").And.Value.IsEqualTo("EFFECT_DENY");
        await Assert.That(tenantAdminMachine).ContainsKey("webhook:redrive-incoming").And.Value.IsEqualTo("EFFECT_ALLOW");
        await Assert.That(tenantAdminMachine).ContainsKey("webhook:pause").And.Value.IsEqualTo("EFFECT_ALLOW");
        await Assert.That(tenantAdminMachine).ContainsKey("webhook:resume").And.Value.IsEqualTo("EFFECT_ALLOW");
        await Assert.That(tenantAdminMachine).ContainsKey("webhook:reconcile-publication").And.Value.IsEqualTo("EFFECT_ALLOW");
        await Assert.That(tenantAdminMachine).ContainsKey("webhook:abandon-publication").And.Value.IsEqualTo("EFFECT_ALLOW");
        await Assert.That(tenantAdminMachine).ContainsKey("webhook:manage-provider").And.Value.IsEqualTo("EFFECT_ALLOW");
        await Assert.That(tenantAdminMachine).ContainsKey("webhook:view-payload").And.Value.IsEqualTo("EFFECT_ALLOW");
        await Assert.That(tenantAdminMachine).ContainsKey("webhook:bulk-replay").And.Value.IsEqualTo("EFFECT_ALLOW");
    }

    [Test]
    public async Task RegularUser_ShouldBeAllowed_CreateAndDownloadReadableStorageObjects()
    {
        var result = await _cerbos.CheckResourceAsync(
            principalId: "user-regular",
            principalRoles: ["islamuevent_authenticated_user"],
            principalAttrs: new { isInstanceAdmin = false, tenantMemberships = new { }, orgMemberships = new { } },
            resourceKind: "islamuevent_storage_object",
            resourceId: "obj-1",
            resourceAttrs: new
            {
                tenantId = "tenant-1",
                organizationId = "org-1",
                visibility = "authenticated_tenant",
                lifecycleState = "active"
            },
            actions: ["view", "create", "download", "presigned_download"]);

        await Assert.That(result).ContainsKey("view").And.Value.IsEqualTo("EFFECT_DENY");
        await Assert.That(result).ContainsKey("create").And.Value.IsEqualTo("EFFECT_ALLOW");
        await Assert.That(result).ContainsKey("download").And.Value.IsEqualTo("EFFECT_ALLOW");
        await Assert.That(result).ContainsKey("presigned_download").And.Value.IsEqualTo("EFFECT_ALLOW");
    }

    [Test]
    public async Task RegularUser_ShouldBeDenied_DeleteStorageObjects()
    {
        var result = await _cerbos.CheckResourceAsync(
            principalId: "user-regular",
            principalRoles: ["islamuevent_authenticated_user"],
            principalAttrs: new { isInstanceAdmin = false, tenantMemberships = new { }, orgMemberships = new { } },
            resourceKind: "islamuevent_storage_object",
            resourceId: "obj-1",
            resourceAttrs: new { tenantId = "tenant-1", organizationId = "org-1" },
            actions: ["delete", "update"]);

        await Assert.That(result).ContainsKey("delete").And.Value.IsEqualTo("EFFECT_DENY");
        await Assert.That(result).ContainsKey("update").And.Value.IsEqualTo("EFFECT_DENY");
    }

    [Test]
    public async Task RegularUser_ShouldBeAllowed_UpdateOwnUserProfile()
    {
        var result = await _cerbos.CheckResourceAsync(
            principalId: "external-subject-self",
            principalRoles: ["islamuevent_authenticated_user"],
            principalAttrs: new { userId = "user-self", isInstanceAdmin = false, tenantMemberships = new { }, orgMemberships = new { } },
            resourceKind: "islamuevent_user",
            resourceId: "user-self",
            resourceAttrs: new { tenantId = "tenant-1" },
            actions: ["view", "update", "delete"]);

        await Assert.That(result).ContainsKey("view").And.Value.IsEqualTo("EFFECT_ALLOW");
        await Assert.That(result).ContainsKey("update").And.Value.IsEqualTo("EFFECT_ALLOW").Because("a signed-in user must be able to save their own account settings profile even when the auth subject differs from the domain user id");
        await Assert.That(result).ContainsKey("delete").And.Value.IsEqualTo("EFFECT_DENY").Because("self-service profile editing must not imply self-service hard deletion");
    }

    [Test]
    public async Task RegularUser_ShouldBeDenied_UpdateAnotherUserProfile()
    {
        var result = await _cerbos.CheckResourceAsync(
            principalId: "external-subject-self",
            principalRoles: ["islamuevent_authenticated_user"],
            principalAttrs: new { userId = "user-self", isInstanceAdmin = false, tenantMemberships = new { }, orgMemberships = new { } },
            resourceKind: "islamuevent_user",
            resourceId: "user-other",
            resourceAttrs: new { tenantId = "tenant-1" },
            actions: ["update", "delete"]);

        await Assert.That(result).ContainsKey("update").And.Value.IsEqualTo("EFFECT_DENY");
        await Assert.That(result).ContainsKey("delete").And.Value.IsEqualTo("EFFECT_DENY");
    }

    public static IEnumerable<string> GetAllViewableResources()
    {
        yield return "islamuevent_event";
        yield return "islamuevent_organization";
        yield return "islamuevent_tenant";
        yield return "islamuevent_user";
        yield return "islamuevent_category";
        yield return "islamuevent_tag";
        yield return "islamuevent_actor";
        yield return "islamuevent_group";
        yield return "islamuevent_group_member";
        yield return "islamuevent_event_session";
        yield return "islamuevent_event_day";
        yield return "islamuevent_event_agenda_item";
        yield return "islamuevent_event_session_agenda_item";
        yield return "islamuevent_organization_review";
        yield return "islamuevent_notification";
        yield return "islamuevent_custom_property_definition";
        yield return "islamuevent_custom_property_value";
    }

    [Test]
    [MethodDataSource(nameof(GetAllViewableResources))]
    public async Task RegularUser_ShouldBeAllowed_ViewAllResources(string resourceKind)
    {
        var result = await _cerbos.CheckResourceAsync(
            principalId: "user-regular",
            principalRoles: ["islamuevent_authenticated_user"],
            principalAttrs: new { isInstanceAdmin = false, tenantMemberships = new { }, orgMemberships = new { } },
            resourceKind: resourceKind,
            resourceId: "resource-1",
            resourceAttrs: new { tenantId = "tenant-1", organizationId = "org-1" },
            actions: ["view"]);

        await Assert.That(result).ContainsKey("view").And.Value.IsEqualTo("EFFECT_ALLOW").Because($"regular users should be able to view '{resourceKind}' resources");
    }

    [Test]
    public async Task RegularUser_ShouldBeDenied_ViewPhysicalLocations()
    {
        var result = await _cerbos.CheckResourceAsync(
            principalId: "user-regular",
            principalRoles: ["islamuevent_authenticated_user"],
            principalAttrs: new { isInstanceAdmin = false, tenantMemberships = new { }, orgMemberships = new { } },
            resourceKind: "islamuevent_location",
            resourceId: "location-1",
            resourceAttrs: new { tenantId = "tenant-1", organizationId = "org-1" },
            actions: ["view"]);

        await Assert.That(result).ContainsKey("view").And.Value.IsEqualTo("EFFECT_DENY").Because("generic physical-location data is restricted to tenant administrators");
    }

    [Test]
    public async Task RegularUser_ShouldBeDenied_ViewTenantUserRoleGrants()
    {
        var result = await _cerbos.CheckResourceAsync(
            principalId: "user-regular",
            principalRoles: ["islamuevent_authenticated_user"],
            principalAttrs: new { isInstanceAdmin = false, tenantMemberships = new { }, orgMemberships = new { } },
            resourceKind: "islamuevent_tenant_user_role_grant",
            resourceId: "role-grant-1",
            resourceAttrs: new { tenantId = "tenant-1", tenantUserId = "tenant-user-1", userId = "user-1" },
            actions: ["view"]);

        await Assert.That(result).ContainsKey("view").And.Value.IsEqualTo("EFFECT_DENY").Because("tenant role grants include identity, role, grant, and revocation metadata and are tenant-admin-only");
    }

    [Test]
    public async Task RegularUser_ShouldBeDenied_ViewOrganizationMembers()
    {
        var result = await _cerbos.CheckResourceAsync(
            principalId: "user-regular",
            principalRoles: ["islamuevent_authenticated_user"],
            principalAttrs: new { isInstanceAdmin = false, tenantMemberships = new { }, orgMemberships = new { } },
            resourceKind: "islamuevent_organization_member",
            resourceId: "member-1",
            resourceAttrs: new { tenantId = "tenant-1", organizationId = "org-1", userId = "user-1" },
            actions: ["view"]);

        await Assert.That(result).ContainsKey("view").And.Value.IsEqualTo("EFFECT_DENY").Because("organization members include identity, role, and position metadata and are admin/member-management data");
    }

    #endregion

    #region Instance Settings — Restricted to Instance Admin

    [Test]
    public async Task RegularUser_ShouldBeDenied_UpdateInstanceSettings()
    {
        var result = await _cerbos.CheckResourceAsync(
            principalId: "user-regular",
            principalRoles: ["islamuevent_authenticated_user"],
            principalAttrs: new { isInstanceAdmin = false, tenantMemberships = new { }, orgMemberships = new { } },
            resourceKind: "islamuevent_instance_setting",
            resourceId: "setting-1",
            resourceAttrs: new { tenantId = "tenant-1" },
            actions: ["update", "delete", "lock", "unlock"]);

        await Assert.That(result).ContainsKey("update").And.Value.IsEqualTo("EFFECT_DENY");
        await Assert.That(result).ContainsKey("delete").And.Value.IsEqualTo("EFFECT_DENY");
        await Assert.That(result).ContainsKey("lock").And.Value.IsEqualTo("EFFECT_DENY");
        await Assert.That(result).ContainsKey("unlock").And.Value.IsEqualTo("EFFECT_DENY");
    }

    [Test]
    public async Task TenantAdmin_ShouldBeDenied_ModifyInstanceSettings()
    {
        var result = await _cerbos.CheckResourceAsync(
            principalId: "user-tenant-admin",
            principalRoles: ["islamuevent_authenticated_user"],
            principalAttrs: new
            {
                isInstanceAdmin = false,
                tenantMemberships = new Dictionary<string, string> { ["tenant-1"] = "admin" },
                orgMemberships = new { }
            },
            resourceKind: "islamuevent_instance_setting",
            resourceId: "setting-1",
            resourceAttrs: new { tenantId = "tenant-1" },
            actions: ["update", "lock", "unlock"]);

        await Assert.That(result).ContainsKey("update").And.Value.IsEqualTo("EFFECT_DENY").Because("tenant admins cannot modify instance-level settings");
        await Assert.That(result).ContainsKey("lock").And.Value.IsEqualTo("EFFECT_DENY");
        await Assert.That(result).ContainsKey("unlock").And.Value.IsEqualTo("EFFECT_DENY");
    }

    #endregion

    #region Tenant-Scoped Lookup Resources

    public static IEnumerable<string> GetTenantScopedLookupResources()
    {
        yield return "islamuevent_category";
        yield return "islamuevent_tag";
        yield return "islamuevent_location";
    }

    [Test]
    [MethodDataSource(nameof(GetTenantScopedLookupResources))]
    public async Task TenantAdmin_ShouldBeAllowed_ManageLookupResourcesInOwnTenant(string resourceKind)
    {
        var result = await _cerbos.CheckResourceAsync(
            principalId: "user-tenant-admin",
            principalRoles: ["islamuevent_authenticated_user"],
            principalAttrs: new
            {
                isInstanceAdmin = false,
                tenantMemberships = new Dictionary<string, string> { ["tenant-1"] = "admin" },
                orgMemberships = new { }
            },
            resourceKind: resourceKind,
            resourceId: "resource-1",
            resourceAttrs: new { tenantId = "tenant-1", organizationId = "org-1" },
            actions: ["view", "create", "update", "delete"]);

        foreach (var action in new[] { "view", "create", "update", "delete" })
        {
            await Assert.That(result).ContainsKey(action).And.Value.IsEqualTo("EFFECT_ALLOW").Because($"tenant admin should be allowed '{action}' on '{resourceKind}' in own tenant");
        }
    }

    [Test]
    [MethodDataSource(nameof(GetTenantScopedLookupResources))]
    public async Task RegularUser_ShouldBeDenied_MutateLookupResources(string resourceKind)
    {
        var result = await _cerbos.CheckResourceAsync(
            principalId: "user-regular",
            principalRoles: ["islamuevent_authenticated_user"],
            principalAttrs: new { isInstanceAdmin = false, tenantMemberships = new { }, orgMemberships = new { } },
            resourceKind: resourceKind,
            resourceId: "resource-1",
            resourceAttrs: new { tenantId = "tenant-1", organizationId = "org-1" },
            actions: ["create", "update", "delete"]);

        foreach (var action in new[] { "create", "update", "delete" })
        {
            await Assert.That(result).ContainsKey(action).And.Value.IsEqualTo("EFFECT_DENY").Because($"regular user should be denied '{action}' on '{resourceKind}'");
        }
    }

    #endregion

    #region Org-Scoped Event Sub-Resources

    public static IEnumerable<string> GetOrgScopedEventSubResources()
    {
        yield return "islamuevent_event_session";
        yield return "islamuevent_event_day";
        yield return "islamuevent_event_agenda_item";
        yield return "islamuevent_event_session_agenda_item";
    }

    [Test]
    [MethodDataSource(nameof(GetOrgScopedEventSubResources))]
    public async Task OrgAdmin_ShouldBeAllowed_ManageEventSubResourcesInOwnOrg(string resourceKind)
    {
        var result = await _cerbos.CheckResourceAsync(
            principalId: "user-org-admin",
            principalRoles: ["islamuevent_authenticated_user"],
            principalAttrs: new
            {
                isInstanceAdmin = false,
                tenantMemberships = new { },
                orgMemberships = new Dictionary<string, string> { ["org-1"] = "admin" }
            },
            resourceKind: resourceKind,
            resourceId: "sub-1",
            resourceAttrs: new { tenantId = "tenant-1", organizationId = "org-1" },
            actions: ["view", "create", "update", "delete"]);

        foreach (var action in new[] { "view", "create", "update", "delete" })
        {
            await Assert.That(result).ContainsKey(action).And.Value.IsEqualTo("EFFECT_ALLOW").Because($"org admin should be allowed '{action}' on '{resourceKind}' in own org");
        }
    }

    [Test]
    [MethodDataSource(nameof(GetOrgScopedEventSubResources))]
    public async Task RegularUser_ShouldBeAllowed_ViewEventSubResources(string resourceKind)
    {
        var result = await _cerbos.CheckResourceAsync(
            principalId: "user-regular",
            principalRoles: ["islamuevent_authenticated_user"],
            principalAttrs: new { isInstanceAdmin = false, tenantMemberships = new { }, orgMemberships = new { } },
            resourceKind: resourceKind,
            resourceId: "sub-1",
            resourceAttrs: new { tenantId = "tenant-1", organizationId = "org-1" },
            actions: ["view", "create"]);

        await Assert.That(result).ContainsKey("view").And.Value.IsEqualTo("EFFECT_ALLOW");
        await Assert.That(result).ContainsKey("create").And.Value.IsEqualTo("EFFECT_DENY").Because($"regular users should be denied create on '{resourceKind}'");
    }

    #endregion

    #region Organization Reviews — User Can Create

    [Test]
    public async Task RegularUser_ShouldBeAllowed_CreateOrganizationReviews()
    {
        var result = await _cerbos.CheckResourceAsync(
            principalId: "user-regular",
            principalRoles: ["islamuevent_authenticated_user"],
            principalAttrs: new { isInstanceAdmin = false, tenantMemberships = new { }, orgMemberships = new { } },
            resourceKind: "islamuevent_organization_review",
            resourceId: "review-1",
            resourceAttrs: new { tenantId = "tenant-1", organizationId = "org-1" },
            actions: ["create", "view"]);

        await Assert.That(result).ContainsKey("create").And.Value.IsEqualTo("EFFECT_ALLOW").Because("all authenticated users can create organization reviews");
        await Assert.That(result).ContainsKey("view").And.Value.IsEqualTo("EFFECT_ALLOW");
    }

    [Test]
    public async Task RegularUser_ShouldBeDenied_DeleteOrganizationReviews()
    {
        var result = await _cerbos.CheckResourceAsync(
            principalId: "user-regular",
            principalRoles: ["islamuevent_authenticated_user"],
            principalAttrs: new { isInstanceAdmin = false, tenantMemberships = new { }, orgMemberships = new { } },
            resourceKind: "islamuevent_organization_review",
            resourceId: "review-1",
            resourceAttrs: new { tenantId = "tenant-1", organizationId = "org-1" },
            actions: ["delete", "update"]);

        await Assert.That(result).ContainsKey("delete").And.Value.IsEqualTo("EFFECT_DENY");
        await Assert.That(result).ContainsKey("update").And.Value.IsEqualTo("EFFECT_DENY");
    }

    #endregion

    #region Notification — All Users Can Manage

    [Test]
    public async Task RegularUser_ShouldBeAllowed_ManageNotifications()
    {
        var result = await _cerbos.CheckResourceAsync(
            principalId: "user-regular",
            principalRoles: ["islamuevent_authenticated_user"],
            principalAttrs: new { isInstanceAdmin = false, tenantMemberships = new { }, orgMemberships = new { } },
            resourceKind: "islamuevent_notification",
            resourceId: "notif-1",
            resourceAttrs: new { tenantId = "tenant-1", organizationId = "org-1" },
            actions: ["view", "create", "update", "delete"]);

        foreach (var action in new[] { "view", "create", "update", "delete" })
        {
            await Assert.That(result).ContainsKey(action).And.Value.IsEqualTo("EFFECT_ALLOW").Because("all authenticated users should be able to manage their notifications");
        }
    }

    #endregion

    #region Custom Property Resources

    [Test]
    public async Task RegularUser_ShouldBeAllowed_ViewCustomPropertyDefinitions()
    {
        var result = await _cerbos.CheckResourceAsync(
            principalId: "user-regular",
            principalRoles: ["islamuevent_authenticated_user"],
            principalAttrs: new { isInstanceAdmin = false, tenantMemberships = new { }, orgMemberships = new { } },
            resourceKind: "islamuevent_custom_property_definition",
            resourceId: "cpd-1",
            resourceAttrs: new { tenantId = "tenant-1", organizationId = "org-1" },
            actions: ["view", "create"]);

        await Assert.That(result).ContainsKey("view").And.Value.IsEqualTo("EFFECT_ALLOW");
        await Assert.That(result).ContainsKey("create").And.Value.IsEqualTo("EFFECT_DENY").Because("only tenant admins can create custom property definitions");
    }

    [Test]
    public async Task OrgAdmin_ShouldBeAllowed_ManageCustomPropertyValues()
    {
        var result = await _cerbos.CheckResourceAsync(
            principalId: "user-org-admin",
            principalRoles: ["islamuevent_authenticated_user"],
            principalAttrs: new
            {
                isInstanceAdmin = false,
                tenantMemberships = new { },
                orgMemberships = new Dictionary<string, string> { ["org-1"] = "admin" }
            },
            resourceKind: "islamuevent_custom_property_value",
            resourceId: "cpv-1",
            resourceAttrs: new { tenantId = "tenant-1", organizationId = "org-1" },
            actions: ["view", "create", "update", "delete"]);

        foreach (var action in new[] { "view", "create", "update", "delete" })
        {
            await Assert.That(result).ContainsKey(action).And.Value.IsEqualTo("EFFECT_ALLOW").Because($"org admin should manage custom property values for their org");
        }
    }

    #endregion

    #region Location Room — Org Admin Access

    [Test]
    public async Task OrgAdmin_ShouldBeDenied_ManageLocationRooms()
    {
        var result = await _cerbos.CheckResourceAsync(
            principalId: "user-org-admin",
            principalRoles: ["islamuevent_authenticated_user"],
            principalAttrs: new
            {
                isInstanceAdmin = false,
                tenantMemberships = new { },
                orgMemberships = new Dictionary<string, string> { ["org-1"] = "admin" }
            },
            resourceKind: "islamuevent_location_room",
            resourceId: "room-1",
            resourceAttrs: new { tenantId = "tenant-1", organizationId = "org-1" },
            actions: ["view", "create", "update", "delete"]);

        foreach (var action in new[] { "view", "create", "update", "delete" })
        {
            await Assert.That(result).ContainsKey(action).And.Value.IsEqualTo("EFFECT_DENY").Because($"generic location rooms are tenant-admin-only physical-location data");
        }
    }

    #endregion
}
