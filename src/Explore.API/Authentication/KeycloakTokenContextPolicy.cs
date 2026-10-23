using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Explore.API.Authentication;

/// <summary>
/// Reads signed payload context after JwtBearer cryptographic validation.
/// Keycloak 26 access tokens carry payload typ=Bearer; the JOSE JWT header
/// is shared with ID tokens and cannot establish access purpose.
/// </summary>
internal sealed class KeycloakTokenContextPolicy(string audience, IReadOnlySet<string> permittedClients)
{
    public bool Accepts(SecurityToken token)
    {
        string? encodedPayload = token switch
        {
            JsonWebToken current => current.EncodedPayload,
            JwtSecurityToken legacy => legacy.RawPayload,
            _ => null
        };
        if (string.IsNullOrEmpty(encodedPayload))
            return false;

        try
        {
            using var document = JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(encodedPayload));
            JsonElement payload = document.RootElement;
            if (payload.ValueKind != JsonValueKind.Object)
                return false;

            if (!TrySingleProperty(payload, "aud", out JsonElement resource)
                || !AcceptsAudience(resource)
                || !TrySingleProperty(payload, "typ", out JsonElement purpose)
                || purpose.ValueKind != JsonValueKind.String
                || !string.Equals(purpose.GetString(), "Bearer", StringComparison.Ordinal))
                return false;

            JsonProperty[] clients = payload.EnumerateObject().Where(property => property.NameEquals("azp")).ToArray();
            if (clients.Length == 0)
                return permittedClients.Count == 0;
            if (clients.Length != 1 || clients[0].Value.ValueKind != JsonValueKind.String)
                return false;

            string? client = clients[0].Value.GetString();
            return !string.IsNullOrWhiteSpace(client)
                && (permittedClients.Count == 0 || permittedClients.Contains(client));
        }
        catch (JsonException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private bool AcceptsAudience(JsonElement resource)
    {
        if (resource.ValueKind == JsonValueKind.String)
            return string.Equals(resource.GetString(), audience, StringComparison.Ordinal);
        if (resource.ValueKind != JsonValueKind.Array || resource.GetArrayLength() == 0)
            return false;

        var audiences = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement value in resource.EnumerateArray())
        {
            if (value.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(value.GetString())
                || !audiences.Add(value.GetString()!))
                return false;
        }
        return audiences.Contains(audience);
    }

    private static bool TrySingleProperty(JsonElement payload, string name, out JsonElement value)
    {
        JsonProperty[] properties = payload.EnumerateObject().Where(property => property.NameEquals(name)).ToArray();
        value = properties.Length == 1 ? properties[0].Value : default;
        return properties.Length == 1;
    }
}
