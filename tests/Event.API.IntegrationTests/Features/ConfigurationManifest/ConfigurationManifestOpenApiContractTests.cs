namespace Event.Api.IntegrationTests.Features.ConfigurationManifest;

using System.Net;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using ISLAMU.Wire.Contracts.ConfigurationPortability;

[ClassDataSource<ContractApiFixture>(Shared = SharedType.PerAssembly)]
public sealed class ConfigurationManifestOpenApiContractTests(
    ContractApiFixture fixture)
{
    private const string OpenApiEndpoint = "/openapi/islamu-event.json";
    private const string CanonicalPath =
        "/api/control-plane/configuration-manifest/export";

    [Test]
    public async Task NativeDocument_ExposesOnlyCanonicalTypedBinaryExport()
    {
        using HttpResponseMessage response =
            await fixture.Client.GetAsync(OpenApiEndpoint);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        using JsonDocument document =
            JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        JsonElement root = document.RootElement;
        JsonElement paths = root.GetProperty("paths");

        await Assert.That(paths.TryGetProperty(
            "/api/tenant/settings/configuration-manifest/export",
            out _)).IsFalse();
        await Assert.That(paths.TryGetProperty(
            "/api/admin/control-plane/tenants/{tenantId}/configuration-manifest/export",
            out _)).IsFalse();

        JsonElement operation = paths.GetProperty(CanonicalPath).GetProperty("get");
        await Assert.That(operation.GetProperty("operationId").GetString())
            .IsEqualTo("ExportConfigurationManifest");
        await Assert.That(operation.GetProperty("x-endpoint-class").GetString())
            .IsEqualTo("Admin");

        JsonElement view = operation.GetProperty("parameters")
            .EnumerateArray()
            .Single(parameter =>
                parameter.GetProperty("name").GetString() == "view");
        await Assert.That(view.GetProperty("schema").GetProperty("$ref").GetString())
            .IsEqualTo("#/components/schemas/ConfigurationManifestExportView");

        JsonElement content = operation.GetProperty("responses")
            .GetProperty("200")
            .GetProperty("content");
        JsonProperty mediaType = content.EnumerateObject().Single();
        await Assert.That(mediaType.Name)
            .IsEqualTo(ConfigurationManifestContractMetadata.MediaType);
        await Assert.That(mediaType.Value.GetProperty("schema")
            .GetProperty("type").GetString()).IsEqualTo("string");
        await Assert.That(mediaType.Value.GetProperty("schema")
            .GetProperty("format").GetString()).IsEqualTo("binary");

        JsonElement schemas = root.GetProperty("components").GetProperty("schemas");
        JsonElement exportView = schemas.GetProperty("ConfigurationManifestExportView");
        await Assert.That(exportView.GetProperty("type").GetString())
            .IsEqualTo("string");
        await Assert.That(exportView.GetProperty("enum")
            .EnumerateArray()
            .Select(value => value.GetString()
                ?? throw new InvalidOperationException(
                    "Configuration manifest export view values must be strings."))
            .ToArray())
            .IsEquivalentTo(["Overrides", "Portable"]);
    }
}
