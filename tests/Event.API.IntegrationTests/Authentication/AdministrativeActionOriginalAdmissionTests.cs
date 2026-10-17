using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Authentication;
using Explore.Application.Constants;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Exceptions;
using Explore.Application.Features.CustomPropertyDefinitions.Handlers.Commands;
using Explore.Application.Features.CustomPropertyDefinitions.Requests.Commands;
using Explore.Application.Features.InstanceAdmin.Handlers.Commands;
using Explore.Application.Features.InstanceAdmin.Plans;
using Explore.Application.Features.InstanceAdmin.Requests.Commands;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Identity;
using Explore.Persistence.QueryFilters;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

[NotInParallel]
public sealed partial class AdministrativeActionOriginalAdmissionTests
{
    [Test]
    [Arguments(false, "unchanged")]
    [Arguments(true, "unchanged")]
    [Arguments(false, "instance")]
    [Arguments(false, "binding")]
    [Arguments(true, "binding")]
    [Arguments(false, "stamp")]
    [Arguments(true, "stamp")]
    [Arguments(false, "grant")]
    [Arguments(true, "grant")]
    [Arguments(false, "credential_after_reads")]
    [Arguments(true, "credential_after_reads")]
    [Arguments(false, "request")]
    [Arguments(true, "request")]
    [Arguments(false, "route")]
    [Arguments(true, "route")]
    [Arguments(true, "support_active")]
    [Arguments(true, "support_revoke")]
    [Arguments(true, "support_extend_after_reads")]
    [Arguments(true, "content")]
    public async Task ActualAdministrativeBodyCommitsOnlyWithItsOriginalTerminalAuthority(bool plan, string change)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        Guid definitionId = Guid.CreateVersion7();
        string planKey = "original-" + Guid.CreateVersion7().ToString("N");
        Guid grantId = Guid.CreateVersion7();
        Guid? supportId = null;
        DateTimeOffset supportExpiry = fixture.Clock.GetUtcNow().AddMinutes(5);
        bool platform = plan || change == "instance" || change.StartsWith("support_", StringComparison.Ordinal);
        await SeedAsync(fixture, definitionId, grantId, platform, change.StartsWith("support_", StringComparison.Ordinal),
            supportExpiry, id => supportId = id);
        var originalIdentity = await fixture.ReadIdentityAsync();
        var quotas = new List<TenantPlanQuotaLimit>();
        var planCommand = new CreateInstanceTenantPlanDraftCommand(Draft(planKey) with { QuotaLimits = quotas });
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var services = request.Services;
        if (supportId is { } forwarded)
        {
            request.Context.Request.Headers[SupportAccessHeaderNames.SessionId] = forwarded.ToString("D");
            await Assert.That((await services.GetRequiredService<ISupportAccessSessionService>().GetCurrentAsync()).IsActive)
                .IsTrue();
        }
        await services.GetRequiredService<IHumanCommitScopePreparation>().PrepareAsync(default);
        var context = services.GetRequiredService<ExploreDbContext>();
        bool realEffectObserved = false;
        var owner = new AfterBody((ISerializableAttemptOwner)new EfCoreUnitOfWork(context), async token =>
        {
            realEffectObserved = plan
                ? await context.TenantPlans.AsNoTracking().AnyAsync(row => row.Key == planKey, token)
                : await context.CustomPropertyDefinitions.IgnoreQueryFilters([QueryFilterNames.SoftDelete])
                    .AsNoTracking().AnyAsync(row => row.Id == definitionId && row.IsDeleted, token);
            await Assert.That(realEffectObserved).IsTrue();
            switch (change)
            {
                case "binding":
                    await context.UserExternalLogins.Where(row => row.Id == fixture.Binding.ExternalLoginId)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.ProviderKey,
                            Guid.CreateVersion7().ToString("N")), token);
                    break;
                case "stamp":
                    await context.Set<LocalIdentityUser>().Where(row => row.Id == fixture.Binding.LocalSubjectId)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.SecurityStamp,
                            Guid.CreateVersion7().ToString("N")), token);
                    break;
                case "grant":
                    if (platform)
                        await context.PlatformUserRoles.Where(row => row.Id == grantId).ExecuteDeleteAsync(token);
                    else
                        await context.TenantUserRoleGrants.Where(row => row.Id == grantId)
                            .ExecuteUpdateAsync(update => update.SetProperty(row => row.RevokedAt,
                                fixture.Clock.GetUtcNow().UtcDateTime), token);
                    break;
                case "request":
                    request.Context.User = new System.Security.Claims.ClaimsPrincipal(request.Context.User);
                    break;
                case "route":
                    await context.SystemSettings.Where(row => row.SettingKey == GovernanceSettingKeys.Security.AuthorizationProvider)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.Value, "\"cerbos\""), token);
                    break;
                case "content":
                    quotas.Add(new(TenantPlanQuotaKeys.StorageBytes, 1024));
                    break;
                case "support_revoke":
                    await context.SupportAccessSessions.Where(row => row.Id == supportId)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.StatusId,
                            (int)SupportAccessSessionStatusEnum.Revoked), token);
                    break;
                case "support_extend_after_reads":
                    await context.SupportAccessSessions.Where(row => row.Id == supportId)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.ExpiresAtUtc,
                            supportExpiry.AddMinutes(5)), token);
                    break;
            }
            if (change is "credential_after_reads" or "support_extend_after_reads")
                services.GetRequiredService<IHumanActionCommitFinalizer>().Register(async finalToken =>
                {
                    // A later, genuine blocking read completes after this action's source
                    // callback. Time admission must still follow every registered reader.
                    if (plan)
                        await context.TenantPlans.AsNoTracking().SingleAsync(row => row.Key == planKey, finalToken);
                    else
                        await context.CustomPropertyDefinitions.IgnoreQueryFilters([QueryFilterNames.SoftDelete])
                            .AsNoTracking().SingleAsync(row => row.Id == definitionId, finalToken);
                    DateTimeOffset deadline = supportExpiry;
                    if (change == "credential_after_reads")
                    {
                        var lifetime = (await services.GetRequiredService<IHumanAuthenticationLifetimeObserver>()
                            .ObserveAsync(request.Context.User, finalToken))!;
                        deadline = lifetime.ExpiresAt.Add(lifetime.ClockSkew).AddTicks(1);
                    }
                    fixture.Clock.Advance(deadline - fixture.Clock.GetUtcNow());
                }, _ => { });
        });
        async Task InvokeAsync()
        {
            if (plan)
            {
                var handler = ActivatorUtilities.CreateInstance<CreateInstanceTenantPlanDraftCommandHandler>(services, owner);
                var result = await handler.ExecuteAsync(planCommand, default);
                await Assert.That(result.IsSuccess).IsTrue();
            }
            else
            {
                var handler = ActivatorUtilities.CreateInstance<DeleteCustomPropertyDefinitionCommandHandler>(services, owner);
                await Assert.That(await handler.ExecuteAsync(new() { Id = definitionId }, default)).IsTrue();
            }
        }
        bool accepted = change is "unchanged" or "instance" or "support_active";
        if (accepted) await InvokeAsync();
        else await Assert.ThrowsAsync<UnauthorizedAccessException>(InvokeAsync);
        await Assert.That(realEffectObserved).IsTrue();
        await Assert.That(context.Database.CurrentTransaction).IsNull();
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.TenantPlans.AsNoTracking().AnyAsync(row => row.Key == planKey))
            .IsEqualTo(plan && accepted);
        await Assert.That(await observer.CustomPropertyDefinitions.IgnoreQueryFilters([QueryFilterNames.SoftDelete])
            .AsNoTracking().Where(row => row.Id == definitionId).Select(row => row.IsDeleted).SingleAsync())
            .IsEqualTo(!plan && accepted);
        await Assert.That(await observer.UserExternalLogins.AsNoTracking()
            .Where(row => row.Id == fixture.Binding.ExternalLoginId).Select(row => row.ProviderKey).SingleAsync())
            .IsEqualTo(fixture.Binding.LocalSubjectId.ToString("D"));
        await Assert.That(await observer.Set<LocalIdentityUser>().AsNoTracking()
            .Where(row => row.Id == fixture.Binding.LocalSubjectId).Select(row => row.SecurityStamp).SingleAsync())
            .IsEqualTo(originalIdentity.SecurityStamp);
        if (platform)
            await Assert.That(await observer.PlatformUserRoles.AsNoTracking().AnyAsync(row => row.Id == grantId)).IsTrue();
        else
            await Assert.That(await observer.TenantUserRoleGrants.AsNoTracking()
                .Where(row => row.Id == grantId).Select(row => row.RevokedAt).SingleAsync()).IsNull();
        if (supportId is { } sessionId)
        {
            var support = await observer.SupportAccessSessions.AsNoTracking().SingleAsync(row => row.Id == sessionId);
            await Assert.That(support.ExpiresAtUtc).IsEqualTo(supportExpiry);
            await Assert.That(support.StatusId).IsEqualTo((int)SupportAccessSessionStatusEnum.Active);
        }
    }

    [Test]
    public async Task DefinitionDeletionPreservesOriginalMissingTenantSupportFactsDenial()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        Guid id = Guid.CreateVersion7();
        Guid supportId = Guid.Empty;
        await SeedAsync(fixture, id, Guid.CreateVersion7(), platform: true, support: true,
            fixture.Clock.GetUtcNow().AddMinutes(5), session => supportId = session);
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        request.Context.Request.Headers[SupportAccessHeaderNames.SessionId] = supportId.ToString("D");
        await Assert.That((await request.Services.GetRequiredService<ISupportAccessSessionService>().GetCurrentAsync()).IsActive)
            .IsTrue();
        await request.Services.GetRequiredService<IHumanCommitScopePreparation>().PrepareAsync(default);
        // The actual annotation has no tenant attributes. Supplying newly invented
        // attributes would change the original question and turn this denial into a grant.
        await Assert.ThrowsAsync<AuthorizationException>(() =>
            ActivatorUtilities.CreateInstance<DeleteCustomPropertyDefinitionCommandHandler>(request.Services)
                .ExecuteAsync(new() { Id = id }, default));
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.CustomPropertyDefinitions.AsNoTracking()
            .AnyAsync(row => row.Id == id && !row.IsDeleted)).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task OriginalAdministrativePredicatesCannotBeBorrowedFromAnotherAction(bool plan)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        Guid definitionId = Guid.CreateVersion7();
        Guid grantId = Guid.CreateVersion7();
        await SeedAsync(fixture, definitionId, grantId, platform: false, support: false,
            fixture.Clock.GetUtcNow().AddMinutes(5), _ => { });
        if (!plan)
        {
            await using var seed = fixture.Native.CreateDatabase();
            await seed.TenantUserRoleGrants.Where(row => row.Id == grantId).ExecuteDeleteAsync();
        }
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        await request.Services.GetRequiredService<IHumanCommitScopePreparation>().PrepareAsync(default);
        string key = "denied-" + Guid.CreateVersion7().ToString("N");
        await Assert.ThrowsAsync<AuthorizationException>(async () =>
        {
            if (plan)
                await ActivatorUtilities.CreateInstance<CreateInstanceTenantPlanDraftCommandHandler>(request.Services)
                    .ExecuteAsync(new(Draft(key)), default);
            else
                await ActivatorUtilities.CreateInstance<DeleteCustomPropertyDefinitionCommandHandler>(request.Services)
                    .ExecuteAsync(new() { Id = definitionId }, default);
        });
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.TenantPlans.AnyAsync(row => row.Key == key)).IsFalse();
        await Assert.That(await observer.CustomPropertyDefinitions.AsNoTracking()
            .AnyAsync(row => row.Id == definitionId && !row.IsDeleted)).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task EqualActionCannotReplaceOriginalRequestOrReplayAcquisition(bool plan)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        Guid id = Guid.CreateVersion7();
        await SeedAsync(fixture, id, Guid.CreateVersion7(), platform: plan, support: false,
            fixture.Clock.GetUtcNow().AddMinutes(5), _ => { });
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        await request.Services.GetRequiredService<IHumanCommitScopePreparation>().PrepareAsync(default);
        var preparation = request.Services.GetRequiredService<IHumanAdministrativeActionPreparation>();
        object command = plan ? new CreateInstanceTenantPlanDraftCommand(Draft("custody"))
            : new DeleteCustomPropertyDefinitionCommand { Id = id };
        var prepared = plan
            ? await preparation.PrepareAsync((CreateInstanceTenantPlanDraftCommand)command, fixture.Binding.LocalSubjectId)
            : await preparation.PrepareAsync((DeleteCustomPropertyDefinitionCommand)command, fixture.Binding.LocalSubjectId);
        object equal = plan ? ((CreateInstanceTenantPlanDraftCommand)command) with { }
            : ((DeleteCustomPropertyDefinitionCommand)command) with { };
        var owner = request.Services.GetRequiredService<ISerializableAttemptOwner>();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => owner.ExecuteAsync(async token =>
        {
            await prepared.AcquireAsync(equal, fixture.Binding.LocalSubjectId, token);
            return true;
        }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => owner.ExecuteAsync(async token =>
        {
            await prepared.AcquireAsync(command, fixture.Binding.LocalSubjectId, token);
            return true;
        }));
    }

    private static TenantPlanDraft Draft(string key) =>
        new(key, "Original administrative draft", new(0, "USD", TenantPlanBillingPeriods.Monthly), true, [], []);

    private static async Task SeedAsync(LocalIdentityLifecycleHttpFixture fixture, Guid definitionId, Guid grantId,
        bool platform, bool support, DateTimeOffset expiresAt, Action<Guid> captureSupport)
    {
        await using var seed = fixture.Native.CreateDatabase();
        seed.CustomPropertyDefinitions.Add(new CustomPropertyDefinition
        {
            Id = definitionId,
            TenantId = PlatformDefaults.DefaultTenantId,
            EntityTypeName = EntityTypeName.Organization,
            Namespace = "tenant",
            Key = "original-" + definitionId.ToString("N"),
            DisplayName = "Original definition",
            PropertyType = PropertyType.Text,
            ExposureLevel = ExposureLevel.Public,
            IsActive = true
        });
        if (platform)
            seed.PlatformUserRoles.Add(new PlatformUserRole
            {
                Id = grantId,
                UserId = fixture.Binding.LocalSubjectId,
                User = null!,
                RoleId = (int)RoleEnum.Admin,
                Role = null!,
                GrantedAt = fixture.Clock.GetUtcNow().UtcDateTime
            });
        else
        {
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
            seed.TenantUserRoleGrants.Add(new TenantUserRoleGrant
            {
                Id = grantId,
                TenantId = member.TenantId,
                Tenant = null!,
                TenantUserId = member.Id,
                TenantUser = member,
                RoleId = (int)RoleEnum.TenantAdmin,
                Role = null!,
                RoleScopeId = (int)RoleScopeEnum.Tenant,
                GrantedAt = fixture.Clock.GetUtcNow().UtcDateTime
            });
        }
        var settings = new List<(string Key, string Value)>
        {
            (GovernanceSettingKeys.Deployment.Mode, "\"MultiTenant\""),
            (GovernanceSettingKeys.Security.AuthorizationProvider, "\"local\"")
        };
        if (support)
        {
            settings.Add((GovernanceSettingKeys.SupportAccess.Enabled, "true"));
            settings.Add((GovernanceSettingKeys.SupportAccess.AllowWriteMode, "true"));
            var session = SupportAccessSession.Start(fixture.Binding.LocalSubjectId, PlatformDefaults.DefaultTenantId,
                SupportAccessModeEnum.Write, "maintenance", "Administrative admission", "fixture-ticket",
                fixture.Clock.GetUtcNow().AddMinutes(-1), expiresAt);
            seed.SupportAccessSessions.Add(session);
            captureSupport(session.Id);
        }
        foreach (var (key, value) in settings)
        {
            var row = await seed.SystemSettings.SingleOrDefaultAsync(setting => setting.SettingKey == key);
            if (row is null) seed.SystemSettings.Add(new SystemSetting { SettingKey = key, Value = value });
            else row.Value = value;
        }
        await seed.SaveChangesAsync();
    }

    private sealed class AfterBody(ISerializableAttemptOwner inner, Func<CancellationToken, Task> afterBody)
        : ISerializableAttemptOwner
    {
        public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken = default) => inner.ExecuteAsync(async token =>
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
            accessor.HttpContext = Context;
        }

        internal async Task AuthenticateAsync(string credential)
        {
            Context.Request.Headers.Authorization = "Bearer " + credential;
            var authenticated = await Context.AuthenticateAsync();
            await Assert.That(authenticated.Succeeded).IsTrue();
            Context.User = authenticated.Principal!;
        }

        public async ValueTask DisposeAsync()
        {
            accessor.HttpContext = null;
            await scope.DisposeAsync();
        }
    }
}
