using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Models;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using Explore.Infrastructure;
using Explore.Infrastructure.Services;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Explore.Infrastructure.Tests.Infrastructure;

public sealed class ConfiguredAdministratorBootstrapProviderTests
{
    private const string Authority = "https://identity.example.test/realms/operators";
    private const string Subject = "operator-subject-01";
    private const string Did = "did:plc:ExactConfiguredAdministrator";
    private const string Email = "configured.admin@example.test";
    private static readonly DateTime PreparedAt =
        new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task SevenKeyMatrix_RequiresExactCompleteConfiguredOrEmptyInteractiveCatalogue()
    {
        await using var database = await BootstrapDatabase.CreateAsync();

        ConfiguredAdministratorBootstrapProvider.ConfigurationSnapshot interactive =
            CreateProvider(database.Repository, InteractiveConfiguration()).ReadConfiguration();
        await Assert.That(interactive.Mode).IsEqualTo(InstanceBootstrapMode.Interactive);
        await Assert.That(interactive.ProviderKind).IsNull();

        ConfiguredAdministratorBootstrapProvider.ConfigurationSnapshot configured =
            CreateProvider(database.Repository, ConfiguredConfiguration()).ReadConfiguration();
        await Assert.That(configured.Mode).IsEqualTo(InstanceBootstrapMode.ConfiguredAdministrator);
        await Assert.That(configured.AdministratorProfile!.FirstName).IsEqualTo("Configured");
        await Assert.That(configured.AdministratorProfile.LastName).IsEqualTo("Administrator");

        foreach (string key in new[]
                 {
                     "INSTANCE_BOOTSTRAP_MODE",
                     "INSTANCE_BOOTSTRAP_ADMIN_PROVIDER",
                     "INSTANCE_BOOTSTRAP_ADMIN_SUBJECT",
                     "INSTANCE_BOOTSTRAP_BINDING_GENERATION"
                 })
        {
            Dictionary<string, string?> incomplete = ConfiguredValues();
            incomplete.Remove(key);
            await AssertReason(database.Repository, incomplete,
                key == "INSTANCE_BOOTSTRAP_MODE"
                    ? "instance_bootstrap_mode_missing"
                    : "instance_bootstrap_configured_matrix_incomplete");
        }

        foreach (string key in new[]
                 {
                     "INSTANCE_BOOTSTRAP_ADMIN_PROVIDER",
                     "INSTANCE_BOOTSTRAP_ADMIN_SUBJECT",
                     "INSTANCE_BOOTSTRAP_BINDING_GENERATION",
                     "INSTANCE_BOOTSTRAP_ADMIN_EMAIL",
                     "INSTANCE_BOOTSTRAP_ADMIN_FIRST_NAME",
                     "INSTANCE_BOOTSTRAP_ADMIN_LAST_NAME"
                 })
        {
            Dictionary<string, string?> pollutedInteractive = InteractiveValues();
            pollutedInteractive[key] = ConfiguredValues()[key];
            await AssertReason(
                database.Repository,
                pollutedInteractive,
                "instance_bootstrap_interactive_matrix_invalid");
        }

        Dictionary<string, string?> firstNameOnly = ConfiguredValues();
        firstNameOnly.Remove("INSTANCE_BOOTSTRAP_ADMIN_LAST_NAME");
        await AssertReason(database.Repository, firstNameOnly, "instance_bootstrap_profile_matrix_invalid");

        Dictionary<string, string?> lastNameOnly = ConfiguredValues();
        lastNameOnly.Remove("INSTANCE_BOOTSTRAP_ADMIN_FIRST_NAME");
        await AssertReason(database.Repository, lastNameOnly, "instance_bootstrap_profile_matrix_invalid");
    }

    [Test]
    [Arguments("keycloak", Subject)]
    [Arguments("atproto", Did)]
    public async Task ExternalBindingWithoutEmailStillRequiresTheExactProviderIdentity(string providerCode, string subject)
    {
        await using var database = await BootstrapDatabase.CreateAsync();
        var values = ConfiguredValues();
        values["INSTANCE_BOOTSTRAP_ADMIN_PROVIDER"] = providerCode;
        values["INSTANCE_BOOTSTRAP_ADMIN_SUBJECT"] = subject;
        values.Remove("INSTANCE_BOOTSTRAP_ADMIN_EMAIL");
        var provider = CreateProvider(database.Repository, BuildConfiguration(values));
        await new ConfiguredAdministratorBootstrapStartupRunner(provider, database.Repository, database.UnitOfWork,
            new FixedTimeProvider(PreparedAt)).PrepareAsync();
        ProviderAccountKey account = providerCode == "keycloak"
            ? PlatformIdentityPrincipalExtensions.CreateOidcAccountKey(Authority, subject)
            : PlatformIdentityPrincipalExtensions.CreateAtprotoAccountKey(AtprotoDid.Parse(subject));
        var binding = await provider.GetVerifiedBindingAsync(account);
        await Assert.That(binding).IsNotNull();
        await Assert.That(binding!.AdministratorProfile.Email).IsNull();
        await Assert.That(binding.AccountKey).IsEqualTo(account);
        await Assert.That(await provider.GetVerifiedBindingAsync(new ProviderAccountKey(
            AuthenticationProviderKind.Local, Guid.CreateVersion7().ToString("D")))).IsNull();
    }

