using System.Security.Cryptography;
using Explore.Domain.Secrets;
using Explore.ServiceDefaults.Secrets;
using Microsoft.Extensions.Configuration;

namespace Explore.Infrastructure.Tests.Secrets;

public sealed class RuntimeSecretEnvironmentTests
{
    private static readonly SecretAcquisitionEntry[] Entries = SecretDefinitionRegistry.All.Values.Select(definition =>
        new SecretAcquisitionEntry(definition.DefaultInfisicalPath,
            definition.DefaultInfisicalKey, definition.DefaultEnvironmentVariableName)).ToArray();
    [Test]
    [Arguments(SecretConsumerRole.Api)]
    [Arguments(SecretConsumerRole.Bff)]
    [Arguments(SecretConsumerRole.Combined)]
    [Arguments(SecretConsumerRole.Migration)]
    [Arguments(SecretConsumerRole.ModerationMeasurement)]
    public async Task ProcessProjectionKeepsOnlyItsIdentityAndNoProviderAdministration(SecretConsumerRole consumer)
    {
        var inputs = Inputs();
        var target = new Dictionary<string, object>
        {
            ["KEYCLOAK_ADMIN_PASSWORD"] = CreateSecret(),
            ["CERBOS_ADMIN_PASSWORD_HASH"] = CreateSecret(),
            ["Cerbos__AdminApi__Endpoints__0"] = "http://cerbos.test",
            ["ISLAMU_ASPIRE_MODE"] = "FullLocal",
            ["Storage__Local__RootPath"] = "/data"
        };
        string inheritedCanary = CreateSecret();
        inputs["INFISICAL_CLIENT_SECRET"] = inheritedCanary;
        inputs["UNDECLARED_SECRET"] = inheritedCanary;
        RuntimeSecretEnvironment.Apply(target, Configuration(inputs), inputs.Keys, consumer, SecretAuthorityKind.Infisical, Entries);

        await Assert.That(target["SecretProvider__Provider"]).IsEqualTo("Infisical");
        await Assert.That(target["SecretProvider__Consumer"]).IsEqualTo(consumer.ToString());
        await Assert.That(target["SecretProvider__Infisical__ClientId"])
            .IsEqualTo(inputs[$"INFISICAL_{consumer.ToString().ToUpperInvariant()}_CLIENT_ID"]!);
        await Assert.That(target["SecretProvider__Infisical__ClientSecret"])
            .IsEqualTo(inputs[$"INFISICAL_{consumer.ToString().ToUpperInvariant()}_CLIENT_SECRET"]!);
        await Assert.That(target["KEYCLOAK_ADMIN_PASSWORD"]).IsEqualTo(string.Empty);
        await Assert.That(target["CERBOS_ADMIN_PASSWORD_HASH"]).IsEqualTo(string.Empty);
        await Assert.That(target["UNDECLARED_SECRET"]).IsEqualTo(string.Empty);
        await Assert.That(target.Values.Contains(inheritedCanary)).IsFalse();
        await Assert.That(target["Storage__Local__RootPath"]).IsEqualTo("/data");
        await Assert.That(target["Cerbos__AdminApi__Endpoints__0"]).IsEqualTo("http://cerbos.test");
        foreach (SecretConsumerRole sibling in Enum.GetValues<SecretConsumerRole>().Where(value => value != consumer))
            await Assert.That(target.Values.Contains(
                inputs[$"INFISICAL_{sibling.ToString().ToUpperInvariant()}_CLIENT_SECRET"]!)).IsFalse();
    }

    [Test]
    public async Task PublicAudienceAndPermittedClientsRemainIndependentOfSecretSource()
    {
        var inputs = Inputs();
        var target = new Dictionary<string, object>
        {
            ["KEYCLOAK__AUDIENCE"] = "event-resource",
            ["KEYCLOAK__PERMITTEDCLIENTS__0"] = "event-browser"
        };
        RuntimeSecretEnvironment.Apply(target, Configuration(inputs), inputs.Keys,
            SecretConsumerRole.Api, SecretAuthorityKind.Infisical, Entries);
        await Assert.That(target["KEYCLOAK__AUDIENCE"]).IsEqualTo("event-resource");
        await Assert.That(target["KEYCLOAK__PERMITTEDCLIENTS__0"]).IsEqualTo("event-browser");
    }

