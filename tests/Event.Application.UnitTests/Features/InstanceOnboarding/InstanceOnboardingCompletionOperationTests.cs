using Explore.Application.Authentication;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.DTOs.TenantSettings;
using Explore.Application.Features.InstanceOnboarding.Handlers.Commands;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Features.InstanceOnboarding.Services;
using Explore.Application.Models;
using Explore.Application.Onboarding;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Settings.Documents;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Event.Application.UnitTests.Features.InstanceOnboarding;

public sealed class InstanceOnboardingCompletionOperationTests
{
    [Test]
    public async Task ConfiguredCompletion_CommitsRolesTenantSettingsAndBootstrapAsOneStateChange()
    {
        var scenario = new OnboardingCompletionScenario();

        BaseCommandResponse<Guid> response = await scenario.ClaimAsync();

        await Assert.That(response.IsSuccess).IsTrue();
        await Assert.That(scenario.Bootstrap.Status).IsEqualTo(InstanceBootstrapStatus.Completed);
        await Assert.That(scenario.CommittedWrites).Contains("tenant");
        await Assert.That(scenario.CommittedWrites).Contains("platform-role");
        await Assert.That(scenario.CommittedWrites).Contains("tenant-role");
        await Assert.That(scenario.CommittedWrites).Contains("system-setting");
        await Assert.That(scenario.CommittedWrites).Contains("bootstrap");
        await Assert.That(scenario.Users).Contains(scenario.UserId);
        await Assert.That(scenario.PostCommitEffects)
            .IsEquivalentTo(["secret-lock", "admin-cache", "deployment-cache", "jwt-reload", "audit"]);
    }

    [Test]
    [Arguments(1)]
    [Arguments(6)]
    public async Task ConfiguredCompletion_FailureBeforeOrAfterIntermediateWritesRollsBackEverything(int failingWrite)
    {
        var scenario = new OnboardingCompletionScenario { FailAtWrite = failingWrite };

        _ = await Assert.ThrowsAsync<InjectedOnboardingWriteException>(() => scenario.ClaimAsync());

        await Assert.That(scenario.Bootstrap.Status).IsEqualTo(InstanceBootstrapStatus.Pending);
        await Assert.That(scenario.CommittedWrites).IsEmpty();
        await Assert.That(scenario.Users).IsEmpty();
        await Assert.That(scenario.PostCommitEffects).IsEmpty();
    }

    [Test]
    public async Task PostCommitEffects_AreNotVisibleUntilTheTransactionCommits()
    {
        var scenario = new OnboardingCompletionScenario();
        scenario.BeforeCommit = () =>
        {
            if (scenario.PostCommitEffects.Count != 0)
            {
                throw new InvalidOperationException("A post-commit effect escaped before commit.");
            }
        };

        BaseCommandResponse<Guid> response = await scenario.ClaimAsync();

        await Assert.That(response.IsSuccess).IsTrue();
        await Assert.That(scenario.EventSequence[scenario.EventSequence.IndexOf("commit")..])
            .IsEquivalentTo(["commit", "secret-lock", "admin-cache", "deployment-cache", "jwt-reload", "audit"]);
    }

    [Test]
    public async Task ExactCompletedConfiguredReplay_ReconcilesMandatoryPostCommitEffectsOnce()
    {
        var scenario = new OnboardingCompletionScenario();
        BaseCommandResponse<Guid> first = await scenario.ClaimAsync();
        scenario.EventSequence.Clear();

        BaseCommandResponse<Guid> replay = await scenario.ClaimAsync();

        await Assert.That(first.IsSuccess).IsTrue();
        await Assert.That(replay.IsSuccess).IsTrue();
        await Assert.That(replay.Id).IsEqualTo(first.Id);
        await Assert.That(scenario.CommittedWrites).IsEmpty();
        await Assert.That(scenario.PostCommitEffects)
            .IsEquivalentTo(["secret-lock", "admin-cache", "deployment-cache", "jwt-reload", "audit"]);
    }

