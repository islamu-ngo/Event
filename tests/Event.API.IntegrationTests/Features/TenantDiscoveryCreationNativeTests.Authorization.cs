using System.Net;
using System.Text.Json;
using Cerbos.Api.V1.Effect;
using Cerbos.Sdk;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure.Services;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Security;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Event.Api.IntegrationTests.Features;

public sealed partial class TenantDiscoveryCreationNativeTests
{
    [Test]
    public async Task InstanceCerbosAllowsCreationOnOriginalGlobalCreatorDespiteAmbientTenant()
    {
        await database.ResetAsync();
        var transport = new TenantCreationPolicyTransport("allow");
        var probe = new HumanCreatorGateProbe();
        await using var authentication = await AuthenticatedSettingsFixture.CreateForTenantCreationApiAsync(
            OpenMigrator, database, probe, services =>
            {
                transport.Configure(services);
                services.AddScoped<IEventDiscoveryEnrollmentGate>(provider =>
                {
                    if (provider.GetRequiredService<ITenantContext>().TenantId
                        != PlatformDefaults.DefaultTenantId)
                        throw new InvalidOperationException("The creator regression requires its real ambient tenant.");
                    return new ObservedCreatorEnrollment(provider.GetRequiredService<ExploreDbContext>(),
                        provider.GetRequiredService<ExploreDbContextExecutionLease>(), probe);
                });
            });
        probe.Armed = true;

        _ = await CreateAndVerifyAsync(authentication, Request(assignAdministrator: true));

        await Assert.That(probe.ObservedBeforeEnrollment).IsTrue();
        await Assert.That(probe.ObservedBeforeBegin).IsTrue();
        await Assert.That(probe.ObservedCommit).IsTrue();
        await Assert.That(HumanSecuritySourceGate.TryRequireCurrent(probe.OriginalContext!)).IsFalse();
        await transport.AssertTenantCreationQuestionAsync(authentication.ActorId);
    }

    [Test]
    [Arguments("deny")]
    [Arguments("unavailable")]
    public async Task InstanceCerbosRefusalCannotFallBackToLocalAdministratorOrPersistCreation(string decision)
    {
        await database.ResetAsync();
        var transport = new TenantCreationPolicyTransport(decision);
        await using var authentication = await AuthenticatedSettingsFixture.CreateForTenantCreationApiAsync(
            OpenMigrator, database, configureServices: transport.Configure);
        var tenantsBefore = await ReadTenantsAsync();
        var revisionsBefore = await ReadRevisionsAsync();
        var effectsBefore = await ReadCreationEffectsAsync();

        using var response = await authentication.CreateTenantViaApiAsync(Request(assignAdministrator: true), Token);

        await Assert.That(response.StatusCode).IsEqualTo(decision == "unavailable"
            ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.Forbidden)
            .Because(authentication.TenantCreationFailureDescription);
        await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("application/problem+json");
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        await Assert.That(problem.RootElement.GetProperty("status").GetInt32()).IsEqualTo((int)response.StatusCode);
        if (decision == "unavailable")
            await Assert.That(problem.RootElement.GetProperty("code").GetString()).IsEqualTo("authorization_provider_unavailable");
        await Assert.That(await ReadTenantsAsync()).IsEquivalentTo(tenantsBefore);
        await Assert.That(await ReadRevisionsAsync()).IsEquivalentTo(revisionsBefore);
        await AssertExistingEffectsUnchangedAsync(effectsBefore);
        await transport.AssertTenantCreationQuestionAsync(authentication.ActorId);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CurrentLocalRegularUserOrTenantOnlyAdministratorCannotCreateTenant(bool tenantAdministrator)
    {
        await database.ResetAsync();
        await using var authentication = await AuthenticatedSettingsFixture.CreateForTenantCreationApiAsync(OpenMigrator, database);
        await using (var seed = OpenMigrator())
        {
            // Revoke persisted instance authority without replacing the original authenticated credential.
            await seed.PlatformUserRoles.Where(row => row.UserId == authentication.ActorId).ExecuteDeleteAsync(Token);
            if (tenantAdministrator)
            {
                var member = new TenantUser
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = PlatformDefaults.DefaultTenantId,
                    Tenant = null!,
                    UserId = authentication.ActorId,
                    User = null!,
                    StatusId = (int)TenantUserStatusEnum.Active,
                    JoinedAt = DateTime.UtcNow
                };
                seed.Set<TenantUser>().Add(member);
                seed.Set<TenantUserRoleGrant>().Add(new TenantUserRoleGrant
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = member.TenantId,
                    Tenant = null!,
                    TenantUserId = member.Id,
                    TenantUser = member,
                    RoleId = await seed.Roles.Where(row => row.MasterCode == "tenant.admin"
                        && row.RoleScopeId == (int)RoleScopeEnum.Tenant).Select(row => row.Id).SingleAsync(Token),
                    Role = null!,
                    RoleScopeId = (int)RoleScopeEnum.Tenant,
                    GrantedAt = DateTime.UtcNow
                });
                await seed.SaveChangesAsync(Token);
            }
        }
        var tenantsBefore = await ReadTenantsAsync();
        var revisionsBefore = await ReadRevisionsAsync();
        var effectsBefore = await ReadCreationEffectsAsync();

