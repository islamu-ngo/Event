namespace ISLAMU.Event.Setup.Core.Environment;

public sealed class DotenvCompositionResult
{
    private readonly EnvironmentDiagnostic[] _diagnostics;

    internal DotenvCompositionResult(
        DotenvDocument document,
        DotenvReadinessResult readiness,
        IEnumerable<EnvironmentDiagnostic> diagnostics)
    {
        Document = document;
        Readiness = readiness;
        _diagnostics = diagnostics.ToArray();
    }

    public DotenvDocument Document { get; }
    public DotenvReadinessResult Readiness { get; }
    public IReadOnlyList<EnvironmentDiagnostic> Diagnostics =>
        Array.AsReadOnly((EnvironmentDiagnostic[])_diagnostics.Clone());
    public override string ToString() =>
        $"{nameof(DotenvCompositionResult)}:State={Readiness.State}:Diagnostics={_diagnostics.Length}";
}

public static class DotenvComposer
{
    public static DotenvCompositionResult ComposeNoSecrets(
        EnvironmentCatalogue catalogue,
        EnvironmentActivationContext context,
        IEnumerable<DotenvEntry> suppliedEntries) =>
        Compose(catalogue, context, suppliedEntries, includeProtectedValues: false);

    public static DotenvCompositionResult ComposeWithSecrets(
        EnvironmentCatalogue catalogue,
        EnvironmentActivationContext context,
        IEnumerable<DotenvEntry> suppliedEntries) =>
        Compose(catalogue, context, suppliedEntries, includeProtectedValues: true);

    private static DotenvCompositionResult Compose(
        EnvironmentCatalogue catalogue,
        EnvironmentActivationContext context,
        IEnumerable<DotenvEntry> suppliedEntries,
        bool includeProtectedValues)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(suppliedEntries);
        DotenvEntry?[] supplied = suppliedEntries.Cast<DotenvEntry?>().ToArray();
        EnvironmentVariableDefinition[] relevant = catalogue.Relevant(context)
            .Where(item => item.Generation.Surfaces.HasFlag(EnvironmentGenerationSurface.Dotenv))
            .ToArray();
        var relevantByKey = relevant.ToDictionary(item => item.Key, StringComparer.Ordinal);
        var diagnostics = new List<EnvironmentDiagnostic>();
        var groups = new Dictionary<string, List<DotenvEntry>>(StringComparer.OrdinalIgnoreCase);
        foreach (DotenvEntry? entry in supplied)
        {
            if (entry is null)
            {
                diagnostics.Add(new EnvironmentDiagnostic(
                    "dotenv-input-null", "$.inputs", null, "dotenv-composition"));
                continue;
            }
            if (!groups.TryGetValue(entry.Key, out List<DotenvEntry>? group))
            {
                group = [];
                groups.Add(entry.Key, group);
            }
            group.Add(entry);
        }

        var suppliedByKey = new Dictionary<string, DotenvEntry>(StringComparer.Ordinal);
        foreach (List<DotenvEntry> group in groups.Values)
        {
            DotenvEntry first = group[0];
            EnvironmentVariableDefinition? relevantDefinition = relevant.FirstOrDefault(definition =>
                string.Equals(definition.Key, first.Key, StringComparison.OrdinalIgnoreCase));
            if (group.Count > 1)
            {
                string code = group.Select(item => item.Key).Distinct(StringComparer.Ordinal).Count() == 1
                    ? "dotenv-input-duplicate-key"
                    : "dotenv-input-key-case-collision";
                if (relevantDefinition is not null) Add(diagnostics, code, relevantDefinition.Key);
                else diagnostics.Add(new EnvironmentDiagnostic(code, "$.inputs", null, "dotenv-composition"));
                continue;
            }

            EnvironmentVariableDefinition? known = catalogue.Lookup(first.Key);
            if (known is null)
            {
                diagnostics.Add(new EnvironmentDiagnostic(
                    "dotenv-input-key-unknown", "$.inputs", null, "dotenv-composition"));
                continue;
            }
            if (!relevantByKey.ContainsKey(first.Key))
            {
                Add(diagnostics, "dotenv-input-key-irrelevant", first.Key);
                continue;
            }
            suppliedByKey.Add(first.Key, first);
        }

