namespace ISLAMU.Event.DefinitionExporter;

using System.Text.Json;
using System.Text.Json.Nodes;
using ISLAMU.Event.DefinitionExporter.Environment;

internal static class EnvironmentMetadata
{
    internal static byte[] Export()
    {
        EnvironmentCatalogue catalogue = PlatformEnvironmentCatalogue.Catalogue;
        JsonArray definitions = [];
        foreach (EnvironmentVariableDefinition item in catalogue.Definitions.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            var generation = new JsonObject
            {
                ["surfaces"] = (int)item.Generation.Surfaces,
                ["composeRequired"] = item.Generation.ComposeRequired
            };
            if (item.Generation.DotenvOrder is { } dotenvOrder)
                generation["dotenvOrder"] = dotenvOrder;
            if (item.Generation.ComposeOrder is { } composeOrder)
                generation["composeOrder"] = composeOrder;
            var definition = new JsonObject
            {
                ["key"] = item.Key, ["category"] = Name(item.Category),
                ["sensitivity"] = Name(item.Sensitivity), ["requirement"] = Name(item.Requirement),
                ["order"] = item.Order, ["hasSafeDefault"] = item.SafeDefault is not null,
                ["validatorId"] = item.ValidatorId, ["restartBehavior"] = Name(item.RestartBehavior),
                ["generation"] = generation,
                ["documentation"] = new JsonObject
                {
                    ["localizationKey"] = item.Documentation.LocalizationKey,
                    ["helpKey"] = item.Documentation.HelpKey, ["anchor"] = item.Documentation.Anchor
                },
                ["activation"] = Activation(item.Activation)
            };
            if (item.Sensitivity == EnvironmentVariableSensitivity.Public && item.SafeDefault is not null)
                definition["safeDefault"] = item.SafeDefault;
            definitions.Add(definition);
        }
        return CanonicalJson.Bytes(new JsonObject
        {
            ["schemaVersion"] = 1, ["generatedBy"] = "eng/configurator/DefinitionExporter",
            ["definitions"] = definitions,
            ["secretBindingEnvironmentKeys"] = JsonSerializer.SerializeToNode(PlatformEnvironmentCatalogue.SecretBindingEnvironmentKeys),
            ["dotenvEnvironmentKeys"] = JsonSerializer.SerializeToNode(PlatformEnvironmentCatalogue.DotenvEnvironmentKeys),
            ["composeEnvironmentKeys"] = JsonSerializer.SerializeToNode(PlatformEnvironmentCatalogue.ComposeEnvironmentKeys),
            ["startupEnvironmentKeys"] = JsonSerializer.SerializeToNode(catalogue.Definitions
                .Where(item => item.Generation.Surfaces.HasFlag(EnvironmentGenerationSurface.Startup))
                .OrderBy(item => item.Order).Select(item => item.Key))
        });
    }

    private static JsonObject Activation(EnvironmentActivationExpression expression)
    {
        JsonArray operands = [];
        foreach (EnvironmentActivationExpression child in expression.Operands)
            operands.Add(Activation(child));
        var result = new JsonObject { ["kind"] = Name(expression.Kind), ["operands"] = operands };
        if (expression.Identifier is not null)
            result["identifier"] = expression.Identifier;
        return result;
    }

    private static string Name<T>(T value) where T : Enum => value.ToString().ToLowerInvariant();
}
