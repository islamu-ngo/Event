using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Authentication;
using Explore.Application.Constants;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.SupportAccess.Handlers.Commands;
using Explore.Application.Features.SupportAccess.Handlers.Queries;
using Explore.Application.Features.SupportAccess.Requests.Commands;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

[NotInParallel]
public sealed partial class HumanSupportAdministrationCustodyTests
{
    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task EndingOwnForwardedSessionAdmitsOnlyTheIntendedTransition(bool force, bool write)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        Guid sessionId = await Seed(fixture, session: true, write: write);
        await using var request = new Request(fixture.Host.Services, sessionId);
        await request.Authenticate(await fixture.SignInAsync());
        var original = await ReadSession(fixture, sessionId);
        var result = force
            ? await ActivatorUtilities.CreateInstance<ForceStopSupportAccessSessionCommandHandler>(request.Services)
                .ExecuteAsync(new() { SessionId = sessionId, EndReasonText = "  Finished  " }, default)
            : await ActivatorUtilities.CreateInstance<StopSupportAccessSessionCommandHandler>(request.Services)
                .ExecuteAsync(new() { SessionId = sessionId, EndReasonText = "  Finished  " }, default);
        await Assert.That(result.IsSuccess).IsTrue();
        var stored = await ReadSession(fixture, sessionId);
        await Assert.That(stored.StatusId).IsEqualTo((int)(force
            ? SupportAccessSessionStatusEnum.Revoked : SupportAccessSessionStatusEnum.Stopped));
        await Assert.That(stored.EndReasonId).IsEqualTo((int)(force
            ? SupportAccessEndReasonEnum.ForceStopped : SupportAccessEndReasonEnum.UserStopped));
        await Assert.That(stored.EndReasonText).IsEqualTo("Finished");
        await Assert.That(stored.ExpiresAtUtc).IsEqualTo(original.ExpiresAtUtc);
        await Assert.That(stored.TargetTenantId).IsEqualTo(original.TargetTenantId);
        await Assert.That(stored.ActorUserId).IsEqualTo(fixture.Binding.LocalSubjectId);
        await Assert.That(stored.EndedAtUtc!.Value.Ticks % 10).IsEqualTo(0L);
        await using var observer = fixture.Native.CreateDatabase();
        var audit = await observer.SupportAccessAuditEvents.AsNoTracking()
            .SingleAsync(row => row.SupportAccessSessionId == sessionId);
        await Assert.That(audit.ActorUserId).IsEqualTo(fixture.Binding.LocalSubjectId);
        await Assert.That(audit.OccurredAtUtc).IsEqualTo(stored.EndedAtUtc!.Value);
        await Assert.That(audit.Outcome).IsEqualTo(force ? "force_stopped" : "stopped");
    }

    [Test]
    [Arguments("start")]
    [Arguments("stop")]
    [Arguments("force")]
    [Arguments("list")]
    [Arguments("audit")]
    public async Task EveryActualOwnerRejectsExpiryAfterItsLastBlockingRead(string family)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        Guid sessionId = await Seed(fixture, session: family != "start");
        await using var request = new Request(fixture.Host.Services);
        await request.Authenticate(await fixture.SignInAsync());
        var context = request.Services.GetRequiredService<ExploreDbContext>();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var owner = new AfterBody(request.Services.GetRequiredService<ISerializableAttemptOwner>(), _ =>
        {
            request.Services.GetRequiredService<IHumanActionCommitFinalizer>().Register(async token =>
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(TimeSpan.FromSeconds(30), token);
                await context.SupportAccessSessions.AsNoTracking().CountAsync(token);
                var lifetime = (await request.Services.GetRequiredService<IHumanAuthenticationLifetimeObserver>()
                    .ObserveAsync(request.Context.User, token))!;
                fixture.Clock.Advance(lifetime.ExpiresAt.Add(lifetime.ClockSkew).AddTicks(1)
                    - fixture.Clock.GetUtcNow());
            }, _ => { });
            return Task.CompletedTask;
        });
        Task operation = Invoke(request.Services, owner, family, sessionId);
        try { await Reach(entered.Task, operation); }
        finally { release.TrySetResult(); }
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await operation);
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.SupportAccessSessions.AsNoTracking().CountAsync())
            .IsEqualTo(family == "start" ? 0 : 1);
        await Assert.That(await observer.SupportAccessAuditEvents.AsNoTracking().CountAsync()).IsEqualTo(0);
        if (family != "start")
            await Assert.That((await ReadSession(fixture, sessionId)).StatusId)
                .IsEqualTo((int)SupportAccessSessionStatusEnum.Active);
    }

    private static async Task Invoke(IServiceProvider services, ISerializableAttemptOwner owner,
        string family, Guid sessionId)
    {
        switch (family)
        {
            case "start":
                await Assert.That((await ActivatorUtilities.CreateInstance<StartSupportAccessSessionCommandHandler>(services, owner)
                    .ExecuteAsync(StartRequest(), default)).IsSuccess).IsTrue();
                break;
            case "stop":
                await Assert.That((await ActivatorUtilities.CreateInstance<StopSupportAccessSessionCommandHandler>(services, owner)
                    .ExecuteAsync(new() { SessionId = sessionId, EndReasonText = "Finished" }, default)).IsSuccess).IsTrue();
                break;
            case "force":
                await Assert.That((await ActivatorUtilities.CreateInstance<ForceStopSupportAccessSessionCommandHandler>(services, owner)
                    .ExecuteAsync(new() { SessionId = sessionId, EndReasonText = "Finished" }, default)).IsSuccess).IsTrue();
                break;
            case "list":
                await ActivatorUtilities.CreateInstance<ListSupportAccessSessionsQueryHandler>(services, owner)
                    .QueryAsync(new() { TargetTenantId = PlatformDefaults.DefaultTenantId }, default);
                break;
            case "audit":
                await ActivatorUtilities.CreateInstance<GetSupportAccessAuditEventsQueryHandler>(services, owner)
                    .QueryAsync(new() { TargetTenantId = PlatformDefaults.DefaultTenantId, SessionId = sessionId }, default);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(family));
        }
    }

    private static StartSupportAccessSessionCommand StartRequest() => new()
    {
        TargetTenantId = PlatformDefaults.DefaultTenantId,
        Mode = SupportAccessModeEnum.ReadOnly,
        DurationMinutes = 5,
        ReasonCode = "maintenance",
        ReasonText = "Support custody",
        TicketReference = "support-custody"
    };

    private static async Task<Guid> Seed(LocalIdentityLifecycleHttpFixture fixture, bool session,
        bool write = false, bool administrator = true)
    {
        await using var seed = fixture.Native.CreateDatabase();
        if (administrator) seed.PlatformUserRoles.Add(new PlatformUserRole
        {
            Id = Guid.CreateVersion7(),
            UserId = fixture.Binding.LocalSubjectId,
            User = null!,
            RoleId = (int)RoleEnum.Admin,
            Role = null!,
            GrantedAt = fixture.Clock.GetUtcNow().UtcDateTime
        });
        foreach (var (key, value) in new[]
        {
            (GovernanceSettingKeys.Deployment.Mode, "\"MultiTenant\""),
            (GovernanceSettingKeys.Security.AuthorizationProvider, "\"local\""),
            (GovernanceSettingKeys.SupportAccess.Enabled, "true"),
            (GovernanceSettingKeys.SupportAccess.AllowWriteMode, "true")
        })
        {
            var setting = await seed.SystemSettings.SingleOrDefaultAsync(row => row.SettingKey == key);
            if (setting is null) seed.SystemSettings.Add(new SystemSetting { SettingKey = key, Value = value });
            else setting.Value = value;
        }
        Guid id = Guid.Empty;
        if (session)
        {
            var row = SupportAccessSession.Start(fixture.Binding.LocalSubjectId, PlatformDefaults.DefaultTenantId,
                write ? SupportAccessModeEnum.Write : SupportAccessModeEnum.ReadOnly,
                "maintenance", "Support custody", "support-custody",
                fixture.Clock.GetUtcNow().AddMinutes(-1), fixture.Clock.GetUtcNow().AddMinutes(5));
            id = row.Id;
            seed.SupportAccessSessions.Add(row);
        }
        await seed.SaveChangesAsync();
        return id;
    }

    private static async Task<SupportAccessSession> ReadSession(LocalIdentityLifecycleHttpFixture fixture, Guid id)
    {
        await using var observer = fixture.Native.CreateDatabase();
        return await observer.SupportAccessSessions.AsNoTracking().SingleAsync(row => row.Id == id);
    }

    private static async Task Reach(Task entered, Task operation)
    {
        await Task.WhenAny(entered, operation).WaitAsync(TimeSpan.FromSeconds(30));
        if (!entered.IsCompleted) await operation;
        await entered.WaitAsync(TimeSpan.FromSeconds(30));
    }

    private sealed class AfterBody(ISerializableAttemptOwner inner, Func<CancellationToken, Task> after)
        : ISerializableAttemptOwner
    {
        public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken = default) => inner.ExecuteAsync(async token =>
        {
            T result = await operation(token);
            await after(token);
            return result;
        }, cancellationToken);
    }

    private sealed class Request : IAsyncDisposable
    {
        private readonly AsyncServiceScope scope;
        private readonly IHttpContextAccessor accessor;
        internal IServiceProvider Services => scope.ServiceProvider;
        internal DefaultHttpContext Context { get; }

        internal Request(IServiceProvider services, Guid? forwarded = null)
        {
            scope = services.CreateAsyncScope();
            Context = new DefaultHttpContext { RequestServices = Services };
            accessor = Services.GetRequiredService<IHttpContextAccessor>();
            Services.GetRequiredService<ITenantContextAccessor>().BindExecutionTenant(PlatformDefaults.DefaultTenantId);
            if (forwarded.HasValue)
                Context.Request.Headers[SupportAccessHeaderNames.SessionId] = forwarded.Value.ToString("D");
        }

        internal async Task Authenticate(string token)
        {
            accessor.HttpContext = Context;
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
}
