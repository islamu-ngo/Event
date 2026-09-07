using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CarpaNet.OAuth.Crypto;
using CarpaNet.OAuth.Storage;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Secrets;
using Explore.Domain.Secrets;

namespace Explore.Infrastructure.Services.Federation;

public sealed class AtprotoSessionEnvelopeProtector(ISecretResolver secretResolver)
{
    internal const int CurrentEnvelopeVersion = 1;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int MaximumPlaintextBytes = 256 * 1024;

    internal async Task<AtprotoProtectedSession> ProtectAsync(
        OAuthSessionData session,
        AtprotoOAuthSessionStoreContext context,
        CancellationToken cancellationToken)
    {
        ValidateSession(session, context);
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(
            session,
            AtprotoOAuthSessionJsonContext.Default.OAuthSessionData);
        if (plaintext.Length is 0 or > MaximumPlaintextBytes)
        {
            CryptographicOperations.ZeroMemory(plaintext);
            throw Unavailable("invalid_session");
        }

        try
        {
            using var keyRing = await ResolveKeyRingAsync(cancellationToken).ConfigureAwait(false);
            var key = keyRing.GetActiveKey();
            var nonce = RandomNumberGenerator.GetBytes(NonceSize);
            var ciphertext = new byte[plaintext.Length];
            var tag = new byte[TagSize];
            var associatedData = BuildAssociatedData(context);
            try
            {
                using var aes = new AesGcm(key.Key, TagSize);
                aes.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);
                var envelope = new byte[NonceSize + ciphertext.Length + TagSize];
                nonce.CopyTo(envelope, 0);
                ciphertext.CopyTo(envelope, NonceSize);
                tag.CopyTo(envelope, NonceSize + ciphertext.Length);
                return new(envelope, key.KeyId);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(nonce);
                CryptographicOperations.ZeroMemory(ciphertext);
                CryptographicOperations.ZeroMemory(tag);
                CryptographicOperations.ZeroMemory(associatedData);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    internal async Task<AtprotoUnprotectedSession> UnprotectAsync(
        byte[] envelope,
        string encryptionKeyId,
        AtprotoOAuthSessionStoreContext context,
        CancellationToken cancellationToken)
    {
        if (envelope.Length <= NonceSize + TagSize || envelope.Length > MaximumPlaintextBytes + NonceSize + TagSize)
        {
            throw Unavailable("malformed_envelope");
        }

        using var keyRing = await ResolveKeyRingAsync(cancellationToken).ConfigureAwait(false);
        if (!keyRing.TryGetKey(encryptionKeyId, out var key))
        {
            throw Unavailable("unknown_kid");
        }

        var ciphertextLength = envelope.Length - NonceSize - TagSize;
        var plaintext = new byte[ciphertextLength];
        var associatedData = BuildAssociatedData(context);
        try
        {
            using var aes = new AesGcm(key.Key, TagSize);
            aes.Decrypt(
                envelope.AsSpan(0, NonceSize),
                envelope.AsSpan(NonceSize, ciphertextLength),
                envelope.AsSpan(NonceSize + ciphertextLength, TagSize),
                plaintext,
                associatedData);
            var session = JsonSerializer.Deserialize(
                plaintext,
                AtprotoOAuthSessionJsonContext.Default.OAuthSessionData)
                ?? throw Unavailable("malformed_envelope");
            ValidateSession(session, context);
            return new(session, !string.Equals(encryptionKeyId, keyRing.ActiveKeyId, StringComparison.Ordinal));
        }
        catch (AtprotoOAuthSessionUnavailableException)
        {
            throw;
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException or NotSupportedException)
        {
            throw Unavailable("invalid_envelope");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
            CryptographicOperations.ZeroMemory(associatedData);
        }
    }

    private async Task<AtprotoSessionEncryptionKeyRing> ResolveKeyRingAsync(
        CancellationToken cancellationToken)
    {
        var resolved = await secretResolver.ResolveAsync(
            SecretDefinitionRegistry.Keys.Atproto.SessionEncryptionKeyRing,
            tenantId: null,
            cancellationToken).ConfigureAwait(false);
        return AtprotoSessionEncryptionKeyRing.Parse(resolved.Value)
            ?? throw Unavailable("key_ring_unavailable");
    }

    private static void ValidateSession(OAuthSessionData session, AtprotoOAuthSessionStoreContext context)
    {
        if (session is null || session.TokenSet is null || session.DPoPKey is null)
        {
            throw Unavailable("invalid_session");
        }

        var tokenSet = session.TokenSet;
        var dpopKey = session.DPoPKey;
        if (string.IsNullOrWhiteSpace(tokenSet.Sub)
            || string.IsNullOrWhiteSpace(tokenSet.Audience)
            || string.IsNullOrWhiteSpace(tokenSet.Scope)
            || string.IsNullOrWhiteSpace(tokenSet.AccessToken)
            || string.IsNullOrWhiteSpace(tokenSet.RefreshToken)
            || string.IsNullOrWhiteSpace(tokenSet.Issuer)
            || string.IsNullOrWhiteSpace(session.ClientId)
            || string.IsNullOrWhiteSpace(session.RedirectUri)
            || string.IsNullOrWhiteSpace(session.Scope)
            || !string.Equals(tokenSet.Sub, context.ExpectedSubjectDid, StringComparison.Ordinal)
            || !string.Equals(
                AtprotoOAuthSessionStoreContext.NormalizePdsUri(tokenSet.Audience),
                context.ExpectedPdsUri,
                StringComparison.Ordinal)
            || dpopKey.Kty != "EC"
            || dpopKey.Crv != "P-256"
            || dpopKey.Alg != "ES256"
            || dpopKey.Use != "sig"
            || string.IsNullOrWhiteSpace(dpopKey.X)
            || string.IsNullOrWhiteSpace(dpopKey.Y)
            || string.IsNullOrWhiteSpace(dpopKey.D))
        {
            throw Unavailable("invalid_session");
        }

        try
        {
            using var key = DPoPKeyPair.Import(dpopKey);
        }
        catch (Exception exception) when (exception is ArgumentException or CryptographicException or FormatException)
        {
            throw Unavailable("invalid_session");
        }
    }

    private static byte[] BuildAssociatedData(AtprotoOAuthSessionStoreContext context)
    {
        var writer = new ArrayBufferWriter<byte>();
        WriteInt32(writer, CurrentEnvelopeVersion);
        WriteString(writer, context.TenantId.ToString("D"));
        WriteString(writer, context.UserId.ToString("D"));
        WriteString(writer, RepositoryBackedAtprotoSession.Provider);
        WriteString(writer, context.ExpectedSubjectDid);
        WriteString(writer, context.ExpectedPdsUri);
        WriteString(writer, context.OAuthClientKeyId);
        return writer.WrittenSpan.ToArray();
    }

    private static void WriteString(ArrayBufferWriter<byte> writer, string value)
    {
        var byteCount = Encoding.UTF8.GetByteCount(value);
        WriteInt32(writer, byteCount);
        Encoding.UTF8.GetBytes(value, writer.GetSpan(byteCount));
        writer.Advance(byteCount);
    }

    private static void WriteInt32(ArrayBufferWriter<byte> writer, int value)
    {
        BinaryPrimitives.WriteInt32BigEndian(writer.GetSpan(sizeof(int)), value);
        writer.Advance(sizeof(int));
    }

    private static AtprotoOAuthSessionUnavailableException Unavailable(string failureCode) =>
        new(failureCode);
}

internal sealed class AtprotoSessionEncryptionKeyRing : IDisposable
{
    private const int MaximumSerializedBytes = 64 * 1024;
    private static readonly HashSet<string> RingProperties = new(StringComparer.Ordinal) { "keys" };
    private static readonly HashSet<string> KeyProperties = new(StringComparer.Ordinal) { "kid", "k", "status" };
    private readonly Dictionary<string, AtprotoSessionEncryptionKey> _keys;

    private AtprotoSessionEncryptionKeyRing(
        Dictionary<string, AtprotoSessionEncryptionKey> keys,
        string activeKeyId)
    {
        _keys = keys;
        ActiveKeyId = activeKeyId;
    }

    public string ActiveKeyId { get; }

    public AtprotoSessionEncryptionKey GetActiveKey() => _keys[ActiveKeyId];

    public bool TryGetKey(string keyId, out AtprotoSessionEncryptionKey key) =>
        _keys.TryGetValue(keyId, out key!);

    public static AtprotoSessionEncryptionKeyRing? Parse(string? serialized)
    {
        if (string.IsNullOrWhiteSpace(serialized)
            || Encoding.UTF8.GetByteCount(serialized) > MaximumSerializedBytes)
        {
            return null;
        }

        var parsedKeys = new Dictionary<string, AtprotoSessionEncryptionKey>(StringComparer.Ordinal);
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
                || !root.TryGetProperty("keys", out var keys)
                || keys.ValueKind != JsonValueKind.Array
                || keys.GetArrayLength() is 0 or > 16)
            {
                return null;
            }

            string? activeKeyId = null;
            foreach (var element in keys.EnumerateArray())
            {
                if (!TryParseKey(element, out var key) || !parsedKeys.TryAdd(key.KeyId, key))
                {
                    key?.Dispose();
                    DisposeKeys(parsedKeys);
                    return null;
                }

                if (!key.IsActive)
                {
                    continue;
                }

                if (activeKeyId is not null)
                {
                    DisposeKeys(parsedKeys);
                    return null;
                }

                activeKeyId = key.KeyId;
            }

            if (activeKeyId is null)
            {
                DisposeKeys(parsedKeys);
                return null;
            }

            return new(parsedKeys, activeKeyId);
        }
        catch (Exception exception) when (exception is JsonException or FormatException or ArgumentException)
        {
            DisposeKeys(parsedKeys);
            return null;
        }
    }

