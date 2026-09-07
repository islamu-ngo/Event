namespace Event.Api.IntegrationTests.Features.ConfigurationManifest;

using System.Net;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API.ConfigurationImport;
using Explore.API.Hateoas;
using ISLAMU.Wire.Contracts.ConfigurationPortability;

[ClassDataSource<ContractApiFixture>(Shared = SharedType.PerAssembly)]
public sealed class ConfigurationImportOpenApiContractTests(
    ContractApiFixture fixture)
{
    private const string OpenApiEndpoint = "/openapi/islamu-event.json";
    private const string InstancePath =
        "/api/control-plane/configuration-import/sessions";
    private const string TenantPath =
        "/api/tenants/{tenantId}/configuration-import/sessions";

    [Test]
    public async Task NativeDocument_ExposesCanonicalBinaryUploadsAndActions()
    {
        using JsonDocument document = await ReadDocument();
        JsonElement paths = document.RootElement.GetProperty("paths");

        AssertUpload(
            paths.GetProperty(InstancePath).GetProperty("post"),
            RouteNames.CreateInstanceConfigurationImportSession,
            ConfigurationManifestContractMetadata.MediaType);
        AssertUpload(
            paths.GetProperty(TenantPath).GetProperty("post"),
            RouteNames.CreateTenantConfigurationImportSession,
            TenantConfigurationPackageContractMetadata.MediaType);

        AssertAction(
            paths.GetProperty($"{InstancePath}/{{sessionId}}/preview")
                .GetProperty("post"),
            RouteNames.PreviewInstanceConfigurationImportSession,
            hasBody: true);
        AssertAction(
            paths.GetProperty($"{InstancePath}/{{sessionId}}/refresh")
                .GetProperty("post"),
            RouteNames.RefreshInstanceConfigurationImportSession,
            hasBody: true);
        AssertAction(
            paths.GetProperty($"{InstancePath}/{{sessionId}}")
                .GetProperty("delete"),
            RouteNames.CancelInstanceConfigurationImportSession,
            hasBody: false);
        AssertAction(
            paths.GetProperty($"{TenantPath}/{{sessionId}}/preview")
                .GetProperty("post"),
            RouteNames.PreviewTenantConfigurationImportSession,
            hasBody: true);
        AssertAction(
            paths.GetProperty($"{TenantPath}/{{sessionId}}/refresh")
                .GetProperty("post"),
            RouteNames.RefreshTenantConfigurationImportSession,
            hasBody: true);
        AssertAction(
            paths.GetProperty($"{TenantPath}/{{sessionId}}")
                .GetProperty("delete"),
            RouteNames.CancelTenantConfigurationImportSession,
            hasBody: false);
    }

    [Test]
    public async Task PreviewSchema_CarriesIntentButNoTargetOrFreshnessAuthority()
    {
        using JsonDocument document = await ReadDocument();
        JsonElement schemas = document.RootElement.GetProperty("components")
            .GetProperty("schemas");
        string[] properties = schemas
            .GetProperty("ConfigurationImportPreviewRequest")
            .GetProperty("properties")
            .EnumerateObject()
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        await Assert.That(properties).IsEquivalentTo(
        [
            "applyMode",
            "grantedApprovalCodes",
            "mappings",
            "selectedSectionKeys"
        ]);
        await Assert.That(
                schemas.GetProperty("ConfigurationImportApplyMode")
                    .GetProperty("type")
                    .GetString())
            .IsEqualTo("string");
        await Assert.That(
                schemas.GetProperty("ConfigurationImportScope")
                    .GetProperty("type")
                    .GetString())
            .IsEqualTo("string");
        await Assert.That(
                schemas.GetProperty("ConfigurationImportSessionState")
                    .GetProperty("type")
                    .GetString())
            .IsEqualTo("string");
        await Assert.That(
                schemas.GetProperty(
                        "HalResourceOfConfigurationImportSessionCreatedResult")
                    .GetProperty("properties")
                    .EnumerateObject()
                    .Any())
            .IsTrue();
        await Assert.That(
                schemas.GetProperty(
                        "HalResourceOfConfigurationImportPreviewResult")
                    .GetProperty("properties")
                    .EnumerateObject()
                    .Any())
            .IsTrue();
    }

    private async Task<JsonDocument> ReadDocument()
    {
        using HttpResponseMessage response =
            await fixture.Client.GetAsync(OpenApiEndpoint);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        return await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync());
    }

    private static void AssertUpload(
        JsonElement operation,
        string operationId,
        string mediaType)
    {
        if (!string.Equals(
                operation.GetProperty("operationId").GetString(),
                operationId,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Unexpected configuration import upload operation: {operationId}.");
        }
        JsonProperty content = operation.GetProperty("requestBody")
            .GetProperty("content")
            .EnumerateObject()
            .Single();
        if (!string.Equals(content.Name, mediaType, StringComparison.Ordinal)
            || content.Value.GetProperty("schema")
                .GetProperty("type").GetString() != "string"
            || content.Value.GetProperty("schema")
                .GetProperty("format").GetString() != "binary")
        {
            throw new InvalidOperationException(
                $"Configuration import upload {operationId} is not binary.");
        }
        if (!operation.GetProperty("responses")
                .TryGetProperty("413", out _))
        {
            throw new InvalidOperationException(
                $"Configuration import upload {operationId} is missing 413.");
        }
    }

    private static void AssertAction(
        JsonElement operation,
        string operationId,
        bool hasBody)
    {
        if (!string.Equals(
                operation.GetProperty("operationId").GetString(),
                operationId,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Unexpected configuration import operation: {operationId}.");
        }
        JsonElement capability = operation.GetProperty("parameters")
            .EnumerateArray()
            .Single(parameter =>
                parameter.GetProperty("name").GetString()
                == ConfigurationImportApiBoundary.AccessTokenHeader);
        if (capability.GetProperty("in").GetString() != "header"
            || !capability.GetProperty("required").GetBoolean())
        {
            throw new InvalidOperationException(
                $"Configuration import operation {operationId} has an optional capability.");
        }
        if (hasBody)
        {
            JsonProperty body = operation.GetProperty("requestBody")
                .GetProperty("content")
                .EnumerateObject()
                .Single(property =>
                    property.Name.StartsWith(
                        "application/json",
                        StringComparison.Ordinal));
            if (!body.Value.GetProperty("schema")
                    .GetProperty("$ref").GetString()!
                    .EndsWith(
                        "/ConfigurationImportPreviewRequest",
                        StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Configuration import operation {operationId} has the wrong body.");
            }
        }
        else if (operation.TryGetProperty("requestBody", out _))
        {
            throw new InvalidOperationException(
                $"Configuration import cancellation {operationId} has a body.");
        }
    }
}
