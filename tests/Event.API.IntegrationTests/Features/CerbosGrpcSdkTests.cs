using Cerbos.Api.V1.Effect;
using Cerbos.Sdk;
using Cerbos.Sdk.Builder;
using Cerbos.Sdk.Response;
using Cerbos.Sdk.Utility;
using Event.Api.IntegrationTests.Fixtures;
using Grpc.Net.Client;
using TUnit.Core;

namespace Event.Api.IntegrationTests.Features;

/// <summary>
/// Production-faithful gRPC SDK tests mirroring how
/// <c>CerbosAuthorizationService</c> uses the Cerbos SDK.
/// Validates the gRPC transport path separately from HTTP API tests.
/// </summary>
[Category(TestCategories.PolicyContract)]
[NotInParallel("SecurityInfra")]
[ClassDataSource<SecurityInfrastructureFixture>(Shared = SharedType.PerAssembly)]
public class CerbosGrpcSdkTests : IDisposable
{
    private readonly ICerbosClient _client;

    public CerbosGrpcSdkTests(SecurityInfrastructureFixture infra)
    {
        var grpcEndpoint = infra.CerbosGrpcEndpoint;

        var builder = CerbosClientBuilder
            .ForTarget(grpcEndpoint)
            .WithGrpcChannelOptions(new GrpcChannelOptions
            {
                HttpHandler = new SocketsHttpHandler(),
                DisposeHttpClient = true
            });

        if (grpcEndpoint.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            builder = builder.WithPlaintext();
        }

        _client = builder.Build();
    }

    public void Dispose()
    {
        (_client as IDisposable)?.Dispose();
    }

    #region Instance Admin via gRPC SDK

    [Test]
    public async Task GrpcSdk_InstanceAdmin_ShouldBeAllowedAllEventActions()
    {
        var principal = Principal
            .NewInstance("user-instance-admin", "islamuevent_authenticated_user")
            .WithAttribute("isInstanceAdmin", AttributeValue.BoolValue(true))
            .WithAttribute("tenantMemberships", AttributeValue.MapValue(new Dictionary<string, AttributeValue>()))
            .WithAttribute("orgMemberships", AttributeValue.MapValue(new Dictionary<string, AttributeValue>()));

        var resource = ResourceEntry
            .NewInstance("islamuevent_event", "event-grpc-1")
            .WithActions("view", "create", "update", "delete", "moderate-light");

        var response = await SendCheckResourcesAsync(principal, resource);
        var result = response.Find("event-grpc-1");

        await Assert.That(result).IsNotNull();
        await AssertEffect(result!, "view", Effect.Allow);
        await AssertEffect(result!, "create", Effect.Deny);
        await AssertEffect(result!, "update", Effect.Deny);
        await AssertEffect(result!, "delete", Effect.Deny);
        await AssertEffect(result!, "moderate-light", Effect.Allow);
    }

    #endregion

    #region Regular User via gRPC SDK

    [Test]
    public async Task GrpcSdk_RegularUser_ShouldOnlyViewEvents()
    {
        var principal = BuildRegularUserPrincipal();
        var resource = ResourceEntry
            .NewInstance("islamuevent_event", "event-grpc-2")
            .WithAttribute("tenantId", AttributeValue.StringValue("tenant-1"))
            .WithAttribute("organizationId", AttributeValue.StringValue("org-1"))
            .WithActions("view", "create", "update", "delete");

        var response = await SendCheckResourcesAsync(principal, resource);
        var result = response.Find("event-grpc-2")!;

        await AssertEffect(result, "view", Effect.Allow);
        await AssertEffect(result, "create", Effect.Deny);
        await AssertEffect(result, "update", Effect.Deny);
        await AssertEffect(result, "delete", Effect.Deny);
    }