    [Test]
    public async Task MissingPurposeIdentityCannotFallBackToSharedBootstrap()
    {
        var inputs = Inputs();
        inputs.Remove("INFISICAL_API_CLIENT_SECRET");
        inputs["INFISICAL_CLIENT_SECRET"] = CreateSecret();
        await Assert.That(() => RuntimeSecretEnvironment.Apply(
            new Dictionary<string, object>(), Configuration(inputs), inputs.Keys,
            SecretConsumerRole.Api, SecretAuthorityKind.Infisical, Entries)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ReusedIdentityIsRejectedBeforeProjectLaunch()
    {
        var inputs = Inputs();
        inputs["INFISICAL_BFF_CLIENT_ID"] = inputs["INFISICAL_API_CLIENT_ID"];
        await Assert.That(() => RuntimeSecretEnvironment.Apply(
            new Dictionary<string, object>(), Configuration(inputs), inputs.Keys,
            SecretConsumerRole.Api, SecretAuthorityKind.Infisical, Entries)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task EnvironmentProjectionSeparatesFrontendAndLifecycleCanaries()
    {
        var inputs = Inputs();
        inputs["KEYCLOAK_BLAZOR_CLIENT_SECRET"] = CreateSecret();
        inputs["KEYCLOAK_LIFECYCLE_CLIENT_SECRET"] = CreateSecret();
        inputs["MAIL_SMTP_PASSWORD"] = CreateSecret();
        var api = new Dictionary<string, object>();
        var bff = new Dictionary<string, object>();
        var combined = new Dictionary<string, object>();
        RuntimeSecretEnvironment.Apply(api, Configuration(inputs), inputs.Keys, SecretConsumerRole.Api, SecretAuthorityKind.Environment, Entries);
        RuntimeSecretEnvironment.Apply(bff, Configuration(inputs), inputs.Keys, SecretConsumerRole.Bff, SecretAuthorityKind.Environment, Entries);
        RuntimeSecretEnvironment.Apply(combined, Configuration(inputs), inputs.Keys, SecretConsumerRole.Combined, SecretAuthorityKind.Environment, Entries);
        await Assert.That(api["KEYCLOAK_BLAZOR_CLIENT_SECRET"]).IsEqualTo(string.Empty);
        await Assert.That(api["KEYCLOAK_LIFECYCLE_CLIENT_SECRET"]).IsEqualTo(inputs["KEYCLOAK_LIFECYCLE_CLIENT_SECRET"]!);
        await Assert.That(api["MAIL_SMTP_PASSWORD"]).IsEqualTo(inputs["MAIL_SMTP_PASSWORD"]!);
        await Assert.That(bff["KEYCLOAK_LIFECYCLE_CLIENT_SECRET"]).IsEqualTo(string.Empty);
        await Assert.That(bff["MAIL_SMTP_PASSWORD"]).IsEqualTo(string.Empty);
        await Assert.That(bff["KEYCLOAK_BLAZOR_CLIENT_SECRET"]).IsEqualTo(inputs["KEYCLOAK_BLAZOR_CLIENT_SECRET"]!);
        await Assert.That(combined["KEYCLOAK_BLAZOR_CLIENT_SECRET"]).IsEqualTo(inputs["KEYCLOAK_BLAZOR_CLIENT_SECRET"]!);
        await Assert.That(combined["KEYCLOAK_LIFECYCLE_CLIENT_SECRET"]).IsEqualTo(inputs["KEYCLOAK_LIFECYCLE_CLIENT_SECRET"]!);
    }

    [Test]
    [Arguments(SecretConsumerRole.Api, SecretAuthorityKind.Environment)]
    [Arguments(SecretConsumerRole.Combined, SecretAuthorityKind.Environment)]
    [Arguments(SecretConsumerRole.Bff, SecretAuthorityKind.Environment)]
    [Arguments(SecretConsumerRole.Migration, SecretAuthorityKind.Environment)]
    [Arguments(SecretConsumerRole.Api, SecretAuthorityKind.Infisical)]
    [Arguments(SecretConsumerRole.Combined, SecretAuthorityKind.Infisical)]
    [Arguments(SecretConsumerRole.Bff, SecretAuthorityKind.Infisical)]
    [Arguments(SecretConsumerRole.Migration, SecretAuthorityKind.Infisical)]
    public async Task LocalWriterPasswordsReachOnlyTheirSelectedBackendAuthority(
        SecretConsumerRole consumer, SecretAuthorityKind provider)
    {
        var inputs = Inputs();
        string applicationWriter = CreateSecret();
        string identityWriter = CreateSecret();
        inputs["DATABASE_LOCAL_IDENTITY_WRITER_PASSWORD"] = applicationWriter;
        inputs["IDENTITY_DATABASE_LOCAL_IDENTITY_WRITER_PASSWORD"] = identityWriter;
        var target = new Dictionary<string, object>
        {
            ["Database__LocalIdentityWriter__Password"] = applicationWriter,
            ["IdentityDatabase__LocalIdentityWriter__Password"] = identityWriter
        };

        RuntimeSecretEnvironment.Apply(target, Configuration(inputs), inputs.Keys, consumer, provider, Entries);

        bool delivers = provider == SecretAuthorityKind.Environment
            && consumer is SecretConsumerRole.Api or SecretConsumerRole.Combined;
        await Assert.That(Equals(target["DATABASE_LOCAL_IDENTITY_WRITER_PASSWORD"],
            delivers ? applicationWriter : string.Empty)).IsTrue();
        await Assert.That(Equals(target["IDENTITY_DATABASE_LOCAL_IDENTITY_WRITER_PASSWORD"],
            delivers ? identityWriter : string.Empty)).IsTrue();
        await Assert.That(Equals(target["Database__LocalIdentityWriter__Password"],
            delivers ? applicationWriter : string.Empty)).IsTrue();
        await Assert.That(Equals(target["IdentityDatabase__LocalIdentityWriter__Password"],
            delivers ? identityWriter : string.Empty)).IsTrue();
        if (!delivers)
        {
            await Assert.That(target.Values.Contains(applicationWriter)).IsFalse();
            await Assert.That(target.Values.Contains(identityWriter)).IsFalse();
        }
    }

    [Test]
    [Arguments(SecretConsumerRole.Api)]
    [Arguments(SecretConsumerRole.Combined)]
    public async Task MissingLocalWriterPasswordNeverBorrowsRuntimeGlobalOrMigrator(SecretConsumerRole consumer)
    {
        var inputs = Inputs();
        foreach (string name in new[]
        {
            "DATABASE_RUNTIME_PASSWORD", "DATABASE_GLOBAL_PASSWORD", "DATABASE_MIGRATOR_PASSWORD",
            "IDENTITY_DATABASE_RUNTIME_PASSWORD", "IDENTITY_DATABASE_MIGRATOR_PASSWORD"
        })
            inputs[name] = CreateSecret();
        var inherited = inputs.Keys.Concat(new[]
        {
            "DATABASE_LOCAL_IDENTITY_WRITER_PASSWORD", "IDENTITY_DATABASE_LOCAL_IDENTITY_WRITER_PASSWORD",
            "Database__LocalIdentityWriter__Password", "IdentityDatabase__LocalIdentityWriter__Password"
        }).ToArray();
        var target = new Dictionary<string, object>();

        RuntimeSecretEnvironment.Apply(target, Configuration(inputs), inherited, consumer, SecretAuthorityKind.Environment, Entries);

        foreach (string name in inherited.Where(name => name.Contains("LOCAL_IDENTITY_WRITER", StringComparison.Ordinal)
            || name.Contains("LocalIdentityWriter", StringComparison.Ordinal)))
            await Assert.That(Equals(target[name], string.Empty)).IsTrue();
    }

    [Test]
    [Arguments(SecretConsumerRole.Api, SecretAuthorityKind.Environment)]
    [Arguments(SecretConsumerRole.Combined, SecretAuthorityKind.Environment)]
    [Arguments(SecretConsumerRole.Migration, SecretAuthorityKind.Environment)]
    [Arguments(SecretConsumerRole.Bff, SecretAuthorityKind.Environment)]
    [Arguments(SecretConsumerRole.Api, SecretAuthorityKind.Infisical)]
    [Arguments(SecretConsumerRole.Combined, SecretAuthorityKind.Infisical)]
    [Arguments(SecretConsumerRole.Migration, SecretAuthorityKind.Infisical)]
    [Arguments(SecretConsumerRole.Bff, SecretAuthorityKind.Infisical)]
    public async Task LocalWriterEnrollmentProjectsDeclaredLoginNamesWithoutSiblingPasswords(
        SecretConsumerRole consumer, SecretAuthorityKind provider)
    {
        var inputs = Inputs();
        string[] names =
        [
            "DATABASE_RUNTIME_USERNAME", "DATABASE_GLOBAL_USERNAME", "DATABASE_MIGRATOR_USERNAME",
            "DATABASE_EMAIL_POLICY_INSTANCE_MUTATION_USERNAME", "DATABASE_EMAIL_POLICY_TENANT_MUTATION_USERNAME",
            "DATABASE_LOCAL_IDENTITY_WRITER_USERNAME", "IDENTITY_DATABASE_RUNTIME_USERNAME",
            "IDENTITY_DATABASE_MIGRATOR_USERNAME", "IDENTITY_DATABASE_LOCAL_IDENTITY_WRITER_USERNAME"
        ];
        foreach (string name in names)
            inputs[name] = "declared_" + Guid.CreateVersion7().ToString("N");
        inputs["DATABASE_LOCAL_IDENTITY_WRITER_PASSWORD"] = CreateSecret();
        inputs["IDENTITY_DATABASE_LOCAL_IDENTITY_WRITER_PASSWORD"] = CreateSecret();
        var target = new Dictionary<string, object>();

        RuntimeSecretEnvironment.Apply(target, Configuration(inputs), inputs.Keys, consumer, provider, Entries);

        bool projectsNames = provider == SecretAuthorityKind.Environment && consumer != SecretConsumerRole.Bff;
        foreach (string name in names)
            await Assert.That(Equals(target[name], projectsNames ? inputs[name] : string.Empty)).IsTrue();
        if (consumer is SecretConsumerRole.Migration or SecretConsumerRole.Bff)
        {
            await Assert.That(Equals(target["DATABASE_LOCAL_IDENTITY_WRITER_PASSWORD"], string.Empty)).IsTrue();
            await Assert.That(Equals(target["IDENTITY_DATABASE_LOCAL_IDENTITY_WRITER_PASSWORD"], string.Empty)).IsTrue();
        }
    }

    [Test]
    [Arguments(SecretConsumerRole.Api, SecretAuthorityKind.Environment, "only")]
    [Arguments(SecretConsumerRole.Combined, SecretAuthorityKind.Environment, "only")]
    [Arguments(SecretConsumerRole.Api, SecretAuthorityKind.Environment, "both")]
    [Arguments(SecretConsumerRole.Combined, SecretAuthorityKind.Environment, "both")]
    [Arguments(SecretConsumerRole.Api, SecretAuthorityKind.Environment, "empty")]
    [Arguments(SecretConsumerRole.Combined, SecretAuthorityKind.Environment, "empty")]
    [Arguments(SecretConsumerRole.Api, SecretAuthorityKind.Infisical, "only")]
    [Arguments(SecretConsumerRole.Combined, SecretAuthorityKind.Infisical, "only")]
    [Arguments(SecretConsumerRole.Api, SecretAuthorityKind.Infisical, "both")]
    [Arguments(SecretConsumerRole.Combined, SecretAuthorityKind.Infisical, "both")]
    [Arguments(SecretConsumerRole.Api, SecretAuthorityKind.Infisical, "empty")]
    [Arguments(SecretConsumerRole.Combined, SecretAuthorityKind.Infisical, "empty")]
    public async Task StructuredLocalWriterAuthorityMasksFlatAliasesWithoutLeakingDiscardedValues(
        SecretConsumerRole consumer, SecretAuthorityKind provider, string form)
    {
        var inputs = Inputs();
        (string Canonical, string Alias, string Value)[] coordinates =
        [
            ("Database:LocalIdentityWriter:Username", "DATABASE_LOCAL_IDENTITY_WRITER_USERNAME",
                "writer_" + Guid.CreateVersion7().ToString("N")),
            ("Database:LocalIdentityWriter:Password", "DATABASE_LOCAL_IDENTITY_WRITER_PASSWORD",
                CreateSecret()),
            ("IdentityDatabase:LocalIdentityWriter:Username", "IDENTITY_DATABASE_LOCAL_IDENTITY_WRITER_USERNAME",
                "writer_" + Guid.CreateVersion7().ToString("N")),
            ("IdentityDatabase:LocalIdentityWriter:Password", "IDENTITY_DATABASE_LOCAL_IDENTITY_WRITER_PASSWORD",
                CreateSecret())
        ];
        var discarded = new List<string>();
        foreach (var coordinate in coordinates)
        {
            inputs[coordinate.Canonical] = form == "empty" ? string.Empty : coordinate.Value;
            if (form != "only")
            {
                string shadowed = CreateSecret();
                inputs[coordinate.Alias] = shadowed;
                discarded.Add(shadowed);
            }
        }
        string[] inherited = inputs.Keys.Select(name => name.Replace(":", "__", StringComparison.Ordinal)).ToArray();
        var target = new Dictionary<string, object>();

        RuntimeSecretEnvironment.Apply(target, Configuration(inputs), inherited, consumer, provider, Entries);

        foreach (var coordinate in coordinates)
        {
            string projectedName = coordinate.Canonical.Replace(":", "__", StringComparison.Ordinal);
            string expected = provider == SecretAuthorityKind.Environment && form != "empty"
                ? coordinate.Value : string.Empty;
            await Assert.That(Equals(target[projectedName], expected)).IsTrue();
        }
        foreach (string shadowed in discarded)
            await Assert.That(target.Values.Contains(shadowed)).IsFalse();
    }

    [Test]
    [Arguments(SecretConsumerRole.Api, SecretAuthorityKind.Environment)]
    [Arguments(SecretConsumerRole.Combined, SecretAuthorityKind.Environment)]
    [Arguments(SecretConsumerRole.Api, SecretAuthorityKind.Infisical)]
    [Arguments(SecretConsumerRole.Combined, SecretAuthorityKind.Infisical)]
    public async Task ExternalServerVersionReachesOnlyTheSelectedEnvironmentAuthority(
        SecretConsumerRole consumer, SecretAuthorityKind provider)
    {
        var inputs = Inputs();
        const string explicitVersion = "10.11.8";
        inputs["IDENTITY_DATABASE_SERVER_VERSION"] = explicitVersion;
        var target = new Dictionary<string, object>();

        RuntimeSecretEnvironment.Apply(target, Configuration(inputs), inputs.Keys, consumer, provider, Entries);

        await Assert.That(Equals(target["IDENTITY_DATABASE_SERVER_VERSION"],
            provider == SecretAuthorityKind.Environment ? explicitVersion : string.Empty)).IsTrue();
    }

    [Test]
    [Arguments(SecretConsumerRole.Api, "Finite")]
    [Arguments(SecretConsumerRole.Bff, "Finite")]
    [Arguments(SecretConsumerRole.Combined, "Finite")]
    [Arguments(SecretConsumerRole.Migration, "Finite")]
    [Arguments(SecretConsumerRole.Api, "Periodic")]
    [Arguments(SecretConsumerRole.Bff, "Periodic")]
    [Arguments(SecretConsumerRole.Combined, "Periodic")]
    [Arguments(SecretConsumerRole.Migration, "Periodic")]
    public async Task NativeTokenPolicyReachesEachSelectedProcessWithoutSharedBootstrap(
        SecretConsumerRole consumer, string lifetime)
    {
        var inputs = Inputs();
        string prefix = $"INFISICAL_{consumer.ToString().ToUpperInvariant()}";
        inputs[$"{prefix}_TOKEN_LIFETIME"] = lifetime;
        inputs[$"{prefix}_RENEW_BEFORE_SECONDS"] = "90";
        inputs[$"{prefix}_REQUEST_TIMEOUT_SECONDS"] = "20";
        if (lifetime == "Periodic")
            inputs[$"{prefix}_TOKEN_PERIOD_SECONDS"] = "600";
        string sharedSecret = CreateSecret();
        inputs["SecretProvider:Infisical:ClientSecret"] = sharedSecret;
        string[] inherited = inputs.Keys.Select(name => name.Replace(":", "__", StringComparison.Ordinal)).ToArray();
        var target = new Dictionary<string, object>();

        RuntimeSecretEnvironment.Apply(target, Configuration(inputs), inherited, consumer,
            SecretAuthorityKind.Infisical, Entries);

        await Assert.That(target["SecretProvider__Infisical__TokenLifetime"]).IsEqualTo(lifetime);
        await Assert.That(target["SecretProvider__Infisical__RenewBeforeSeconds"]).IsEqualTo("90");
        await Assert.That(target["SecretProvider__Infisical__RequestTimeoutSeconds"]).IsEqualTo("20");
        if (lifetime == "Periodic")
            await Assert.That(target["SecretProvider__Infisical__TokenPeriodSeconds"]).IsEqualTo("600");
        else
            await Assert.That(target.ContainsKey("SecretProvider__Infisical__TokenPeriodSeconds")).IsFalse();
        await Assert.That(target["SecretProvider__Infisical__ClientSecret"])
            .IsEqualTo(inputs[$"INFISICAL_{consumer.ToString().ToUpperInvariant()}_CLIENT_SECRET"]!);
        await Assert.That(target.Values.Contains(sharedSecret)).IsFalse();
    }

    [Test]
    [Arguments(SecretAuthorityKind.Environment)]
    [Arguments(SecretAuthorityKind.UserSecrets)]
    public async Task LocalAuthoritiesNeverProjectInfisicalMachineInputs(SecretAuthorityKind provider)
    {
        var inputs = Inputs();
        inputs["SecretProvider:Infisical:TokenLifetime"] = "Periodic";
        inputs["SecretProvider:Infisical:TokenPeriodSeconds"] = "600";
        string[] inherited = inputs.Keys.Select(name => name.Replace(":", "__", StringComparison.Ordinal)).ToArray();
        var target = new Dictionary<string, object>();

        RuntimeSecretEnvironment.Apply(target, Configuration(inputs), inherited, SecretConsumerRole.Api,
            provider, Entries);

        foreach (string name in target.Keys.Where(name => name.StartsWith("INFISICAL_", StringComparison.Ordinal)
            || name.StartsWith("SecretProvider__Infisical__", StringComparison.Ordinal)))
            await Assert.That(target[name]).IsEqualTo(string.Empty);
        await Assert.That(target["SecretProvider__Provider"]).IsEqualTo(provider.ToString());
        await Assert.That(target["SecretProvider__Consumer"]).IsEqualTo("Api");
    }

    [Test]
    [Arguments(SecretConsumerRole.Api)]
    [Arguments(SecretConsumerRole.Bff)]
    [Arguments(SecretConsumerRole.Combined)]
    [Arguments(SecretConsumerRole.Migration)]
    public async Task DeploymentPolicyUsesOnlyTheSelectedConsumer(SecretConsumerRole consumer)
    {
        var inputs = Inputs();
        foreach (SecretConsumerRole role in Enum.GetValues<SecretConsumerRole>())
        {
            string prefix = $"INFISICAL_{role.ToString().ToUpperInvariant()}";
            inputs[$"{prefix}_TOKEN_LIFETIME"] = role == consumer ? "Periodic" : "Finite";
            inputs[$"{prefix}_TOKEN_PERIOD_SECONDS"] = role == consumer ? "600" : "900";
            inputs[$"{prefix}_RENEW_BEFORE_SECONDS"] = role == consumer ? "90" : "120";
            inputs[$"{prefix}_REQUEST_TIMEOUT_SECONDS"] = role == consumer ? "20" : "30";
        }
        var target = new Dictionary<string, object>();
        RuntimeSecretEnvironment.Apply(target, Configuration(inputs), inputs.Keys, consumer,
            SecretAuthorityKind.Infisical, Entries);
        await Assert.That(target.GetValueOrDefault("SecretProvider__Infisical__TokenLifetime")).IsEqualTo("Periodic");
        await Assert.That(target.GetValueOrDefault("SecretProvider__Infisical__TokenPeriodSeconds")).IsEqualTo("600");
        await Assert.That(target.GetValueOrDefault("SecretProvider__Infisical__RenewBeforeSeconds")).IsEqualTo("90");
        await Assert.That(target.GetValueOrDefault("SecretProvider__Infisical__RequestTimeoutSeconds")).IsEqualTo("20");
    }

    private static string CreateSecret() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    private static IConfiguration Configuration(Dictionary<string, string?> inputs) =>
        new ConfigurationBuilder().AddInMemoryCollection(inputs).Build();

    private static Dictionary<string, string?> Inputs()
    {
        var inputs = new Dictionary<string, string?>
        {
            ["INFISICAL_URL"] = "https://authority.test",
            ["INFISICAL_PROJECT_ID"] = Guid.CreateVersion7().ToString("N"),
            ["INFISICAL_ENV"] = "test"
        };
        foreach (SecretConsumerRole consumer in Enum.GetValues<SecretConsumerRole>())
        {
            string prefix = $"INFISICAL_{consumer.ToString().ToUpperInvariant()}";
            inputs[$"{prefix}_CLIENT_ID"] = Guid.CreateVersion7().ToString("N");
            inputs[$"{prefix}_CLIENT_SECRET"] = CreateSecret();
        }
        return inputs;
    }
}
