namespace ISLAMU.Event.Setup.Core.Environment;

[Flags]
public enum EnvironmentGenerationSurface
{
    None = 0,
    Dotenv = 1,
    Compose = 2,
    Startup = 4,
}

public enum EnvironmentVariableCategory
{
    Platform,
    Database,
    Identity,
    Security,
    Storage,
    Messaging,
    Integration,
    Observability,
    Deployment,
}

public enum EnvironmentVariableSensitivity
{
    Public,
    Sensitive,
    Secret,
}

public enum EnvironmentVariableRequirement
{
    Optional,
    Required,
    Defaulted,
}

public enum EnvironmentRestartBehavior
{
    None,
    Capability,
    Process,
    Deployment,
}

public enum EnvironmentActivationKind
{
    All,
    Any,
    Not,
    Topology,
    Capability,
    Provider,
    Feature,
}

public sealed record EnvironmentGenerationPolicy
{
    public EnvironmentGenerationPolicy(
        EnvironmentGenerationSurface surfaces,
        int? dotenvOrder,
        int? composeOrder,
        bool composeRequired)
    {
        Surfaces = surfaces;
        DotenvOrder = dotenvOrder;
        ComposeOrder = composeOrder;
        ComposeRequired = composeRequired;
    }

    public EnvironmentGenerationSurface Surfaces { get; }
    public int? DotenvOrder { get; }
    public int? ComposeOrder { get; }
    public bool ComposeRequired { get; }
}

public sealed record EnvironmentDocumentationMetadata
{
    public EnvironmentDocumentationMetadata(string localizationKey, string helpKey, string anchor)
    {
        LocalizationKey = localizationKey;
        HelpKey = helpKey;
        Anchor = anchor;
    }

    public string LocalizationKey { get; }
    public string HelpKey { get; }
    public string Anchor { get; }
}

public sealed record EnvironmentVariableDefinition
{
    public EnvironmentVariableDefinition(
        string key,
        EnvironmentVariableCategory category,
        EnvironmentVariableSensitivity sensitivity,
        EnvironmentVariableRequirement requirement,
        string? safeDefault,
        int order,
        EnvironmentActivationExpression activation,
        string validatorId,
        EnvironmentGenerationPolicy generation,
        EnvironmentRestartBehavior restartBehavior,
        EnvironmentDocumentationMetadata documentation)
    {
        Key = key;
        Category = category;
        Sensitivity = sensitivity;
        Requirement = requirement;
        SafeDefault = safeDefault;
        Order = order;
        Activation = activation;
        ValidatorId = validatorId;
        Generation = generation;
        RestartBehavior = restartBehavior;
        Documentation = documentation;
    }

    public string Key { get; }
    public EnvironmentVariableCategory Category { get; }
    public EnvironmentVariableSensitivity Sensitivity { get; }
    public EnvironmentVariableRequirement Requirement { get; }
    public string? SafeDefault { get; }
    public int Order { get; }
    public EnvironmentActivationExpression Activation { get; }
    public string ValidatorId { get; }
    public EnvironmentGenerationPolicy Generation { get; }
    public EnvironmentRestartBehavior RestartBehavior { get; }
    public EnvironmentDocumentationMetadata Documentation { get; }
    public string LocalizationKey => Documentation.LocalizationKey;
    public string HelpKey => Documentation.HelpKey;
}

public sealed record EnvironmentDiagnostic(string Code, string Path, string? Key, string Category)
{
    public override string ToString() => $"{Code}:{Path}:{Category}";
}

public sealed record EnvironmentActivationContext
{
    private readonly string[] _capabilities;
    private readonly string[] _providers;

    public EnvironmentActivationContext(
        string topology,
        IEnumerable<string> capabilities,
        IEnumerable<string> providers)
    {
        ArgumentNullException.ThrowIfNull(topology);
        ArgumentNullException.ThrowIfNull(capabilities);
        ArgumentNullException.ThrowIfNull(providers);
        Topology = topology;
        _capabilities = capabilities.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        _providers = providers.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }

    public string Topology { get; }
    public IReadOnlyList<string> Capabilities => Array.AsReadOnly((string[])_capabilities.Clone());
    public IReadOnlyList<string> Providers => Array.AsReadOnly((string[])_providers.Clone());

    internal bool HasCapability(string identifier) => Array.BinarySearch(_capabilities, identifier, StringComparer.Ordinal) >= 0;
    internal bool HasProvider(string identifier) => Array.BinarySearch(_providers, identifier, StringComparer.Ordinal) >= 0;
}
