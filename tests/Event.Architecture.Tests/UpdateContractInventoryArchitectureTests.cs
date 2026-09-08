using System.Text.Json;

namespace Event.Architecture.Tests;

public sealed class UpdateContractInventoryArchitectureTests
{
    [Test]
    public async Task CurrentUpdateOperationsMustReachGeneratedClient()
    {
        string root = ResolveRepositoryRoot();
        var failures = new List<string>();
        await using FileStream stream = File.OpenRead(Path.Combine(root, "schemas", "openapi_islamu-event.json"));
        using JsonDocument document = await JsonDocument.ParseAsync(stream);
        var currentOperations = new HashSet<string>(StringComparer.Ordinal);
        string generatedClient = await File.ReadAllTextAsync(Path.Combine(
            root,
            "src/Explore.Blazor.Client/Clients/EventApiTagClients.g.cs"));

        foreach (JsonProperty path in document.RootElement.GetProperty("paths").EnumerateObject())
        {
            foreach (string method in new[] { "put", "patch" })
            {
                if (!path.Value.TryGetProperty(method, out JsonElement operation)
                    || !operation.TryGetProperty("operationId", out JsonElement operationIdElement))
                {
                    continue;
                }

                string operationId = operationIdElement.GetString() ?? string.Empty;
                if (!currentOperations.Add(operationId))
                {
                    failures.Add($"OpenAPI update operation ID is duplicated: {operationId}.");
                }
                if (!generatedClient.Contains($"{operationId}Async(", StringComparison.Ordinal))
                {
                    failures.Add($"Generated client is missing current {method.ToUpperInvariant()} operation {operationId}.");
                }
            }
        }

        await Assert.That(currentOperations).IsNotEmpty();
        await Assert.That(failures).IsEmpty().Because(string.Join(Environment.NewLine, failures));
    }

    private static string ResolveRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Explore.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate repository root containing Explore.slnx.");
    }
}
