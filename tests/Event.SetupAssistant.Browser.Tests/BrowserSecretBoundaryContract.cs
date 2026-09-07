namespace Event.SetupAssistant.Browser.Tests;

using System.Reflection;
using System.Text.Json;

internal sealed class BrowserSecretBoundaryContract
{
    private const string ProductNamespace = "ISLAMU.Event.SetupAssistant.Browser";
    private readonly Assembly _assembly = Assembly.Load("Event.SetupAssistant.Browser");

    internal IEnumerable<Type> ExportedBrowserTypes() => _assembly.GetExportedTypes()
        .Where(type => type.Namespace?.StartsWith(ProductNamespace, StringComparison.Ordinal) == true);

    internal string[] ReferencedAssemblies() => _assembly.GetReferencedAssemblies()
        .Select(reference => reference.Name ?? string.Empty)
        .Order(StringComparer.Ordinal)
        .ToArray();

    internal Type RequireProductType(string name) =>
        _assembly.GetType($"{ProductNamespace}.{name}", throwOnError: false)
        ?? throw new InvalidOperationException(
            $"missing-approved-owner:{ProductNamespace}.{name}");

    internal static string[] PublicPropertyNames(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

    internal static string[] PublicMethodNames(Type type) =>
        type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(method => method.Name)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

    internal static async Task<JsonDocument> ReadCapabilitiesAsync()
    {
        string root = RepositoryRoot();
        string path = Path.Combine(
            root,
            "eng",
            "setup-assistant",
            "generated",
            "browser-release-capabilities.json");
        return JsonDocument.Parse(await File.ReadAllBytesAsync(path));
    }

    internal static string RepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Explore.slnx")))
                return current.FullName;
            current = current.Parent;
        }

        throw new InvalidOperationException("browser-test-repository-root-missing");
    }
}