    [Test]
    public async Task ModeAndProviderCatalogues_AreOrdinalAndClosed()
    {
        await using var database = await BootstrapDatabase.CreateAsync();
        foreach ((string mode, string reason) in new[]
                 {
                     ("interactive", "instance_bootstrap_mode_invalid"),
                     ("Configuredadministrator", "instance_bootstrap_mode_invalid"),
                     ("ConfiguredAdministrator ", "instance_bootstrap_mode_invalid")
                 })
        {
            Dictionary<string, string?> values = ConfiguredValues();
            values["INSTANCE_BOOTSTRAP_MODE"] = mode;
            await AssertReason(database.Repository, values, reason);
        }

        foreach (string provider in new[] { "Keycloak", "ATPROTO", "oidc", "keycloak " })
        {
            Dictionary<string, string?> values = ConfiguredValues();
            values["INSTANCE_BOOTSTRAP_ADMIN_PROVIDER"] = provider;
            await AssertReason(database.Repository, values, "instance_bootstrap_provider_invalid");
        }
    }

    [Test]
    public async Task Generation_MustBePositiveInvariantDecimalWithoutSignsOrWhitespace()
    {
        await using var database = await BootstrapDatabase.CreateAsync();
        foreach (string generation in new[] { "0", "-1", "+1", " 1", "1 ", "1.0", "9223372036854775808" })
        {
            Dictionary<string, string?> values = ConfiguredValues();
            values["INSTANCE_BOOTSTRAP_BINDING_GENERATION"] = generation;
            await AssertReason(database.Repository, values, "instance_bootstrap_generation_invalid");
        }

        Dictionary<string, string?> maximum = ConfiguredValues();
        maximum["INSTANCE_BOOTSTRAP_BINDING_GENERATION"] = long.MaxValue.ToString();
        ConfiguredAdministratorBootstrapProvider.ConfigurationSnapshot snapshot =
            CreateProvider(database.Repository, BuildConfiguration(maximum)).ReadConfiguration();
        await Assert.That(snapshot.Generation).IsEqualTo(long.MaxValue);
    }

    [Test]
    public async Task SubjectEmailAndNameBounds_AcceptBoundariesAndRejectOutsideValues()
    {
        await using var database = await BootstrapDatabase.CreateAsync();
        Dictionary<string, string?> boundary = ConfiguredValues();
        boundary["INSTANCE_BOOTSTRAP_ADMIN_SUBJECT"] = new string('s', 2048);
        boundary["INSTANCE_BOOTSTRAP_ADMIN_EMAIL"] = string.Concat(new string('e', 307), "@example.test");
        boundary["INSTANCE_BOOTSTRAP_ADMIN_FIRST_NAME"] = new string('f', 128);
        boundary["INSTANCE_BOOTSTRAP_ADMIN_LAST_NAME"] = new string('l', 128);
        ConfiguredAdministratorBootstrapProvider.ConfigurationSnapshot accepted =
            CreateProvider(database.Repository, BuildConfiguration(boundary)).ReadConfiguration();
        await Assert.That(accepted.AdministratorProfile!.Email).Length().IsEqualTo(320);
        await Assert.That(accepted.AdministratorProfile.FirstName).Length().IsEqualTo(128);
        await Assert.That(accepted.AccountKey!.Value).Contains(new string('s', 2048));

        foreach ((string key, string value, string reason) in new[]
                 {
                     ("INSTANCE_BOOTSTRAP_ADMIN_SUBJECT", new string('s', 2049), "instance_bootstrap_subject_invalid"),
                     ("INSTANCE_BOOTSTRAP_ADMIN_SUBJECT", " subject", "instance_bootstrap_subject_invalid"),
                     ("INSTANCE_BOOTSTRAP_ADMIN_SUBJECT", "subject\nvalue", "instance_bootstrap_subject_invalid"),
                     ("INSTANCE_BOOTSTRAP_ADMIN_EMAIL", string.Concat(new string('e', 308), "@example.test"), "instance_bootstrap_email_invalid"),
                     ("INSTANCE_BOOTSTRAP_ADMIN_EMAIL", "admin @example.test", "instance_bootstrap_email_invalid"),
                     ("INSTANCE_BOOTSTRAP_ADMIN_EMAIL", "admin@@example.test", "instance_bootstrap_email_invalid"),
                     ("INSTANCE_BOOTSTRAP_ADMIN_FIRST_NAME", new string('f', 129), "instance_bootstrap_profile_name_invalid"),
                     ("INSTANCE_BOOTSTRAP_ADMIN_FIRST_NAME", "   ", "instance_bootstrap_profile_name_invalid"),
                     ("INSTANCE_BOOTSTRAP_ADMIN_LAST_NAME", "last\u0000name", "instance_bootstrap_profile_name_invalid")
                 })
        {
            Dictionary<string, string?> values = ConfiguredValues();
            values[key] = value;
            await AssertReason(database.Repository, values, reason);
        }
    }

