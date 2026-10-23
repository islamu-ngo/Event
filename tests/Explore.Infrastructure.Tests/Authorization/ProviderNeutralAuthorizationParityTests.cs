// This suite seeds the Cerbos response on purpose: its subject is the *adapter* — that a typed request
// projects to the right gRPC shape and that a PDP response normalizes to the right decision. It cannot and
// does not establish policy behaviour.
//
// Provider decision parity is established by the two lanes that share
// tests/Shared/Authorization/ProviderNeutralCorpus.cs:
//   - Explore.Infrastructure.Tests/Authorization/LocalProviderParityLaneTests.cs (real Local evaluator)
//   - Event.API.IntegrationTests/Features/CerbosProviderParityLaneTests.cs      (live Cerbos PDP)

using System.Text.Json;
using Cerbos.Api.V1.Effect;
using Cerbos.Sdk;
using Cerbos.Sdk.Builder;
using Cerbos.Sdk.Response;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Domain.Enums;
using Explore.Infrastructure.Services;
using Explore.Infrastructure.Tests.Fixtures;
using Explore.Persistence.Repositories;
using Explore.Persistence.Services;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Explore.Infrastructure.Tests.Authorization;

public class ProviderNeutralAuthorizationParityTests
{
    private const string ArtifactDirectory = ".omo/evidence/authorization-platform-redesign/phase2-task23-parity";
    private const string ReportFileName = "adapter-contract-report.json";
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TenantId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid OtherTenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid EventId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid ActorId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private static readonly Scenario[] Scenarios =
    [
        new("event-view-allowed", "positive", Setup.TenantAdmin, [Request(TenantId, AuthorizationActions.View)], [true], [Effect.Allow]),
        new("event-view-denied", "negative", Setup.StandardUser, [Request(TenantId, AuthorizationActions.View)], [false], [Effect.Deny]),
        new("event-view-wrong-tenant", "wrong-tenant", Setup.TenantAdmin, [Request(OtherTenantId, AuthorizationActions.View)], [false], [Effect.Deny]),
        new("event-view-missing-subject", "missing-subject", Setup.MissingSubject, [Request(TenantId, AuthorizationActions.View, missingSubject: true)], [false], []),
        new("event-view-missing-fact", "missing-fact", Setup.TenantAdmin, [RequestWithoutFacts()], [false], [Effect.Deny]),
        new("event-view-provider-unavailable", "provider-unavailable", Setup.ProviderUnavailable, [Request(TenantId, AuthorizationActions.View)], [false], []),
        new(
            "hal-batch-denied-affordance-suppressed",
            "hal-batch-suppression",
            Setup.HalBatch,
            [Request(TenantId, AuthorizationActions.View), Request(TenantId, AuthorizationActions.Update)],
            [true, false],
            [Effect.Allow, Effect.Deny])
    ];

    [Test]
    public async Task SharedAdapterCorpus_ProvesLocalOutcomesAndCerbosTypedProjection()
    {
        var diagnostics = new List<Diagnostic>();

        foreach (var scenario in Scenarios)
        {
            foreach (var providerName in new[] { "local", "cerbos" })
            {
                await using var human = new CurrentHumanPrincipalFixture(UserId, TenantId);
                if (providerName == "local")
                {
                    await human.InitializeAsync(authenticate: scenario.Setup != Setup.MissingSubject);
                    if (scenario.Setup is Setup.TenantAdmin or Setup.HalBatch)
                        await human.GrantTenantAdminAsync();
                }
                human.EnterRequest();
                var fixture = CreateProvider(providerName, scenario, human);
                var decisions = await fixture.AuthorizeAsync(scenario.Requests);

                await Assert.That(decisions.Count).IsEqualTo(scenario.Expected.Length);

                for (var index = 0; index < decisions.Count; index++)
                {
                    var decision = decisions[index];
                    var expected = providerName == "cerbos" && scenario.CerbosEffects.Length > 0
                        ? scenario.CerbosEffects[index] == Effect.Allow
                        : scenario.Expected[index];
                    await Assert.That(decision.IsAllowed).IsEqualTo(expected);
                    await Assert.That(decision.Provider.ProviderId).IsEqualTo(providerName);
                    diagnostics.Add(new Diagnostic(
                        ScenarioId: scenario.Id,
                        Category: scenario.Category,
                        Capability: Capability(scenario.Requests[index]),
                        Expected: Outcome(expected),
                        Actual: Outcome(decision.IsAllowed),
                        Provider: decision.Provider.ProviderId,
                        Reason: decision.ReasonCode,
                        Revision: decision.Provider.ObservedRevision));
                }

                await AssertTypedProjectionAsync(scenario, providerName, fixture.CapturedRequests);
            }
        }

        var artifact = diagnostics.ToArray();
        var serializedArtifact = JsonSerializer.Serialize(artifact, JsonOptions);
        AssertPrivacySafeReport(serializedArtifact);
        var path = Path.Combine(FindRepositoryRoot(), ArtifactDirectory, ReportFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, serializedArtifact);
    }

