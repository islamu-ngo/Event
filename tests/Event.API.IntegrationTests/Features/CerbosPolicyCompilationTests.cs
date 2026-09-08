using Event.Api.IntegrationTests.Fixtures;
using TUnit.Core;

namespace Event.Api.IntegrationTests.Features;

/// <summary>
/// Validates that all Cerbos policy files compile and load correctly in the containerized PDP.
/// Uses the Cerbos HTTP API to verify policy loading, health status, and schema validation.
/// Catches YAML syntax errors, invalid rule conditions, missing imports, and schema mismatches
/// before they reach production.
/// </summary>
[Category(TestCategories.PolicyContract)]
[NotInParallel("SecurityInfra")]
[ClassDataSource<SecurityInfrastructureFixture>(Shared = SharedType.PerAssembly)]
public class CerbosPolicyCompilationTests : IDisposable
{
    private readonly CerbosTestClient _cerbos;

    public CerbosPolicyCompilationTests(SecurityInfrastructureFixture infra)
    {
        _cerbos = new CerbosTestClient(infra.CerbosHttpEndpoint);
    }

    public void Dispose()
    {
        _cerbos.Dispose();
    }

    #region Container Health

    [Test]
    public async Task CerbosContainer_ShouldBeHealthy()
    {
        var response = await _cerbos.HealthAsync();

        await Assert.That(response.IsSuccessStatusCode).IsTrue().Because("the Cerbos container must report healthy after startup");
    }

    [Test]
    public async Task CerbosContainer_ShouldServeOpenAPISpec()
    {
        var response = await _cerbos.GetSchemaAsync();

        await Assert.That(response.IsSuccessStatusCode).IsTrue().Because("the Cerbos HTTP API must be serving the schema endpoint");
    }

    #endregion

    #region Derived Roles Loading

    [Test]
    public async Task DerivedRoles_ShouldBeLoaded()
    {
        var result = await _cerbos.CheckResourceAsync(
            principalId: "test-derived-roles",
            principalRoles: ["islamuevent_authenticated_user"],
            principalAttrs: new { isInstanceAdmin = true, tenantMemberships = new { }, orgMemberships = new { } },
            resourceKind: "islamuevent_event",
            resourceId: "test",
            resourceAttrs: new { tenantId = "tenant-1", organizationId = "org-1" },
            actions: ["view"]);

        await Assert.That(result).ContainsKey("view").And.Value.IsEqualTo("EFFECT_ALLOW").Because("the derived roles (islamuevent_explore_admin_roles) must be loaded for islamuevent_instance_admin to match");
    }

    #endregion

    #region All Resource Policies Are Loadable

    public static IEnumerable<string> GetAllExpectedResourcePolicies()
    {
        yield return "islamuevent_event";
        yield return "islamuevent_organization";
        yield return "islamuevent_tenant";
        yield return "islamuevent_user";
        yield return "islamuevent_category";
        yield return "islamuevent_tag";
        yield return "islamuevent_location";
        yield return "islamuevent_location_room";
        yield return "islamuevent_actor";
        yield return "islamuevent_tenant_user_role_grant";
        yield return "islamuevent_organization_member";
        yield return "islamuevent_organization_review";
        yield return "islamuevent_group";
        yield return "islamuevent_group_member";
        yield return "islamuevent_storage_object";
        yield return "islamuevent_instance_setting";
        yield return "islamuevent_tenant_setting";
        yield return "islamuevent_event_session";
        yield return "islamuevent_event_day";
        yield return "islamuevent_event_agenda_item";
        yield return "islamuevent_event_session_agenda_item";
        yield return "islamuevent_event_contact_share_consent";
        yield return "islamuevent_support_access_session";
        yield return "islamuevent_notification";
        yield return "islamuevent_custom_property_definition";
        yield return "islamuevent_custom_property_value";
        yield return "islamuevent_custom_property_governance";
        yield return "islamuevent_custom_property_projection";
        yield return "islamuevent_custom_property_template";
        yield return "islamuevent_indexed_did";
        yield return "islamuevent_atproto_record";
    }

    [Test]
    [MethodDataSource(nameof(GetAllExpectedResourcePolicies))]
    public async Task ResourcePolicy_ShouldBeLoadable_AndAcceptCheckRequests(string resourceKind)
    {
        var result = await _cerbos.CheckResourceAsync(
            principalId: "compilation-test-user",
            principalRoles: ["islamuevent_authenticated_user"],
            principalAttrs: new { isInstanceAdmin = true, tenantMemberships = new { }, orgMemberships = new { } },
            resourceKind: resourceKind,
            resourceId: "compilation-test",
            resourceAttrs: new { tenantId = "tenant-1", organizationId = "org-1" },
            actions: ["view"]);

        await Assert.That(result).ContainsKey("view").Because($"resource policy '{resourceKind}' must be loaded and accept check requests");
        await Assert.That(result["view"]).IsEqualTo("EFFECT_ALLOW").Because($"instance admin should always be allowed to view '{resourceKind}'");
    }