    [Test]
    public async Task ProviderSelectors_UseNormalizedKeycloakAuthorityAndExactCanonicalDid()
    {
        await using var database = await BootstrapDatabase.CreateAsync();
        Dictionary<string, string?> keycloakValues = ConfiguredValues();
        keycloakValues["Keycloak:Authority"] = "HTTPS://IDENTITY.EXAMPLE.TEST:443/realms/operators/";
        ConfiguredAdministratorBootstrapProvider.ConfigurationSnapshot keycloak =
            CreateProvider(database.Repository, BuildConfiguration(keycloakValues)).ReadConfiguration();
        await Assert.That(keycloak.AccountKey!.Value)
            .IsEqualTo($"oidc:{Authority.Length}:{Authority}:{Subject}");

        Dictionary<string, string?> atprotoValues = ConfiguredValues();
        atprotoValues["INSTANCE_BOOTSTRAP_ADMIN_PROVIDER"] = "atproto";
        atprotoValues["INSTANCE_BOOTSTRAP_ADMIN_SUBJECT"] = Did;
        ConfiguredAdministratorBootstrapProvider.ConfigurationSnapshot atproto =
            CreateProvider(database.Repository, BuildConfiguration(atprotoValues)).ReadConfiguration();
        await Assert.That(atproto.AccountKey)
            .IsEqualTo(PlatformIdentityPrincipalExtensions.CreateAtprotoAccountKey(AtprotoDid.Parse(Did)));
        await Assert.That(atproto.AccountKey!.Value).IsEqualTo(Did);

        foreach (string invalid in new[] { "alice.example.test", "at://alice.example.test", "DID:plc:alice", "did:plc:alice#key" })
        {
            atprotoValues["INSTANCE_BOOTSTRAP_ADMIN_SUBJECT"] = invalid;
            await AssertReason(database.Repository, atprotoValues, "instance_bootstrap_atproto_did_invalid");
        }
    }

    [Test]
    public async Task Catalogue_HasNoAliasOrFallbackConfigurationSource()
    {
        await using var database = await BootstrapDatabase.CreateAsync();
        var aliases = new Dictionary<string, string?>
        {
            ["InstanceBootstrap:Mode"] = "ConfiguredAdministrator",
            ["InstanceBootstrap:Administrator:Provider"] = "keycloak",
            ["InstanceBootstrap:Administrator:Subject"] = Subject,
            ["InstanceBootstrap:BindingGeneration"] = "1",
            ["InstanceBootstrap:Administrator:Email"] = Email,
            ["Keycloak:Authority"] = Authority,
            ["Deployment:Mode"] = "SingleTenant"
        };

        await AssertReason(database.Repository, aliases, "instance_bootstrap_mode_missing");
    }