    private static ProviderFixture CreateProvider(
        string providerName, Scenario scenario, CurrentHumanPrincipalFixture human)
    {
        var adminContext = human.Admin;
        var machinePrincipalAccessor = human.Machine;
        var eventAuthority = new EventAuthoritySnapshotService(human.Context);
        var organizationMembers = new OrganizationMemberRepository(human.Context);
        var groupMembers = new GroupMemberRepository(human.Context);
        var resolver = Substitute.For<IHierarchicalSettingsResolver>();
        var tenantContext = human.Tenant;

        if (providerName == "local")
        {
            var local = new FallbackAuthorizationService(
                adminContext,
                machinePrincipalAccessor,
                eventAuthority,
                organizationMembers,
                groupMembers,
                resolver,
                tenantContext,
                Substitute.For<ILogger<FallbackAuthorizationService>>());
            if (scenario.Setup == Setup.ProviderUnavailable)
                local.ActivateSafeMode();

            return new ProviderFixture(checks => local.AuthorizeBatchAsync(checks), []);
        }

        var client = Substitute.For<ICerbosClient>();
        List<Cerbos.Api.V1.Request.CheckResourcesRequest> captured = [];
        if (scenario.Setup == Setup.ProviderUnavailable)
        {
            client.CheckResourcesAsync(Arg.Any<CheckResourcesRequest>(), Arg.Any<Metadata>())
                .ThrowsAsync(new RpcException(new Status(StatusCode.Unavailable, "unavailable")));
        }
        else
        {
            client.CheckResourcesAsync(Arg.Any<CheckResourcesRequest>(), Arg.Any<Metadata>())
                .Returns(call =>
                {
                    var sent = call.ArgAt<CheckResourcesRequest>(0).ToCheckResourcesRequest();
                    captured.Add(sent);
                    var resource = sent.Resources.Single();
                    int index = Array.FindIndex(scenario.Requests, request =>
                        request.ResourceKind == resource.Resource.Kind
                        && request.ResourceId == resource.Resource.Id && request.Action == resource.Actions.Single());
                    var response = new Cerbos.Api.V1.Response.CheckResourcesResponse { RequestId = sent.RequestId };
                    response.Results.Add(CreateResult(scenario.Requests[index], scenario.CerbosEffects[index]));

                    return new CheckResourcesResponse(response);
                });
        }

        var clientFactory = Substitute.For<ICerbosClientFactory>();
        clientFactory.GetOrCreate("http://localhost:3593").Returns(client);
        var cerbos = new CerbosAuthorizationService(
            client,
            new CerbosPrincipalBuilder(adminContext, machinePrincipalAccessor, eventAuthority, human.Reader),
            adminContext,
            machinePrincipalAccessor,
            resolver,
            tenantContext,
            clientFactory,
            Options.Create(new CerbosSettings { GrpcEndpoint = "http://localhost:3593", PlaintextMode = true }),
            Substitute.For<ILogger<CerbosAuthorizationService>>());
        return new ProviderFixture(async checks =>
        {
            if (scenario.Setup == Setup.MissingSubject)
                return await cerbos.AuthorizeBatchAsync(checks);

            // Projection vectors are independent inputs, not a native observation or admission.
            Guid memberId = Guid.Parse("55555555-5555-5555-5555-555555555555");
            bool tenantAdmin = scenario.Setup is Setup.TenantAdmin or Setup.HalBatch;
            var sources = new HumanPrincipalSourceFacts(
                UserId, TenantId, [], [], [], [],
                tenantAdmin ? [new(memberId, TenantId, (int)TenantUserStatusEnum.Active, false)] : [],
                tenantAdmin ? [new(Guid.Parse("66666666-6666-6666-6666-666666666666"), TenantId,
                    memberId, (int)RoleEnum.TenantAdmin, (int)RoleScopeEnum.Tenant, null)] : [],
                [], [], [], [], false, [], [], [], [], null);
            var principal = CerbosPrincipalBuilder.BuildPrincipalFromSources(sources);
            var decisions = new List<AuthorizationDecision>();
            foreach (var check in checks)
                decisions.Add(await cerbos.AuthorizePreparedHumanAsync(principal, check, TenantId,
                    usePolicyScope: false, "http://localhost:3593", CancellationToken.None));
            return decisions;
        }, captured);
    }

