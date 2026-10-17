using System.Text.Json;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Exceptions;
using Explore.Application.Features.InstanceOnboarding.Handlers.Commands;
using Explore.Application.Features.InstanceOnboarding.Handlers.Queries;
using Explore.Application.Features.InstanceOnboarding.Queries;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Features.InstanceOnboarding.Requests.Queries;
using Explore.Application.Features.InstanceOnboarding.Services;
using Explore.Application.Models;
using Explore.Application.Onboarding;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Event.Application.UnitTests.Features.InstanceOnboarding;

public sealed class InstanceOnboardingCompletionOperationTests
{
    [Test]
    [Arguments(null, "https://established.example.test:9443/community", "https://established.example.test:9443/community/")]
    [Arguments("https://configured.example.test/prefix", "https://established.example.test", "https://configured.example.test/prefix/")]
    [Arguments("ftp://invalid.example.test", "https://established.example.test", null)]
    [Arguments(null, null, null)]
    public async Task BackgroundAddress_UsesOnlyOverrideOrAuthorizedSetupState(string? configured, string? established, string? expected)
    {
        var scenario = new OnboardingCompletionScenario(interactive: true);
        scenario.HostConfiguration["PublicBaseUrl"] = "";
        scenario.HostConfiguration["PUBLIC_BASE_URL"] = configured;
        scenario.HostConfiguration["ASPNETCORE_URLS"] = "http://0.0.0.0:8080";
        if (established is not null) scenario.ChangeSetting(GovernanceSettingKeys.Domains.PublicBaseUrl, established);
        var address = await Explore.Application.Configuration.PublicAddressResolver.ResolveAsync(
            scenario.HostConfiguration, scenario.SystemSettings, CancellationToken.None);
        await Assert.That(address?.AbsoluteUri).IsEqualTo(expected);
        await Assert.That(scenario.CommittedWrites).IsEmpty();
    }

    [Test]
    [Arguments(false, false, false, false)]
    [Arguments(true, false, false, false)]
    [Arguments(true, true, false, true)]
    [Arguments(true, true, true, false)]
    public async Task EmailAddressWarning_IsCapabilitySpecificAndNeverBlocksLaunch(bool smtp, bool enabled, bool established, bool warning)
    {
        var scenario = new OnboardingCompletionScenario(interactive: true);
        scenario.HostConfiguration["PublicBaseUrl"] = "";
        if (smtp) scenario.ChangeSetting(GovernanceSettingKeys.Email.SmtpHost, "smtp.example.test");
        if (enabled) scenario.ChangeSetting(GovernanceSettingKeys.Email.DeliveryEnabled, "true");
        if (established) scenario.ChangeSetting(GovernanceSettingKeys.Domains.PublicBaseUrl, "https://events.example.test");
        var preflight = await scenario.Preflight.QueryAsync(new(), CancellationToken.None);
        await Assert.That(preflight.WarningChecks.Any(check => check.Code == "email_public_address")).IsEqualTo(warning);
        await Assert.That(preflight.BlockingChecks.Any(check => check.Code is "public_host" or "email_public_address")).IsFalse();
    }

    [Test]
    [Arguments(false, true)]
    [Arguments(true, false)]
    public async Task OnlySubdomainRoutingRequiresAnExplicitBaseDomain(bool subdomain, bool valid)
    {
        var validator = new Explore.Application.DTOs.Onboarding.Validators.ResolverConfigurationDtoValidator();
        var result = await validator.ValidateAsync(new ResolverConfigurationDto
        {
            HeaderEnabled = true,
            PathEnabled = true,
            PathPrefix = "/t",
            SubdomainEnabled = subdomain
        });
        await Assert.That(result.IsValid).IsEqualTo(valid);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task Journey_ResolvesPublicAddressInternally(bool configured)
    {
        var scenario = new OnboardingCompletionScenario(interactive: true);
        scenario.HostConfiguration["PublicBaseUrl"] = configured ? "https://platform.example.test" : "";
        var journey = await scenario.Journey.QueryAsync(new(), CancellationToken.None);
        await Assert.That(journey.Profile!.PublicUrl).IsEqualTo(configured ? "https://platform.example.test" : null);
    }

    [Test]
    public async Task InteractiveCompletion_FencesChangedDeploymentOrigin()
    {
        var scenario = new OnboardingCompletionScenario(interactive: true);
        var command = await scenario.InteractiveCommandAsync();
        scenario.HostConfiguration["PUBLIC_BASE_URL"] = "https://changed.example.test";
        var handler = new CompleteInstanceOnboardingCommandHandler(
            scenario.BootstrapRepository, scenario.UserRepository, scenario.DeploymentModeProvider, scenario.Operation);
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => handler.ExecuteAsync(command, CancellationToken.None));
        await Assert.That(scenario.CommittedWrites).IsEmpty();
    }

