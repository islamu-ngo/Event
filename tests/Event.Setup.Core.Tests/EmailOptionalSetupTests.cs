using System.Text;
using ISLAMU.Event.Setup.Core.Environment;
using ISLAMU.Setup.Core.EnvironmentTests;
using YamlDotNet.RepresentationModel;

namespace Event.Setup.Core.Tests;

public sealed class EmailOptionalSetupTests
{
    [Test]
    public async Task ComposeMailCaptureIsOptionalPrivateBoundedAndPinned()
    {
        YamlMappingNode root = ReadCompose();
        var services = (YamlMappingNode)root["services"];
        var mailpit = (YamlMappingNode)services["mailpit"];
        var api = (YamlMappingNode)services["islamu-event-api"];

        await Assert.That(((YamlMappingNode)api["depends_on"]).Children.ContainsKey("mailpit")).IsFalse();
        await Assert.That(((YamlSequenceNode)mailpit["profiles"]).Children.Select(Scalar))
            .IsEquivalentTo(["mail"]);
        await Assert.That(Scalar(mailpit["image"])).IsEqualTo(
            "axllent/mailpit:v1.30.0@sha256:0059ef81e492a7192af3816281eed6859eb078bd7bdc58b76757c13e10e53a7d");
        await Assert.That(((YamlSequenceNode)mailpit["ports"]).Children.Select(Scalar))
            .IsEquivalentTo(["127.0.0.1:${MAILPIT_UI_PORT:-8025}:8025"]);
        var environment = (YamlMappingNode)mailpit["environment"];
        await Assert.That(environment.Children.Keys.Select(Scalar)).IsEquivalentTo(
            ["MP_MAX_MESSAGES", "MP_DATABASE", "MP_DISABLE_VERSION_CHECK"]);
        await Assert.That(Scalar(environment["MP_MAX_MESSAGES"])).IsEqualTo("500");
        await Assert.That(Scalar(environment["MP_DISABLE_VERSION_CHECK"])).IsEqualTo("true");
        await Assert.That(Scalar(environment["MP_DATABASE"])).IsEqualTo("/data/mailpit.db");
    }

    [Test]
    public async Task ComposeDoesNotBootstrapSmtpOrRequireAnAbsentBroker()
    {
        YamlMappingNode root = ReadCompose();
        var smtp = (YamlMappingNode)root["x-smtp-env"];
        foreach (KeyValuePair<YamlNode, YamlNode> entry in smtp.Children)
            await Assert.That(Scalar(entry.Value)).IsEqualTo("${" + Scalar(entry.Key) + ":-}");
        var rabbit = (YamlMappingNode)root["x-email-dispatch-rabbitmq-env"];
        await Assert.That(Scalar(rabbit["EmailDispatchRabbitMq__Enabled"]))
            .IsEqualTo("${EMAIL_DISPATCH_RABBITMQ_ENABLED:-false}");
        await Assert.That(smtp.Children.Keys.Select(Scalar).Any(key =>
            key.Contains("DELIVERY", StringComparison.OrdinalIgnoreCase))).IsFalse();
    }

    [Test]
    public async Task CuratedBaselineHasNoMailBootstrapOrDeliveryAuthority()
    {
        MachineEnvironmentFile parsed = EnvironmentMachineConfiguration.ParseEnvironmentTemplate(File.ReadAllText(Path.Combine(
            EnvironmentMachineConfiguration.RepositoryRoot(), ".env.example")));
        MachineEnvironmentEntry[] entries = parsed.Entries.ToArray();
        await Assert.That(entries.Any(entry => entry.Key.StartsWith("MAIL_", StringComparison.Ordinal)
            || entry.Key.StartsWith("MAILPIT_", StringComparison.Ordinal))).IsFalse();
        await Assert.That(entries.Any(entry => entry.Key == "EMAIL_DISPATCH_RABBITMQ_ENABLED"))
            .IsTrue();
        await Assert.That(entries.Length).IsLessThan(CanonicalEnvironmentCatalogue.DotenvEnvironmentKeys.Count);
        await Assert.That(entries.Any(entry => entry.Key.Contains("DELIVERY_ENABLED", StringComparison.OrdinalIgnoreCase)))
            .IsFalse();
    }

    [Test]
    [Arguments("standalone")]
    [Arguments("split")]
    public async Task ZeroEmailProjectionNeedsNoMailInputs(string topology)
    {
        var context = new EnvironmentActivationContext(topology, ["messaging"], ["local"]);
        DotenvCompositionResult result = DotenvComposer.ComposeNoSecrets(
            CanonicalEnvironmentCatalogue.Catalogue, context, []);
        await Assert.That(result.Diagnostics).IsEmpty();
        await Assert.That(result.Readiness.State).IsEqualTo(DotenvReadinessState.Ready);
        await Assert.That(result.Document.Entries).IsEmpty();
        await Assert.That(CanonicalEnvironmentCatalogue.Catalogue.Lookup("EMAIL_DISPATCH_RABBITMQ_ENABLED")!.SafeDefault)
            .IsEqualTo("false");
    }

