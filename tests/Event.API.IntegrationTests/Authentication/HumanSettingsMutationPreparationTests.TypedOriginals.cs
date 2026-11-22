using System.Collections.Immutable;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Instance;
using Explore.Application.DTOs.TenantPolicy;
using Explore.Application.DTOs.TenantSettings;
using Explore.Application.Features.InstanceOnboarding.Handlers.Commands;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Features.Settings;
using Explore.Application.Features.Announcements;
using Explore.Application.Features.Announcements.Requests.Commands;
using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Application.Features.Settings.Requests.Commands;
using Explore.Application.Features.TenantOnboarding.Handlers.Commands;
using Explore.Application.Features.TenantOnboarding.Requests.Commands;
using Explore.Application.Models.Common;
using Explore.Application.Notifications;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Settings;
using Explore.Domain.Settings.Documents;
using Explore.Persistence;
using Explore.Persistence.Identity;
using Explore.Persistence.Security;
using Explore.Persistence.Seed;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ISLAMU.Wire.Contracts.ConfigurationPortability;

namespace Event.Api.IntegrationTests.Authentication;

public sealed partial class HumanSettingsMutationPreparationTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AcquiredAnnouncementImportRejectsSelfAttestingFactoryAndStore(bool substituteFactory)
    {
        await using var fixture = await ResourceVisitorAuthenticatedFixture.CreateAsync(
            configureServices: services =>
            {
                if (substituteFactory)
                    services.Replace(ServiceDescriptor.Scoped<IHumanCommitScopePreparation>(provider =>
                        new SelfAttestingAnnouncementFactory(
                            (IHumanCommitScopePreparation)provider.GetRequiredService<IPolicyMutationExecutionScopeFactory>())));
            });
        var context = fixture.Context;
        var member = await context.TenantUsers.SingleAsync(row =>
            row.TenantId == fixture.TenantId && row.UserId == fixture.UserId);
        context.TenantUserRoleGrants.Add(new TenantUserRoleGrant
        {
            Id = Guid.CreateVersion7(), TenantId = fixture.TenantId, Tenant = null!,
            TenantUserId = member.Id, TenantUser = member,
            RoleId = (int)RoleEnum.TenantAdmin, Role = null!, RoleScopeId = (int)RoleScopeEnum.Tenant,
            GrantedAt = fixture.Clock.GetUtcNow().UtcDateTime
        });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var human = fixture.Services.GetRequiredService<IHumanCommitScopePreparation>();
        await human.PrepareAsync(CancellationToken.None);
        var subject = human.Subject ?? throw new InvalidOperationException("The real login must bind its native subject.");
        var target = ConfigurationImportTarget.ForTenant(fixture.TenantId);
        var issuer = fixture.Services.GetRequiredService<IHumanConfigurationImportAdmission>();
        var prepared = await issuer.PrepareAsync(subject, target, null, CancellationToken.None);
        var nativeFactory = fixture.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>();
        await Assert.That(ReferenceEquals(subject, ((IHumanCommitScopePreparation)nativeFactory).Subject)).IsTrue();
        var announcementFactory = substituteFactory
            ? (IHumanConfigurationImportAnnouncementFactory)human
            : (IHumanConfigurationImportAnnouncementFactory)nativeFactory;
        var parser = fixture.Services.GetRequiredService<ConfigurationImportArtifactParser>();
        string key = GovernanceSettingKeys.PublicExperience.AnnouncementBarPublication;
        var package = new TenantConfigurationPackageV1Alpha2
        {
            Schema = TenantConfigurationPackageContractMetadata.SchemaId,
            ApiVersion = TenantConfigurationPackageContractMetadata.ApiVersion,
            Kind = TenantConfigurationPackageContractMetadata.Kind,
            Metadata = new()
            {
                Name = "announcement-import-regression",
                Source = new() { TenantName = "announcement-import-regression", InstanceName = "regression" }
            },
            Spec = new()
            {
                DisplayName = "Announcement import regression",
                Documents = new Dictionary<string, ConfigurationManifestDocumentV1Alpha2>(StringComparer.Ordinal),
                Settings = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                {
                    [key] = JsonSerializer.SerializeToElement(new
                    {
                        operation = "publish", enabled = true, message = "Owned import regression",
                        linkText = "", linkUrl = "", redisplay = true
                    })
                }
            }
        };
        var bytes = TenantConfigurationPackageSerializer.Serialize(package);
        var parsed = parser.ParseTenantPackage(bytes);
        var request = new ConfigurationImportPreviewRequest
        {
            SelectedSectionKeys = ["tenant.settings"], Mappings = new Dictionary<string, string>(),
            ApplyMode = ConfigurationImportApplyMode.ApplySelected, GrantedApprovalCodes = []
        };
        var compiler = fixture.Services.GetRequiredService<ConfigurationImportSectionApplier>();
        string? before = await context.Set<TenantSetting>().AsNoTracking()
            .Where(row => row.TenantId == fixture.TenantId && row.SettingKey == key)
            .Select(row => row.Value).SingleOrDefaultAsync();
        bool acquired = false, attemptingOriginal = false, returnedCompletion = false;
        async Task Execute()
        {
            await ((ISerializableAttemptOwner)fixture.Services.GetRequiredService<IUnitOfWork>())
                .ExecuteAsync(async token =>
                {
                    await prepared.AcquireAsync(token);
                    acquired = true;
                    var locks = await compiler.CaptureLockGroupsAsync(target, bytes, request, parser, token);
                    attemptingOriginal = true;
                    var sections = await compiler.CaptureOriginalApplyAsync(Guid.CreateVersion7(), target, bytes,
                        request, prepared.Authority, fixture.Clock.GetUtcNow().UtcDateTime, parsed.Sha256Digest,
                        parser, (IHumanConfigurationImportPublicationFactory)nativeFactory,
                        announcementFactory, locks, token);
                    var apply = compiler.PrepareAdmittedApply(sections, cancellationToken =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        fixture.Services.GetRequiredService<IHumanOriginalCredentialCustody>()
                            .RequireOriginalCredential(prepared.Authority.Lifetime);
                        return Task.CompletedTask;
                    });
                    await prepared.ExecuteApplyAsync(apply, token);
                    returnedCompletion = true;
                    return true;
                }, CancellationToken.None);
        }
        if (substituteFactory)
            await Assert.ThrowsAsync<UnauthorizedAccessException>(Execute);
        else
            await Execute();
        await Assert.That(acquired).IsTrue();
        await Assert.That(attemptingOriginal).IsTrue();
        await Assert.That(returnedCompletion).IsEqualTo(!substituteFactory);
        await using var observer = fixture.CreateIndependentContext();
        observer.TenantContext = new SqliteGovernanceTenant(fixture.TenantId);
        observer.BindTenantLease();
        var value = await observer.Set<TenantSetting>().AsNoTracking()
            .Where(row => row.TenantId == fixture.TenantId && row.SettingKey == key)
            .Select(row => row.Value).SingleOrDefaultAsync();
        if (substituteFactory)
            await Assert.That(value).IsEqualTo(before);
        else
        {
            var group = AnnouncementSettingValueCodec.DeserializeGroup(value)
                ?? throw new InvalidOperationException("The genuine import must persist its publication.");
            await Assert.That(group.Publication?.Material.Message).IsEqualTo("Owned import regression");
            await Assert.That(group.Enabled).IsTrue();
        }
    }

    private sealed class SelfAttestingAnnouncementFactory(IHumanCommitScopePreparation original)
        : IHumanCommitScopePreparation, IHumanConfigurationImportAnnouncementFactory
    {
        public HumanCommitSubject? Subject => original.Subject;
        public ValueTask PrepareAsync(CancellationToken cancellationToken) => original.PrepareAsync(cancellationToken);
        public void RequireAnnouncementStore(IHumanConfigurationImportAnnouncementStore store) { }
        public Task<IHumanConfigurationImportAnnouncementStore> ObserveAnnouncementAsync(
            HumanConfigurationImportAuthority authority, Guid operationId, string artifactDigest,
            ImmutableArray<AnnouncementImportIntent> intents, DateTime occurredAt, CancellationToken cancellationToken) =>
            Task.FromResult<IHumanConfigurationImportAnnouncementStore>(new UntrustedAnnouncementImportStore());
    }

    [Test]
    public async Task AnnouncementImportFactoryRejectsPublicNoOpCompletion()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await using var request = await Request.CreateAsync(fixture);
        var factory = (IHumanConfigurationImportAnnouncementFactory)request.Services
            .GetRequiredService<IPolicyMutationExecutionScopeFactory>();
        await Assert.That(() => factory.RequireAnnouncementStore(new UntrustedAnnouncementImportStore()))
            .Throws<UnauthorizedAccessException>();
    }

    private sealed class UntrustedAnnouncementImportStore : IHumanConfigurationImportAnnouncementStore
    {
        public Task RequireCurrentAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task RequireCompletedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<ImmutableArray<HumanConfigurationImportSettingEffect>> CompileAsync(
            HumanConfigurationImportApply original, ImmutableArray<HumanConfigurationImportAnnouncement> announcements,
            CancellationToken cancellationToken) => Task.FromResult(ImmutableArray<HumanConfigurationImportSettingEffect>.Empty);
        public Task<ImmutableArray<SettingChangedNotification>> ApplyAsync(
            HumanConfigurationImportAnnouncement announcement, CancellationToken cancellationToken) =>
            Task.FromResult(ImmutableArray<SettingChangedNotification>.Empty);
    }

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
    [Arguments("material-change-without-redisplay")]
    public async Task ActualOnboardingCompilesCleanupNormalizationEditionAndWholeTransaction(string outcome)
    {
        bool committed = outcome is "commit" or "material-change-without-redisplay";
        var observation = new TypedObservation();
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(
            configureServices: services => ConfigureTypedObservation(services, observation));
        await SeedMixedTenantAdministratorAsync(fixture);
        Guid tenant = PlatformDefaults.DefaultTenantId;
        string cleanupKey = GovernanceSettingKeys.Domains.TenantCustomDomain;
        string revisionKey = GovernanceSettingKeys.PublicExperience.AnnouncementBarPublication;
        var originalGroup = new AnnouncementGroup(
            Guid.CreateVersion7(), true, new(Guid.CreateVersion7(), new("Previous announcement", "", "")));
        var originalAnnouncement = new AnnouncementSourceSnapshot(AnnouncementOwner.ForTenant(tenant), originalGroup, false);
        string originalValue = AnnouncementSettingValueCodec.SerializeGroup(originalGroup);
        await using (var seed = fixture.Native.CreateDatabase())
        {
            seed.TenantContext = new SqliteGovernanceTenant(tenant);
            seed.BindTenantLease();
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
                new TenantSetting { TenantId = tenant, Tenant = null!, SettingKey = revisionKey, Value = originalValue },
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
                Announcement = AnnouncementTransportMapper.ToDto(new AnnouncementUpdate(
                    new AnnouncementChange.Publish(new("  original message  ", "", ""), true,
                        outcome != "material-change-without-redisplay"), originalAnnouncement)),
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
        if (committed)
            await Assert.That((await handler.ExecuteAsync(command, CancellationToken.None)).IsSuccess).IsTrue();
        else
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => handler.ExecuteAsync(command, CancellationToken.None));
        await Assert.That(sawWholeTransaction).IsTrue();
        await using var independent = fixture.Native.CreateDatabase();
        independent.TenantContext = new SqliteGovernanceTenant(tenant);
        independent.BindTenantLease();
        var values = await independent.TenantSettingOverrides.AsNoTracking().Where(row => row.TenantId == tenant)
            .ToDictionaryAsync(row => row.SettingKey, row => row.Value);
        if (committed)
            await Assert.That(values[revisionKey]).IsNotEqualTo(originalValue);
        else
            await Assert.That(values[revisionKey]).IsEqualTo(originalValue);
        await Assert.That(values.ContainsKey(cleanupKey)).IsEqualTo(!committed);
        await Assert.That(values.ContainsKey(GovernanceSettingKeys.Events.RequireApproval)).IsEqualTo(!committed);
        if (committed)
        {
            await Assert.That(values[GovernanceSettingKeys.AiAssistant.AllowedModelIds]).IsEqualTo("[\"model-A\",\"model-B\"]");
            var published = AnnouncementSettingValueCodec.DeserializeGroup(values[revisionKey])
                ?? throw new InvalidOperationException("The original compiler must persist a complete group.");
            await Assert.That(published.Publication?.Edition).IsNotEqualTo(originalGroup.Publication?.Edition);
            await Assert.That(published.Publication?.Material.Message).IsEqualTo("original message");
        }
        await Assert.That(await independent.Set<TenantOnboardingState>().AnyAsync(row => row.TenantId == tenant)).IsEqualTo(committed);
        await Assert.That(await independent.Set<TenantSettingsDocument>().CountAsync(row => row.TenantId == tenant
                && (row.DocumentKey == SettingsDocumentKeys.Tenant.DirectoryOperatorIdentity
                    || row.DocumentKey == SettingsDocumentKeys.Tenant.Branding))).IsEqualTo(committed ? 2 : 0);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    [Arguments("idempotent")]
    [Arguments("stale-source")]
    [Arguments("marker-conflict")]
    public async Task ActualAnnouncementDismissalRetainsSourceAndExactMarkerPreimage(string outcome)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await SeedMixedTenantAdministratorAsync(fixture);
        Guid tenant = PlatformDefaults.DefaultTenantId;
        await using var request = await Request.CreateAsync(fixture);
        var scopes = request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>();

        async Task<AnnouncementPublicationResult> Publish(AnnouncementSourceSnapshot expected, string message)
        {
            await using var selected = await scopes.CreateAsync(PolicyMutationScope.MixedSettingsTenant(tenant), CancellationToken.None);
            var handler = ActivatorUtilities.CreateInstance<UpdateTenantAnnouncementCommandHandler>(selected.Services,
                (IEnumerable<INotificationHandler<SettingChangedNotification>>)[]);
            return await handler.ExecuteAsync(new UpdateTenantAnnouncementCommand(
                tenant, fixture.Binding.LocalSubjectId,
                new(new AnnouncementChange.Publish(new(message, "", ""), true, false), expected)),
                CancellationToken.None);
        }

        async Task<AnnouncementDismissalState> Observe()
        {
            await using var selected = await scopes.CreateAsync(new(tenant), CancellationToken.None);
            return await selected.Services.GetRequiredService<AnnouncementEditionResolver>().ResolveSelfAsync(CancellationToken.None);
        }

        async Task<AnnouncementDismissalResult> Dismiss(AnnouncementObservation observed, AnnouncementDismissalMarker? expected)
        {
            await using var selected = await scopes.CreateAsync(new(tenant), CancellationToken.None);
            var handler = ActivatorUtilities.CreateInstance<DismissAnnouncementCommandHandler>(selected.Services);
            return await handler.ExecuteAsync(new(fixture.Binding.LocalSubjectId, observed, expected), CancellationToken.None);
        }

        var firstPublication = await Publish(new(AnnouncementOwner.ForTenant(tenant), null, false), "First publication");
        await Assert.That(firstPublication.Outcome).IsEqualTo(AnnouncementPublicationOutcome.Applied);
        var firstState = await Observe();
        var firstObservation = firstState.Current
            ?? throw new InvalidOperationException("The real publication must be observable.");
        await Assert.That(firstState.Marker).IsNull();
        var firstDismissal = await Dismiss(firstObservation, null);
        await Assert.That(firstDismissal.Decision).IsEqualTo(AnnouncementDismissalDecision.Apply);
        var firstMarker = firstDismissal.Marker
            ?? throw new InvalidOperationException("Typed dismissal must persist its marker.");

        AnnouncementDismissalResult result;
        if (outcome == "idempotent")
        {
            result = await Dismiss(firstObservation, null);
            await Assert.That(result.Decision).IsEqualTo(AnnouncementDismissalDecision.AlreadyDismissed);
        }
        else
        {
            var replacement = await Publish(firstPublication.Source, "Replacement publication");
            await Assert.That(replacement.Outcome).IsEqualTo(AnnouncementPublicationOutcome.Applied);
            var current = await Observe();
            var currentObservation = current.Current
                ?? throw new InvalidOperationException("The replacement must be observable.");
            result = outcome == "stale-source"
                ? await Dismiss(firstObservation, firstMarker)
                : await Dismiss(currentObservation, null);
            await Assert.That(result.Decision).IsEqualTo(outcome == "stale-source"
                ? AnnouncementDismissalDecision.SourceChanged : AnnouncementDismissalDecision.MarkerChanged);
        }
        await Assert.That(result.Marker).IsEqualTo(firstMarker);
        await Assert.That((await Observe()).Marker).IsEqualTo(firstMarker);
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
        public Task<(AnnouncementPublicationResult Result, ImmutableArray<SettingChangedNotification> DeferredNotifications)>
            ApplyAnnouncementAsync(TypedSettingsOriginal original, CancellationToken token) =>
                inner.ApplyAnnouncementAsync(original, token);
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