    public void Dispose() => DisposeKeys(_keys);

    private static bool TryParseKey(JsonElement element, out AtprotoSessionEncryptionKey? key)
    {
        key = null;
        if (element.ValueKind != JsonValueKind.Object || !HasOnlyUniqueProperties(element, KeyProperties))
        {
            return false;
        }

        var keyId = RequiredString(element, "kid");
        var status = RequiredString(element, "status");
        if (string.IsNullOrWhiteSpace(keyId)
            || keyId.Length > 128
            || keyId.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_' or '.'))
            || status is not ("active" or "retired"))
        {
            return false;
        }

        var material = DecodeBase64Url(RequiredString(element, "k"));
        if (material.Length != 32)
        {
            CryptographicOperations.ZeroMemory(material);
            return false;
        }

        key = new(keyId, material, status == "active");
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

    private static byte[] DecodeBase64Url(string value)
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
        if (!string.Equals(EncodeBase64Url(decoded), value, StringComparison.Ordinal))
        {
            CryptographicOperations.ZeroMemory(decoded);
            throw new FormatException();
        }

        return decoded;
    }

    private static string EncodeBase64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static void DisposeKeys(Dictionary<string, AtprotoSessionEncryptionKey> keys)
    {
        foreach (var key in keys.Values)
        {
            key.Dispose();
        }

        keys.Clear();
    }
}

internal sealed class AtprotoSessionEncryptionKey(string keyId, byte[] key, bool isActive) : IDisposable
{
    public string KeyId { get; } = keyId;
    public byte[] Key { get; } = key;
    public bool IsActive { get; } = isActive;
    public void Dispose() => CryptographicOperations.ZeroMemory(Key);
}

internal sealed record AtprotoProtectedSession(byte[] Ciphertext, string EncryptionKeyId);
internal sealed record AtprotoUnprotectedSession(OAuthSessionData Session, bool NeedsRewrite);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = false,
    WriteIndented = false,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(OAuthSessionData))]
internal partial class AtprotoOAuthSessionJsonContext : JsonSerializerContext;
