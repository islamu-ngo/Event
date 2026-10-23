using Cerbos.Sdk;
using Event.Api.IntegrationTests.Builders;
using Event.Api.IntegrationTests.Fixtures;
using Event.Api.IntegrationTests.Seeds;
using Explore.Application.Contracts.Services;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence.Identity;
using Explore.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;

namespace Event.Api.IntegrationTests.Features;

public sealed partial class NativeEventPublicActionsHttpTests
{
    private sealed class ProjectionRoomFixture : IAsyncDisposable
    {
        internal LocalIdentityLifecycleHttpFixture Local { get; private set; } = null!;
        private string _credential = null!;
        internal (Guid OwnerId, Guid PublicId, Guid ForeignTenantId, Guid ForeignId) Data { get; private set; }

        internal static async Task<ProjectionRoomFixture> CreateAsync(
            IInterceptor? interceptor = null, ICerbosClient? cerbos = null,
            IdentityDatabaseTopology topology = IdentityDatabaseTopology.Colocated)
        {
            var fixture = new ProjectionRoomFixture();
            try
            {
                fixture.Local = await LocalIdentityLifecycleHttpFixture.CreateAsync(
                    interceptor: interceptor, topology: topology, configureServices: services =>
                    {
                        // Persisted source routing must remain observable rather than
                        // being shadowed by a deployment-level provider override.
                        services.PostConfigure<AuthorizationProviderDeploymentOptions>(options => options.Provider = null);
                        if (cerbos is null) return;
                        services.RemoveAll<ICerbosClient>();
                        services.AddSingleton(cerbos);
                        var clients = Substitute.For<ICerbosClientFactory>();
                        clients.GetOrCreate(Arg.Any<string>()).Returns(cerbos);
                        services.RemoveAll<ICerbosClientFactory>();
                        services.AddSingleton(clients);
                    });
                await using (var seed = fixture.Local.Native.CreateDatabase())
                {
                    Guid userId = fixture.Local.Binding.LocalSubjectId;
                    if (!await seed.TenantUsers.AnyAsync(row =>
                        row.UserId == userId && row.TenantId == PlatformDefaults.DefaultTenantId))
                        seed.TenantUsers.Add(new TenantUser
                        {
                            Id = Guid.CreateVersion7(),
                            TenantId = PlatformDefaults.DefaultTenantId,
                            Tenant = null!,
                            UserId = userId,
                            User = null!,
                            StatusId = (int)TenantUserStatusEnum.Active,
                            JoinedAt = fixture.Local.Clock.GetUtcNow().UtcDateTime
                        });
                    foreach (var (key, value) in new[]
                    {
                        (GovernanceSettingKeys.Deployment.Mode, "\"MultiTenant\""),
                        (GovernanceSettingKeys.Security.AuthorizationProvider, cerbos is null ? "\"local\"" : "\"cerbos\"")
                    })
                    {
                        var setting = await seed.SystemSettings.SingleOrDefaultAsync(row => row.SettingKey == key);
                        if (setting is null) seed.SystemSettings.Add(new SystemSetting { SettingKey = key, Value = value });
                        else setting.Value = value;
                    }
                    (await seed.InstanceBootstrapStates.SingleAsync())
                        .TransitionDeploymentMode(DeploymentMode.MultiTenant);
                    await seed.SaveChangesAsync();
                    var foreign = await TenantScenarioSeed.SeedSecondaryTenantWithUserAsync(seed);
                    Guid actorId = await seed.Actors.Where(row => row.UserId == userId).Select(row => row.Id).SingleAsync();
                    var parent = new EventBuilder().WithActorId(actorId).WithTenantId(PlatformDefaults.DefaultTenantId).Build();
                    var foreignParent = new EventBuilder().WithActorId(foreign.ActorId).WithTenantId(foreign.TenantId).Build();
                    seed.Events.AddRange(parent, foreignParent);
                    await seed.SaveChangesAsync();
                    fixture.Data = (userId, parent.Id, foreign.TenantId, foreignParent.Id);
                }
                var deployment = fixture.Local.Host.Services.GetRequiredService<IDeploymentModeProvider>();
                await deployment.InvalidateCacheAsync();
                await Assert.That(await deployment.GetCurrentModeAsync()).IsEqualTo(DeploymentMode.MultiTenant);
                fixture._credential = await fixture.Local.SignInAsync();
                return fixture;
            }
            catch
            {
                await fixture.DisposeAsync();
                throw;
            }
        }

        // Construction is deliberately synchronous: install the AsyncLocal
        // HttpContext in the caller before authentication has its first await.
        internal IServiceScope CreateScope() => new AdministrativeRequest(Local.Host.Services);
        internal Task AuthenticateAsync(IServiceScope scope) =>
            ((AdministrativeRequest)scope).AuthenticateAsync(_credential);

        public async ValueTask DisposeAsync()
        {
            if (Local is not null) await Local.DisposeAsync();
        }
    }
}
