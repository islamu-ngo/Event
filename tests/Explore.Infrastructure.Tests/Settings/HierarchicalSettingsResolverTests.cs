namespace Explore.Infrastructure.Tests.Settings;

using System.Collections.Immutable;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Application.Settings;
using Explore.Application.Settings.Groups;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Settings;
using Explore.Infrastructure.Services;
using Explore.Tests.Shared.Settings;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

public class HierarchicalSettingsResolverTests : IDisposable
{
    private readonly ISystemSettingRepository _systemRepo;
    private readonly ITenantSettingRepository _tenantRepo;
    private readonly IOrganizationSettingRepository _orgRepo;
    private readonly IGroupSettingRepository _groupRepo;
    private readonly IGroupTenantRepository _groupTenantRepo;
    private readonly IUserPreferenceRepository _userPrefRepo;
    private readonly ITenantContext _tenantContext;
    private readonly MemoryCache _cache;
    private readonly ILogger<HierarchicalSettingsResolver> _logger;
    private readonly HierarchicalSettingsResolver _resolver;

    public HierarchicalSettingsResolverTests()
    {
        _systemRepo = Substitute.For<ISystemSettingRepository>();
        _tenantRepo = Substitute.For<ITenantSettingRepository>();
        _orgRepo = Substitute.For<IOrganizationSettingRepository>();
        _groupRepo = Substitute.For<IGroupSettingRepository>();
        _groupTenantRepo = Substitute.For<IGroupTenantRepository>();
        _userPrefRepo = Substitute.For<IUserPreferenceRepository>();
        _tenantContext = Substitute.For<ITenantContext>();
        _cache = new MemoryCache(new MemoryCacheOptions());
        _logger = Substitute.For<ILogger<HierarchicalSettingsResolver>>();
        _resolver = new HierarchicalSettingsResolver(
            _systemRepo,
            _tenantRepo,
            _orgRepo,
            _groupRepo,
            _groupTenantRepo,
            _userPrefRepo,
            _tenantContext,
            ImmediateSettingMutationLock.Instance,
            _cache,
            _logger,
            emailSettingsWriter: RejectingEmailDeliverySettingsWriter.Instance);
    }

    public void Dispose()
    {
        _cache.Dispose();
        GC.SuppressFinalize(this);
    }

    private sealed class ImmediateSettingMutationLock : ISettingMutationLock
    {
        internal static readonly ImmediateSettingMutationLock Instance = new();

        public Task<T> ExecuteAsync<T>(
            string canonicalSettingKey,
            Func<CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken = default) => operation(cancellationToken);

        public Task<T> ExecuteManyAsync<T>(
            IEnumerable<string> canonicalSettingKeys,
            Func<CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken = default) => operation(cancellationToken);
    }

    private sealed class RejectingSettingMutationLock : ISettingMutationLock
    {
        internal static readonly RejectingSettingMutationLock Instance = new();

        public Task<T> ExecuteAsync<T>(
            string canonicalSettingKey,
            Func<CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken = default) =>
            throw new MutationLockReachedException();

        public Task<T> ExecuteManyAsync<T>(
            IEnumerable<string> canonicalSettingKeys,
            Func<CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken = default) =>
            throw new MutationLockReachedException();
    }

    private sealed class MutationLockReachedException : Exception;
    private sealed class EmailSettingsWriterReachedException : Exception;

    private sealed class RejectingEmailDeliverySettingsWriter : IEmailDeliverySettingsWriter
    {
        internal static readonly RejectingEmailDeliverySettingsWriter Instance = new();

        public Task<EmailDeliverySettingsWriteResult> ApplyAsync(
            ImmutableArray<EmailDeliverySettingMutation> mutations,
            Guid? actorUserId,
            CancellationToken cancellationToken = default) =>
            throw new EmailSettingsWriterReachedException();

        public Task<EmailDeliverySettingsWriteResult> DisableAsync(
            EmailDeliveryDisableConfirmation confirmation,
            CancellationToken cancellationToken = default) =>
            throw new EmailSettingsWriterReachedException();
    }

    public enum GuardedResolverMutation
    {
        Set,
        Remove,
        Lock,
        Unlock
    }

    // --- Basic resolution ---

    [Test]
    public async Task ResolveAsync_ReturnsSystemDefault_WhenNoTenantContext()
    {
        SetupSystemSettings(new SystemSetting
        {
            SettingKey = "email.smtp_port",
            Value = "587",
            ValueType = SettingValueType.Integer,
            IsLocked = false
        });

        var result = await _resolver.ResolveAsync<int>("email.smtp_port", new SettingContext());
        await Assert.That(result).IsEqualTo(587);
    }

    [Test]
    public async Task ResolveAsync_ReturnsSystemValue_WhenLocked()
    {
        SetupSystemSettings(new SystemSetting
        {
            SettingKey = "deployment.mode",
            Value = "\"MultiTenant\"",
            ValueType = SettingValueType.String,
            IsLocked = true
        });
        var tenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        SetupTenantSettings(tenantId, new TenantSetting
        {
            TenantId = tenantId,
            Tenant = null!,
            SettingKey = "deployment.mode",
            Value = "\"SingleTenant\""
        });

        var context = new SettingContext(TenantId: tenantId);
        var result = await _resolver.ResolveAsync<string>("deployment.mode", context);
        await Assert.That(result).IsEqualTo("MultiTenant");
    }

