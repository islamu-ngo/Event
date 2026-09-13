using System.Net;
using System.Net.Http.Json;
using Event.Api.IntegrationTests.Fixtures;
using Event.Api.IntegrationTests.Seeds;
using Explore.API.Controllers;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Application.Features.Modules.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Modules;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel("ApiTestFixture")]
public sealed class NativeModuleHttpTests
{
    private const string ModuleKey = "Mod_NativeCohort";
    private const string InactiveModuleKey = "Mod_NativeInactive";

    [Test]
    public async Task EnableDisableAndReenable_PreserveTenantActorRecordAndCachedDiscovery()
    {
        await using var factory = new AuthenticatedWebApplicationFactory();
        SeedData data = await SeedAsync(factory);
        using HttpClient client = Client(factory, data.UserId);
        await Assert.That(await IsEnabledAsync(client)).IsFalse();

        using (HttpResponseMessage inactive = await client.PostAsync(
            $"/api/module/{InactiveModuleKey}/enable", null))
            await Assert.That(inactive.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        using (HttpResponseMessage missing = await client.PostAsync("/api/module/Mod_Missing/disable", null))
            await Assert.That(missing.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);

        using (HttpResponseMessage enabled = await client.PostAsync(
            $"/api/module/{ModuleKey}/enable?tenantId={data.OtherTenantId:D}&enabledBy={data.OtherUserId:D}", null))
        {
            await Assert.That(enabled.StatusCode).IsEqualTo(HttpStatusCode.OK);
            ModuleActionResponse response = (await enabled.Content.ReadFromJsonAsync<ModuleActionResponse>())!;
            await Assert.That(response.Success).IsTrue();
            await Assert.That(response.ModuleKey).IsEqualTo(ModuleKey);
            await Assert.That(response.Action).IsEqualTo("enabled");
        }
        await Assert.That(await IsEnabledAsync(client)).IsTrue();

        Guid capabilityId;
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            TenantCapability capability = await db.TenantCapabilities.SingleAsync(item => item.ModuleId == data.ModuleId);
            capabilityId = capability.Id;
            await Assert.That(capability.TenantId).IsEqualTo(PlatformDefaults.DefaultTenantId);
            await Assert.That(capability.EnabledBy).IsEqualTo(data.UserId);
            capability.ConfigurationJson = """{"retained":true}""";
            await db.SaveChangesAsync();
        }
        using (HttpResponseMessage duplicate = await client.PostAsync($"/api/module/{ModuleKey}/enable", null))
            await Assert.That(duplicate.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using (HttpResponseMessage disabled = await client.PostAsync($"/api/module/{ModuleKey}/disable", null))
            await Assert.That(disabled.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(await IsEnabledAsync(client)).IsFalse();
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<ITenantCapabilityRepository>();
            TenantCapability? disabled = await repository.GetByTenantAndModuleKey(
                PlatformDefaults.DefaultTenantId, ModuleKey);
            await Assert.That(disabled).IsNotNull();
            await Assert.That(disabled!.Id).IsEqualTo(capabilityId);
            await Assert.That(disabled.IsEnabled).IsFalse();
            await Assert.That(disabled.ConfigurationJson).IsEqualTo("""{"retained":true}""");
        }
        using (HttpResponseMessage reenabled = await client.PostAsync($"/api/module/{ModuleKey}/enable", null))
            await Assert.That(reenabled.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(await IsEnabledAsync(client)).IsTrue();
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            TenantCapability capability = await db.TenantCapabilities.SingleAsync(item => item.ModuleId == data.ModuleId);
            await Assert.That(capability.Id).IsEqualTo(capabilityId);
            await Assert.That(capability.EnabledBy).IsEqualTo(data.UserId);
            await Assert.That(capability.ConfigurationJson).IsEqualTo("""{"retained":true}""");
            await Assert.That(await scope.ServiceProvider.GetRequiredService<ITenantCapabilityRepository>()
                .GetByTenantAndModuleKey(data.OtherTenantId, ModuleKey)).IsNull();
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task DeniedOrUnavailableAuthority_RejectsBothWritesWithoutCreatingCapability(bool unavailable)
    {
        var authorization = Substitute.For<IAuthorizationProvider>();
        authorization.AuthorizeAsync(Arg.Any<AuthorizationRequest>(), Arg.Any<CancellationToken>())
            .Returns(unavailable
                ? AuthorizationDecision.Deny(
                    AuthorizationProviderMetadata.Cerbos, AuthorizationDecisionReasonCodes.ProviderUnavailable)
                : AuthorizationDecision.Deny(AuthorizationProviderMetadata.Local));
        await using var factory = new AuthenticatedWebApplicationFactory
        {
            AuthorizationProviderOverride = authorization
        };
        SeedData data = await SeedAsync(factory);
        using HttpClient anonymous = factory.CreateClient();
        using HttpClient client = Client(factory, data.UserId);
        foreach (string action in new[] { "enable", "disable" })
        {
            using HttpResponseMessage missingAuthentication = await anonymous.PostAsync(
                $"/api/module/{ModuleKey}/{action}", null);
            await Assert.That(missingAuthentication.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
            using HttpResponseMessage rejected = await client.PostAsync($"/api/module/{ModuleKey}/{action}", null);
            await Assert.That(rejected.StatusCode).IsEqualTo(
                unavailable ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.Forbidden);
            await Assert.That(rejected.Content.Headers.ContentType?.MediaType).IsEqualTo("application/problem+json");
        }
        using IServiceScope scope = factory.Services.CreateScope();
        await Assert.That(await scope.ServiceProvider.GetRequiredService<ExploreDbContext>()
            .TenantCapabilities.AnyAsync(item => item.ModuleId == data.ModuleId)).IsFalse();
    }

    [Test]
    public async Task NativePorts_RejectForeignTenantAuthorityBeforeEitherMutation()
    {
        await using var factory = new AuthenticatedWebApplicationFactory
        {
            AuthorizationProviderOverride = new StubAuthorizationProvider
            {
                CheckPredicate = request => request.ResourceKind == ResourceKinds.Tenant
                    && request.Action == AuthorizationActions.Update
                    && request.Facts is TenantScopedAuthorizationFacts facts
                    && facts.TenantId == PlatformDefaults.DefaultTenantId
            }
        };
        SeedData data = await SeedAsync(factory);
        using IServiceScope scope = factory.Services.CreateScope();
        var enable = scope.ServiceProvider.GetRequiredService<
            ICommandHandler<EnableTenantModuleCommand, BaseCommandResponse<Guid>>>();
        var disable = scope.ServiceProvider.GetRequiredService<
            ICommandHandler<DisableTenantModuleCommand, BaseCommandResponse<Guid>>>();
        await Assert.That(async () => await enable.ExecuteAsync(
            new() { TenantId = data.OtherTenantId, ModuleKey = ModuleKey }, default))
            .Throws<AuthorizationException>();
        await Assert.That(async () => await disable.ExecuteAsync(
            new() { TenantId = data.OtherTenantId, ModuleKey = ModuleKey }, default))
            .Throws<AuthorizationException>();
        await Assert.That(await scope.ServiceProvider.GetRequiredService<ITenantCapabilityRepository>()
            .GetByTenantAndModuleKey(data.OtherTenantId, ModuleKey)).IsNull();
    }

    private static async Task<SeedData> SeedAsync(AuthenticatedWebApplicationFactory factory)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var tenant = await TenantScenarioSeed.SeedActiveTenantWithUserAsync(db);
        var other = await TenantScenarioSeed.SeedSecondaryTenantWithUserAsync(db);
        TenantUser membership = await db.TenantUsers.Include(member => member.Tenant)
            .SingleAsync(member => member.UserId == tenant.UserId && member.TenantId == tenant.TenantId);
        db.TenantUserRoleGrants.Add(new TenantUserRoleGrant
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.TenantId,
            Tenant = membership.Tenant,
            TenantUserId = membership.Id,
            TenantUser = membership,
            RoleId = (int)RoleEnum.TenantAdmin,
            Role = await db.Set<Role>().SingleAsync(role => role.Id == (int)RoleEnum.TenantAdmin),
            RoleScopeId = (int)RoleScopeEnum.Tenant
        });
        Guid moduleId = Guid.CreateVersion7();
        db.Set<ModuleDefinition>().AddRange(
            new ModuleDefinition { Id = moduleId, ModuleKey = ModuleKey, Name = "Native module", IsActive = true },
            new ModuleDefinition
            {
                Id = Guid.CreateVersion7(), ModuleKey = InactiveModuleKey, Name = "Inactive module", IsActive = false
            });
        await db.SaveChangesAsync();
        return new SeedData(tenant.UserId, other.TenantId, other.UserId, moduleId);
    }

    private static HttpClient Client(AuthenticatedWebApplicationFactory factory, Guid userId)
    {
        HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.AuthHeaderName,
            TestAuthHandler.CreateTenantAdminHeaderValue(userId, PlatformDefaults.DefaultTenantId));
        return client;
    }

    private static async Task<bool> IsEnabledAsync(HttpClient client)
    {
        using HttpResponseMessage response = await client.GetAsync($"/api/module/{ModuleKey}/enabled");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<ModuleEnabledResponse>())!.IsEnabled;
    }

    private sealed record SeedData(Guid UserId, Guid OtherTenantId, Guid OtherUserId, Guid ModuleId);
}
