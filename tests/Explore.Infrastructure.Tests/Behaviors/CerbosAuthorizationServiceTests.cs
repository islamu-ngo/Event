using Explore.Infrastructure.Tests.Authorization;
using Cerbos.Api.V1.Effect;
using Cerbos.Sdk;
using Cerbos.Sdk.Builder;
using Cerbos.Sdk.Response;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Infrastructure.Services;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NSubstitute.Exceptions;
using System.Text.Json;

namespace Explore.Infrastructure.Tests.Behaviors;

public class CerbosAuthorizationServiceTests
{
    private const string ArtifactRelativePath = ".omo/start-work/artifacts/authorization-platform-redesign/phase0-task02/cerbos-provider-scenarios.json";
    private const string FixedUserId = "11111111-1111-1111-1111-111111111111";
    private const string FixedTenantId = "22222222-2222-2222-2222-222222222222";
    private const string FixedEventId = "33333333-3333-3333-3333-333333333333";

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
    private readonly IMachinePrincipalAccessor _machinePrincipalAccessor;
    private readonly IOrganizationMemberRepository _organizationMemberRepository;
    private readonly IGroupMemberRepository _groupMemberRepository;
    private readonly IHierarchicalSettingsResolver _settingsResolver;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<CerbosAuthorizationService> _logger;
    private readonly ICerbosClient _cerbosClient;
    private readonly ICerbosClientFactory _clientFactory;