        using var response = await authentication.CreateTenantViaApiAsync(Request(assignAdministrator: true), Token);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden)
            .Because(authentication.TenantCreationFailureDescription);
        await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("application/problem+json");
        await Assert.That(await ReadTenantsAsync()).IsEquivalentTo(tenantsBefore);
        await Assert.That(await ReadRevisionsAsync()).IsEquivalentTo(revisionsBefore);
        await AssertExistingEffectsUnchangedAsync(effectsBefore);
    }

    /// <summary>Only the external PDP response is controlled; the decorated API and native authority remain real.</summary>
    private sealed class TenantCreationPolicyTransport
    {
        private const string Endpoint = "http://tenant-creation-pdp.invalid:3593";
        private readonly ICerbosClient client = Substitute.For<ICerbosClient>();
        private readonly ICerbosClientFactory factory = Substitute.For<ICerbosClientFactory>();
        private readonly List<Cerbos.Api.V1.Request.CheckResourcesRequest> requests = [];
        private string? selectedEndpoint;

        internal TenantCreationPolicyTransport(string decision)
        {
            factory.GetOrCreate(Arg.Any<string>()).Returns(call =>
            {
                selectedEndpoint = call.Arg<string>();
                return client;
            });
            client.CheckResourcesAsync(Arg.Any<Cerbos.Sdk.Builder.CheckResourcesRequest>(), Arg.Any<Metadata>())
                .Returns(call =>
                {
                    var sent = call.ArgAt<Cerbos.Sdk.Builder.CheckResourcesRequest>(0).ToCheckResourcesRequest().Clone();
                    requests.Add(sent);
                    if (decision == "unavailable")
                        throw new RpcException(new Status(StatusCode.Unavailable, "Test instance PDP unavailable."));
                    var resource = sent.Resources.Single();
                    var result = new Cerbos.Api.V1.Response.CheckResourcesResponse.Types.ResultEntry
                    {
                        Resource = new() { Kind = resource.Resource.Kind, Id = resource.Resource.Id }
                    };
                    result.Actions.Add(resource.Actions.Single(), decision == "allow" ? Effect.Allow : Effect.Deny);
                    var response = new Cerbos.Api.V1.Response.CheckResourcesResponse { RequestId = sent.RequestId };
                    response.Results.Add(result);
                    return new Cerbos.Sdk.Response.CheckResourcesResponse(response);
                });
        }

        internal void Configure(IServiceCollection services)
        {
            services.AddScoped<ICerbosClient>(_ => client);
            services.AddScoped<ICerbosClientFactory>(_ => factory);
            services.Configure<CerbosSettings>(options => options.GrpcEndpoint = Endpoint);
            services.Configure<AuthorizationProviderDeploymentOptions>(options => options.Provider = "cerbos");
        }

        internal async Task AssertTenantCreationQuestionAsync(Guid actorId)
        {
            await Assert.That(requests.Count).IsEqualTo(1);
            var sent = requests.Single();
            var resource = sent.Resources.Single();
            await Assert.That(selectedEndpoint).IsEqualTo(Endpoint);
            await Assert.That(sent.Principal.Id).IsEqualTo(actorId.ToString("D"));
            await Assert.That(resource.Resource.Kind).IsEqualTo("islamuevent_tenant");
            await Assert.That(resource.Resource.Id).IsEqualTo("CreateTenantCommand");
            await Assert.That(resource.Actions.Single()).IsEqualTo("create");
        }
    }
}
