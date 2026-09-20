namespace Event.Architecture.Tests;

/// <summary>Contract bytes captured from the inputs used by this architecture build.</summary>
internal static class GeneratedContractInputs
{
    private static readonly Lazy<string> SchemaText = new(() => Read("openapi_islamu-event.json"));
    private static readonly Lazy<string> ClientText = new(() => Read("EventApiTagClients.g.cs"));
    private static readonly Lazy<string> PolicyText = new(() => Read("mutable-generated-contracts.txt"));

    public static string Schema => SchemaText.Value;
    public static string Client => ClientText.Value;
    public static string MutablePolicy => PolicyText.Value;
    public static Stream OpenSchema() => Open("openapi_islamu-event.json");

    private static Stream Open(string name) => typeof(GeneratedContractInputs).Assembly
        .GetManifestResourceStream("GeneratedContracts." + name)
        ?? throw new InvalidOperationException($"Missing build-captured contract {name}. Rebuild the architecture project.");

    private static string Read(string name)
    {
        using var reader = new StreamReader(Open(name));
        return reader.ReadToEnd();
    }
}
