using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Authentication;
using Explore.Application.Constants;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.Authentication.Local.Requests.Queries;
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

public sealed partial class LocalAdministrationCustodyTests
{
    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task ProtectedDisclosureRetainsTheOriginalDeadlineAfterItsLastRead(bool details, bool expire)
    {
        var barrier = new TerminalReadBarrier();
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(
            configureServices: barrier.Decorate);
        await GrantAdministrator(fixture);
        var current = await Current(fixture);
        await using var request = new Request(fixture.Host.Services);
        await request.Authenticate(await fixture.SignInAsync());
        var preparation = request.Services.GetRequiredService<ILocalAdministrationPreparation>();
        var administration = request.Services.GetRequiredService<ILocalHumanCredentialAdministration>();
        object query = details ? new GetLocalCredentialOperationQuery(current.Receipt.OperationId)
            : new ListLocalIdentitiesQuery(1, 100);
        var original = await preparation.PrepareAsync(query, fixture.Binding.LocalSubjectId, default);
        var lifetime = (await request.Services.GetRequiredService<IHumanAuthenticationLifetimeObserver>()
            .ObserveAsync(request.Context.User, default))!;
        async Task Read()
        {
            if (query is GetLocalCredentialOperationQuery)
                await Assert.That(await administration.ReadOperationAsync(original, query, default)).IsNotNull();
            else
                await Assert.That((await administration.ListAsync(original, (ListLocalIdentitiesQuery)query, default))
                    .Items.Any(row => row.LocalSubjectId == fixture.Binding.LocalSubjectId)).IsTrue();
        }
        Task pending = Read();
        try
        {
            await Task.WhenAny(barrier.Read.Task, pending).WaitAsync(TimeSpan.FromSeconds(30));
            if (!barrier.Read.Task.IsCompleted) await pending;
            await barrier.Read.Task.WaitAsync(TimeSpan.FromSeconds(30));
            if (expire) fixture.Clock.Advance(lifetime.ExpiresAt.Add(lifetime.ClockSkew).AddTicks(1)
                - fixture.Clock.GetUtcNow());
        }
        finally { barrier.Release.TrySetResult(); }
        if (expire) await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await pending);
        else await pending;
        await Assert.That(await Current(fixture)).IsEqualTo(current);
    }

    [Test]
    [Arguments("credential")]
    [Arguments("binding")]
    [Arguments("administrator")]
    [Arguments("route")]
    public async Task PreparedDisclosureCannotSubstituteFreshAuthority(string change)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await GrantAdministrator(fixture);
        await using var request = new Request(fixture.Host.Services);
        await request.Authenticate(await fixture.SignInAsync());
        var query = new ListLocalIdentitiesQuery(1, 100);
        var original = await request.Services.GetRequiredService<ILocalAdministrationPreparation>()
            .PrepareAsync(query, fixture.Binding.LocalSubjectId, default);
        await using (var writer = fixture.Native.CreateDatabase())
        {
            switch (change)
            {
                case "credential":
                    await writer.Set<LocalIdentityUser>().Where(row => row.Id == fixture.Binding.LocalSubjectId)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.SecurityStamp,
                            Guid.CreateVersion7().ToString("N")));
                    break;
                case "binding":
                    await writer.UserExternalLogins.Where(row => row.Id == fixture.Binding.ExternalLoginId)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.ProviderKey, Guid.CreateVersion7().ToString("D")));
                    break;
                case "administrator":
                    await writer.PlatformUserRoles.Where(row => row.UserId == fixture.Binding.LocalSubjectId).ExecuteDeleteAsync();
                    writer.PlatformUserRoles.Add(new PlatformUserRole
                    {
                        Id = Guid.CreateVersion7(),
                        UserId = fixture.Binding.LocalSubjectId,
                        User = null!,
                        RoleId = (int)RoleEnum.Admin,
                        Role = null!,
                        GrantedAt = fixture.Clock.GetUtcNow().UtcDateTime
                    });
                    await writer.SaveChangesAsync();
                    break;
                case "route":
                    var setting = await writer.SystemSettings.SingleAsync(row =>
                        row.SettingKey == GovernanceSettingKeys.Security.AuthorizationProvider);
                    setting.Value = "\"cerbos\"";
                    await writer.SaveChangesAsync();
                    break;
            }
        }
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            request.Services.GetRequiredService<ILocalHumanCredentialAdministration>().ListAsync(original, query, default));
    }

    [Test]
    [Arguments("copied")]
    [Arguments("replayed")]
    [Arguments("forged")]
    public async Task DisclosureRequiresThePrivateOriginalRequestAndOneStage(string mode)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await GrantAdministrator(fixture);
        await using var request = new Request(fixture.Host.Services);
        await request.Authenticate(await fixture.SignInAsync());
        var query = new ListLocalIdentitiesQuery(1, 100);
        var original = await request.Services.GetRequiredService<ILocalAdministrationPreparation>()
            .PrepareAsync(query, fixture.Binding.LocalSubjectId, default);
        var administration = request.Services.GetRequiredService<ILocalHumanCredentialAdministration>();
        if (mode == "replayed") await administration.ListAsync(original, query, default);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => administration.ListAsync(
            mode == "forged" ? new ForgedInvocation(original.PrincipalId) : original,
            mode == "copied" ? query with { } : query, default));
    }

    [Test]
    public async Task AnApplicationTransactionCannotPretendToOwnTheLocalLifecycle()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await GrantAdministrator(fixture);
        var before = await Current(fixture);
        await using var request = new Request(fixture.Host.Services);
        await request.Authenticate(await fixture.SignInAsync());
        var query = new ListLocalIdentitiesQuery(1, 100);
        var preparation = request.Services.GetRequiredService<ILocalAdministrationPreparation>();
        var original = await preparation.PrepareAsync(query, fixture.Binding.LocalSubjectId, default);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            request.Services.GetRequiredService<ISerializableAttemptOwner>().ExecuteAsync(async token =>
            {
                await preparation.AcquireAsync(original, query, LocalAdministrationStage.Disclose, token);
                return true;
            }));
        await Assert.That(await Current(fixture)).IsEqualTo(before);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task OriginalSupportCannotBeExtendedIntoDisclosureAuthority(bool extend)
    {
        var barrier = new TerminalReadBarrier();
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(
            configureServices: barrier.Decorate);
        await GrantAdministrator(fixture);
        DateTimeOffset expiry = fixture.Clock.GetUtcNow().AddMinutes(1);
        Guid sessionId;
        await using (var seed = fixture.Native.CreateDatabase())
        {
            foreach (string key in new[] { GovernanceSettingKeys.SupportAccess.Enabled,
                GovernanceSettingKeys.SupportAccess.AllowWriteMode })
            {
                var setting = await seed.SystemSettings.SingleOrDefaultAsync(row => row.SettingKey == key);
                if (setting is null) seed.SystemSettings.Add(new SystemSetting { SettingKey = key, Value = "true" });
                else setting.Value = "true";
            }
            var support = SupportAccessSession.Start(fixture.Binding.LocalSubjectId, PlatformDefaults.DefaultTenantId,
                SupportAccessModeEnum.Write, "maintenance", "Local custody", "local-custody",
                fixture.Clock.GetUtcNow().AddMinutes(-1), expiry);
            sessionId = support.Id;
            seed.SupportAccessSessions.Add(support);
            await seed.SaveChangesAsync();
        }
        await using var request = new Request(fixture.Host.Services);
        request.Context.Request.Headers[SupportAccessHeaderNames.SessionId] = sessionId.ToString("D");
        await request.Authenticate(await fixture.SignInAsync());
        await Assert.That((await request.Services.GetRequiredService<ISupportAccessSessionService>().GetCurrentAsync())
            .IsActive).IsTrue();
        var query = new ListLocalIdentitiesQuery(1, 100);
        var original = await request.Services.GetRequiredService<ILocalAdministrationPreparation>()
            .PrepareAsync(query, fixture.Binding.LocalSubjectId, default);
        if (extend)
        {
            await using var writer = fixture.Native.CreateDatabase();
            await writer.SupportAccessSessions.Where(row => row.Id == sessionId)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.ExpiresAtUtc, expiry.AddMinutes(5)));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                request.Services.GetRequiredService<ILocalHumanCredentialAdministration>().ListAsync(original, query, default));
            return;
        }
        Task<LocalIdentityPage> pending = request.Services.GetRequiredService<ILocalHumanCredentialAdministration>()
            .ListAsync(original, query, default);
        try
        {
            await Task.WhenAny(barrier.Read.Task, pending).WaitAsync(TimeSpan.FromSeconds(30));
            if (!barrier.Read.Task.IsCompleted) await pending;
            await barrier.Read.Task.WaitAsync(TimeSpan.FromSeconds(30));
            fixture.Clock.Advance(expiry.AddTicks(1) - fixture.Clock.GetUtcNow());
        }
        finally { barrier.Release.TrySetResult(); }
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await pending);
    }

    private static async Task<LocalCredentialOperationStatus> Current(LocalIdentityLifecycleHttpFixture fixture)
    {
        await using var scope = fixture.Host.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<ILocalCredentialAdministration>();
        var page = await service.ListAsync(new(1, 100), default);
        Guid id = page.Items.Single(row => row.LocalSubjectId == fixture.Binding.LocalSubjectId).CurrentOperationId!.Value;
        return (await service.ReadOperationAsync(id, default))!;
    }

    private sealed record ForgedInvocation(Guid PrincipalId) : ILocalAdministrationInvocation;

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
            accessor.HttpContext = Context;
            Services.GetRequiredService<ITenantContextAccessor>().BindExecutionTenant(PlatformDefaults.DefaultTenantId);
        }
        internal async Task Authenticate(string token)
        {
            Context.Request.Headers.Authorization = "Bearer " + token;
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

    private sealed class TerminalReadBarrier
    {
        internal TaskCompletionSource Read { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Func<ExploreDbContext, CancellationToken, Task<bool>>? Select { get; init; }
        private int entered;
        internal void Decorate(IServiceCollection services)
        {
            var descriptor = services.Single(row => row.ServiceType == typeof(IHumanActionCommitFinalizer));
            services.Remove(descriptor);
            services.AddScoped<IHumanActionCommitFinalizer>(provider => new Finalizer(
                (IHumanActionCommitFinalizer)(descriptor.ImplementationFactory?.Invoke(provider)
                    ?? ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!)),
                provider.GetRequiredService<ExploreDbContext>(), this));
        }
        private sealed class Finalizer(IHumanActionCommitFinalizer inner, ExploreDbContext context,
            TerminalReadBarrier barrier) : IHumanActionCommitFinalizer
        {
            public void Register(Func<CancellationToken, Task> validateSources, Action<DateTimeOffset> validateAssignments) =>
                inner.Register(async token =>
                {
                    await validateSources(token);
                    if ((barrier.Select is null || await barrier.Select(context, token))
                        && Interlocked.Exchange(ref barrier.entered, 1) == 0)
                    {
                        barrier.Read.TrySetResult();
                        await barrier.Release.Task.WaitAsync(TimeSpan.FromSeconds(30), token);
                    }
                }, validateAssignments);
        }
    }
}