    [Test]
    public async Task GrpcSdk_RegularUser_ShouldPassEventPreCreateGate()
    {
        var principal = BuildRegularUserPrincipal();
        var resource = ResourceEntry
            .NewInstance("islamuevent_event", "create")
            .WithAttribute("tenantId", AttributeValue.StringValue("tenant-1"))
            .WithAttribute("authorizationPhase", AttributeValue.StringValue("pre_create"))
            .WithActions("create");

        var response = await SendCheckResourcesAsync(principal, resource);
        var result = response.Find("create")!;

        await AssertEffect(result, "create", Effect.Allow,
            "human event pre-create checks are allowed through to CreateEventCommandHandler, where EventActorResolver enforces publishing policy");
    }

    [Test]
    public async Task GrpcSdk_ScopedUserOwnedEvent_ShouldFallbackToBundledRootPolicy()
    {
        const string tenantId = "tenant-1";
        const string userId = "user-owned-event-owner";
        const string eventId = "event-grpc-user-owned-scoped";

        var principal = Principal
            .NewInstance(userId, "islamuevent_authenticated_user")
            .WithAttribute("userId", AttributeValue.StringValue(userId))
            .WithAttribute("isInstanceAdmin", AttributeValue.BoolValue(false))
            .WithAttribute("tenantMemberships", AttributeValue.MapValue(new Dictionary<string, AttributeValue>()))
            .WithAttribute("orgMemberships", AttributeValue.MapValue(new Dictionary<string, AttributeValue>()));

        var resource = ResourceEntry
            .NewInstance("islamuevent_event", eventId)
            .WithScope(tenantId)
            .WithAttribute("tenantId", AttributeValue.StringValue(tenantId))
            .WithAttribute("eventId", AttributeValue.StringValue(eventId))
            .WithAttribute("actorId", AttributeValue.StringValue("actor-user-owned-event"))
            .WithAttribute("userId", AttributeValue.StringValue(userId))
            .WithActions("update", "delete", "publish");

        var response = await SendCheckResourcesAsync(principal, resource);
        var result = response.Find(eventId);

        await Assert.That(result).IsNotNull().Because("tenant-scoped HATEOAS event checks must still use the bundled root policy when no tenant override exists");
        await AssertEffect(result!, "update", Effect.Allow);
        await AssertEffect(result!, "delete", Effect.Allow);
        await AssertEffect(result!, "publish", Effect.Allow);
    }

    [Test]
    public async Task GrpcSdk_MachineCaller_ShouldNotPassHumanEventPreCreateGate()
    {
        var principal = BuildMachinePrincipal();
        var resource = ResourceEntry
            .NewInstance("islamuevent_event", "create")
            .WithAttribute("tenantId", AttributeValue.StringValue("tenant-1"))
            .WithAttribute("authorizationPhase", AttributeValue.StringValue("pre_create"))
            .WithActions("create");

        var response = await SendCheckResourcesAsync(principal, resource);
        var result = response.Find("create")!;

        await AssertEffect(result, "create", Effect.Deny,
            "machine/API-key event create is governed by scope and owner checks, not the human pre-create rule");
    }

    #endregion

    #region Tenant Admin via gRPC SDK

    [Test]
    public async Task GrpcSdk_TenantAdmin_ShouldManageEventsInOwnTenant()
    {
        var principal = BuildTenantAdminPrincipal("tenant-1");
        var resource = ResourceEntry
            .NewInstance("islamuevent_event", "event-grpc-3")
            .WithAttribute("tenantId", AttributeValue.StringValue("tenant-1"))
            .WithAttribute("organizationId", AttributeValue.StringValue("org-1"))
            .WithActions("view", "create", "update", "moderate-light");

        var response = await SendCheckResourcesAsync(principal, resource);
        var result = response.Find("event-grpc-3")!;

        await AssertEffect(result, "view", Effect.Allow);
        await AssertEffect(result, "create", Effect.Deny);
        await AssertEffect(result, "update", Effect.Deny);
        await AssertEffect(result, "moderate-light", Effect.Allow);
    }

