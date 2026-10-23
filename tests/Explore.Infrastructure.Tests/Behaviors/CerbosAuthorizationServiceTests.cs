using Explore.Infrastructure.Tests.Authorization;
using Cerbos.Api.V1.Effect;
using Cerbos.Sdk;
using Cerbos.Sdk.Builder;
using Cerbos.Sdk.Response;
using Explore.Application.Authentication;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Infrastructure.Services;
using Explore.Infrastructure.Identity;
using Explore.Infrastructure.Tests.Fixtures;
using Explore.Application.Contracts.Operations;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NSubstitute.Exceptions;
using System.Text.Json;

namespace Explore.Infrastructure.Tests.Behaviors;

public class CerbosAuthorizationServiceTests : IAsyncDisposable
{
    private const string ArtifactRelativePath = ".omo/start-work/artifacts/authorization-platform-redesign/phase0-task02/cerbos-provider-scenarios.json";
    private const string FixedUserId = "11111111-1111-1111-1111-111111111111";
    private static readonly string FixedTenantId = Guid.CreateVersion7().ToString("D");
    private const string FixedEventId = "33333333-3333-3333-3333-333333333333";
    private readonly CurrentHumanPrincipalFixture _human = new(Guid.Parse(FixedUserId), Guid.Parse(FixedTenantId));

    [Before(Test)]
    public Task InitializeHuman() => _human.InitializeAsync();

    [After(Test)]
    public ValueTask DisposeAsync() => _human.DisposeAsync();

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private static readonly Phase0Scenario[] Phase0Scenarios =
    [
        new(
            Id: "cerbos.missing_subject_current_deny",
            MissingFact: "subject",
            ExpectedGrpcCall: false),
        new(
            Id: "cerbos.missing_tenant_fact_current_deny",
            MissingFact: "tenantId",
            ExpectedGrpcCall: true),
        new(
            Id: "cerbos.missing_resource_fact_current_deny",
            MissingFact: "eventId",
            ExpectedGrpcCall: true),
        new(
            Id: "cerbos.provider_unavailable_current_deny",
            ProviderOutcome: "failure",
            ExpectedGrpcCall: true),
        new(
            Id: "cerbos.provider_deny_current_deny",
            ProviderOutcome: "deny",
            ExpectedGrpcCall: true)
    ];

    private readonly IAdminContext _adminContext;
    private readonly MachinePrincipalAccessor _machinePrincipalAccessor;
    private readonly IHierarchicalSettingsResolver _settingsResolver;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<CerbosAuthorizationService> _logger;
    private readonly ICerbosClient _cerbosClient;
    private readonly ICerbosClientFactory _clientFactory;

    public CerbosAuthorizationServiceTests()
    {
        _adminContext = _human.Admin;
        _machinePrincipalAccessor = _human.Machine;
        _settingsResolver = Substitute.For<IHierarchicalSettingsResolver>();
        _tenantContext = _human.Tenant;
        _logger = Substitute.For<ILogger<CerbosAuthorizationService>>();
        _cerbosClient = Substitute.For<ICerbosClient>();
        _clientFactory = Substitute.For<ICerbosClientFactory>();

        _clientFactory.GetOrCreate(Arg.Any<string>()).Returns(_cerbosClient);
    }

    [Test]
    public async Task IsAllowedBatchAsync_NoUserId_DeniesAllWithoutGrpcCall()
    {
        _human.SignOut();

        var service = CreateService();
        var checks = new List<AuthorizationRequest>
        {
            TestAuthorizationRequest.Create("islamuevent_organization", "org-1", "update", null),
            TestAuthorizationRequest.Create("islamuevent_tenant_setting", "setting-key", AuthorizationActions.View, null)
        };

        var result = await service.AuthorizeBatchAsync(checks);

        await Assert.That(result.Count).IsEqualTo(2);
        await Assert.That(result.All(decision => !decision.IsAllowed)).IsTrue();
        await Assert.That(result.All(decision => decision.Provider.ProviderId == "cerbos")).IsTrue();
        await Assert.That(result.All(decision => decision.Provider.ObservedRevision is null)).IsTrue();
        await Assert.That(result.All(decision => decision.ReasonCode == AuthorizationDecisionReasonCodes.MissingSubject)).IsTrue();
        await _cerbosClient.DidNotReceive().CheckResourcesAsync(
            Arg.Any<CheckResourcesRequest>(), Arg.Any<Metadata>());
    }

    [Test]
    [Arguments("cerbos.missing_subject_current_deny")]
    [Arguments("cerbos.missing_tenant_fact_current_deny")]
    [Arguments("cerbos.missing_resource_fact_current_deny")]
    [Arguments("cerbos.provider_unavailable_current_deny")]
    [Arguments("cerbos.provider_deny_current_deny")]
    public async Task IsAllowedAsync_Phase0CerbosFailureCurrentBaseline(
        string scenario)
    {
        var result = await ExecutePhase0ScenarioAsync(scenario);

        await Assert.That(result.Allowed)
            .IsFalse()
            .Because($"phase-0 provider scenario '{scenario}' must pin current Cerbos fail-closed behavior.");
    }

