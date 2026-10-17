
using EventVisitorCapabilitySqliteFixture = Event.Api.IntegrationTests.Authentication.ResourceVisitorAuthenticatedFixture;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Contracts.Services;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Event;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.DTOs.Instance;
using Explore.Application.DTOs.Settings;
using Explore.Application.Features.InstanceAdmin.Requests.Commands;
using Explore.Application.Features.ConfigurationManifest.Application;
using Explore.Application.Features.Events;
using Explore.Application.Features.InstanceOnboarding.Services;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Models.Common;
using Explore.Application.Features.Settings.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Settings;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

public sealed class VisitorAccessSettingsWriterTests
{
    private const string Conflict = "visitor_access_account_required_conflict";
    private static string ModeKey => GovernanceSettingKeys.PublicExperience.VisitorAccessMode;

    [Test]
    [Arguments("AnonymousOnly")]
    [Arguments("DirectoryListingOnly")]
    public async Task InstanceModeChange_RejectsInheritedAccountRequiredConfiguration(string mode)
    {
        await using var fixture = await ReadyAsync();
        await GrantAdministratorAsync(fixture);
        var entity = await fixture.SeedEventAsync(accountRequired: true);
        await using var selected = await fixture.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(PolicyMutationScope.Instance, CancellationToken.None);
        var preparation = selected.Services.GetRequiredService<IHumanCommitScopePreparation>();
        await preparation.PrepareAsync(CancellationToken.None);
        var result = await selected.Services.GetRequiredService<IVisitorAccessSettingsWriter>().ApplyAsync([new(null, ModeKey,
            VisitorAccessSettingMutationKind.SetValue, $"\"{mode}\"")],
            preparation.Subject ?? throw new InvalidOperationException("The original authenticated owner is required."),
            SettingsMutationOperation.InstanceSettings, fixture.UserId);
        await Assert.That(result.FailureCode).IsEqualTo(Conflict);
        await Assert.That(result.DeferredNotifications).IsEmpty();
        var stored = await fixture.Services.GetRequiredService<IEventParticipationConfigurationRepository>()
            .GetByEventAndTenantAsync(entity.Id, fixture.TenantId, CancellationToken.None);
        await Assert.That(stored!.ConcurrencyStamp).IsEqualTo(entity.ParticipationConfiguration!.ConcurrencyStamp);
        await Assert.That((await fixture.Services.GetRequiredService<IVisitorAccessCapabilityResolver>()
            .ResolveAsync(fixture.TenantId)).AllowsAccountRequiredParticipation).IsTrue();
    }

