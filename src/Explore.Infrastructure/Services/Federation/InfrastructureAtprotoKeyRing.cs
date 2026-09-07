using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CarpaNet.OAuth;
using CarpaNet.OAuth.Crypto;

namespace Explore.Infrastructure.Services.Federation;

internal sealed class InfrastructureAtprotoKeyRing
{
    private const int MaximumSerializedBytes = 64 * 1024;
    private static readonly HashSet<string> RingProperties = new(StringComparer.Ordinal) { "keys" };
    private static readonly HashSet<string> KeyProperties = new(StringComparer.Ordinal)
    {
        "kty", "crv", "x", "y", "d", "kid", "use", "alg", "status"
    };

    private readonly IReadOnlyDictionary<string, KeyMaterial> _keys;

    private InfrastructureAtprotoKeyRing(
        IReadOnlyDictionary<string, KeyMaterial> keys,
        string? activeKeyId)
    {
        _keys = keys;
        ActiveKeyId = activeKeyId;
    }

    public bool IsReady => ActiveKeyId is not null;
    public string? ActiveKeyId { get; }
    public bool HasKey(string keyId) =>
        !string.IsNullOrWhiteSpace(keyId) && _keys.ContainsKey(keyId);

    public DPoPKeyPair CreateKey(string keyId)
    {
        if (!_keys.TryGetValue(keyId, out var key))
        {
            throw new InvalidOperationException("ATProto signing key is unavailable.");
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

    public ECDsa CreateEcdsaKey(string keyId, bool includePrivateKey)
    {
        if (!_keys.TryGetValue(keyId, out var key))
        {
            throw new InvalidOperationException("ATProto signing key is unavailable.");
        }

        return ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            D = includePrivateKey ? key.D.ToArray() : null,
            Q = new ECPoint { X = key.X.ToArray(), Y = key.Y.ToArray() }
        });
    }

    public IReadOnlyList<string> KeyIds => _keys.Keys.Order(StringComparer.Ordinal).ToArray();

    public static InfrastructureAtprotoKeyRing Parse(string? serialized)
    {
        if (string.IsNullOrWhiteSpace(serialized)
            || Encoding.UTF8.GetByteCount(serialized) > MaximumSerializedBytes)
        {
            return Empty();
        }

        try
        {
            using var document = JsonDocument.Parse(serialized, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 4
            });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !HasOnlyUniqueProperties(root, RingProperties)
                || !root.TryGetProperty("keys", out var keysElement)
                || keysElement.ValueKind != JsonValueKind.Array
                || keysElement.GetArrayLength() is 0 or > 16)
            {
                return Empty();
            }

            var keys = new Dictionary<string, KeyMaterial>(StringComparer.Ordinal);
            string? activeKeyId = null;
            foreach (var keyElement in keysElement.EnumerateArray())
            {
                if (!TryParseKey(keyElement, out var key) || !keys.TryAdd(key.KeyId, key))
                {
                    return Empty();
                }

                if (!key.IsActive)
                {
                    continue;
                }

                if (activeKeyId is not null)
                {
                    return Empty();
                }

                activeKeyId = key.KeyId;
            }

            return activeKeyId is null ? Empty() : new(keys, activeKeyId);
        }
        catch (Exception exception) when (exception is JsonException
                                          or CryptographicException
                                          or FormatException
                                          or ArgumentException)
        {
            return Empty();
        }
    }

    private static bool TryParseKey(JsonElement element, out KeyMaterial key)
    {
        key = null!;
        if (element.ValueKind != JsonValueKind.Object
            || !HasOnlyUniqueProperties(element, KeyProperties))
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
        key = new(keyId, x, y, d, status == "active");
        return true;
    }

    private static bool HasOnlyUniqueProperties(JsonElement element, HashSet<string> allowed)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return element.EnumerateObject().All(property =>
            allowed.Contains(property.Name) && seen.Add(property.Name));
    }

    private static string RequiredString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;

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
            0 => string.Empty,
            2 => "==",
            3 => "=",
            _ => throw new FormatException()
        };
        var decoded = Convert.FromBase64String(padded);
        if (!string.Equals(Base64UrlEncode(decoded), value, StringComparison.Ordinal))
        {
            throw new FormatException();
        }

        return decoded;
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static InfrastructureAtprotoKeyRing Empty() =>
        new(new Dictionary<string, KeyMaterial>(StringComparer.Ordinal), null);

    private sealed record KeyMaterial(string KeyId, byte[] X, byte[] Y, byte[] D, bool IsActive);
}