    [Test]
    public async Task Fingerprints_AreDeterministicLengthPrefixedLowercaseSha256()
    {
        await using var database = await BootstrapDatabase.CreateAsync();
        ConfiguredAdministratorBootstrapProvider.ConfigurationSnapshot first =
            CreateProvider(database.Repository, ConfiguredConfiguration()).ReadConfiguration();
        ConfiguredAdministratorBootstrapProvider.ConfigurationSnapshot second =
            CreateProvider(database.Repository, ConfiguredConfiguration()).ReadConfiguration();
        string accountKey = $"oidc:{Authority.Length}:{Authority}:{Subject}";
        string expectedSelector = LengthPrefixedSha256(
            "configured-administrator-selector-v1", "provider", "keycloak", "account-key", accountKey);
        string expectedConfiguration = LengthPrefixedSha256(
            "configured-administrator-configuration-v1",
            "mode", "ConfiguredAdministrator",
            "provider", "keycloak",
            "account-key", accountKey,
            "generation", "1",
            "administrator-email", Email,
            "administrator-first-name", "Configured",
            "administrator-last-name", "Administrator",
            "deployment-mode", "SingleTenant",
            "site-name", "Independent Operator",
            "support-email", "contact@example.test",
            "canonical-url", "https://example.test",
            "locale", "en",
            "time-zone", "UTC",
            "purpose", string.Empty,
            "administration-access-mode", CompleteInstanceOnboardingRequest.EmbeddedAdministrationAccess,
            "admin-host", string.Empty,
            "instance-name", "Independent Operator",
            "directory-public-name", "Independent Operator",
            "directory-legal-name", "Independent Operator ASBL",
            "directory-operator-kind", "registered_organization",
            "directory-jurisdiction", "BE",
            "directory-registration-id", "BE 0123.456.789",
            "directory-contact-email", "contact@example.test",
            "directory-legal-notice-url", "https://example.test/legal",
            "directory-terms-url", "https://example.test/terms",
            "directory-privacy-url", "https://example.test/privacy");

        await Assert.That(first.SelectorFingerprint).IsEqualTo(expectedSelector);
        await Assert.That(first.ConfigurationFingerprint).IsEqualTo(expectedConfiguration);
        await Assert.That(second.SelectorFingerprint).IsEqualTo(first.SelectorFingerprint);
        await Assert.That(second.ConfigurationFingerprint).IsEqualTo(first.ConfigurationFingerprint);
        await Assert.That(first.SelectorFingerprint!).Matches("^[0-9a-f]{64}$");
        await Assert.That(first.ConfigurationFingerprint!).Matches("^[0-9a-f]{64}$");

        Dictionary<string, string?> changed = ConfiguredValues();
        changed["INSTANCE_BOOTSTRAP_ADMIN_EMAIL"] = "other.admin@example.test";
        ConfiguredAdministratorBootstrapProvider.ConfigurationSnapshot changedSnapshot =
            CreateProvider(database.Repository, BuildConfiguration(changed)).ReadConfiguration();
        await Assert.That(changedSnapshot.SelectorFingerprint).IsEqualTo(first.SelectorFingerprint);
        await Assert.That(changedSnapshot.ConfigurationFingerprint).IsNotEqualTo(first.ConfigurationFingerprint);
    }

    [Test]
    public async Task VerifiedBinding_RequiresExactAccountAndPersistedGenerationEvidence()
    {
        await using var database = await BootstrapDatabase.CreateAsync();
        IConfigurationRoot configuration = ConfiguredConfiguration();
        ConfiguredAdministratorBootstrapProvider provider = CreateProvider(database.Repository, configuration);
        var runner = new ConfiguredAdministratorBootstrapStartupRunner(
            provider, database.Repository, database.UnitOfWork, new FixedTimeProvider(PreparedAt));
        await runner.PrepareAsync();
        ProviderAccountKey exact = provider.ReadConfiguration().AccountKey!;

        ConfiguredAdministratorBootstrapBinding? binding = await provider.GetVerifiedBindingAsync(exact);
        await Assert.That(binding).IsNotNull();
        await Assert.That(binding!.AccountKey).IsEqualTo(exact);
        await Assert.That(binding.Generation).IsEqualTo(1);
        await Assert.That(binding.AdministratorProfile.Email).IsEqualTo(Email);

        ProviderAccountKey wrongSubject = PlatformIdentityPrincipalExtensions.CreateOidcAccountKey(Authority, "other-subject");
        await Assert.That(await provider.GetVerifiedBindingAsync(wrongSubject)).IsNull();
        await Assert.That(await provider.GetVerifiedBindingAsync(
            new ProviderAccountKey(AuthenticationProviderKind.Atproto, Did))).IsNull();

        configuration["INSTANCE_BOOTSTRAP_BINDING_GENERATION"] = "2";
        await Assert.That(await provider.GetVerifiedBindingAsync(provider.ReadConfiguration().AccountKey!)).IsNull();
        configuration["INSTANCE_BOOTSTRAP_BINDING_GENERATION"] = "1";
        configuration["INSTANCE_BOOTSTRAP_ADMIN_EMAIL"] = "drift@example.test";
        await Assert.That(await provider.GetVerifiedBindingAsync(provider.ReadConfiguration().AccountKey!)).IsNull();
    }

