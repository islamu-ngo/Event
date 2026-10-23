using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.SupportAccess.Handlers.Commands;
using Explore.Application.Features.SupportAccess.Requests.Commands;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

public sealed partial class HumanSupportAdministrationCustodyTests
{
    [Test]
    [Arguments("binding")]
    [Arguments("stamp")]
    [Arguments("grant")]
    [Arguments("route")]
    [Arguments("governance")]
    [Arguments("expiry")]
    [Arguments("mode")]
    [Arguments("actor")]
    [Arguments("tenant")]
    [Arguments("end_reason")]
    [Arguments("ended_at")]
    [Arguments("audit")]
    public async Task OwnStopCannotLaunderAnyOtherSourceOrEffectChange(string change)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        Guid sessionId = await Seed(fixture, session: true);
        Guid foreignTenant = Guid.CreateVersion7();
        await using (var seed = fixture.Native.CreateDatabase())
        {
            seed.Tenants.Add(new Tenant
            {
                Id = foreignTenant,
                Slug = "support-foreign",
                FullName = "Support foreign",
                TenantStatusId = (int)TenantStatusEnum.Active,
                TenantStatus = null!
            });
            await seed.SaveChangesAsync();
        }
        var original = await ReadSession(fixture, sessionId);
        var identity = await fixture.ReadIdentityAsync();
        await using var request = new Request(fixture.Host.Services, sessionId);
        await request.Authenticate(await fixture.SignInAsync());
        var context = request.Services.GetRequiredService<ExploreDbContext>();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var owner = new AfterBody(request.Services.GetRequiredService<ISerializableAttemptOwner>(), async token =>
        {
            var saved = await context.SupportAccessSessions.AsNoTracking().SingleAsync(row => row.Id == sessionId, token);
            await Assert.That(saved.StatusId).IsEqualTo((int)SupportAccessSessionStatusEnum.Stopped);
            await Assert.That(await context.SupportAccessAuditEvents.AsNoTracking()
                .CountAsync(row => row.SupportAccessSessionId == sessionId, token)).IsEqualTo(1);
            entered.TrySetResult();
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
                case "grant":
                    await context.PlatformUserRoles.Where(row => row.UserId == fixture.Binding.LocalSubjectId)
                        .ExecuteDeleteAsync(token);
                    break;
                case "route":
                    await context.SystemSettings.Where(row => row.SettingKey == GovernanceSettingKeys.Security.AuthorizationProvider)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.Value, "\"cerbos\""), token);
                    break;
                case "governance":
                    await context.SystemSettings.Where(row => row.SettingKey == GovernanceSettingKeys.SupportAccess.Enabled)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.Value, "false"), token);
                    break;
                case "expiry":
                    await context.SupportAccessSessions.Where(row => row.Id == sessionId)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.ExpiresAtUtc,
                            original.ExpiresAtUtc.AddMinutes(10)), token);
                    break;
                case "mode":
                    await context.SupportAccessSessions.Where(row => row.Id == sessionId)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.ModeId, (int)SupportAccessModeEnum.Write), token);
                    break;
                case "actor":
                    await context.SupportAccessSessions.Where(row => row.Id == sessionId)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.ActorUserId, (Guid?)null), token);
                    break;
                case "tenant":
                    await context.SupportAccessSessions.Where(row => row.Id == sessionId)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.TargetTenantId, foreignTenant), token);
                    break;
                case "end_reason":
                    await context.SupportAccessSessions.Where(row => row.Id == sessionId)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.EndReasonText, "Not the requested ending"), token);
                    break;
                case "ended_at":
                    await context.SupportAccessSessions.Where(row => row.Id == sessionId)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.EndedAtUtc,
                            saved.EndedAtUtc!.Value.AddSeconds(1)), token);
                    break;
                case "audit":
                    await context.SupportAccessAuditEvents.Where(row => row.SupportAccessSessionId == sessionId)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.Outcome, "not_stopped"), token);
                    break;
            }
        });
        Task operation = Invoke(request.Services, owner, "stop", sessionId);
        try { await Reach(entered.Task, operation); }
        finally { release.TrySetResult(); }
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await operation);
        var stored = await ReadSession(fixture, sessionId);
        await Assert.That(stored.StatusId).IsEqualTo((int)SupportAccessSessionStatusEnum.Active);
        await Assert.That(stored.EndedAtUtc).IsNull();
        await Assert.That(stored.EndReasonText).IsNull();
        await Assert.That(stored.ExpiresAtUtc).IsEqualTo(original.ExpiresAtUtc);
        await Assert.That(stored.ModeId).IsEqualTo(original.ModeId);
        await Assert.That(stored.TargetTenantId).IsEqualTo(original.TargetTenantId);
        await Assert.That(stored.ActorUserId).IsEqualTo(original.ActorUserId);
        await Assert.That(stored.ConcurrencyStamp).IsEqualTo(original.ConcurrencyStamp);
        await Assert.That((await fixture.ReadIdentityAsync()).SecurityStamp).IsEqualTo(identity.SecurityStamp);
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.SupportAccessAuditEvents.AsNoTracking().CountAsync()).IsEqualTo(0);
        await Assert.That(await observer.PlatformUserRoles.AsNoTracking()
            .AnyAsync(row => row.UserId == fixture.Binding.LocalSubjectId)).IsTrue();
        await Assert.That(await observer.UserExternalLogins.AsNoTracking()
            .Where(row => row.Id == fixture.Binding.ExternalLoginId).Select(row => row.ProviderKey).SingleAsync())
            .IsEqualTo(fixture.Binding.LocalSubjectId.ToString("D"));
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task OriginalSupportHorizonStillAppliesAfterOwnTermination(bool force)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        Guid sessionId = await Seed(fixture, session: true);
        var original = await ReadSession(fixture, sessionId);
        await using var request = new Request(fixture.Host.Services, sessionId);
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
                await context.SupportAccessSessions.AsNoTracking().SingleAsync(row => row.Id == sessionId, token);
                fixture.Clock.Advance(original.ExpiresAtUtc.AddTicks(1) - fixture.Clock.GetUtcNow());
            }, _ => { });
            return Task.CompletedTask;
        });
        Task operation = Invoke(request.Services, owner, force ? "force" : "stop", sessionId);
        try { await Reach(entered.Task, operation); }
        finally { release.TrySetResult(); }
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await operation);
        await Assert.That((await ReadSession(fixture, sessionId)).StatusId).IsEqualTo((int)SupportAccessSessionStatusEnum.Active);
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.SupportAccessAuditEvents.AsNoTracking().CountAsync()).IsEqualTo(0);
    }

    [Test]
    [Arguments("target")]
    [Arguments("authority")]
    [Arguments("equal_request")]
    public async Task OriginalPreparationCannotBeReplacedBeforeAcquisition(string change)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        Guid sessionId = await Seed(fixture, session: true);
        await using var request = new Request(fixture.Host.Services);
        await request.Authenticate(await fixture.SignInAsync());
        var command = new StopSupportAccessSessionCommand { SessionId = sessionId };
        var prepared = await request.Services.GetRequiredService<IHumanSupportAdministrationPreparation>()
            .PrepareAsync(command, default);
        await using (var writer = fixture.Native.CreateDatabase())
        {
            if (change == "target")
                await writer.SupportAccessSessions.Where(row => row.Id == sessionId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.TicketReference, "changed-before-owner"));
            if (change == "authority")
                await writer.PlatformUserRoles.Where(row => row.UserId == fixture.Binding.LocalSubjectId).ExecuteDeleteAsync();
        }
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            request.Services.GetRequiredService<ISerializableAttemptOwner>().ExecuteAsync(async token =>
            {
                await prepared.AcquireAsync(change == "equal_request" ? command with { } : command, token);
                return true;
            }));
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.SupportAccessAuditEvents.AsNoTracking().CountAsync()).IsEqualTo(0);
        await Assert.That((await ReadSession(fixture, sessionId)).StatusId).IsEqualTo((int)SupportAccessSessionStatusEnum.Active);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CaughtReplayOrIncompleteBodyCannotCommitTheFirstStop(bool replay)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        Guid sessionId = await Seed(fixture, session: true);
        await using var request = new Request(fixture.Host.Services, sessionId);
        await request.Authenticate(await fixture.SignInAsync());
        var command = new StopSupportAccessSessionCommand { SessionId = sessionId, EndReasonText = "Finished" };
        var prepared = await request.Services.GetRequiredService<IHumanSupportAdministrationPreparation>()
            .PrepareAsync(command, default);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            request.Services.GetRequiredService<ISerializableAttemptOwner>().ExecuteAsync(async token =>
            {
                await prepared.AcquireAsync(command, token);
                var repository = request.Services.GetRequiredService<ISupportAccessSessionRepository>();
                var session = (await repository.GetOwnedSessionAsync(sessionId, prepared.PrincipalId, token))!;
                prepared.EndSession(session);
                await repository.UpdateAsync(session, token);
                var audit = SupportAccessAuditEvent.CreateLifecycleEvent(session,
                    SupportAccessAuditEventTypeEnum.Stopped, "stopped", prepared.TransitionAt);
                await request.Services.GetRequiredService<ISupportAccessAuditEventRepository>().CreateAsync(audit, token);
                if (replay)
                {
                    prepared.Complete(session, audit);
                    await Assert.ThrowsAsync<InvalidOperationException>(() => prepared.AcquireAsync(command, token));
                }
                return true;
            }));
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.SupportAccessAuditEvents.AsNoTracking().CountAsync()).IsEqualTo(0);
        await Assert.That((await ReadSession(fixture, sessionId)).StatusId).IsEqualTo((int)SupportAccessSessionStatusEnum.Active);
    }

    [Test]
    public async Task CancellationAfterBothSavesRollsBackTheWholeOwner()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        Guid sessionId = await Seed(fixture, session: true);
        await using var request = new Request(fixture.Host.Services, sessionId);
        await request.Authenticate(await fixture.SignInAsync());
        using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var owner = new AfterBody(request.Services.GetRequiredService<ISerializableAttemptOwner>(), async token =>
        {
            var context = request.Services.GetRequiredService<ExploreDbContext>();
            await Assert.That(await context.SupportAccessAuditEvents.AsNoTracking()
                .CountAsync(row => row.SupportAccessSessionId == sessionId, token)).IsEqualTo(1);
            entered.TrySetResult();
            await release.Task.WaitAsync(TimeSpan.FromSeconds(30), token);
        });
        var handler = ActivatorUtilities.CreateInstance<StopSupportAccessSessionCommandHandler>(request.Services, owner);
        Task operation = handler.ExecuteAsync(new() { SessionId = sessionId }, cancellation.Token);
        try
        {
            await Reach(entered.Task, operation);
            cancellation.Cancel();
        }
        finally { release.TrySetResult(); }
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await operation);
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.SupportAccessAuditEvents.AsNoTracking().CountAsync()).IsEqualTo(0);
        await Assert.That((await ReadSession(fixture, sessionId)).StatusId).IsEqualTo((int)SupportAccessSessionStatusEnum.Active);
    }
}