    [Test]
    [Arguments(GovernanceSettingKeys.Branding.DisplayName)]
    [Arguments(GovernanceSettingKeys.Security.AuthorizationProvider)]
    [Arguments(Explore.Application.Settings.InstanceOperatorIdentitySettingKeys.OperatorIdentity)]
    public async Task InteractiveCompletion_FencesDurableChangesAfterJourneyAdmission(string key)
    {
        var scenario = new OnboardingCompletionScenario(interactive: true);
        var command = await scenario.InteractiveCommandAsync();
        scenario.ChangeSetting(key, "changed");
        var handler = new CompleteInstanceOnboardingCommandHandler(
            scenario.BootstrapRepository, scenario.UserRepository, scenario.DeploymentModeProvider, scenario.Operation);
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => handler.ExecuteAsync(command, CancellationToken.None));
        await Assert.That(scenario.CommittedWrites).IsEmpty();
        await Assert.That(scenario.Bootstrap.Status).IsEqualTo(InstanceBootstrapStatus.Pending);
        await Assert.That(scenario.ExternalReadinessCallsInsideTransaction).IsEqualTo(0);
    }

    [Test]
    [Arguments(DeploymentMode.SingleTenant, "")]
    [Arguments(DeploymentMode.MultiTenant, "")]
    [Arguments(DeploymentMode.MultiTenant, "https://events.example.test")]
    public async Task Journey_DoesNotRequirePublicAddressForLaunch(DeploymentMode mode, string publicUrl)
    {
        var scenario = new OnboardingCompletionScenario(interactive: true);
        scenario.DeploymentModeProvider.Mode = mode;
        scenario.HostConfiguration["PublicBaseUrl"] = "";
        scenario.HostConfiguration["PUBLIC_BASE_URL"] = publicUrl;
        scenario.HostConfiguration["ASPNETCORE_URLS"] = "http://0.0.0.0:8080";
        var preflight = await scenario.Preflight.QueryAsync(new(), CancellationToken.None);
        await Assert.That(preflight.BlockingChecks.Any(check => check.Code == "public_host")).IsFalse();
        await Assert.That(preflight.WarningChecks.Any(check => check.Code == "dns_wildcard_tenant")).IsFalse();
    }

    [Test]
    public async Task Journey_ReusesDurableProfileWithoutAdditionalSettingQueries()
    {
        var scenario = new OnboardingCompletionScenario(interactive: true);
        scenario.ChangeSetting(GovernanceSettingKeys.Branding.DisplayName, "Snapshot site");
        scenario.ChangeSetting(GovernanceSettingKeys.Branding.SupportEmail, "support@example.test");
        scenario.HostConfiguration["PublicBaseUrl"] = "";
        scenario.ChangeSetting(GovernanceSettingKeys.Domains.PublicBaseUrl, "https://example.test:9443/events");
        scenario.ChangeSetting(GovernanceSettingKeys.Localization.DefaultLanguage, "fr");
        var journey = await scenario.Journey.QueryAsync(new(), CancellationToken.None);
        await Assert.That(journey.State).IsEqualTo("Available");
        await Assert.That(journey.Profile!.SiteName).IsEqualTo("Snapshot site");
        await Assert.That(journey.Profile.SupportEmail).IsEqualTo("support@example.test");
        await Assert.That(journey.Profile.PublicUrl).IsEqualTo("https://example.test:9443/events");
        await Assert.That(journey.Profile.Locale).IsEqualTo("fr");
        await Assert.That(scenario.FullSettingsReads).IsEqualTo(2);
        await Assert.That(scenario.SettingKeysRead).DoesNotContain(GovernanceSettingKeys.Branding.DisplayName);
        await Assert.That(scenario.SettingKeysRead).DoesNotContain(GovernanceSettingKeys.Branding.SupportEmail);
        await Assert.That(scenario.SettingKeysRead).DoesNotContain(GovernanceSettingKeys.Localization.DefaultLanguage);
        await Assert.That(scenario.SettingKeysRead).DoesNotContain(GovernanceSettingKeys.Domains.InstanceBaseDomain);
    }

    [Test]
    public async Task Journey_RejectsDurableChangeDuringExternalReadiness()
    {
        var scenario = new OnboardingCompletionScenario(interactive: true);
        scenario.DuringExternalReadiness = () => scenario.ChangeSetting(GovernanceSettingKeys.Branding.DisplayName, "changed");
        var journey = await scenario.Journey.QueryAsync(new(), CancellationToken.None);
        await Assert.That(journey.ReasonCode).IsEqualTo("snapshot_changed");
        await Assert.That(journey.Generation).IsNull();
    }

    [Test]
    public async Task Generation_LocalReservationPreservesAuthorityButCompletionChangesIt()
    {
        var scenario = new OnboardingCompletionScenario(interactive: true);
        var unreserved = await scenario.GenerationReader.ReadAsync(null, CancellationToken.None);
        var reserved = await scenario.GenerationReader.ReadAsync(scenario.Bootstrap, CancellationToken.None);
        var journey = await scenario.Journey.QueryAsync(new(), CancellationToken.None);
        await Assert.That(reserved).IsEqualTo(unreserved);
        await Assert.That(journey.Generation).IsEqualTo(reserved);
        scenario.Bootstrap.CompleteInteractive(scenario.UserId, DateTime.UtcNow);
        await Assert.That(await scenario.GenerationReader.ReadAsync(scenario.Bootstrap, CancellationToken.None)).IsNotEqualTo(reserved);
    }

    [Test]
    public async Task EmptyConfiguredUserId_ReturnsBoundedFailureWithoutWritesOrEffects()
    {
        var scenario = new OnboardingCompletionScenario();
        var response = await scenario.ClaimAsync(Guid.Empty);
        await Assert.That(response.IsSuccess).IsFalse();
        await Assert.That(response.FailureCode).IsEqualTo("configured_administrator_identity_incomplete");
        await Assert.That(response.Id).IsEqualTo(Guid.Empty);
        await Assert.That(scenario.CommittedWrites).IsEmpty();
        await Assert.That(scenario.PostCommitEffects).IsEmpty();
    }

    [Test]
    public async Task ConfiguredClaim_MismatchedVerifiedBindingCannotEnterAuthorityTransfer()
    {
        var scenario = new OnboardingCompletionScenario
        {
            BindingAccount = new ProviderAccountKey(AuthenticationProviderKind.Atproto, "did:plc:different-configured-account")
        };
        var rejected = await scenario.ClaimAsync();
        await Assert.That(rejected.IsSuccess).IsFalse();
        await Assert.That(rejected.FailureCode).IsEqualTo("configured_administrator_claim_mismatch");
        await Assert.That(scenario.Bootstrap.Status).IsEqualTo(InstanceBootstrapStatus.Pending);
        await Assert.That(scenario.Bootstrap.CompletedByUserId).IsNull();
        await Assert.That(scenario.Users).IsEmpty();
        await Assert.That(scenario.CommittedWrites).IsEmpty();
        await Assert.That(scenario.PostCommitEffects).IsEmpty();
    }
}