    public CerbosAuthorizationServiceTests()
    {
        _adminContext = Substitute.For<IAdminContext>();
        _machinePrincipalAccessor = Substitute.For<IMachinePrincipalAccessor>();
        _organizationMemberRepository = Substitute.For<IOrganizationMemberRepository>();
        _groupMemberRepository = Substitute.For<IGroupMemberRepository>();
        _settingsResolver = Substitute.For<IHierarchicalSettingsResolver>();
        _tenantContext = Substitute.For<ITenantContext>();
        _logger = Substitute.For<ILogger<CerbosAuthorizationService>>();
        _cerbosClient = Substitute.For<ICerbosClient>();
        _clientFactory = Substitute.For<ICerbosClientFactory>();

        _machinePrincipalAccessor.IsMachineCaller.Returns(false);
        _machinePrincipalAccessor.Current.Returns((Explore.Application.Authentication.ApiKeyPrincipalContext?)null);
        _organizationMemberRepository.GetOrganizationIdsWhereUserHasPermission(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns([]);
        _groupMemberRepository.GetGroupIdsWhereUserHasPermission(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns([]);
    }

    [Test]
    public async Task IsAllowedBatchAsync_NoUserId_DeniesAllWithoutGrpcCall()
    {
        _adminContext.UserId.Returns((Guid?)null);

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
            var test = new CerbosAuthorizationServiceTests();
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
        var userId = Guid.NewGuid();
        _adminContext.UserId.Returns(userId);
        _adminContext.IsInstanceAdminAsync(Arg.Any<CancellationToken>()).Returns(false);
        _adminContext.GetAdminTenantIdsAsync(Arg.Any<CancellationToken>()).Returns([]);
        _adminContext.GetAdminOrganizationIdsAsync(Arg.Any<CancellationToken>()).Returns([]);

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
        var userId = Guid.NewGuid();
        _adminContext.UserId.Returns(userId);
        _adminContext.IsInstanceAdminAsync(Arg.Any<CancellationToken>()).Returns(false);
        _adminContext.GetAdminTenantIdsAsync(Arg.Any<CancellationToken>()).Returns([]);
        _adminContext.GetAdminOrganizationIdsAsync(Arg.Any<CancellationToken>()).Returns([]);

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
        var resolvedUserId = Guid.NewGuid();
        _adminContext.UserId.Returns((Guid?)null);
        _adminContext.ResolveUserIdAsync(Arg.Any<CancellationToken>()).Returns(resolvedUserId);
        _adminContext.IsInstanceAdminAsync(Arg.Any<CancellationToken>()).Returns(false);
        _adminContext.GetAdminTenantIdsAsync(Arg.Any<CancellationToken>()).Returns([]);
        _adminContext.GetAdminOrganizationIdsAsync(Arg.Any<CancellationToken>()).Returns([]);

        Cerbos.Api.V1.Request.CheckResourcesRequest? capturedRequest = null;
        var protoResponse = new Cerbos.Api.V1.Response.CheckResourcesResponse();
        protoResponse.Results.Add(CreateResultEntry("storage-upload", "islamuevent_storage_object", "create", Effect.Allow));

        _cerbosClient.CheckResourcesAsync(Arg.Any<CheckResourcesRequest>(), Arg.Any<Metadata>())
            .Returns(call =>
            {
                capturedRequest = call.ArgAt<CheckResourcesRequest>(0).ToCheckResourcesRequest();
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
        var userId = Guid.NewGuid();
        _adminContext.UserId.Returns(userId);

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
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var organizerOrganizationId = Guid.NewGuid();

        _adminContext.UserId.Returns(userId);
        _adminContext.IsInstanceAdminAsync(Arg.Any<CancellationToken>()).Returns(false);
        _adminContext.GetAdminTenantIdsAsync(Arg.Any<CancellationToken>()).Returns([]);
        _adminContext.GetAdminOrganizationIdsAsync(Arg.Any<CancellationToken>()).Returns([]);
        _adminContext.GetAdminGroupIdsAsync(Arg.Any<CancellationToken>()).Returns([]);
        _tenantContext.TenantId.Returns(tenantId);
        var eventAuthoritySnapshotService = Substitute.For<IEventAuthoritySnapshotService>();
        eventAuthoritySnapshotService.GetForUserAndEventsAsync(
                tenantId,
                userId,
                Arg.Is<IReadOnlyCollection<Guid>>(ids => ids != null && ids.Contains(eventId)),
                Arg.Any<CancellationToken>())
            .Returns(new EventAuthoritySnapshot(
                tenantId,
                userId,
                new Dictionary<Guid, EventAuthorityForUser>()));

        Cerbos.Api.V1.Request.CheckResourcesRequest? capturedRequest = null;
        var protoResponse = new Cerbos.Api.V1.Response.CheckResourcesResponse();
        protoResponse.Results.Add(CreateResultEntry(eventId.ToString("D"), ResourceKinds.Event, AuthorizationActions.Update, Effect.Allow));

        _cerbosClient.CheckResourcesAsync(Arg.Any<CheckResourcesRequest>(), Arg.Any<Metadata>())
            .Returns(call =>
            {
                capturedRequest = call.ArgAt<CheckResourcesRequest>(0).ToCheckResourcesRequest();
                return new CheckResourcesResponse(protoResponse);
            });

        var service = CreateService(eventAuthoritySnapshotService: eventAuthoritySnapshotService);
        var result = await service.AuthorizeBatchAsync([
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
                    userId))
        ]);

        var decision = result.Single();
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
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var eventCreateOrganizationId = Guid.NewGuid();
        var eventCreateGroupId = Guid.NewGuid();
        var eventAuthoritySnapshotService = Substitute.For<IEventAuthoritySnapshotService>();

        _adminContext.UserId.Returns(userId);
        _adminContext.IsInstanceAdminAsync(Arg.Any<CancellationToken>()).Returns(false);
        _adminContext.GetAdminTenantIdsAsync(Arg.Any<CancellationToken>()).Returns([]);
        _adminContext.GetAdminOrganizationIdsAsync(Arg.Any<CancellationToken>()).Returns([]);
        _adminContext.GetAdminGroupIdsAsync(Arg.Any<CancellationToken>()).Returns([]);
        _tenantContext.TenantId.Returns(tenantId);
        _organizationMemberRepository.GetOrganizationIdsWhereUserHasPermission(
                userId,
                PermissionCodes.EventCreate,
                Arg.Any<CancellationToken>())
            .Returns([eventCreateOrganizationId]);
        _groupMemberRepository.GetGroupIdsWhereUserHasPermission(
                userId,
                PermissionCodes.EventCreate,
                Arg.Any<CancellationToken>())
            .Returns([eventCreateGroupId]);

        eventAuthoritySnapshotService.GetForUserAndEventsAsync(
                tenantId,
                userId,
                Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(eventId)),
                Arg.Any<CancellationToken>())
            .Returns(new EventAuthoritySnapshot(
                tenantId,
                userId,
                new Dictionary<Guid, EventAuthorityForUser>
                {
                    [eventId] = new(
                        new HashSet<string>(StringComparer.Ordinal) { "event.owner" },
                        new HashSet<string>(StringComparer.Ordinal)
                        {
                            PermissionCodes.EventUpdate,
                            PermissionCodes.EventPublish,
                            PermissionCodes.EventManageTickets
                        },
                        IsOwner: true,
                        IsManager: false)
                }));

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
                return new CheckResourcesResponse(protoResponse);
            });

