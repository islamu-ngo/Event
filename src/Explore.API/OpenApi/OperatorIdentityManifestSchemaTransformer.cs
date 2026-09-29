namespace Explore.API.OpenApi;

using System.Text.Json.Nodes;
using ISLAMU.Wire.Contracts.ConfigurationPortability;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

public sealed class OperatorIdentityManifestSchemaTransformer : IOpenApiDocumentTransformer
{
    private const string DigestPattern = "^[0-9a-f]{64}$";

    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        if (document.Components?.Schemas is not { } schemas)
            return Task.CompletedTask;

        schemas["OperatorIdentityManifest"] = new OpenApiSchema
        {
            Type = JsonSchemaType.Object,
            AdditionalPropertiesAllowed = false,
            Required = new HashSet<string>(StringComparer.Ordinal)
            {
                "apiVersion", "kind", "settingKey", "contentDigest",
                "revisionHash", "document"
            },
            Properties = new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal)
            {
                ["apiVersion"] = Constant(OperatorIdentityManifestJson.ApiVersion),
                ["kind"] = Constant(OperatorIdentityManifestJson.Kind),
                ["settingKey"] = Constant(OperatorIdentityManifestJson.SettingKey),
                ["contentDigest"] = Digest(),
                ["revisionHash"] = Digest(),
                ["document"] = Document()
            }
        };

        if (schemas.TryGetValue("ImportInstanceOperatorIdentityCommand", out IOpenApiSchema? request)
            && request is OpenApiSchema { Properties: not null } concrete
            && concrete.Properties.TryGetValue("expectedRevisionHash", out IOpenApiSchema? revision)
            && revision is OpenApiSchema revisionSchema)
        {
            revisionSchema.Type = JsonSchemaType.String;
            revisionSchema.Pattern = DigestPattern;
        }

        return Task.CompletedTask;
    }

    private static OpenApiSchema Constant(string value) => new()
    {
        Type = JsonSchemaType.String,
        Enum = [JsonValue.Create(value)!]
    };

    private static OpenApiSchema Digest() => new()
    {
        Type = JsonSchemaType.String,
        Pattern = DigestPattern
    };

    private static OpenApiSchema Document()
    {
        var properties = new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal);
        string[] textFields =
        [
            "jurisdictionCountryCode", "legalName", "legalNoticeUrl",
            "officialOrigin", "operatorKindCode", "privacyUrl",
            "publicContactEmail", "publicName", "registrationIdentifier",
            "termsUrl", "websiteUrl"
        ];
        foreach (string field in textFields)
        {
            properties[field] = new OpenApiSchema
            {
                Type = JsonSchemaType.String | JsonSchemaType.Null,
                MaxLength = 2048
            };
        }
        properties["isOfficialInstance"] = new OpenApiSchema
        {
            Type = JsonSchemaType.Boolean
        };
        properties["operatorId"] = new OpenApiSchema
        {
            Type = JsonSchemaType.String,
            Format = "uuid"
        };
        properties["revision"] = new OpenApiSchema
        {
            Type = JsonSchemaType.String,
            Format = "uuid"
        };

        return new OpenApiSchema
        {
            Type = JsonSchemaType.Object,
            AdditionalPropertiesAllowed = false,
            Required = new HashSet<string>(properties.Keys, StringComparer.Ordinal),
            Properties = properties
        };
    }
}
