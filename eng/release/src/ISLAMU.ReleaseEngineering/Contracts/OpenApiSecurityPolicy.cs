using System.Text.Json;

namespace ISLAMU.ReleaseEngineering;

/// <summary>
/// Strongly typed OpenAPI security requirement policy distinguishing anonymous allowance
/// (represented equivalently by <c>security: []</c> or an empty requirement object <c>{}</c>)
/// from explicit named authentication scheme alternatives.
/// </summary>
internal sealed record OpenApiSecurityPolicy(bool AllowsAnonymous, HashSet<string> SchemeRequirements)
{
    private const string SecurityKeyword = "security";

    public static OpenApiSecurityPolicy FromOperation(JsonElement operation, JsonElement? documentFallback = null)
    {
        if (!TrySelectSecurityArray(operation, documentFallback, out JsonElement securityArray))
        {
            return new OpenApiSecurityPolicy(AllowsAnonymous: true, new HashSet<string>(StringComparer.Ordinal));
        }

        bool allowsAnonymous = false;
        var schemes = new HashSet<string>(StringComparer.Ordinal);
        int validObjectCount = 0;

        foreach (JsonElement requirement in securityArray.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object))
        {
            validObjectCount++;
            string canonical = FormatRequirement(requirement);
            if (canonical.Length == 0)
            {
                allowsAnonymous = true;
            }
            else
            {
                schemes.Add(canonical);
            }
        }

        if (validObjectCount == 0)
        {
            allowsAnonymous = true;
        }

        return new OpenApiSecurityPolicy(allowsAnonymous, schemes);
    }

    public static void Diff(
        OpenApiSecurityPolicy basePolicy,
        OpenApiSecurityPolicy headPolicy,
        string target,
        ContractChangeCollector collector)
    {
        if (basePolicy.AllowsAnonymous == headPolicy.AllowsAnonymous &&
            basePolicy.SchemeRequirements.SetEquals(headPolicy.SchemeRequirements))
        {
            return;
        }

        if (basePolicy.AllowsAnonymous && !headPolicy.AllowsAnonymous)
        {
            collector.Breaking(
                ContractChangeKinds.SecurityNarrowed,
                target,
                $"Operation {target} now requires authentication schemes where anonymous access was previously allowed.");
            return;
        }

        if (!basePolicy.AllowsAnonymous && headPolicy.AllowsAnonymous)
        {
            collector.ReviewRequired(
                ContractChangeKinds.SecurityChanged,
                target,
                $"Operation {target} now permits anonymous access; verify authorization policy.");
            return;
        }

        bool anySchemeRemoved = basePolicy.SchemeRequirements.Except(headPolicy.SchemeRequirements, StringComparer.Ordinal).Any();
        bool anySchemeAdded = headPolicy.SchemeRequirements.Except(basePolicy.SchemeRequirements, StringComparer.Ordinal).Any();
        if (anySchemeRemoved)
        {
            collector.Breaking(
                ContractChangeKinds.SecurityNarrowed,
                target,
                $"Operation {target} removed previously accepted security scheme requirements.");
        }
        else if (anySchemeAdded)
        {
            collector.Additive(
                ContractChangeKinds.SecurityWidened,
                target,
                $"Operation {target} added an alternative security scheme option.");
        }
    }

    private static bool TrySelectSecurityArray(JsonElement operation, JsonElement? fallback, out JsonElement array)
    {
        if (operation.ValueKind == JsonValueKind.Object && operation.TryGetProperty(SecurityKeyword, out JsonElement own))
        {
            array = own;
            return own.ValueKind == JsonValueKind.Array;
        }

        if (fallback is { ValueKind: JsonValueKind.Object } fallbackObj &&
            fallbackObj.TryGetProperty(SecurityKeyword, out JsonElement inherited) &&
            inherited.ValueKind == JsonValueKind.Array)
        {
            array = inherited;
            return true;
        }

        array = default;
        return false;
    }

    private static string FormatRequirement(JsonElement requirement) =>
        string.Join(
            "+",
            requirement.EnumerateObject()
                .OrderBy(prop => prop.Name, StringComparer.Ordinal)
                .Select(prop =>
                {
                    string scopes = prop.Value.ValueKind == JsonValueKind.Array
                        ? string.Join(",", prop.Value.EnumerateArray().Select(v => v.ToString()).Order(StringComparer.Ordinal))
                        : string.Empty;
                    return $"{prop.Name}[{scopes}]";
                }));
}