    [Test]
    public async Task StartupPreparation_SerializablyCreatesConvergesAndSupersedesUsingRealPersistence()
    {
        await using var database = await BootstrapDatabase.CreateAsync();
        IConfigurationRoot configuration = ConfiguredConfiguration();
        ConfiguredAdministratorBootstrapProvider provider = CreateProvider(database.Repository, configuration);
        var runner = new ConfiguredAdministratorBootstrapStartupRunner(
            provider, database.Repository, database.UnitOfWork, new FixedTimeProvider(PreparedAt));

        await runner.PrepareAsync();
        await runner.PrepareAsync();
        IReadOnlyList<InstanceBootstrapState> converged = await database.Repository.GetAll();
        await Assert.That(converged).Count().IsEqualTo(1);
        await Assert.That(converged[0].Status).IsEqualTo(InstanceBootstrapStatus.Pending);

        configuration["INSTANCE_BOOTSTRAP_BINDING_GENERATION"] = "2";
        configuration["INSTANCE_BOOTSTRAP_ADMIN_SUBJECT"] = "corrected-subject";
        await runner.PrepareAsync();
        IReadOnlyList<InstanceBootstrapState> corrected = await database.Repository.GetAll();
        await Assert.That(corrected).Count().IsEqualTo(2);
        await Assert.That(corrected.Single(state => state.Generation == 1).Status)
            .IsEqualTo(InstanceBootstrapStatus.Superseded);
        await Assert.That(corrected.Single(state => state.Generation == 2).Status)
            .IsEqualTo(InstanceBootstrapStatus.Pending);
    }

    [Test]
    public async Task DriftAndRegression_FailWithoutPersistedMutation()
    {
        await using var database = await BootstrapDatabase.CreateAsync();
        IConfigurationRoot configuration = ConfiguredConfiguration();
        ConfiguredAdministratorBootstrapProvider provider = CreateProvider(database.Repository, configuration);
        var runner = new ConfiguredAdministratorBootstrapStartupRunner(
            provider, database.Repository, database.UnitOfWork, new FixedTimeProvider(PreparedAt));
        await runner.PrepareAsync();
        BootstrapEvidence before = Evidence((await database.Repository.GetAll()).Single());

        configuration["INSTANCE_BOOTSTRAP_ADMIN_EMAIL"] = "same-generation-drift@example.test";
        await Assert.That(async () => await runner.PrepareAsync())
            .Throws<ConfiguredAdministratorBootstrapException>()
            .WithMessage("instance_bootstrap_same_generation_drift");
        database.Context.ChangeTracker.Clear();
        await Assert.That(Evidence((await database.Repository.GetAll()).Single())).IsEqualTo(before);

        configuration["INSTANCE_BOOTSTRAP_BINDING_GENERATION"] = "2";
        await runner.PrepareAsync();
        IReadOnlyList<BootstrapEvidence> beforeRegression = (await database.Repository.GetAll())
            .Select(Evidence).OrderBy(state => state.Generation).ToArray();
        configuration["INSTANCE_BOOTSTRAP_BINDING_GENERATION"] = "1";
        await Assert.That(async () => await runner.PrepareAsync())
            .Throws<ConfiguredAdministratorBootstrapException>()
            .WithMessage("instance_bootstrap_generation_regression");
        database.Context.ChangeTracker.Clear();
        IReadOnlyList<BootstrapEvidence> afterRegression = (await database.Repository.GetAll())
            .Select(Evidence).OrderBy(state => state.Generation).ToArray();
        await Assert.That(afterRegression).IsEquivalentTo(beforeRegression);
    }