    [Test]
    public async Task CatalogueRetainsSupportedSmtpButRemovesObsoleteCaptureOverrides()
    {
        EnvironmentCatalogue catalogue = CanonicalEnvironmentCatalogue.Catalogue;
        foreach (string key in new[] { "MAILPIT_TAG", "MAILPIT_SMTP_PORT", "MAILPIT_MAX_MESSAGES" })
            await Assert.That(catalogue.Lookup(key)).IsNull();
        foreach (string key in new[] { "MAIL_SMTP_HOST", "MAIL_SMTP_PORT", "MAIL_SMTP_PASSWORD", "MAIL_SMTP_ENCRYPTION" })
        {
            await Assert.That(catalogue.Lookup(key)).IsNotNull();
            await Assert.That(catalogue.Lookup(key)!.Requirement).IsEqualTo(EnvironmentVariableRequirement.Optional);
        }
        EnvironmentVariableDefinition inbox = catalogue.Lookup("MAILPIT_UI_PORT")!;
        await Assert.That(inbox.ValidatorId).IsEqualTo("port-number");
        await Assert.That(inbox.SafeDefault).IsEqualTo("8025");
        await Assert.That(inbox.Generation.Surfaces.HasFlag(EnvironmentGenerationSurface.Startup)).IsFalse();
        await Assert.That(catalogue.Definitions.Any(item =>
            item.Key.Contains("DELIVERY_ENABLED", StringComparison.OrdinalIgnoreCase))).IsFalse();
        MachineComposeFile compose = EnvironmentMachineConfiguration.ParseCompose(File.ReadAllText(Path.Combine(
            EnvironmentMachineConfiguration.RepositoryRoot(), "docker-compose.yml")));
        await Assert.That(compose.Keys.SequenceEqual(CanonicalEnvironmentCatalogue.ComposeEnvironmentKeys,
            StringComparer.Ordinal)).IsTrue();
    }

    [Test]
    [Arguments("MAILPIT_UI_PORT", "0")]
    [Arguments("MAILPIT_UI_PORT", "65536")]
    [Arguments("MAILPIT_UI_PORT", "-1")]
    [Arguments("MAILPIT_UI_PORT", "1.0")]
    [Arguments("MAILPIT_UI_PORT", " 8025")]
    [Arguments("MAILPIT_UI_PORT", "0.0.0.0:8025")]
    [Arguments("MAILPIT_UI_PORT", "8025:8025")]
    [Arguments("MAILPIT_UI_PORT", "2147483648")]
    [Arguments("MAILPIT_UI_PORT", "mail-input-canary")]
    [Arguments("EMAIL_DISPATCH_RABBITMQ_ENABLED", "1")]
    [Arguments("EMAIL_DISPATCH_RABBITMQ_ENABLED", "enabled")]
    [Arguments("EMAIL_DISPATCH_RABBITMQ_ENABLED", " true")]
    public async Task InvalidOptionalMailInputIsOmittedWithValueFreeDiagnostics(string key, string value)
    {
        DotenvCompositionResult result = Compose(key, value);
        await Assert.That(result.Diagnostics.Select(item => item.Code)).Contains("dotenv-input-value-invalid");
        await Assert.That(result.Document.Entries.Any(item => item.Key == key)).IsFalse();
        await Assert.That(result.Diagnostics.All(item => item.Key == key)).IsTrue();
        string rendered = Encoding.UTF8.GetString(DotenvCodec.Render(result.Document, false).Bytes.Span);
        await Assert.That(rendered.Contains(value, StringComparison.Ordinal)).IsFalse();
        if (value == "mail-input-canary")
        {
            string diagnosticJson = System.Text.Json.JsonSerializer.Serialize(result.Diagnostics);
            await Assert.That(diagnosticJson.Contains(value, StringComparison.Ordinal)).IsFalse();
            await Assert.That(result.ToString().Contains(value, StringComparison.Ordinal)).IsFalse();
        }
    }

    [Test]
    [Arguments("MAILPIT_UI_PORT", "1")]
    [Arguments("MAILPIT_UI_PORT", "65535")]
    [Arguments("MAILPIT_UI_PORT", "18025")]
    [Arguments("EMAIL_DISPATCH_RABBITMQ_ENABLED", "true")]
    public async Task ValidOptionalMailInputSurvivesDotenvRoundTrip(string key, string value)
    {
        DotenvCompositionResult result = Compose(key, value);
        await Assert.That(result.Diagnostics).IsEmpty();
        DotenvRenderResult rendered = DotenvCodec.Render(result.Document, false);
        DotenvParseResult parsed = DotenvCodec.Parse(rendered.Bytes);
        await Assert.That(parsed.Diagnostics).IsEmpty();
        await Assert.That(parsed.Document!.Entries.Single(item => item.Key == key).Value).IsEqualTo(value);
    }

    private static DotenvCompositionResult Compose(string key, string value) =>
        DotenvComposer.ComposeNoSecrets(CanonicalEnvironmentCatalogue.Catalogue,
            new EnvironmentActivationContext("split", ["integration", "messaging"], ["local"]),
            [new DotenvEntry(key, value, DotenvEntryKind.LocalHumanValue, false, DotenvProvenance.UserInput)]);

    private static YamlMappingNode ReadCompose()
    {
        using var reader = File.OpenText(Path.Combine(EnvironmentMachineConfiguration.RepositoryRoot(), "docker-compose.yml"));
        var stream = new YamlStream();
        stream.Load(reader);
        return (YamlMappingNode)stream.Documents.Single().RootNode;
    }

    private static string Scalar(YamlNode node) => ((YamlScalarNode)node).Value!;
}