    [Test]
    public async Task Phase0CerbosProviderScenarioArtifact_ShouldBeGenerated()
    {
        var results = new List<Phase0ScenarioResult>();
        foreach (var scenario in Phase0Scenarios)
        {
            await using var test = new CerbosAuthorizationServiceTests();
            await test.InitializeHuman();
            results.Add(await test.ExecutePhase0ScenarioAsync(scenario.Id));
        }

        var artifact = new Phase0ScenarioArtifact(
            SchemaVersion: 1,
            GeneratedFrom: nameof(CerbosAuthorizationServiceTests),
            TestMethod: nameof(IsAllowedAsync_Phase0CerbosFailureCurrentBaseline),
            Results: results.ToArray(),
            Mismatches: results
                .Where(result => result.Allowed != result.ExpectedAllowed || result.GrpcCallObserved != result.ExpectedGrpcCall)
                .Select(result => result.Id)
                .ToArray());

        var path = Path.Combine(FindRepositoryRoot(), ArtifactRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(artifact, JsonOptions));

        await Assert.That(artifact.Mismatches).IsEmpty();
        await Assert.That(File.Exists(path)).IsTrue();
    }

    [Test]
    public async Task IsAllowedBatchAsync_MapsAllowAndDenyFromCerbosResponse()
    {
        var protoResponse = new Cerbos.Api.V1.Response.CheckResourcesResponse();
        protoResponse.Results.Add(CreateResultEntry("org-1", "islamuevent_organization", "update", Effect.Allow));
        protoResponse.Results.Add(CreateResultEntry("org-2", "islamuevent_organization", "delete", Effect.Deny));

        _cerbosClient.CheckResourcesAsync(Arg.Any<CheckResourcesRequest>(), Arg.Any<Metadata>())
            .Returns(new CheckResourcesResponse(protoResponse));

        var service = CreateService();
        var checks = new List<AuthorizationRequest>
        {
            TestAuthorizationRequest.Create("islamuevent_organization", "org-1", "update", null),
            TestAuthorizationRequest.Create("islamuevent_organization", "org-2", "delete", null)
        };

        var result = await service.IsAllowedBatchAsync(checks);

        await Assert.That(result.Count).IsEqualTo(2);
        await Assert.That(result[0]).IsTrue();
        await Assert.That(result[1]).IsFalse();
        await _cerbosClient.Received(1)
            .CheckResourcesAsync(Arg.Any<CheckResourcesRequest>(), Arg.Any<Metadata>());
    }

    [Test]
    public async Task IsAllowedBatchAsync_MapsRepeatedResourceEntriesByAction()
    {
        const string eventId = "event-1";
        var protoResponse = new Cerbos.Api.V1.Response.CheckResourcesResponse();
        protoResponse.Results.Add(CreateResultEntry(eventId, ResourceKinds.Event, AuthorizationActions.Events.ManageTeam, Effect.Deny));
        protoResponse.Results.Add(CreateResultEntry(eventId, ResourceKinds.Event, AuthorizationActions.Update, Effect.Allow));
        protoResponse.Results.Add(CreateResultEntry(eventId, ResourceKinds.Event, AuthorizationActions.Delete, Effect.Deny));

        _cerbosClient.CheckResourcesAsync(Arg.Any<CheckResourcesRequest>(), Arg.Any<Metadata>())
            .Returns(new CheckResourcesResponse(protoResponse));

        var service = CreateService();
        var checks = new List<AuthorizationRequest>
        {
            TestAuthorizationRequest.Create(ResourceKinds.Event, eventId, AuthorizationActions.Events.ManageTeam, null),
            TestAuthorizationRequest.Create(ResourceKinds.Event, eventId, AuthorizationActions.Update, null),
            TestAuthorizationRequest.Create(ResourceKinds.Event, eventId, AuthorizationActions.Delete, null)
        };

        var result = await service.IsAllowedBatchAsync(checks);

        await Assert.That(result.Count).IsEqualTo(3);
        await Assert.That(result[0]).IsFalse();
        await Assert.That(result[1]).IsTrue();
        await Assert.That(result[2]).IsFalse();
    }

    [Test]
    public async Task IsAllowedBatchAsync_UserIdClaimMissingButResolvable_UsesResolvedUserIdForCerbosPrincipal()
    {
        var resolvedUserId = _human.UserId;
        _human.EnterRequest();
        await Assert.That(_adminContext.UserId).IsNull();

        Cerbos.Api.V1.Request.CheckResourcesRequest? capturedRequest = null;
        var protoResponse = new Cerbos.Api.V1.Response.CheckResourcesResponse();
        protoResponse.Results.Add(CreateResultEntry("storage-upload", "islamuevent_storage_object", "create", Effect.Allow));

        _cerbosClient.CheckResourcesAsync(Arg.Any<CheckResourcesRequest>(), Arg.Any<Metadata>())
            .Returns(call =>
            {
                capturedRequest = call.ArgAt<CheckResourcesRequest>(0).ToCheckResourcesRequest();
                protoResponse.RequestId = capturedRequest.RequestId;
                return new CheckResourcesResponse(protoResponse);
            });

        var service = CreateService();
        var result = await service.IsAllowedAsync("islamuevent_storage_object", "storage-upload", "create");

        await Assert.That(result).IsTrue();
        await Assert.That(capturedRequest).IsNotNull();
        await Assert.That(capturedRequest!.Principal.Id).IsEqualTo(resolvedUserId.ToString());
    }

    [Test]
    public async Task IsAllowedBatchAsync_StorageUploadTypedFacts_FailsClosedWithoutGrpcCall()
    {
        var userId = _human.UserId;

        var service = CreateService();
        var checks = new[]
        {
            TestAuthorizationRequest.Create(
                ResourceKinds.StorageObject,
                "CreateStorageUploadSessionCommand",
                AuthorizationActions.StorageObjects.Create,
                facts: new StorageUploadIntentFacts(
                    userId,
                    Guid.NewGuid(),
                    StorageOwningResourceKinds.OrganizationTenant,
                    Guid.NewGuid(),
                    Guid.NewGuid()))
        };

        var result = await service.IsAllowedBatchAsync(checks);

        await Assert.That(result.Count).IsEqualTo(1);
        await Assert.That(result[0]).IsFalse();
        await _cerbosClient.DidNotReceive().CheckResourcesAsync(
            Arg.Any<CheckResourcesRequest>(), Arg.Any<Metadata>());
    }