    [Test]
    public async Task ResolveAsync_ReturnsNull_WhenKeyNotFound()
    {
        _systemRepo.GetAllSettings(Arg.Any<string?>()).Returns(new List<SystemSetting>());

        var result = await _resolver.ResolveAsync<string>("nonexistent.key", new SettingContext());
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task ResolveAsync_InstanceLockStopsTenantOverride_InSingleTenantMode()
    {
        SetupSystemSettings(
            new SystemSetting
            {
                SettingKey = "deployment.mode",
                Value = "\"SingleTenant\"",
                ValueType = SettingValueType.String,
                IsLocked = false
            },
            new SystemSetting
            {
                SettingKey = "ai_assistant.enabled",
                Value = "true",
                ValueType = SettingValueType.Boolean,
                IsLocked = true
            });
        var tenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        SetupTenantSettings(tenantId, new TenantSetting
        {
            TenantId = tenantId,
            Tenant = null!,
            SettingKey = "ai_assistant.enabled",
            Value = "false"
        });

        var context = new SettingContext(TenantId: tenantId);
        var result = await _resolver.ResolveAsync<bool>("ai_assistant.enabled", context);
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task ResolveGroupAsync_WebhookInstanceLockStopsTenantConcurrencyOverride()
    {
        var tenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        SetupSystemSettings(new SystemSetting
        {
            SettingKey = GovernanceSettingKeys.WebhookDelivery.MaxConcurrentDeliveriesPerTenant,
            Value = "3",
            ValueType = SettingValueType.Integer,
            IsLocked = true
        });
        SetupTenantSettings(tenantId, new TenantSetting
        {
            TenantId = tenantId,
            Tenant = null!,
            SettingKey = GovernanceSettingKeys.WebhookDelivery.MaxConcurrentDeliveriesPerTenant,
            Value = "99"
        });

        var group = await _resolver.ResolveGroupAsync<WebhookDeliverySettingGroup>(
            new SettingContext(TenantId: tenantId));
        var metadata = await _resolver.ResolveWithMetadataAsync(
            GovernanceSettingKeys.WebhookDelivery.MaxConcurrentDeliveriesPerTenant,
            new SettingContext(TenantId: tenantId));

        await Assert.That(group.MaxConcurrentDeliveriesPerTenant).IsEqualTo(3);
        await Assert.That(metadata).IsNotNull();
        await Assert.That(metadata!.Source).IsEqualTo(SettingSource.SystemLocked);
        await Assert.That(metadata.IsLocked).IsTrue();
    }

    [Test]
    public async Task ResolveGroupAsync_WebhookInstanceOnlyGlobalLimitIgnoresStaleTenantRow()
    {
        var tenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        SetupSystemSettings(new SystemSetting
        {
            SettingKey = GovernanceSettingKeys.WebhookDelivery.MaxConcurrentDeliveries,
            Value = "12",
            ValueType = SettingValueType.Integer,
            IsLocked = false
        });
        SetupTenantSettings(tenantId, new TenantSetting
        {
            TenantId = tenantId,
            Tenant = null!,
            SettingKey = GovernanceSettingKeys.WebhookDelivery.MaxConcurrentDeliveries,
            Value = "200"
        });

        var group = await _resolver.ResolveGroupAsync<WebhookDeliverySettingGroup>(
            new SettingContext(TenantId: tenantId));
        var metadata = await _resolver.ResolveWithMetadataAsync(
            GovernanceSettingKeys.WebhookDelivery.MaxConcurrentDeliveries,
            new SettingContext(TenantId: tenantId));

        await Assert.That(group.MaxConcurrentDeliveries).IsEqualTo(12);
        await Assert.That(metadata).IsNotNull();
        await Assert.That(metadata!.Source).IsEqualTo(SettingSource.SystemDefault);
    }

    [Test]
    [Arguments(false, true, true, false)]
    [Arguments(false, false, true, true)]
    [Arguments(true, true, false, true)]
    [Arguments(true, false, false, false)]
    public async Task ResolveGroupAsync_AtprotoCapabilityHonorsInstanceLock(
        bool instanceValue,
        bool instanceLocked,
        bool tenantValue,
        bool expected)
    {
        var tenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        SetupSystemSettings(
            new SystemSetting
            {
                SettingKey = GovernanceSettingKeys.Federation.AtprotoEventsEnabled,
                Value = instanceValue ? "true" : "false",
                ValueType = SettingValueType.Boolean,
                IsLocked = instanceLocked
            },
            new SystemSetting
            {
                SettingKey = GovernanceSettingKeys.Federation.AtprotoEventValidationProfile,
                Value = "\"platform\"",
                ValueType = SettingValueType.String,
                IsLocked = instanceLocked
            });
        SetupTenantSettings(tenantId, new TenantSetting
        {
            TenantId = tenantId,
            Tenant = null!,
            SettingKey = GovernanceSettingKeys.Federation.AtprotoEventsEnabled,
            Value = tenantValue ? "true" : "false"
        });

        var group = await _resolver.ResolveGroupAsync<AtprotoFederationSettingGroup>(
            new SettingContext(TenantId: tenantId));

        await Assert.That(group.EventsEnabled).IsEqualTo(expected);
    }

    [Test]
    [Arguments("platform", true, "community_lexicon", "platform")]
    [Arguments("platform", false, "community_lexicon", "community_lexicon")]
    [Arguments("community_lexicon", true, "platform", "community_lexicon")]
    [Arguments("community_lexicon", false, "platform", "platform")]
    public async Task ResolveGroupAsync_AtprotoProfileHonorsInstanceLock(
        string instanceProfile,
        bool instanceLocked,
        string tenantProfile,
        string expected)
    {
        var tenantId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        SetupSystemSettings(
            new SystemSetting
            {
                SettingKey = GovernanceSettingKeys.Federation.AtprotoEventsEnabled,
                Value = "true",
                ValueType = SettingValueType.Boolean,
                IsLocked = true
            },
            new SystemSetting
            {
                SettingKey = GovernanceSettingKeys.Federation.AtprotoEventValidationProfile,
                Value = $"\"{instanceProfile}\"",
                ValueType = SettingValueType.String,
                IsLocked = instanceLocked
            });
        SetupTenantSettings(tenantId, new TenantSetting
        {
            TenantId = tenantId,
            Tenant = null!,
            SettingKey = GovernanceSettingKeys.Federation.AtprotoEventValidationProfile,
            Value = $"\"{tenantProfile}\""
        });

        var group = await _resolver.ResolveGroupAsync<AtprotoFederationSettingGroup>(
            new SettingContext(TenantId: tenantId));

        await Assert.That(group.EventValidationProfile).IsEqualTo(expected);
    }

    [Test]
    [Arguments(false, "downtime_only", true, true, "full", false, "downtime_only")]
    [Arguments(false, "downtime_only", false, true, "full", true, "full")]
    [Arguments(true, "full", true, false, "downtime_only", true, "full")]
    [Arguments(true, "full", false, false, "downtime_only", false, "downtime_only")]
    public async Task ResolveGroupAsync_AtprotoBackfillHonorsTenantOverrideAndInstanceLock(
        bool instanceEnabled,
        string instanceMode,
        bool instanceLocked,
        bool tenantEnabled,
        string tenantMode,
        bool expectedEnabled,
        string expectedMode)
    {
        const string enabledKey = "federation.atproto_events_backfill_enabled";
        const string modeKey = "federation.atproto_events_backfill_mode";
        var tenantId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        SetupSystemSettings(
            new SystemSetting
            {
                SettingKey = enabledKey,
                Value = instanceEnabled ? "true" : "false",
                ValueType = SettingValueType.Boolean,
                IsLocked = instanceLocked
            },
            new SystemSetting
            {
                SettingKey = modeKey,
                Value = $"\"{instanceMode}\"",
                ValueType = SettingValueType.String,
                IsLocked = instanceLocked
            });
        SetupTenantSettings(
            tenantId,
            new TenantSetting
            {
                TenantId = tenantId,
                Tenant = null!,
                SettingKey = enabledKey,
                Value = tenantEnabled ? "true" : "false"
            },
            new TenantSetting
            {
                TenantId = tenantId,
                Tenant = null!,
                SettingKey = modeKey,
                Value = $"\"{tenantMode}\""
            });

        var group = await _resolver.ResolveGroupAsync<AtprotoFederationSettingGroup>(
            new SettingContext(TenantId: tenantId));
        var enabledProperty = typeof(AtprotoFederationSettingGroup).GetProperty("EventsBackfillEnabled");
        var modeProperty = typeof(AtprotoFederationSettingGroup).GetProperty("EventsBackfillMode");

        await Assert.That(enabledProperty).IsNotNull();
        await Assert.That(modeProperty).IsNotNull();
        await Assert.That((bool)enabledProperty!.GetValue(group)!).IsEqualTo(expectedEnabled);
        await Assert.That((string)modeProperty!.GetValue(group)!).IsEqualTo(expectedMode);
    }

    [Test]
    public async Task ResolveGroupAsync_AtprotoBackfillRefreshesAfterTenantCacheInvalidation()
    {
        const string enabledKey = "federation.atproto_events_backfill_enabled";
        const string modeKey = "federation.atproto_events_backfill_mode";
        var tenantId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
        SetupSystemSettings(
            new SystemSetting
            {
                SettingKey = enabledKey,
                Value = "false",
                ValueType = SettingValueType.Boolean,
                IsLocked = false
            },
            new SystemSetting
            {
                SettingKey = modeKey,
                Value = "\"downtime_only\"",
                ValueType = SettingValueType.String,
                IsLocked = false
            });
        SetupTenantSettings(
            tenantId,
            new TenantSetting { TenantId = tenantId, Tenant = null!, SettingKey = enabledKey, Value = "false" },
            new TenantSetting { TenantId = tenantId, Tenant = null!, SettingKey = modeKey, Value = "\"downtime_only\"" });
        var context = new SettingContext(TenantId: tenantId);

        var initial = await _resolver.ResolveGroupAsync<AtprotoFederationSettingGroup>(context);
        SetupTenantSettings(
            tenantId,
            new TenantSetting { TenantId = tenantId, Tenant = null!, SettingKey = enabledKey, Value = "true" },
            new TenantSetting { TenantId = tenantId, Tenant = null!, SettingKey = modeKey, Value = "\"full\"" });
        var cached = await _resolver.ResolveGroupAsync<AtprotoFederationSettingGroup>(context);
        _resolver.InvalidateCache(SettingScope.Tenant, tenantId);
        var refreshed = await _resolver.ResolveGroupAsync<AtprotoFederationSettingGroup>(context);

        await Assert.That(initial.EventsBackfillEnabled).IsFalse();
        await Assert.That(initial.EventsBackfillMode).IsEqualTo("downtime_only");
        await Assert.That(cached.EventsBackfillEnabled).IsFalse();
        await Assert.That(cached.EventsBackfillMode).IsEqualTo("downtime_only");
        await Assert.That(refreshed.EventsBackfillEnabled).IsTrue();
        await Assert.That(refreshed.EventsBackfillMode).IsEqualTo("full");
    }

    // --- Batch resolution ---

    [Test]
    public async Task ResolveBatchAsync_ReturnsMultipleSettings()
    {
        SetupSystemSettings(
            new SystemSetting { SettingKey = "email.smtp_host", Value = "\"smtp.test.com\"", ValueType = SettingValueType.String, IsLocked = false },
            new SystemSetting { SettingKey = "email.smtp_port", Value = "587", ValueType = SettingValueType.Integer, IsLocked = false }
        );

        var results = await _resolver.ResolveBatchAsync(
            ["email.smtp_host", "email.smtp_port"],
            new SettingContext());

        await Assert.That(results.Count).IsEqualTo(2);
        await Assert.That(results[0].Key).IsEqualTo("email.smtp_host");
        await Assert.That(results[1].Key).IsEqualTo("email.smtp_port");
    }

    [Test]
    public async Task ResolveBatchAsync_WithEmptyKeys_ReturnsEmpty()
    {
        var results = await _resolver.ResolveBatchAsync([], new SettingContext());
        await Assert.That(results.Count).IsEqualTo(0);
    }

    // --- Metadata ---

    [Test]
    public async Task ResolveWithMetadata_ReturnsSourceSystemDefault()
    {
        SetupSystemSettings(new SystemSetting
        {
            SettingKey = "branding.display_name",
            Value = "\"ISLAMU\"",
            ValueType = SettingValueType.String,
            IsLocked = false,
            Description = "Brand name",
            Category = "Branding"
        });

        var result = await _resolver.ResolveWithMetadataAsync("branding.display_name", new SettingContext());
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Source).IsEqualTo(SettingSource.SystemDefault);
        await Assert.That(result.Category).IsEqualTo("Branding");
    }

    [Test]
    public async Task ResolveWithMetadata_ReturnsSourceSystemLocked_WhenLocked()
    {
        SetupSystemSettings(new SystemSetting
        {
            SettingKey = "deployment.mode",
            Value = "\"MultiTenant\"",
            ValueType = SettingValueType.String,
            IsLocked = true
        });

        var result = await _resolver.ResolveWithMetadataAsync("deployment.mode", new SettingContext());
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Source).IsEqualTo(SettingSource.SystemLocked);
        await Assert.That(result.IsLocked).IsTrue();
    }

