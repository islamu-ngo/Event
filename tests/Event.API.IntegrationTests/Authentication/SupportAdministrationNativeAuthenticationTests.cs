using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.SupportAccess.Handlers.Commands;
using Explore.Application.Features.SupportAccess.Handlers.Queries;
using Explore.Application.Features.SupportAccess.Requests.Commands;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

[NotInParallel]
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
public sealed class SupportAdministrationNativeAuthenticationTests(PostgreSqlContainerFixture postgres)
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task GenuineRuntimeHumanOwnsLifecycleAndProtectedDisclosure(bool force)
    {
        Guid tenant = await Seed();
        await using var fixture = await TenantBrandingNativeAuthenticationFixture.CreateAsync(postgres, tenant);
        await fixture.InOriginalRequestAsync(async () =>
        {
            var start = await ActivatorUtilities.CreateInstance<StartSupportAccessSessionCommandHandler>(fixture.Services)
                .ExecuteAsync(Command(tenant), default);
            await Assert.That(start.IsSuccess).IsTrue();
            var session = start.Session!;
            await Assert.That(session.ActorUserId).IsEqualTo(fixture.Subject.PrincipalId);
            var current = await ActivatorUtilities.CreateInstance<GetCurrentSupportAccessSessionQueryHandler>(fixture.Services)
                .QueryAsync(new(), default);
            await Assert.That(current.Session!.Id).IsEqualTo(session.Id);
            // Exercise the actual forwarding service and its execute-only repository
            // on the original selected human, without inventing a cached context.
            var forwarded = await fixture.Services.GetRequiredService<ISupportAccessSessionService>()
                .ValidateForwardedSessionAsync(session.Id, fixture.Subject.PrincipalId, tenant, default);
            await Assert.That(forwarded.IsActive).IsTrue();
            var listed = await ActivatorUtilities.CreateInstance<ListSupportAccessSessionsQueryHandler>(fixture.Services)
                .QueryAsync(new() { TargetTenantId = tenant, Limit = 1 }, default);
            await Assert.That(listed.Items.Single().Id).IsEqualTo(session.Id);
            var ended = force
                ? await ActivatorUtilities.CreateInstance<ForceStopSupportAccessSessionCommandHandler>(fixture.Services)
                    .ExecuteAsync(new() { SessionId = session.Id, EndReasonText = "Native operator" }, default)
                : await ActivatorUtilities.CreateInstance<StopSupportAccessSessionCommandHandler>(fixture.Services)
                    .ExecuteAsync(new() { SessionId = session.Id, EndReasonText = "Native owner" }, default);
            await Assert.That(ended.IsSuccess).IsTrue();
            await Assert.That(ended.Session!.StatusId).IsEqualTo((int)(force
                ? SupportAccessSessionStatusEnum.Revoked : SupportAccessSessionStatusEnum.Stopped));
            var inactive = await ActivatorUtilities.CreateInstance<GetCurrentSupportAccessSessionQueryHandler>(fixture.Services)
                .QueryAsync(new(), default);
            await Assert.That(inactive.IsActive).IsFalse();
            var audit = await ActivatorUtilities.CreateInstance<GetSupportAccessAuditEventsQueryHandler>(fixture.Services)
                .QueryAsync(new() { TargetTenantId = tenant, SessionId = session.Id, Limit = 10 }, default);
            await Assert.That(audit.Items.Count).IsEqualTo(2);
            await Assert.That(audit.Items.Any(row => row.Outcome == (force ? "force_stopped" : "stopped"))).IsTrue();
            await Assert.That(audit.Items.All(row => row.SanitizedMetadataJson is null && row.TraceId is null)).IsTrue();
            await Assert.That(fixture.Owner.ChangeTracker.Entries<SupportAccessSession>().Any()).IsFalse();
            await Assert.That(fixture.Owner.Database.CurrentTransaction).IsNull();
            return true;
        });
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CompletedNativeSupportGroupStillRequiresTheOriginalDeadline(bool expire)
    {
        Guid tenant = await Seed();
        var clock = new ControlledClock();
        var terminal = new AfterSources(clock);
        await using var fixture = await TenantBrandingNativeAuthenticationFixture.CreateAsync(
            postgres, tenant, clock, terminal.Decorate);
        terminal.Expiry = expire ? fixture.Lifetime.ExpiresAt.Add(fixture.Lifetime.ClockSkew).AddTicks(1) : null;
        Guid? sessionId = await fixture.InOriginalRequestAsync(async () =>
        {
            var handler = ActivatorUtilities.CreateInstance<StartSupportAccessSessionCommandHandler>(fixture.Services);
            if (expire)
            {
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => handler.ExecuteAsync(Command(tenant), default));
                return (Guid?)null;
            }
            var result = await handler.ExecuteAsync(Command(tenant), default);
            await Assert.That(result.IsSuccess).IsTrue();
            return (Guid?)result.Session!.Id;
        });
        await using var observer = postgres.CreateDbContext();
        var sessions = await observer.SupportAccessSessions.AsNoTracking()
            .Where(row => row.ActorUserId == fixture.Subject.PrincipalId).ToArrayAsync();
        await Assert.That(sessions.Length).IsEqualTo(expire ? 0 : 1);
        if (sessionId is { } stored)
            await Assert.That(await observer.SupportAccessAuditEvents.AsNoTracking()
                .CountAsync(row => row.SupportAccessSessionId == stored)).IsEqualTo(1);
        await Assert.That(fixture.Owner.Database.CurrentTransaction).IsNull();
    }

    [Test]
    public async Task RepositoryConstructionDoesNotAcquireUnsupportedForwardingAuthority()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["IdentityDatabase:Topology"] = "external" }).Build());
        services.AddScoped(_ => postgres.CreateEnrolledTenantDbContext(new SelectedTenant(Guid.CreateVersion7())));
        services.AddScoped<ExploreDbContextExecutionLease>();
        services.AddHumanSupportAdministrationDataAdapters();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        // No forwarding request exists. External-topology/native admission must be
        // checked at the exact read, not make unrelated service construction fail.
        await Assert.That(scope.ServiceProvider.GetRequiredService<ISupportAccessSessionRepository>()).IsNotNull();
    }

    private async Task<Guid> Seed()
    {
        await using var setup = postgres.CreateDbContext();
        var tenant = new Tenant
        {
            Id = Guid.CreateVersion7(),
            FullName = "Support native custody",
            Slug = $"support-custody-{Guid.CreateVersion7():N}",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!
        };
        setup.Tenants.Add(tenant);
        foreach (string key in new[] { GovernanceSettingKeys.SupportAccess.Enabled,
                     GovernanceSettingKeys.SupportAccess.AllowWriteMode })
        {
            var setting = await setup.SystemSettings.SingleOrDefaultAsync(row => row.SettingKey == key);
            if (setting is null) setup.SystemSettings.Add(new SystemSetting { SettingKey = key, Value = "true" });
            else setting.Value = "true";
        }
        await setup.SaveChangesAsync();
        return tenant.Id;
    }

    private static StartSupportAccessSessionCommand Command(Guid tenant) => new()
    {
        TargetTenantId = tenant,
        Mode = SupportAccessModeEnum.ReadOnly,
        DurationMinutes = 10,
        ReasonCode = "maintenance",
        ReasonText = "Native original-owner support",
        TicketReference = "native-control"
    };

    private sealed record SelectedTenant(Guid TenantId) : Explore.Application.Contracts.Infrastructure.ITenantContext;
    private sealed class ControlledClock : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class AfterSources(ControlledClock clock)
    {
        internal DateTimeOffset? Expiry { get; set; }
        internal void Decorate(IServiceCollection services)
        {
            var descriptor = services.Single(row => row.ServiceType == typeof(IHumanActionCommitFinalizer));
            services.Remove(descriptor);
            services.AddScoped<IHumanActionCommitFinalizer>(provider => new Finalizer(
                (IHumanActionCommitFinalizer)(descriptor.ImplementationFactory?.Invoke(provider)
                    ?? ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!)), this, clock));
        }
        private sealed class Finalizer(IHumanActionCommitFinalizer inner, AfterSources terminal,
            ControlledClock clock) : IHumanActionCommitFinalizer
        {
            public void Register(Func<CancellationToken, Task> validateSources, Action<DateTimeOffset> validateAssignments) =>
                inner.Register(async token =>
                {
                    await validateSources(token);
                    if (terminal.Expiry is { } expiry) clock.Now = expiry;
                }, validateAssignments);
        }
    }
}