    [Test]
    public async Task IsAllowedBatchAsync_EventTypedFacts_ProjectToCerbosResourceAttributes()
    {
        var userId = _human.UserId;
        var tenantId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var organizerOrganizationId = Guid.NewGuid();

        Cerbos.Api.V1.Request.CheckResourcesRequest? capturedRequest = null;
        var protoResponse = new Cerbos.Api.V1.Response.CheckResourcesResponse();
        protoResponse.Results.Add(CreateResultEntry(eventId.ToString("D"), ResourceKinds.Event, AuthorizationActions.Update, Effect.Allow));

        _cerbosClient.CheckResourcesAsync(Arg.Any<CheckResourcesRequest>(), Arg.Any<Metadata>())
            .Returns(call =>
            {
                capturedRequest = call.ArgAt<CheckResourcesRequest>(0).ToCheckResourcesRequest();
                protoResponse.RequestId = capturedRequest.RequestId;
                return new CheckResourcesResponse(protoResponse);
            });

        var service = CreateService();
        var decision = await service.AuthorizePreparedHumanAsync(
            CerbosPrincipalBuilder.BuildPrincipalFromSources(ProjectionFacts(userId)),
            TestAuthorizationRequest.Create(
                ResourceKinds.Event,
                eventId.ToString("D"),
                AuthorizationActions.Update,
                facts: new EventAuthorizationFacts(
                    tenantId,
                    eventId,
                    actorId,
                    null,
                    null,
                    null,
                    actorId,
                    null,
                    organizerOrganizationId,
                    null,
                    "LOCAL",
                    userId)),
            tenantId, usePolicyScope: false, "http://localhost:3593", CancellationToken.None);
        await Assert.That(decision.IsAllowed).IsTrue();
        await Assert.That(decision.Provider.ProviderId).IsEqualTo("cerbos");
        await Assert.That(decision.Provider.ObservedRevision).IsNull();
        await Assert.That(decision.ReasonCode).IsEqualTo(AuthorizationDecisionReasonCodes.Allowed);
        await Assert.That(capturedRequest).IsNotNull();
        var attributes = capturedRequest!.Resources[0].Resource.Attr;
        await Assert.That(attributes["tenantId"].StringValue).IsEqualTo(tenantId.ToString("D"));
        await Assert.That(attributes["eventId"].StringValue).IsEqualTo(eventId.ToString("D"));
        await Assert.That(attributes["organizerOrganizationId"].StringValue).IsEqualTo(organizerOrganizationId.ToString("D"));
    }

    [Test]
    public async Task IsAllowedBatchAsync_EventTicketManagement_EnrichesPrincipalWithExactEventPermission()
    {
        var tenantId = _human.Tenant.TenantId;
        var eventId = Guid.NewGuid();
        var eventCreateOrganizationId = Guid.NewGuid();
        var eventCreateGroupId = Guid.NewGuid();
        await _human.GrantOrganizationAdminAsync(eventCreateOrganizationId);
        await _human.GrantGroupAdminAsync(eventCreateGroupId);
        await _human.GrantEventOwnerAsync(eventId);
        int publishPermission = await _human.Context.Permissions.MaxAsync(permission => permission.Id) + 1;
        _human.Context.Permissions.Add(new Permission
        {
            Id = publishPermission,
            MasterCode = PermissionCodes.EventPublish,
            ResourceKind = "event",
            Action = "publish",
            FullName = "Publish the fixture event",
            GroupName = "Events",
            RoleScopeId = (int)RoleScopeEnum.Event,
            RoleScope = null!,
            IsActive = true
        });
        _human.Context.RolePermissions.Add(new RolePermission
        {
            RoleId = (int)RoleEnum.EventOwner,
            Role = null!,
            PermissionId = publishPermission,
            Permission = null!
        });
        int createPermission = await _human.Context.Permissions
            .Where(permission => permission.MasterCode == PermissionCodes.EventCreate)
            .Select(permission => permission.Id).SingleAsync();
        _human.Context.RolePermissions.AddRange(
            new RolePermission { RoleId = (int)RoleEnum.OrgAdmin, Role = null!, PermissionId = createPermission, Permission = null! },
            new RolePermission { RoleId = (int)RoleEnum.GroupAdmin, Role = null!, PermissionId = createPermission, Permission = null! });
        await _human.Context.SaveChangesAsync();
        _human.Context.ChangeTracker.Clear();

        Cerbos.Api.V1.Request.CheckResourcesRequest? capturedRequest = null;
        var protoResponse = new Cerbos.Api.V1.Response.CheckResourcesResponse();
        protoResponse.Results.Add(CreateResultEntry(
            eventId.ToString(),
            ResourceKinds.Event,
            AuthorizationActions.Events.ManageTickets,
            Effect.Allow));

        _cerbosClient.CheckResourcesAsync(Arg.Any<CheckResourcesRequest>(), Arg.Any<Metadata>())
            .Returns(call =>
            {
                capturedRequest = call.ArgAt<CheckResourcesRequest>(0).ToCheckResourcesRequest();
                protoResponse.RequestId = capturedRequest.RequestId;
                return new CheckResourcesResponse(protoResponse);
            });

        var service = CreateService();
        var allowed = await service.IsAllowedAsync(
            ResourceKinds.Event,
            eventId.ToString(),
            AuthorizationActions.Events.ManageTickets,
            new Dictionary<string, object> { ["eventId"] = eventId });

        await Assert.That(allowed).IsTrue();
        await Assert.That(capturedRequest).IsNotNull();
        await Assert.That(capturedRequest!.Principal.Attr.ContainsKey("eventAssignments")).IsTrue();
        await Assert.That(capturedRequest.Principal.Attr.ContainsKey("nowUtc")).IsTrue();
        var eventCreateOrganizations = capturedRequest.Principal.Attr["eventCreateOrganizations"]
            .ListValue.Values.Select(value => value.StringValue).ToArray();
        var eventCreateGroups = capturedRequest.Principal.Attr["eventCreateGroups"]
            .ListValue.Values.Select(value => value.StringValue).ToArray();
        await Assert.That(eventCreateOrganizations).IsEquivalentTo([eventCreateOrganizationId.ToString()]);
        await Assert.That(eventCreateGroups).IsEquivalentTo([eventCreateGroupId.ToString()]);

        var assignmentFields = capturedRequest.Principal.Attr["eventAssignments"]
            .StructValue.Fields[eventId.ToString()]
            .StructValue.Fields;
        var roles = assignmentFields["roles"].ListValue.Values.Select(value => value.StringValue).ToArray();
        var permissions = assignmentFields["permissions"].ListValue.Values.Select(value => value.StringValue).ToArray();

        await Assert.That(assignmentFields["tenantId"].StringValue).IsEqualTo(tenantId.ToString());
        await Assert.That(roles).Contains("event.owner");
        await Assert.That(permissions).Contains(PermissionCodes.EventUpdate);
        await Assert.That(permissions).Contains(PermissionCodes.EventPublish);
        await Assert.That(permissions).Contains(PermissionCodes.EventManageTickets);
    }