        ValidateConfiguredBootstrapMatrix(relevantByKey, suppliedByKey, diagnostics);
        var output = new List<DotenvEntry>();
        foreach (EnvironmentVariableDefinition definition in relevant)
        {
            bool isProtected = definition.Sensitivity != EnvironmentVariableSensitivity.Public;
            suppliedByKey.TryGetValue(definition.Key, out DotenvEntry? suppliedEntry);
            if (isProtected && !includeProtectedValues)
            {
                if (suppliedEntry is not null)
                    Add(diagnostics, "dotenv-secret-input-forbidden", definition.Key);
                if (definition.Requirement == EnvironmentVariableRequirement.Required)
                    output.Add(Placeholder(definition.Key, true));
                continue;
            }

            if (suppliedEntry is not null)
            {
                if (!ValidSuppliedEntry(suppliedEntry, isProtected))
                {
                    Add(diagnostics, "dotenv-input-provenance-invalid", definition.Key);
                    if (definition.Requirement == EnvironmentVariableRequirement.Required)
                        output.Add(Placeholder(definition.Key, isProtected));
                    continue;
                }
                if (!ValidConfiguredBootstrapValue(definition, suppliedEntry.Value!)
                    || !ValidOptionalMailValue(definition.Key, suppliedEntry.Value!))
                {
                    Add(diagnostics, "dotenv-input-value-invalid", definition.Key);
                    if (definition.Requirement == EnvironmentVariableRequirement.Required)
                        output.Add(Placeholder(definition.Key, isProtected));
                    continue;
                }
                if (!isProtected && definition.SafeDefault is not null
                    && string.Equals(suppliedEntry.Value, definition.SafeDefault, StringComparison.Ordinal))
                    continue;
                var candidate = new DotenvEntry(definition.Key, suppliedEntry.Value, suppliedEntry.Kind,
                    isProtected, suppliedEntry.Provenance);
                if (!DotenvCodec.Render(new DotenvDocument([candidate]), true).Succeeded)
                {
                    Add(diagnostics, "dotenv-input-value-invalid", definition.Key);
                    if (definition.Requirement == EnvironmentVariableRequirement.Required)
                        output.Add(Placeholder(definition.Key, isProtected));
                    continue;
                }
                output.Add(candidate);
                continue;
            }

            if (definition.Requirement == EnvironmentVariableRequirement.Required)
                output.Add(Placeholder(definition.Key, isProtected));
        }

        var document = new DotenvDocument(output);
        DotenvReadinessResult readiness = DotenvReadiness.Evaluate(relevant, document);
        DotenvRenderResult validation = DotenvCodec.Render(document, true);
        diagnostics.AddRange(validation.Diagnostics);
        return new DotenvCompositionResult(document, readiness,
            diagnostics.OrderBy(item => item.Code, StringComparer.Ordinal).ThenBy(item => item.Key, StringComparer.Ordinal));
    }

    private static bool ValidSuppliedEntry(DotenvEntry entry, bool isProtected)
    {
        if (string.IsNullOrEmpty(entry.Value) || entry.Kind == DotenvEntryKind.EmptyPlaceholder) return false;
        if (!isProtected)
            return entry.Kind == DotenvEntryKind.LocalHumanValue
                && entry.Provenance == DotenvProvenance.UserInput;
        return entry.Kind switch
        {
            DotenvEntryKind.LocalHumanValue => entry.Provenance == DotenvProvenance.UserInput,
            DotenvEntryKind.GeneratedValueReference => entry.Provenance == DotenvProvenance.Generated,
            _ => false,
        };
    }

    private static bool ValidConfiguredBootstrapValue(
        EnvironmentVariableDefinition definition,
        string value) => definition.ValidatorId switch
    {
        "instance-bootstrap-mode" => value is "Interactive" or "ConfiguredAdministrator",
        "instance-bootstrap-provider" => value is "local" or "keycloak" or "atproto",
        "positive-integer" when definition.Key == "INSTANCE_BOOTSTRAP_BINDING_GENERATION" =>
            long.TryParse(value, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out long generation)
            && generation > 0,
        "email-address" when definition.Key == "INSTANCE_BOOTSTRAP_ADMIN_EMAIL" =>
            ValidEmailAddress(value),
        "profile-name" when definition.Key is "INSTANCE_BOOTSTRAP_ADMIN_FIRST_NAME"
            or "INSTANCE_BOOTSTRAP_ADMIN_LAST_NAME" =>
            value.Length is >= 1 and <= 128 && !value.Any(char.IsControl),
        _ => true,
    };

    private static bool ValidOptionalMailValue(string key, string value) => key switch
    {
        "MAILPIT_UI_PORT" => int.TryParse(value, System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out int port) && port is >= 1 and <= 65535,
        "EMAIL_DISPATCH_RABBITMQ_ENABLED" => value is "true" or "false",
        _ => true,
    };

    private static bool ValidEmailAddress(string value)
    {
        if (value.Length is < 3 or > 320 || value.Any(character =>
                char.IsWhiteSpace(character) || char.IsControl(character)))
            return false;
        int separator = value.IndexOf('@');
        return separator > 0 && separator == value.LastIndexOf('@') && separator < value.Length - 1;
    }

    private static void ValidateConfiguredBootstrapMatrix(
        Dictionary<string, EnvironmentVariableDefinition> relevant,
        Dictionary<string, DotenvEntry> supplied,
        List<EnvironmentDiagnostic> diagnostics)
    {
        const string firstName = "INSTANCE_BOOTSTRAP_ADMIN_FIRST_NAME";
        const string lastName = "INSTANCE_BOOTSTRAP_ADMIN_LAST_NAME";
        if (!relevant.ContainsKey(firstName) || !relevant.ContainsKey(lastName)) return;
        bool hasFirstName = supplied.ContainsKey(firstName);
        bool hasLastName = supplied.ContainsKey(lastName);
        if (hasFirstName == hasLastName) return;
        string missingKey = hasFirstName ? lastName : firstName;
        diagnostics.Add(new EnvironmentDiagnostic(
            "dotenv-input-matrix-invalid", "$.inputs", missingKey, "dotenv-composition"));
    }

    private static DotenvEntry Placeholder(string key, bool isSecret) =>
        new(key, null, DotenvEntryKind.EmptyPlaceholder, isSecret, DotenvProvenance.UserInput);

    private static void Add(List<EnvironmentDiagnostic> diagnostics, string code, string key) =>
        diagnostics.Add(new EnvironmentDiagnostic(code, "$.inputs", key, "dotenv-composition"));
}