    [Test]
    public async Task GrpcSdk_TenantAdmin_ShouldBeDeniedInOtherTenant()
    {
        var principal = BuildTenantAdminPrincipal("tenant-1");
        var resource = ResourceEntry
            .NewInstance("islamuevent_event", "event-grpc-4")
            .WithAttribute("tenantId", AttributeValue.StringValue("tenant-other"))
            .WithAttribute("organizationId", AttributeValue.StringValue("org-1"))
            .WithActions("create", "update", "delete");

        var response = await SendCheckResourcesAsync(principal, resource);
        var result = response.Find("event-grpc-4")!;

        await AssertEffect(result, "create", Effect.Deny);
        await AssertEffect(result, "update", Effect.Deny);
        await AssertEffect(result, "delete", Effect.Deny);
    }

    #endregion

    #region Org Admin via gRPC SDK

    [Test]
    public async Task GrpcSdk_OrgAdmin_ShouldManageEventsInOwnOrg()
    {
        var principal = BuildOrgAdminPrincipal("org-1");
        var resource = ResourceEntry
            .NewInstance("islamuevent_event", "event-grpc-5")
            .WithAttribute("tenantId", AttributeValue.StringValue("tenant-1"))
            .WithAttribute("organizationId", AttributeValue.StringValue("org-1"))
            .WithActions("view", "create", "update", "delete");

        var response = await SendCheckResourcesAsync(principal, resource);
        var result = response.Find("event-grpc-5")!;

        await AssertEffect(result, "view", Effect.Allow);
        await AssertEffect(result, "create", Effect.Allow);
        await AssertEffect(result, "update", Effect.Allow);
        await AssertEffect(result, "delete", Effect.Allow);
    }

    [Test]
    public async Task GrpcSdk_OrgAdmin_ShouldBeDeniedInOtherOrg()
    {
        var principal = BuildOrgAdminPrincipal("org-1");
        var resource = ResourceEntry
            .NewInstance("islamuevent_event", "event-grpc-6")
            .WithAttribute("tenantId", AttributeValue.StringValue("tenant-1"))
            .WithAttribute("organizationId", AttributeValue.StringValue("org-other"))
            .WithActions("create", "update", "delete");

        var response = await SendCheckResourcesAsync(principal, resource);
        var result = response.Find("event-grpc-6")!;

        await AssertEffect(result, "create", Effect.Deny);
        await AssertEffect(result, "update", Effect.Deny);
        await AssertEffect(result, "delete", Effect.Deny);
    }

    #endregion

    #region Batch Check via gRPC SDK

    [Test]
    public async Task GrpcSdk_BatchCheck_ShouldReturnResultsForMultipleResources()
    {
        var principal = BuildRegularUserPrincipal();

        var eventResource = ResourceEntry
            .NewInstance("islamuevent_event", "batch-event-1")
            .WithAttribute("tenantId", AttributeValue.StringValue("tenant-1"))
            .WithAttribute("organizationId", AttributeValue.StringValue("org-1"))
            .WithActions("view");

        var orgResource = ResourceEntry
            .NewInstance("islamuevent_organization", "batch-org-1")
            .WithAttribute("tenantId", AttributeValue.StringValue("tenant-1"))
            .WithAttribute("organizationId", AttributeValue.StringValue("org-1"))
            .WithActions("view");

        var request = CheckResourcesRequest
            .NewInstance()
            .WithRequestId(RequestId.Generate())
            .WithPrincipal(principal)
            .WithResourceEntries(eventResource, orgResource);

        var response = await _client.CheckResourcesAsync(request);

        var eventResult = response.Find("batch-event-1")!;
        var orgResult = response.Find("batch-org-1")!;

        await AssertEffect(eventResult, "view", Effect.Allow);
        await AssertEffect(orgResult, "view", Effect.Allow);
    }

    #endregion

    #region Tenant Setting Lock via gRPC SDK

    [Test]
    public async Task GrpcSdk_TenantAdmin_ShouldBeDeniedUpdateWhenLockedByInstance()
    {
        var principal = BuildTenantAdminPrincipal("tenant-1");
        var resource = ResourceEntry
            .NewInstance("islamuevent_tenant_setting", "setting-grpc-1")
            .WithAttribute("tenantId", AttributeValue.StringValue("tenant-1"))
            .WithAttribute("isLockedByInstance", AttributeValue.BoolValue(true))
            .WithActions("update");

        var response = await SendCheckResourcesAsync(principal, resource);
        var result = response.Find("setting-grpc-1")!;

        await AssertEffect(result, "update", Effect.Deny,
            "tenant admin must be denied update when isLockedByInstance=true");
    }