    [Test]
    public async Task IsAllowedBatchAsync_ScopedCheck_DefaultsToTenantAttributeWithoutCerbosPolicyScope()
    {
        var userId = _human.UserId;
        var tenantId = Guid.NewGuid().ToString();
        const string eventId = "event-policy-scope-default";

        Cerbos.Api.V1.Request.CheckResourcesRequest? capturedRequest = null;
        var protoResponse = new Cerbos.Api.V1.Response.CheckResourcesResponse();
        protoResponse.Results.Add(CreateResultEntry(eventId, ResourceKinds.Event, AuthorizationActions.Update, Effect.Allow));

        _cerbosClient.CheckResourcesAsync(Arg.Any<CheckResourcesRequest>(), Arg.Any<Metadata>())
            .Returns(call =>
            {
                capturedRequest = call.ArgAt<CheckResourcesRequest>(0).ToCheckResourcesRequest();
                protoResponse.RequestId = capturedRequest.RequestId;
                return new CheckResourcesResponse(protoResponse);
            });

        var service = CreateService();
        var checks = new List<AuthorizationRequest>
        {
            TestAuthorizationRequest.Create(
                ResourceKinds.Event,
                eventId,
                AuthorizationActions.Update,
                new Dictionary<string, object> { ["eventId"] = eventId },
                new AuthorizationScope(TenantId: tenantId))
        };

        var result = await service.IsAllowedBatchAsync(checks);

        await Assert.That(result.Single()).IsTrue();
        await Assert.That(capturedRequest).IsNotNull();
        var resource = capturedRequest!.Resources[0].Resource;
        await Assert.That(resource.Scope).IsEqualTo(string.Empty);
        await Assert.That(resource.Attr["tenantId"].StringValue).IsEqualTo(tenantId);
    }

    [Test]
    public async Task IsAllowedBatchAsync_WhenPolicyScopeEnabled_SendsTenantAsCerbosResourceScope()
    {
        var userId = _human.UserId;
        var tenantId = Guid.NewGuid().ToString();
        const string eventId = "event-policy-scope-enabled";

        Cerbos.Api.V1.Request.CheckResourcesRequest? capturedRequest = null;
        var protoResponse = new Cerbos.Api.V1.Response.CheckResourcesResponse();
        protoResponse.Results.Add(CreateResultEntry(eventId, ResourceKinds.Event, AuthorizationActions.Update, Effect.Allow));

        _cerbosClient.CheckResourcesAsync(Arg.Any<CheckResourcesRequest>(), Arg.Any<Metadata>())
            .Returns(call =>
            {
                capturedRequest = call.ArgAt<CheckResourcesRequest>(0).ToCheckResourcesRequest();
                protoResponse.RequestId = capturedRequest.RequestId;
                return new CheckResourcesResponse(protoResponse);
            });

        var service = CreateService(usePolicyScope: true);
        var checks = new List<AuthorizationRequest>
        {
            TestAuthorizationRequest.Create(
                ResourceKinds.Event,
                eventId,
                AuthorizationActions.Update,
                new Dictionary<string, object> { ["eventId"] = eventId },
                new AuthorizationScope(TenantId: tenantId))
        };

        var result = await service.IsAllowedBatchAsync(checks);

        await Assert.That(result.Single()).IsTrue();
        await Assert.That(capturedRequest).IsNotNull();
        await Assert.That(capturedRequest!.Resources[0].Resource.Scope).IsEqualTo(tenantId);
    }

