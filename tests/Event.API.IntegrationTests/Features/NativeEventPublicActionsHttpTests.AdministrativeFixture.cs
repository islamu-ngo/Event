using Event.Api.IntegrationTests.Builders;
using Event.Api.IntegrationTests.Fixtures;
using Event.Api.IntegrationTests.Seeds;
using Explore.Application.Contracts.Services;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

public sealed partial class NativeEventPublicActionsHttpTests
{
    // Every administrative owner requires a real Local credential and its original
    // request lifetime, including event/session definitions and values.
    private sealed class RetainedAdministrativeFixture : IAsyncDisposable
    {
        private LocalIdentityLifecycleHttpFixture? _local;
        private string? _credential;
        internal LocalIdentityLifecycleHttpFixture Local => _local!;
        internal (Guid OutsiderId, Guid PublicId, Guid ForeignTenantId, Guid OwnerId, Guid OtherId) Data { get; private set; }

        internal static async Task<RetainedAdministrativeFixture> CreateAsync(
            IInterceptor? interceptor = null)
        {
            var fixture = new RetainedAdministrativeFixture();
            try
            {
                fixture._local = await LocalIdentityLifecycleHttpFixture.CreateAsync(interceptor: interceptor);
                var local = fixture._local;
                await using (var seed = local.Native.CreateDatabase())
                {
                    Guid userId = local.Binding.LocalSubjectId;
                    var member = await seed.TenantUsers.SingleOrDefaultAsync(row =>
                        row.TenantId == PlatformDefaults.DefaultTenantId && row.UserId == userId);
                    if (member is null)
                        seed.TenantUsers.Add(new TenantUser
                        {
                            Id = Guid.CreateVersion7(),
                            TenantId = PlatformDefaults.DefaultTenantId,
                            Tenant = null!,
                            UserId = userId,
                            User = null!,
                            StatusId = (int)TenantUserStatusEnum.Active,
                            JoinedAt = local.Clock.GetUtcNow().UtcDateTime
                        });
                    foreach (var (key, value) in new[]
                    {
                        (GovernanceSettingKeys.Deployment.Mode, "\"MultiTenant\""),
                        (GovernanceSettingKeys.Security.AuthorizationProvider, "\"local\"")
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
                    var owner = await TenantScenarioSeed.SeedActiveTenantWithUserAsync(seed);
                    Guid actorId = await seed.Actors.Where(row => row.UserId == userId).Select(row => row.Id).SingleAsync();
                    var parent = new EventBuilder().WithActorId(actorId)
                        .WithTenantId(PlatformDefaults.DefaultTenantId).Build();
                    var other = new EventBuilder().WithActorId(owner.ActorId)
                        .WithTenantId(PlatformDefaults.DefaultTenantId).Build();
                    seed.Events.AddRange(parent, other);
                    await seed.SaveChangesAsync();
                    fixture.Data = (userId, parent.Id, foreign.TenantId, owner.UserId, other.Id);
                }
                var deployment = local.Host.Services.GetRequiredService<IDeploymentModeProvider>();
                await deployment.InvalidateCacheAsync();
                await Assert.That(await deployment.GetCurrentModeAsync()).IsEqualTo(DeploymentMode.MultiTenant);
                fixture._credential = await local.SignInAsync();
                return fixture;
            }
            catch
            {
                await fixture.DisposeAsync();
                throw;
            }
        }

        internal IServiceScope CreateScope()
        {
            return new AdministrativeRequest(_local!.Host.Services);
        }

        internal Task AuthenticateAsync(IServiceScope scope) =>
            scope is AdministrativeRequest request ? request.AuthenticateAsync(_credential!) : Task.CompletedTask;

        public async ValueTask DisposeAsync()
        {
            if (_local is not null) await _local.DisposeAsync();
        }
    }

    private sealed class AdministrativeRequest : IServiceScope
    {
        private readonly IServiceScope _scope;
        private readonly IHttpContextAccessor _accessor;
        private readonly DefaultHttpContext _context;
        public IServiceProvider ServiceProvider => _scope.ServiceProvider;
        internal DefaultHttpContext Context => _context;

        internal AdministrativeRequest(IServiceProvider services)
        {
            _scope = services.CreateScope();
            _accessor = ServiceProvider.GetRequiredService<IHttpContextAccessor>();
            _context = new DefaultHttpContext { RequestServices = ServiceProvider };
            ServiceProvider.GetRequiredService<ITenantContextAccessor>()
                .BindExecutionTenant(PlatformDefaults.DefaultTenantId);
            // Install custody in the caller's execution context before awaiting
            // authentication; a child AsyncLocal assignment cannot flow back.
            _accessor.HttpContext = _context;
        }

        internal async Task AuthenticateAsync(string credential)
        {
            _context.Request.Headers.Authorization = "Bearer " + credential;
            var authenticated = await _context.AuthenticateAsync();
            await Assert.That(authenticated.Succeeded).IsTrue();
            _context.User = authenticated.Principal!;
        }

        public void Dispose()
        {
            _accessor.HttpContext = null;
            _scope.Dispose();
        }
    }
}
