using System.Security.Cryptography;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Infrastructure.Services;
using Explore.Persistence.Database;
using Explore.Persistence.Identity;
using Explore.Persistence.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Event.Api.IntegrationTests.Authentication;

[NotInParallel]
public sealed partial class PlatformApiKeyOriginalOwnerTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task OriginalCredentialExpiryPreventsReleaseAndRollsBackMutation(bool expire)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        Guid key = await SeedAsync(fixture);
        await using var request = new Request(fixture);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        await using var scope = await request.Factory.CreateWriteAsync(fixture.Binding.LocalSubjectId, key, default);
        bool body = false;
        async Task InvokeAsync() => await scope.ExecuteAsync(InstanceApiKeyAction.Revoke, async token =>
        {
            await Assert.That(await scope.RevokeAsync(fixture.Clock.GetUtcNow().UtcDateTime, token)).IsTrue();
            body = true;
            if (expire)
            {
                var lifetime = (await request.Services.GetRequiredService<IHumanAuthenticationLifetimeObserver>()
                    .ObserveAsync(request.Context.User, token))!;
                fixture.Clock.Advance(lifetime.ExpiresAt.Add(lifetime.ClockSkew).AddTicks(1)
                    - fixture.Clock.GetUtcNow());
            }
            return true;
        }, default);
        if (expire) await Assert.ThrowsAsync<UnauthorizedAccessException>(InvokeAsync);
        else await InvokeAsync();
        await Assert.That(body).IsTrue();
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.ExternalApiKeys.Where(row => row.Id == key)
            .Select(row => row.ExternalApiKeyStatusId).SingleAsync()).IsEqualTo(expire
                ? (int)ExternalApiKeyStatusEnum.Active : (int)ExternalApiKeyStatusEnum.Revoked);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => scope.ReadAsync(default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.ExecuteAsync(
            InstanceApiKeyAction.Revoke, _ => Task.FromResult(true), default));
    }

    [Test]
    public async Task PublicLowerScopeCannotBorrowAnotherActionOrAnotherScopeAdmission()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        Guid key = await SeedAsync(fixture);
        await using var request = new Request(fixture);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        await using var first = await request.Factory.CreateWriteAsync(fixture.Binding.LocalSubjectId, key, default);
        await using var second = await request.Factory.CreateWriteAsync(fixture.Binding.LocalSubjectId, key, default);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => first.ReadAsync(default));
        await first.ExecuteAsync(InstanceApiKeyAction.Update, async token =>
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                second.UpdatePolicyAsync("Foreign", "events:read", null, fixture.Clock.GetUtcNow().UtcDateTime, token));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => first.RevokeAsync(
                fixture.Clock.GetUtcNow().UtcDateTime, token));
            return (await first.ReadAsync(token)).Single().Id;
        }, default);
        await using var observer = fixture.Native.CreateDatabase();
        var persisted = await observer.ExternalApiKeys.AsNoTracking().SingleAsync(row => row.Id == key);
        await Assert.That(persisted.Name).IsEqualTo("Original platform owner");
        await Assert.That(persisted.ExternalApiKeyStatusId).IsEqualTo((int)ExternalApiKeyStatusEnum.Active);
    }

    [Test]
    [Arguments("stamp")]
    [Arguments("grant")]
    [Arguments("principal")]
    public async Task OriginalScopeRejectsChangedAuthorityBeforeData(string change)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        Guid key = await SeedAsync(fixture);
        await using var request = new Request(fixture);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        await using var scope = await request.Factory.CreateReadAsync(fixture.Binding.LocalSubjectId, key, default);
        await using (var writer = fixture.Native.CreateDatabase())
        {
            if (change == "stamp")
                await writer.Set<LocalIdentityUser>().Where(row => row.Id == fixture.Binding.LocalSubjectId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.SecurityStamp, Guid.CreateVersion7().ToString("N")));
            if (change == "grant")
                await writer.PlatformUserRoles.Where(row => row.UserId == fixture.Binding.LocalSubjectId).ExecuteDeleteAsync();
        }
        if (change == "principal")
            request.Context.User = new(request.Context.User);
        bool body = false;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => scope.ExecuteAsync(InstanceApiKeyAction.Read, async token =>
        {
            body = true;
            return await scope.ReadAsync(token);
        }, default));
        await Assert.That(body).IsFalse();
    }

    private static async Task<Guid> SeedAsync(LocalIdentityLifecycleHttpFixture fixture)
    {
        await using var db = fixture.Native.CreateDatabase();
        db.PlatformUserRoles.Add(new PlatformUserRole
        {
            Id = Guid.CreateVersion7(),
            UserId = fixture.Binding.LocalSubjectId,
            User = null!,
            RoleId = (int)RoleEnum.Admin,
            Role = null!,
            GrantedAt = fixture.Clock.GetUtcNow().UtcDateTime
        });
        var key = new ExternalApiKey
        {
            Id = Guid.CreateVersion7(),
            TenantId = null,
            Name = "Original platform owner",
            KeyId = Guid.CreateVersion7().ToString("N"),
            SecretHash = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
            Scopes = "events:read",
            OwnerType = ExternalApiKeyOwnerType.InstanceAdmin,
            OwnerId = fixture.Binding.LocalSubjectId,
            ExternalApiKeyStatusId = (int)ExternalApiKeyStatusEnum.Active,
            ExternalApiKeyStatus = null!,
            ExternalApiKeyCreditPeriodId = (int)ExternalApiKeyCreditPeriodEnum.None,
            ExternalApiKeyCreditPeriod = null!
        };
        db.ExternalApiKeys.Add(key);
        await db.SaveChangesAsync();
        return key.Id;
    }

    private sealed class Request : IAsyncDisposable, IServiceProvider
    {
        private readonly AsyncServiceScope scope;
        private readonly LocalIdentityLifecycleHttpFixture fixture;
        private readonly InstanceApiKeyDbContextFactories pools;
        internal IPlatformApiKeyPolicyPreparation? Policy { get; set; }
        internal IdentityDatabaseTopology IdentityTopology { get; set; } = IdentityDatabaseTopology.Colocated;
        internal IServiceProvider Services => scope.ServiceProvider;
        internal DefaultHttpContext Context { get; }
        internal IInstanceApiKeyExecutionScopeFactory Factory { get; }

        internal Request(LocalIdentityLifecycleHttpFixture fixture)
        {
            this.fixture = fixture;
            scope = fixture.Host.Services.CreateAsyncScope();
            Context = new() { RequestServices = Services };
            Services.GetRequiredService<ITenantContextAccessor>().BindExecutionTenant(PlatformDefaults.DefaultTenantId);
            using var db = fixture.Native.CreateDatabase();
            var options = new PrimaryDatabaseConnectionOptions
            {
                Role = PrimaryDatabaseRole.Runtime,
                Provider = PrimaryDatabaseProvider.Sqlite,
                Database = db.Database.GetDbConnection().DataSource
            };
            pools = new(options, options with { Role = PrimaryDatabaseRole.Global },
            [
                options with { Role = PrimaryDatabaseRole.InstanceKeyRead },
                options with { Role = PrimaryDatabaseRole.InstanceKeyWrite },
                options with { Role = PrimaryDatabaseRole.InstanceKeyIssuance },
                options with { Role = PrimaryDatabaseRole.PlatformKeyReport }
            ]);
            Factory = new InstanceApiKeyExecutionScopeFactory(
                Services.GetRequiredService<IUserContext>(), Services.GetRequiredService<IAdminContext>(),
                Services.GetRequiredService<IHttpContextAccessor>(), this);
        }

        public object? GetService(Type type) =>
            type == typeof(InstanceApiKeyDbContextFactories) ? pools :
            type == typeof(PlatformApiKeySourceOptions) ? new PlatformApiKeySourceOptions(IdentityTopology) :
            type == typeof(IPlatformApiKeyPolicyPreparation) ? Policy ?? new PlatformApiKeyPolicyPreparation(
                Services.GetRequiredService<CerbosAuthorizationService>(),
                Options.Create(new AuthorizationProviderDeploymentOptions()),
                Services.GetRequiredService<IOptions<CerbosSettings>>(),
                Services.GetRequiredService<ITenantContext>(), fixture.Clock) :
            Services.GetService(type);

        internal Task AuthenticateAsync(string token)
        {
            Services.GetRequiredService<IHttpContextAccessor>().HttpContext = Context;
            Context.Request.Headers.Authorization = "Bearer " + token;
            return CompleteAuthenticationAsync();
        }

        private async Task CompleteAuthenticationAsync()
        {
            var result = await Context.AuthenticateAsync();
            await Assert.That(result.Succeeded).IsTrue();
            Context.User = result.Principal!;
        }

        public async ValueTask DisposeAsync()
        {
            Services.GetRequiredService<IHttpContextAccessor>().HttpContext = null;
            await scope.DisposeAsync();
        }
    }
}
