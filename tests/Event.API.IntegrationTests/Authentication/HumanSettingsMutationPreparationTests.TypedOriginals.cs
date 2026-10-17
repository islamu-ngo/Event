using System.Collections.Immutable;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Instance;
using Explore.Application.DTOs.TenantPolicy;
using Explore.Application.DTOs.TenantSettings;
using Explore.Application.Features.InstanceOnboarding.Handlers.Commands;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Features.Settings;
using Explore.Application.Features.Settings.Requests.Commands;
using Explore.Application.Features.TenantOnboarding.Handlers.Commands;
using Explore.Application.Features.TenantOnboarding.Requests.Commands;
using Explore.Application.Models.Common;
using Explore.Application.Notifications;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Settings.Documents;
using Explore.Persistence;
using Explore.Persistence.Identity;
using Explore.Persistence.Security;
using Explore.Persistence.Seed;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Event.Api.IntegrationTests.Authentication;

public sealed partial class HumanSettingsMutationPreparationTests
{
    [Test]
    [Arguments(false, "commit")]
    [Arguments(true, "commit")]
    [Arguments(false, "corrupt")]
    [Arguments(false, "credential-after-terminal-read")]
    [Arguments(false, "omitted-writer")]
    [Arguments(false, "caught-replay")]
    public async Task ActualEventPolicyPatchRetainsPairedLockAndPublicationOwner(bool absent, string outcome)
    {
        var observation = new TypedObservation { CatchReplayedApply = outcome == "caught-replay" };
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(
            configureServices: services => ConfigureTypedObservation(services, observation));
        await SeedPlatformAdministratorAsync(fixture);
        string key = GovernanceSettingKeys.Events.CardClickOpensDetailPage;
        await using (var seed = fixture.Native.CreateDatabase())
        {
            await SeedSpecializedSettingAsync(seed, GovernanceSettingKeys.Events.RequireApproval, "true");
            await SeedSpecializedSettingAsync(seed, GovernanceSettingKeys.EventReporting.IntakeEnabled, "true");
            await SeedSpecializedSettingAsync(seed, GovernanceSettingKeys.Events.UserSubmissionEnabled, "true");
            if (absent)
            {
                await seed.SystemSettings.Where(row => row.SettingKey == key).ExecuteDeleteAsync();
                await seed.SystemSettings.Where(row => row.SettingKey == GovernanceSettingKeys.Events.UserSubmissionEnabled)
                    .ExecuteDeleteAsync();
            }
            else
            {
                await SeedSpecializedSettingAsync(seed, key, "false");
                await seed.SystemSettings.Where(row => row.SettingKey == key)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.Description, "original retained metadata"));
            }
        }
        await using var request = await Request.CreateAsync(fixture);
        await using var selected = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(PolicyMutationScope.MixedSettingsInstance, CancellationToken.None);
        var context = selected.Services.GetRequiredService<ExploreDbContext>();
        var published = new List<SettingChangedNotification>();
        observation.BeforeComplete = async services =>
        {
            var owner = services.GetRequiredService<ExploreDbContext>();
            await Assert.That(ReferenceEquals(owner, context)).IsTrue();
            await Assert.That(published.Count).IsEqualTo(0);
            if (outcome == "corrupt")
                await owner.SystemSettings.Where(row => row.SettingKey == key)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.Value, "false"));
        };
        observation.AfterTerminalRead = () =>
        {
            if (outcome == "credential-after-terminal-read")
                ExpireMixedCredential(fixture, selected.Services);
        };
        var command = new UpdateEventPolicyCommand
        {
            UserId = fixture.Binding.LocalSubjectId,
            Patch = new PatchEventPolicyDto
            {
                AllowUserSubmittedEvents = OptionalUpdate<bool>.Set(false),
                EventCardClickOpensDetailPage = OptionalUpdate<bool>.Set(true),
                LockTenantEventCardClickBehavior = OptionalUpdate<bool>.Set(true)
            }
        };
        if (outcome == "omitted-writer")
        {
            var preparation = selected.Services.GetRequiredService<IHumanSettingsMutationPreparation>();
            var prepared = await preparation.PrepareAsync(command);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                preparation.ExecutePreparedAsync(prepared, (_, _) => Task.FromResult(true)));
        }
        else
        {
            var handler = ActivatorUtilities.CreateInstance<UpdateEventPolicyCommandHandler>(selected.Services,
                (IEnumerable<INotificationHandler<SettingChangedNotification>>)[new TypedNotifications(context, published)]);
            if (outcome == "commit")
                await Assert.That((await handler.ExecuteAsync(command, CancellationToken.None)).IsSuccess).IsTrue();
            else
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => handler.ExecuteAsync(command, CancellationToken.None));
        }
        await using var independent = fixture.Native.CreateDatabase();
        var stored = await independent.SystemSettings.AsNoTracking().SingleOrDefaultAsync(row => row.SettingKey == key);
        await Assert.That(stored?.Value).IsEqualTo(outcome == "commit" ? "true" : absent ? null : "false");
        await Assert.That(stored?.IsLocked ?? false).IsEqualTo(outcome == "commit");
        if (!absent) await Assert.That(stored!.Description).IsEqualTo("original retained metadata");
        await Assert.That(await independent.SystemSettings.Where(row =>
                row.SettingKey == GovernanceSettingKeys.Events.UserSubmissionEnabled).Select(row => row.Value).SingleAsync())
            .IsEqualTo(outcome == "commit" ? "false" : "true");
        await Assert.That(published.Count).IsEqualTo(outcome == "commit" ? 2 : 0);
        if (absent && outcome == "commit")
        {
            var publication = await independent.SystemSettings.AsNoTracking().SingleAsync(row =>
                row.SettingKey == GovernanceSettingKeys.Events.UserSubmissionEnabled);
            await Assert.That(publication.Id).IsEqualTo(SeedIds.SystemSettingUserSubmissionEnabledId);
            await Assert.That(publication.DisplayOrder).IsEqualTo(3);
        }
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    public async Task ActualLockOnlyPatchCreatesTheAbsentOriginalDefault()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(
            configureServices: services => services.AddHumanMixedSettingsSources(IdentityDatabaseTopology.Colocated));
        await SeedPlatformAdministratorAsync(fixture);
        string key = GovernanceSettingKeys.Events.CardClickOpensDetailPage;
        await using (var seed = fixture.Native.CreateDatabase())
            await seed.SystemSettings.Where(row => row.SettingKey == key).ExecuteDeleteAsync();
        await using var request = await Request.CreateAsync(fixture);
        await using var selected = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(PolicyMutationScope.MixedSettingsInstance, CancellationToken.None);
        var handler = ActivatorUtilities.CreateInstance<UpdateEventPolicyCommandHandler>(selected.Services,
            (IEnumerable<INotificationHandler<SettingChangedNotification>>)[]);
        var result = await handler.ExecuteAsync(new()
        {
            UserId = fixture.Binding.LocalSubjectId,
            Patch = new() { LockTenantEventCardClickBehavior = OptionalUpdate<bool>.Set(true) }
        }, CancellationToken.None);
        await Assert.That(result.IsSuccess).IsTrue();
        await using var independent = fixture.Native.CreateDatabase();
        var stored = await independent.SystemSettings.AsNoTracking().SingleAsync(row => row.SettingKey == key);
        await Assert.That(stored.Value).IsEqualTo("true");
        await Assert.That(stored.IsLocked).IsTrue();
    }

    [Test]
    [Arguments("commit")]
    [Arguments("corrupt")]
    [Arguments("credential-after-terminal-read")]
    public async Task ActualOnboardingCompilesCleanupNormalizationRevisionAndWholeTransaction(string outcome)
    {
        var observation = new TypedObservation();
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(
            configureServices: services => ConfigureTypedObservation(services, observation));
        await SeedMixedTenantAdministratorAsync(fixture);
        Guid tenant = PlatformDefaults.DefaultTenantId;
        string cleanupKey = GovernanceSettingKeys.Domains.TenantCustomDomain;
        string revisionKey = GovernanceSettingKeys.PublicExperience.AnnouncementBarRevision;
        await using (var seed = fixture.Native.CreateDatabase())
        {
            await seed.Set<TenantOnboardingState>().Where(row => row.TenantId == tenant).ExecuteDeleteAsync();
            await seed.Set<TenantSettingsDocument>().Where(row => row.TenantId == tenant
                && (row.DocumentKey == SettingsDocumentKeys.Tenant.DirectoryOperatorIdentity
                    || row.DocumentKey == SettingsDocumentKeys.Tenant.Branding)).ExecuteDeleteAsync();
            await SeedSpecializedSettingAsync(seed, GovernanceSettingKeys.Events.RequireApproval, "true");
            await SeedSpecializedSettingAsync(seed, GovernanceSettingKeys.EventReporting.IntakeEnabled, "true");
            await SeedSpecializedSettingAsync(seed, cleanupKey, "\"instance.example.test\"");
            await seed.SystemSettings.Where(row => row.SettingKey == cleanupKey
                    || row.SettingKey == GovernanceSettingKeys.Events.RequireApproval)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsLocked, true));
            await seed.TenantSettingOverrides.Where(row => row.TenantId == tenant).ExecuteDeleteAsync();
            seed.TenantSettingOverrides.AddRange(
                new TenantSetting { TenantId = tenant, Tenant = null!, SettingKey = cleanupKey, Value = "\"old.example.test\"" },
                new TenantSetting { TenantId = tenant, Tenant = null!, SettingKey = revisionKey, Value = "7" },
                new TenantSetting { TenantId = tenant, Tenant = null!, SettingKey = GovernanceSettingKeys.Events.RequireApproval, Value = "false" });
            await seed.SaveChangesAsync();
        }
        var modelIds = new List<string> { " model-A ", "MODEL-a", "model-B" };
        observation.AfterObservation = () => { modelIds.Clear(); modelIds.Add("substituted-after-observation"); };
        await using var request = await Request.CreateAsync(fixture);
        await using var selected = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(PolicyMutationScope.MixedSettingsTenant(tenant), CancellationToken.None);
        var context = selected.Services.GetRequiredService<ExploreDbContext>();
        bool sawWholeTransaction = false;
        observation.BeforeComplete = async services =>
        {
            var owner = services.GetRequiredService<ExploreDbContext>();
            sawWholeTransaction = await owner.Set<TenantOnboardingState>().AnyAsync(row => row.TenantId == tenant && row.IsCompleted)
                && await owner.Set<TenantSettingsDocument>().CountAsync(row => row.TenantId == tenant
                    && (row.DocumentKey == SettingsDocumentKeys.Tenant.DirectoryOperatorIdentity
                        || row.DocumentKey == SettingsDocumentKeys.Tenant.Branding)) == 2;
            if (outcome == "corrupt")
                await owner.TenantSettingOverrides.Where(row => row.TenantId == tenant && row.SettingKey == revisionKey)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.Value, "999"));
        };
        observation.AfterTerminalRead = () =>
        {
            if (outcome == "credential-after-terminal-read") ExpireMixedCredential(fixture, selected.Services);
        };
        var handler = ActivatorUtilities.CreateInstance<CompleteTenantOnboardingCommandHandler>(selected.Services,
            (IEnumerable<INotificationHandler<SettingChangedNotification>>)[]);
        var command = new CompleteTenantOnboardingCommand
        {
            UserId = fixture.Binding.LocalSubjectId,
            Settings = new UpdateTenantPolicyRequest
            {
                RequireEventApproval = false,
                CustomDomain = "https://new.example.test/",
                ForceAnnouncementBarRedisplay = true,
                AnnouncementBarMessage = "  original message  ",
                AiAssistantProvider = "openai-compatible",
                AiAssistantModelId = " model-A ",
                AiAssistantAllowedModelIds = modelIds
            },
            DirectoryOperatorIdentity = new TenantDirectoryOperatorIdentityInputDto
            {
                PublicName = "Typed Original Operator",
                LegalName = "Typed Original Operator",
                OperatorKindCode = "registered_organization",
                JurisdictionCountryCode = "GB",
                PublicContactEmail = "operator@example.test",
                LegalNoticeUrl = "https://example.test/legal",
                TermsUrl = "https://example.test/terms",
                PrivacyUrl = "https://example.test/privacy"
            }
        };
        if (outcome == "commit")
            await Assert.That((await handler.ExecuteAsync(command, CancellationToken.None)).IsSuccess).IsTrue();
        else
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => handler.ExecuteAsync(command, CancellationToken.None));
        await Assert.That(sawWholeTransaction).IsTrue();
        await using var independent = fixture.Native.CreateDatabase();
        var values = await independent.TenantSettingOverrides.AsNoTracking().Where(row => row.TenantId == tenant)
            .ToDictionaryAsync(row => row.SettingKey, row => row.Value);
        await Assert.That(values[revisionKey]).IsEqualTo(outcome == "commit" ? "8" : "7");
        await Assert.That(values.ContainsKey(cleanupKey)).IsEqualTo(outcome != "commit");
        await Assert.That(values.ContainsKey(GovernanceSettingKeys.Events.RequireApproval)).IsEqualTo(outcome != "commit");
        if (outcome == "commit")
        {
            await Assert.That(values[GovernanceSettingKeys.AiAssistant.AllowedModelIds]).IsEqualTo("[\"model-A\",\"model-B\"]");
            await Assert.That(values[GovernanceSettingKeys.PublicExperience.AnnouncementBarMessage]).IsEqualTo("\"original message\"");
        }
        await Assert.That(await independent.Set<TenantOnboardingState>().AnyAsync(row => row.TenantId == tenant)).IsEqualTo(outcome == "commit");
        await Assert.That(await independent.Set<TenantSettingsDocument>().CountAsync(row => row.TenantId == tenant
                && (row.DocumentKey == SettingsDocumentKeys.Tenant.DirectoryOperatorIdentity
                    || row.DocumentKey == SettingsDocumentKeys.Tenant.Branding))).IsEqualTo(outcome == "commit" ? 2 : 0);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    private static void ConfigureTypedObservation(IServiceCollection services, TypedObservation observation)
    {
        services.AddHumanMixedSettingsSources(IdentityDatabaseTopology.Colocated);
        var factory = services.Last(descriptor => descriptor.ServiceType == typeof(IHumanMixedSettingsSourceFactory));
        services.RemoveAll<IHumanMixedSettingsSourceFactory>();
        services.AddScoped<IHumanMixedSettingsSourceFactory>(provider =>
            new ObservedTypedSourceFactory((IHumanMixedSettingsSourceFactory)factory.ImplementationFactory!(provider), observation));
        services.AddScoped<TypedSettingsOriginalStore>();
        services.RemoveAll<ITypedSettingsOriginalStore>();
        services.AddScoped<ITypedSettingsOriginalStore>(provider =>
            new ObservedTypedStore(provider.GetRequiredService<TypedSettingsOriginalStore>(), provider, observation));
    }

    private sealed class TypedObservation
    {
        internal Action? AfterObservation { get; set; }
        internal Func<IServiceProvider, Task>? BeforeComplete { get; set; }
        internal Action? AfterTerminalRead { get; set; }
        internal bool Finalizing { get; set; }
        internal bool TerminalObserved { get; set; }
        internal bool CatchReplayedApply { get; set; }
    }

    private sealed class TypedNotifications(ExploreDbContext context, List<SettingChangedNotification> published)
        : INotificationHandler<SettingChangedNotification>
    {
        public Task HandleAsync(SettingChangedNotification notification, CancellationToken cancellationToken)
        {
            if (context.Database.CurrentTransaction is not null)
                throw new InvalidOperationException("Typed notification preceded original physical settlement.");
            published.Add(notification);
            return Task.CompletedTask;
        }
    }

    // Transparent observation of the real issuer and writer. No fixture creates subject,
    // source, credential or completion authority.
    private sealed class ObservedTypedSourceFactory(IHumanMixedSettingsSourceFactory inner, TypedObservation observation)
        : IHumanMixedSettingsSourceFactory
    {
        public Task<IHumanMixedSettingsSourceProtocol> CreateAsync(UpdateSettingBatchCommand command, CancellationToken token) =>
            inner.CreateAsync(command, token);
        public async Task<IHumanMixedSettingsSourceProtocol> CreateAsync(TypedSettingsOriginal original, CancellationToken token) =>
            new ObservedTypedProtocol(await inner.CreateAsync(original, token), observation);
    }

    private sealed class ObservedTypedProtocol(IHumanMixedSettingsSourceProtocol inner, TypedObservation observation)
        : IHumanMixedSettingsSourceProtocol
    {
        public HumanCommitSubject Subject => inner.Subject;
        public Explore.Application.Authentication.HumanAuthenticationLifetime Lifetime => inner.Lifetime;
        public HumanSupportForwardingObservation Forwarding => inner.Forwarding;
        public Guid? TenantId => inner.TenantId;
        public string OriginalData => inner.OriginalData;
        public async Task<HumanEmailSourceFacts> ObserveAsync(CancellationToken token)
        {
            var result = await inner.ObserveAsync(token);
            observation.AfterObservation?.Invoke();
            return result;
        }
        public Task<HumanEmailSourceFacts> AcquireAsync(CancellationToken token) => inner.AcquireAsync(token);
        public async Task<HumanEmailSourceFacts> RequireOwnedAsync(CancellationToken token)
        {
            var result = await inner.RequireOwnedAsync(token);
            if (observation.Finalizing && !observation.TerminalObserved)
            {
                observation.TerminalObserved = true;
                observation.AfterTerminalRead?.Invoke();
            }
            return result;
        }
    }

    private sealed class ObservedTypedStore(ITypedSettingsOriginalStore inner, IServiceProvider services, TypedObservation observation)
        : ITypedSettingsOriginalStore
    {
        public Task ObserveAsync(TypedSettingsOriginal original, IHumanMixedSettingsSource source, CancellationToken token) =>
            inner.ObserveAsync(original, source, token);
        public Task<ImmutableArray<TypedSettingsEffect>> AcquireAndCompileAsync(TypedSettingsOriginal original, CancellationToken token) =>
            inner.AcquireAndCompileAsync(original, token);
        public async Task<PublicationPolicyMutationResult> ApplyEventPolicyAsync(TypedSettingsOriginal original, CancellationToken token)
        {
            var result = await inner.ApplyEventPolicyAsync(original, token);
            if (observation.CatchReplayedApply)
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => inner.ApplyEventPolicyAsync(original, token));
            return result;
        }
        public Task<IReadOnlyList<SettingChangedNotification>> ApplyOnboardingAsync(TypedSettingsOriginal original, CancellationToken token) =>
            inner.ApplyOnboardingAsync(original, token);
        public Task<PublicationPolicyMutationResult> ApplyReportingAsync(TypedSettingsOriginal original, CancellationToken token) =>
            inner.ApplyReportingAsync(original, token);
        public Task<IReadOnlyList<SettingChangedNotification>> ApplyGovernanceAsync(TypedSettingsOriginal original, CancellationToken token) =>
            inner.ApplyGovernanceAsync(original, token);
        public void InvalidateGovernanceAfterCommit(TypedSettingsOriginal original) => inner.InvalidateGovernanceAfterCommit(original);
        public async Task RequireCompleteAsync(TypedSettingsOriginal original, CancellationToken token)
        {
            if (observation.BeforeComplete is { } before) await before(services);
            observation.Finalizing = true;
            await inner.RequireCompleteAsync(original, token);
        }
        public Task VerifyAsync(TypedSettingsOriginal original, CancellationToken token) => inner.VerifyAsync(original, token);
        public void RequireObservedData(TypedSettingsOriginal original, string data) => inner.RequireObservedData(original, data);
        public Task RequirePublicationAsync(TypedSettingsPublication publication, CancellationToken token) =>
            inner.RequirePublicationAsync(publication, token);
        public Task<CoordinatedSettingMutationWriteResult> WritePublicationAsync(TypedSettingsPublication publication, CancellationToken token) =>
            inner.WritePublicationAsync(publication, token);
        public void Close(TypedSettingsOriginal original) => inner.Close(original);
    }
}
