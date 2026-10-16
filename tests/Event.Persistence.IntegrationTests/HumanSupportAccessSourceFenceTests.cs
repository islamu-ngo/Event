using System.Data;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Authorization;
using Explore.Application.Constants;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.Users.Requests.Queries;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Infrastructure.SupportAccess;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Persistence.IntegrationTests;

public sealed class HumanSupportAccessSourceFenceTests
{
    [Test]
    public async Task FinalPersistedGovernanceAndSessionOverrideCachedPositiveContext()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        var session = await SeedAsync(fixture);
        var (subject, forwarding, service) = await CaptureAsync(fixture, session.Id.ToString());
        var source = new HumanSupportAccessSourceFence(fixture.Context, subject, forwarding,
            PolicySourceSetPurpose.RuntimeTenant);
        var principal = new HumanPrincipalSourceFence(fixture.Context, subject, PolicySourceSetPurpose.RuntimeTenant);
        var sets = new PolicySourceSetFence(fixture.Context, PolicySourceSetPurpose.RuntimeTenant);
        var observed = await source.ObserveAsync();
        await Assert.That(HumanSupportAccessProjection.Context(observed, session.StartedAtUtc).AllowsWrites).IsTrue();
        await Assert.That(HumanSupportAccessProjection.BoundaryDenialReason(observed, session.StartedAtUtc,
            ResourceKinds.Event, "update", new Dictionary<string, object> { ["tenantId"] = fixture.TenantId })).IsNull();
        await using var owner = await fixture.Context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var setLease = await sets.AcquireAsync();
        var held = await source.AcquireAsync(setLease, await principal.AcquireAsync(setLease));
        await fixture.Context.SystemSettings.Where(row => row.SettingKey == GovernanceSettingKeys.SupportAccess.AllowWriteMode)
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.Value, "false"));
        await Assert.That((await service.GetCurrentAsync()).AllowsWrites).IsTrue();
        await Assert.That(HumanSupportAccessProjection.Context(await held.ValidateAsync(), session.StartedAtUtc).IsActive).IsFalse();
        await Assert.That(HumanSupportAccessProjection.Context(held.Facts, session.StartedAtUtc).AllowsWrites).IsTrue();
        await fixture.Context.SystemSettings.Where(row => row.SettingKey == GovernanceSettingKeys.SupportAccess.AllowWriteMode)
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.Value, "true"));
        session.Stop(session.StartedAtUtc.AddSeconds(1));
        // Tracked changes are not committed-source observations.
        await Assert.That(HumanSupportAccessProjection.Context(await held.ValidateAsync(), session.StartedAtUtc.AddMinutes(1)).IsActive).IsTrue();
        await fixture.Context.SaveChangesAsync();
        await Assert.That(HumanSupportAccessProjection.Context(await held.ValidateAsync(), session.StartedAtUtc.AddMinutes(1)).IsActive).IsFalse();
        await owner.RollbackAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => held.ValidateAsync());
    }

    [Test]
    public async Task OriginalLifetimeReadOnlyCompatibilityAndExactTenantArePreserved()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        var session = await SeedAsync(fixture, SupportAccessModeEnum.ReadOnly);
        var (subject, forwarding, _) = await CaptureAsync(fixture, session.Id.ToString());
        var facts = await new HumanSupportAccessSourceFence(fixture.Context, subject, forwarding,
            PolicySourceSetPurpose.RuntimeTenant).ObserveAsync();
        var tenant = new Dictionary<string, object> { ["tenantId"] = fixture.TenantId };
        await Assert.That(HumanSupportAccessProjection.Context(facts, session.StartedAtUtc.AddTicks(-1)).IsActive).IsFalse();
        await Assert.That(HumanSupportAccessProjection.Context(facts, session.StartedAtUtc).IsActive).IsTrue();
        await Assert.That(HumanSupportAccessProjection.Context(facts, session.ExpiresAtUtc).IsActive).IsFalse();
        await Assert.That(HumanSupportAccessProjection.BoundaryDenialReason(facts, session.StartedAtUtc,
            ResourceKinds.Event, "view", tenant)).IsNull();
        await Assert.That(HumanSupportAccessProjection.BoundaryDenialReason(facts, session.StartedAtUtc,
            ResourceKinds.Event, "update", tenant)).IsEqualTo("support_access_read_only");
        await Assert.That(HumanSupportAccessProjection.BoundaryDenialReason(facts, session.StartedAtUtc,
            ResourceKinds.Event, "anything:view-delivery", tenant)).IsNull();
        tenant["tenantId"] = Guid.CreateVersion7();
        await Assert.That(HumanSupportAccessProjection.BoundaryDenialReason(facts, session.StartedAtUtc,
            ResourceKinds.Event, "view", tenant)).IsEqualTo("support_access_target_tenant_mismatch");
        await Assert.That(HumanSupportAccessProjection.BoundaryDenialReason(facts, session.StartedAtUtc,
            ResourceKinds.Event, "view", null)).IsEqualTo("support_access_missing_tenant_context");
        await Assert.That(HumanSupportAccessProjection.BoundaryDenialReason(facts, session.ExpiresAtUtc,
            ResourceKinds.SupportAccessSession, "stop", tenant)).IsNull();
    }

    [Test]
    public async Task MissingSettingsUseOriginalDefaultsAndMalformedValuesDoNotBecomeAuthority()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        var session = await SeedAsync(fixture);
        var (subject, forwarding, _) = await CaptureAsync(fixture, session.Id.ToString());
        var source = new HumanSupportAccessSourceFence(fixture.Context, subject, forwarding, PolicySourceSetPurpose.RuntimeTenant);
        await fixture.Context.SystemSettings.Where(row => row.SettingKey == GovernanceSettingKeys.SupportAccess.Enabled)
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.Value, "not-a-boolean"));
        await Assert.That(HumanSupportAccessProjection.Context(await source.ObserveAsync(), session.StartedAtUtc).IsActive).IsFalse();
        await fixture.Context.SystemSettings.Where(row => row.SettingKey == GovernanceSettingKeys.SupportAccess.Enabled).ExecuteDeleteAsync();
        await Assert.That(HumanSupportAccessProjection.Context(await source.ObserveAsync(), session.StartedAtUtc).IsActive).IsFalse();
    }

    [Test]
    [Arguments(null, false)]
    [Arguments("invalid-session", true)]
    public async Task OnlyCapturedValidatedForwardingCanSelectSession(string? header, bool forwarded)
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        var session = await SeedAsync(fixture);
        var (subject, observation, _) = await CaptureAsync(fixture, header);
        var source = new HumanSupportAccessSourceFence(fixture.Context, subject, observation, PolicySourceSetPurpose.RuntimeTenant);
        var facts = await source.ObserveAsync();
        await Assert.That(facts.Sessions.Length).IsEqualTo(0);
        await Assert.That(HumanSupportAccessProjection.Context(facts, session.StartedAtUtc).WasForwarded).IsEqualTo(forwarded);
        await Assert.That(HumanSupportAccessProjection.BoundaryDenialReason(facts, session.StartedAtUtc,
            ResourceKinds.Event, "update", new Dictionary<string, object> { ["tenantId"] = fixture.TenantId }))
            .IsEqualTo(forwarded ? "support_access_inactive" : null);
    }

    [Test]
    public async Task EmptySourceStillExcludesWritersAndRejectsObservationInsideOwner()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        var (subject, forwarding, _) = await CaptureAsync(fixture, null);
        var source = new HumanSupportAccessSourceFence(fixture.Context, subject, forwarding, PolicySourceSetPurpose.RuntimeTenant);
        var principal = new HumanPrincipalSourceFence(fixture.Context, subject, PolicySourceSetPurpose.RuntimeTenant);
        var sets = new PolicySourceSetFence(fixture.Context, PolicySourceSetPurpose.RuntimeTenant);
        await using var owner = await fixture.Context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var setLease = await sets.AcquireAsync();
        var held = await source.AcquireAsync(setLease, await principal.AcquireAsync(setLease));
        await Assert.ThrowsAsync<InvalidOperationException>(() => source.ObserveAsync());
        await using var writer = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = fixture.DatabasePath,
            Pooling = false
        }.ToString());
        await writer.OpenAsync();
        await Assert.That(SQLitePCL.raw.sqlite3_exec(writer.Handle, "BEGIN IMMEDIATE")).IsEqualTo(SQLitePCL.raw.SQLITE_BUSY);
        await Assert.That((await held.ValidateAsync()).Sessions.Length).IsEqualTo(0);
        await owner.RollbackAsync();
        await using var replacement = await fixture.Context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await Assert.ThrowsAsync<InvalidOperationException>(() => held.ValidateAsync());
        await replacement.RollbackAsync();
    }

    internal static async Task<SupportAccessSession> SeedAsync(EventVisitorCapabilitySqliteFixture fixture,
        SupportAccessModeEnum mode = SupportAccessModeEnum.Write)
    {
        foreach (string key in new[] { GovernanceSettingKeys.SupportAccess.Enabled, GovernanceSettingKeys.SupportAccess.AllowWriteMode })
        {
            var existing = await fixture.Context.SystemSettings.SingleOrDefaultAsync(row => row.SettingKey == key);
            if (existing is null)
                fixture.Context.SystemSettings.Add(new SystemSetting
                {
                    Id = Guid.CreateVersion7(),
                    SettingKey = key,
                    Value = "true",
                    ValueType = SettingValueType.Boolean
                });
            else existing.Value = "true";
        }
        var start = DateTimeOffset.UtcNow.AddMinutes(-1);
        var session = SupportAccessSession.Start(fixture.UserId, fixture.TenantId, mode,
            "source-regression", "Persisted support source", "fixture", start, start.AddHours(1));
        fixture.Context.SupportAccessSessions.Add(session);
        await fixture.Context.SaveChangesAsync();
        return session;
    }

    internal static async Task<(HumanCommitSubject Subject, HumanSupportForwardingObservation Forwarding,
        SupportAccessSessionService Service)> CaptureAsync(EventVisitorCapabilitySqliteFixture fixture, string? header)
    {
        var http = fixture.Services.GetRequiredService<IHttpContextAccessor>();
        if (header is not null) http.HttpContext!.Request.Headers[SupportAccessHeaderNames.SessionId] = header;
        var tenant = fixture.Services.GetRequiredService<ITenantContextAccessor>();
        tenant.SetTenant(fixture.TenantId);
        var subject = await HumanCommitSubject.ResolveAsync(http.HttpContext!.User,
            fixture.Services.GetRequiredService<IQueryHandler<ResolveCurrentUserIdByIdentityRequest, Guid?>>(), default)
            ?? throw new InvalidOperationException("The persisted original human is required.");
        var service = new SupportAccessSessionService(http, fixture.Services.GetRequiredService<IAdminContext>(), tenant,
            fixture.Services.GetRequiredService<IHierarchicalSettingsResolver>(), new SupportAccessSessionRepository(fixture.Context));
        return (subject, await HumanSupportForwardingObservation.CaptureAsync(subject, service, tenant), service);
    }
}
