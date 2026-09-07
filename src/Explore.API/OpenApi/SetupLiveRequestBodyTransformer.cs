namespace Explore.API.OpenApi;

using Explore.API.Hateoas;
using ISLAMU.Wire.Contracts.SetupLive;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

public sealed class SetupLiveRequestBodyTransformer
    : IOpenApiOperationTransformer, IOpenApiDocumentTransformer
{
    private static readonly HashSet<string> OperationIds =
    [
        RouteNames.CreateSetupTargetEnrollment,
        RouteNames.GetSetupTargetEnrollment,
        RouteNames.RevokeSetupTargetEnrollment,
        RouteNames.RotateSetupTargetEnrollmentCapability,
        RouteNames.GetSetupSecretBindingReadiness,
        RouteNames.WriteSetupSecretBinding,
        RouteNames.GetSetupSecretBindingOperation
    ];

    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        if (operation.OperationId is null
            || !OperationIds.Contains(operation.OperationId))
            return Task.CompletedTask;

        NormalizeResponseMediaTypes(operation);
        NormalizeCreateRequestBody(operation);
        MarkRequiredHeaders(operation);
        AddCapabilityHeader(operation);

        if (string.Equals(
                operation.OperationId,
                RouteNames.WriteSetupSecretBinding,
                StringComparison.Ordinal))
        {
            operation.RequestBody = new OpenApiRequestBody
            {
                Required = true,
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    [SetupLiveContractMetadata.SecretWriteRequestMediaType] = new()
                    {
                        Schema = new OpenApiSchema
                        {
                            Type = JsonSchemaType.String,
                            Format = "binary"
                        }
                    }
                }
            };
        }

        return Task.CompletedTask;
    }

    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.Schemas ??=
            new Dictionary<string, IOpenApiSchema>();

        OpenApiSchema challenge = Component(document, nameof(SetupClientChallenge));
        challenge.Type = JsonSchemaType.String;
        challenge.Format = null;
        challenge.Pattern = "^[A-Za-z0-9_-]{43}$";
        challenge.MinLength = SetupClientChallenge.EncodedLength;
        challenge.MaxLength = SetupClientChallenge.EncodedLength;
        challenge.Properties?.Clear();

        Type[] enumTypes =
        [
            typeof(SetupEnrollmentScope),
            typeof(SetupEnrollmentState),
            typeof(SetupEnrollmentIssuance),
            typeof(SetupSecretBindingReadinessState),
            typeof(SetupSecretBindingOperationState),
            typeof(SetupSecretBindingOperationOutcome)
        ];
        foreach (Type enumType in enumTypes)
        {
            OpenApiStringEnumSchemaMutator.Apply(
                Component(document, enumType.Name),
                enumType);
        }

        SetReference(document, nameof(CreateSetupTargetEnrollmentRequest),
            "clientChallenge", nameof(SetupClientChallenge));
        SetArrayReference(document, nameof(CreateSetupTargetEnrollmentRequest),
            "requestedScopes", nameof(SetupEnrollmentScope), boundedSet: true);

        SetEnrollmentReferences(document, nameof(SetupTargetEnrollmentData));
        SetEnrollmentReferences(document, "HalResourceOfSetupTargetEnrollmentData");
        SetReference(document, nameof(SetupSecretBindingReadinessItem),
            "state", nameof(SetupSecretBindingReadinessState));
        SetReference(document, "HalResourceOfSetupSecretBindingReadinessItem",
            "state", nameof(SetupSecretBindingReadinessState));
        SetOperationReferences(document, nameof(SetupSecretBindingOperationData));
        SetOperationReferences(document, "HalResourceOfSetupSecretBindingOperationData");

        return Task.CompletedTask;
    }

    private static void NormalizeResponseMediaTypes(OpenApiOperation operation)
    {
        if (operation.Responses is null)
            return;

        foreach ((string status, IOpenApiResponse response) in operation.Responses)
        {
            if (response is not OpenApiResponse concrete
                || concrete.Content is null
                || concrete.Content.Count == 0)
            {
                continue;
            }

            IOpenApiSchema? schema = concrete.Content.Values
                .Select(content => content.Schema)
                .FirstOrDefault(candidate => candidate is not null);
            concrete.Content.Clear();
            concrete.Content[
                status.Length == 3 && status[0] == '2'
                    ? SetupLiveContractMetadata.SuccessMediaType
                    : SetupLiveContractMetadata.ErrorMediaType] = new()
            {
                Schema = schema
            };
        }
    }

    private static void NormalizeCreateRequestBody(OpenApiOperation operation)
    {
        if (!string.Equals(
                operation.OperationId,
                RouteNames.CreateSetupTargetEnrollment,
                StringComparison.Ordinal)
            || operation.RequestBody is not OpenApiRequestBody requestBody
            || requestBody.Content is null)
        {
            return;
        }

        IOpenApiSchema? schema = requestBody.Content.Values
            .Select(content => content.Schema)
            .FirstOrDefault(candidate => candidate is not null);
        requestBody.Content.Clear();
        requestBody.Content[SetupLiveContractMetadata.CreateRequestMediaType] =
            new OpenApiMediaType { Schema = schema };
    }

    private static void MarkRequiredHeaders(OpenApiOperation operation)
    {
        string[] required = operation.OperationId switch
        {
            RouteNames.CreateSetupTargetEnrollment =>
                [SetupLiveContractMetadata.IdempotencyHeader],
            RouteNames.RevokeSetupTargetEnrollment or
                RouteNames.RotateSetupTargetEnrollmentCapability or
                RouteNames.WriteSetupSecretBinding =>
                [
                    SetupLiveContractMetadata.CapabilityHeader,
                    SetupLiveContractMetadata.IdempotencyHeader
                ],
            _ => [SetupLiveContractMetadata.CapabilityHeader]
        };
        if (operation.Parameters is null)
            return;

        foreach (IOpenApiParameter parameter in operation.Parameters)
        {
            if (parameter is OpenApiParameter concrete
                && concrete.In == ParameterLocation.Header
                && required.Contains(concrete.Name, StringComparer.Ordinal))
            {
                concrete.Required = true;
            }
        }
    }

    private static void AddCapabilityHeader(OpenApiOperation operation)
    {
        string? status = operation.OperationId switch
        {
            RouteNames.CreateSetupTargetEnrollment => "201",
            RouteNames.RotateSetupTargetEnrollmentCapability => "200",
            _ => null
        };
        if (status is null
            || operation.Responses is null
            || !operation.Responses.TryGetValue(status, out IOpenApiResponse? response)
            || response is not OpenApiResponse concrete)
        {
            return;
        }

        concrete.Headers ??= new Dictionary<string, IOpenApiHeader>();
        concrete.Headers[SetupLiveContractMetadata.CapabilityHeader] =
            new OpenApiHeader
            {
                Description = "One-time enrollment authority returned only on issuance.",
                Schema = new OpenApiSchema { Type = JsonSchemaType.String }
            };
    }

    private static OpenApiSchema Component(OpenApiDocument document, string name)
    {
        if (document.Components!.Schemas!.TryGetValue(name, out IOpenApiSchema? schema)
            && schema is OpenApiSchema concrete)
        {
            return concrete;
        }

        var created = new OpenApiSchema();
        document.Components.Schemas[name] = created;
        return created;
    }

    private static void SetEnrollmentReferences(
        OpenApiDocument document,
        string schemaName)
    {
        SetReference(document, schemaName, "state", nameof(SetupEnrollmentState));
        SetArrayReference(document, schemaName, "scopes",
            nameof(SetupEnrollmentScope), boundedSet: true);
        SetReference(document, schemaName, "issuance", nameof(SetupEnrollmentIssuance));
    }

    private static void SetOperationReferences(
        OpenApiDocument document,
        string schemaName)
    {
        SetReference(document, schemaName, "state",
            nameof(SetupSecretBindingOperationState));
        SetReference(document, schemaName, "outcome",
            nameof(SetupSecretBindingOperationOutcome));
    }

    private static void SetReference(
        OpenApiDocument document,
        string schemaName,
        string propertyName,
        string targetSchemaName)
    {
        OpenApiSchema schema = Component(document, schemaName);
        if (schema.Properties?.ContainsKey(propertyName) == true)
        {
            schema.Properties[propertyName] =
                new OpenApiSchemaReference(targetSchemaName, document);
        }
    }

    private static void SetArrayReference(
        OpenApiDocument document,
        string schemaName,
        string propertyName,
        string targetSchemaName,
        bool boundedSet = false)
    {
        OpenApiSchema schema = Component(document, schemaName);
        if (schema.Properties?.TryGetValue(propertyName, out IOpenApiSchema? property)
            == true && property is OpenApiSchema array)
        {
            array.Items = new OpenApiSchemaReference(targetSchemaName, document);
            array.UniqueItems = boundedSet;
            if (boundedSet)
            {
                array.MinItems = 1;
                array.MaxItems = 3;
            }
        }
    }
}