    [Test]
    public async Task PostCommitReconciliation_DoesNotUseCanceledRequestToken()
    {
        var scenario = new OnboardingCompletionScenario();
        using var cancellation = new CancellationTokenSource();
        scenario.BeforeCommit = cancellation.Cancel;

        BaseCommandResponse<Guid> response = await scenario.ClaimAsync(cancellationToken: cancellation.Token);

        await Assert.That(response.IsSuccess).IsTrue();
        await Assert.That(scenario.JwtCancellationWasRequested).IsFalse();
    }

    [Test]
    public async Task InteractiveAndConfiguredCommands_ReachTheSameAtomicCompletionOperation()
    {
        var configured = new OnboardingCompletionScenario();
        BaseCommandResponse<Guid> configuredResponse = await new ClaimConfiguredInstanceAdministratorCommandHandler(
            configured.Operation).Handle(configured.Command(), CancellationToken.None);

        var interactive = new OnboardingCompletionScenario(interactive: true);
        var handler = new CompleteInstanceOnboardingCommandHandler(
            interactive.BootstrapRepository,
            interactive.UserRepository,
            interactive.DeploymentModeProvider,
            interactive.Operation);
        BaseCommandResponse<Guid> interactiveResponse = await handler.Handle(
            interactive.InteractiveCommand(),
            CancellationToken.None);

        await Assert.That(configuredResponse.IsSuccess).IsTrue();
        await Assert.That(interactiveResponse.IsSuccess).IsTrue();
        await Assert.That(configured.CommittedWrites).Contains("bootstrap");
        await Assert.That(interactive.CommittedWrites).Contains("bootstrap");
        await Assert.That(configured.PostCommitEffects).IsEquivalentTo(interactive.PostCommitEffects);
    }

    [Test]
    public async Task EmptyConfiguredUserId_ReturnsBoundedFailureWithoutWritesOrEffects()
    {
        var scenario = new OnboardingCompletionScenario();

        BaseCommandResponse<Guid> response = await scenario.ClaimAsync(Guid.Empty);

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
            BindingAccount = new ProviderAccountKey(
                AuthenticationProviderKind.Atproto,
                "did:plc:different-configured-account")
        };

        BaseCommandResponse<Guid> rejected = await scenario.ClaimAsync();

        await Assert.That(rejected.IsSuccess).IsFalse();
        await Assert.That(rejected.FailureCode)
            .IsEqualTo("configured_administrator_claim_mismatch");
        await Assert.That(scenario.Bootstrap.Status)
            .IsEqualTo(InstanceBootstrapStatus.Pending);
        await Assert.That(scenario.Bootstrap.CompletedByUserId).IsNull();
        await Assert.That(scenario.Users).IsEmpty();
        await Assert.That(scenario.CommittedWrites).IsEmpty();
        await Assert.That(scenario.PostCommitEffects).IsEmpty();
    }

    [Test]
    public async Task CompletedConfiguredClaim_DifferentActorCannotAcquireAuthority()
    {
        var scenario = new OnboardingCompletionScenario();
        BaseCommandResponse<Guid> initial = await scenario.ClaimAsync();
        int effectCount = scenario.PostCommitEffects.Count;
        Guid attackerId = Guid.CreateVersion7();

        BaseCommandResponse<Guid> rejected = await scenario.ClaimAsync(attackerId);

        await Assert.That(initial.IsSuccess).IsTrue();
        await Assert.That(rejected.IsSuccess).IsFalse();
        await Assert.That(rejected.FailureCode)
            .IsEqualTo("configured_administrator_claim_conflict");
        await Assert.That(scenario.Bootstrap.CompletedByUserId)
            .IsEqualTo(scenario.UserId);
        await Assert.That(scenario.Users).DoesNotContain(attackerId);
        await Assert.That(scenario.CommittedWrites).IsEmpty();
        await Assert.That(scenario.PostCommitEffects.Count)
            .IsEqualTo(effectCount);
    }
}