    [Test]
    public async Task InstanceProviderMutation_IncludesOtherTenantsWithNoOverrides()
    {
        await using var fixture = await ReadyAsync();
        await GrantAdministratorAsync(fixture);
        Guid otherTenantId = Guid.CreateVersion7();
        await using (var context = fixture.CreateIndependentContext())
        {
            var tenant = new Tenant
            {
                Id = otherTenantId,
                FullName = "Inherited visitor scope",
                Slug = $"inherited-{otherTenantId:N}",
                TenantStatusId = (int)TenantStatusEnum.Active,
                TenantStatus = null!
            };
            var entity = new Explore.Domain.Event
            {
                Id = Guid.CreateVersion7(),
                Title = "Other tenant registration",
                TenantId = otherTenantId,
                Tenant = tenant,
                ActorId = fixture.ActorId,
                Actor = null!,
                OrganizerActorId = fixture.ActorId,
                EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
                VisibilityTypeId = (int)VisibilityTypeEnum.Public,
                VisibilityType = null!,
                EventFormatId = (int)EventFormatEnum.Local,
                EventFormat = null!,
                EventStatus = null!
            };
            entity.ParticipationConfiguration = EventParticipationConfiguration.Create(entity.Id, otherTenantId,
                (int)ParticipationHandlingModeEnum.PlatformManaged, (int)AdvanceRegistrationObligationEnum.Required,
                (int)IdentityAccessModeEnum.AccountRequired, null, DateTime.UtcNow);
            await new EventRepository(context).Create(entity);
        }
        await Assert.ThrowsAsync<Explore.Application.Exceptions.ConcurrencyConflictException>(() =>
            ProviderService(fixture).ApplyConfigurationAsync(ProviderCommand(fixture,
                new() { PrimaryProviderId = (int)AuthenticationProviderKind.Local })));
        await Assert.That((await fixture.Services.GetRequiredService<ISystemSettingRepository>()
            .GetByKey(GovernanceSettingKeys.Authentication.GoogleSsoEnabled))!.Value).IsEqualTo("true");
        await Assert.That(await fixture.Services.GetRequiredService<ITenantSettingRepository>().GetAllForTenant(otherTenantId)).IsEmpty();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task LastProviderRemovalAndPublication_ShareOuterLeaseBeforeEitherSnapshot(bool publicationFirst)
    {
        await using var fixture = await ReadyAsync();
        await GrantAdministratorAsync(fixture);
        var entity = await fixture.SeedEventAsync(accountRequired: true);
        await using var selected = await fixture.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(PolicyMutationScope.Instance, CancellationToken.None);
        var writerServices = selected.Services;
        var writerContext = writerServices.GetRequiredService<ExploreDbContext>();
        var writerOwner = writerServices.GetRequiredService<IUnitOfWork>();
        var subject = writerServices.GetRequiredService<IHumanCommitScopePreparation>().Subject
            ?? throw new InvalidOperationException("The original authenticated owner is required.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var firstOwnsKeys = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var contenderArrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        string lastKey = VisitorAccessCapabilityResolver.AuthoritySettingKeys[^1];
        string firstKey = VisitorAccessCapabilityResolver.AuthoritySettingKeys[0];
        async Task HolderHook(string key, CancellationToken token)
        {
            if (key != lastKey) return;
            firstOwnsKeys.TrySetResult();
            await release.Task.WaitAsync(token);
        }
        Task ContenderHook(string key, CancellationToken token)
        {
            if (key == firstKey) contenderArrived.TrySetResult();
            return Task.CompletedTask;
        }
        var eventLock = new RelationalSettingMutationLock(fixture.Context, new EfCoreUnitOfWork(fixture.Context),
            publicationFirst ? HolderHook : ContenderHook);
        var writerLock = new RelationalSettingMutationLock(writerContext, writerOwner,
            publicationFirst ? ContenderHook : HolderHook);
        var publisher = ActivatorUtilities.CreateInstance<EventPublicationExecutor>(fixture.Services, eventLock);
        var writer = new VisitorAccessSettingsWriter(writerContext, writerLock, writerOwner,
            writerServices.GetRequiredService<IEventParticipationConfigurationRepository>(),
            writerServices.GetRequiredService<IConfiguration>(),
            writerServices.GetRequiredService<IInstanceAdministratorCommitAuthority>(),
            writerServices.GetRequiredService<ITenantAdministratorCommitAuthority>(),
            writerServices.GetRequiredService<IHumanActionCommitFinalizer>(), subject,
            writerServices.GetRequiredService<IHumanSettingsMutationPreparation>());
        Task<BaseCommandResponse<Guid>> Publish() => publisher.ExecuteAsync(entity.Id,
            new PublishEventRequestDto { ExpectedConcurrencyStamp = entity.ConcurrencyStamp },
            EventPublicationMode.PrivilegedApproval, timeout.Token);
        Task<VisitorAccessSettingsWriteResult> Remove() => writerLock.ExecuteOrderedGroupsAsync(
            [VisitorAccessCapabilityResolver.AuthoritySettingKeys],
            token => writer.ApplyAsync(
                [new(null, GovernanceSettingKeys.Authentication.GoogleSsoEnabled, VisitorAccessSettingMutationKind.SetValue, "false")],
                subject, SettingsMutationOperation.InstanceSettings, fixture.UserId, token), timeout.Token);
        Task<BaseCommandResponse<Guid>>? publishing = null;
        Task<VisitorAccessSettingsWriteResult>? removing = null;
        if (publicationFirst) publishing = Publish(); else removing = Remove();
        await firstOwnsKeys.Task.WaitAsync(timeout.Token);
        if (publicationFirst) removing = Remove(); else publishing = Publish();
        try
        {
            await contenderArrived.Task.WaitAsync(timeout.Token);
            await Assert.That(fixture.Context.Database.CurrentTransaction).IsNull();
            await Assert.That(writerContext.Database.CurrentTransaction).IsNull();
        }
        finally
        {
            release.TrySetResult();
        }
        var published = await publishing!.WaitAsync(timeout.Token);
        var removed = await removing!.WaitAsync(timeout.Token);
        await Assert.That(published.IsSuccess).IsTrue().Because(published.FailureCode ?? "publication");
        await Assert.That(removed.FailureCode).IsEqualTo(Conflict);
        await Assert.That(removed.DeferredNotifications).IsEmpty();
        var persisted = await fixture.Services.GetRequiredService<IEventRepository>().GetById(entity.Id);
        await Assert.That(persisted!.EventStatusId).IsEqualTo((int)EventStatusEnum.Published);
        await Assert.That((await fixture.Services.GetRequiredService<ISystemSettingRepository>()
            .GetByKey(GovernanceSettingKeys.Authentication.GoogleSsoEnabled))!.Value).IsEqualTo("true");
    }

    [Test]
    [Arguments("scalar")]
    [Arguments("batch")]
    [Arguments("control-plane")]
    public async Task NativeSettingHandlers_RejectUnsafeVisitorModeWithoutPartialEffects(string surface)
    {
        await using var fixture = await ReadyAsync();
        await GrantAdministratorAsync(fixture);
        var entity = await fixture.SeedEventAsync(accountRequired: true);
        if (surface == "batch")
        {
            var result = await fixture.Services.GetRequiredService<Explore.Application.Contracts.Operations.ICommandHandler<UpdateSettingBatchCommand, BatchUpdateResponseDto>>().ExecuteAsync(new()
            {
                Category = SettingRegistry.Get(ModeKey)!.Category,
                Scope = SettingScope.Instance,
                Mode = BatchUpdateMode.Strict,
                Values = new Dictionary<string, string>
                {
                    [ModeKey] = "AnonymousOnly",
                    [GovernanceSettingKeys.PublicExperience.EventCatalogLabel] = "Uncommitted label"
                }
            }, CancellationToken.None);
            await Assert.That(result.Success).IsFalse();
            await Assert.That(result.Results.All(item => !item.Applied)).IsTrue();
            await Assert.That(await fixture.Services.GetRequiredService<ISystemSettingRepository>()
                .GetByKey(GovernanceSettingKeys.PublicExperience.EventCatalogLabel)).IsNull();
        }
        else
        {
            BaseCommandResponse<Guid> result = surface == "scalar"
                ? await fixture.Services.GetRequiredService<Explore.Application.Contracts.Operations.ICommandHandler<UpdateSettingCommand, BaseCommandResponse<Guid>>>().ExecuteAsync(new()
                { Key = ModeKey, Value = "AnonymousOnly", Scope = SettingScope.Instance }, CancellationToken.None)
                : await fixture.Services.GetRequiredService<Explore.Application.Contracts.Operations.ICommandHandler<SetInstanceTenantSettingCommand, BaseCommandResponse<Guid>>>()
                    .ExecuteAsync(new(fixture.TenantId, ModeKey, "AnonymousOnly"), CancellationToken.None);
            await Assert.That(result.FailureCode).IsEqualTo(Conflict);
        }
        var stored = await fixture.Services.GetRequiredService<IEventParticipationConfigurationRepository>()
            .GetByEventAndTenantAsync(entity.Id, fixture.TenantId, CancellationToken.None);
        await Assert.That(stored!.ConcurrencyStamp).IsEqualTo(entity.ParticipationConfiguration!.ConcurrencyStamp);
    }

    [Test]
    [Arguments("reset")]
    [Arguments("lock")]
    [Arguments("unlock")]
    public async Task InheritanceTransitions_AreEvaluatedAgainstAffectedEvents(string operation)
    {
        await using var fixture = await ReadyAsync();
        await GrantAdministratorAsync(fixture);
        bool unlocking = operation == "unlock";
        fixture.Context.SystemSettings.Add(new SystemSetting
        {
            Id = Guid.CreateVersion7(),
            SettingKey = ModeKey,
            Value = unlocking ? "\"FullRegistrationAndAuth\"" : "\"AnonymousOnly\"",
            ValueType = SettingValueType.String,
            IsLocked = unlocking,
            CreatedAt = fixture.Clock.GetUtcNow().UtcDateTime
        });
        fixture.Context.Set<TenantSetting>().Add(new TenantSetting
        {
            Id = Guid.CreateVersion7(),
            TenantId = fixture.TenantId,
            Tenant = null!,
            SettingKey = ModeKey,
            Value = unlocking ? "\"AnonymousOnly\"" : "\"FullRegistrationAndAuth\"",
            CreatedAt = fixture.Clock.GetUtcNow().UtcDateTime
        });
        await fixture.Context.SaveChangesAsync();
        await fixture.SeedEventAsync(accountRequired: true);
        BaseCommandResponse<Guid> result = operation switch
        {
            "reset" => await fixture.Services.GetRequiredService<Explore.Application.Contracts.Operations.ICommandHandler<ResetSettingCommand, BaseCommandResponse<Guid>>>().ExecuteAsync(new()
            { Key = ModeKey, Scope = SettingScope.Tenant }, CancellationToken.None),
            "lock" => await fixture.Services.GetRequiredService<Explore.Application.Contracts.Operations.ICommandHandler<LockSettingCommand, BaseCommandResponse<Guid>>>().ExecuteAsync(new()
            { Key = ModeKey, Scope = SettingScope.Instance }, CancellationToken.None),
            _ => await fixture.Services.GetRequiredService<Explore.Application.Contracts.Operations.ICommandHandler<UnlockSettingCommand, BaseCommandResponse<Guid>>>().ExecuteAsync(new()
            { Key = ModeKey, Scope = SettingScope.Instance }, CancellationToken.None)
        };
        await Assert.That(result.FailureCode).IsEqualTo(Conflict);
        await Assert.That((await fixture.Services.GetRequiredService<IVisitorAccessCapabilityResolver>()
            .ResolveAsync(fixture.TenantId)).AllowsAccountRequiredParticipation).IsTrue();
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task ControlPlaneLockTransitions_PreserveUsableInheritedProviderPolicy(bool locking)
    {
        await using var fixture = await ReadyAsync();
        await GrantAdministratorAsync(fixture);
        fixture.Context.Set<TenantSetting>().Add(new TenantSetting
        {
            Id = Guid.CreateVersion7(),
            TenantId = fixture.TenantId,
            Tenant = null!,
            SettingKey = ModeKey,
            Value = "\"FullRegistrationAndAuth\"",
            IsLocked = !locking,
            CreatedAt = fixture.Clock.GetUtcNow().UtcDateTime
        });
        await fixture.Context.SaveChangesAsync();
        await fixture.SeedEventAsync(accountRequired: true);
        var result = locking
            ? await fixture.Services.GetRequiredService<Explore.Application.Contracts.Operations.ICommandHandler<LockInstanceTenantSettingCommand, BaseCommandResponse<Guid>>>()
                .ExecuteAsync(new(fixture.TenantId, ModeKey), CancellationToken.None)
            : await fixture.Services.GetRequiredService<Explore.Application.Contracts.Operations.ICommandHandler<UnlockInstanceTenantSettingCommand, BaseCommandResponse<Guid>>>()
                .ExecuteAsync(new(fixture.TenantId, ModeKey), CancellationToken.None);
        await Assert.That(result.IsSuccess).IsTrue().Because(result.FailureCode ?? "tenant lock");
        var stored = await fixture.Services.GetRequiredService<ITenantSettingRepository>().GetByTenantAndKey(fixture.TenantId, ModeKey);
        await Assert.That(stored!.IsLocked).IsEqualTo(locking);
        await Assert.That(stored.Value).IsEqualTo("\"FullRegistrationAndAuth\"");
    }

    [Test]
    public async Task ProviderReplacement_IsValidatedAsCompleteBatchAndKeepsOperatorAuthenticationIndependent()
    {
        await using var fixture = await ReadyAsync();
        await GrantAdministratorAsync(fixture);
        await fixture.SeedEventAsync(accountRequired: true);
        await ProviderService(fixture).ApplyConfigurationAsync(ProviderCommand(fixture, new()
        {
            PrimaryProviderId = (int)AuthenticationProviderKind.Local,
            AtprotoLoginEnabled = true,
            AtprotoPublicUrl = "https://events.example.test"
        }));
        var capability = await fixture.Services.GetRequiredService<IVisitorAccessCapabilityResolver>().ResolveAsync(fixture.TenantId);
        await Assert.That(capability.AllowsAccountRequiredParticipation).IsTrue();
        await Assert.That(capability.SignupDestinations.Single().Provider).IsEqualTo(AuthenticationProviderKind.Atproto);
        var authentication = await fixture.Services.GetRequiredService<IAuthProviderConfigurationService>().ReadConfigurationAsync();
        await Assert.That(authentication.PrimaryProviderId).IsEqualTo((int)AuthenticationProviderKind.Local);
    }

    [Test]
    public async Task RepositoryBypassesRejectVisitorKeysBeforeAnyBatchWrite_AndUnrelatedWritesRemainAvailable()
    {
        await using var fixture = await ReadyAsync();
        var systems = fixture.Services.GetRequiredService<ISystemSettingRepository>();
        var tenants = fixture.Services.GetRequiredService<ITenantSettingRepository>();
        var setting = new SystemSetting { SettingKey = ModeKey, Value = "\"AnonymousOnly\"" };
        await Assert.ThrowsAsync<InvalidOperationException>(() => systems.UpsertAsync(setting));
        await Assert.ThrowsAsync<InvalidOperationException>(() => systems.UpsertLockAsync(setting));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Services.GetRequiredService<IUnitOfWork>()
            .ExecuteSerializableAsync(token => systems.UpsertInCurrentTransactionAsync(setting, token)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => tenants.SetValueAsync(fixture.TenantId, ModeKey, setting.Value));
        await Assert.ThrowsAsync<InvalidOperationException>(() => tenants.RemoveOverrideAsync(fixture.TenantId, ModeKey));
        await Assert.ThrowsAsync<InvalidOperationException>(() => tenants.LockAsync(fixture.TenantId, ModeKey, fixture.UserId));
        await Assert.ThrowsAsync<InvalidOperationException>(() => tenants.UnlockAsync(fixture.TenantId, ModeKey, fixture.UserId));
        await Assert.ThrowsAsync<InvalidOperationException>(() => tenants.UpsertManyForTenantAsync(fixture.TenantId,
            [new(GovernanceSettingKeys.PublicExperience.EventCatalogLabel, "\"No partial write\"", false),
             new(ModeKey, setting.Value, false)], fixture.UserId));
        await Assert.ThrowsAsync<InvalidOperationException>(() => tenants.CreateManyForTenantAsync(fixture.TenantId,
            [new(GovernanceSettingKeys.PublicExperience.EventCatalogLabel, "\"No partial write\"", false),
             new(ModeKey, setting.Value, false)], fixture.UserId, DateTime.UtcNow));
        await Assert.That(await tenants.GetByTenantAndKey(fixture.TenantId, GovernanceSettingKeys.PublicExperience.EventCatalogLabel)).IsNull();
        await GrantAdministratorAsync(fixture);
        var update = await fixture.Services.GetRequiredService<ICommandHandler<UpdateSettingCommand, BaseCommandResponse<Guid>>>()
            .ExecuteAsync(new()
            {
                Scope = SettingScope.Tenant,
                Key = GovernanceSettingKeys.PublicExperience.EventCatalogLabel,
                Value = "Community events"
            }, CancellationToken.None);
        await Assert.That(update.IsSuccess).IsTrue();
        await Assert.That((await tenants.GetByTenantAndKey(fixture.TenantId, GovernanceSettingKeys.PublicExperience.EventCatalogLabel))!.Value)
            .IsEqualTo("\"Community events\"");
    }

    [Test]
    public async Task MissingOuterOwnership_IsRejectedBeforeVisitorMutationEvenInsideSerializableTransaction()
    {
        await using var fixture = await ReadyAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Services.GetRequiredService<IUnitOfWork>()
            .ExecuteSerializableAsync(token => Writer(fixture).ApplyInternalAsync(
                [new(null, ModeKey, VisitorAccessSettingMutationKind.SetValue, "\"AnonymousOnly\"")], fixture.UserId, token)));
        await Assert.That(await fixture.Services.GetRequiredService<ISystemSettingRepository>().GetByKey(ModeKey)).IsNull();
    }

    [Test]
    public async Task ReadConfigurationResolvesNormalizedPrimaryProviderMetadata()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        await GrantAdministratorAsync(fixture);
        var service = ProviderService(fixture);
        await service.ApplyConfigurationAsync(ProviderCommand(fixture,
            new() { PrimaryProviderId = (int)AuthenticationProviderKind.Local }));
        AuthProviderConfigurationDto result = await service.ReadConfigurationAsync();
        await Assert.That(result.PrimaryProviderId).IsEqualTo((int)AuthenticationProviderKind.Local);
        await Assert.That(result.PrimaryProviderCode).IsEqualTo("local");
        await Assert.That(result.PrimaryProviderName).IsEqualTo("Local Identity");
    }

    [Test]
    public async Task ApplyConfigurationPersistsPrimaryProviderAsLookupIdentifier()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        await GrantAdministratorAsync(fixture);
        await ProviderService(fixture).ApplyConfigurationAsync(ProviderCommand(fixture,
            new() { PrimaryProviderId = (int)AuthenticationProviderKind.Local }));
        var setting = await fixture.Services.GetRequiredService<ISystemSettingRepository>()
            .GetByKey(GovernanceSettingKeys.Authentication.PrimaryProviderId);
        await Assert.That(setting!.Value).IsEqualTo("4");
    }