    [Test]
    public async Task IsAllowedBatchAsync_WithSameResourceIdAcrossKinds_MapsDecisionByKindAndId()
    {
        const string sharedId = "shared-1";
        var protoResponse = new Cerbos.Api.V1.Response.CheckResourcesResponse();
        protoResponse.Results.Add(CreateResultEntry(sharedId, "islamuevent_tenant", "update", Effect.Allow));
        protoResponse.Results.Add(CreateResultEntry(sharedId, "islamuevent_organization", "update", Effect.Deny));

        _cerbosClient.CheckResourcesAsync(Arg.Any<CheckResourcesRequest>(), Arg.Any<Metadata>())
            .Returns(new CheckResourcesResponse(protoResponse));

        var service = CreateService();
        var checks = new List<AuthorizationRequest>
        {
            TestAuthorizationRequest.Create("islamuevent_organization", sharedId, "update", null),
            TestAuthorizationRequest.Create("islamuevent_tenant", sharedId, "update", null)
        };

        var result = await service.IsAllowedBatchAsync(checks);

        await Assert.That(result.Count).IsEqualTo(2);
        await Assert.That(result[0]).IsFalse();
        await Assert.That(result[1]).IsTrue();
    }

    [Test]
    public async Task IsAllowedBatchAsync_GrpcFailure_DeniesAll()
    {
        _cerbosClient.CheckResourcesAsync(Arg.Any<CheckResourcesRequest>(), Arg.Any<Metadata>())
            .ThrowsAsync(new RpcException(new Status(StatusCode.Unavailable, "PDP unreachable")));

        var service = CreateService();
        var checks = new List<AuthorizationRequest>
        {
            TestAuthorizationRequest.Create("islamuevent_organization", "org-1", "update", null),
            TestAuthorizationRequest.Create("islamuevent_tenant_setting", "setting-1", "update", null)
        };

        var result = await service.IsAllowedBatchAsync(checks);

        await Assert.That(result.Count).IsEqualTo(2);
        await Assert.That(result[0]).IsFalse();
        await Assert.That(result[1]).IsFalse();
    }

    [Test]
    public async Task IsAllowedBatchAsync_GrpcFailure_LogsOnlySafeFailureMetadata()
    {
        const string rawEndpoint = "https://tenant-pdp.example.com:443";
        const string exceptionMessage = "PDP unreachable at https://secret-pdp.example.com:443 with token abc123";

        _cerbosClient.CheckResourcesAsync(Arg.Any<CheckResourcesRequest>(), Arg.Any<Metadata>())
            .ThrowsAsync(new RpcException(new Status(StatusCode.Unavailable, exceptionMessage)));

        var service = CreateService(rawEndpoint);
        var result = await service.IsAllowedAsync("islamuevent_organization", "org-1", "update");

        await Assert.That(result).IsFalse();
        _logger.Received(1).Log(
            LogLevel.Error,
            Arg.Any<EventId>(),
            Arg.Is<object>(state => LogStateIsRedacted(state, rawEndpoint, exceptionMessage)),
            Arg.Is<Exception?>(ex => ex == null),
            Arg.Any<Func<object, Exception?, string>>());
    }

    private static bool LogStateIsRedacted(object? state, string rawEndpoint, string exceptionMessage)
    {
        var rendered = state?.ToString();
        return rendered is not null &&
               rendered.Contains("FailureType=RpcException", StringComparison.Ordinal) &&
               !rendered.Contains(rawEndpoint, StringComparison.Ordinal) &&
               !rendered.Contains(exceptionMessage, StringComparison.Ordinal) &&
               !rendered.Contains("secret-pdp", StringComparison.Ordinal) &&
               !rendered.Contains("abc123", StringComparison.Ordinal);
    }

    [Test]
    public async Task CheckSettingAccessAsync_TenantScope_SendsLockAndTenantAttributes()
    {
        var userId = _human.UserId;
        var tenantId = Guid.NewGuid();

        Cerbos.Api.V1.Request.CheckResourcesRequest? capturedRequest = null;

        var protoResponse = new Cerbos.Api.V1.Response.CheckResourcesResponse();
        protoResponse.Results.Add(CreateResultEntry("events.require_approval", "islamuevent_tenant_setting", "update", Effect.Deny));

        _cerbosClient.CheckResourcesAsync(Arg.Any<CheckResourcesRequest>(), Arg.Any<Metadata>())
            .Returns(call =>
            {
                capturedRequest = call.ArgAt<CheckResourcesRequest>(0).ToCheckResourcesRequest();
                protoResponse.RequestId = capturedRequest.RequestId;
                return new CheckResourcesResponse(protoResponse);
            });

        var service = CreateService();
        var decision = await service.AuthorizePreparedHumanAsync(
            CerbosPrincipalBuilder.BuildPrincipalFromSources(ProjectionFacts(userId, tenantId)),
            TestAuthorizationRequest.Create(
                ResourceKinds.TenantSetting,
                "events.require_approval",
                AuthorizationActions.Update,
                new Dictionary<string, object>
                {
                    ["settingKey"] = "events.require_approval",
                    ["tenantId"] = tenantId.ToString("D"),
                    ["isLockedByInstance"] = true
                },
                new AuthorizationScope(TenantId: tenantId.ToString("D"))),
            ambientTenantId: null, usePolicyScope: false, "http://localhost:3593", CancellationToken.None);

        await Assert.That(decision.IsAllowed).IsFalse();
        await Assert.That(decision.Provider.ProviderId).IsEqualTo("cerbos");
        await Assert.That(decision.Provider.ObservedRevision).IsNull();
        await Assert.That(decision.ReasonCode).IsEqualTo(AuthorizationDecisionReasonCodes.Denied);
        await Assert.That(capturedRequest).IsNotNull();

        var resource = capturedRequest!.Resources[0];
        await Assert.That(resource.Resource.Kind).IsEqualTo("islamuevent_tenant_setting");
        await Assert.That(resource.Resource.Id).IsEqualTo("events.require_approval");

        var attrs = resource.Resource.Attr;
        await Assert.That(attrs.ContainsKey("tenantId")).IsTrue();
        await Assert.That(attrs["tenantId"].StringValue).IsEqualTo(tenantId.ToString());
        await Assert.That(attrs.ContainsKey("isLockedByInstance")).IsTrue();
        await Assert.That(attrs["isLockedByInstance"].BoolValue).IsTrue();
    }

