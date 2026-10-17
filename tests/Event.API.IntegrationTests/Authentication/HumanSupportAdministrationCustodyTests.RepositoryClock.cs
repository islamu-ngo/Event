using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

public sealed partial class HumanSupportAdministrationCustodyTests
{
    [Test]
    [Arguments(0, false)]
    [Arguments(0, true)]
    [Arguments(330, false)]
    [Arguments(330, true)]
    public async Task RepositoryClockPreservesOffsetAndExclusiveTickDeadline(int offsetMinutes, bool expired)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await Seed(fixture, session: false);
        var now = new DateTimeOffset(fixture.Clock.GetUtcNow().Ticks
            - fixture.Clock.GetUtcNow().Ticks % TimeSpan.TicksPerSecond, TimeSpan.Zero).AddTicks(1234567);
        Guid id;
        await using (var seed = fixture.Native.CreateDatabase())
        {
            var session = SupportAccessSession.Start(fixture.Binding.LocalSubjectId, PlatformDefaults.DefaultTenantId,
                SupportAccessModeEnum.ReadOnly, "maintenance", "Exact clock", "clock",
                now.AddMinutes(-1).ToOffset(TimeSpan.FromMinutes(offsetMinutes)),
                now.AddTicks(expired ? 0 : 1).ToOffset(TimeSpan.FromMinutes(offsetMinutes)));
            id = session.Id;
            seed.SupportAccessSessions.Add(session);
            await seed.SaveChangesAsync();
        }
        await using var request = new Request(fixture.Host.Services);
        await request.Authenticate(await fixture.SignInAsync());
        var repository = request.Services.GetRequiredService<ISupportAccessSessionRepository>();
        await Assert.That(await repository.HasActiveSessionForActorAsync(fixture.Binding.LocalSubjectId, now)).IsEqualTo(!expired);
        await Assert.That((await repository.GetActiveForActorAsync(fixture.Binding.LocalSubjectId, now))?.Id)
            .IsEqualTo(expired ? (Guid?)null : id);
        await Assert.That((await repository.GetActiveOwnedSessionAsync(id, fixture.Binding.LocalSubjectId,
            PlatformDefaults.DefaultTenantId, now))?.Id).IsEqualTo(expired ? (Guid?)null : id);
    }

    [Test]
    public async Task RepositoryListsOrderByActualInstantBeforeApplyingTheirLimits()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await Seed(fixture, session: false);
        var at = new DateTimeOffset(fixture.Clock.GetUtcNow().Ticks
            - fixture.Clock.GetUtcNow().Ticks % TimeSpan.TicksPerSecond, TimeSpan.Zero).AddTicks(1234567);
        Guid newestSession, newestAudit;
        await using (var seed = fixture.Native.CreateDatabase())
        {
            var older = SupportAccessSession.Start(fixture.Binding.LocalSubjectId, PlatformDefaults.DefaultTenantId,
                SupportAccessModeEnum.ReadOnly, "maintenance", "Older", "clock",
                at.ToOffset(TimeSpan.FromHours(5.5)), at.AddMinutes(5));
            var newer = SupportAccessSession.Start(fixture.Binding.LocalSubjectId, PlatformDefaults.DefaultTenantId,
                SupportAccessModeEnum.ReadOnly, "maintenance", "Newer", "clock",
                at.AddTicks(1), at.AddMinutes(5));
            newestSession = newer.Id;
            var oldAudit = SupportAccessAuditEvent.CreateLifecycleEvent(older,
                SupportAccessAuditEventTypeEnum.Started, "started", at.ToOffset(TimeSpan.FromHours(5.5)));
            var newAudit = SupportAccessAuditEvent.CreateLifecycleEvent(older,
                SupportAccessAuditEventTypeEnum.Stopped, "stopped", at.AddTicks(1));
            newestAudit = newAudit.Id;
            seed.SupportAccessSessions.AddRange(older, newer);
            seed.SupportAccessAuditEvents.AddRange(oldAudit, newAudit);
            await seed.SaveChangesAsync();
        }
        await using var request = new Request(fixture.Host.Services);
        await request.Authenticate(await fixture.SignInAsync());
        var sessions = await request.Services.GetRequiredService<ISupportAccessSessionRepository>()
            .ListForTargetTenantAsync(PlatformDefaults.DefaultTenantId, 1);
        var audits = request.Services.GetRequiredService<ISupportAccessAuditEventRepository>();
        await Assert.That(sessions.Single().Id).IsEqualTo(newestSession);
        var tenantAudit = await audits.ListForTargetTenantAsync(PlatformDefaults.DefaultTenantId, 1);
        await Assert.That(tenantAudit.Single().Id).IsEqualTo(newestAudit);
        var context = request.Services.GetRequiredService<ExploreDbContext>();
        Guid sessionId = await context.SupportAccessAuditEvents.AsNoTracking()
            .Where(row => row.Id == newestAudit).Select(row => row.SupportAccessSessionId).SingleAsync();
        await Assert.That((await audits.ListForSessionAsync(sessionId, 1)).Single().Id).IsEqualTo(newestAudit);
    }
}
