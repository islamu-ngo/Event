using System.Text.RegularExpressions;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi;

namespace Explore.API.OpenApi;

/// <summary>
/// Document transformer that validates every operation has a stable, explicit operationId.
/// Runs on every OpenAPI document request; in the <c>Development</c> environment any violation
/// aggregates into a single <see cref="InvalidOperationException"/>. In non-Development
/// environments violations are silently ignored so that production startup is never blocked
/// by a transient misconfiguration. The canonical fix is always to add
/// <c>[HttpVerb(Name = RouteNames.Xxx)]</c> on the offending controller action so that
/// ASP.NET Core's <c>Name</c>-to-operationId propagation yields a stable identifier.
/// </summary>
/// <remarks>
/// Invariants enforced:
/// <list type="number">
///   <item>OperationId is non-null, non-empty, non-whitespace.</item>
///   <item>OperationId does not match a placeholder/fallback pattern such as verb-only
///         (<c>GetAsync</c>, <c>POST</c>) or numeric-suffix overflow collisions
///         (<c>GetEventsAsync2</c>, <c>Create3</c>).</item>
/// </list>
/// Uniqueness is already covered by <c>ContractInvariantsTests.OpenApiDocument_OperationIdsAreUnique</c>
/// and does not need a runtime check.
/// </remarks>
public sealed partial class OperationIdInvariantTransformer : IOpenApiDocumentTransformer
{
    private readonly IHostEnvironment _environment;

    public OperationIdInvariantTransformer(IHostEnvironment environment)
    {
        _environment = environment;
    }

    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        if (!_environment.IsDevelopment())
        {
            return Task.CompletedTask;
        }

        if (document.Paths is null)
        {
            return Task.CompletedTask;
        }

        var violations = new List<string>();

        foreach (var (path, pathItem) in document.Paths)
        {
            if (pathItem?.Operations is null)
            {
                continue;
            }

            foreach (var (method, operation) in pathItem.Operations)
            {
                var operationId = operation.OperationId;

                if (string.IsNullOrWhiteSpace(operationId))
                {
                    violations.Add(
                        $"{method.ToString().ToUpperInvariant()} {path}: missing operationId. " +
                        $"Add [Http{ToPascalCase(method.ToString())}(Name = RouteNames.Xxx)] on the action.");
                    continue;
                }

                if (PlaceholderOperationIdRegex().IsMatch(operationId))
                {
                    violations.Add(
                        $"{method.ToString().ToUpperInvariant()} {path}: operationId '{operationId}' " +
                        "matches a placeholder/fallback pattern (verb-only or numeric-suffix collision). " +
                        "Replace with an explicit [Http...(Name = RouteNames.Xxx)] so both RouteName and operationId stay stable.");
                }
            }
        }

        if (violations.Count > 0)
        {
            throw new InvalidOperationException(
                $"OperationId invariant violated ({violations.Count} offending operation(s)). " +
                $"Each controller action must declare [HttpVerb(Name = RouteNames.Xxx)] so that " +
                $"the generated OpenAPI document and NSwag client both receive stable identifiers. " +
                $"Violations:{Environment.NewLine}  - {string.Join(Environment.NewLine + "  - ", violations)}");
        }

        return Task.CompletedTask;
    }

    [GeneratedRegex(@"^(GET|POST|PUT|PATCH|DELETE|HEAD|OPTIONS)(Async)?$|\d+$|\d+Async$", RegexOptions.IgnoreCase)]
    private static partial Regex PlaceholderOperationIdRegex();

    private static string ToPascalCase(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        return char.ToUpperInvariant(value[0]) + value[1..].ToLowerInvariant();
    }
}
