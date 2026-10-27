using Explore.ServiceDefaults.Secrets;
using Microsoft.Extensions.Configuration;
using YamlDotNet.RepresentationModel;

namespace Event.Architecture.Tests;

public sealed class SecretAuthorityLeastPrivilegeArchitectureTests
{
    private static readonly string[] FrontendRoots =
    [
        "/runtime/bff/oidc/keycloak", "/runtime/bff/oidc/google", "/runtime/bff/atproto/oauth"
    ];
    private static readonly string[] BackendKeys =
    [
        "AUTHENTICATION_LOCAL_JWT_KEY", "SETUP_SECRET", "INSTANCE_BOOTSTRAP_LOCAL_PASSWORD",
        "CONTROL_PLANE_REGISTRATION_CREDENTIALS", "VAPID_PRIVATE_KEY",
        "DATABASE_RUNTIME_PASSWORD", "DATABASE_MIGRATOR_PASSWORD", "DATABASE_GLOBAL_PASSWORD",
        "DATABASE_INSTANCE_KEY_READ_PASSWORD", "DATABASE_INSTANCE_KEY_WRITE_PASSWORD",
        "DATABASE_INSTANCE_KEY_ISSUANCE_PASSWORD", "DATABASE_PLATFORM_KEY_REPORT_PASSWORD",
        "DATABASE_EMAIL_POLICY_INSTANCE_MUTATION_PASSWORD", "DATABASE_EMAIL_POLICY_TENANT_MUTATION_PASSWORD",
        "ERASURE_DATABASE_RUNTIME_PASSWORD", "IDENTITY_DATABASE_RUNTIME_PASSWORD",
        "STRIPE_PLATFORM_SECRET_KEY", "STRIPE_WEBHOOK_SECRET", "MAIL_SMTP_PASSWORD"
    ];

    [Test]
    public async Task BffAdmissionAcceptsOnlyItsThreeRuntimePurposes()
    {
        await Assert.That(SecretAcquisitionPolicy.Roots(SecretConsumerRole.Bff)).IsEquivalentTo(FrontendRoots);
        var selection = new SecretAuthoritySelection(SecretAuthorityKind.Infisical, "Testing", SecretConsumerRole.Bff);
        foreach (string root in SecretAcquisitionPolicy.Roots(SecretConsumerRole.Api).Concat(new[] { "/keycloak", "/blazor", "/cerbos" }))
            await Assert.That(() => SecretAcquisitionPolicy.AdmitRoots(selection, [root])).Throws<InvalidOperationException>();
        foreach (string root in FrontendRoots)
            await Assert.That(() => SecretAcquisitionPolicy.AdmitRoots(selection, [root])).ThrowsNothing();
    }

