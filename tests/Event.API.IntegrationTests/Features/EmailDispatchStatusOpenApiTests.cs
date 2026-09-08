
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;

namespace Event.Api.IntegrationTests.Features;

[Category(TestCategories.Email)]
public sealed class EmailDispatchStatusOpenApiTests
{
    [Test]
    public async Task GeneratedNativeDocumentExposesNamedStringEnumsForStatusDtoAndHalResource()
    {
        await using var stream = File.OpenRead(Path.Combine(
            FindRepositoryRoot(), "schemas", "openapi_islamu-event.json"));
        using var document = await JsonDocument.ParseAsync(stream);
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");

        var status = schemas.GetProperty("EmailDispatchStatus");
        await Assert.That(status.GetProperty("type").GetString()).IsEqualTo("string");
        await Assert.That(status.GetProperty("enum").EnumerateArray().Select(value => value.GetString()).ToArray())
            .IsEquivalentTo(["Pending", "Processing", "Sent", "RetryScheduled", "DeadLettered", "Parked", "Unknown", "Skipped"]);

        var reason = schemas.GetProperty("EmailDispatchParkReason");
        await Assert.That(reason.GetProperty("type").GetString()).IsEqualTo("string");
        await Assert.That(reason.GetProperty("enum").EnumerateArray().Select(value => value.GetString()).ToArray())
            .IsEquivalentTo(["CapabilityUnavailable", "Operator"]);

        foreach (var schemaName in new[] { "EmailDispatchStatusDto", "HalResourceOfEmailDispatchStatusDto" })
        {
            var schema = schemas.GetProperty(schemaName);
            var properties = schema.GetProperty("properties");
            await Assert.That(properties.GetProperty("deliveryStatus").GetProperty("$ref").GetString())
                .IsEqualTo("#/components/schemas/EmailDispatchStatus");
            var parkReasonAlternatives = properties.GetProperty("parkReason").GetProperty("oneOf")
                .EnumerateArray().ToArray();
            await Assert.That(parkReasonAlternatives.Length).IsEqualTo(2);
            await Assert.That(parkReasonAlternatives.Single(value => value.TryGetProperty("$ref", out _))
                .GetProperty("$ref").GetString()).IsEqualTo("#/components/schemas/EmailDispatchParkReason");
            await Assert.That(parkReasonAlternatives.Single(value => value.TryGetProperty("type", out _))
                .GetProperty("type").GetString()).IsEqualTo("null");
            var required = schema.TryGetProperty("required", out var requiredProperties)
                ? requiredProperties.EnumerateArray().Select(value => value.GetString()).ToArray()
                : [];
            await Assert.That(required).DoesNotContain("parkReason");
        }
    }

    [Test]
    public async Task GeneratedSmtpHalResourcesExposeTypedDeliveryAndConfirmationFields()
    {
        await using var stream = File.OpenRead(Path.Combine(
            FindRepositoryRoot(), "schemas", "openapi_islamu-event.json"));
        using var document = await JsonDocument.ParseAsync(stream);
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        var settings = schemas.GetProperty("HalResourceOfInstanceSmtpSettingsDto").GetProperty("properties");
        await Assert.That(settings.TryGetProperty("deliveryEnabled", out _)).IsTrue();
        await Assert.That(settings.TryGetProperty("host", out _)).IsTrue();
        await Assert.That(settings.TryGetProperty("_links", out _)).IsTrue();

        var preview = schemas.GetProperty("HalResourceOfEmailDeliveryDisablePreviewDto").GetProperty("properties");
        foreach (var field in new[]
                 {
                     "tenantId", "expectedRevision", "isLocked", "affectedScopes",
                     "confirmationToken", "expiresAtUtc", "canDisable", "_links"
                 })
        {
            await Assert.That(preview.TryGetProperty(field, out _)).IsTrue();
        }
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null
               && !File.Exists(Path.Combine(directory.FullName, "Explore.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the repository root.");
    }
}