    [Test]
    public async Task GenericEventResource_AdminPrincipalCannotBypassFrozenAdapter()
    {
        await _human.GrantInstanceAdminAsync();
        var response = new Cerbos.Api.V1.Response.CheckResourcesResponse();
        response.Results.Add(CreateResultEntry("resource-1", ResourceKinds.EventResource, AuthorizationActions.View, Effect.Allow));
        _cerbosClient.CheckResourcesAsync(Arg.Any<CheckResourcesRequest>(), Arg.Any<Metadata>())
            .Returns(new CheckResourcesResponse(response));

        int commandsBeforeCheck = _human.DatabaseCommands;
        var decision = await CreateService().AuthorizeAsync(TestAuthorizationRequest.Create(
            ResourceKinds.EventResource, "resource-1", AuthorizationActions.View, null));

        await Assert.That(decision.IsAllowed).IsFalse();
        await _cerbosClient.DidNotReceive().CheckResourcesAsync(Arg.Any<CheckResourcesRequest>(), Arg.Any<Metadata>());
        await Assert.That(_human.DatabaseCommands).IsEqualTo(commandsBeforeCheck);
        await Assert.That(_human.Services.GetRequiredService<IHumanCommitScopePreparation>().Subject).IsNull();
    }

    [Test]
    public async Task GenericEventResource_MachinePrincipalCannotBypassFrozenAdapter()
    {
        _machinePrincipalAccessor.SetPrincipal(new ApiKeyPrincipalContext(
            "key-1", Guid.NewGuid(), ExternalApiKeyOwnerType.InstanceAdmin, Guid.NewGuid(), ["*"]));
        var response = new Cerbos.Api.V1.Response.CheckResourcesResponse();
        response.Results.Add(CreateResultEntry("resource-1", ResourceKinds.EventResource, AuthorizationActions.View, Effect.Allow));
        _cerbosClient.CheckResourcesAsync(Arg.Any<CheckResourcesRequest>(), Arg.Any<Metadata>())
            .Returns(new CheckResourcesResponse(response));

        var decision = await CreateService().AuthorizeAsync(TestAuthorizationRequest.Create(
            ResourceKinds.EventResource, "resource-1", AuthorizationActions.View, null));

        await Assert.That(decision.IsAllowed).IsFalse();
        await _cerbosClient.DidNotReceive().CheckResourcesAsync(Arg.Any<CheckResourcesRequest>(), Arg.Any<Metadata>());
    }

    [Test]
    public async Task GenericEventResource_MixedBatchDeniesProtectedKindAndPreservesOtherPositions()
    {
        var response = new Cerbos.Api.V1.Response.CheckResourcesResponse();
        response.Results.Add(CreateResultEntry("org-1", ResourceKinds.Organization, AuthorizationActions.Update, Effect.Allow));
        Cerbos.Api.V1.Request.CheckResourcesRequest? captured = null;
        _cerbosClient.CheckResourcesAsync(Arg.Any<CheckResourcesRequest>(), Arg.Any<Metadata>())
            .Returns(call =>
            {
                captured = call.ArgAt<CheckResourcesRequest>(0).ToCheckResourcesRequest();
                return new CheckResourcesResponse(response);
            });
        var checks = new[]
        {
            TestAuthorizationRequest.Create(ResourceKinds.EventResource, "resource-1", AuthorizationActions.View, null),
            TestAuthorizationRequest.Create(ResourceKinds.Organization, "org-1", AuthorizationActions.Update, null),
            TestAuthorizationRequest.Create(ResourceKinds.EventResource, "resource-2", AuthorizationActions.EventResources.Download, null)
        };

        var decisions = await CreateService().AuthorizeBatchAsync(checks);

        await Assert.That(decisions.Count).IsEqualTo(3);
        await Assert.That(decisions[0].IsAllowed).IsFalse();
        await Assert.That(decisions[1].IsAllowed).IsTrue();
        await Assert.That(decisions[2].IsAllowed).IsFalse();
        await Assert.That(captured).IsNotNull();
        await Assert.That(captured!.Resources.Count).IsEqualTo(1);
        await Assert.That(captured.Resources[0].Resource.Kind).IsEqualTo(ResourceKinds.Organization);
    }

    [Test]
    public async Task GenericEventResource_UnavailableSignalAndExplicitEndpointDenyBeforeProviderIo()
    {
        var check = TestAuthorizationRequest.Create(
            ResourceKinds.EventResource, "resource-1", AuthorizationActions.View, null);
        var service = CreateService();
        int commandsBeforeChecks = _human.DatabaseCommands;

        var unavailableSignal = await service.AuthorizeBatchWithUnavailableSignalAsync([check]);
        var explicitEndpoint = await service.AuthorizeBatchWithEndpointAsync("https://pdp.example.test", [check]);

        await Assert.That(unavailableSignal.Single().IsAllowed).IsFalse();
        await Assert.That(explicitEndpoint.Single().IsAllowed).IsFalse();
        await _cerbosClient.DidNotReceive().CheckResourcesAsync(Arg.Any<CheckResourcesRequest>(), Arg.Any<Metadata>());
        _clientFactory.DidNotReceive().GetOrCreate(Arg.Any<string>());
        await Assert.That(_human.DatabaseCommands).IsEqualTo(commandsBeforeChecks);
        await Assert.That(_human.Services.GetRequiredService<IHumanCommitScopePreparation>().Subject).IsNull();
    }