/// <summary>
/// Read-only source composition for Journey and pre-write rejection. Successful enrollment
/// belongs to LocalBootstrapConvergenceTests in Persistence, with the real owner and writer.
/// Any attempted repository mutation fails immediately; this helper cannot simulate a commit.
/// </summary>
internal sealed class OnboardingCompletionScenario
{
    internal const string Fingerprint = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    internal const string OtherFingerprint = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private readonly List<SystemSetting> settings = [];
    private readonly List<string> attemptedWrites = [];
    private readonly List<Guid> attemptedUsers = [];
    private readonly List<string> effects = [];
    private readonly ReadOnlyUnitOfWork unitOfWork = new();

    public OnboardingCompletionScenario(bool interactive = false, AuthenticationProviderKind providerKind = AuthenticationProviderKind.Keycloak)
    {
        UserId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000222");
        Account = new ProviderAccountKey(providerKind, providerKind == AuthenticationProviderKind.Local ? UserId.ToString("D") : "subject-123");
        BindingAccount = Account;
        Bootstrap = interactive
            ? InstanceBootstrapState.CreateInteractivePending(Guid.CreateVersion7(), DeploymentMode.SingleTenant, DateTime.UtcNow)
            : InstanceBootstrapState.CreateConfiguredAdministratorPending(Guid.CreateVersion7(), providerKind,
                DeploymentMode.SingleTenant, 7, OtherFingerprint, Fingerprint, DateTime.UtcNow);
        BootstrapRepository = Substitute.For<IInstanceBootstrapStateRepository>();
        BootstrapRepository.GetCurrent(Arg.Any<CancellationToken>()).Returns(_ => Bootstrap);
        BootstrapRepository.GetCurrentForUpdate(Arg.Any<CancellationToken>()).Returns(_ => Bootstrap);
        BootstrapRepository.Create(Arg.Any<InstanceBootstrapState>()).Returns(_ => RejectWrite<InstanceBootstrapState>("bootstrap"));
        BootstrapRepository.Update(Arg.Any<InstanceBootstrapState>()).Returns(_ => RejectWrite<Task>("bootstrap"));
        UserRepository = Substitute.For<IUserRepository>();
        UserRepository.Create(Arg.Any<User>()).Returns(call =>
        {
            attemptedUsers.Add(call.Arg<User>()!.Id);
            return RejectWrite<User>("user");
        });
        SystemSettings = Substitute.For<ISystemSettingRepository>();
        SystemSettings.GetAllSettings(Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            FullSettingsReads++;
            return settings.ToList();
        });
        SystemSettings.GetByKey(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            SettingKeysRead.Add(call.Arg<string>()!);
            return settings.SingleOrDefault(setting => setting.SettingKey == call.Arg<string>());
        });
        SystemSettings.UpsertAsync(Arg.Any<SystemSetting>(), Arg.Any<CancellationToken>())
            .Returns(_ => RejectWrite<string>("system-setting"));
        var tenants = Substitute.For<ITenantRepository>();
        var platformRoles = Substitute.For<IPlatformUserRoleRepository>();
        platformRoles.Create(Arg.Any<PlatformUserRole>()).Returns(_ => RejectWrite<PlatformUserRole>("platform-role"));
        var actors = Substitute.For<IActorRepository>();
        actors.Create(Arg.Any<Actor>()).Returns(_ => RejectWrite<Actor>("actor"));
        var logins = Substitute.For<IUserExternalLoginRepository>();
        logins.GetByUser(Arg.Any<Guid>()).Returns([]);
        logins.Create(Arg.Any<UserExternalLogin>()).Returns(_ => RejectWrite<UserExternalLogin>("external-login"));
        var emails = Substitute.For<IUserIdentityEmailRepository>();
        var privacy = Substitute.For<IPrivacyErasureStateRepository>();
        var secret = Substitute.For<ISetupSecretProvider>();
        secret.IsSetupModeActive.Returns(true);
        secret.IsSetupSecretRequired.Returns(true);
        secret.IsFromEnvironmentVariable.Returns(true);
        secret.When(value => value.Lock()).Do(_ => effects.Add("secret-lock"));
        DeploymentModeProvider = new ReadOnlyDeploymentModeProvider(effects);
        var audit = Substitute.For<IInstanceBootstrapAuditLogger>();
        audit.When(value => value.Log(Arg.Any<InstanceBootstrapAuditEvent>())).Do(_ => effects.Add("audit"));
        var jwt = Substitute.For<IJwtAuthorityRefreshNotifier>();
        jwt.ReloadAsync(Arg.Any<CancellationToken>()).Returns(_ => { effects.Add("jwt-reload"); return Task.CompletedTask; });
        HostConfiguration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PublicBaseUrl"] = "https://example.test",
            ["Keycloak:Authority"] = "https://identity.example.test",
            ["Keycloak:ClientId"] = "event"
        }).Build();
        GenerationReader = new InstanceOnboardingGenerationReader(SystemSettings, DeploymentModeProvider, BootstrapRepository, HostConfiguration);
        var smtp = Substitute.For<ISmtpConfigResolver>();
        smtp.ResolveAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            if (unitOfWork.InTransaction) ExternalReadinessCallsInsideTransaction++;
            DuringExternalReadiness?.Invoke();
            return Task.FromResult<SmtpConfiguration?>(null);
        });
        var dispatcher = Substitute.For<IAuthenticationProviderDispatcher>();
        dispatcher.GetActivePrimaryProviderAsync(Arg.Any<CancellationToken>()).Returns(providerKind);
        Preflight = new GetOnboardingPreflightQueryHandler(BootstrapRepository, DeploymentModeProvider, secret,
            tenants, SystemSettings, HostConfiguration, dispatcher, smtpConfigResolver: smtp);
        var status = Substitute.For<IQueryHandler<GetInstanceOnboardingStatusQuery, InstanceOnboardingStatusDto>>();
        status.QueryAsync(Arg.Any<GetInstanceOnboardingStatusQuery>(), Arg.Any<CancellationToken>()).Returns(_ => new InstanceOnboardingStatusDto
        {
            State = "InteractivePending",
            Provider = providerKind.ToString(),
            Generation = Bootstrap.Generation,
            SelectedDeploymentMode = Bootstrap.DeploymentMode.ToString()
        });
        var authentication = Substitute.For<IAuthProviderConfigurationService>();
        authentication.ReadConfigurationAsync().Returns(new AuthProviderConfigurationDto
        {
            PrimaryProviderId = (int)providerKind,
            PrimaryProviderCode = providerKind.ToString().ToLowerInvariant(),
            KeycloakAuthority = "https://identity.example.test",
            KeycloakClientId = "event"
        });
        var authorization = Substitute.For<IAuthorizationProviderConfigurationService>();
        authorization.ReadConfigurationAsync().Returns(new AuthorizationProviderConfigurationDto
        {
            Provider = "local",
            AuthorizationProviderConfigured = true
        });
        Journey = new GetInstanceOnboardingJourneyQueryHandler(status, Preflight,
            Substitute.For<IQueryHandler<GetInstanceOperatorIdentityQuery, InstanceOperatorIdentityDocumentDto>>(),
            authentication, authorization, GenerationReader, NullLogger<GetInstanceOnboardingJourneyQueryHandler>.Instance);
        Operation = new InstanceOnboardingCompletionOperation(BootstrapRepository, platformRoles,
            Substitute.For<IRoleRepository>(), UserRepository, actors,
            logins, emails, privacy, new Explore.Application.Services.IdentityAccountResolver(UserRepository, logins,
                emails, privacy, Microsoft.Extensions.Options.Options.Create(new Explore.Application.Configuration.IdentityCorrelationOptions())),
            new Explore.Application.Services.IdentityEmailSynchronizationOperation(emails),
            SystemSettings, [new RejectionSource(this)], secret, audit, DeploymentModeProvider, jwt,
            NullLogger<InstanceOnboardingCompletionOperation>.Instance, unitOfWork, GenerationReader, HostConfiguration,
            new Explore.Application.Services.PrivacyIdentityFenceOperation(new Services.FenceTestAuthority()));
    }

    public Guid UserId { get; }
    public ProviderAccountKey Account { get; }
    public InstanceBootstrapState Bootstrap { get; }
    public IInstanceBootstrapStateRepository BootstrapRepository { get; }
    public IUserRepository UserRepository { get; }
    public ISystemSettingRepository SystemSettings { get; }
    public IConfiguration HostConfiguration { get; }
    public ReadOnlyDeploymentModeProvider DeploymentModeProvider { get; }
    public GetOnboardingPreflightQueryHandler Preflight { get; }
    public GetInstanceOnboardingJourneyQueryHandler Journey { get; }
    public IInstanceOnboardingGenerationReader GenerationReader { get; }
    public InstanceOnboardingCompletionOperation Operation { get; }
    public long BindingGeneration { get; set; } = 7;
    public string BindingFingerprint { get; set; } = Fingerprint;
    public ProviderAccountKey BindingAccount { get; set; }
    public bool ProviderAvailable { get; set; } = true;
    public IReadOnlyList<string> CommittedWrites => attemptedWrites;
    public IReadOnlyList<Guid> Users => attemptedUsers;
    public IReadOnlyList<string> PostCommitEffects => effects;
    public int FullSettingsReads { get; private set; }
    public List<string> SettingKeysRead { get; } = [];
    public int ExternalReadinessCallsInsideTransaction { get; private set; }
    public Action? DuringExternalReadiness { get; set; }

    public void ChangeSetting(string key, string value)
    {
        settings.RemoveAll(setting => setting.SettingKey == key);
        settings.Add(new SystemSetting { SettingKey = key, Value = JsonSerializer.Serialize(value) });
    }

    public Task<BaseCommandResponse<Guid>> ClaimAsync(Guid? userId = null, ProviderAccountKey? account = null) =>
        new ClaimConfiguredInstanceAdministratorCommandHandler(Operation).ExecuteAsync(new()
        {
            AuthenticatedAccount = account ?? Account,
            UserId = userId ?? UserId,
            Email = "adapter@example.test",
            FirstName = "Adapter",
            LastName = "User",
            EmailVerified = true
        }, CancellationToken.None);

    public async Task<CompleteInstanceOnboardingCommand> InteractiveCommandAsync() => new()
    {
        UserId = UserId,
        Email = "interactive@example.test",
        FirstName = "Interactive",
        LastName = "Admin",
        AuthProvider = "keycloak",
        AuthProviderId = "interactive-subject",
        Settings = Settings() with { ExpectedJourneyGeneration = (await Journey.QueryAsync(new(), CancellationToken.None)).Generation }
    };

    public void CompleteBootstrap() => Bootstrap.CompleteConfiguredAdministrator(
        Bootstrap.ProviderKind!.Value, Bootstrap.Generation, Bootstrap.SelectorFingerprint!, UserId, DateTime.UtcNow);

    private T RejectWrite<T>(string category)
    {
        attemptedWrites.Add(category);
        throw new InvalidOperationException("Read-only onboarding scenario attempted a repository mutation.");
    }

    private static CompleteInstanceOnboardingRequest Settings() => new()
    {
        DeploymentMode = DeploymentMode.SingleTenant,
        InstanceName = "Invariant Instance",
        SiteProfile = new SelfHostOnboardingProfileDto { SiteName = "Invariant Instance" }
    };

    private sealed class RejectionSource(OnboardingCompletionScenario scenario) : IConfiguredAdministratorBootstrapProvider
    {
        public Task<ConfiguredAdministratorBootstrapBinding?> GetVerifiedBindingAsync(
            ProviderAccountKey authenticatedAccount, CancellationToken cancellationToken = default) =>
            Task.FromResult(scenario.ProviderAvailable
                ? new ConfiguredAdministratorBootstrapBinding(scenario.BindingAccount, scenario.BindingGeneration,
                    scenario.BindingFingerprint, Settings(), new ConfiguredAdministratorProfile("configured@example.test", "Configured", "Admin"))
                : null);
    }

    private sealed class ReadOnlyUnitOfWork : IUnitOfWork
    {
        public bool InTransaction { get; private set; }
        public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken ct = default) =>
            throw new NotSupportedException("This scenario only observes pre-write admission.");
        public Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default) =>
            throw new NotSupportedException("This scenario only observes pre-write admission.");
        public Task<T> ExecuteReadCommittedAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default) =>
            ExecuteSerializableAsync(operation, ct);
        public async Task<T> ExecuteSerializableAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default)
        {
            InTransaction = true;
            try { return await operation(ct); }
            finally { InTransaction = false; }
        }
    }

    internal sealed class ReadOnlyDeploymentModeProvider(List<string> effects) : IDeploymentModeProvider
    {
        public DeploymentMode Mode { get; set; } = DeploymentMode.SingleTenant;
        public Task<DeploymentMode> GetCurrentModeAsync(CancellationToken ct = default) => Task.FromResult(DeploymentMode.SingleTenant);
        public Task<DeploymentMode> GetConfiguredOnboardingModeAsync(CancellationToken ct = default) => Task.FromResult(Mode);
        public Task<bool> IsSingleTenantAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task InvalidateCacheAsync() { effects.Add("deployment-cache"); return Task.CompletedTask; }
    }
}
