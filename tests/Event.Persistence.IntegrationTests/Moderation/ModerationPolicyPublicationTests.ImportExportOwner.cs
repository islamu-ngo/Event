using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Application.Features.ConfigurationManifest.Moderation;
using Explore.Application.Features.ConfigurationManifest.Requests.Queries;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure.Tests.Fixtures;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using ISLAMU.Wire.Contracts.ConfigurationPortability;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationPolicyPublicationTests
{
    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    [Arguments(5)]
    [Arguments(6)]
    public async Task Tenant_session_preview_keeps_tenant_authority_and_enforces_session_lifetime_through_delivery(int expiryPhase)
    {
        await using var database = await Database.CreateAsync();
        var foreign = await PublishExportTenantAsync(database);
        await database.PublishAsync(database.Request());
        await DemoteExportAdministratorAsync(database);
        await using (var changed = database.Context())
        {
            changed.TenantContext = new ExportTenantContext(foreign.TenantId);
            changed.ModerationTrustDomainId = database.Scope.TrustDomainId;
            await changed.Set<ModerationPolicyPublicationPlan>().ExecuteDeleteAsync();
        }

        await using var context = database.Context();
        await SeedExportPaidPolicyAsync(context);
        var reader = CurrentExportReader(database, context);
        var artifact = await reader.ReadTenantAsync(database.TenantId,
            ConfigurationManifestExportView.Overrides, CancellationToken.None);
        var clock = new ImportExportOwnerClock();
        var protection = new EphemeralDataProtectionProvider();
        await using var human = ImportHuman(database, context, protection, clock);
        await human.InitializeAsync();
        await human.PrepareAsync();
        var service = human.Services.GetRequiredService<ConfigurationImportSessionApplicationService>();
        var created = await service.CreateTenantAsync(database.TenantId, artifact.Utf8Json, CancellationToken.None);
        var preview = await service.PreviewTenantAsync(database.TenantId, created.SessionId, created.AccessToken,
            new ConfigurationImportPreviewRequest
            {
                SelectedSectionKeys = [ModerationConfigurationImportSections.Tenant],
                Mappings = new Dictionary<string, string>(),
                ApplyMode = ConfigurationImportApplyMode.PreviewOnly,
                GrantedApprovalCodes = []
            }, CancellationToken.None);

        await Assert.That(preview.IsApplyReady).IsTrue();
        await Assert.That(preview.Items.Single(item => item.SectionKey == ModerationConfigurationImportSections.Tenant)
            .Category).IsEqualTo(ConfigurationImportPreviewCategory.Unchanged);
        await Assert.That(context.TenantFilterTenantId).IsEqualTo(database.TenantId);
        var retained = await ReadImportSessionAsync(context, created.SessionId,
            ConfigurationImportTarget.ForTenant(database.TenantId));
        await Assert.That(retained.ModerationReservation).IsNotNull();
        var retainedBytes = await new ConfigurationImportArtifactRepository(context, protection)
            .ReadAsync(retained.Artifact.Handle, CancellationToken.None);
        await Assert.That(retainedBytes.ToArray()).IsEquivalentTo(artifact.Utf8Json.ToArray());
        await Assert.That(preview.Moderation).IsEmpty();
        if (expiryPhase == 6)
            await service.CancelTenantAsync(database.TenantId, created.SessionId,
                created.AccessToken, CancellationToken.None);
        await using var disclosure = preview.Disclosure!;
        bool prepared = false, delivered = false, completionAttempted = false;
        async Task DiscloseAsync() => await disclosure.DiscloseAsync(async (view, _) =>
        {
            prepared = true;
            var destination = view.Moderation.Single();
            await Assert.That(destination.SectionKey).IsEqualTo(ModerationConfigurationImportSections.Tenant);
            await Assert.That(destination.CanApply).IsTrue();
            if (expiryPhase == 1) clock.Current = new DateTimeOffset(created.ExpiresAt);
            return ReadOnlyMemory<byte>.Empty;
        }, async (_, token) =>
        {
            delivered = true;
            if (expiryPhase is 2 or 3) clock.Current = new DateTimeOffset(created.ExpiresAt);
            if (expiryPhase == 3) await Task.Delay(Timeout.InfiniteTimeSpan, token);
        }, async token =>
        {
            completionAttempted = true;
            await Assert.That(context.Database.CurrentTransaction).IsNull();
            if (expiryPhase is 4 or 5) clock.Current = new DateTimeOffset(created.ExpiresAt);
            if (expiryPhase == 5) await Task.Delay(Timeout.InfiniteTimeSpan, token);
        }, CancellationToken.None);
        if (expiryPhase == 6)
            await Assert.That(DiscloseAsync).Throws<ConfigurationImportSessionException>();
        else if (expiryPhase != 0)
            await Assert.That(DiscloseAsync).Throws<OperationCanceledException>();
        else
            await DiscloseAsync();
        await Assert.That(prepared).IsEqualTo(expiryPhase != 6);
        await Assert.That(delivered).IsEqualTo(expiryPhase is not (1 or 6));
        await Assert.That(completionAttempted).IsEqualTo(expiryPhase is 0 or 4 or 5);
        await Assert.That(DiscloseAsync).Throws<InvalidOperationException>();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Current_artifact_read_participates_in_original_import_transaction_without_starting_another(bool tenant)
    {
        await using var database = await Database.CreateAsync();
        await database.PublishAsync(database.Request());
        await using var context = database.Context();
        await SeedExportPaidPolicyAsync(context);
        var reader = CurrentExportReader(database, context);
        var unit = new EfCoreUnitOfWork(context);
        var artifact = await unit.ExecuteSerializableAsync(async token =>
        {
            var original = context.Database.CurrentTransaction!.TransactionId;
            var bytes = tenant
                ? (await reader.ReadTenantInCurrentTransactionAsync(database.TenantId,
                    ConfigurationManifestExportView.Overrides, token)).Utf8Json
                : (await reader.ReadInCurrentTransactionAsync(
                    ConfigurationManifestExportView.Overrides, token)).Utf8Json;
            await Assert.That(context.Database.CurrentTransaction!.TransactionId).IsEqualTo(original);
            await Assert.That(context.ChangeTracker.HasChanges()).IsFalse();
            return bytes;
        });

        var parser = new ConfigurationImportArtifactParser();
        var definition = tenant
            ? parser.ParseTenantPackage(artifact).Package.Spec.Moderation
            : parser.Parse(artifact).Manifest.Spec.Tenants
                .Single(row => row.Metadata.Name == $"publication-{database.TenantId:N}").Spec.Moderation;
        await Assert.That(definition).IsNotNull();
        await database.AssertCountsAsync(1, 1, 2);
    }

    private static CurrentHumanPrincipalFixture ImportHuman(Database database, ExploreDbContext context,
        IDataProtectionProvider protection, TimeProvider clock,
        Action<IServiceCollection>? configureServices = null) => new(database.AdminId, database.TenantId,
        borrowedContext: context, configureServices: services =>
        {
            services.AddSingleton(database.Inventory);
            services.AddSingleton(protection);
            configureServices?.Invoke(services);
        }, timeProvider: clock);

    private static async Task<ConfigurationImportSession> ReadImportSessionAsync(
        ExploreDbContext context, Guid sessionId, ConfigurationImportTarget target) =>
        await new EfCoreUnitOfWork(context).ExecuteSerializableAsync(async token =>
            await new ConfigurationImportSessionRepository(context).GetForUpdateAsync(sessionId, target, token)
                ?? throw new InvalidOperationException("The original import session must be retained."));

    private static async Task GrantImportTenantAdministratorAsync(Database database, ExploreDbContext context)
    {
        var membership = await context.TenantUsers.SingleAsync(row => row.UserId == database.AdminId);
        var role = await context.Roles.SingleAsync(row => row.Id == (int)RoleEnum.TenantAdmin);
        context.TenantUserRoleGrants.Add(new TenantUserRoleGrant
        {
            Id = Guid.CreateVersion7(),
            TenantId = database.TenantId,
            Tenant = null!,
            TenantUserId = membership.Id,
            TenantUser = membership,
            RoleId = role.Id,
            Role = role,
            RoleScopeId = (int)RoleScopeEnum.Tenant,
            GrantedAt = Now.UtcDateTime
        });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
    }

    private sealed class ImportExportOwnerClock : TimeProvider
    {
        private DateTimeOffset current = Now;
        private readonly List<PreviewDeadlineTimer> timers = [];
        public DateTimeOffset Current
        {
            get => current;
            set
            {
                current = value;
                foreach (var timer in timers.ToArray()) timer.FireIfDue();
            }
        }
        public override DateTimeOffset GetUtcNow() => Current;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new PreviewDeadlineTimer(this, callback, state);
            timers.Add(timer);
            timer.Change(dueTime, period);
            return timer;
        }

        private sealed class PreviewDeadlineTimer(ImportExportOwnerClock clock, TimerCallback callback, object? state) : ITimer
        {
            private DateTimeOffset? due;
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                if (period != Timeout.InfiniteTimeSpan) throw new NotSupportedException("Preview deadlines are one-shot.");
                due = dueTime == Timeout.InfiniteTimeSpan ? null : clock.Current + dueTime;
                return true;
            }
            public void FireIfDue()
            {
                if (due is not { } at || clock.Current < at) return;
                due = null;
                callback(state);
            }
            public void Dispose() { due = null; clock.timers.Remove(this); }
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