    [Test]
    public async Task CompletedGeneration_IsFinalAcrossConfigurationDrift()
    {
        await using var database = await BootstrapDatabase.CreateAsync();
        IConfigurationRoot configuration = ConfiguredConfiguration();
        ConfiguredAdministratorBootstrapProvider provider = CreateProvider(database.Repository, configuration);
        var runner = new ConfiguredAdministratorBootstrapStartupRunner(
            provider, database.Repository, database.UnitOfWork, new FixedTimeProvider(PreparedAt));
        await runner.PrepareAsync();
        InstanceBootstrapState current = (await database.Repository.GetCurrentForUpdate())!;
        _ = current.CompleteConfiguredAdministrator(
            AuthenticationProviderKind.Keycloak,
            1,
            current.SelectorFingerprint!,
            Guid.Parse("01991f00-0000-7000-8000-000000000099"),
            PreparedAt.AddMinutes(1));
        await database.Context.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF;");
        await database.Repository.Update(current);
        BootstrapEvidence completed = Evidence(current);

        configuration["INSTANCE_BOOTSTRAP_BINDING_GENERATION"] = "99";
        configuration["INSTANCE_BOOTSTRAP_ADMIN_PROVIDER"] = "atproto";
        configuration["INSTANCE_BOOTSTRAP_ADMIN_SUBJECT"] = Did;
        configuration["INSTANCE_BOOTSTRAP_ADMIN_EMAIL"] = "replacement@example.test";
        await runner.PrepareAsync();
        database.Context.ChangeTracker.Clear();
        IReadOnlyList<InstanceBootstrapState> states = await database.Repository.GetAll();
        await Assert.That(states).Count().IsEqualTo(1);
        await Assert.That(Evidence(states[0])).IsEqualTo(completed);
    }

    [Test]
    public async Task CompletedGeneration_IsFinalWhenDeploymentConfigurationIsNoLongerParseable()
    {
        foreach (Action<IConfigurationRoot> invalidate in new Action<IConfigurationRoot>[]
                 {
                     configuration =>
                     {
                         configuration["INSTANCE_BOOTSTRAP_ADMIN_PROVIDER"] = null;
                         configuration["INSTANCE_BOOTSTRAP_ADMIN_SUBJECT"] = null;
                     },
                     configuration =>
                     {
                         configuration["INSTANCE_BOOTSTRAP_MODE"] = "configured-administrator";
                     },
                     configuration =>
                     {
                         configuration["Deployment:Mode"] = "invalid";
                     }
                 })
        {
            await using var database = await BootstrapDatabase.CreateAsync();
            IConfigurationRoot configuration = ConfiguredConfiguration();
            ConfiguredAdministratorBootstrapProvider provider = CreateProvider(database.Repository, configuration);
            var runner = new ConfiguredAdministratorBootstrapStartupRunner(
                provider, database.Repository, database.UnitOfWork, new FixedTimeProvider(PreparedAt));
            await runner.PrepareAsync();
            InstanceBootstrapState current = (await database.Repository.GetCurrentForUpdate())!;
            _ = current.CompleteConfiguredAdministrator(
                AuthenticationProviderKind.Keycloak,
                1,
                current.SelectorFingerprint!,
                Guid.Parse("01991f00-0000-7000-8000-000000000099"),
                PreparedAt.AddMinutes(1));
            await database.Context.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF;");
            await database.Repository.Update(current);
            BootstrapEvidence completed = Evidence(current);

            invalidate(configuration);

            await runner.PrepareAsync();
            database.Context.ChangeTracker.Clear();
            IReadOnlyList<InstanceBootstrapState> states = await database.Repository.GetAll();
            await Assert.That(states).Count().IsEqualTo(1);
            await Assert.That(Evidence(states[0])).IsEqualTo(completed);
        }
    }

    [Test]
    public async Task InteractivePreparation_ConvergesOnlyWithMatchingInteractiveState()
    {
        await using var database = await BootstrapDatabase.CreateAsync();
        IConfigurationRoot interactiveConfiguration = InteractiveConfiguration();
        ConfiguredAdministratorBootstrapProvider interactiveProvider =
            CreateProvider(database.Repository, interactiveConfiguration);
        var interactiveRunner = new ConfiguredAdministratorBootstrapStartupRunner(
            interactiveProvider, database.Repository, database.UnitOfWork, new FixedTimeProvider(PreparedAt));
        await interactiveRunner.PrepareAsync();
        await Assert.That(await database.Repository.GetCurrent()).IsNull();

        ConfiguredAdministratorBootstrapProvider configuredProvider =
            CreateProvider(database.Repository, ConfiguredConfiguration());
        var configuredRunner = new ConfiguredAdministratorBootstrapStartupRunner(
            configuredProvider, database.Repository, database.UnitOfWork, new FixedTimeProvider(PreparedAt));
        await configuredRunner.PrepareAsync();
        BootstrapEvidence before = Evidence((await database.Repository.GetCurrent())!);
        await Assert.That(async () => await interactiveRunner.PrepareAsync())
            .Throws<ConfiguredAdministratorBootstrapException>()
            .WithMessage("instance_bootstrap_interactive_state_conflict");
        database.Context.ChangeTracker.Clear();
        await Assert.That(Evidence((await database.Repository.GetCurrent())!)).IsEqualTo(before);
    }

