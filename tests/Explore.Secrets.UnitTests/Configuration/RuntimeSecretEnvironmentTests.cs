using Explore.Secrets.Abstractions;
using Explore.Secrets.Configuration;
using Microsoft.Extensions.Configuration;

namespace Explore.Secrets.UnitTests.Configuration;

public sealed class RuntimeSecretEnvironmentTests
{
    [Test]
    [Arguments(SecretConsumer.Api)]
    [Arguments(SecretConsumer.Bff)]
    [Arguments(SecretConsumer.Combined)]
    [Arguments(SecretConsumer.Migration)]
    public async Task ProcessProjectionKeepsOnlyItsIdentityAndNoProviderAdministration(SecretConsumer consumer)
    {
        var inputs = Inputs();
        var target = new Dictionary<string, object>
        {
            ["KEYCLOAK_ADMIN_PASSWORD"] = SecretsTestValues.CreateSecret(),
            ["CERBOS_ADMIN_PASSWORD_HASH"] = SecretsTestValues.CreateSecret(),
            ["Cerbos__AdminApi__Endpoints__0"] = "http://cerbos.test",
            ["ISLAMU_ASPIRE_MODE"] = "FullLocal",
            ["Storage__Local__RootPath"] = "/data"
        };
        string inheritedCanary = SecretsTestValues.CreateSecret();
        inputs["INFISICAL_CLIENT_SECRET"] = inheritedCanary;
        inputs["UNDECLARED_SECRET"] = inheritedCanary;
        RuntimeSecretEnvironment.Apply(target, Configuration(inputs), inputs.Keys, consumer, SecretProviderType.Infisical);

        await Assert.That(target["SecretProvider__Provider"]).IsEqualTo("Infisical");
        await Assert.That(target["SecretProvider__Infisical__ClientSecret"])
            .IsEqualTo(inputs[$"INFISICAL_{consumer.ToString().ToUpperInvariant()}_CLIENT_SECRET"]!);
        await Assert.That(target["KEYCLOAK_ADMIN_PASSWORD"]).IsEqualTo(string.Empty);
        await Assert.That(target["CERBOS_ADMIN_PASSWORD_HASH"]).IsEqualTo(string.Empty);
        await Assert.That(target["UNDECLARED_SECRET"]).IsEqualTo(string.Empty);
        await Assert.That(target.Values.Contains(inheritedCanary)).IsFalse();
        await Assert.That(target["Storage__Local__RootPath"]).IsEqualTo("/data");
        await Assert.That(target["Cerbos__AdminApi__Endpoints__0"]).IsEqualTo("http://cerbos.test");
        foreach (SecretConsumer sibling in Enum.GetValues<SecretConsumer>().Where(value => value != consumer))
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
            SecretConsumer.Api, SecretProviderType.Infisical);
        await Assert.That(target["KEYCLOAK__AUDIENCE"]).IsEqualTo("event-resource");
        await Assert.That(target["KEYCLOAK__PERMITTEDCLIENTS__0"]).IsEqualTo("event-browser");
    }

    [Test]
    public async Task MissingPurposeIdentityCannotFallBackToSharedBootstrap()
    {
        var inputs = Inputs();
        inputs.Remove("INFISICAL_API_CLIENT_SECRET");
        inputs["INFISICAL_CLIENT_SECRET"] = SecretsTestValues.CreateSecret();
        await Assert.That(() => RuntimeSecretEnvironment.Apply(
            new Dictionary<string, object>(), Configuration(inputs), inputs.Keys,
            SecretConsumer.Api, SecretProviderType.Infisical)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ReusedIdentityIsRejectedBeforeProjectLaunch()
    {
        var inputs = Inputs();
        inputs["INFISICAL_BFF_CLIENT_ID"] = inputs["INFISICAL_API_CLIENT_ID"];
        await Assert.That(() => RuntimeSecretEnvironment.Apply(
            new Dictionary<string, object>(), Configuration(inputs), inputs.Keys,
            SecretConsumer.Api, SecretProviderType.Infisical)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task EnvironmentProjectionSeparatesFrontendAndLifecycleCanaries()
    {
        var inputs = Inputs();
        inputs["KEYCLOAK_BLAZOR_CLIENT_SECRET"] = SecretsTestValues.CreateSecret();
        inputs["KEYCLOAK_LIFECYCLE_CLIENT_SECRET"] = SecretsTestValues.CreateSecret();
        inputs["MAIL_SMTP_PASSWORD"] = SecretsTestValues.CreateSecret();
        var api = new Dictionary<string, object>();
        var bff = new Dictionary<string, object>();
        var combined = new Dictionary<string, object>();
        RuntimeSecretEnvironment.Apply(api, Configuration(inputs), inputs.Keys, SecretConsumer.Api, SecretProviderType.Environment);
        RuntimeSecretEnvironment.Apply(bff, Configuration(inputs), inputs.Keys, SecretConsumer.Bff, SecretProviderType.Environment);
        RuntimeSecretEnvironment.Apply(combined, Configuration(inputs), inputs.Keys, SecretConsumer.Combined, SecretProviderType.Environment);
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
    [Arguments(SecretConsumer.Api, SecretProviderType.Environment)]
    [Arguments(SecretConsumer.Combined, SecretProviderType.Environment)]
    [Arguments(SecretConsumer.Bff, SecretProviderType.Environment)]
    [Arguments(SecretConsumer.Migration, SecretProviderType.Environment)]
    [Arguments(SecretConsumer.Api, SecretProviderType.Infisical)]
    [Arguments(SecretConsumer.Combined, SecretProviderType.Infisical)]
    [Arguments(SecretConsumer.Bff, SecretProviderType.Infisical)]
    [Arguments(SecretConsumer.Migration, SecretProviderType.Infisical)]
    public async Task LocalWriterPasswordsReachOnlyTheirSelectedBackendAuthority(
        SecretConsumer consumer, SecretProviderType provider)
    {
        var inputs = Inputs();
        string applicationWriter = SecretsTestValues.CreateSecret();
        string identityWriter = SecretsTestValues.CreateSecret();
        inputs["DATABASE_LOCAL_IDENTITY_WRITER_PASSWORD"] = applicationWriter;
        inputs["IDENTITY_DATABASE_LOCAL_IDENTITY_WRITER_PASSWORD"] = identityWriter;
        var target = new Dictionary<string, object>
        {
            ["Database__LocalIdentityWriter__Password"] = applicationWriter,
            ["IdentityDatabase__LocalIdentityWriter__Password"] = identityWriter
        };

        RuntimeSecretEnvironment.Apply(target, Configuration(inputs), inputs.Keys, consumer, provider);

        bool delivers = provider == SecretProviderType.Environment
            && consumer is SecretConsumer.Api or SecretConsumer.Combined;
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
    [Arguments(SecretConsumer.Api)]
    [Arguments(SecretConsumer.Combined)]
    public async Task MissingLocalWriterPasswordNeverBorrowsRuntimeGlobalOrMigrator(SecretConsumer consumer)
    {
        var inputs = Inputs();
        foreach (string name in new[]
        {
            "DATABASE_RUNTIME_PASSWORD", "DATABASE_GLOBAL_PASSWORD", "DATABASE_MIGRATOR_PASSWORD",
            "IDENTITY_DATABASE_RUNTIME_PASSWORD", "IDENTITY_DATABASE_MIGRATOR_PASSWORD"
        })
            inputs[name] = SecretsTestValues.CreateSecret();
        var inherited = inputs.Keys.Concat(new[]
        {
            "DATABASE_LOCAL_IDENTITY_WRITER_PASSWORD", "IDENTITY_DATABASE_LOCAL_IDENTITY_WRITER_PASSWORD",
            "Database__LocalIdentityWriter__Password", "IdentityDatabase__LocalIdentityWriter__Password"
        }).ToArray();
        var target = new Dictionary<string, object>();

        RuntimeSecretEnvironment.Apply(target, Configuration(inputs), inherited, consumer, SecretProviderType.Environment);

        foreach (string name in inherited.Where(name => name.Contains("LOCAL_IDENTITY_WRITER", StringComparison.Ordinal)
            || name.Contains("LocalIdentityWriter", StringComparison.Ordinal)))
            await Assert.That(Equals(target[name], string.Empty)).IsTrue();
    }

    [Test]
    [Arguments(SecretConsumer.Api, SecretProviderType.Environment)]
    [Arguments(SecretConsumer.Combined, SecretProviderType.Environment)]
    [Arguments(SecretConsumer.Migration, SecretProviderType.Environment)]
    [Arguments(SecretConsumer.Bff, SecretProviderType.Environment)]
    [Arguments(SecretConsumer.Api, SecretProviderType.Infisical)]
    [Arguments(SecretConsumer.Combined, SecretProviderType.Infisical)]
    [Arguments(SecretConsumer.Migration, SecretProviderType.Infisical)]
    [Arguments(SecretConsumer.Bff, SecretProviderType.Infisical)]
    public async Task LocalWriterEnrollmentProjectsDeclaredLoginNamesWithoutSiblingPasswords(
        SecretConsumer consumer, SecretProviderType provider)
    {
        var inputs = Inputs();
        string[] names =
        [
            "DATABASE_RUNTIME_USERNAME", "DATABASE_GLOBAL_USERNAME", "DATABASE_MIGRATOR_USERNAME",
            "DATABASE_INSTANCE_KEY_READ_USERNAME", "DATABASE_INSTANCE_KEY_WRITE_USERNAME",
            "DATABASE_PLATFORM_KEY_REPORT_USERNAME",
            "DATABASE_EMAIL_POLICY_INSTANCE_MUTATION_USERNAME", "DATABASE_EMAIL_POLICY_TENANT_MUTATION_USERNAME",
            "DATABASE_LOCAL_IDENTITY_WRITER_USERNAME", "IDENTITY_DATABASE_RUNTIME_USERNAME",
            "IDENTITY_DATABASE_MIGRATOR_USERNAME", "IDENTITY_DATABASE_LOCAL_IDENTITY_WRITER_USERNAME"
        ];
        foreach (string name in names)
            inputs[name] = "declared_" + Guid.CreateVersion7().ToString("N");
        inputs["DATABASE_LOCAL_IDENTITY_WRITER_PASSWORD"] = SecretsTestValues.CreateSecret();
        inputs["IDENTITY_DATABASE_LOCAL_IDENTITY_WRITER_PASSWORD"] = SecretsTestValues.CreateSecret();
        var target = new Dictionary<string, object>();

        RuntimeSecretEnvironment.Apply(target, Configuration(inputs), inputs.Keys, consumer, provider);

        bool projectsNames = provider == SecretProviderType.Environment && consumer != SecretConsumer.Bff;
        foreach (string name in names)
            await Assert.That(Equals(target[name], projectsNames ? inputs[name] : string.Empty)).IsTrue();
        if (consumer is SecretConsumer.Migration or SecretConsumer.Bff)
        {
            await Assert.That(Equals(target["DATABASE_LOCAL_IDENTITY_WRITER_PASSWORD"], string.Empty)).IsTrue();
            await Assert.That(Equals(target["IDENTITY_DATABASE_LOCAL_IDENTITY_WRITER_PASSWORD"], string.Empty)).IsTrue();
        }

        (string Canonical, string Alias, string Value)[] enrollment =
        [
            ("Database:InstanceKeyRead:Username", "DATABASE_INSTANCE_KEY_READ_USERNAME",
                "reader_" + Guid.CreateVersion7().ToString("N")),
            ("Database:InstanceKeyWrite:Username", "DATABASE_INSTANCE_KEY_WRITE_USERNAME",
                "writer_" + Guid.CreateVersion7().ToString("N")),
            ("Database:PlatformKeyReport:Username", "DATABASE_PLATFORM_KEY_REPORT_USERNAME",
                "reporter_" + Guid.CreateVersion7().ToString("N"))
        ];
        foreach (string form in new[] { "only", "both", "empty" })
        {
            var canonicalInputs = Inputs();
            var discarded = new List<string>();
            foreach (var coordinate in enrollment)
            {
                canonicalInputs[coordinate.Canonical] = form == "empty" ? string.Empty : coordinate.Value;
                if (form != "only")
                {
                    string shadowed = "shadowed_" + Guid.CreateVersion7().ToString("N");
                    canonicalInputs[coordinate.Alias] = shadowed;
                    discarded.Add(shadowed);
                }
            }
            string[] inherited = canonicalInputs.Keys
                .Select(name => name.Replace(":", "__", StringComparison.Ordinal))
                .Concat(enrollment.Select(coordinate => coordinate.Alias)).ToArray();
            var canonicalTarget = new Dictionary<string, object>();

            RuntimeSecretEnvironment.Apply(canonicalTarget, Configuration(canonicalInputs), inherited, consumer, provider);

            foreach (var coordinate in enrollment)
            {
                string projectedName = coordinate.Canonical.Replace(":", "__", StringComparison.Ordinal);
                string expected = projectsNames && form != "empty" ? coordinate.Value : string.Empty;
                await Assert.That(Equals(canonicalTarget[projectedName], expected)).IsTrue();
                await Assert.That(Equals(canonicalTarget[coordinate.Alias], string.Empty)).IsTrue();
            }
            foreach (string shadowed in discarded)
                await Assert.That(canonicalTarget.ContainsValue(shadowed)).IsFalse();
        }

        foreach (var coordinate in enrollment)
        {
            var explicitInputs = Inputs();
            explicitInputs[coordinate.Alias] = "ambient_" + Guid.CreateVersion7().ToString("N");
            string projectedName = coordinate.Canonical.Replace(":", "__", StringComparison.Ordinal);
            string selected = "selected_" + Guid.CreateVersion7().ToString("N");
            var explicitTarget = new Dictionary<string, object> { [projectedName] = selected };

            RuntimeSecretEnvironment.Apply(explicitTarget, Configuration(explicitInputs), explicitInputs.Keys, consumer, provider);

            await Assert.That(Equals(explicitTarget[projectedName], projectsNames ? selected : string.Empty)).IsTrue();

            explicitInputs[coordinate.Canonical] = string.Empty;
            RuntimeSecretEnvironment.Apply(explicitTarget, Configuration(explicitInputs), explicitInputs.Keys, consumer, provider);

            await Assert.That(Equals(explicitTarget[projectedName], string.Empty)).IsTrue();
            await Assert.That(Equals(explicitTarget[coordinate.Alias], string.Empty)).IsTrue();
        }
    }

    [Test]
    [Arguments(SecretConsumer.Api, SecretProviderType.Environment, "only")]
    [Arguments(SecretConsumer.Combined, SecretProviderType.Environment, "only")]
    [Arguments(SecretConsumer.Api, SecretProviderType.Environment, "both")]
    [Arguments(SecretConsumer.Combined, SecretProviderType.Environment, "both")]
    [Arguments(SecretConsumer.Api, SecretProviderType.Environment, "empty")]
    [Arguments(SecretConsumer.Combined, SecretProviderType.Environment, "empty")]
    [Arguments(SecretConsumer.Api, SecretProviderType.Infisical, "only")]
    [Arguments(SecretConsumer.Combined, SecretProviderType.Infisical, "only")]
    [Arguments(SecretConsumer.Api, SecretProviderType.Infisical, "both")]
    [Arguments(SecretConsumer.Combined, SecretProviderType.Infisical, "both")]
    [Arguments(SecretConsumer.Api, SecretProviderType.Infisical, "empty")]
    [Arguments(SecretConsumer.Combined, SecretProviderType.Infisical, "empty")]
    public async Task StructuredLocalWriterAuthorityMasksFlatAliasesWithoutLeakingDiscardedValues(
        SecretConsumer consumer, SecretProviderType provider, string form)
    {
        var inputs = Inputs();
        (string Canonical, string Alias, string Value)[] coordinates =
        [
            ("Database:LocalIdentityWriter:Username", "DATABASE_LOCAL_IDENTITY_WRITER_USERNAME",
                "writer_" + Guid.CreateVersion7().ToString("N")),
            ("Database:LocalIdentityWriter:Password", "DATABASE_LOCAL_IDENTITY_WRITER_PASSWORD",
                SecretsTestValues.CreateSecret()),
            ("IdentityDatabase:LocalIdentityWriter:Username", "IDENTITY_DATABASE_LOCAL_IDENTITY_WRITER_USERNAME",
                "writer_" + Guid.CreateVersion7().ToString("N")),
            ("IdentityDatabase:LocalIdentityWriter:Password", "IDENTITY_DATABASE_LOCAL_IDENTITY_WRITER_PASSWORD",
                SecretsTestValues.CreateSecret())
        ];
        var discarded = new List<string>();
        foreach (var coordinate in coordinates)
        {
            inputs[coordinate.Canonical] = form == "empty" ? string.Empty : coordinate.Value;
            if (form != "only")
            {
                string shadowed = SecretsTestValues.CreateSecret();
                inputs[coordinate.Alias] = shadowed;
                discarded.Add(shadowed);
            }
        }
        string[] inherited = inputs.Keys.Select(name => name.Replace(":", "__", StringComparison.Ordinal)).ToArray();
        var target = new Dictionary<string, object>();

        RuntimeSecretEnvironment.Apply(target, Configuration(inputs), inherited, consumer, provider);

        foreach (var coordinate in coordinates)
        {
            string projectedName = coordinate.Canonical.Replace(":", "__", StringComparison.Ordinal);
            string expected = provider == SecretProviderType.Environment && form != "empty"
                ? coordinate.Value : string.Empty;
            await Assert.That(Equals(target[projectedName], expected)).IsTrue();
        }
        foreach (string shadowed in discarded)
            await Assert.That(target.Values.Contains(shadowed)).IsFalse();
    }

    [Test]
    [Arguments(SecretConsumer.Api, SecretProviderType.Environment)]
    [Arguments(SecretConsumer.Combined, SecretProviderType.Environment)]
    [Arguments(SecretConsumer.Api, SecretProviderType.Infisical)]
    [Arguments(SecretConsumer.Combined, SecretProviderType.Infisical)]
    public async Task ExternalServerVersionReachesOnlyTheSelectedEnvironmentAuthority(
        SecretConsumer consumer, SecretProviderType provider)
    {
        var inputs = Inputs();
        const string explicitVersion = "10.11.8";
        inputs["IDENTITY_DATABASE_SERVER_VERSION"] = explicitVersion;
        var target = new Dictionary<string, object>();

        RuntimeSecretEnvironment.Apply(target, Configuration(inputs), inputs.Keys, consumer, provider);

        await Assert.That(Equals(target["IDENTITY_DATABASE_SERVER_VERSION"],
            provider == SecretProviderType.Environment ? explicitVersion : string.Empty)).IsTrue();
    }

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
        foreach (string consumer in new[] { "API", "BFF", "COMBINED", "MIGRATION" })
        {
            inputs[$"INFISICAL_{consumer}_CLIENT_ID"] = Guid.CreateVersion7().ToString("N");
            inputs[$"INFISICAL_{consumer}_CLIENT_SECRET"] = SecretsTestValues.CreateSecret();
        }
        return inputs;
    }
}
