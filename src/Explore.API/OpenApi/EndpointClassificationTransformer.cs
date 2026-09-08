using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Explore.API.Attributes;
using Explore.API.Filters;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.OpenApi;

namespace Explore.API.OpenApi;

public sealed class EndpointClassificationTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        var endpointMetadata = context.Description.ActionDescriptor.EndpointMetadata;
        var classification = endpointMetadata.OfType<EndpointClassificationAttribute>().LastOrDefault();
        var rateLimit = endpointMetadata.OfType<EnableRateLimitingAttribute>().LastOrDefault();
        var outputCache = endpointMetadata.OfType<OutputCacheAttribute>().LastOrDefault();
        var requiresMultiTenant = endpointMetadata.OfType<RequireMultiTenantAttribute>().Any();
        var requiresIdempotencyKey = endpointMetadata.OfType<RequireIdempotencyKeyAttribute>().Any();

        if (classification is null
            && rateLimit is null
            && outputCache is null
            && !requiresMultiTenant
            && !requiresIdempotencyKey)
        {
            return Task.CompletedTask;
        }

        operation.Extensions ??= new Dictionary<string, IOpenApiExtension>();

        if (classification is not null)
        {
            operation.Extensions["x-endpoint-class"] =
                new JsonNodeExtension(classification.Class.ToString());
        }

        if (!string.IsNullOrWhiteSpace(rateLimit?.PolicyName))
        {
            operation.Extensions["x-rate-limit-policy"] =
                new JsonNodeExtension(rateLimit.PolicyName);
        }

        if (!string.IsNullOrWhiteSpace(outputCache?.PolicyName))
        {
            operation.Extensions["x-output-cache-policy"] =
                new JsonNodeExtension(outputCache.PolicyName);
        }

        if (requiresMultiTenant)
        {
            operation.Extensions["x-tenant-mode"] =
                new JsonNodeExtension("multi-tenant-required");
        }

        if (requiresIdempotencyKey)
        {
            ApplyIdempotencyKeyRequirement(operation, endpointMetadata);
        }

        return Task.CompletedTask;
    }

    internal static bool ApplyIdempotencyKeyRequirement(
        OpenApiOperation operation,
        IEnumerable<object> endpointMetadata)
    {
        if (!endpointMetadata.OfType<RequireIdempotencyKeyAttribute>().Any())
        {
            return false;
        }

        operation.Extensions ??= new Dictionary<string, IOpenApiExtension>();
        operation.Extensions["x-idempotency-key-required"] =
            new JsonNodeExtension(JsonValue.Create(true)!);
        operation.Parameters ??= [];
        var parameter = operation.Parameters.FirstOrDefault(candidate =>
            candidate.In == ParameterLocation.Header &&
            string.Equals(candidate.Name, "Idempotency-Key", StringComparison.OrdinalIgnoreCase));
        if (parameter is null)
        {
            parameter = new OpenApiParameter
            {
                Name = "Idempotency-Key",
                In = ParameterLocation.Header,
                Description = "Client-generated replay key bound by the server to the current principal or hashed capability and resolved route.",
                Schema = new OpenApiSchema { Type = JsonSchemaType.String }
            };
            operation.Parameters.Add(parameter);
        }

        if (parameter is OpenApiParameter concreteParameter)
        {
            concreteParameter.Required = true;
        }
        return true;
    }
}
