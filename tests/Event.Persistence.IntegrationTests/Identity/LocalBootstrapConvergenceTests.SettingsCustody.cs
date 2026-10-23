using Explore.Application.Configuration;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Identity;
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
using Explore.Persistence.Security;
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
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task SettingsCustodyRejectsReceiptDisposalAfterFinalSourceCheckBeforeCommit(
        IdentityDatabaseTopology topology)
    {
        await using Fixture fixture = await Fixture.CreateAsync(topology);
        await fixture.PrepareAsync();
        string originalSettings = await fixture.ReadSettingsAsync();
        await using var scope = fixture.Provider.CreateAsyncScope();
        var context = fixture.Application(scope);
        context.BindTenantLease();
        var account = new ProviderAccountKey(AuthenticationProviderKind.Local, fixture.Subject.ToString("D"));
        var receipts = scope.ServiceProvider.GetRequiredService<SetupReceiptCustody>().Source;
        await using var retainedHuman = new BorrowedHumanReceiptSource(context, receipts);
        var operation = CreateSettingsCustodyOperation(fixture, scope, out var custody,
            receiptSource: retainedHuman);
        bool disposedBeforeSyncGuard = false;
        bool synchronousGuardAdmittedDisposedReceipt = false;
        custody.AfterApply = source =>
        {
            var receipt = source.LocalReceiptLease
                ?? throw new InvalidOperationException("The actual completion must retain its genuine receipt.");
            var snapshot = source.Local
                ?? throw new InvalidOperationException("The actual completion must retain its full snapshot.");
            var originalTransaction = context.Database.CurrentTransaction
                ?? throw new InvalidOperationException("The actual writer must retain its original transaction.");
            // Appended after the actual writer's source callback: revoke only after its
            // final async check, while its synchronous commit callback is still pending.
            context.RegisterTrustedSetupSettingsAdmission(async token =>
            {
                await receipts.RequireCurrentAsync(receipt, snapshot, token);
                await receipt.DisposeAsync();
                HumanSecuritySourceGate.RequireCurrent(context);
                await Assert.That(ReferenceEquals(originalTransaction, context.Database.CurrentTransaction)).IsTrue();
                disposedBeforeSyncGuard = true;
            }, () =>
            {
                // Reached only if the real synchronous guard wrongly admits the disposed
                // receipt. Release this test-owned SQLite Human lease so the buggy path's
                // later Local activation can finish rather than waiting on the test itself.
                synchronousGuardAdmittedDisposedReceipt = true;
                retainedHuman.DisposeAsync().AsTask().GetAwaiter().GetResult();
            });
        };
        Exception? failure = null;
        try
        {
            await operation.CompleteConfiguredAsync(account, fixture.Token);
        }
        catch (Exception exception) { failure = exception; }

        string persistedSettings = await fixture.ReadSettingsAsync();
        await using var observation = fixture.Provider.CreateAsyncScope();
        bool persistedAdministrator = await fixture.Application(observation).PlatformUserRoles.AnyAsync(fixture.Token);
        string outcome = $"Observed exception: {failure?.ToString() ?? "<none>"}; "
            + $"sync guard admitted disposed receipt: {synchronousGuardAdmittedDisposedReceipt}; "
            + $"committed platform administrator: {persistedAdministrator}.";
        await Assert.That(disposedBeforeSyncGuard).IsTrue().Because(outcome);
        await Assert.That(context.Database.CurrentTransaction).IsNull().Because(outcome);
        await Assert.That(persistedSettings).IsEqualTo(originalSettings).Because(outcome);
        await Assert.That(persistedAdministrator).IsFalse().Because(outcome);
        await Assert.That(failure is UnauthorizedAccessException).IsTrue().Because(outcome);
        await Assert.That(synchronousGuardAdmittedDisposedReceipt).IsFalse().Because(outcome);
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
        Fixture fixture, AsyncServiceScope scope, out SettingsCustodyProbe custody, bool replaceOwner = false,
        ILocalBootstrapReceiptSource? receiptSource = null)
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
        context.BindTenantLease();
        var receipts = receiptSource ?? scope.ServiceProvider.GetRequiredService<SetupReceiptCustody>().Source;
        var enrollment = new EventDiscoveryEnrollmentGate(context);
        var setup = scope.ServiceProvider.GetRequiredService<ISetupSecretProvider>();
        var deployment = scope.ServiceProvider.GetRequiredService<IDeploymentModeProvider>();
        var dispatcher = new RuntimeAuthenticationProviderDispatcher(settings,
            scope.ServiceProvider.GetRequiredService<IMemoryCache>(),
            Options.Create(new AuthenticationProviderDeploymentOptions()));
        var authority = new TrustedSetupSettingsAuthority(new HttpContextAccessor(), [],
            setup, receipts,
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
            enrollment, receipts,
            operatorOptions, custody);
        return new LocalAdministratorBootstrapOperation(bootstrap, provider, new BootstrapLocalScopes(fixture), fixture.Secrets,
            completion, setup, deployment, unitOfWork, TimeProvider.System, dispatcher, enrollment);
    }

    private sealed class SettingsCustodyProbe(ITrustedSetupSettingsWriter inner) : ITrustedSetupSettingsWriter
    {
        internal Action? BeforeApply { get; set; }
        internal Action<TrustedSetupSettingsSource>? AfterApply { get; set; }
        internal IReadOnlyList<string> Keys { get; private set; } = [];
        private Func<Task>? replay;

        internal Task ReplayAsync() => replay!();

        public async Task<T> ExecuteAsync<T>(TrustedSetupSettingsApply<T> apply, CancellationToken cancellationToken)
        {
            Keys = apply.Effects.Select(effect => effect.Key).ToArray();
            replay = async () => { await inner.ExecuteAsync(apply, cancellationToken); };
            BeforeApply?.Invoke();
            T result = await inner.ExecuteAsync(apply, cancellationToken);
            AfterApply?.Invoke(apply.Source);
            return result;
        }
    }

    private sealed class BorrowedHumanReceiptSource(
        ExploreDbContext context, ILocalBootstrapReceiptSource inner) : ILocalBootstrapReceiptSource, IAsyncDisposable
    {
        private readonly HumanSecuritySourceGate human = new(context);

        public async Task<ILocalBootstrapReceiptLease> AcquireAsync(
            LocalCredentialProvisioningSnapshot expected, CancellationToken cancellationToken)
        {
            _ = await human.AcquireAsync(cancellationToken);
            return await inner.AcquireAsync(expected, cancellationToken);
        }

        public Task RequireCurrentAsync(ILocalBootstrapReceiptLease lease,
            LocalCredentialProvisioningSnapshot expected, CancellationToken cancellationToken) =>
            inner.RequireCurrentAsync(lease, expected, cancellationToken);

        public void RequireHeld(ILocalBootstrapReceiptLease lease, LocalCredentialProvisioningSnapshot expected) =>
            inner.RequireHeld(lease, expected);

        public ValueTask DisposeAsync() => human.DisposeAsync();
    }

    /// <summary>
    /// Receipt observation uses native idle reconciliation contexts, never the context that
    /// currently owns settings enrollment. The parent scope owns and disposes this read scope.
    /// </summary>
    private sealed class SetupReceiptCustody : IAsyncDisposable
    {
        internal SetupReceiptCustody(Fixture fixture, ExploreDbContext original)
        {
            using var descriptorScope = fixture.Provider.CreateScope();
            var options = new Explore.Secrets.Database.PrimaryDatabaseConnectionOptions
            {
                Role = Explore.Secrets.Database.PrimaryDatabaseRole.LocalIdentityWriter,
                Provider = Explore.Secrets.Database.PrimaryDatabaseProvider.Sqlite,
                Database = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(original.Database.GetConnectionString()).DataSource
            };
            var identity = descriptorScope.ServiceProvider.GetService<ExternalIdentityDbContext>();
            var external = identity is not null
                ? new ExternalIdentityDatabaseDescriptor(Explore.Secrets.Database.PrimaryDatabaseProvider.Sqlite,
                    identity.Database.GetConnectionString()!,
                    IdentityDatabaseConfiguration.DefaultSchema, null, null) : null;
            Source = new ConfiguredLocalBootstrapReceiptSource(original, () => new LocalIdentityWriterDbContextFactory(options, external, poolSize: 1));
        }

        internal ConfiguredLocalBootstrapReceiptSource Source { get; }
        public ValueTask DisposeAsync() => Source.DisposeAsync();
    }

    private sealed class BootstrapLocalScopes(Fixture fixture) : ILocalIdentityExecutionScopeFactory
    {
        public ValueTask<ILocalIdentityExecutionScope> CreateAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var scope = fixture.Provider.CreateAsyncScope();
            fixture.Application(scope).BindTenantLease();
            return ValueTask.FromResult<ILocalIdentityExecutionScope>(new BootstrapLocalScope(scope, fixture.Store(scope)));
        }
    }

    private sealed class BootstrapLocalScope(AsyncServiceScope scope, LocalIdentityCredentialStateStore credentials)
        : ILocalIdentityExecutionScope, IServiceProvider
    {
        public IServiceProvider Services => this;
        public object? GetService(Type serviceType) => serviceType == typeof(ILocalCredentialAdministration)
            ? credentials : scope.ServiceProvider.GetService(serviceType);
        public ValueTask DisposeAsync() => scope.DisposeAsync();
    }
}
