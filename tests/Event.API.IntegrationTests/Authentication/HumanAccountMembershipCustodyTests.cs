using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.TenantUserRoleGrants.Handlers.Commands;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

[NotInParallel]
public sealed partial class HumanAccountMembershipCustodyTests
{
    [Test]
    [Arguments("self")]
    [Arguments("binding")]
    [Arguments("stamp")]
    [Arguments("other_grant")]
    [Arguments("role")]
    [Arguments("route")]
    [Arguments("credential_after_read")]
    public async Task TenantSelfRevocationCommitsOnlyItsExactOriginalTransition(string change)
    {
        await using var fixture = await CreateFixture();
        fixture.Clock.Advance(TimeSpan.FromTicks(1));
        var (target, other) = await SeedTenantAdministrator(fixture, change == "other_grant");
        var originalIdentity = await fixture.ReadIdentityAsync();
        await using var request = new Request(fixture.Host.Services);
        await request.Authenticate(await fixture.SignInAsync());
        await request.Services.GetRequiredService<IHumanCommitScopePreparation>().PrepareAsync(default);
        var context = request.Services.GetRequiredService<ExploreDbContext>();
        var bodyReached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var owner = new AfterBody((ISerializableAttemptOwner)new EfCoreUnitOfWork(context), async token =>
        {
            await Assert.That(await context.TenantUserRoleGrants.AsNoTracking()
                .Where(row => row.Id == target).Select(row => row.RevokedAt).SingleAsync(token)).IsNotNull();
            bodyReached.TrySetResult();
            await release.Task.WaitAsync(TimeSpan.FromSeconds(30), token);
            switch (change)
            {
                case "binding":
                    await context.UserExternalLogins.Where(row => row.Id == fixture.Binding.ExternalLoginId)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.ProviderKey,
                            Guid.CreateVersion7().ToString("D")), token);
                    break;
                case "stamp":
                    await context.Set<LocalIdentityUser>().Where(row => row.Id == fixture.Binding.LocalSubjectId)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.SecurityStamp,
                            Guid.CreateVersion7().ToString("D")), token);
                    break;
                case "other_grant":
                    await context.PlatformUserRoles.Where(row => row.Id == other).ExecuteDeleteAsync(token);
                    break;
                case "role":
                    await context.Roles.Where(row => row.Id == (int)RoleEnum.TenantAdmin)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.MasterCode, "changed.authority"), token);
                    break;
                case "route":
                    await context.SystemSettings.Where(row => row.SettingKey == GovernanceSettingKeys.Security.AuthorizationProvider)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.Value, "\"cerbos\""), token);
                    break;
                case "credential_after_read":
                    request.Services.GetRequiredService<IHumanActionCommitFinalizer>().Register(async finalToken =>
                    {
                        await context.TenantUserRoleGrants.AsNoTracking().SingleAsync(row => row.Id == target, finalToken);
                        var lifetime = (await request.Services.GetRequiredService<IHumanAuthenticationLifetimeObserver>()
                            .ObserveAsync(request.Context.User, finalToken))!;
                        fixture.Clock.Advance(lifetime.ExpiresAt.Add(lifetime.ClockSkew).AddTicks(1)
                            - fixture.Clock.GetUtcNow());
                    }, _ => { });
                    break;
            }
        });
        var handler = ActivatorUtilities.CreateInstance<RevokeTenantUserRoleGrantCommandHandler>(request.Services, owner);
        Task<bool> operation = handler.ExecuteAsync(new() { Id = target, TenantId = PlatformDefaults.DefaultTenantId }, default);
        try
        {
            await Task.WhenAny(bodyReached.Task, operation).WaitAsync(TimeSpan.FromSeconds(30));
            if (!bodyReached.Task.IsCompleted) await operation;
            await bodyReached.Task.WaitAsync(TimeSpan.FromSeconds(30));
        }
        finally { release.TrySetResult(); }
        if (change == "self") await Assert.That(await operation).IsTrue();
        else await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await operation);
        await using var observer = fixture.Native.CreateDatabase();
        var stored = await observer.TenantUserRoleGrants.AsNoTracking().SingleAsync(row => row.Id == target);
        await Assert.That(stored.RevokedAt is not null).IsEqualTo(change == "self");
        await Assert.That(await observer.PlatformUserRoles.AsNoTracking().AnyAsync(row => row.Id == other))
            .IsEqualTo(change == "other_grant");
        await Assert.That(await observer.UserExternalLogins.AsNoTracking()
            .Where(row => row.Id == fixture.Binding.ExternalLoginId).Select(row => row.ProviderKey).SingleAsync())
            .IsEqualTo(fixture.Binding.LocalSubjectId.ToString("D"));
        await Assert.That((await fixture.ReadIdentityAsync()).SecurityStamp).IsEqualTo(originalIdentity.SecurityStamp);
        if (change == "self")
        {
            await Assert.That(stored.RevokedBy).IsEqualTo(fixture.Binding.LocalSubjectId);
            await Assert.That(stored.RevokedAt!.Value.Ticks % 10).IsEqualTo(0L);
        }
    }

    private static Task<LocalIdentityLifecycleHttpFixture> CreateFixture() =>
        LocalIdentityLifecycleHttpFixture.CreateAsync();

    private static async Task<(Guid Target, Guid Other)> SeedTenantAdministrator(LocalIdentityLifecycleHttpFixture fixture,
        bool platform = false)
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
        Guid target = Guid.CreateVersion7(), other = Guid.CreateVersion7();
        seed.TenantUserRoleGrants.Add(new TenantUserRoleGrant
        {
            Id = target,
            TenantId = member.TenantId,
            Tenant = null!,
            TenantUserId = member.Id,
            TenantUser = member,
            RoleId = (int)RoleEnum.TenantAdmin,
            Role = null!,
            RoleScopeId = (int)RoleScopeEnum.Tenant,
            GrantedAt = fixture.Clock.GetUtcNow().UtcDateTime
        });
        // A separate original contributor makes unrelated drift observable without
        // creating a duplicate active tenant-role grant.
        if (platform) seed.PlatformUserRoles.Add(new PlatformUserRole
        {
            Id = other,
            UserId = fixture.Binding.LocalSubjectId,
            User = null!,
            RoleId = (int)RoleEnum.Admin,
            Role = null!,
            GrantedAt = fixture.Clock.GetUtcNow().UtcDateTime
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
        await seed.SaveChangesAsync();
        return (target, other);
    }

    private sealed class AfterBody(ISerializableAttemptOwner inner, Func<CancellationToken, Task> afterBody)
        : ISerializableAttemptOwner
    {
        public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken = default) => inner.ExecuteAsync(async token =>
        {
            T result = await operation(token);
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

        internal Task Authenticate(string token)
        {
            // Set the AsyncLocal in the caller's execution context before entering
            // async authentication; a child-flow assignment cannot establish its owner.
            accessor.HttpContext = Context;
            Context.Request.Headers.Authorization = "Bearer " + token;
            return AuthenticateOriginalRequest();
        }

        private async Task AuthenticateOriginalRequest()
        {
            var result = await Context.AuthenticateAsync();
            await Assert.That(result.Succeeded).IsTrue();
            Context.User = result.Principal!;
            await Services.GetRequiredService<IHumanCommitScopePreparation>().PrepareAsync(default);
        }

        public async ValueTask DisposeAsync()
        {
            accessor.HttpContext = null;
            await scope.DisposeAsync();
        }
    }
}
