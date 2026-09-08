using System.Text.Json;
using ISLAMU.Event.Setup.Core.Environment;

namespace ISLAMU.Event.SetupAssistant.Cli;

internal static class SetupCliCatalogueEnvironmentHandlers
{
    internal static SetupCliCommandResult Catalogue(SetupCliCommand command, SetupCliInvocation invocation)
    {
        EnvironmentVariableDefinition[] definitions = command.Operation == "list"
            ? CanonicalEnvironmentCatalogue.Catalogue.Definitions.OrderBy(item => item.Key, StringComparer.Ordinal).ToArray()
            : CanonicalEnvironmentCatalogue.Catalogue.Lookup(command.Key!) is { } definition ? [definition] : [];
        if (definitions.Length == 0) return SetupCliResults.Failure(SetupCliExitCode.Validation, "catalogue-key-unknown", "$.key");
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            if (command.Operation == "list") writer.WriteStartArray();
            WriteDefinitions(writer, definitions);
            if (command.Operation == "list") writer.WriteEndArray();
        }
        byte[] bytes = stream.ToArray();
        Write(invocation, command, bytes);
        return SetupCliResults.Success([SetupCliResults.Artifact("catalogue", "application/json", bytes, "public",
            SetupCliResults.PathIntent(command.Output), command.DryRun ? "planned" : "written")]);
    }

    internal static SetupCliCommandResult Environment(SetupCliCommand command, SetupCliInvocation invocation)
    {
        if (command.Operation == "validate")
        {
            byte[] input = SetupCliIoOperations.Read(invocation, command.Input!);
            DotenvParseResult parsed = DotenvCodec.Parse(input);
            return parsed.Succeeded
                ? SetupCliResults.Success([SetupCliResults.Artifact("dotenv-template", "text/plain", input, "sensitive", "input", "none")])
                : SetupCliResults.EnvironmentFailure(parsed.Diagnostics, SetupCliExitCode.Data);
        }

        EnvironmentCatalogue catalogue = CanonicalEnvironmentCatalogue.Catalogue;
        string topology = command.Topology ?? "standalone";
        string[] capabilities = command.Capabilities.Count == 0 ? ["database", "platform", "storage"] : command.Capabilities.ToArray();
        string[] providers = command.Providers.Count == 0 ? ["environment", "local", "sqlite"] : command.Providers.ToArray();
        if (!catalogue.Topologies.Contains(topology, StringComparer.Ordinal)
            || capabilities.Any(value => !catalogue.Capabilities.Contains(value, StringComparer.Ordinal))
            || providers.Any(value => !catalogue.Providers.Contains(value, StringComparer.Ordinal)))
            return SetupCliResults.Failure(SetupCliExitCode.Usage, "activation-identifier-unknown");

        var context = new EnvironmentActivationContext(topology, capabilities, providers);
        DotenvCompositionResult composition = DotenvComposer.ComposeNoSecrets(catalogue, context, []);
        DotenvRenderResult rendered = DotenvCodec.Render(composition.Document, true);
        if (!rendered.Succeeded) return SetupCliResults.EnvironmentFailure(rendered.Diagnostics, SetupCliExitCode.Validation);
        Write(invocation, command, rendered.Bytes);
        SetupCliMachineReadiness readiness = new(SetupCliResults.Lower(composition.Readiness.State),
            SetupCliResults.NormalizeKeys(composition.Readiness.Missing), SetupCliResults.NormalizeKeys(composition.Readiness.Blocked));
        SetupCliMachineCoverage coverage = new([], SetupCliResults.NormalizeKeys(composition.Readiness.Missing));
        return SetupCliResults.Success([SetupCliResults.Artifact("dotenv-template", "text/plain", rendered.Bytes.Span,
            "sensitive", SetupCliResults.PathIntent(command.Output), command.DryRun ? "planned" : "written", coverage, readiness)],
            coverage, readiness);
    }

    private static void WriteDefinitions(Utf8JsonWriter writer, IEnumerable<EnvironmentVariableDefinition> definitions)
    {
        foreach (EnvironmentVariableDefinition item in definitions)
        {
            writer.WriteStartObject();
            writer.WriteString("key", item.Key);
            writer.WriteString("category", SetupCliResults.Lower(item.Category));
            writer.WriteString("sensitivity", SetupCliResults.Lower(item.Sensitivity));
            writer.WriteString("requirement", SetupCliResults.Lower(item.Requirement));
            writer.WriteString("restart", SetupCliResults.Lower(item.RestartBehavior));
            writer.WriteNumber("generation", (int)item.Generation.Surfaces);
            writer.WriteString("activation", SetupCliResults.Lower(item.Activation.Kind));
            writer.WriteEndObject();
        }
    }

    private static void Write(SetupCliInvocation invocation, SetupCliCommand command, ReadOnlyMemory<byte> bytes)
    {
        if (!command.DryRun) invocation.Io.Output.Write(command.Output!, bytes, invocation.Io.MaximumBytes);
    }
}