    [Test]
    public async Task FailuresAndCapturedLogs_ContainZeroBootstrapPiiOrSelectorValues()
    {
        await using var database = await BootstrapDatabase.CreateAsync();
        string subjectCanary = "subject-canary-8cbce742";
        string emailCanary = "pii-canary-8cbce742@example.test";
        string firstNameCanary = "FirstNameCanary8cbce742";
        string lastNameCanary = "LastNameCanary8cbce742";
        string issuerCanary = "https://issuer-canary-8cbce742.example.test/realms/private";
        Dictionary<string, string?> values = ConfiguredValues();
        values["INSTANCE_BOOTSTRAP_ADMIN_SUBJECT"] = subjectCanary;
        values["INSTANCE_BOOTSTRAP_ADMIN_EMAIL"] = emailCanary;
        values["INSTANCE_BOOTSTRAP_ADMIN_FIRST_NAME"] = firstNameCanary;
        values["INSTANCE_BOOTSTRAP_ADMIN_LAST_NAME"] = lastNameCanary;
        values["Keycloak:Authority"] = issuerCanary;
        values["INSTANCE_BOOTSTRAP_BINDING_GENERATION"] = "invalid";
        var capture = new CapturingLoggerProvider();
        using ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(capture));
        _ = loggerFactory.CreateLogger<ConfiguredAdministratorBootstrapProvider>();