    [Test]
    public async Task IsAllowedBatchAsync_EmptyChecks_ReturnsEmptyList()
    {
        var service = CreateService();
        var result = await service.IsAllowedBatchAsync([]);
        await Assert.That(result.Count).IsEqualTo(0);
    }

    [Test]
    public async Task ToAttributeValue_ConvertsClrTypes()
    {
        var strVal = CerbosAuthorizationService.ToAttributeValue("hello");
        await Assert.That(strVal).IsNotNull();

        var boolVal = CerbosAuthorizationService.ToAttributeValue(true);
        await Assert.That(boolVal).IsNotNull();

        var nullVal = CerbosAuthorizationService.ToAttributeValue(null);
        await Assert.That(nullVal).IsNotNull();

        var intVal = CerbosAuthorizationService.ToAttributeValue(42);
        await Assert.That(intVal).IsNotNull();
    }

    private async Task<Phase0ScenarioResult> ExecutePhase0ScenarioAsync(string scenarioId)
    {
        var scenario = Phase0Scenarios.Single(item => item.Id == scenarioId);
        Cerbos.Api.V1.Request.CheckResourcesRequest? capturedRequest = null;
        if (scenario.MissingFact == "subject")
        {
            _human.SignOut();
        }
        else
        {
            if (scenario.ProviderOutcome == "failure")
            {
                _cerbosClient.CheckResourcesAsync(Arg.Any<CheckResourcesRequest>(), Arg.Any<Metadata>())
                    .ThrowsAsync(new RpcException(new Status(StatusCode.Unavailable, "PDP unreachable")));
            }
            else
            {
                var protoResponse = new Cerbos.Api.V1.Response.CheckResourcesResponse();
                protoResponse.Results.Add(CreateResultEntry(FixedEventId, ResourceKinds.Event, AuthorizationActions.Update, Effect.Deny));
                _cerbosClient.CheckResourcesAsync(Arg.Any<CheckResourcesRequest>(), Arg.Any<Metadata>())
                    .Returns(call =>
                    {
                        capturedRequest = call.ArgAt<CheckResourcesRequest>(0).ToCheckResourcesRequest();
                        protoResponse.RequestId = capturedRequest.RequestId;
                        return new CheckResourcesResponse(protoResponse);
                    });
            }
        }

        var service = CreateService();
        var check = CreateScenarioCheck(scenario);
        var result = scenario.MissingFact == "subject"
            ? await service.AuthorizeAsync(check)
            : await service.AuthorizePreparedHumanAsync(
                CerbosPrincipalBuilder.BuildPrincipalFromSources(ProjectionFacts(Guid.Parse(FixedUserId))),
                check, ambientTenantId: null, usePolicyScope: false, "http://localhost:3593", CancellationToken.None);
        var grpcCallObserved = await DidObserveGrpcCallAsync();
        await Assert.That(grpcCallObserved).IsEqualTo(scenario.ExpectedGrpcCall);

        if (scenario.MissingFact is "tenantId" or "eventId")
        {
            await Assert.That(capturedRequest).IsNotNull();
            await Assert.That(capturedRequest!.Resources[0].Resource.Attr.ContainsKey(scenario.MissingFact)).IsFalse();
        }
        else if (scenario.Id == "cerbos.provider_deny_current_deny")
        {
            await Assert.That(capturedRequest).IsNotNull();
            await Assert.That(capturedRequest!.Resources[0].Resource.Attr.ContainsKey("tenantId")).IsTrue();
            await Assert.That(capturedRequest.Resources[0].Resource.Attr.ContainsKey("eventId")).IsTrue();
        }

        return new Phase0ScenarioResult(
            Id: scenario.Id,
            MissingFact: scenario.MissingFact,
            ProviderOutcome: scenario.ProviderOutcome,
            ExpectedAllowed: scenario.ExpectedAllowed,
            Allowed: result.IsAllowed,
            ExpectedGrpcCall: scenario.ExpectedGrpcCall,
            GrpcCallObserved: grpcCallObserved);
    }

