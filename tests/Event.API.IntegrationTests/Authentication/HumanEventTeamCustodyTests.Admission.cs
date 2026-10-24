using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Authentication;
using Explore.Application.Constants;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Application.Features.EventRoleAssignments.Handlers.Commands;
using Explore.Application.Features.EventRoleAssignments.Handlers.Queries;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

public sealed partial class HumanEventTeamCustodyTests
{
    [Test]
    [Arguments("assign")]
    [Arguments("email")]
    [Arguments("revoke")]
    [Arguments("team")]
    [Arguments("presets")]
    public async Task EveryOwnerRetainsCredentialDeadlineAfterAllBlockingReads(string operationKind)
    {
        await using var fixture = await CreateFixture();
        var target = await Seed(fixture);
        await MakeManagerDelegable(fixture);
        await using var request = new Request(fixture.Host.Services);
        await request.Authenticate(await fixture.SignInAsync());
        var context = request.Services.GetRequiredService<ExploreDbContext>();
        var lifetime = (await request.Services.GetRequiredService<IHumanAuthenticationLifetimeObserver>()
            .ObserveAsync(request.Context.User, default))!;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        request.Admission.AfterAllSources = async token =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(TimeSpan.FromSeconds(30), token);
            await context.EventRoleAssignments.AsNoTracking().SingleAsync(row => row.Id == target.Assignment, token);
            fixture.Clock.Advance(lifetime.ExpiresAt.Add(lifetime.ClockSkew).AddTicks(1) - fixture.Clock.GetUtcNow());
        };
        Task operation = Invoke();
        await ReleaseAfterEntry(operation, entered, release);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await operation);
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.EventRoleAssignments.AsNoTracking().CountAsync(row => row.EventId == target.Event)).IsEqualTo(1);
        await Assert.That(await observer.EventRoleAssignments.AsNoTracking().Where(row => row.Id == target.Assignment)
            .Select(row => row.Status).SingleAsync()).IsEqualTo(EventRoleAssignmentStatus.Active);
        await Assert.That(await observer.AuditLogs.AsNoTracking().AnyAsync(row =>
            row.EntityType == nameof(EventRoleAssignment) && row.ActorId == fixture.Binding.LocalSubjectId)).IsFalse();

        async Task Invoke()
        {
            switch (operationKind)
            {
                case "assign":
                    await Assert.That((await ActivatorUtilities.CreateInstance<AssignEventRoleCommandHandler>(request.Services)
                        .ExecuteAsync(Assignment(fixture, target.Event))).IsSuccess).IsTrue();
                    break;
                case "email":
                    await Assert.That((await ActivatorUtilities.CreateInstance<AssignEventRoleByEmailCommandHandler>(request.Services)
                        .ExecuteAsync(new()
                        {
                            TenantId = PlatformDefaults.DefaultTenantId,
                            EventId = target.Event,
                            TargetUserEmail = fixture.Login.Identifier,
                            RoleId = (int)RoleEnum.CheckInStaff,
                            StartsAtUtc = fixture.Clock.GetUtcNow().AddMinutes(-1).UtcDateTime
                        })).IsSuccess).IsTrue();
                    break;
                case "revoke":
                    await Assert.That((await ActivatorUtilities.CreateInstance<RevokeEventRoleAssignmentCommandHandler>(request.Services)
                        .ExecuteAsync(new()
                        {
                            TenantId = PlatformDefaults.DefaultTenantId,
                            EventId = target.Event,
                            AssignmentId = target.Assignment
                        })).IsSuccess).IsTrue();
                    break;
                case "team":
                    await ActivatorUtilities.CreateInstance<GetEventTeamListRequestHandler>(request.Services)
                        .QueryAsync(new() { TenantId = PlatformDefaults.DefaultTenantId, EventId = target.Event });
                    break;
                case "presets":
                    await ActivatorUtilities.CreateInstance<GetAssignableEventRolePresetsRequestHandler>(request.Services)
                        .QueryAsync(new()
                        {
                            TenantId = PlatformDefaults.DefaultTenantId,
                            EventId = target.Event,
                            AssignerUserId = fixture.Binding.LocalSubjectId
                        });
                    break;
            }
        }
    }

    [Test]
    [Arguments("live")]
    [Arguments("expired")]
    [Arguments("extended")]
    public async Task OriginalSupportHorizonCannotBeRenewedInsideTheTeamOwner(string state)
    {
        await using var fixture = await CreateFixture();
        var target = await Seed(fixture);
        DateTimeOffset expiry = fixture.Clock.GetUtcNow().AddMinutes(1);
        Guid sessionId;
        await using (var seed = fixture.Native.CreateDatabase())
        {
            seed.PlatformUserRoles.Add(new PlatformUserRole
            {
                Id = Guid.CreateVersion7(),
                UserId = fixture.Binding.LocalSubjectId,
                User = null!,
                RoleId = (int)RoleEnum.Admin,
                Role = null!,
                GrantedAt = fixture.Clock.GetUtcNow().UtcDateTime
            });
            foreach (string key in new[] { GovernanceSettingKeys.SupportAccess.Enabled, GovernanceSettingKeys.SupportAccess.AllowWriteMode })
            {
                var setting = await seed.SystemSettings.SingleOrDefaultAsync(row => row.SettingKey == key);
                if (setting is null) seed.SystemSettings.Add(new SystemSetting { SettingKey = key, Value = "true" });
                else setting.Value = "true";
            }
            var session = SupportAccessSession.Start(fixture.Binding.LocalSubjectId, PlatformDefaults.DefaultTenantId,
                SupportAccessModeEnum.Write, "maintenance", "Event team custody", "team-custody",
                fixture.Clock.GetUtcNow().AddMinutes(-1), expiry);
            sessionId = session.Id;
            seed.SupportAccessSessions.Add(session);
            await seed.SaveChangesAsync();
        }
        await using var request = new Request(fixture.Host.Services);
        request.Context.Request.Headers[SupportAccessHeaderNames.SessionId] = sessionId.ToString("D");
        await request.Authenticate(await fixture.SignInAsync());
        await Assert.That((await request.Services.GetRequiredService<ISupportAccessSessionService>().GetCurrentAsync()).IsActive).IsTrue();
        var context = request.Services.GetRequiredService<ExploreDbContext>();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        request.Admission.AfterNativeSources = async token =>
        {
            if (state == "extended")
                await context.SupportAccessSessions.Where(row => row.Id == sessionId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.ExpiresAtUtc, expiry.AddMinutes(5)), token);
        };
        request.Admission.AfterAllSources = async token =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(TimeSpan.FromSeconds(30), token);
            await context.SupportAccessSessions.AsNoTracking().SingleAsync(row => row.Id == sessionId, token);
            if (state != "live") fixture.Clock.Advance(expiry.AddTicks(1) - fixture.Clock.GetUtcNow());
        };
        var operation = ActivatorUtilities.CreateInstance<AssignEventRoleCommandHandler>(request.Services)
            .ExecuteAsync(Assignment(fixture, target.Event));
        await ReleaseAfterEntry(operation, entered, release);
        if (state == "live") await Assert.That((await operation).IsSuccess).IsTrue();
        else await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await operation);
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.EventRoleAssignments.AsNoTracking().AnyAsync(row =>
            row.EventId == target.Event && row.RoleId == (int)RoleEnum.CheckInStaff)).IsEqualTo(state == "live");
        await Assert.That(await observer.SupportAccessSessions.AsNoTracking().Where(row => row.Id == sessionId)
            .Select(row => row.ExpiresAtUtc).SingleAsync()).IsEqualTo(expiry);
    }

    [Test]
    public async Task OriginalAssignmentExpiryAfterFinalReadRejectsCommit()
    {
        await using var fixture = await CreateFixture();
        var target = await Seed(fixture);
        var expiry = fixture.Clock.GetUtcNow().AddSeconds(10).UtcDateTime;
        await using (var seed = fixture.Native.CreateDatabase())
            await seed.EventRoleAssignments.Where(row => row.Id == target.Assignment)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.ExpiresAtUtc, expiry));
        await using var request = new Request(fixture.Host.Services);
        await request.Authenticate(await fixture.SignInAsync());
        var context = request.Services.GetRequiredService<ExploreDbContext>();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        request.Admission.AfterAllSources = async token =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(TimeSpan.FromSeconds(30), token);
            await context.EventRoleAssignments.AsNoTracking().SingleAsync(row => row.Id == target.Assignment, token);
            fixture.Clock.Advance(new DateTimeOffset(expiry).AddTicks(1) - fixture.Clock.GetUtcNow());
        };
        var operation = ActivatorUtilities.CreateInstance<AssignEventRoleCommandHandler>(request.Services)
            .ExecuteAsync(Assignment(fixture, target.Event));
        await ReleaseAfterEntry(operation, entered, release);
        await Assert.ThrowsAsync<AuthorizationException>(async () => await operation);
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.EventRoleAssignments.AsNoTracking().CountAsync(row => row.EventId == target.Event)).IsEqualTo(1);
    }

    [Test]
    public async Task ASecondRealLoginCannotExtendTheOriginalCredentialAtTerminalAdmission()
    {
        await using var fixture = await CreateFixture();
        var target = await Seed(fixture);
        await using var request = new Request(fixture.Host.Services);
        await request.Authenticate(await fixture.SignInAsync());
        var lifetime = (await request.Services.GetRequiredService<IHumanAuthenticationLifetimeObserver>()
            .ObserveAsync(request.Context.User, default))!;
        fixture.Clock.Advance(lifetime.ExpiresAt.AddMinutes(-1) - fixture.Clock.GetUtcNow());
        await using var renewed = new Request(fixture.Host.Services);
        await renewed.Authenticate(await fixture.SignInAsync());
        var renewedLifetime = (await renewed.Services.GetRequiredService<IHumanAuthenticationLifetimeObserver>()
            .ObserveAsync(renewed.Context.User, default))!;
        request.Services.GetRequiredService<IHttpContextAccessor>().HttpContext = request.Context;
        var context = request.Services.GetRequiredService<ExploreDbContext>();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        request.Admission.AfterAllSources = async token =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(TimeSpan.FromSeconds(30), token);
            await context.EventRoleAssignments.AsNoTracking().SingleAsync(row => row.Id == target.Assignment, token);
            fixture.Clock.Advance(lifetime.ExpiresAt.Add(lifetime.ClockSkew).AddTicks(1) - fixture.Clock.GetUtcNow());
            await Assert.That(renewedLifetime.IsValidAt(fixture.Clock.GetUtcNow())).IsTrue();
            request.Context.User = renewed.Context.User;
        };
        var operation = ActivatorUtilities.CreateInstance<AssignEventRoleCommandHandler>(request.Services)
            .ExecuteAsync(Assignment(fixture, target.Event));
        await ReleaseAfterEntry(operation, entered, release);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await operation);
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.EventRoleAssignments.CountAsync(row => row.EventId == target.Event)).IsEqualTo(1);
        await Assert.That(await observer.AuditLogs.AnyAsync(row => row.EntityType == nameof(EventRoleAssignment)
            && row.ActorId == fixture.Binding.LocalSubjectId)).IsFalse();
    }

    private static async Task ReleaseAfterEntry(Task operation, TaskCompletionSource entered, TaskCompletionSource release)
    {
        try
        {
            await Task.WhenAny(entered.Task, operation).WaitAsync(TimeSpan.FromSeconds(30));
            if (!entered.Task.IsCompleted) await operation;
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
        }
        finally { release.TrySetResult(); }
    }

    private static async Task MakeManagerDelegable(LocalIdentityLifecycleHttpFixture fixture)
    {
        await using var seed = fixture.Native.CreateDatabase();
        var forbidden = new[]
        {
            PermissionCodes.EventManageOwner, PermissionCodes.EventTransferOwnership, PermissionCodes.EventDelete,
            PermissionCodes.EventManageFinance, PermissionCodes.EventApprovePublish
        };
        await seed.RolePermissions.Where(row => row.RoleId == (int)RoleEnum.EventManager
            && (row.Permission.IsFiltered || forbidden.Contains(row.Permission.MasterCode))).ExecuteDeleteAsync();
    }
}
