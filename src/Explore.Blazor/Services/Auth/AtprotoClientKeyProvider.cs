using System.Security.Cryptography;
using System.Text.Json;
using CarpaNet.OAuth;
using CarpaNet.OAuth.Crypto;
using Microsoft.Extensions.Options;

namespace Explore.Blazor.Services.Auth;

public sealed class AtprotoClientKeyProvider
{
    public const string ConfigurationKey = "Atproto:OAuthClientPrivateJwks";

    private static readonly HashSet<string> RingProperties = new(StringComparer.Ordinal) { "keys" };
    private static readonly HashSet<string> KeyProperties = new(StringComparer.Ordinal)
    {
        "kty", "crv", "x", "y", "d", "kid", "use", "alg", "status"
    };

    private readonly IReadOnlyDictionary<string, KeyMaterial> _keys;

    public AtprotoClientKeyProvider(IOptions<AtprotoClientKeyOptions> configuredOptions)
    {
        (_keys, ActiveKeyId, FailureCode) = Parse(configuredOptions.Value.OAuthClientPrivateJwks);
    }

    public bool IsReady => FailureCode is null;
    public string? ActiveKeyId { get; }
    public string? FailureCode { get; }

    public ECDsa CreateActiveSigningKey() => CreateSigningKey(ActiveKeyId ?? string.Empty);

    public bool HasKey(string keyId) => IsReady && !string.IsNullOrWhiteSpace(keyId) && _keys.ContainsKey(keyId);

    public DPoPKeyPair CreateCarpaSigningKey(string keyId)
    {
        if (!IsReady || string.IsNullOrWhiteSpace(keyId) || !_keys.TryGetValue(keyId, out var key))
        {
            throw new InvalidOperationException("ATProto OAuth client signing key is unavailable.");
        }

        return DPoPKeyPair.Import(new JsonWebKey
        {
            Kty = "EC",
            Crv = "P-256",
            X = Base64UrlEncode(key.X),
            Y = Base64UrlEncode(key.Y),
            D = Base64UrlEncode(key.D),
            Kid = key.KeyId,
            Use = "sig",
            Alg = "ES256"
        });
    }

    public ECDsa CreateSigningKey(string keyId)
    {
        if (!IsReady || string.IsNullOrWhiteSpace(keyId) || !_keys.TryGetValue(keyId, out var key))
        {
            throw new InvalidOperationException("ATProto OAuth client signing key is unavailable.");
        }

        return ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            D = key.D.ToArray(),
            Q = new ECPoint { X = key.X.ToArray(), Y = key.Y.ToArray() }
        });
    }

    public IReadOnlyList<AtprotoPublicJsonWebKey> GetPublicKeys()
    {
        if (!IsReady)
        {
            return [];
        }

        return _keys.Values
            .OrderBy(key => key.KeyId, StringComparer.Ordinal)
            .Select(key => new AtprotoPublicJsonWebKey(
                "EC", "P-256", Base64UrlEncode(key.X), Base64UrlEncode(key.Y), key.KeyId, "sig", "ES256"))
            .ToArray();
    }

    private static (IReadOnlyDictionary<string, KeyMaterial> Keys, string? ActiveKeyId, string? FailureCode)
        Parse(string? serializedRing)
    {
        if (string.IsNullOrWhiteSpace(serializedRing))
        {
            return (EmptyKeys(), null, "missing_key_ring");
        }

        try
        {
            using var document = JsonDocument.Parse(serializedRing, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 4
            });
            var root = document.RootElement;
            if (root.ValueKind is not JsonValueKind.Object || !HasOnlyUniqueProperties(root, RingProperties)
                || !root.TryGetProperty("keys", out var keysElement)
                || keysElement.ValueKind is not JsonValueKind.Array
                || keysElement.GetArrayLength() is 0 or > 16)
            {
                return (EmptyKeys(), null, "invalid_key_ring");
            }

            var keys = new Dictionary<string, KeyMaterial>(StringComparer.Ordinal);
            string? activeKeyId = null;
            foreach (var keyElement in keysElement.EnumerateArray())
            {
                if (!TryParseKey(keyElement, out var key) || !keys.TryAdd(key.KeyId, key))
                {
                    return (EmptyKeys(), null, "invalid_key_ring");
                }

                if (!key.IsActive)
                {
                    continue;
                }

                if (activeKeyId is not null)
                {
                    return (EmptyKeys(), null, "invalid_active_key_count");
                }

                activeKeyId = key.KeyId;
            }

            return activeKeyId is null
                ? (EmptyKeys(), null, "invalid_active_key_count")
                : (keys, activeKeyId, null);
        }
        catch (JsonException)
        {
            return (EmptyKeys(), null, "invalid_key_ring");
        }
        catch (CryptographicException)
        {
            return (EmptyKeys(), null, "invalid_key_ring");
        }
        catch (FormatException)
        {
            return (EmptyKeys(), null, "invalid_key_ring");
        }
    }

    private static bool TryParseKey(JsonElement element, out KeyMaterial key)
    {
        key = null!;
        if (element.ValueKind is not JsonValueKind.Object || !HasOnlyUniqueProperties(element, KeyProperties))
        {
            return false;
        }

        var keyId = RequiredString(element, "kid");
        var status = RequiredString(element, "status");
        if (RequiredString(element, "kty") != "EC"
            || RequiredString(element, "crv") != "P-256"
            || RequiredString(element, "use") != "sig"
            || RequiredString(element, "alg") != "ES256"
            || string.IsNullOrWhiteSpace(keyId)
            || keyId.Length > 128
            || status is not ("active" or "retired"))
        {
            return false;
        }

        var x = Base64UrlDecode(RequiredString(element, "x"));
        var y = Base64UrlDecode(RequiredString(element, "y"));
        var d = Base64UrlDecode(RequiredString(element, "d"));
        if (x.Length != 32 || y.Length != 32 || d.Length != 32)
        {
            return false;
        }

        using var validationKey = ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            D = d,
            Q = new ECPoint { X = x, Y = y }
        });
        _ = validationKey.KeySize;
        key = new KeyMaterial(keyId, x, y, d, status == "active");
        return true;
    }

    private static bool HasOnlyUniqueProperties(JsonElement element, HashSet<string> allowedProperties)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!allowedProperties.Contains(property.Name) || !seen.Add(property.Name))
            {
                return false;
            }
        }

        return true;
    }

    private static string RequiredString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind is JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;
    }

    private static byte[] Base64UrlDecode(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Contains('=')
            || value.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_')))
        {
            return [];
        }

        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch
        {
            2 => "==",
            3 => "=",
            0 => string.Empty,
            _ => throw new FormatException()
        };
        var decoded = Convert.FromBase64String(padded);
        if (!string.Equals(Base64UrlEncode(decoded), value, StringComparison.Ordinal))
        {
            throw new FormatException();
        }

        return decoded;
    }

    private static string Base64UrlEncode(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static Dictionary<string, KeyMaterial> EmptyKeys() =>
        new Dictionary<string, KeyMaterial>(StringComparer.Ordinal);

    private sealed record KeyMaterial(string KeyId, byte[] X, byte[] Y, byte[] D, bool IsActive);
}

public sealed record AtprotoPublicJsonWebKey(
    string Kty,
    string Crv,
    string X,
    string Y,
    string Kid,
    string Use,
    string Alg);

public sealed class AtprotoClientKeyOptions
{
    public string? OAuthClientPrivateJwks { get; set; }
}