    [Test]
    [Arguments("allow")]
    [Arguments("missing")]
    [Arguments("duplicate")]
    [Arguments("wrong_request")]
    [Arguments("wrong_resource")]
    [Arguments("unavailable")]
    public async Task PreparedHumanTransportUsesFrozenPrincipalAndRequiresExactResponse(string outcome)
    {
        var principal = CerbosPrincipalBuilder.BuildPrincipalFromSources(ProjectionFacts(Guid.Parse(FixedUserId)));
        Cerbos.Api.V1.Request.CheckResourcesRequest? captured = null;
        _clientFactory.GetOrCreate("http://prepared.pdp.test:3593").Returns(_cerbosClient);
        _cerbosClient.CheckResourcesAsync(Arg.Any<CheckResourcesRequest>(), Arg.Any<Metadata>())
            .Returns(call =>
            {
                principal.ToPrincipal().Attr["isInstanceAdmin"] = new Google.Protobuf.WellKnownTypes.Value { BoolValue = true };
                captured = call.ArgAt<CheckResourcesRequest>(0).ToCheckResourcesRequest();
                if (outcome == "unavailable")
                    throw new RpcException(new Status(StatusCode.Unavailable, "unavailable"));
                var response = new Cerbos.Api.V1.Response.CheckResourcesResponse
                {
                    RequestId = outcome == "wrong_request" ? Guid.NewGuid().ToString() : captured.RequestId
                };
                if (outcome != "missing")
                    response.Results.Add(CreateResultEntry(
                        outcome == "wrong_resource" ? FixedEventId : FixedTenantId,
                        ResourceKinds.Tenant, AuthorizationActions.Delete, Effect.Allow));
                if (outcome == "duplicate")
                    response.Results.Add(response.Results[0].Clone());
                return new CheckResourcesResponse(response);
            });
        // Ambient administrator resolution is intentionally absent: prepared input is the source.
        var check = TestAuthorizationRequest.Create(ResourceKinds.Tenant, FixedTenantId,
            AuthorizationActions.Delete, null, new AuthorizationScope(FixedTenantId),
            new TenantScopedAuthorizationFacts(Guid.Parse(FixedTenantId)));
        var result = await CreateService().AuthorizePreparedHumanAsync(principal, check,
            Guid.Parse(FixedEventId), usePolicyScope: true, "http://prepared.pdp.test:3593",
            CancellationToken.None);
        await Assert.That(result.IsAllowed).IsEqualTo(outcome == "allow");
        await Assert.That(principal.ToPrincipal().Attr["isInstanceAdmin"].BoolValue).IsTrue();
        await Assert.That(captured!.Principal.Attr["isInstanceAdmin"].BoolValue).IsFalse();
        await Assert.That(captured.Resources[0].Resource.Scope).IsEqualTo(FixedTenantId);
        await Assert.That(captured.Resources[0].Resource.PolicyVersion).IsEqualTo(string.Empty);
    }

    private static AuthorizationRequest CreateScenarioCheck(Phase0Scenario scenario)
    {
        var scope = scenario.MissingFact is "tenantId" or "subject"
            ? null
            : new AuthorizationScope(TenantId: FixedTenantId);

        // The resource fact is what the resolver failed to produce, so the check carries tenant authority
        // and nothing about the event itself — the resource id alone never becomes a policy attribute.
        IAuthorizationFacts? facts = scenario.MissingFact == "eventId"
            ? new TenantScopedAuthorizationFacts(Guid.Parse(FixedTenantId))
            : null;

        return TestAuthorizationRequest.Create(
            ResourceKinds.Event,
            FixedEventId,
            AuthorizationActions.Update,
            scenario.MissingFact == "eventId"
                ? null
                : new Dictionary<string, object> { ["eventId"] = FixedEventId },
            scope,
            facts);
    }

    private async Task<bool> DidObserveGrpcCallAsync()
    {
        try
        {
            await _cerbosClient.Received(1).CheckResourcesAsync(Arg.Any<CheckResourcesRequest>(), Arg.Any<Metadata>());
            return true;
        }
        catch (ReceivedCallsException)
        {
            return false;
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Explore.slnx")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root from test output directory.");
    }

    private CerbosAuthorizationService CreateService(
        string grpcEndpoint = "http://localhost:3593",
        bool usePolicyScope = false)
    {
        _human.EnterRequest();
        return new CerbosAuthorizationService(
            _cerbosClient,
            new CerbosPrincipalBuilder(
                _adminContext,
                _machinePrincipalAccessor,
                new EventAuthoritySnapshotService(_human.Context),
                _human.Reader),
            _adminContext,
            _machinePrincipalAccessor,
            _settingsResolver,
            _tenantContext,
            _clientFactory,
            Options.Create(new CerbosSettings
            {
                GrpcEndpoint = grpcEndpoint,
                PlaintextMode = true,
                UsePolicyScope = usePolicyScope
            }),
            _logger);
    }

    // Only SDK input vectors: these are never supplied as a current-human source or commit admission.
    private static HumanPrincipalSourceFacts ProjectionFacts(Guid userId, Guid? adminTenant = null)
    {
        Guid membership = Guid.Parse("77777777-7777-7777-7777-777777777777");
        return new(userId, adminTenant, [], [], [], [],
            adminTenant is { } tenant ? [new(membership, tenant, (int)TenantUserStatusEnum.Active, false)] : [],
            adminTenant is { } target ? [new(Guid.Parse("88888888-8888-8888-8888-888888888888"),
                target, membership, (int)RoleEnum.TenantAdmin, (int)RoleScopeEnum.Tenant, null)] : [],
            [], [], [], [], false, [], [], [], [], null);
    }

    private static Cerbos.Api.V1.Response.CheckResourcesResponse.Types.ResultEntry CreateResultEntry(
        string resourceId, string resourceKind, string action, Effect effect)
    {
        var entry = new Cerbos.Api.V1.Response.CheckResourcesResponse.Types.ResultEntry
        {
            Resource = new Cerbos.Api.V1.Response.CheckResourcesResponse.Types.ResultEntry.Types.Resource
            {
                Id = resourceId,
                Kind = resourceKind
            }
        };
        entry.Actions.Add(action, effect);
        return entry;
    }

    private sealed record Phase0Scenario(
        string Id,
        string MissingFact = "none",
        string ProviderOutcome = "deny",
        bool ExpectedAllowed = false,
        bool ExpectedGrpcCall = true);

    private sealed record Phase0ScenarioResult(
        string Id,
        string MissingFact,
        string ProviderOutcome,
        bool ExpectedAllowed,
        bool Allowed,
        bool ExpectedGrpcCall,
        bool GrpcCallObserved);

    private sealed record Phase0ScenarioArtifact(
        int SchemaVersion,
        string GeneratedFrom,
        string TestMethod,
        Phase0ScenarioResult[] Results,
        string[] Mismatches);
}