    [Test]
    public async Task ReadConfigurationForAtprotoPrimaryForcesAtprotoLoginEnabled()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        fixture.Context.SystemSettings.AddRange(
            new SystemSetting
            {
                Id = Guid.CreateVersion7(),
                SettingKey = GovernanceSettingKeys.Authentication.PrimaryProviderId,
                Value = "2",
                ValueType = SettingValueType.Integer,
                CreatedAt = fixture.Clock.GetUtcNow().UtcDateTime
            },
            new SystemSetting
            {
                Id = Guid.CreateVersion7(),
                SettingKey = GovernanceSettingKeys.Authentication.AtprotoLoginEnabled,
                Value = "false",
                ValueType = SettingValueType.Boolean,
                CreatedAt = fixture.Clock.GetUtcNow().UtcDateTime
            });
        await fixture.Context.SaveChangesAsync();
        AuthProviderConfigurationDto result = await ProviderService(fixture).ReadConfigurationAsync();
        await Assert.That(result.PrimaryProviderId).IsEqualTo((int)AuthenticationProviderKind.Atproto);
        await Assert.That(result.PrimaryProviderCode).IsEqualTo("atproto");
        await Assert.That(result.PrimaryProviderName).IsEqualTo("AT Protocol");
        await Assert.That(result.AtprotoLoginEnabled).IsTrue();
    }

    [Test]
    public async Task ApplyAtprotoPrimaryPersistsEnabledAtprotoAxis()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        await GrantAdministratorAsync(fixture);
        await ProviderService(fixture).ApplyConfigurationAsync(ProviderCommand(fixture, new()
        {
            PrimaryProviderId = (int)AuthenticationProviderKind.Atproto,
            AtprotoLoginEnabled = false,
            AtprotoPublicUrl = "https://events.example.test"
        }));
        await Assert.That((await fixture.Services.GetRequiredService<ISystemSettingRepository>()
            .GetByKey(GovernanceSettingKeys.Authentication.AtprotoLoginEnabled))!.Value).IsEqualTo("true");
    }

    [Test]
    public async Task ProviderRoot_RejectsLastPathRemovalAndPreservesUnchangedCompleteGroupFields()
    {
        await using var fixture = await ReadyAsync();
        await GrantAdministratorAsync(fixture);
        await fixture.SeedEventAsync(accountRequired: true);
        var service = ProviderService(fixture);
        AuthProviderConfigurationDto previous = await service.ReadConfigurationAsync();
        var configuration = new AuthProviderConfigurationWriteDto
        {
            PrimaryProviderId = previous.PrimaryProviderId,
            LockPrimaryProvider = previous.LockPrimaryProvider,
            KeycloakAuthority = previous.KeycloakAuthority,
            KeycloakClientId = previous.KeycloakClientId,
            AtprotoLoginEnabled = previous.AtprotoLoginEnabled,
            AtprotoPublicUrl = previous.AtprotoPublicUrl,
            GoogleSsoEnabled = previous.GoogleSsoEnabled,
            GoogleClientId = previous.GoogleClientId,
            LockAtprotoLoginEnabled = previous.LockAtprotoLoginEnabled,
            LockGoogleSsoEnabled = previous.LockGoogleSsoEnabled,
            KeycloakPublicOnboardingPolicy = previous.KeycloakPublicOnboardingPolicy,
            KeycloakPublicSignupUrl = previous.KeycloakPublicSignupUrl,
            GooglePublicOnboardingPolicy = previous.GooglePublicOnboardingPolicy,
            GooglePublicSignupUrl = previous.GooglePublicSignupUrl
        };
        await Assert.ThrowsAsync<Explore.Application.Exceptions.ConcurrencyConflictException>(() =>
            service.ApplyConfigurationAsync(ProviderCommand(fixture, configuration with
            { GoogleSsoEnabled = false, KeycloakAuthority = "https://uncommitted.example.test" })));
        await Assert.That(await fixture.Services.GetRequiredService<ISystemSettingRepository>()
            .GetByKey(GovernanceSettingKeys.Authentication.KeycloakAuthority)).IsNull();
        await service.ApplyConfigurationAsync(ProviderCommand(fixture, configuration with
        { KeycloakAuthority = "https://configured.example.test" }));
        AuthProviderConfigurationDto after = await service.ReadConfigurationAsync();
        await Assert.That(after.GoogleSsoEnabled).IsTrue();
        await Assert.That(after.GooglePublicOnboardingPolicy).IsEqualTo(PublicOnboardingPolicy.Allowed);
        await Assert.That(after.GooglePublicSignupUrl).IsEqualTo(previous.GooglePublicSignupUrl);
        await Assert.That(after.KeycloakAuthority).IsEqualTo("https://configured.example.test");
    }

    [Test]
    public async Task PlanAssignment_RejectsUnsafeModeAndRollsBackOtherPlanSettings()
    {
        await using var fixture = await ReadyAsync();
        await GrantAdministratorAsync(fixture);
        await fixture.SeedEventAsync(accountRequired: true);
        var repository = fixture.Services.GetRequiredService<ITenantPlanRepository>();
        var now = DateTime.UtcNow;
        var plan = new TenantPlan { Id = Guid.CreateVersion7(), Key = $"visitor-{Guid.CreateVersion7():N}", DisplayName = "Visitor plan", CreatedAt = now };
        var version = new TenantPlanVersion
        {
            Id = Guid.CreateVersion7(),
            TenantPlan = plan,
            TenantPlanId = plan.Id,
            VersionNumber = 1,
            TenantPlanStatusId = (int)TenantPlanStatusEnum.Published,
            CurrencyCode = "EUR",
            BillingPeriod = "monthly",
            IsActiveForProvisioning = true,
            CreatedAt = now,
            Settings = [new() { Id = Guid.CreateVersion7(), SettingKey = ModeKey, JsonValue = "\"AnonymousOnly\"", CreatedAt = now },
                new() { Id = Guid.CreateVersion7(), SettingKey = GovernanceSettingKeys.PublicExperience.EventCatalogLabel,
                    JsonValue = "\"Uncommitted plan label\"", CreatedAt = now }]
        };
        var assignment = await repository.CreateAssignmentAsync(new TenantPlanAssignment
        {
            Id = Guid.CreateVersion7(),
            TenantId = fixture.TenantId,
            Tenant = null!,
            TenantPlan = plan,
            TenantPlanId = plan.Id,
            TenantPlanVersion = version,
            TenantPlanVersionId = version.Id,
            TenantPlanAssignmentStatusId = (int)TenantPlanAssignmentStatusEnum.Active,
            AssignedByUserId = fixture.UserId,
            AssignedAt = now,
            CreatedAt = now
        });
        var result = await fixture.Services.GetRequiredService<Explore.Application.Contracts.Operations.ICommandHandler<ApplyInstanceTenantPlanAssignmentCommand, BaseCommandResponse<Guid>>>()
            .ExecuteAsync(new(fixture.TenantId, assignment.Id, fixture.UserId), CancellationToken.None);
        await Assert.That(result.FailureCode).IsEqualTo(Conflict);
        await Assert.That(await fixture.Services.GetRequiredService<ITenantSettingRepository>()
            .GetByTenantAndKey(fixture.TenantId, GovernanceSettingKeys.PublicExperience.EventCatalogLabel)).IsNull();
        await Assert.That((await repository.GetAssignmentAsync(assignment.Id))!.UpdatedAt).IsNull();
    }

    [Test]
    public async Task ManifestBoundary_DoesNotBroadenProhibitedVisitorKeys_AndAllowsExistingCatalogWrites()
    {
        await using var fixture = await ReadyAsync();
        var boundary = fixture.Services.GetRequiredService<IConfigurationManifestInstanceSettingMutationBoundary>();
        var unitOfWork = fixture.Services.GetRequiredService<IUnitOfWork>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => unitOfWork.ExecuteSerializableAsync(token =>
            boundary.ApplyInCurrentTransactionAsync(new(
                [new(ModeKey, "\"AnonymousOnly\""),
                 new(GovernanceSettingKeys.PublicExperience.EventCatalogLabel, "\"Not committed\"")], fixture.UserId, DateTime.UtcNow), token)));
        var systems = fixture.Services.GetRequiredService<ISystemSettingRepository>();
        await Assert.That(await systems.GetByKey(GovernanceSettingKeys.PublicExperience.EventCatalogLabel)).IsNull();
        var result = await unitOfWork.ExecuteSerializableAsync(token => boundary.ApplyInCurrentTransactionAsync(new(
            [new(GovernanceSettingKeys.PublicExperience.EventCatalogLabel, "\"Manifest events\"")], fixture.UserId, DateTime.UtcNow), token));
        await Assert.That(result.Success).IsTrue();
        await Assert.That((await systems.GetByKey(GovernanceSettingKeys.PublicExperience.EventCatalogLabel))!.Value)
            .IsEqualTo("\"Manifest events\"");
    }

    private static AuthProviderConfigurationService ProviderService(EventVisitorCapabilitySqliteFixture fixture)
    {
        IConfiguration configuration = new ConfigurationBuilder().Build();
        var unitOfWork = new EfCoreUnitOfWork(fixture.Context);
        var mutationLock = new RelationalSettingMutationLock(fixture.Context, unitOfWork);
        return new AuthProviderConfigurationService(SystemSettingRepository.CreateReadOnly(fixture.Context, mutationLock), configuration,
            fixture.Services.GetRequiredService<Explore.Application.Contracts.Operations.IPolicyMutationExecutionScopeFactory>(),
            new KeycloakConnectionResolver(fixture.Services.GetRequiredService<ISecretResolver>(), configuration),
            fixture.Services.GetRequiredService<ISecretResolver>());
    }

    private static UpdateAuthProviderConfigurationCommand ProviderCommand(
        EventVisitorCapabilitySqliteFixture fixture, AuthProviderConfigurationWriteDto configuration) => new()
        {
            UserId = fixture.UserId,
            Patch = new() { Configuration = OptionalUpdate<AuthProviderConfigurationWriteDto>.Set(configuration) }
        };

    private static IVisitorAccessSettingsWriter Writer(EventVisitorCapabilitySqliteFixture fixture) =>
        fixture.Services.GetRequiredService<IVisitorAccessSettingsWriter>();

    private static async Task<EventVisitorCapabilitySqliteFixture> ReadyAsync()
    {
        var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        try
        {
            foreach (var (key, value, type) in new[]
            {
                (GovernanceSettingKeys.Authentication.GoogleSsoEnabled, "true", SettingValueType.Boolean),
                (GovernanceSettingKeys.Authentication.GoogleClientId, "\"public-client\"", SettingValueType.String),
                (GovernanceSettingKeys.Authentication.GooglePublicOnboardingPolicy, "\"Allowed\"", SettingValueType.String),
                (GovernanceSettingKeys.Authentication.GooglePublicSignupUrl, "\"https://accounts.example.test/signup\"", SettingValueType.String)
            })
            {
                fixture.Context.SystemSettings.Add(new SystemSetting
                {
                    Id = Guid.CreateVersion7(),
                    SettingKey = key,
                    Value = value,
                    ValueType = type,
                    CreatedAt = fixture.Clock.GetUtcNow().UtcDateTime
                });
            }
            await fixture.Context.SaveChangesAsync();
            return fixture;
        }
        catch
        {
            await fixture.DisposeAsync();
            throw;
        }
    }

    private static async Task GrantAdministratorAsync(EventVisitorCapabilitySqliteFixture fixture)
    {
        var role = await fixture.Context.Roles.SingleAsync(role => role.MasterCode == "platform.admin");
        await fixture.Services.GetRequiredService<IPlatformUserRoleRepository>().Create(new PlatformUserRole
        {
            Id = Guid.CreateVersion7(),
            UserId = fixture.UserId,
            User = null!,
            RoleId = role.Id,
            Role = role,
            GrantedAt = DateTime.UtcNow
        });
        Guid tenantUserId = await fixture.Context.TenantUsers.Where(user => user.UserId == fixture.UserId)
            .Select(user => user.Id).SingleAsync();
        await fixture.Services.GetRequiredService<ITenantUserRoleGrantRepository>().Create(new TenantUserRoleGrant
        {
            Id = Guid.CreateVersion7(),
            TenantId = fixture.TenantId,
            Tenant = null!,
            TenantUserId = tenantUserId,
            TenantUser = null!,
            RoleId = (int)RoleEnum.TenantAdmin,
            Role = null!,
            RoleScopeId = (int)RoleScopeEnum.Tenant,
            GrantedAt = DateTime.UtcNow
        });
    }
}
