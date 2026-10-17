using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Authentication;
using Explore.Application.Constants;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.TenantUserRoleGrants.Handlers.Commands;
using Explore.Application.Features.TenantUserRoleGrants.Handlers.Queries;
using Explore.Application.Features.TenantUserRoleGrants.Requests.Commands;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

public sealed partial class HumanAccountMembershipCustodyTests
{
    [Test]
    public async Task EqualRequestCannotReplaceTheOriginalAction()
    {
        await using var fixture = await CreateFixture();
        var (target, _) = await SeedTenantAdministrator(fixture);
        await using var request = new Request(fixture.Host.Services);
        await request.Authenticate(await fixture.SignInAsync());
        var command = new RevokeTenantUserRoleGrantCommand { Id = target, TenantId = PlatformDefaults.DefaultTenantId };
        var prepared = await request.Services.GetRequiredService<IHumanAccountAdministrativeActionPreparation>()
            .PrepareAsync(command, default);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            request.Services.GetRequiredService<ISerializableAttemptOwner>().ExecuteAsync(async token =>
            {
                await prepared.AcquireAsync(command with { }, token);
                return true;
            }));
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.TenantUserRoleGrants.AsNoTracking().Where(row => row.Id == target)
            .Select(row => row.RevokedAt).SingleAsync()).IsNull();
    }

    [Test]
    public async Task CaughtAcquisitionReplayStillRollsBackTheOriginalOwner()
    {
        await using var fixture = await CreateFixture();
        var (target, _) = await SeedTenantAdministrator(fixture);
        await using var request = new Request(fixture.Host.Services);
        await request.Authenticate(await fixture.SignInAsync());
        var command = new RevokeTenantUserRoleGrantCommand { Id = target, TenantId = PlatformDefaults.DefaultTenantId };
        var prepared = await request.Services.GetRequiredService<IHumanAccountAdministrativeActionPreparation>()
            .PrepareAsync(command, default);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            request.Services.GetRequiredService<ISerializableAttemptOwner>().ExecuteAsync(async token =>
            {
                await prepared.AcquireAsync(command, token);
                var repository = request.Services.GetRequiredService<ITenantUserRoleGrantRepository>();
                var grant = (await repository.GetById(target))!;
                grant.RevokedAt = prepared.ChangedAt;
                grant.RevokedBy = prepared.PrincipalId;
                await repository.Update(grant);
                prepared.Complete(target);
                await Assert.ThrowsAsync<InvalidOperationException>(() => prepared.AcquireAsync(command, token));
                return true;
            }));
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.TenantUserRoleGrants.AsNoTracking().Where(row => row.Id == target)
            .Select(row => row.RevokedAt).SingleAsync()).IsNull();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task OriginalSupportDeadlineSurvivesPersistedExtension(bool extend)
    {
        await using var fixture = await CreateFixture();
        var (target, _) = await SeedTenantAdministrator(fixture, platform: true);
        DateTimeOffset expiry = fixture.Clock.GetUtcNow().AddMinutes(1);
        Guid sessionId;
        await using (var seed = fixture.Native.CreateDatabase())
        {
            foreach (string key in new[] { GovernanceSettingKeys.SupportAccess.Enabled, GovernanceSettingKeys.SupportAccess.AllowWriteMode })
            {
                var setting = await seed.SystemSettings.SingleOrDefaultAsync(row => row.SettingKey == key);
                if (setting is null) seed.SystemSettings.Add(new SystemSetting { SettingKey = key, Value = "true" });
                else setting.Value = "true";
            }
            var session = SupportAccessSession.Start(fixture.Binding.LocalSubjectId, PlatformDefaults.DefaultTenantId,
                SupportAccessModeEnum.Write, "maintenance", "Account custody", "account-custody",
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
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var owner = new AfterBody((ISerializableAttemptOwner)new EfCoreUnitOfWork(context), async token =>
        {
            if (extend)
                await context.SupportAccessSessions.Where(row => row.Id == sessionId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.ExpiresAtUtc, expiry.AddMinutes(5)), token);
            request.Services.GetRequiredService<IHumanActionCommitFinalizer>().Register(async finalToken =>
            {
                // This reader is registered after account admission. Its completion,
                // rather than a sleep, determines the terminal time sample.
                entered.TrySetResult();
                await released.Task.WaitAsync(TimeSpan.FromSeconds(30), finalToken);
                await context.SupportAccessSessions.AsNoTracking().SingleAsync(row => row.Id == sessionId, finalToken);
                if (extend) fixture.Clock.Advance(expiry.AddTicks(1) - fixture.Clock.GetUtcNow());
            }, _ => { });
        });
        Task<bool> operation = ActivatorUtilities.CreateInstance<RevokeTenantUserRoleGrantCommandHandler>(request.Services, owner)
            .ExecuteAsync(new() { Id = target, TenantId = PlatformDefaults.DefaultTenantId }, default);
        try
        {
            await Task.WhenAny(entered.Task, operation).WaitAsync(TimeSpan.FromSeconds(30));
            if (!entered.Task.IsCompleted) await operation;
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
        }
        finally { released.TrySetResult(); }
        if (extend) await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await operation);
        else await Assert.That(await operation).IsTrue();
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.TenantUserRoleGrants.AsNoTracking().Where(row => row.Id == target)
            .Select(row => row.RevokedAt != null).SingleAsync()).IsEqualTo(!extend);
        await Assert.That(await observer.SupportAccessSessions.AsNoTracking().Where(row => row.Id == sessionId)
            .Select(row => row.ExpiresAtUtc).SingleAsync()).IsEqualTo(expiry);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ProtectedQueryCannotDiscloseAfterItsLastBlockingReadExpires(bool details)
    {
        await using var fixture = await CreateFixture();
        var (target, _) = await SeedTenantAdministrator(fixture);
        await using var request = new Request(fixture.Host.Services);
        await request.Authenticate(await fixture.SignInAsync());
        var context = request.Services.GetRequiredService<ExploreDbContext>();
        var readEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var owner = new AfterBody((ISerializableAttemptOwner)new EfCoreUnitOfWork(context), _ =>
        {
            request.Services.GetRequiredService<IHumanActionCommitFinalizer>().Register(async token =>
            {
                readEntered.TrySetResult();
                await releaseRead.Task.WaitAsync(TimeSpan.FromSeconds(30), token);
                await context.TenantUserRoleGrants.AsNoTracking().SingleAsync(row => row.Id == target, token);
                var lifetime = (await request.Services.GetRequiredService<IHumanAuthenticationLifetimeObserver>()
                    .ObserveAsync(request.Context.User, token))!;
                fixture.Clock.Advance(lifetime.ExpiresAt.Add(lifetime.ClockSkew).AddTicks(1) - fixture.Clock.GetUtcNow());
            }, _ => { });
            return Task.CompletedTask;
        });
        async Task Query()
        {
            if (details)
                await ActivatorUtilities.CreateInstance<GetTenantUserRoleGrantDetailsRequestHandler>(request.Services, owner)
                    .QueryAsync(new() { Id = target, TenantId = PlatformDefaults.DefaultTenantId }, default);
            else
                await ActivatorUtilities.CreateInstance<GetTenantUserRoleGrantListRequestHandler>(request.Services, owner)
                    .QueryAsync(new() { TenantId = PlatformDefaults.DefaultTenantId }, default);
        }
        Task operation = Query();
        try
        {
            await Task.WhenAny(readEntered.Task, operation).WaitAsync(TimeSpan.FromSeconds(30));
            if (!readEntered.Task.IsCompleted) await operation;
            await readEntered.Task.WaitAsync(TimeSpan.FromSeconds(30));
        }
        finally { releaseRead.TrySetResult(); }
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await operation);
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.TenantUserRoleGrants.AsNoTracking().Where(row => row.Id == target)
            .Select(row => row.RevokedAt).SingleAsync()).IsNull();
    }

    [Test]
    public async Task GrantCreationAndMissingDetailUseAuthenticSelectedCustody()
    {
        await using var fixture = await CreateFixture();
        var (adminGrant, _) = await SeedTenantAdministrator(fixture);
        Guid tenantUser;
        await using (var seed = fixture.Native.CreateDatabase())
            tenantUser = await seed.TenantUserRoleGrants.Where(row => row.Id == adminGrant).Select(row => row.TenantUserId).SingleAsync();
        await using var request = new Request(fixture.Host.Services);
        await request.Authenticate(await fixture.SignInAsync());
        var command = new CreateTenantUserRoleGrantCommand
        {
            TenantId = PlatformDefaults.DefaultTenantId,
            TenantUserRoleGrantDto = new() { TenantUserId = tenantUser, RoleId = (int)RoleEnum.TenantModerator }
        };
        var handler = ActivatorUtilities.CreateInstance<CreateTenantUserRoleGrantCommandHandler>(request.Services);
        var created = await handler.ExecuteAsync(command, default);
        await Assert.That(created.IsSuccess).IsTrue();
        await Assert.That((await handler.ExecuteAsync(command, default)).IsSuccess).IsFalse();
        var details = ActivatorUtilities.CreateInstance<GetTenantUserRoleGrantDetailsRequestHandler>(request.Services);
        await Assert.That(await details.QueryAsync(new()
        { Id = Guid.Empty, TenantId = PlatformDefaults.DefaultTenantId }, default)).IsNull();
        var detail = await details.QueryAsync(new() { Id = created.Id, TenantId = PlatformDefaults.DefaultTenantId }, default);
        var list = await ActivatorUtilities.CreateInstance<GetTenantUserRoleGrantListRequestHandler>(request.Services)
            .QueryAsync(new() { TenantId = PlatformDefaults.DefaultTenantId }, default);
        await Assert.That(detail!.UserId).IsEqualTo(fixture.Binding.LocalSubjectId);
        await Assert.That(list.Count(row => row.Id == created.Id)).IsEqualTo(1);
        await using var observer = fixture.Native.CreateDatabase();
        var persisted = await observer.TenantUserRoleGrants.AsNoTracking().SingleAsync(row => row.Id == created.Id);
        await Assert.That(persisted.GrantedBy).IsEqualTo(fixture.Binding.LocalSubjectId);
        await Assert.That(persisted.RoleId).IsEqualTo((int)RoleEnum.TenantModerator);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task UnannotatedGroupMutationRetainsTheOriginalSupportWriteBoundary(bool write)
    {
        await using var fixture = await CreateFixture();
        var target = await SeedMembers(fixture, group: true, secondAdmin: false);
        await SeedTenantAdministrator(fixture, platform: true);
        Guid sessionId;
        await using (var seed = fixture.Native.CreateDatabase())
        {
            foreach (string key in new[] { GovernanceSettingKeys.SupportAccess.Enabled, GovernanceSettingKeys.SupportAccess.AllowWriteMode })
            {
                var setting = await seed.SystemSettings.SingleOrDefaultAsync(row => row.SettingKey == key);
                if (setting is null) seed.SystemSettings.Add(new SystemSetting { SettingKey = key, Value = "true" });
                else setting.Value = "true";
            }
            var session = SupportAccessSession.Start(fixture.Binding.LocalSubjectId, PlatformDefaults.DefaultTenantId,
                write ? SupportAccessModeEnum.Write : SupportAccessModeEnum.ReadOnly,
                "maintenance", "Group custody", "group-custody",
                fixture.Clock.GetUtcNow().AddMinutes(-1), fixture.Clock.GetUtcNow().AddMinutes(5));
            sessionId = session.Id;
            seed.SupportAccessSessions.Add(session);
            await seed.SaveChangesAsync();
        }
        await using var request = new Request(fixture.Host.Services);
        request.Context.Request.Headers[SupportAccessHeaderNames.SessionId] = sessionId.ToString("D");
        await request.Authenticate(await fixture.SignInAsync());
        await Assert.That((await request.Services.GetRequiredService<ISupportAccessSessionService>().GetCurrentAsync()).IsActive).IsTrue();
        var handler = ActivatorUtilities.CreateInstance<Explore.Application.Features.GroupMembers.Handlers.Commands.AddGroupMemberCommandHandler>(
            request.Services);
        var command = new Explore.Application.Features.GroupMembers.Requests.Commands.AddGroupMemberCommand
        {
            RequesterUserId = fixture.Binding.LocalSubjectId.ToString("D"),
            AddGroupMemberDto = new() { GroupId = target.Owner, Email = target.OtherEmail, Role = RoleEnum.GroupMember }
        };
        if (write)
            await Assert.That((await handler.ExecuteAsync(command, default)).IsSuccess).IsTrue();
        else
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => handler.ExecuteAsync(command, default));
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.GroupMembers.AsNoTracking().AnyAsync(row =>
            row.GroupTenant.GroupId == target.Owner && row.UserId == target.OtherUser)).IsEqualTo(write);
    }

    [Test]
    public async Task SourcesChangedBetweenPreparationAndAcquisitionCannotBeReplaced()
    {
        await using var fixture = await CreateFixture();
        var (target, _) = await SeedTenantAdministrator(fixture);
        await using var request = new Request(fixture.Host.Services);
        await request.Authenticate(await fixture.SignInAsync());
        var command = new RevokeTenantUserRoleGrantCommand { Id = target, TenantId = PlatformDefaults.DefaultTenantId };
        var prepared = await request.Services.GetRequiredService<IHumanAccountAdministrativeActionPreparation>()
            .PrepareAsync(command, default);
        await using (var writer = fixture.Native.CreateDatabase())
            await writer.TenantUserRoleGrants.Where(row => row.Id == target)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.RevokedAt, fixture.Clock.GetUtcNow().UtcDateTime));
        var owner = request.Services.GetRequiredService<ISerializableAttemptOwner>();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => owner.ExecuteAsync(async token =>
        {
            await prepared.AcquireAsync(command, token);
            return true;
        }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => owner.ExecuteAsync(async token =>
        {
            await prepared.AcquireAsync(command, token);
            return true;
        }));
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.TenantUserRoleGrants.AsNoTracking().Where(row => row.Id == target)
            .Select(row => row.RevokedAt).SingleAsync()).IsNotNull();
    }

    [Test]
    public async Task IndependentLocalIdentityIsExplicitlyNotAdmitted()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(
            topology: IdentityDatabaseTopology.External);
        var (target, _) = await SeedTenantAdministrator(fixture);
        await using var request = new Request(fixture.Host.Services);
        await request.Authenticate(await fixture.SignInAsync());
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            ActivatorUtilities.CreateInstance<RevokeTenantUserRoleGrantCommandHandler>(request.Services)
                .ExecuteAsync(new() { Id = target, TenantId = PlatformDefaults.DefaultTenantId }, default));
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.TenantUserRoleGrants.AsNoTracking().Where(row => row.Id == target)
            .Select(row => row.RevokedAt).SingleAsync()).IsNull();
    }
}