    private static async Task AssertTypedProjectionAsync(
        Scenario scenario,
        string providerName,
        IReadOnlyList<Cerbos.Api.V1.Request.CheckResourcesRequest> captured)
    {
        if (providerName != "cerbos" || scenario.Setup is Setup.MissingSubject or Setup.ProviderUnavailable)
            return;

        await Assert.That(captured.Count).IsEqualTo(scenario.Requests.Length);
        for (var index = 0; index < scenario.Requests.Length; index++)
        {
            var request = scenario.Requests[index];
            await Assert.That(captured[index].Resources.Count).IsEqualTo(1);
            var resource = captured[index].Resources[0];
            await Assert.That(resource.Resource.Kind).IsEqualTo(request.ResourceKind);
            await Assert.That(resource.Resource.Id).IsEqualTo(request.ResourceId);
            await Assert.That(resource.Actions).Contains(request.Action);

            var attributes = resource.Resource.Attr;
            if (request.Facts is null)
            {
                await Assert.That(attributes.ContainsKey("eventId")).IsFalse();
            }
            else
            {
                await Assert.That(attributes.ContainsKey("tenantId")).IsTrue();
                await Assert.That(attributes.ContainsKey("eventId")).IsTrue();
                await Assert.That(attributes.ContainsKey("actorId")).IsTrue();
            }
        }
    }

    private static AuthorizationRequest Request(Guid tenantId, string action, bool missingSubject = false) =>
        new(
            ResourceKinds.Event,
            EventId.ToString("D"),
            action,
            Facts: new EventAuthorizationFacts(
                tenantId,
                EventId,
                ActorId,
                null,
                null,
                null,
                ActorId,
                null,
                null,
                null,
                "PROVENANCE_TEST",
                UserId),
            Scope: new AuthorizationScope(tenantId.ToString("D")),
            Subject: new AuthorizationSubject(missingSubject ? null : UserId),
            Tenant: new AuthorizationTenant(tenantId));

    private static AuthorizationRequest RequestWithoutFacts() =>
        new(
            ResourceKinds.Event,
            EventId.ToString("D"),
            AuthorizationActions.View,
            Subject: new AuthorizationSubject(UserId),
            Tenant: new AuthorizationTenant(TenantId));

    private static Cerbos.Api.V1.Response.CheckResourcesResponse.Types.ResultEntry CreateResult(
        AuthorizationRequest request,
        Effect effect)
    {
        var entry = new Cerbos.Api.V1.Response.CheckResourcesResponse.Types.ResultEntry
        {
            Resource = new Cerbos.Api.V1.Response.CheckResourcesResponse.Types.ResultEntry.Types.Resource
            {
                Id = request.ResourceId,
                Kind = request.ResourceKind
            }
        };
        entry.Actions.Add(request.Action, effect);
        return entry;
    }

    private static string Capability(AuthorizationRequest request) => $"{request.ResourceKind}:{request.Action}";

    private static string Outcome(bool allowed) => allowed ? "allow" : "deny";

    private static void AssertPrivacySafeReport(string report)
    {
        using var document = JsonDocument.Parse(report);
        var expectedFields = new HashSet<string>(StringComparer.Ordinal)
        {
            "scenarioId", "category", "capability", "expected", "actual", "provider", "reason", "revision"
        };

        if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() == 0)
            throw new InvalidOperationException("Parity report must contain diagnostic rows.");

        foreach (var row in document.RootElement.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object || !expectedFields.SetEquals(row.EnumerateObject().Select(property => property.Name)))
                throw new InvalidOperationException("Adapter report contains fields outside the approved diagnostic schema.");
        }

        var sensitiveValues = new[]
        {
            UserId.ToString("D"), TenantId.ToString("D"), OtherTenantId.ToString("D"), EventId.ToString("D"), ActorId.ToString("D"), "PROVENANCE_TEST"
        };
        if (sensitiveValues.Any(value => report.Contains(value, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Adapter report must not contain subject, tenant, resource, or fact values.");
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Explore.slnx")))
                return directory.FullName;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }

    private enum Setup
    {
        StandardUser,
        TenantAdmin,
        MissingSubject,
        ProviderUnavailable,
        HalBatch
    }

    private sealed record Scenario(
        string Id,
        string Category,
        Setup Setup,
        AuthorizationRequest[] Requests,
        bool[] Expected,
        Effect[] CerbosEffects);

    private sealed record ProviderFixture(
        Func<IReadOnlyList<AuthorizationRequest>, Task<IReadOnlyList<AuthorizationDecision>>> AuthorizeAsync,
        IReadOnlyList<Cerbos.Api.V1.Request.CheckResourcesRequest> CapturedRequests);

    private sealed record Diagnostic(
        string ScenarioId,
        string Category,
        string Capability,
        string Expected,
        string Actual,
        string Provider,
        string Reason,
        string? Revision);
}
