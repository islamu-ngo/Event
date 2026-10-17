using Explore.Application.Configuration;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.InstanceOnboarding.Services;
using Explore.Application.Services;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Infrastructure.Authentication;
using Explore.Infrastructure.Services;
using Explore.Persistence;
using Explore.Persistence.Identity;
using Explore.Persistence.Repositories;
using Explore.Persistence.Services;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Event.Persistence.IntegrationTests.Identity;

public sealed partial class LocalBootstrapConvergenceTests
{
    [Test]
    public async Task SettingsCustodyRejectsUnrelatedPendingTenantRows()
    {
        await using Fixture fixture = await Fixture.CreateAsync(IdentityDatabaseTopology.External);
        await fixture.PrepareAsync();
        string originalSettings = await fixture.ReadSettingsAsync();
        await using var request = fixture.Provider.CreateAsyncScope();
        var operation = CreateSettingsCustodyOperation(fixture, request, out var custody);
        var originalContext = fixture.Application(request);
        Guid unrelatedTenant = Guid.CreateVersion7();
        custody.BeforeApply = () => originalContext.Set<Explore.Domain.TenantSetting>().Add(new Explore.Domain.TenantSetting
        {
            TenantId = unrelatedTenant,
            Tenant = null!,
            SettingKey = "unrelated.pending",
            Value = "\"pending\""
        });
        await Assert.That(async () => await operation.CompleteConfiguredAsync(
            new(AuthenticationProviderKind.Local, fixture.Subject.ToString("D")), fixture.Token))
            .Throws<UnauthorizedAccessException>();
        await using var scope = fixture.Provider.CreateAsyncScope();
        var observer = fixture.Application(scope);
        await Assert.That(await observer.Set<Explore.Domain.TenantSetting>()
            .IgnoreQueryFilters([QueryFilterNames.Tenant])
            .AnyAsync(row => row.TenantId == unrelatedTenant, fixture.Token)).IsFalse();
        await Assert.That(await fixture.ReadSettingsAsync()).IsEqualTo(originalSettings);
        await Assert.That(await observer.PlatformUserRoles.AnyAsync(fixture.Token)).IsFalse();
        await Assert.That((await new InstanceBootstrapStateRepository(observer).GetCurrent(fixture.Token))!
            .CompletedAt).IsNull();
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task SettingsCustodyUsesGenuineConfiguredLocalReceiptWithoutHumanLifetime(
        IdentityDatabaseTopology topology)
    {
        await using Fixture fixture = await Fixture.CreateAsync(topology);
        await fixture.PrepareAsync();
        await using var scope = fixture.Provider.CreateAsyncScope();
        var operation = CreateSettingsCustodyOperation(fixture, scope, out var custody);

        var response = await operation.CompleteConfiguredAsync(
            new(Explore.Domain.Enums.AuthenticationProviderKind.Local, fixture.Subject.ToString("D")), fixture.Token);

        await Assert.That(response.IsSuccess).IsTrue();
        await fixture.AssertCompletedAsync();
        await Assert.That(custody.Keys.Contains(GovernanceSettingKeys.Deployment.Mode)).IsTrue();
        await Assert.That(custody.Keys.Contains(GovernanceSettingKeys.Branding.DisplayName)).IsTrue();
        var context = fixture.Application(scope);
        var settings = SystemSettingRepository.CreateReadOnly(context,
            new RelationalSettingMutationLock(context, new EfCoreUnitOfWork(context)));
        await Assert.That((await settings.GetByKey(GovernanceSettingKeys.Deployment.Mode, fixture.Token))!.IsLocked).IsTrue();
        await Assert.That((await settings.GetByKey(GovernanceSettingKeys.Branding.DisplayName, fixture.Token))!.Value)
            .IsEqualTo("\"Bootstrap Operator\"");
        // A retained producer cannot be used again, even by its original consumer.
        await Assert.That(custody.ReplayAsync).Throws<UnauthorizedAccessException>();
    }

    [Test]
    public async Task SettingsCustodyRejectsConfiguredGenerationRotationAfterCredentialCreation()
    {
        await using Fixture fixture = await Fixture.CreateAsync(IdentityDatabaseTopology.External);
        await fixture.PrepareAsync();
        string originalSettings = await fixture.ReadSettingsAsync();
        await using var scope = fixture.Provider.CreateAsyncScope();
        var operation = CreateSettingsCustodyOperation(fixture, scope, out var custody);
        custody.BeforeApply = () => fixture.Configuration["INSTANCE_BOOTSTRAP_BINDING_GENERATION"] = "2";

        await Assert.That(async () =>
        {
            await operation.CompleteConfiguredAsync(
            new(AuthenticationProviderKind.Local, fixture.Subject.ToString("D")), fixture.Token);
        })
            .Throws<UnauthorizedAccessException>();

        await using var observation = fixture.Provider.CreateAsyncScope();
        var context = fixture.Application(observation);
        var current = await new InstanceBootstrapStateRepository(context).GetCurrent(fixture.Token);
        await Assert.That(current!.Status).IsEqualTo(InstanceBootstrapStatus.Pending);
        var credentials = await fixture.Store(observation).ListAsync(new(1, 10), fixture.Token);
        await Assert.That(credentials.Items.Single().CredentialState)
            .IsEqualTo(Explore.Application.Contracts.Identity.LocalCredentialState.ProvisioningPending);
        await Assert.That(await fixture.ReadSettingsAsync()).IsEqualTo(originalSettings);
        await Assert.That(await new PlatformUserRoleRepository(context).IsUserPlatformAdmin(fixture.Subject)).IsFalse();
    }

    [Test]
    public async Task SettingsCustodyRollsBackSettingsWhenOriginalRoleGrantFails()
    {
        await using Fixture fixture = await Fixture.CreateAsync(IdentityDatabaseTopology.External);
        await fixture.PrepareAsync();
        string originalSettings = await fixture.ReadSettingsAsync();
        await using var scope = fixture.Provider.CreateAsyncScope();
        var operation = CreateSettingsCustodyOperation(fixture, scope, out _);
        fixture.GrantFault.Armed = true;

        await Assert.That(async () =>
        {
            await operation.CompleteConfiguredAsync(
            new(AuthenticationProviderKind.Local, fixture.Subject.ToString("D")), fixture.Token);
        })
            .Throws<InjectedBootstrapFailure>();

        await using var observation = fixture.Provider.CreateAsyncScope();
        var context = fixture.Application(observation);
        await Assert.That(await fixture.ReadSettingsAsync()).IsEqualTo(originalSettings);
        await Assert.That((await new InstanceBootstrapStateRepository(context).GetCurrent(fixture.Token))!.Status)
            .IsEqualTo(InstanceBootstrapStatus.Pending);
    }

    [Test]
    public async Task SettingsCustodyRejectsAReplacementUnitOfWorkEvenOnTheSameContext()
    {
        await using Fixture fixture = await Fixture.CreateAsync(IdentityDatabaseTopology.External);
        await fixture.PrepareAsync();
        string originalSettings = await fixture.ReadSettingsAsync();
        await using var scope = fixture.Provider.CreateAsyncScope();
        var operation = CreateSettingsCustodyOperation(fixture, scope, out _, replaceOwner: true);

        await Assert.That(async () =>
        {
            await operation.CompleteConfiguredAsync(
            new(AuthenticationProviderKind.Local, fixture.Subject.ToString("D")), fixture.Token);
        })
            .Throws<UnauthorizedAccessException>();

        await using var observation = fixture.Provider.CreateAsyncScope();
        var context = fixture.Application(observation);
        await Assert.That(await fixture.ReadSettingsAsync()).IsEqualTo(originalSettings);
        await Assert.That((await new InstanceBootstrapStateRepository(context).GetCurrent(fixture.Token))!.Status)
            .IsEqualTo(InstanceBootstrapStatus.Pending);
    }

    [Test]
    public async Task SettingsCustodyRevokesAnUnconsumedCapsuleWhenItsProducerUnwinds()
    {
        await using Fixture fixture = await Fixture.CreateAsync(IdentityDatabaseTopology.External);
        await fixture.PrepareAsync();
        await using var scope = fixture.Provider.CreateAsyncScope();
        var operation = CreateSettingsCustodyOperation(fixture, scope, out var custody);
        custody.BeforeApply = () => throw new InjectedBootstrapFailure();

        await Assert.That(async () =>
        {
            await operation.CompleteConfiguredAsync(
            new(AuthenticationProviderKind.Local, fixture.Subject.ToString("D")), fixture.Token);
        })
            .Throws<InjectedBootstrapFailure>();
        await Assert.That(custody.ReplayAsync).Throws<UnauthorizedAccessException>();
    }

    private static LocalAdministratorBootstrapOperation CreateSettingsCustodyOperation(
        Fixture fixture, AsyncServiceScope scope, out SettingsCustodyProbe custody, bool replaceOwner = false)
    {
        var context = fixture.Application(scope);
        var unitOfWork = new EfCoreUnitOfWork(context);
        var mutationLock = new RelationalSettingMutationLock(context, unitOfWork);
        var settings = SystemSettingRepository.CreateReadOnly(context, mutationLock);
        var bootstrap = new InstanceBootstrapStateRepository(context);
        var operatorOptions = Options.Create(new InstanceOperatorIdentityOptions
        {
            OperatorId = fixture.Subject,
            PublicName = "Bootstrap Operator",
            LegalName = "Bootstrap Operator ASBL",
            OfficialOrigin = "https://example.test",
            OperatorKindCode = "registered_organization",
            JurisdictionCountryCode = "BE",
            RegistrationIdentifier = "BE 0123.456.789",
            PublicContactEmail = "contact@example.test",
            WebsiteUrl = "https://example.test",
            LegalNoticeUrl = "https://example.test/legal",
            TermsUrl = "https://example.test/terms",
            PrivacyUrl = "https://example.test/privacy"
        });
        var provider = new ConfiguredAdministratorBootstrapProvider(fixture.Configuration, operatorOptions, bootstrap);
        var store = fixture.Store(scope);
        var setup = scope.ServiceProvider.GetRequiredService<ISetupSecretProvider>();
        var deployment = scope.ServiceProvider.GetRequiredService<IDeploymentModeProvider>();
        var dispatcher = new RuntimeAuthenticationProviderDispatcher(settings,
            scope.ServiceProvider.GetRequiredService<IMemoryCache>(),
            Options.Create(new AuthenticationProviderDeploymentOptions()));
        var authority = new TrustedSetupSettingsAuthority(new HttpContextAccessor(), [],
            setup, scope.ServiceProvider.GetRequiredService<SetupReceiptCustody>().Credentials,
            dispatcher, [provider], TimeProvider.System);
        custody = new SettingsCustodyProbe(new TrustedSetupSettingsWriter(context, authority, mutationLock,
            replaceOwner ? new EfCoreUnitOfWork(context) : unitOfWork));
        var tenants = new TenantRepository(context);
        var users = new UserRepository(context);
        var logins = new UserExternalLoginRepository(context);
        var emails = new UserIdentityEmailRepository(context);
        var privacy = new PrivacyErasureStateRepository(context);
        var completion = new InstanceOnboardingCompletionOperation(bootstrap,
            new PlatformUserRoleRepository(context),
            new RoleRepository(context), users,
            new ActorRepository(context), logins, emails, privacy,
            new IdentityAccountResolver(users, logins, emails, privacy, Options.Create(new IdentityCorrelationOptions())),
            new IdentityEmailSynchronizationOperation(emails),
            settings, [provider], setup,
            new InstanceBootstrapAuditLogger(NullLogger<InstanceBootstrapAuditLogger>.Instance),
            deployment, new RuntimeMetadataRefresh(), NullLogger<InstanceOnboardingCompletionOperation>.Instance,
            unitOfWork, fixture.GenerationReader(scope), fixture.Configuration,
            new PrivacyIdentityFenceOperation(Substitute.For<Explore.Application.Contracts.PrivacyErasure.IPrivacyIdentityFenceAuthority>()),
            operatorOptions, custody);
        return new LocalAdministratorBootstrapOperation(bootstrap, provider, store, fixture.Secrets,
            completion, setup, deployment, unitOfWork, TimeProvider.System, dispatcher);
    }

    private sealed class SettingsCustodyProbe(ITrustedSetupSettingsWriter inner) : ITrustedSetupSettingsWriter
    {
        internal Action? BeforeApply { get; set; }
        internal IReadOnlyList<string> Keys { get; private set; } = [];
        private Func<Task>? replay;

        internal Task ReplayAsync() => replay!();

        public async Task<T> ExecuteAsync<T>(TrustedSetupSettingsApply<T> apply, CancellationToken cancellationToken)
        {
            Keys = apply.Effects.Select(effect => effect.Key).ToArray();
            replay = async () => { await inner.ExecuteAsync(apply, cancellationToken); };
            BeforeApply?.Invoke();
            return await inner.ExecuteAsync(apply, cancellationToken);
        }
    }

    /// <summary>
    /// Receipt observation uses native idle reconciliation contexts, never the context that
    /// currently owns settings enrollment. The parent scope owns and disposes this read scope.
    /// </summary>
    private sealed class SetupReceiptCustody : IAsyncDisposable
    {
        private readonly AsyncServiceScope scope;
        internal SetupReceiptCustody(Fixture fixture)
        {
            scope = fixture.Provider.CreateAsyncScope();
            Credentials = fixture.Store(scope);
        }

        internal LocalIdentityCredentialStateStore Credentials { get; }
        public ValueTask DisposeAsync() => scope.DisposeAsync();
    }
}
