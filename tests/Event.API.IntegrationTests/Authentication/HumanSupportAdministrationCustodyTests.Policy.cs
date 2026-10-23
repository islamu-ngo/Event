using System.Security.Claims;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Application.Features.SupportAccess;
using Explore.Application.Features.SupportAccess.Handlers.Commands;
using Explore.Application.Features.SupportAccess.Handlers.Queries;
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
    public async Task OriginalAdministratorCanStartThenDiscloseThePersistedLifecycle()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await Seed(fixture, session: false);
        Guid id;
        await using (var request = new Request(fixture.Host.Services))
        {
            await request.Authenticate(await fixture.SignInAsync());
            var result = await ActivatorUtilities.CreateInstance<StartSupportAccessSessionCommandHandler>(request.Services)
                .ExecuteAsync(StartRequest(), default);
            await Assert.That(result.IsSuccess).IsTrue();
            id = result.Id;
        }
        await using (var request = new Request(fixture.Host.Services))
        {
            await request.Authenticate(await fixture.SignInAsync());
            var list = await ActivatorUtilities.CreateInstance<ListSupportAccessSessionsQueryHandler>(request.Services)
                .QueryAsync(new() { TargetTenantId = PlatformDefaults.DefaultTenantId }, default);
            await Assert.That(list.Items.Single().Id).IsEqualTo(id);
            var audit = await ActivatorUtilities.CreateInstance<GetSupportAccessAuditEventsQueryHandler>(request.Services)
                .QueryAsync(new() { TargetTenantId = PlatformDefaults.DefaultTenantId, SessionId = id }, default);
            await Assert.That(audit.Items.Single().Outcome).IsEqualTo("started");
            await Assert.That(audit.Items.Single().ActorUserId).IsEqualTo(fixture.Binding.LocalSubjectId);
        }
        var stored = await ReadSession(fixture, id);
        await Assert.That(stored.TargetTenantId).IsEqualTo(PlatformDefaults.DefaultTenantId);
        await Assert.That(stored.ActorUserId).IsEqualTo(fixture.Binding.LocalSubjectId);
        await Assert.That(stored.ModeId).IsEqualTo((int)SupportAccessModeEnum.ReadOnly);
        await Assert.That(stored.ExpiresAtUtc - stored.StartedAtUtc).IsEqualTo(TimeSpan.FromMinutes(5));
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.SupportAccessAuditEvents.AsNoTracking()
            .CountAsync(row => row.SupportAccessSessionId == id)).IsEqualTo(1);
    }

    [Test]
    [Arguments("start")]
    [Arguments("stop")]
    [Arguments("force")]
    [Arguments("list")]
    [Arguments("audit")]
    public async Task ForwardedSupportAndRoleClaimsNeverCreateAdministratorAuthority(string family)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        Guid sessionId = await Seed(fixture, session: true, administrator: false);
        await using var request = new Request(fixture.Host.Services, sessionId);
        await request.Authenticate(await fixture.SignInAsync());
        ((ClaimsIdentity)request.Context.User.Identity!).AddClaim(new Claim(ClaimTypes.Role, "platform.admin"));
        await Assert.That((await request.Services.GetRequiredService<ISupportAccessSessionService>().GetCurrentAsync()).IsActive).IsTrue();
        await Assert.ThrowsAsync<AuthorizationException>(() =>
            Invoke(request.Services, request.Services.GetRequiredService<ISerializableAttemptOwner>(), family, sessionId));
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.SupportAccessAuditEvents.AsNoTracking().CountAsync()).IsEqualTo(0);
        await Assert.That((await ReadSession(fixture, sessionId)).StatusId).IsEqualTo((int)SupportAccessSessionStatusEnum.Active);
    }

    [Test]
    [Arguments("start")]
    [Arguments("stop")]
    [Arguments("force")]
    [Arguments("list")]
    [Arguments("audit")]
    public async Task TenantAdministratorRetainsOnlyTheExistingDisclosureQuestions(string family)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        Guid sessionId = await Seed(fixture, session: true, administrator: false);
        await using (var seed = fixture.Native.CreateDatabase())
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
                Id = Guid.CreateVersion7(),
                TenantId = member.TenantId,
                Tenant = null!,
                TenantUserId = member.Id,
                TenantUser = member,
                RoleId = (int)RoleEnum.TenantAdmin,
                Role = null!,
                RoleScopeId = (int)RoleScopeEnum.Tenant,
                GrantedAt = fixture.Clock.GetUtcNow().UtcDateTime
            });
            var session = await seed.SupportAccessSessions.SingleAsync(row => row.Id == sessionId);
            seed.SupportAccessAuditEvents.Add(SupportAccessAuditEvent.CreateLifecycleEvent(session,
                SupportAccessAuditEventTypeEnum.Started, "started", session.StartedAtUtc));
            await seed.SaveChangesAsync();
        }
        await using var request = new Request(fixture.Host.Services);
        await request.Authenticate(await fixture.SignInAsync());
        if (family is "list")
        {
            var list = await ActivatorUtilities.CreateInstance<ListSupportAccessSessionsQueryHandler>(request.Services)
                .QueryAsync(new() { TargetTenantId = PlatformDefaults.DefaultTenantId }, default);
            await Assert.That(list.Items.Single().Id).IsEqualTo(sessionId);
        }
        else if (family is "audit")
        {
            var audit = await ActivatorUtilities.CreateInstance<GetSupportAccessAuditEventsQueryHandler>(request.Services)
                .QueryAsync(new() { TargetTenantId = PlatformDefaults.DefaultTenantId, SessionId = sessionId }, default);
            await Assert.That(audit.Items.Single().Outcome).IsEqualTo("started");
        }
        else
            await Assert.ThrowsAsync<AuthorizationException>(() =>
                Invoke(request.Services, request.Services.GetRequiredService<ISerializableAttemptOwner>(), family, sessionId));
        await Assert.That((await ReadSession(fixture, sessionId)).StatusId).IsEqualTo((int)SupportAccessSessionStatusEnum.Active);
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.SupportAccessAuditEvents.AsNoTracking().CountAsync()).IsEqualTo(1);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task StoppingAnotherActorRequiresTheExistingForceStopOperation(bool force)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await Seed(fixture, session: false);
        var otherLogin = await fixture.Native.SeedLocalUserAsync(emailConfirmed: true);
        Guid sessionId, otherActor;
        await using (var seed = fixture.Native.CreateDatabase())
        {
            otherActor = await seed.Set<LocalIdentityUser>().Where(row => row.Email == otherLogin.Identifier)
                .Select(row => row.Id).SingleAsync();
            var session = SupportAccessSession.Start(otherActor, PlatformDefaults.DefaultTenantId, SupportAccessModeEnum.ReadOnly,
                "maintenance", "Other actor", "other-support", fixture.Clock.GetUtcNow().AddMinutes(-1),
                fixture.Clock.GetUtcNow().AddMinutes(5));
            sessionId = session.Id;
            seed.SupportAccessSessions.Add(session);
            await seed.SaveChangesAsync();
        }
        await using var request = new Request(fixture.Host.Services);
        await request.Authenticate(await fixture.SignInAsync());
        var result = force
            ? await ActivatorUtilities.CreateInstance<ForceStopSupportAccessSessionCommandHandler>(request.Services)
                .ExecuteAsync(new() { SessionId = sessionId }, default)
            : await ActivatorUtilities.CreateInstance<StopSupportAccessSessionCommandHandler>(request.Services)
                .ExecuteAsync(new() { SessionId = sessionId }, default);
        await Assert.That(result.IsSuccess).IsEqualTo(force);
        if (!force) await Assert.That(result.FailureCode).IsEqualTo(SupportAccessFailureCodes.SessionNotFound);
        var stored = await ReadSession(fixture, sessionId);
        await Assert.That(stored.ActorUserId).IsEqualTo(otherActor);
        await Assert.That(stored.StatusId).IsEqualTo((int)(force
            ? SupportAccessSessionStatusEnum.Revoked : SupportAccessSessionStatusEnum.Active));
        await using var observer = fixture.Native.CreateDatabase();
        var audits = await observer.SupportAccessAuditEvents.AsNoTracking().ToListAsync();
        await Assert.That(audits.Count).IsEqualTo(force ? 1 : 0);
        if (force) await Assert.That(audits.Single().ActorUserId).IsEqualTo(fixture.Binding.LocalSubjectId);
    }

    [Test]
    public async Task UnforwardedExpiredOwnStopAndRepeatedStopPreserveLifecycleRules()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        Guid sessionId = await Seed(fixture, session: true);
        var original = await ReadSession(fixture, sessionId);
        fixture.Clock.Advance(original.ExpiresAtUtc.AddTicks(1) - fixture.Clock.GetUtcNow());
        foreach (int attempt in new[] { 1, 2 })
        {
            await using var request = new Request(fixture.Host.Services);
            await request.Authenticate(await fixture.SignInAsync());
            var result = await ActivatorUtilities.CreateInstance<StopSupportAccessSessionCommandHandler>(request.Services)
                .ExecuteAsync(new() { SessionId = sessionId }, default);
            await Assert.That(result.IsSuccess).IsTrue();
            await Assert.That(result.Session!.StatusId).IsEqualTo((int)SupportAccessSessionStatusEnum.Expired);
        }
        await using var observer = fixture.Native.CreateDatabase();
        var audit = await observer.SupportAccessAuditEvents.AsNoTracking().SingleAsync(row => row.SupportAccessSessionId == sessionId);
        await Assert.That(audit.Outcome).IsEqualTo("expired");
        await Assert.That((await ReadSession(fixture, sessionId)).EndReasonId).IsEqualTo((int)SupportAccessEndReasonEnum.Expired);
    }

    [Test]
    public async Task IndependentIdentityTopologyCannotBorrowAnApplicationOwner()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(topology: IdentityDatabaseTopology.External);
        Guid sessionId = await Seed(fixture, session: true);
        await using var request = new Request(fixture.Host.Services);
        await request.Authenticate(await fixture.SignInAsync());
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            ActivatorUtilities.CreateInstance<StopSupportAccessSessionCommandHandler>(request.Services)
                .ExecuteAsync(new() { SessionId = sessionId }, default));
        await Assert.That((await ReadSession(fixture, sessionId)).StatusId).IsEqualTo((int)SupportAccessSessionStatusEnum.Active);
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.SupportAccessAuditEvents.AsNoTracking().CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task EqualDatabaseThroughAnotherPhysicalOwnerCannotAcquireOriginalSources()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        Guid sessionId = await Seed(fixture, session: true);
        await using var request = new Request(fixture.Host.Services);
        await request.Authenticate(await fixture.SignInAsync());
        var command = new StopSupportAccessSessionCommand { SessionId = sessionId };
        var prepared = await request.Services.GetRequiredService<IHumanSupportAdministrationPreparation>()
            .PrepareAsync(command, default);
        await using var foreign = fixture.Native.CreateDatabase();
        var otherOwner = (ISerializableAttemptOwner)new EfCoreUnitOfWork(foreign);
        await Assert.ThrowsAsync<InvalidOperationException>(() => otherOwner.ExecuteAsync(async token =>
        {
            await prepared.AcquireAsync(command, token);
            return true;
        }));
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.SupportAccessAuditEvents.AsNoTracking().CountAsync()).IsEqualTo(0);
        await Assert.That((await ReadSession(fixture, sessionId)).StatusId).IsEqualTo((int)SupportAccessSessionStatusEnum.Active);
    }
}
