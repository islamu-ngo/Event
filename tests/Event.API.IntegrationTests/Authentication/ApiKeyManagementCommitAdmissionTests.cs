using System.Security.Cryptography;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.ExternalApiKeys.Handlers.Commands;
using Explore.Application.Features.ExternalApiKeys.Handlers.Queries;
using Explore.Application.Features.ExternalApiKeys.Requests.Commands;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Infrastructure.Services;
using Explore.Persistence;
using Explore.Persistence.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Event.Api.IntegrationTests.Authentication;

[NotInParallel]
public sealed partial class ApiKeyManagementCommitAdmissionTests
{
    [Test]
    [Arguments(false, "unchanged")]
    [Arguments(true, "unchanged")]
    [Arguments(false, "stamp")]
    [Arguments(true, "stamp")]
    [Arguments(false, "expiry")]
    [Arguments(true, "expiry")]
    [Arguments(false, "membership")]
    [Arguments(true, "membership")]
    public async Task RealKeyOwnerDoesNotReleaseAfterTerminalAuthorityLoss(bool revoke, string change)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        Guid keyId = await SeedAsync(fixture);
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var services = request.Services;
        await services.GetRequiredService<IHumanCommitScopePreparation>().PrepareAsync(default);
        var context = services.GetRequiredService<ExploreDbContext>();
        bool bodyObserved = false;
        var owner = new AfterBody((ISerializableAttemptOwner)new EfCoreUnitOfWork(context), async token =>
        {
            bodyObserved = true;
            if (change == "stamp")
                await context.Set<LocalIdentityUser>().Where(row => row.Id == fixture.Binding.LocalSubjectId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.SecurityStamp,
                        Guid.CreateVersion7().ToString("N")), token);
            if (change == "membership")
                await context.TenantUsers.Where(row => row.UserId == fixture.Binding.LocalSubjectId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsDeleted, true), token);
            if (change == "expiry")
                services.GetRequiredService<IHumanActionCommitFinalizer>().Register(async finalToken =>
                {
                    await context.ExternalApiKeys.AsNoTracking().SingleAsync(row => row.Id == keyId, finalToken);
                    var lifetime = (await services.GetRequiredService<IHumanAuthenticationLifetimeObserver>()
                        .ObserveAsync(request.Context.User, finalToken))!;
                    fixture.Clock.Advance(lifetime.ExpiresAt.Add(lifetime.ClockSkew).AddTicks(1)
                        - fixture.Clock.GetUtcNow());
                }, _ => { });
        });
        var preparation = Preparation(services, fixture.Clock);
        async Task InvokeAsync()
        {
            if (revoke)
                await Assert.That(await ActivatorUtilities.CreateInstance<RevokeExternalApiKeyCommandHandler>(
                    services, owner, preparation).ExecuteAsync(new(keyId), default)).IsTrue();
            else
                await Assert.That(await ActivatorUtilities.CreateInstance<GetExternalApiKeyDetailsRequestHandler>(
                    services, owner, preparation).QueryAsync(new(keyId), default)).IsNotNull();
        }

        if (change == "unchanged") await InvokeAsync();
        else await Assert.ThrowsAsync<UnauthorizedAccessException>(InvokeAsync);
        await Assert.That(bodyObserved).IsTrue();
        await Assert.That(context.Database.CurrentTransaction).IsNull();
        await using var observer = fixture.Native.CreateDatabase();
        var key = await observer.ExternalApiKeys.AsNoTracking().SingleAsync(row => row.Id == keyId);
        await Assert.That(key.ExternalApiKeyStatusId).IsEqualTo(
            revoke && change == "unchanged"
                ? (int)ExternalApiKeyStatusEnum.Revoked : (int)ExternalApiKeyStatusEnum.Active);
        await Assert.That(await observer.TenantUsers.Where(row => row.UserId == fixture.Binding.LocalSubjectId)
            .Select(row => row.IsDeleted).SingleAsync()).IsFalse();
    }

    [Test]
    public async Task EqualRequestCannotBorrowOriginalCapabilityAndAcquisitionCannotReplay()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        Guid keyId = await SeedAsync(fixture);
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var preparation = Preparation(request.Services, fixture.Clock);
        var command = new RevokeExternalApiKeyCommand(keyId);
        var prepared = (await preparation.PrepareAsync(command, fixture.Binding.LocalSubjectId))!;
        var owner = request.Services.GetRequiredService<ISerializableAttemptOwner>();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => owner.ExecuteAsync(async token =>
        {
            await prepared.AcquireAsync(command with { }, fixture.Binding.LocalSubjectId, token);
            return true;
        }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => owner.ExecuteAsync(async token =>
        {
            await prepared.AcquireAsync(command, fixture.Binding.LocalSubjectId, token);
            return true;
        }));
    }

    [Test]
    public async Task IndependentLocalIdentityCannotSupplyColocatedManagementAuthority()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(
            topology: IdentityDatabaseTopology.External);
        Guid keyId = await SeedAsync(fixture);
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        await Assert.ThrowsAsync<NotSupportedException>(() => Preparation(request.Services, fixture.Clock)
            .PrepareAsync(new RevokeExternalApiKeyCommand(keyId), fixture.Binding.LocalSubjectId));
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.ExternalApiKeys.Where(row => row.Id == keyId)
            .Select(row => row.ExternalApiKeyStatusId).SingleAsync()).IsEqualTo((int)ExternalApiKeyStatusEnum.Active);
    }

    private static HumanApiKeyManagementPreparation Preparation(IServiceProvider services, TimeProvider clock) =>
        new(services.GetRequiredService<IHumanCommitScopePreparation>(),
            services.GetRequiredService<IHttpContextAccessor>(),
            services.GetRequiredService<IHumanAuthenticationLifetimeObserver>(),
            services.GetRequiredService<IMachinePrincipalAccessor>(),
            services.GetRequiredService<ITenantContext>(), clock,
            services.GetRequiredService<CerbosAuthorizationService>(),
            Options.Create(new AuthorizationProviderDeploymentOptions()),
            services.GetRequiredService<IOptions<CerbosSettings>>(),
            () => services.GetRequiredService<IHumanPrincipalSourceFence>(),
            () => services.GetRequiredService<IHumanAuthorizationRouteSource>(),
            () => services.GetRequiredService<IHumanSupportAccessSourceFactory>(),
            () => services.GetRequiredService<IPolicySourceSetFence>(),
            () => services.GetRequiredService<IHumanActionCommitFinalizer>());

    private static async Task<Guid> SeedAsync(LocalIdentityLifecycleHttpFixture fixture)
    {
        await using var context = fixture.Native.CreateDatabase();
        if (!await context.TenantUsers.AnyAsync(row => row.UserId == fixture.Binding.LocalSubjectId))
            context.TenantUsers.Add(new TenantUser
            {
                Id = Guid.CreateVersion7(),
                TenantId = PlatformDefaults.DefaultTenantId,
                Tenant = null!,
                UserId = fixture.Binding.LocalSubjectId,
                User = null!,
                StatusId = (int)TenantUserStatusEnum.Active,
                JoinedAt = fixture.Clock.GetUtcNow().UtcDateTime
            });
        var key = new ExternalApiKey
        {
            Id = Guid.CreateVersion7(),
            TenantId = PlatformDefaults.DefaultTenantId,
            Name = "Source custody",
            KeyId = Guid.CreateVersion7().ToString("N"),
            SecretHash = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
            Scopes = "events:read",
            OwnerType = ExternalApiKeyOwnerType.User,
            OwnerId = fixture.Binding.LocalSubjectId,
            ExternalApiKeyStatusId = (int)ExternalApiKeyStatusEnum.Active,
            ExternalApiKeyStatus = null!,
            ExternalApiKeyCreditPeriodId = (int)ExternalApiKeyCreditPeriodEnum.None,
            ExternalApiKeyCreditPeriod = null!
        };
        context.ExternalApiKeys.Add(key);
        await context.SaveChangesAsync();
        return key.Id;
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
