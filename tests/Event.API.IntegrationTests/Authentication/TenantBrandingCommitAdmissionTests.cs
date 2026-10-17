using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.TenantSettingsDocuments;
using Explore.Application.Features.TenantSettingsDocuments.Handlers.Commands;
using Explore.Application.Features.TenantSettingsDocuments.Requests.Commands;
using Explore.Application.Models.Common;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Settings.Documents;
using Explore.Infrastructure.Services;
using Explore.Persistence;
using Explore.Persistence.Identity;
using Explore.Persistence.Repositories;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Event.Api.IntegrationTests.Authentication;

[NotInParallel]
public sealed partial class TenantBrandingCommitAdmissionTests
{
    [Test]
    [Arguments("unchanged")]
    [Arguments("grant")]
    [Arguments("credential")]
    [Arguments("session")]
    [Arguments("route")]
    [Arguments("governance")]
    [Arguments("unrelated")]
    public async Task OriginalSourcesRemainRequiredAfterTheRealBrandingBody(string change)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        var seeded = await SeedAsync(fixture);
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var services = request.Services;
        await services.GetRequiredService<IHumanCommitScopePreparation>().PrepareAsync(CancellationToken.None);
        var context = services.GetRequiredService<ExploreDbContext>();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var resolver = new TypedSettingsDocumentResolver(new TenantSettingsDocumentRepository(context), cache);
        await resolver.ResolveTenantDocumentAsync<Explore.Domain.Settings.Documents.Payloads.BrandingSettings>(
            new Explore.Application.Settings.SettingsResolutionContext(PlatformDefaults.DefaultTenantId,
                RequestedDocuments: [SettingsDocumentKeys.Tenant.Branding]), SettingsDocumentKeys.Tenant.Branding);
        bool bodyObserved = false;
        var owner = new AfterBody((ISerializableAttemptOwner)new EfCoreUnitOfWork(context), async token =>
        {
            bodyObserved = true;
            await Assert.That(context.ChangeTracker.Entries<TenantSettingsDocument>()
                .Single().Entity.PayloadJson.Contains("Patched", StringComparison.Ordinal)).IsTrue();
            switch (change)
            {
                case "grant":
                    await context.TenantUserRoleGrants.Where(row => row.Id == seeded.GrantId)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.RevokedAt,
                            fixture.Clock.GetUtcNow().UtcDateTime), token);
                    break;
                case "session":
                    await context.Set<LocalIdentityUser>().Where(row => row.Id == fixture.Binding.LocalSubjectId)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.SecurityStamp,
                            Guid.CreateVersion7().ToString("N")), token);
                    break;
                case "route":
                    await context.SystemSettings.Where(row => row.SettingKey == GovernanceSettingKeys.Security.AuthorizationProvider)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.Value, "\"cerbos\""), token);
                    break;
                case "governance":
                    await context.SystemSettings.Where(row => row.SettingKey == GovernanceSettingKeys.Branding.DisplayName)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsLocked, true), token);
                    break;
                case "unrelated":
                    await context.SystemSettings.Where(row => row.SettingKey == GovernanceSettingKeys.Branding.LogoUrl)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsLocked, true), token);
                    break;
                case "credential":
                    // A later blocking source read must finish before authentic time is sampled.
                    services.GetRequiredService<IHumanActionCommitFinalizer>().Register(async finalToken =>
                    {
                        await context.TenantSettingsDocuments.AsNoTracking().SingleAsync(row =>
                            row.TenantId == PlatformDefaults.DefaultTenantId
                            && row.DocumentKey == SettingsDocumentKeys.Tenant.Branding, finalToken);
                        var lifetime = (await services.GetRequiredService<IHumanAuthenticationLifetimeObserver>()
                            .ObserveAsync(request.Context.User, finalToken))!;
                        fixture.Clock.Advance(lifetime.ExpiresAt.Add(lifetime.ClockSkew).AddTicks(1)
                            - fixture.Clock.GetUtcNow());
                    }, _ => { });
                    break;
            }
        });
        var handler = Handler(services, owner, resolver, Preparation(services, fixture.Clock));
        var execution = handler.ExecuteAsync(Command(seeded.Stamp), CancellationToken.None);
        bool accepted = change is "unchanged" or "unrelated";
        if (accepted)
            await Assert.That((await execution).IsSuccess).IsTrue();
        else if (change == "grant")
            await Assert.That((await execution).IsSuccess).IsFalse();
        else
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => execution);
        await Assert.That(bodyObserved).IsTrue();
        await Assert.That(context.Database.CurrentTransaction).IsNull();

        await using var reader = fixture.Native.CreateDatabase();
        var document = await reader.TenantSettingsDocuments.AsNoTracking().SingleAsync(row =>
            row.TenantId == PlatformDefaults.DefaultTenantId && row.DocumentKey == SettingsDocumentKeys.Tenant.Branding);
        await Assert.That(document.PayloadJson.Contains("Patched", StringComparison.Ordinal)).IsEqualTo(accepted);
        await Assert.That(document.ConcurrencyStamp == seeded.Stamp).IsEqualTo(!accepted);
        await Assert.That(await reader.TenantUserRoleGrants.Where(row => row.Id == seeded.GrantId)
            .Select(row => row.RevokedAt).SingleAsync()).IsNull();
        var cached = await resolver.ResolveTenantDocumentAsync<Explore.Domain.Settings.Documents.Payloads.BrandingSettings>(
            new Explore.Application.Settings.SettingsResolutionContext(PlatformDefaults.DefaultTenantId,
                RequestedDocuments: [SettingsDocumentKeys.Tenant.Branding]), SettingsDocumentKeys.Tenant.Branding);
        await Assert.That(cached!.Payload.DisplayName).IsEqualTo(accepted ? "Patched" : "Original");
        if (accepted)
            await Assert.That(document.UpdatedBy).IsEqualTo(fixture.Binding.LocalSubjectId);
    }

    private static PatchTenantBrandingSettingsDocumentCommand Command(Guid stamp) => new()
    {
        TenantId = PlatformDefaults.DefaultTenantId,
        IsLockedByInstance = false,
        Patch = new PatchTenantBrandingSettingsDocumentDto
        {
            ExpectedConcurrencyStamp = stamp,
            DisplayName = new() { Value = OptionalUpdate<string?>.Set("Patched") }
        }
    };

    private static PatchTenantBrandingSettingsDocumentCommandHandler Handler(IServiceProvider services,
        ISerializableAttemptOwner owner, ITypedSettingsDocumentResolver resolver,
        IHumanTenantBrandingMutationPreparation preparation)
    {
        var context = services.GetRequiredService<ExploreDbContext>();
        var unit = new EfCoreUnitOfWork(context);
        var locks = new RelationalSettingMutationLock(context, unit);
        return new(new TenantSettingsDocumentRepository(context), owner, resolver,
            new TenantBrandingSettingsDocumentLockService(SystemSettingRepository.CreateReadOnly(context, locks), locks),
            services.GetRequiredService<IHumanCommitScopePreparation>(),
            services.GetRequiredService<ITenantContext>(),
            services.GetRequiredService<ITenantAdministratorCommitAuthority>(), preparation,
            services.GetRequiredService<IHumanActionCommitFinalizer>());
    }

    private static HumanTenantBrandingMutationPreparation Preparation(IServiceProvider services, TimeProvider clock) =>
        new(services.GetRequiredService<IHumanCommitScopePreparation>(),
            services.GetRequiredService<IHttpContextAccessor>(),
            services.GetRequiredService<IHumanAuthenticationLifetimeObserver>(),
            services.GetRequiredService<ITenantContext>(), clock,
            services.GetRequiredService<CerbosAuthorizationService>(),
            Options.Create(new AuthorizationProviderDeploymentOptions()),
            services.GetRequiredService<IOptions<CerbosSettings>>(),
            () => services.GetRequiredService<IHumanPrincipalSourceFence>(),
            () => services.GetRequiredService<IHumanAuthorizationRouteSource>(),
            () => services.GetRequiredService<IHumanSupportAccessSourceFactory>(),
            () => services.GetRequiredService<IPolicySourceSetFence>(),
            () => services.GetRequiredService<IHumanActionCommitFinalizer>());

    private static async Task<(Guid GrantId, Guid Stamp)> SeedAsync(LocalIdentityLifecycleHttpFixture fixture)
    {
        await using var seed = fixture.Native.CreateDatabase();
        var member = await seed.TenantUsers.SingleOrDefaultAsync(row =>
            row.TenantId == PlatformDefaults.DefaultTenantId && row.UserId == fixture.Binding.LocalSubjectId);
        if (member is null)
        {
            member = new TenantUser
            {
                Id = Guid.CreateVersion7(),
                TenantId = PlatformDefaults.DefaultTenantId,
                Tenant = null!,
                UserId = fixture.Binding.LocalSubjectId,
                User = null!,
                StatusId = (int)TenantUserStatusEnum.Active,
                JoinedAt = fixture.Clock.GetUtcNow().UtcDateTime
            };
            seed.TenantUsers.Add(member);
        }
        var grant = new TenantUserRoleGrant
        {
            Id = Guid.CreateVersion7(),
            TenantId = member.TenantId,
            Tenant = null!,
            TenantUserId = member.Id,
            TenantUser = member,
            RoleId = (int)RoleEnum.TenantAdmin,
            Role = null!,
            RoleScopeId = (int)RoleScopeEnum.Tenant,
            GrantedAt = fixture.Clock.GetUtcNow().UtcDateTime
        };
        seed.TenantUserRoleGrants.Add(grant);
        foreach (var (key, value) in new[]
        {
            (GovernanceSettingKeys.Deployment.Mode, "\"MultiTenant\""),
            (GovernanceSettingKeys.Security.AuthorizationProvider, "\"local\""),
            (GovernanceSettingKeys.Tenants.WhiteLabelingEnabled, "true"),
            (GovernanceSettingKeys.Branding.DisplayName, "\"\""),
            (GovernanceSettingKeys.Branding.LogoUrl, "\"\"")
        })
        {
            var setting = await seed.SystemSettings.SingleOrDefaultAsync(row => row.SettingKey == key);
            if (setting is null) seed.SystemSettings.Add(new SystemSetting { SettingKey = key, Value = value });
            else { setting.Value = value; setting.IsLocked = false; }
        }
        var document = await seed.TenantSettingsDocuments.SingleOrDefaultAsync(row =>
            row.TenantId == member.TenantId && row.DocumentKey == SettingsDocumentKeys.Tenant.Branding);
        if (document is null)
        {
            document = TenantBrandingSettingsDocumentDefaults.Create(member.TenantId, "Original");
            seed.TenantSettingsDocuments.Add(document);
        }
        else document.UpdatePayload(document.SchemaVersion, document.DefaultsVersion, "{\"displayName\":\"Original\"}");
        await seed.SaveChangesAsync();
        return (grant.Id, document.ConcurrencyStamp);
    }

    private sealed class AfterBody(ISerializableAttemptOwner inner, Func<CancellationToken, Task> afterBody)
        : ISerializableAttemptOwner
    {
        public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken = default) =>
            inner.ExecuteAsync(async token =>
            {
                var result = await operation(token);
                await afterBody(token);
                return result;
            }, cancellationToken);
    }

    private sealed class Request : IAsyncDisposable
    {
        private readonly AsyncServiceScope scope;
        private readonly IHttpContextAccessor accessor;
        internal IServiceProvider Services => scope.ServiceProvider;
        internal DefaultHttpContext Context { get; }

        internal Request(IServiceProvider services)
        {
            scope = services.CreateAsyncScope();
            Context = new DefaultHttpContext { RequestServices = Services };
            accessor = Services.GetRequiredService<IHttpContextAccessor>();
            Services.GetRequiredService<ITenantContextAccessor>().BindExecutionTenant(PlatformDefaults.DefaultTenantId);
        }

        internal Task AuthenticateAsync(string token)
        {
            accessor.HttpContext = Context;
            Context.Request.Headers.Authorization = "Bearer " + token;
            return CompleteAsync();
        }

        private async Task CompleteAsync()
        {
            var result = await Context.AuthenticateAsync();
            await Assert.That(result.Succeeded).IsTrue();
            Context.User = result.Principal!;
        }

        public async ValueTask DisposeAsync()
        {
            accessor.HttpContext = null;
            await scope.DisposeAsync();
        }
    }
}