internal sealed class OnboardingCompletionScenario
{
    internal const string Fingerprint = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    internal const string OtherFingerprint = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    private readonly List<User> _users = [];
    private readonly List<Actor> _actors = [];
    private readonly List<UserExternalLogin> _logins = [];
    private readonly List<PlatformUserRole> _platformRoles = [];
    private readonly List<TenantUser> _tenantUsers = [];
    private readonly List<TenantUserRoleGrant> _tenantRoles = [];
    private readonly List<SystemSetting> _settings = [];
    private readonly StatefulUnitOfWork _unitOfWork;
    private readonly ConfiguredProviderFake _provider;

    public OnboardingCompletionScenario(
        bool interactive = false,
        AuthenticationProviderKind providerKind = AuthenticationProviderKind.Keycloak)
    {
        UserId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000222");
        Account = new ProviderAccountKey(
            providerKind,
            providerKind == AuthenticationProviderKind.Local
                ? UserId.ToString("D")
                : "subject-123");
        Bootstrap = interactive
            ? InstanceBootstrapState.CreateInteractivePending(
                Guid.Parse("018e4e5c-7f00-7000-8000-000000000111"),
                DeploymentMode.SingleTenant,
                DateTime.UtcNow.AddMinutes(-1))
            : CreatePending(Account.ProviderKind, 7, Fingerprint);

        BootstrapRepository = Substitute.For<IInstanceBootstrapStateRepository>();
        UserRepository = Substitute.For<IUserRepository>();
        var platformRoles = Substitute.For<IPlatformUserRoleRepository>();
        var tenantRoles = Substitute.For<ITenantUserRoleGrantRepository>();
        var tenantUsers = Substitute.For<ITenantUserRepository>();
        var roles = Substitute.For<IRoleRepository>();
        var actors = Substitute.For<IActorRepository>();
        var externalLogins = Substitute.For<IUserExternalLoginRepository>();
        var tenants = Substitute.For<ITenantRepository>();
        var tenantCreation = Substitute.For<ITenantCreationService>();
        var tenantSettings = Substitute.For<ITenantSettingsDocumentRepository>();
        var systemSettings = Substitute.For<ISystemSettingRepository>();
        var branding = Substitute.For<ITenantBrandingSettingsDocumentProvisioningService>();

        _unitOfWork = new StatefulUnitOfWork(this);
        _provider = new ConfiguredProviderFake(this);

        BootstrapRepository.GetCurrent(Arg.Any<CancellationToken>()).Returns(_ => Bootstrap);
        BootstrapRepository.GetCurrentForUpdate(Arg.Any<CancellationToken>()).Returns(_ => Bootstrap);
        BootstrapRepository.Update(Arg.Any<InstanceBootstrapState>()).Returns(call =>
        {
            RecordWrite("bootstrap");
            Bootstrap = call.Arg<InstanceBootstrapState>();
            return Task.CompletedTask;
        });
        BootstrapRepository.Create(Arg.Any<InstanceBootstrapState>()).Returns(call =>
        {
            RecordWrite("bootstrap");
            Bootstrap = call.Arg<InstanceBootstrapState>();
            return Bootstrap;
        });

        UserRepository.GetById(Arg.Any<Guid>()).Returns(call =>
            _users.SingleOrDefault(user => user.Id == call.Arg<Guid>()));
        UserRepository.Create(Arg.Any<User>()).Returns(call =>
        {
            RecordWrite("user");
            User user = call.Arg<User>();
            _users.Add(user);
            return user;
        });
        actors.Create(Arg.Any<Actor>()).Returns(call =>
        {
            RecordWrite("actor");
            Actor actor = call.Arg<Actor>();
            actor.Id = actor.Id == Guid.Empty ? Guid.CreateVersion7() : actor.Id;
            _actors.Add(actor);
            User? user = _users.SingleOrDefault(candidate => candidate.Id == actor.UserId);
            if (user is not null) user.Actor = actor;
            return actor;
        });
        actors.GetActorByUserId(Arg.Any<Guid>()).Returns(call =>
            _actors.SingleOrDefault(actor => actor.UserId == call.Arg<Guid>()));
        externalLogins.GetByUser(Arg.Any<Guid>()).Returns(call =>
            _logins.Where(login => login.UserId == call.Arg<Guid>()).ToList());
        externalLogins.GetByProviderAndKey(Arg.Any<ProviderAccountKey>()).Returns(call =>
        {
            ProviderAccountKey account = call.Arg<ProviderAccountKey>()
                ?? throw new ArgumentNullException(nameof(account));
            return _logins.SingleOrDefault(login =>
                login.AuthenticationProviderId == (int)account.ProviderKind
                && string.Equals(login.ProviderKey, account.Value, StringComparison.Ordinal));
        });
        externalLogins.Create(Arg.Any<UserExternalLogin>()).Returns(call =>
        {
            RecordWrite("external-login");
            UserExternalLogin login = call.Arg<UserExternalLogin>();
            _logins.Add(login);
            return login;
        });

        Role platformRole = new() { Id = 1, MasterCode = "platform.admin", FullName = "Platform admin", Scope = RoleScopeEnum.Platform };
        Role tenantRole = new() { Id = 2, MasterCode = "tenant.admin", FullName = "Tenant admin", Scope = RoleScopeEnum.Tenant };
        roles.GetByMasterCodeAsync("platform.admin").Returns(platformRole);
        roles.GetByMasterCodeAsync("tenant.admin").Returns(tenantRole);
        platformRoles.GetByUserAndRole(Arg.Any<Guid>(), Arg.Any<int>()).Returns(call =>
            _platformRoles.SingleOrDefault(role => role.UserId == call.ArgAt<Guid>(0) && role.RoleId == call.ArgAt<int>(1)));
        platformRoles.Create(Arg.Any<PlatformUserRole>()).Returns(call =>
        {
            RecordWrite("platform-role");
            PlatformUserRole role = call.Arg<PlatformUserRole>();
            _platformRoles.Add(role);
            return role;
        });

        tenants.GetById(PlatformDefaults.DefaultTenantId).Returns(_ => (Tenant?)null);
        tenantCreation.CreateInCurrentTransactionAsync(Arg.Any<TenantCreationRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                RecordWrite("tenant");
                TenantCreationRequest request = call.Arg<TenantCreationRequest>();
                var tenant = new Tenant
                {
                    Id = request.TenantId,
                    FullName = request.FullName,
                    Slug = request.Slug,
                    TenantStatusId = request.TenantStatusId,
                    TenantStatus = null!
                };
                return new TenantCreationOutcome(tenant, null!, null!);
            });
        branding.EnsureTenantBrandingDocumentAsync(Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                RecordWrite("tenant-branding");
                return TenantBrandingSettingsDocumentDefaults.Create(call.ArgAt<Guid>(0), call.ArgAt<string?>(1));
            });
        tenantUsers.GetByTenantAndUserAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call => _tenantUsers.SingleOrDefault(item =>
                item.TenantId == call.ArgAt<Guid>(0) && item.UserId == call.ArgAt<Guid>(1)));
        tenantUsers.Create(Arg.Any<TenantUser>()).Returns(call =>
        {
            RecordWrite("tenant-user");
            TenantUser tenantUser = call.Arg<TenantUser>();
            tenantUser.Id = tenantUser.Id == Guid.Empty ? Guid.CreateVersion7() : tenantUser.Id;
            _tenantUsers.Add(tenantUser);
            return tenantUser;
        });
        tenantRoles.GetByTenantAndUser(Arg.Any<Guid>(), Arg.Any<Guid>()).Returns(call =>
            _tenantRoles.SingleOrDefault(item =>
                item.TenantId == call.ArgAt<Guid>(0) && item.TenantUser?.UserId == call.ArgAt<Guid>(1)));
        tenantRoles.Create(Arg.Any<TenantUserRoleGrant>()).Returns(call =>
        {
            RecordWrite("tenant-role");
            TenantUserRoleGrant role = call.Arg<TenantUserRoleGrant>();
            _tenantRoles.Add(role);
            return role;
        });
        systemSettings.UpsertAsync(Arg.Any<SystemSetting>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            RecordWrite("system-setting");
            SystemSetting setting = call.Arg<SystemSetting>();
            _settings.RemoveAll(existing => existing.SettingKey == setting.SettingKey);
            _settings.Add(setting);
            return setting.Value;
        });

        var setupSecret = new EffectSetupSecret(EventSequence);
        var cache = new EffectAdminCache(EventSequence);
        DeploymentModeProvider = new EffectDeploymentModeProvider(EventSequence);
        var jwt = new EffectJwtNotifier(this, EventSequence);
        var audit = new EffectAuditLogger(EventSequence);

        Operation = new InstanceOnboardingCompletionOperation(
            BootstrapRepository,
            platformRoles,
            tenantRoles,
            tenantUsers,
            roles,
            UserRepository,
            actors,
            externalLogins,
            tenants,
            tenantCreation,
            tenantSettings,
            systemSettings,
            [_provider],
            setupSecret,
            audit,
            cache,
            DeploymentModeProvider,
            jwt,
            branding,
            NullLogger<InstanceOnboardingCompletionOperation>.Instance,
            _unitOfWork);
    }

    public Guid UserId { get; }
    public ProviderAccountKey Account { get; }
    public InstanceBootstrapState Bootstrap { get; set; }
    public IInstanceBootstrapStateRepository BootstrapRepository { get; }
    public IUserRepository UserRepository { get; }
    public EffectDeploymentModeProvider DeploymentModeProvider { get; }
    public InstanceOnboardingCompletionOperation Operation { get; }
    public List<string> EventSequence { get; } = [];
    public IReadOnlyList<string> PostCommitEffects => EventSequence.Where(item => item != "commit").ToArray();
    public IReadOnlyList<string> CommittedWrites => _unitOfWork.CommittedWrites;
    public IReadOnlyList<Guid> Users => _users.Select(user => user.Id).ToArray();
    public int? FailAtWrite { get => _unitOfWork.FailAtWrite; set => _unitOfWork.FailAtWrite = value; }
    public Action? BeforeCommit { get => _unitOfWork.BeforeCommit; set => _unitOfWork.BeforeCommit = value; }
    public long BindingGeneration { get => _provider.Generation; set => _provider.Generation = value; }
    public string BindingFingerprint { get => _provider.Fingerprint; set => _provider.Fingerprint = value; }
    public ProviderAccountKey BindingAccount { get => _provider.BindingAccount; set => _provider.BindingAccount = value; }
    public bool ProviderAvailable { get => _provider.Available; set => _provider.Available = value; }
    public bool JwtCancellationWasRequested { get; private set; }

    public ClaimConfiguredInstanceAdministratorCommand Command(Guid? userId = null, ProviderAccountKey? account = null) => new()
    {
        AuthenticatedAccount = account ?? Account,
        UserId = userId ?? UserId,
        Email = "adapter@example.test",
        FirstName = "Adapter",
        LastName = "User",
        EmailVerified = true
    };

    public Task<BaseCommandResponse<Guid>> ClaimAsync(
        Guid? userId = null,
        ProviderAccountKey? account = null,
        CancellationToken cancellationToken = default) =>
        new ClaimConfiguredInstanceAdministratorCommandHandler(Operation)
            .Handle(Command(userId, account), cancellationToken);

    public Task<BaseCommandResponse<Guid>> CompleteProvisionedLocalAsync()
    {
        var binding = new ConfiguredAdministratorBootstrapBinding(
            BindingAccount, BindingGeneration, BindingFingerprint, Settings(),
            new ConfiguredAdministratorProfile("configured@example.test", "Configured", "Admin"));
        var receipt = new LocalCredentialOperationReceipt(
            Bootstrap.Id, LocalCredentialOperationKind.Create, LocalCredentialOperationStage.ProvisioningPending,
            UserId, UserId, Guid.CreateVersion7(), Guid.CreateVersion7(), DateTime.UtcNow);
        var snapshot = new LocalCredentialProvisioningSnapshot(
            receipt, Guid.CreateVersion7(), binding.AdministratorProfile.Email,
            "Configured", "Admin", emailVerified: true, username: Account.Value);
        var credentials = Substitute.For<ILocalCredentialAdministration>();
        credentials.ReadProvisioningAsync(Bootstrap.Id, Arg.Any<CancellationToken>())
            .Returns(_ => snapshot);
        credentials.ActivateChangeRequiredAsync(Arg.Any<LocalCredentialActivationRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                LocalCredentialActivationRequest request = call.Arg<LocalCredentialActivationRequest>()
                    ?? throw new ArgumentNullException(nameof(request));
                if (_unitOfWork.InTransaction || Bootstrap.Status != InstanceBootstrapStatus.Completed
                    || request.OperationId != receipt.OperationId
                    || request.ExpectedOperationConcurrencyStamp != snapshot.OperationConcurrencyStamp
                    || !_users.Any(user => user.Id == receipt.LocalSubjectId && !user.IsDeleted)
                    || !_actors.Any(actor => actor.Id == receipt.PersonalActorId
                        && actor.UserId == receipt.LocalSubjectId && actor.ActorTypeId == (int)ActorTypeEnum.User
                        && !actor.IsDeleted && !actor.IsSuspended)
                    || !_logins.Any(login => login.Id == receipt.ExternalLoginId
                        && login.UserId == receipt.LocalSubjectId
                        && login.AuthenticationProviderId == (int)AuthenticationProviderKind.Local
                        && login.ProviderKey == Account.Value))
                    return LocalCredentialActivationOutcome.BindingIncomplete;

                snapshot = new LocalCredentialProvisioningSnapshot(
                    new LocalCredentialOperationReceipt(receipt.OperationId, receipt.Kind,
                        LocalCredentialOperationStage.ChangeRequired, receipt.InitiatingApplicationUserId,
                        receipt.LocalSubjectId, receipt.PersonalActorId, receipt.ExternalLoginId, receipt.CreatedAt),
                    Guid.CreateVersion7(), snapshot.Email, snapshot.FirstName, snapshot.LastName,
                    snapshot.EmailVerified, snapshot.Username);
                EventSequence.Add("credential-activation");
                return LocalCredentialActivationOutcome.Activated;
            });
        var bootstrapProvider = Substitute.For<IConfiguredAdministratorBootstrapProvider>();
        bootstrapProvider.GetVerifiedBindingAsync(Account, Arg.Any<CancellationToken>()).Returns(binding);
        var dispatcher = Substitute.For<IAuthenticationProviderDispatcher>();
        dispatcher.GetActivePrimaryProviderAsync(Arg.Any<CancellationToken>())
            .Returns(AuthenticationProviderKind.Local);
        var bootstrap = new LocalAdministratorBootstrapOperation(
            BootstrapRepository, bootstrapProvider, credentials, Substitute.For<ISecretResolver>(), Operation,
            new EffectSetupSecret(EventSequence), DeploymentModeProvider, _unitOfWork, TimeProvider.System, dispatcher);
        return bootstrap.CompleteConfiguredAsync(Account);
    }

    public CompleteInstanceOnboardingCommand InteractiveCommand() => new()
    {
        UserId = UserId,
        Email = "interactive@example.test",
        FirstName = "Interactive",
        LastName = "Admin",
        AuthProvider = "keycloak",
        AuthProviderId = "interactive-subject",
        Settings = Settings()
    };

    public void CompleteBootstrap(Guid? userId = null) =>
        Bootstrap.CompleteConfiguredAdministrator(
            Bootstrap.ProviderKind!.Value,
            Bootstrap.Generation,
            Bootstrap.SelectorFingerprint!,
            userId ?? UserId,
            DateTime.UtcNow);

    public static InstanceBootstrapState CreatePending(
        AuthenticationProviderKind provider,
        long generation,
        string fingerprint) => InstanceBootstrapState.CreateConfiguredAdministratorPending(
            Guid.CreateVersion7(), provider, DeploymentMode.SingleTenant, generation,
            OtherFingerprint, fingerprint, DateTime.UtcNow.AddMinutes(-1));

    private static CompleteInstanceOnboardingRequest Settings() => new()
    {
        DeploymentMode = DeploymentMode.SingleTenant,
        InstanceName = "Invariant Instance",
        SiteProfile = new SelfHostOnboardingProfileDto { SiteName = "Invariant Instance" },
        DirectoryOperatorIdentity = new TenantDirectoryOperatorIdentityInputDto
        {
            PublicName = "Invariant Operator",
            LegalName = "Invariant Operator Ltd",
            OperatorKindCode = "registered_organization",
            JurisdictionCountryCode = "GB",
            RegistrationIdentifier = "REG-123",
            PublicContactEmail = "operator@example.test",
            LegalNoticeUrl = "https://example.test/legal",
            TermsUrl = "https://example.test/terms",
            PrivacyUrl = "https://example.test/privacy"
        }
    };

    private void RecordWrite(string category) => _unitOfWork.RecordWrite(category);

    private sealed class ConfiguredProviderFake(OnboardingCompletionScenario owner)
        : IConfiguredAdministratorBootstrapProvider
    {
        public long Generation { get; set; } = 7;
        public string Fingerprint { get; set; } = OnboardingCompletionScenario.Fingerprint;
        public ProviderAccountKey BindingAccount { get; set; } = owner.Account;
        public bool Available { get; set; } = true;

        public Task<ConfiguredAdministratorBootstrapBinding?> GetVerifiedBindingAsync(
            ProviderAccountKey authenticatedAccount,
            CancellationToken cancellationToken = default)
        {
            if (!owner._unitOfWork.InTransaction)
            {
                throw new InvalidOperationException("Provider authority was read outside the serializable transaction.");
            }
            if (!Available)
            {
                return Task.FromResult<ConfiguredAdministratorBootstrapBinding?>(null);
            }
            return Task.FromResult<ConfiguredAdministratorBootstrapBinding?>(new(
                BindingAccount,
                Generation,
                Fingerprint,
                Settings(),
                new ConfiguredAdministratorProfile("configured@example.test", "Configured", "Admin")));
        }
    }

    private sealed class StatefulUnitOfWork(OnboardingCompletionScenario owner) : IUnitOfWork
    {
        private readonly List<string> _workingWrites = [];
        public bool InTransaction { get; private set; }
        public int? FailAtWrite { get; set; }
        public Action? BeforeCommit { get; set; }
        public IReadOnlyList<string> CommittedWrites { get; private set; } = [];

        public void RecordWrite(string category)
        {
            _workingWrites.Add(category);
            if (FailAtWrite == _workingWrites.Count)
            {
                throw new InjectedOnboardingWriteException();
            }
        }

        public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public async Task<T> ExecuteSerializableAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default)
        {
            InstanceBootstrapState bootstrap = Clone(owner.Bootstrap);
            int users = owner._users.Count;
            int actors = owner._actors.Count;
            int logins = owner._logins.Count;
            int platformRoles = owner._platformRoles.Count;
            int tenantUsers = owner._tenantUsers.Count;
            int tenantRoles = owner._tenantRoles.Count;
            int settings = owner._settings.Count;
            _workingWrites.Clear();
            InTransaction = true;
            try
            {
                T result = await operation(ct);
                BeforeCommit?.Invoke();
                CommittedWrites = _workingWrites.ToArray();
                owner.EventSequence.Add("commit");
                return result;
            }
            catch
            {
                owner.Bootstrap = bootstrap;
                Trim(owner._users, users);
                Trim(owner._actors, actors);
                Trim(owner._logins, logins);
                Trim(owner._platformRoles, platformRoles);
                Trim(owner._tenantUsers, tenantUsers);
                Trim(owner._tenantRoles, tenantRoles);
                Trim(owner._settings, settings);
                CommittedWrites = [];
                throw;
            }
            finally
            {
                InTransaction = false;
            }
        }

        private static void Trim<T>(List<T> values, int count)
        {
            if (values.Count > count) values.RemoveRange(count, values.Count - count);
        }

        private static InstanceBootstrapState Clone(InstanceBootstrapState source)
        {
            InstanceBootstrapState clone = source.Mode == InstanceBootstrapMode.Interactive
                ? InstanceBootstrapState.CreateInteractivePending(source.Id, source.DeploymentMode, source.CreatedAt)
                : InstanceBootstrapState.CreateConfiguredAdministratorPending(
                    source.Id,
                    source.ProviderKind!.Value,
                    source.DeploymentMode,
                    source.Generation,
                    source.ConfigurationFingerprint!,
                    source.SelectorFingerprint!,
                    source.CreatedAt);
            if (source.Status == InstanceBootstrapStatus.Completed)
            {
                if (source.Mode == InstanceBootstrapMode.Interactive)
                    clone.CompleteInteractive(source.CompletedByUserId!.Value, source.CompletedAt!.Value);
                else
                    clone.CompleteConfiguredAdministrator(
                        source.ProviderKind!.Value,
                        source.Generation,
                        source.CompletedIdentityFingerprint!,
                        source.CompletedByUserId!.Value,
                        source.CompletedAt!.Value);
            }
            return clone;
        }
    }

    private sealed class EffectSetupSecret(List<string> events) : ISetupSecretProvider
    {
        public bool IsSetupModeActive => true;
        public bool IsSetupSecretRequired => true;
        public bool IsFromEnvironmentVariable => true;
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public bool ValidateSecret(string? secret) => true;
        public void Lock() => events.Add("secret-lock");
    }

    private sealed class EffectAdminCache(List<string> events) : IAdminCacheInvalidator
    {
        public void InvalidateUser(Guid userId) => events.Add("admin-cache");
        public void InvalidateAll() => throw new NotSupportedException();
    }

    internal sealed class EffectDeploymentModeProvider(List<string> events) : IDeploymentModeProvider
    {
        public Task<DeploymentMode> GetCurrentModeAsync(CancellationToken ct = default) => Task.FromResult(DeploymentMode.SingleTenant);
        public Task<DeploymentMode> GetConfiguredOnboardingModeAsync(CancellationToken ct = default) => Task.FromResult(DeploymentMode.SingleTenant);
        public Task<bool> IsSingleTenantAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task InvalidateCacheAsync() { events.Add("deployment-cache"); return Task.CompletedTask; }
    }

    private sealed class EffectJwtNotifier(OnboardingCompletionScenario owner, List<string> events) : IJwtAuthorityRefreshNotifier
    {
        public Task ReloadAsync(CancellationToken ct = default)
        {
            owner.JwtCancellationWasRequested = ct.IsCancellationRequested;
            events.Add("jwt-reload");
            return Task.CompletedTask;
        }
    }

    private sealed class EffectAuditLogger(List<string> events) : IInstanceBootstrapAuditLogger
    {
        public void Log(InstanceBootstrapAuditEvent auditEvent) => events.Add("audit");
    }
}

internal sealed class InjectedOnboardingWriteException : Exception;
