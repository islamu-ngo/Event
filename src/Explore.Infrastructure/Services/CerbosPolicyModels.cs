using System.Text.Json.Serialization;

namespace Explore.Infrastructure.Services;

/// <summary>
/// Top-level Cerbos policy document for derived roles.
/// Serialized as JSON and pushed to the Cerbos Admin API via <c>POST /admin/policy</c>.
/// </summary>
internal sealed class CerbosDerivedRolePolicyDocument
{
    [JsonPropertyName("apiVersion")]
    public string ApiVersion { get; init; } = "api.cerbos.dev/v1";

    [JsonPropertyName("derivedRoles")]
    public CerbosDerivedRolesSpec DerivedRoles { get; init; } = null!;
}

/// <summary>
/// Derived roles specification containing the role set name and its definitions.
/// </summary>
internal sealed class CerbosDerivedRolesSpec
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("definitions")]
    public CerbosDerivedRoleDefinition[] Definitions { get; init; } = [];
}

/// <summary>
/// A single derived role definition mapping parent roles and a CEL condition
/// to a resource-scoped derived role name.
/// </summary>
internal sealed class CerbosDerivedRoleDefinition
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("parentRoles")]
    public string[] ParentRoles { get; init; } = [];

    [JsonPropertyName("condition")]
    public CerbosDerivedRoleCondition? Condition { get; init; }
}

/// <summary>
/// Condition wrapper for Cerbos CEL expressions using the match/all/of structure.
/// </summary>
internal sealed class CerbosDerivedRoleCondition
{
    [JsonPropertyName("match")]
    public CerbosDerivedRoleMatch Match { get; init; } = null!;
}

/// <summary>
/// Match clause — currently only supports <c>all</c> (AND) semantics.
/// </summary>
internal sealed class CerbosDerivedRoleMatch
{
    [JsonPropertyName("all")]
    public CerbosDerivedRoleMatchAll? All { get; init; }
}

/// <summary>
/// All-of clause containing an array of CEL expressions that must all evaluate to true.
/// </summary>
internal sealed class CerbosDerivedRoleMatchAll
{
    [JsonPropertyName("of")]
    public CerbosDerivedRoleExpression[] Of { get; init; } = [];
}

/// <summary>
/// A single CEL expression used in Cerbos policy conditions.
/// </summary>
internal sealed class CerbosDerivedRoleExpression
{
    [JsonPropertyName("expr")]
    public string Expr { get; init; } = string.Empty;
}

/// <summary>
/// Cerbos Admin API request wrapper for adding or updating policy documents.
/// </summary>
internal sealed class CerbosPolicyBatchRequest
{
    [JsonPropertyName("policies")]
    public required IReadOnlyList<object> Policies { get; init; }
}

/// <summary>
/// Cerbos Admin API request wrapper for adding or updating JSON schemas.
/// </summary>
internal sealed class CerbosSchemaBatchRequest
{
    [JsonPropertyName("schemas")]
    public required IReadOnlyList<CerbosSchemaDefinition> Schemas { get; init; }
}

/// <summary>
/// Cerbos Admin API schema definition. The definition is base64-encoded raw JSON schema text.
/// </summary>
internal sealed class CerbosSchemaDefinition
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("definition")]
    public required string Definition { get; init; }
}