    [Test]
    public async Task GrpcSdk_TenantAdmin_ShouldBeAllowedTenantBrandingDocumentUpdateWhenLockedForHandlerValidation()
    {
        var principal = BuildTenantAdminPrincipal("tenant-1");
        var resource = ResourceEntry
            .NewInstance("islamuevent_tenant_setting", "setting-grpc-tenant-branding")
            .WithAttribute("tenantId", AttributeValue.StringValue("tenant-1"))
            .WithAttribute("documentKey", AttributeValue.StringValue("tenant.branding"))
            .WithAttribute("isLockedByInstance", AttributeValue.BoolValue(true))
            .WithActions("update");

        var response = await SendCheckResourcesAsync(principal, resource);
        var result = response.Find("setting-grpc-tenant-branding")!;

        await AssertEffect(result, "update", Effect.Allow,
            "tenant.branding uses handler-level field locks after resource authorization");
    }

    #endregion

    #region Helpers

    private async Task<CheckResourcesResponse> SendCheckResourcesAsync(
        Principal principal, ResourceEntry resource)
    {
        var request = CheckResourcesRequest
            .NewInstance()
            .WithRequestId(RequestId.Generate())
            .WithPrincipal(principal)
            .WithResourceEntries(resource);

        return await _client.CheckResourcesAsync(request);
    }

    private static async Task AssertEffect(
        CheckResourcesResponse.Types.ResultEntry result,
        string action,
        Effect expected,
        string? because = null)
    {
        await Assert.That(result.Actions.TryGetValue(action, out var actual)).IsTrue().Because($"action '{action}' must be present in the Cerbos response");
        await Assert.That(actual).IsEqualTo(expected).Because(because ?? $"action '{action}' should be {expected}");
    }

    private static Principal BuildRegularUserPrincipal() =>
        Principal
            .NewInstance("user-regular-grpc", "islamuevent_authenticated_user")
            .WithAttribute("isInstanceAdmin", AttributeValue.BoolValue(false))
            .WithAttribute("tenantMemberships", AttributeValue.MapValue(new Dictionary<string, AttributeValue>()))
            .WithAttribute("orgMemberships", AttributeValue.MapValue(new Dictionary<string, AttributeValue>()));

    private static Principal BuildMachinePrincipal() =>
        Principal
            .NewInstance("api-key-machine-grpc", "islamuevent_authenticated_user")
            .WithAttribute("isInstanceAdmin", AttributeValue.BoolValue(false))
            .WithAttribute("tenantMemberships", AttributeValue.MapValue(new Dictionary<string, AttributeValue>()))
            .WithAttribute("orgMemberships", AttributeValue.MapValue(new Dictionary<string, AttributeValue>()))
            .WithAttribute("is_machine", AttributeValue.BoolValue(true));

    private static Principal BuildTenantAdminPrincipal(string tenantId) =>
        Principal
            .NewInstance("user-tenant-admin-grpc", "islamuevent_authenticated_user")
            .WithAttribute("isInstanceAdmin", AttributeValue.BoolValue(false))
            .WithAttribute("tenantMemberships", AttributeValue.MapValue(new Dictionary<string, AttributeValue>
            {
                [tenantId] = AttributeValue.StringValue("admin")
            }))
            .WithAttribute("orgMemberships", AttributeValue.MapValue(new Dictionary<string, AttributeValue>()));

    private static Principal BuildOrgAdminPrincipal(string orgId) =>
        Principal
            .NewInstance("user-org-admin-grpc", "islamuevent_authenticated_user")
            .WithAttribute("isInstanceAdmin", AttributeValue.BoolValue(false))
            .WithAttribute("tenantMemberships", AttributeValue.MapValue(new Dictionary<string, AttributeValue>()))
            .WithAttribute("orgMemberships", AttributeValue.MapValue(new Dictionary<string, AttributeValue>
            {
                [orgId] = AttributeValue.StringValue("admin")
            }));

    #endregion
}