    [Test]
    public async Task ResolveWithMetadata_ReturnsSourceTenantOverride()
    {
        SetupSystemSettings(new SystemSetting
        {
            SettingKey = "branding.display_name",
            Value = "\"Default\"",
            ValueType = SettingValueType.String,
            IsLocked = false
        });
        var tenantId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        SetupTenantSettings(tenantId, new TenantSetting
        {
            TenantId = tenantId,
            Tenant = null!,
            SettingKey = "branding.display_name",
            Value = "\"Tenant Brand\""
        });

        var result = await _resolver.ResolveWithMetadataAsync(
            "branding.display_name",
            new SettingContext(TenantId: tenantId));

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Source).IsEqualTo(SettingSource.TenantOverride);
        await Assert.That(result.Value).IsEqualTo("\"Tenant Brand\"");
    }

    // --- SetValue scope validation ---

    [Test]
    [Arguments(GuardedResolverMutation.Set)]
    [Arguments(GuardedResolverMutation.Remove)]
    [Arguments(GuardedResolverMutation.Lock)]
    [Arguments(GuardedResolverMutation.Unlock)]
    public async Task GuardedMutationsRejectBeforeLockOrRepositoryMutationCalls(
        GuardedResolverMutation mutation)
    {
        var resolver = new HierarchicalSettingsResolver(
            _systemRepo,
            _tenantRepo,
            _orgRepo,
            _groupRepo,
            _groupTenantRepo,
            _userPrefRepo,
            _tenantContext,
            RejectingSettingMutationLock.Instance,
            _cache,
            _logger,
            emailSettingsWriter: RejectingEmailDeliverySettingsWriter.Instance);
        string guardedKey = PublicationPolicySettingKeys.All[0];
        Guid tenantId = Guid.NewGuid();
        Guid actorId = Guid.NewGuid();
        Func<Task> guardedMutation = mutation switch
        {
            GuardedResolverMutation.Set => () => resolver.SetValueAsync(
                guardedKey, "true", SettingScope.Tenant, tenantId, actorId),
            GuardedResolverMutation.Remove => () => resolver.RemoveOverrideAsync(
                guardedKey, SettingScope.Tenant, tenantId, actorId),
            GuardedResolverMutation.Lock => () => resolver.LockAsync(
                guardedKey, SettingScope.Tenant, tenantId, actorId),
            GuardedResolverMutation.Unlock => () => resolver.UnlockAsync(
                guardedKey, SettingScope.Tenant, tenantId, actorId),
            _ => throw new ArgumentOutOfRangeException(nameof(mutation), mutation, null)
        };

        await Assert.ThrowsAsync<InvalidOperationException>(guardedMutation);
        await _systemRepo.DidNotReceive().IsLocked(guardedKey, Arg.Any<CancellationToken>());
        await _systemRepo.DidNotReceive().GetByKey(guardedKey, Arg.Any<CancellationToken>());
        await _systemRepo.DidNotReceive().UpsertAsync(Arg.Any<SystemSetting>(), Arg.Any<CancellationToken>());
        await _tenantRepo.DidNotReceive().SetValueAsync(
            tenantId, guardedKey, Arg.Any<string>(), Arg.Any<CancellationToken>(), Arg.Any<Guid?>());
        await _tenantRepo.DidNotReceive().RemoveOverrideAsync(tenantId, guardedKey, Arg.Any<CancellationToken>());
        await _tenantRepo.DidNotReceive().LockAsync(tenantId, guardedKey, Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _tenantRepo.DidNotReceive().UnlockAsync(tenantId, guardedKey, Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SetValueAsync_ThrowsWhenScopeOutOfRange()
    {
        // deployment.mode is Instance-only (MinScope=Instance, MaxScope=Instance)
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _resolver.SetValueAsync(
                "deployment.mode", "\"SingleTenant\"",
                SettingScope.Tenant, Guid.NewGuid(), Guid.NewGuid()));

        await Assert.That(ex!.Message).Contains("cannot be set at scope");
    }

    [Test]
    public async Task SetValueAsync_SucceedsAtInstanceScope()
    {
        await using var database = await SmtpSettingsDatabase.CreateAsync();

        await database.Settings.SetValueAsync(
            key: GovernanceSettingKeys.Email.SmtpPort, value: "465",
            scope: SettingScope.Instance, scopeId: Guid.Empty, actorId: Guid.NewGuid());

        await Assert.That(await database.Settings.ResolveAsync<int>(
            key: GovernanceSettingKeys.Email.SmtpPort, context: new SettingContext())).IsEqualTo(465);
    }

    [Test]
    public async Task RemoveOverrideAsync_WhenSystemSettingIsLocked_DoesNotRemoveTenantOverride()
    {
        await using var database = await SmtpSettingsDatabase.CreateAsync();
        var actorId = Guid.NewGuid();
        await database.SetInstanceAsync(GovernanceSettingKeys.TenantDelegation.LockSmtp, false);
        await database.SetInstanceAsync(GovernanceSettingKeys.Email.SmtpPort, 587);
        await database.SetTenantAsync(GovernanceSettingKeys.Email.SmtpPort, 465);
        await database.Settings.LockAsync(
            key: GovernanceSettingKeys.Email.SmtpPort, scope: SettingScope.Instance,
            scopeId: Guid.Empty, actorId: actorId);

        ValidationException? exception = await Assert.ThrowsAsync<ValidationException>(async () =>
            await database.Settings.RemoveOverrideAsync(
                key: GovernanceSettingKeys.Email.SmtpPort, scope: SettingScope.Tenant,
                scopeId: database.TenantId, actorId: actorId));

        await Assert.That(exception!.Errors).Contains("SMTP settings are locked by current governance.");
        await database.Settings.UnlockAsync(
            key: GovernanceSettingKeys.Email.SmtpPort, scope: SettingScope.Instance,
            scopeId: Guid.Empty, actorId: actorId);
        await Assert.That(await database.Settings.ResolveAsync<int>(
            key: GovernanceSettingKeys.Email.SmtpPort,
            context: new SettingContext(TenantId: database.TenantId))).IsEqualTo(465);
    }

    // --- Lock ---

    [Test]
    public async Task LockAsync_SetsIsLockedToTrue()
    {
        await using var database = await SmtpSettingsDatabase.CreateAsync();
        await database.SetInstanceAsync(GovernanceSettingKeys.Email.SmtpPort, 587);

        await database.Settings.LockAsync(
            key: GovernanceSettingKeys.Email.SmtpPort, scope: SettingScope.Instance,
            scopeId: Guid.Empty, actorId: Guid.NewGuid());

        var setting = await database.Settings.ResolveWithMetadataAsync(
            key: GovernanceSettingKeys.Email.SmtpPort, context: new SettingContext());
        await Assert.That(setting).IsNotNull();
        await Assert.That(setting!.IsLocked).IsTrue();
        await Assert.That(setting.Value).IsEqualTo("587");
    }

    [Test]
    public async Task LockAsync_ThrowsForUnsupportedScope()
    {
        await Assert.ThrowsAsync<ValidationException>(async () =>
            await _resolver.LockAsync("email.smtp_port", SettingScope.Organization, Guid.NewGuid(), Guid.NewGuid()));
    }

    [Test]
    public async Task LockAsync_ThrowsWhenTenantSettingNotFound()
    {
        var tenantId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        _tenantRepo.LockAsync(tenantId, "nonexistent.key", actorId).Returns(false);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _resolver.LockAsync("nonexistent.key", SettingScope.Tenant, tenantId, actorId));
    }

    // --- Unlock ---

    [Test]
    public async Task UnlockAsync_SucceedsAtInstanceScope()
    {
        await using var database = await SmtpSettingsDatabase.CreateAsync();
        var actorId = Guid.NewGuid();
        await database.SetInstanceAsync(GovernanceSettingKeys.Email.SmtpPort, 587);
        await database.Settings.LockAsync(
            key: GovernanceSettingKeys.Email.SmtpPort, scope: SettingScope.Instance,
            scopeId: Guid.Empty, actorId: actorId);

        await database.Settings.UnlockAsync(
            key: GovernanceSettingKeys.Email.SmtpPort, scope: SettingScope.Instance,
            scopeId: Guid.Empty, actorId: actorId);

        var setting = await database.Settings.ResolveWithMetadataAsync(
            key: GovernanceSettingKeys.Email.SmtpPort, context: new SettingContext());
        await Assert.That(setting).IsNotNull();
        await Assert.That(setting!.IsLocked).IsFalse();
        await Assert.That(setting.Value).IsEqualTo("587");
    }

    [Test]
    public async Task UnlockAsync_ThrowsForUnsupportedScope()
    {
        await Assert.ThrowsAsync<ValidationException>(async () =>
            await _resolver.UnlockAsync("email.smtp_port", SettingScope.Group, Guid.NewGuid(), Guid.NewGuid()));
    }

    // --- Tenant lock cascade ---

    [Test]
    public async Task ResolveWithMetadata_ReturnsTenantLocked_WhenTenantSettingIsLocked()
    {
        SetupSystemSettings(new SystemSetting
        {
            SettingKey = "branding.display_name",
            Value = "\"Default\"",
            ValueType = SettingValueType.String,
            IsLocked = false
        });
        var tenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        SetupTenantSettings(tenantId, new TenantSetting
        {
            TenantId = tenantId,
            Tenant = null!,
            SettingKey = "branding.display_name",
            Value = "\"Tenant Enforced\"",
            IsLocked = true
        });

        var result = await _resolver.ResolveWithMetadataAsync(
            "branding.display_name",
            new SettingContext(TenantId: tenantId));

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Source).IsEqualTo(SettingSource.TenantLocked);
        await Assert.That(result.IsLocked).IsTrue();
        await Assert.That(result.Value).IsEqualTo("\"Tenant Enforced\"");
    }

    [Test]
    public async Task ResolveAsync_TenantLocked_IgnoresUserPreference()
    {
        SetupSystemSettings(new SystemSetting
        {
            SettingKey = "appearance.theme_mode",
            Value = "\"light\"",
            ValueType = SettingValueType.String,
            IsLocked = false
        });
        var tenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var userId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
        SetupTenantSettings(tenantId, new TenantSetting
        {
            TenantId = tenantId,
            Tenant = null!,
            SettingKey = "appearance.theme_mode",
            Value = "\"dark\"",
            IsLocked = true
        });
        SetupUserPreferences(tenantId, userId, new UserPreference
        {
            TenantId = tenantId,
            Tenant = null!,
            UserId = userId,
            SettingKey = "appearance.theme_mode",
            Value = "\"system\""
        });

        var context = new SettingContext(TenantId: tenantId, UserId: userId);
        var result = await _resolver.ResolveWithMetadataAsync("appearance.theme_mode", context);

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Source).IsEqualTo(SettingSource.TenantLocked);
        await Assert.That(result.Value).IsEqualTo("\"dark\"");
    }

    [Test]
    public async Task ResolveAsync_TenantLocked_IgnoresOrgOverride()
    {
        SetupSystemSettings(new SystemSetting
        {
            SettingKey = "branding.display_name",
            Value = "\"Platform\"",
            ValueType = SettingValueType.String,
            IsLocked = false
        });
        var tenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var orgId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        SetupTenantSettings(tenantId, new TenantSetting
        {
            TenantId = tenantId,
            Tenant = null!,
            SettingKey = "branding.display_name",
            Value = "\"Tenant Enforced\"",
            IsLocked = true
        });
        SetupOrgSettings(tenantId, orgId, CreateOrganizationSetting(
            orgId,
            tenantId,
            "branding.display_name",
            "\"Org Brand\""));

        var context = new SettingContext(TenantId: tenantId, OrganizationId: orgId);
        var result = await _resolver.ResolveWithMetadataAsync("branding.display_name", context);

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Source).IsEqualTo(SettingSource.TenantLocked);
        await Assert.That(result.Value).IsEqualTo("\"Tenant Enforced\"");
    }

    [Test]
    public async Task ResolveAsync_InstanceLock_TakesPrecedence_OverTenantLock()
    {
        SetupSystemSettings(new SystemSetting
        {
            SettingKey = "deployment.mode",
            Value = "\"MultiTenant\"",
            ValueType = SettingValueType.String,
            IsLocked = true
        });
        var tenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        SetupTenantSettings(tenantId, new TenantSetting
        {
            TenantId = tenantId,
            Tenant = null!,
            SettingKey = "deployment.mode",
            Value = "\"SingleTenant\"",
            IsLocked = true
        });

        var context = new SettingContext(TenantId: tenantId);
        var result = await _resolver.ResolveWithMetadataAsync("deployment.mode", context);

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Source).IsEqualTo(SettingSource.SystemLocked);
        await Assert.That(result.Value).IsEqualTo("\"MultiTenant\"");
        await Assert.That(result.IsLocked).IsTrue();
    }

    [Test]
    public async Task ResolveAsync_UnlockedTenant_AllowsFullCascade()
    {
        SetupSystemSettings(new SystemSetting
        {
            SettingKey = "appearance.theme_mode",
            Value = "\"light\"",
            ValueType = SettingValueType.String,
            IsLocked = false
        });
        var tenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var userId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
        SetupTenantSettings(tenantId, new TenantSetting
        {
            TenantId = tenantId,
            Tenant = null!,
            SettingKey = "appearance.theme_mode",
            Value = "\"dark\"",
            IsLocked = false
        });
        SetupUserPreferences(tenantId, userId, new UserPreference
        {
            TenantId = tenantId,
            Tenant = null!,
            UserId = userId,
            SettingKey = "appearance.theme_mode",
            Value = "\"system\""
        });

        var context = new SettingContext(TenantId: tenantId, UserId: userId);
        var result = await _resolver.ResolveWithMetadataAsync("appearance.theme_mode", context);

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Source).IsEqualTo(SettingSource.UserPreference);
        await Assert.That(result.Value).IsEqualTo("\"system\"");
        await Assert.That(result.IsLocked).IsFalse();
    }

    // --- Cache invalidation ---

    [Test]
    public async Task InvalidateCache_RemovesSystemCache()
    {
        SetupSystemSettings(new SystemSetting
        {
            SettingKey = "email.smtp_port",
            Value = "587",
            ValueType = SettingValueType.Integer,
            IsLocked = false
        });

        // Prime the cache
        await _resolver.ResolveAsync<int>("email.smtp_port", new SettingContext());

        // Invalidate
        _resolver.InvalidateCache(SettingScope.Instance);

        // Next call should hit the repo again
        _systemRepo.ClearReceivedCalls();
        await _resolver.ResolveAsync<int>("email.smtp_port", new SettingContext());
        await _systemRepo.Received(1).GetAllSettings(Arg.Any<string?>());
    }

    // --- Organization scope ---

    [Test]
    public async Task ResolveAsync_ReturnsOrgOverride_WhenNotLocked()
    {
        const string settingKey = "custom.organization_brand";
        SetupSystemSettings(new SystemSetting
        {
            SettingKey = settingKey,
            Value = "\"Platform\"",
            ValueType = SettingValueType.String,
            IsLocked = false
        });
        var tenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var orgId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        SetupTenantSettings(tenantId, new TenantSetting
        {
            TenantId = tenantId,
            Tenant = null!,
            SettingKey = settingKey,
            Value = "\"Tenant Brand\""
        });
        SetupOrgSettings(
            tenantId,
            orgId,
            CreateOrganizationSetting(orgId, tenantId, settingKey, "\"Org Brand\""));

        var context = new SettingContext(TenantId: tenantId, OrganizationId: orgId);
        var result = await _resolver.ResolveWithMetadataAsync(settingKey, context);
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Source).IsEqualTo(SettingSource.OrganizationOverride);
        await Assert.That(result.Value).IsEqualTo("\"Org Brand\"");
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task ResolveAsync_SameGlobalOrganizationAcrossTenants_HasNoWarmCacheBleed(
        bool resolveTenantAFirst)
    {
        string settingKey = GovernanceSettingKeys.AddressGovernance.OrganizationCreationGrant;
        Guid tenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        Guid tenantB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        Guid organizationId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        SetupSystemSettings();
        SetupTenantSettings(tenantA);
        SetupTenantSettings(tenantB);
        SetupOrgSettings(
            tenantA,
            organizationId,
            CreateOrganizationSetting(organizationId, tenantA, settingKey, "true"));
        SetupOrgSettings(
            tenantB,
            organizationId,
            CreateOrganizationSetting(organizationId, tenantB, settingKey, "false"));

        var contextA = new SettingContext(TenantId: tenantA, OrganizationId: organizationId);
        var contextB = new SettingContext(TenantId: tenantB, OrganizationId: organizationId);
        if (resolveTenantAFirst)
        {
            await Assert.That(await _resolver.ResolveAsync<bool>(settingKey, contextA)).IsTrue();
            await Assert.That(await _resolver.ResolveAsync<bool>(settingKey, contextB)).IsFalse();
        }
        else
        {
            await Assert.That(await _resolver.ResolveAsync<bool>(settingKey, contextB)).IsFalse();
            await Assert.That(await _resolver.ResolveAsync<bool>(settingKey, contextA)).IsTrue();
        }

        await _orgRepo.Received(1).GetAllForOrganization(
            tenantA,
            organizationId,
            Arg.Any<CancellationToken>());
        await _orgRepo.Received(1).GetAllForOrganization(
            tenantB,
            organizationId,
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task InvalidateOrganizationCache_RemovesOnlyExactTenantOrganizationPair()
    {
        string settingKey = GovernanceSettingKeys.AddressGovernance.OrganizationCreationGrant;
        Guid tenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        Guid tenantB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        Guid organizationId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        List<OrganizationSetting> tenantASettings =
            [CreateOrganizationSetting(organizationId, tenantA, settingKey, "true")];
        List<OrganizationSetting> tenantBSettings =
            [CreateOrganizationSetting(organizationId, tenantB, settingKey, "false")];
        SetupSystemSettings();
        SetupTenantSettings(tenantA);
        SetupTenantSettings(tenantB);
        _orgRepo.GetAllForOrganization(tenantA, organizationId, Arg.Any<CancellationToken>())
            .Returns(_ => tenantASettings);
        _orgRepo.GetAllForOrganization(tenantB, organizationId, Arg.Any<CancellationToken>())
            .Returns(_ => tenantBSettings);
        var contextA = new SettingContext(TenantId: tenantA, OrganizationId: organizationId);
        var contextB = new SettingContext(TenantId: tenantB, OrganizationId: organizationId);

        await Assert.That(await _resolver.ResolveAsync<bool>(settingKey, contextA)).IsTrue();
        await Assert.That(await _resolver.ResolveAsync<bool>(settingKey, contextB)).IsFalse();
        tenantASettings = [CreateOrganizationSetting(organizationId, tenantA, settingKey, "false")];
        tenantBSettings = [CreateOrganizationSetting(organizationId, tenantB, settingKey, "true")];

        _resolver.InvalidateOrganizationCache(tenantA, organizationId);

        await Assert.That(await _resolver.ResolveAsync<bool>(settingKey, contextA)).IsFalse();
        await Assert.That(await _resolver.ResolveAsync<bool>(settingKey, contextB)).IsFalse();
        await _orgRepo.Received(2).GetAllForOrganization(
            tenantA,
            organizationId,
            Arg.Any<CancellationToken>());
        await _orgRepo.Received(1).GetAllForOrganization(
            tenantB,
            organizationId,
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ResolveAsync_OrganizationWithoutTenant_DoesNotReadOrganizationSettings()
    {
        Guid organizationId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        SetupSystemSettings();

        bool? result = await _resolver.ResolveAsync<bool>(
            GovernanceSettingKeys.AddressGovernance.OrganizationCreationGrant,
            new SettingContext(OrganizationId: organizationId));

        await Assert.That(result).IsFalse();
        await _orgRepo.DidNotReceiveWithAnyArgs().GetAllForOrganization(
            default,
            default,
            default);
    }

    // --- Group scope ---

    [Test]
    public async Task ResolveAsync_ReturnsGroupOverride_WhenNotLocked()
    {
        const string settingKey = "custom.group_brand";
        SetupSystemSettings(new SystemSetting
        {
            SettingKey = settingKey,
            Value = "\"Platform\"",
            ValueType = SettingValueType.String,
            IsLocked = false
        });
        var tenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var groupId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        SetupGroupSettings(groupId, CreateGroupSetting(groupId, tenantId, settingKey, "\"Group Brand\""));

        var context = new SettingContext(TenantId: tenantId, GroupId: groupId);
        var result = await _resolver.ResolveWithMetadataAsync(settingKey, context);
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Source).IsEqualTo(SettingSource.GroupOverride);
    }

    // --- SetValue at organization/group scopes ---

    [Test]
    public async Task SetValueAsync_SucceedsAtOrganizationScope()
    {
        var orgId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        _tenantContext.TenantId.Returns(tenantId);

        await _resolver.SetValueAsync(
            "custom.org_name", "\"Org Brand\"",
            SettingScope.Organization, orgId, actorId);

        await _orgRepo.Received(1).SetValueAsync(
            tenantId,
            orgId,
            "custom.org_name",
            "\"Org Brand\"",
            actorId,
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SetValueAsync_SucceedsAtGroupScope()
    {
        var groupId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        _tenantContext.TenantId.Returns(tenantId);
        _groupTenantRepo.GetByGroupAndTenant(groupId, tenantId, Arg.Any<CancellationToken>())
            .Returns(CreateGroupParticipation(groupId, tenantId));
        _groupRepo.GetByGroupAndKey(groupId, "custom.group_name").Returns((GroupSetting?)null);

        await _resolver.SetValueAsync(
            "custom.group_name", "\"Group Brand\"",
            SettingScope.Group, groupId, Guid.NewGuid());

        await _groupRepo.Received(1).Create(Arg.Is<GroupSetting>(s =>
            s!.SettingKey == "custom.group_name" && s.GroupTenant!.GroupId == groupId));
    }

    // --- Helpers ---

    private void SetupSystemSettings(params SystemSetting[] settings)
    {
        _systemRepo.GetAllSettings(Arg.Any<string?>()).Returns(settings.ToList());
    }

    private void SetupTenantSettings(Guid tenantId, params TenantSetting[] settings)
    {
        _tenantRepo.GetAllForTenant(tenantId).Returns(settings.ToList());
    }

    private void SetupOrgSettings(
        Guid tenantId,
        Guid orgId,
        params OrganizationSetting[] settings)
    {
        _orgRepo.GetAllForOrganization(tenantId, orgId, Arg.Any<CancellationToken>())
            .Returns(settings.ToList());
    }

    private void SetupGroupSettings(Guid groupId, params GroupSetting[] settings)
    {
        _groupRepo.GetAllForGroup(groupId).Returns(settings.ToList());
    }

    private static OrganizationSetting CreateOrganizationSetting(
        Guid organizationId,
        Guid tenantId,
        string key,
        string value)
    {
        OrganizationTenant participation = CreateOrganizationParticipation(organizationId, tenantId);
        return new OrganizationSetting
        {
            OrganizationTenantId = participation.Id,
            OrganizationTenant = participation,
            TenantId = tenantId,
            Tenant = null!,
            SettingKey = key,
            Value = value
        };
    }

    private static GroupSetting CreateGroupSetting(Guid groupId, Guid tenantId, string key, string value)
    {
        GroupTenant participation = CreateGroupParticipation(groupId, tenantId);
        return new GroupSetting
        {
            GroupTenantId = participation.Id,
            GroupTenant = participation,
            TenantId = tenantId,
            Tenant = null!,
            SettingKey = key,
            Value = value
        };
    }

    private static OrganizationTenant CreateOrganizationParticipation(Guid organizationId, Guid tenantId) => new()
    {
        Id = Guid.CreateVersion7(),
        OrganizationId = organizationId,
        Organization = new Organization
        {
            Id = organizationId,
            Pii = new OrganizationPii { FullName = "Organization" }
        },
        TenantId = tenantId,
        Tenant = null!,
        ApprovalStatus = null!
    };

    private static GroupTenant CreateGroupParticipation(Guid groupId, Guid tenantId) => new()
    {
        Id = Guid.CreateVersion7(),
        GroupId = groupId,
        Group = new Group { Id = groupId, FullName = "Group" },
        TenantId = tenantId,
        Tenant = null!,
        ApprovalStatus = null!
    };

    private void SetupUserPreferences(Guid tenantId, Guid userId, params UserPreference[] prefs)
    {
        _userPrefRepo.GetAllForUser(tenantId, userId).Returns(prefs.ToList());
    }
}
