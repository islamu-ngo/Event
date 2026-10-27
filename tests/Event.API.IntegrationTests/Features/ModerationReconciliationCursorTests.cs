using System.Text.Json;
using Explore.API.Scheduling;
using Explore.Application.Configuration;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Moderation;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Scheduling;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Contracts.Services;
using Explore.Application.Services;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Domain.Enums;
using ISLAMU.Moderation.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Quartz;

namespace Event.API.IntegrationTests.Features;

public sealed class ModerationReconciliationCursorTests
{
    private static readonly string[] CursorKeys =
    [
        "last-scanned-sanction", "last-scanned-effect",
        "last-scanned-notice-approval", "last-scanned-resource-quarantine",
        "last-scanned-schedule-risk-review"
    ];

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Reconciliation_batch_owns_the_discovered_requests_before_the_caller_reuses_its_list(bool replace)
    {
        var instance = AuthorityScope.Instance(
            AuthorityScope.Solution(AuthorityScope.TrustDomain(Guid.CreateVersion7()), Guid.CreateVersion7()),
            Guid.CreateVersion7());
        var tenantId = Guid.CreateVersion7();
        var target = new ModerationReconciliationTarget(
            Guid.CreateVersion7(), tenantId, Guid.CreateVersion7(),
            SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), instance),
            AuthorityScope.Tenant(instance, tenantId), Guid.CreateVersion7(), 1,
            Guid.CreateVersion7(), 1, Guid.CreateVersion7(), 1,
            new DateTimeOffset(2026, 10, 12, 12, 0, 0, TimeSpan.Zero));
        var discovered = new List<ModerationReconciliationTarget> { target };
        var batch = replace
            ? new ModerationReconciliationBatch([], null) with { Targets = discovered, LastScannedSanctionId = target.SanctionId }
            : new ModerationReconciliationBatch(discovered, target.SanctionId);
        discovered.Clear();

        await Assert.That(batch.Targets.Single()).IsEqualTo(target);
        await Assert.That(batch.LastScannedSanctionId).IsEqualTo(target.SanctionId);
        using var retained = JsonDocument.Parse(JsonSerializer.Serialize(batch));
        var retainedTarget = retained.RootElement.GetProperty(nameof(batch.Targets))[0];
        await Assert.That(retainedTarget.GetProperty(nameof(target.DecisionId)).GetGuid()).IsEqualTo(target.DecisionId);
        await Assert.That(retainedTarget.GetProperty(nameof(target.SanctionId)).GetGuid()).IsEqualTo(target.SanctionId);
        await Assert.That(retained.RootElement.GetProperty(nameof(batch.LastScannedSanctionId)).GetGuid())
            .IsEqualTo(target.SanctionId);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Effect_recovery_batch_owns_the_original_pointers_and_scan_progress(bool replace)
    {
        var target = new ModerationEffectRecoveryTarget(Guid.CreateVersion7(),
            new DateTimeOffset(2026, 10, 12, 12, 0, 0, TimeSpan.Zero));
        var discovered = new List<ModerationEffectRecoveryTarget> { target };
        var batch = replace
            ? new ModerationEffectRecoveryBatch([], null) with { Targets = discovered, LastScannedEffectId = target.EffectId }
            : new ModerationEffectRecoveryBatch(discovered, target.EffectId);
        discovered.Clear();

        await Assert.That(batch.Targets.Single()).IsEqualTo(target);
        await Assert.That(batch.LastScannedEffectId).IsEqualTo(target.EffectId);
        var retained = JsonSerializer.Deserialize<ModerationEffectRecoveryBatch>(JsonSerializer.Serialize(batch))!;
        await Assert.That(retained.Targets.Single()).IsEqualTo(target);
        await Assert.That(retained.LastScannedEffectId).IsEqualTo(target.EffectId);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Tenant_switch_cannot_reuse_foreign_progress_after_an_interrupted_pass(bool afterCleanup)
    {
        await using var fixture = new Fixture();
        var data = Data(Guid.CreateVersion7());
        fixture.Secrets.Available = afterCleanup;
        fixture.FailDiscovery = afterCleanup;
        await Assert.That(async () => await fixture.Job.Execute(Context(data)))
            .Throws<InvalidOperationException>();

        await Assert.That(data.GetString("cursor-tenant")).IsEqualTo(fixture.TenantId.ToString("N"));
        foreach (var key in CursorKeys)
            await Assert.That(data.GetString(key)).IsEqualTo(
                afterCleanup && key == "last-scanned-notice-approval"
                    ? fixture.CleanupCursor.ToString("N")
                    : afterCleanup && key == "last-scanned-schedule-risk-review"
                        ? fixture.ScheduleRiskCursor.ToString("N") : string.Empty);

        // The next fire uses the mutated durable job data, not a newly constructed map.
        fixture.Secrets.Available = true;
        fixture.FailDiscovery = false;
        await fixture.Job.Execute(Context(data));
        await Assert.That(fixture.SanctionCursors.Count).IsEqualTo(afterCleanup ? 2 : 1);
        foreach (var cursor in fixture.SanctionCursors)
            await Assert.That(cursor).IsNull();
        foreach (var cursor in fixture.EffectCursors) await Assert.That(cursor).IsNull();
        foreach (var cursor in fixture.ReviewCursors) await Assert.That(cursor).IsNull();
        await Assert.That(fixture.ScheduleRiskCursors[0]).IsNull();
        if (afterCleanup)
            await Assert.That(fixture.ScheduleRiskCursors[1]).IsEqualTo(fixture.ScheduleRiskCursor);
        await Assert.That(fixture.NoticeCursors[0]).IsNull();
        if (afterCleanup)
            await Assert.That(fixture.NoticeCursors[1]).IsEqualTo(fixture.CleanupCursor);
    }

    [Test]
    public async Task Missing_maintenance_credential_cannot_retain_expired_narrative_payload()
    {
        await using var fixture = new Fixture();
        var expired = Guid.CreateVersion7();
        var active = Guid.CreateVersion7();
        fixture.Narratives[expired] = (DateTimeOffset.UnixEpoch.AddSeconds(-1), new byte[] { 0x42 });
        fixture.Narratives[active] = (DateTimeOffset.UnixEpoch.AddDays(1), new byte[] { 0x24 });
        await Assert.That(async () => await fixture.Job.Execute(Context(Data(fixture.TenantId))))
            .Throws<InvalidOperationException>();
        await Assert.That(fixture.Narratives.ContainsKey(expired)).IsFalse();
        await Assert.That(fixture.Narratives.ContainsKey(active)).IsTrue();
        await Assert.That(fixture.Narratives[active].Payload.Single()).IsEqualTo((byte)0x24);
    }

    [Test]
    public async Task Same_tenant_credential_failure_preserves_its_original_progress()
    {
        await using var fixture = new Fixture();
        var data = Data(fixture.TenantId);
        var original = CursorKeys.ToDictionary(key => key, key => data.GetString(key));
        await Assert.That(async () => await fixture.Job.Execute(Context(data)))
            .Throws<InvalidOperationException>();
        foreach (var key in CursorKeys)
            await Assert.That(data.GetString(key)).IsEqualTo(original[key]);
        await Assert.That(data.GetString("cursor-tenant")).IsEqualTo(fixture.TenantId.ToString("N"));
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Schedule_risk_cursor_advances_only_after_its_native_pass_is_available(bool available)
    {
        await using var fixture = new Fixture();
        fixture.Secrets.Available = true;
        fixture.ScheduleRiskAvailable = available;
        var data = Data(fixture.TenantId);
        var original = data.GetString("last-scanned-schedule-risk-review");
        await fixture.Job.Execute(Context(data));
        await Assert.That(fixture.ScheduleRiskCursors.Single()).IsEqualTo(Guid.Parse(original!));
        await Assert.That(data.GetString("last-scanned-schedule-risk-review")).IsEqualTo(
            available ? fixture.ScheduleRiskCursor.ToString("N") : original);
        await Assert.That(fixture.EffectCursors.Count).IsEqualTo(1);
    }

    [Test]
    [Arguments("PreFinding")]
    [Arguments("Narrative")]
    [Arguments("Notice")]
    [Arguments("Sanction")]
    [Arguments("Resource")]
    [Arguments("Risk")]
    [Arguments("Effect")]
    public async Task Failed_stage_preserves_its_progress_without_starving_independent_owners(string failedStage)
    {
        await using var fixture = new Fixture();
        fixture.Secrets.Available = true;
        fixture.BeforeStage = stage =>
        {
            if (stage == failedStage) throw new InvalidOperationException("Owner unavailable.");
        };
        var data = Data(fixture.TenantId);
        var original = CursorKeys.ToDictionary(key => key, key => data.GetString(key));

        await Assert.That(async () => await fixture.Job.Execute(Context(data)))
            .Throws<InvalidOperationException>();

        await Assert.That(string.Join(",", fixture.Stages))
            .IsEqualTo("PreFinding,Narrative,Notice,Sanction,Resource,Risk,Effect");
        foreach (var (stage, key, completedCursor) in new[]
        {
            ("Notice", "last-scanned-notice-approval", fixture.CleanupCursor.ToString("N")),
            ("Sanction", "last-scanned-sanction", string.Empty),
            ("Resource", "last-scanned-resource-quarantine", string.Empty),
            ("Risk", "last-scanned-schedule-risk-review", fixture.ScheduleRiskCursor.ToString("N")),
            ("Effect", "last-scanned-effect", string.Empty)
        })
            await Assert.That(data.GetString(key)).IsEqualTo(stage == failedStage ? original[key] : completedCursor);

        fixture.BeforeStage = null;
        await fixture.Job.Execute(Context(data));
        await Assert.That(data.GetString("last-scanned-sanction")).IsEqualTo(string.Empty);
        await Assert.That(data.GetString("last-scanned-effect")).IsEqualTo(string.Empty);
        await Assert.That(data.GetString("last-scanned-notice-approval")).IsEqualTo(fixture.CleanupCursor.ToString("N"));
        await Assert.That(data.GetString("last-scanned-resource-quarantine")).IsEqualTo(string.Empty);
        await Assert.That(data.GetString("last-scanned-schedule-risk-review")).IsEqualTo(fixture.ScheduleRiskCursor.ToString("N"));
    }

    [Test]
    public async Task Cancellation_stops_independent_passes_without_advancing_the_interrupted_cursor()
    {
        await using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        fixture.Secrets.Available = true;
        fixture.BeforeStage = stage =>
        {
            if (stage != "Resource") return;
            cancellation.Cancel();
            cancellation.Token.ThrowIfCancellationRequested();
        };
        var data = Data(fixture.TenantId);
        var original = CursorKeys.ToDictionary(key => key, key => data.GetString(key));

        await Assert.That(async () => await fixture.Job.Execute(Context(data, cancellation.Token)))
            .Throws<OperationCanceledException>();

        await Assert.That(string.Join(",", fixture.Stages)).IsEqualTo("PreFinding,Narrative,Notice,Sanction,Resource");
        foreach (var key in new[] { "last-scanned-resource-quarantine", "last-scanned-schedule-risk-review", "last-scanned-effect" })
            await Assert.That(data.GetString(key)).IsEqualTo(original[key]);
        await Assert.That(data.GetString("last-scanned-sanction")).IsEqualTo(string.Empty);
    }

    private static JobDataMap Data(Guid tenantId)
    {
        var data = new JobDataMap { ["cursor-tenant"] = tenantId.ToString("N") };
        foreach (var key in CursorKeys) data[key] = Guid.CreateVersion7().ToString("N");
        return data;
    }

    private static IJobExecutionContext Context(JobDataMap data, CancellationToken cancellationToken = default)
    {
        var detail = Substitute.For<IJobDetail>();
        detail.JobDataMap.Returns(data);
        var context = Substitute.For<IJobExecutionContext>();
        context.JobDetail.Returns(detail);
        context.CancellationToken.Returns(cancellationToken);
        return context;
    }

    /// <summary>
    /// Executes the actual job, credential authenticator and effect recovery service.
    /// Owning repositories are boundary substitutes so cursor recovery is independent
    /// of provider readiness; no substitute decides or finalizes a moderation effect.
    /// </summary>
    private sealed class Fixture : IAsyncDisposable
    {
        private readonly ServiceProvider services;
        public Guid TenantId { get; } = Guid.CreateVersion7();
        public Guid CleanupCursor { get; } = Guid.CreateVersion7();
        public Guid ScheduleRiskCursor { get; } = Guid.CreateVersion7();
        public bool ScheduleRiskAvailable { get; set; } = true;
        public SecretProvider Secrets { get; } = new();
        public bool FailDiscovery { get; set; }
        public Action<string>? BeforeStage { get; set; }
        public List<string> Stages { get; } = [];
        public List<Guid?> SanctionCursors { get; } = [];
        public List<Guid?> EffectCursors { get; } = [];
        public List<Guid?> NoticeCursors { get; } = [];
        public List<Guid?> ReviewCursors { get; } = [];
        public List<Guid?> ScheduleRiskCursors { get; } = [];
        public Dictionary<Guid, (DateTimeOffset ExpiresAt, byte[] Payload)> Narratives { get; } = [];
        public ModerationReconciliationJob Job { get; }

        public Fixture()
        {
            var options = new Settings(new() { Enabled = true, TenantId = TenantId, BatchSize = 10 });
            var keyId = ApiKeyHashing.CreateKeyId();
            var secret = ApiKeyHashing.CreateSecret();
            Secrets.Credential = ApiKeyHashing.FormatPersistedApiKey(keyId, secret);
            var keys = Substitute.For<IExternalApiKeyAuthenticationRepository>();
            keys.GetByKeyIdForAuthentication(keyId, Arg.Any<CancellationToken>()).Returns(new ExternalApiKey
            {
                Id = Guid.CreateVersion7(),
                Name = "Cursor recovery maintenance",
                KeyId = keyId,
                SecretHash = ApiKeyHashing.ComputeHash(secret),
                TenantId = TenantId,
                OwnerId = Guid.CreateVersion7(),
                OwnerType = ExternalApiKeyOwnerType.InstanceAdmin,
                Scopes = "events:read",
                ExternalApiKeyStatusId = (int)ExternalApiKeyStatusEnum.Active,
                ExternalApiKeyStatus = null!,
                ExternalApiKeyCreditPeriod = null!
            });
            var cleanup = Substitute.For<IModerationNoticeCleanupRepository>();
            cleanup.PruneAsync(Arg.Any<Guid?>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                NoticeCursors.Add(call.ArgAt<Guid?>(0));
                EnterStage("Notice");
                return new ModerationNoticeCleanupResult(ModerationNoticeCleanupOutcome.Applied, CleanupCursor);
            });
            var query = Substitute.For<IModerationReconciliationQuery>();
            query.ReadDueAsync(TenantId, Arg.Any<DateTimeOffset>(), 10, Arg.Any<Guid?>(),
                Arg.Any<CancellationToken>()).Returns(call =>
            {
                SanctionCursors.Add(call.ArgAt<Guid?>(3));
                EnterStage("Sanction");
                if (FailDiscovery) throw new InvalidOperationException("Discovery unavailable.");
                return new ModerationReconciliationBatch([], null);
            });
            query.ReadRecoverableEffectsAsync(TenantId, Arg.Any<DateTimeOffset>(), 10,
                Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                EffectCursors.Add(call.ArgAt<Guid?>(3));
                EnterStage("Effect");
                return new ModerationEffectRecoveryBatch([], null);
            });
            var duties = Substitute.For<IModerationResourceQuarantineDutyRepository>();
            duties.ExecutePassAsync(Arg.Any<Guid?>(), 10, Arg.Any<CancellationToken>()).Returns(call =>
            {
                ReviewCursors.Add(call.ArgAt<Guid?>(0));
                EnterStage("Resource");
                return new ModerationResourceReviewDutyBatch(true, null, []);
            });
            var scheduleRisk = Substitute.For<IModerationScheduleRiskReviewReconciliationRepository>();
            scheduleRisk.ReconcileScheduleRiskAsync(Arg.Any<Guid?>(), 10, Arg.Any<CancellationToken>()).Returns(call =>
            {
                ScheduleRiskCursors.Add(call.ArgAt<Guid?>(0));
                EnterStage("Risk");
                return new ModerationScheduleRiskReconciliationBatch(ScheduleRiskAvailable, ScheduleRiskCursor, []);
            });
            var narratives = Substitute.For<IModerationCaseNarrativeCustody>();
            narratives.PurgeExpiredAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                EnterStage("Narrative");
                var expired = Narratives.Where(row => row.Value.ExpiresAt <= DateTimeOffset.UnixEpoch)
                    .OrderBy(row => row.Key).Take(call.ArgAt<int>(0)).Select(row => row.Key).ToArray();
                foreach (var id in expired) Narratives.Remove(id);
                return expired.Length;
            });
            var preFinding = Substitute.For<IModerationPreFindingRepository>();
            preFinding.PurgeExpiredPreFindingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(_ =>
            {
                EnterStage("PreFinding");
                return 0;
            });
            services = new ServiceCollection()
                .AddScoped(_ => Substitute.For<ITenantContextAccessor>())
                .AddScoped(_ => Substitute.For<IMachinePrincipalExecutionAccessor>())
                .AddSingleton<IModerationCaseNarrativeCustody>(narratives)
                .AddSingleton<IModerationPreFindingRepository>(preFinding)
                .AddSingleton<IModerationNoticeCleanupRepository>(cleanup)
                .AddSingleton<IModerationReconciliationQuery>(query)
                .AddSingleton<IModerationResourceQuarantineDutyRepository>(duties)
                .AddSingleton<IModerationScheduleRiskReviewReconciliationRepository>(scheduleRisk)
                .AddSingleton(new ModerationReconciliationCredentialProvider(options, keys, Secrets, TimeProvider.System))
                .AddSingleton<IModerationEffectDispatcher>(Substitute.For<IModerationEffectDispatcher>())
                .AddSingleton(TimeProvider.System)
                .AddSingleton<Microsoft.Extensions.Logging.ILogger<ModerationEffectRecoveryService>>(
                    NullLogger<ModerationEffectRecoveryService>.Instance)
                .AddSingleton<ModerationEffectRecoveryService>()
                .BuildServiceProvider();
            Job = new(services, options, TimeProvider.System, NullLogger<ModerationReconciliationJob>.Instance);
        }

        public ValueTask DisposeAsync() => services.DisposeAsync();

        private void EnterStage(string stage)
        {
            Stages.Add(stage);
            BeforeStage?.Invoke(stage);
        }
    }

    private sealed class SecretProvider : IModerationReconciliationSecretProvider
    {
        public string Credential { get; set; } = string.Empty;
        public bool Available { get; set; }
        public ValueTask<string?> ReadAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Available ? Credential : null);
    }

    private sealed class Settings(ModerationReconciliationOptions options) : IOptionsMonitor<ModerationReconciliationOptions>
    {
        public ModerationReconciliationOptions CurrentValue => options;
        public ModerationReconciliationOptions Get(string? name) => options;
        public IDisposable? OnChange(Action<ModerationReconciliationOptions, string?> listener) => null;
    }
}