    [Test]
    public async Task ApiAdmissionCannotAcquireFrontendCredentials()
    {
        var selection = new SecretAuthoritySelection(SecretAuthorityKind.Infisical, "Testing", SecretConsumerRole.Api);
        foreach (string root in FrontendRoots.Concat(new[] { "/blazor" }))
            await Assert.That(() => SecretAcquisitionPolicy.AdmitRoots(selection, [root])).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task MigrationAdmissionIsLimitedToDatabasePurposes()
    {
        string[] expected = ["/database", "/database/identity", "/database/erasure", "/postgresql"];
        await Assert.That(SecretAcquisitionPolicy.Roots(SecretConsumerRole.Migration)).IsEquivalentTo(expected);
        var selection = new SecretAuthoritySelection(SecretAuthorityKind.Infisical, "Testing", SecretConsumerRole.Migration);
        foreach (string root in SecretAcquisitionPolicy.Roots(SecretConsumerRole.Combined).Except(expected, StringComparer.Ordinal))
            await Assert.That(() => SecretAcquisitionPolicy.AdmitRoots(selection, [root])).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task CombinedAdmissionIsExactlyTheUnionOfApiAndBffPurposes()
    {
        await Assert.That(SecretAcquisitionPolicy.Roots(SecretConsumerRole.Combined)).IsEquivalentTo(
            SecretAcquisitionPolicy.Roots(SecretConsumerRole.Api).Concat(FrontendRoots).Distinct(StringComparer.Ordinal));
    }

    [Test]
    public async Task BffProcessProjectionMasksInheritedAndExplicitBackendCredentials()
    {
        using var inputs = (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(
            BackendKeys.ToDictionary(key => key, _ => (string?)Guid.NewGuid().ToString("N"))).Build();
        var target = BackendKeys.ToDictionary(key => key, _ => (object)Guid.NewGuid().ToString("N"));
        RuntimeSecretEnvironment.Apply(target, inputs, BackendKeys, SecretConsumerRole.Bff,
            SecretAuthorityKind.Environment, BackendKeys.Select(key => new SecretAcquisitionEntry("/database", key, key)));
        foreach (string key in BackendKeys) await Assert.That(target[key]).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task ComposeFrontendEnvironmentCannotInheritBackendSecrets()
    {
        var document = new YamlStream();
        using var reader = File.OpenText(ContextSystemHelpers.RepoPath("docker-compose.yml"));
        document.Load(reader);
        var root = (YamlMappingNode)document.Documents[0].RootNode;
        var services = (YamlMappingNode)root.Children[new YamlScalarNode("services")];
        var frontend = (YamlMappingNode)services.Children[new YamlScalarNode("islamu-event-ui")];
        var environment = (YamlMappingNode)frontend.Children[new YamlScalarNode("environment")];
        var keys = EnvironmentKeys(environment).ToArray();
        await Assert.That(keys.Where(key => BackendKeys.Contains(key, StringComparer.Ordinal)
            || key.StartsWith("Database__", StringComparison.OrdinalIgnoreCase))).IsEmpty();
    }

    [Test]
    [Arguments("islamu-event-api", "API")]
    [Arguments("islamu-event-ui", "BFF")]
    [Arguments("event-migrationservice", "MIGRATION")]
    public async Task ComposeProjectsOnlyTheSelectedConsumersTokenPolicy(string service, string consumer)
    {
        var document = new YamlStream();
        using var reader = File.OpenText(ContextSystemHelpers.RepoPath("docker-compose.yml"));
        document.Load(reader);
        var root = (YamlMappingNode)document.Documents[0].RootNode;
        var services = (YamlMappingNode)root.Children[new YamlScalarNode("services")];
        var resource = (YamlMappingNode)services.Children[new YamlScalarNode(service)];
        var environment = (YamlMappingNode)resource.Children[new YamlScalarNode("environment")];
        foreach ((string field, string suffix, string fallback) in new[]
        {
            ("TokenLifetime", "TOKEN_LIFETIME", ""), ("TokenPeriodSeconds", "TOKEN_PERIOD_SECONDS", ""),
            ("RenewBeforeSeconds", "RENEW_BEFORE_SECONDS", "60"),
            ("RequestTimeoutSeconds", "REQUEST_TIMEOUT_SECONDS", "15")
        })
        {
            var configured = (YamlScalarNode)environment.Children[
                new YamlScalarNode($"SecretProvider__Infisical__{field}")];
            await Assert.That(configured.Value)
                .IsEqualTo($"${{INFISICAL_{consumer}_{suffix}:-{fallback}}}");
        }
    }

    private static IEnumerable<string> EnvironmentKeys(YamlMappingNode mapping)
    {
        foreach (var pair in mapping.Children)
        {
            string key = ((YamlScalarNode)pair.Key).Value!;
            if (key != "<<") { yield return key; continue; }
            IEnumerable<YamlNode> merged = pair.Value is YamlSequenceNode sequence ? sequence.Children : [pair.Value];
            foreach (YamlMappingNode source in merged.Cast<YamlMappingNode>())
                foreach (string inherited in EnvironmentKeys(source)) yield return inherited;
        }
    }
}