    #endregion

    #region Policy Accepts Correct Attributes

    [Test]
    public async Task EventPolicy_ShouldAcceptTenantIdAttribute()
    {
        var result = await _cerbos.CheckResourceAsync(
            principalId: "user-tenant-admin",
            principalRoles: ["islamuevent_authenticated_user"],
            principalAttrs: new
            {
                isInstanceAdmin = false,
                tenantMemberships = new Dictionary<string, string> { ["tenant-42"] = "admin" },
                orgMemberships = new Dictionary<string, string>()
            },
            resourceKind: "islamuevent_event",
            resourceId: "evt-1",
            resourceAttrs: new { tenantId = "tenant-42", organizationId = "org-1" },
            actions: ["view", "moderate-light"]);

        await Assert.That(result["view"]).IsEqualTo("EFFECT_ALLOW");
        await Assert.That(result["moderate-light"]).IsEqualTo("EFFECT_ALLOW").Because("tenant admin matching tenantId must be allowed to moderate events");
    }

    [Test]
    public async Task OrgPolicy_ShouldAcceptOrganizationIdAttribute()
    {
        var result = await _cerbos.CheckResourceAsync(
            principalId: "user-org-admin",
            principalRoles: ["islamuevent_authenticated_user"],
            principalAttrs: new
            {
                isInstanceAdmin = false,
                tenantMemberships = new Dictionary<string, string>(),
                orgMemberships = new Dictionary<string, string> { ["org-99"] = "admin" }
            },
            resourceKind: "islamuevent_organization",
            resourceId: "org-99",
            resourceAttrs: new { tenantId = "tenant-1", organizationId = "org-99" },
            actions: ["view", "update"]);

        await Assert.That(result["view"]).IsEqualTo("EFFECT_ALLOW");
        await Assert.That(result["update"]).IsEqualTo("EFFECT_ALLOW").Because("org admin matching organizationId must be allowed to update their org");
    }

    [Test]
    public async Task TenantSettingPolicy_ShouldAcceptIsLockedByInstanceAttribute()
    {
        var result = await _cerbos.CheckResourceAsync(
            principalId: "user-tenant-admin",
            principalRoles: ["islamuevent_authenticated_user"],
            principalAttrs: new
            {
                isInstanceAdmin = false,
                tenantMemberships = new Dictionary<string, string> { ["tenant-1"] = "admin" },
                orgMemberships = new Dictionary<string, string>()
            },
            resourceKind: "islamuevent_tenant_setting",
            resourceId: "setting-1",
            resourceAttrs: new { tenantId = "tenant-1", isLockedByInstance = true },
            actions: ["update"]);

        await Assert.That(result["update"]).IsEqualTo("EFFECT_DENY").Because("tenant admin must be denied update when isLockedByInstance=true");
    }


    [Test]
    public async Task TenantSettingPolicy_ShouldAllowTenantBrandingDocumentUpdateWhenLockedForHandlerValidation()
    {
        var result = await _cerbos.CheckResourceAsync(
            principalId: "user-tenant-admin",
            principalRoles: ["islamuevent_authenticated_user"],
            principalAttrs: new
            {
                isInstanceAdmin = false,
                tenantMemberships = new Dictionary<string, string> { ["tenant-1"] = "admin" },
                orgMemberships = new Dictionary<string, string>()
            },
            resourceKind: "islamuevent_tenant_setting",
            resourceId: "setting-1",
            resourceAttrs: new { tenantId = "tenant-1", documentKey = "tenant.branding", isLockedByInstance = true },
            actions: ["update"]);

        await Assert.That(result["update"]).IsEqualTo("EFFECT_ALLOW").Because("tenant.branding uses handler-level field locks so authorization must allow the command to run");
    }

    #endregion

    #region Negative — Unknown Resource Kind

    [Test]
    public async Task UnknownResourceKind_ShouldDenyAllActions()
    {
        var result = await _cerbos.CheckResourceAsync(
            principalId: "test-user",
            principalRoles: ["islamuevent_authenticated_user"],
            principalAttrs: new { isInstanceAdmin = true, tenantMemberships = new { }, orgMemberships = new { } },
            resourceKind: "nonexistent_resource_kind",
            resourceId: "test",
            resourceAttrs: new { tenantId = "tenant-1", organizationId = "org-1" },
            actions: ["view", "create"]);

        await Assert.That(result["view"]).IsEqualTo("EFFECT_DENY").Because("unknown resource kinds must be denied — no matching policy");
        await Assert.That(result["create"]).IsEqualTo("EFFECT_DENY");
    }

    #endregion
}
