using System.Text.Json;

using Explore.API.Hateoas;

namespace Event.Architecture.Tests;

public sealed class EventOpenGraphImageOpenApiContractTests
{
    private const string GeneratedMethodSignature =
        "Task<FileResponse> GetEventOpenGraphImageAsync";

    [Test]
    public async Task EventOpenGraphImage_MustDeclarePngBinarySuccessResponse()
    {
        var repositoryRoot = ResolveRepositoryRoot();
        var schemaPath = Path.Combine(repositoryRoot, "schemas", "openapi_islamu-event.json");
        await using var schemaStream = File.OpenRead(schemaPath);
        using var document = await JsonDocument.ParseAsync(schemaStream);

        var operation = document.RootElement
            .GetProperty("paths")
            .GetProperty("/api/event/public/{slugCode}/og-image")
            .GetProperty("get");
        var response = operation
            .GetProperty("responses")
            .GetProperty("200");
        var content = response.GetProperty("content");
        var schema = content
            .GetProperty("image/png")
            .GetProperty("schema");
        var rateLimitedSchema = operation
            .GetProperty("responses")
            .GetProperty("429")
            .GetProperty("content")
            .GetProperty("image/png; v=0.1")
            .GetProperty("schema");

        await Assert.That(operation.GetProperty("operationId").GetString())
            .IsEqualTo(RouteNames.GetEventOpenGraphImage);
        await Assert.That(operation.GetProperty("x-rate-limit-policy").GetString())
            .IsEqualTo("EventOpenGraphImage");
        await Assert.That(schema.GetProperty("type").GetString()).IsEqualTo("string");
        await Assert.That(schema.GetProperty("format").GetString()).IsEqualTo("binary");
        await Assert.That(rateLimitedSchema.GetProperty("$ref").GetString())
            .IsEqualTo("#/components/schemas/ProblemDetails");
        await Assert.That(content.TryGetProperty("application/json", out _)).IsFalse();
        await Assert.That(content.TryGetProperty("application/hal+json", out _)).IsFalse();
    }

    [Test]
    public async Task GeneratedClient_EventOpenGraphImage_UsesPngFileResponse()
    {
        var repositoryRoot = ResolveRepositoryRoot();
        var generatedClientPath = Path.Combine(
            repositoryRoot,
            "src",
            "Explore.Blazor.Client",
            "Clients",
            "EventApiTagClients.g.cs");
        var generatedClient = await File.ReadAllTextAsync(generatedClientPath);

        var methodStart = generatedClient.IndexOf(
            "public virtual async System.Threading.Tasks.Task<FileResponse> GetEventOpenGraphImageAsync",
            StringComparison.Ordinal);
        var methodEnd = generatedClient.IndexOf(
            "\n        }\n",
            methodStart,
            StringComparison.Ordinal);

        await Assert.That(methodStart >= 0 && methodEnd > methodStart).IsTrue();
        if (methodStart < 0 || methodEnd <= methodStart)
        {
            return;
        }

        var method = generatedClient[methodStart..methodEnd];

        await Assert.That(generatedClient.Contains(GeneratedMethodSignature, StringComparison.Ordinal)).IsTrue();
        await Assert.That(method.Contains("MediaTypeWithQualityHeaderValue.Parse(\"image/png\")", StringComparison.Ordinal)).IsTrue();
        await Assert.That(method.Contains("new FileResponse", StringComparison.Ordinal)).IsTrue();
        await Assert.That(method.Contains("FileContentResult", StringComparison.Ordinal)).IsFalse();
        await Assert.That(method.Contains("ReadObjectResponseAsync<FileContentResult>", StringComparison.Ordinal)).IsFalse();
    }

    private static string ResolveRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "Explore.slnx")))
        {
            current = current.Parent;
        }

        return current?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the repository root from the architecture test output directory.");
    }
}