        var service = CreateService(eventAuthoritySnapshotService: eventAuthoritySnapshotService);
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
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid().ToString();
        const string eventId = "event-policy-scope-default";

        _adminContext.UserId.Returns(userId);
        _adminContext.IsInstanceAdminAsync(Arg.Any<CancellationToken>()).Returns(false);
        _adminContext.GetAdminTenantIdsAsync(Arg.Any<CancellationToken>()).Returns([]);
        _adminContext.GetAdminOrganizationIdsAsync(Arg.Any<CancellationToken>()).Returns([]);

        Cerbos.Api.V1.Request.CheckResourcesRequest? capturedRequest = null;
        var protoResponse = new Cerbos.Api.V1.Response.CheckResourcesResponse();
        protoResponse.Results.Add(CreateResultEntry(eventId, ResourceKinds.Event, AuthorizationActions.Update, Effect.Allow));

        _cerbosClient.CheckResourcesAsync(Arg.Any<CheckResourcesRequest>(), Arg.Any<Metadata>())
            .Returns(call =>
            {
                capturedRequest = call.ArgAt<CheckResourcesRequest>(0).ToCheckResourcesRequest();
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
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid().ToString();
        const string eventId = "event-policy-scope-enabled";

        _adminContext.UserId.Returns(userId);
        _adminContext.IsInstanceAdminAsync(Arg.Any<CancellationToken>()).Returns(false);
        _adminContext.GetAdminTenantIdsAsync(Arg.Any<CancellationToken>()).Returns([]);
        _adminContext.GetAdminOrganizationIdsAsync(Arg.Any<CancellationToken>()).Returns([]);

        Cerbos.Api.V1.Request.CheckResourcesRequest? capturedRequest = null;
        var protoResponse = new Cerbos.Api.V1.Response.CheckResourcesResponse();
        protoResponse.Results.Add(CreateResultEntry(eventId, ResourceKinds.Event, AuthorizationActions.Update, Effect.Allow));

        _cerbosClient.CheckResourcesAsync(Arg.Any<CheckResourcesRequest>(), Arg.Any<Metadata>())
            .Returns(call =>
            {
                capturedRequest = call.ArgAt<CheckResourcesRequest>(0).ToCheckResourcesRequest();
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
        var userId = Guid.NewGuid();
        _adminContext.UserId.Returns(userId);
        _adminContext.IsInstanceAdminAsync(Arg.Any<CancellationToken>()).Returns(false);
        _adminContext.GetAdminTenantIdsAsync(Arg.Any<CancellationToken>()).Returns([]);
        _adminContext.GetAdminOrganizationIdsAsync(Arg.Any<CancellationToken>()).Returns([]);

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
        var userId = Guid.NewGuid();
        _adminContext.UserId.Returns(userId);
        _adminContext.IsInstanceAdminAsync(Arg.Any<CancellationToken>()).Returns(false);
        _adminContext.GetAdminTenantIdsAsync(Arg.Any<CancellationToken>()).Returns([]);
        _adminContext.GetAdminOrganizationIdsAsync(Arg.Any<CancellationToken>()).Returns([]);

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
        var userId = Guid.NewGuid();
        const string rawEndpoint = "https://tenant-pdp.example.com:443";
        const string exceptionMessage = "PDP unreachable at https://secret-pdp.example.com:443 with token abc123";

        _adminContext.UserId.Returns(userId);
        _adminContext.IsInstanceAdminAsync(Arg.Any<CancellationToken>()).Returns(false);
        _adminContext.GetAdminTenantIdsAsync(Arg.Any<CancellationToken>()).Returns([]);
        _adminContext.GetAdminOrganizationIdsAsync(Arg.Any<CancellationToken>()).Returns([]);
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

    private static bool LogStateIsRedacted(object state, string rawEndpoint, string exceptionMessage)
    {
        var rendered = state.ToString();
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
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        _adminContext.UserId.Returns(userId);
        _adminContext.IsInstanceAdminAsync(Arg.Any<CancellationToken>()).Returns(false);
        _adminContext.GetAdminTenantIdsAsync(Arg.Any<CancellationToken>()).Returns([tenantId]);
        _adminContext.GetAdminOrganizationIdsAsync(Arg.Any<CancellationToken>()).Returns([]);
        Cerbos.Api.V1.Request.CheckResourcesRequest? capturedRequest = null;

        var protoResponse = new Cerbos.Api.V1.Response.CheckResourcesResponse();
        protoResponse.Results.Add(CreateResultEntry("events.require_approval", "islamuevent_tenant_setting", "update", Effect.Deny));

        _cerbosClient.CheckResourcesAsync(Arg.Any<CheckResourcesRequest>(), Arg.Any<Metadata>())
            .Returns(call =>
            {
                capturedRequest = call.ArgAt<CheckResourcesRequest>(0).ToCheckResourcesRequest();
                return new CheckResourcesResponse(protoResponse);
            });

        var service = CreateService();
        var decision = await service.AuthorizeAsync(TestAuthorizationRequest.Create(
            ResourceKinds.TenantSetting,
            "events.require_approval",
            AuthorizationActions.Update,
            new Dictionary<string, object>
            {
                ["settingKey"] = "events.require_approval",
                ["tenantId"] = tenantId.ToString("D"),
                ["isLockedByInstance"] = true
            },
            new AuthorizationScope(TenantId: tenantId.ToString("D"))));

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
        var eventAuthoritySnapshotService = Substitute.For<IEventAuthoritySnapshotService>();
        eventAuthoritySnapshotService.GetForUserAndEventsAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<IReadOnlyCollection<Guid>>(),
                Arg.Any<CancellationToken>())
            .Returns(call => new EventAuthoritySnapshot(
                call.ArgAt<Guid>(0),
                call.ArgAt<Guid>(1),
                new Dictionary<Guid, EventAuthorityForUser>()));

        if (scenario.MissingFact == "subject")
        {
            _adminContext.UserId.Returns((Guid?)null);
            _adminContext.ResolveUserIdAsync(Arg.Any<CancellationToken>()).Returns((Guid?)null);
        }
        else
        {
            ConfigureAuthenticatedUser();
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
                        return new CheckResourcesResponse(protoResponse);
                    });
            }
        }

        var service = CreateService(eventAuthoritySnapshotService: eventAuthoritySnapshotService);
        var result = await service.IsAllowedBatchAsync([CreateScenarioCheck(scenario)]);
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
            Allowed: result.Single(),
            ExpectedGrpcCall: scenario.ExpectedGrpcCall,
            GrpcCallObserved: grpcCallObserved);
    }

    private void ConfigureAuthenticatedUser()
    {
        var userId = Guid.Parse(FixedUserId);
        _adminContext.UserId.Returns(userId);
        _adminContext.IsInstanceAdminAsync(userId, Arg.Any<CancellationToken>()).Returns(false);
        _adminContext.GetAdminTenantIdsAsync(userId, Arg.Any<CancellationToken>()).Returns([]);
        _adminContext.GetAdminOrganizationIdsAsync(userId, Arg.Any<CancellationToken>()).Returns([]);
        _adminContext.GetAdminGroupIdsAsync(userId, Arg.Any<CancellationToken>()).Returns([]);
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
        IEventAuthoritySnapshotService? eventAuthoritySnapshotService = null,
        bool usePolicyScope = false)
    {
        return new CerbosAuthorizationService(
            _cerbosClient,
            new CerbosPrincipalBuilder(
                _adminContext,
                _machinePrincipalAccessor,
                eventAuthoritySnapshotService ?? Substitute.For<IEventAuthoritySnapshotService>(),
                _organizationMemberRepository,
                _groupMemberRepository),
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