        ConfiguredAdministratorBootstrapException failure = (await Assert.ThrowsAsync<ConfiguredAdministratorBootstrapException>(
            () => Task.Run(() => CreateProvider(database.Repository, BuildConfiguration(values)).ReadConfiguration())))!;
        string observable = string.Join('|', new[]
        {
            failure.ReasonCode,
            failure.Message,
            failure.ToString(),
            string.Join('|', capture.Messages)
        });
        await Assert.That(failure.ReasonCode).IsEqualTo("instance_bootstrap_generation_invalid");
        foreach (string forbidden in new[]
                 { subjectCanary, emailCanary, firstNameCanary, lastNameCanary, issuerCanary })
        {
            await Assert.That(observable).DoesNotContain(forbidden);
        }
        await Assert.That(capture.Messages).IsEmpty();
    }

    [Test]
    public async Task InfrastructureDi_ActivatesConfiguredProviderForRuntimeContract()
    {
        await using var database = await BootstrapDatabase.CreateAsync();
        IConfigurationRoot configuration = ConfiguredConfiguration();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IInstanceOperatorIdentity>(OperatorIdentity());
        services.AddScoped<ExploreDbContext>(_ => database.Context);
        services.AddScoped<IInstanceBootstrapStateRepository, InstanceBootstrapStateRepository>();
        services.AddScoped<IUnitOfWork, EfCoreUnitOfWork>();
        services.ConfigureInfrastructureServices(configuration);
        await using ServiceProvider serviceProvider = services.BuildServiceProvider();
        await using AsyncServiceScope scope = serviceProvider.CreateAsyncScope();

        IConfiguredAdministratorBootstrapProvider contract =
            scope.ServiceProvider.GetRequiredService<IConfiguredAdministratorBootstrapProvider>();
        ConfiguredAdministratorBootstrapProvider concrete =
            scope.ServiceProvider.GetRequiredService<ConfiguredAdministratorBootstrapProvider>();
        ConfiguredAdministratorBootstrapStartupRunner runner =
            scope.ServiceProvider.GetRequiredService<ConfiguredAdministratorBootstrapStartupRunner>();

        await Assert.That(contract).IsSameReferenceAs(concrete);
        await Assert.That(contract).IsTypeOf<ConfiguredAdministratorBootstrapProvider>();
        await Assert.That(runner).IsNotNull();
    }

    private static ConfiguredAdministratorBootstrapProvider CreateProvider(
        IInstanceBootstrapStateRepository repository,
        IConfiguration configuration) =>
        new(configuration, OperatorIdentity(), repository);

    private static async Task AssertReason(
        IInstanceBootstrapStateRepository repository,
        IReadOnlyDictionary<string, string?> values,
        string expectedReason)
    {
        ConfiguredAdministratorBootstrapProvider provider =
            CreateProvider(repository, BuildConfiguration(values));
        ConfiguredAdministratorBootstrapException exception =
            (await Assert.ThrowsAsync<ConfiguredAdministratorBootstrapException>(
                () => Task.Run(provider.ReadConfiguration)))!;
        await Assert.That(exception.ReasonCode).IsEqualTo(expectedReason);
        await Assert.That(exception.Message).IsEqualTo(expectedReason);
    }

    private static IConfigurationRoot ConfiguredConfiguration() =>
        BuildConfiguration(ConfiguredValues());

    private static IConfigurationRoot InteractiveConfiguration() =>
        BuildConfiguration(InteractiveValues());

    private static Dictionary<string, string?> ConfiguredValues() => new(StringComparer.Ordinal)
    {
        ["INSTANCE_BOOTSTRAP_MODE"] = "ConfiguredAdministrator",
        ["INSTANCE_BOOTSTRAP_ADMIN_PROVIDER"] = "keycloak",
        ["INSTANCE_BOOTSTRAP_ADMIN_SUBJECT"] = Subject,
        ["INSTANCE_BOOTSTRAP_BINDING_GENERATION"] = "1",
        ["INSTANCE_BOOTSTRAP_ADMIN_EMAIL"] = Email,
        ["INSTANCE_BOOTSTRAP_ADMIN_FIRST_NAME"] = "Configured",
        ["INSTANCE_BOOTSTRAP_ADMIN_LAST_NAME"] = "Administrator",
        ["Keycloak:Authority"] = Authority,
        ["Deployment:Mode"] = "SingleTenant"
    };

    private static Dictionary<string, string?> InteractiveValues() => new(StringComparer.Ordinal)
    {
        ["INSTANCE_BOOTSTRAP_MODE"] = "Interactive",
        ["Keycloak:Authority"] = Authority,
        ["Deployment:Mode"] = "SingleTenant"
    };

    private static IConfigurationRoot BuildConfiguration(
        IEnumerable<KeyValuePair<string, string?>> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static InstanceOperatorIdentity OperatorIdentity() =>
        InstanceOperatorIdentity.Create(new InstanceOperatorIdentityOptions
        {
            OperatorId = Guid.Parse("0198e2a4-5340-7f89-8abc-b8bdf43e0ea8"),
            PublicName = "Independent Operator",
            LegalName = "Independent Operator ASBL",
            IsOfficialInstance = false,
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

    private static string LengthPrefixedSha256(params string[] fields)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> prefix = stackalloc byte[sizeof(int)];
        foreach (string field in fields)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(field);
            BinaryPrimitives.WriteInt32BigEndian(prefix, bytes.Length);
            hash.AppendData(prefix);
            hash.AppendData(bytes);
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static BootstrapEvidence Evidence(InstanceBootstrapState state) => new(
        state.Id,
        state.Status,
        state.Mode,
        state.ProviderKind,
        state.DeploymentMode,
        state.Generation,
        state.ConfigurationFingerprint,
        state.SelectorFingerprint,
        state.CompletedIdentityFingerprint,
        state.CreatedAt,
        state.SupersededAt,
        state.CompletedAt,
        state.CompletedByUserId);

    private sealed record BootstrapEvidence(
        Guid Id,
        InstanceBootstrapStatus Status,
        InstanceBootstrapMode Mode,
        AuthenticationProviderKind? ProviderKind,
        DeploymentMode DeploymentMode,
        long Generation,
        string? ConfigurationFingerprint,
        string? SelectorFingerprint,
        string? CompletedIdentityFingerprint,
        DateTime CreatedAt,
        DateTime? SupersededAt,
        DateTime? CompletedAt,
        Guid? CompletedByUserId);

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow);
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public List<string> Messages { get; } = [];
        public ILogger CreateLogger(string categoryName) => new CaptureLogger(Messages);
        public void Dispose() { }

        private sealed class CaptureLogger(List<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter) =>
                messages.Add(formatter(state, exception));
        }
    }

    private sealed class BootstrapDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private BootstrapDatabase(
            SqliteConnection connection,
            ExploreDbContext context,
            InstanceBootstrapStateRepository repository)
        {
            _connection = connection;
            Context = context;
            Repository = repository;
            UnitOfWork = new EfCoreUnitOfWork(context);
        }

        public ExploreDbContext Context { get; }
        public InstanceBootstrapStateRepository Repository { get; }
        public EfCoreUnitOfWork UnitOfWork { get; }

        public static async Task<BootstrapDatabase> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<ExploreDbContext>()
                .UseSqlite(connection)
                .UseSnakeCaseNamingConvention()
                .Options;
            var context = new ExploreDbContext(options);
            await context.Database.EnsureCreatedAsync();
            return new BootstrapDatabase(
                connection,
                context,
                new InstanceBootstrapStateRepository(context));
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
