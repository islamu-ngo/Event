using System.Text.Json;

namespace Event.Architecture.Tests;

public sealed class NativeRegistrationOpenApiContractTests
{
    private const string AuthenticatedLaunchPath =
        "/api/events/{eventId}/registration-orders/{orderId}/attempts";
    private const string AuthenticatedProgressPath =
        "/api/events/{eventId}/registration-orders/{orderId}/requirement-progress";

    [Test]
    public async Task NativeRegistrationContract_ExposesTypedProgressAndExplicitIdempotencyHeader()
    {
        using JsonDocument document = await ReadOpenApiAsync();
        JsonElement root = document.RootElement;
        JsonElement schemas = root.GetProperty("components").GetProperty("schemas");
        JsonElement progress = schemas.GetProperty("HalResourceOfNativeRegistrationRequirementProgressCollectionDto");
        JsonElement progressProperties = progress.GetProperty("properties");
        JsonElement launch = root.GetProperty("paths").GetProperty(AuthenticatedLaunchPath).GetProperty("post");
        JsonElement progressOperation = root.GetProperty("paths").GetProperty(AuthenticatedProgressPath).GetProperty("get");

        await Assert.That(progressProperties.TryGetProperty("registrationOrderId", out _)).IsTrue();
        await Assert.That(progressProperties.TryGetProperty("requirements", out JsonElement requirements)).IsTrue();
        await Assert.That(requirements.GetProperty("items").GetProperty("properties")
            .TryGetProperty("channelId", out _)).IsTrue();
        await Assert.That(progressProperties.TryGetProperty("_links", out _)).IsTrue();
        await Assert.That(progressOperation.GetProperty("responses").TryGetProperty("200", out _)).IsTrue();
        await Assert.That(launch.GetProperty("parameters").EnumerateArray().Any(parameter =>
            parameter.GetProperty("in").GetString() == "header" &&
            parameter.GetProperty("name").GetString() == "Idempotency-Key")).IsTrue();
    }

    [Test]
    public async Task NativeRegistrationContract_KeepsCapabilityBoundedAndPinnedFormAttendeeSafe()
    {
        using JsonDocument document = await ReadOpenApiAsync();
        JsonElement schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        JsonElement attemptProperties = schemas.GetProperty("NativeRegistrationAttemptDto").GetProperty("properties");
        JsonElement formProperties = attemptProperties.GetProperty("form").GetProperty("properties");
        JsonElement fieldProperties = formProperties.GetProperty("sections").GetProperty("items")
            .GetProperty("properties").GetProperty("fields").GetProperty("items").GetProperty("properties");

        await Assert.That(attemptProperties.TryGetProperty("attemptCapabilityToken", out _)).IsTrue();
        await Assert.That(attemptProperties.TryGetProperty("channelId", out _)).IsTrue();
        await Assert.That(attemptProperties.TryGetProperty("subjects", out _)).IsTrue();
        await Assert.That(attemptProperties.TryGetProperty("progress", out _)).IsTrue();
        await Assert.That(formProperties.TryGetProperty("tenantId", out _)).IsFalse();
        await Assert.That(formProperties.TryGetProperty("sourceTemplateFormId", out _)).IsFalse();
        await Assert.That(fieldProperties.TryGetProperty("consentText", out _)).IsTrue();
        await Assert.That(fieldProperties.TryGetProperty("organizerVisibilityId", out _)).IsFalse();
        await Assert.That(fieldProperties.TryGetProperty("retentionPolicyId", out _)).IsFalse();

        string generated = await File.ReadAllTextAsync(Path.Combine(
            ResolveRepositoryRoot(), "src", "Explore.Blazor.Client", "Clients", "EventApiTagClients.g.cs"));
        await Assert.That(generated).Contains(
            "partial class HalResourceOfNativeRegistrationRequirementProgressCollectionDto");
        await Assert.That(generated).Contains("ICollection<Requirements> Requirements");
        await Assert.That(generated).Contains("string? idempotency_Key = null");
        await Assert.That(generated).Contains("GetAuthenticatedNativeRegistrationRequirementProgressAsync");
        await Assert.That(generated).Contains("SkipAuthenticatedNativeRegistrationRequirementAsync");
    }

    private static async Task<JsonDocument> ReadOpenApiAsync()
    {
        FileStream stream = File.OpenRead(Path.Combine(
            ResolveRepositoryRoot(), "schemas", "openapi_islamu-event.json"));
        await using (stream)
        {
            return await JsonDocument.ParseAsync(stream);
        }
    }

    private static string ResolveRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "Explore.slnx")))
        {
            current = current.Parent;
        }

        return current?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the repository root from architecture test output directory.");
    }
}
