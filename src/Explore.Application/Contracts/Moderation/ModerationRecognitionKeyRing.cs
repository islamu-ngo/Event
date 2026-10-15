using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Explore.Application.Contracts.Secrets;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Application.Contracts.Moderation;

/// <summary>
/// Owns an ephemeral version-one ring snapshot. JSON follows native kid/k/status conventions:
/// version, trustDomainId, ringGeneration, issuedAt, validUntil, keys; each key has kid,
/// generation, canonical unpadded base64url k and status active/readable. Exactly one active
/// key writes; all listed keys must be readable. UTC timestamps use the round-trip O format.
/// Unknown or duplicate properties, unsupported versions and partial rings fail closed.
/// </summary>
/// <remarks>
/// Material remains in the selected authority and process memory, never ledger metadata.
/// Currentness ends at the earlier of validUntil and the resolver's five-minute observation
/// bound. Borrowing copies material into the existing disposable key primitive; disposing
/// the snapshot cannot revoke a copy already borrowed. Callers must recheck native authority
/// at their terminal checkpoint and dispose each borrow promptly. This type cannot certify
/// replica acknowledgement, backup safety, catastrophic recovery or key retirement.
/// </remarks>
public sealed class ModerationRecognitionKeyRing : IDisposable
{
    /// <summary>
    /// Parent integration must catalogue this instance-only, server-only, non-bootstrap secret
    /// in SecretDefinitionRegistry; resolution has no private selection or fallback mechanism.
    /// </summary>
    public const string SecretSettingKey = Explore.Domain.Secrets.SecretDefinitionRegistry.Keys.ModerationRecognitionKeyRing;
    public const int CurrentSerializationVersion = 1;
    public const int MaximumSerializedBytes = 64 * 1024;
    public const int MaximumKeys = 16;
    private static readonly TimeSpan MaximumAuthorityAge = TimeSpan.FromMinutes(5);
    private readonly Dictionary<string, Entry> entries;
    private readonly TimeProvider timeProvider;
    private readonly object gate = new();
    private bool disposed;

    private ModerationRecognitionKeyRing(
        Guid trustDomainId, long ringGeneration, DateTimeOffset issuedAt, DateTimeOffset validUntil,
        ResolvedSecret secret, DateTimeOffset observedAt, DateTimeOffset currentUntil,
        Dictionary<string, Entry> entries, TimeProvider timeProvider)
    {
        TrustDomainId = trustDomainId;
        RingGeneration = ringGeneration;
        IssuedAt = issuedAt;
        ValidUntil = validUntil;
        ResolvedAt = secret.ResolvedAt;
        ObservedAt = observedAt;
        CurrentUntil = currentUntil;
        Source = secret.Source;
        BindingRevision = secret.BindingRevision;
        this.entries = entries;
        this.timeProvider = timeProvider;
        Keys = entries.Values.Select(entry => entry.Metadata).ToImmutableArray();
    }

    public int SerializationVersion => CurrentSerializationVersion;
    public Guid TrustDomainId { get; }
    public long RingGeneration { get; }
    public DateTimeOffset IssuedAt { get; }
    public DateTimeOffset ValidUntil { get; }
    public DateTimeOffset ResolvedAt { get; }
    public DateTimeOffset ObservedAt { get; }
    public DateTimeOffset CurrentUntil { get; }
    public SecretSourceType Source { get; }
    public string BindingRevision { get; }
    public ImmutableArray<ModerationRecognitionKeyRingKeyMetadata> Keys { get; }

    /// <summary>
    /// Parses only an exact instance resolution from the approved native resolver boundary.
    /// The factory copies decoded material into owned storage and retains no JSON payload.
    /// Every failure disposes all partially decoded material and exposes only a bounded code.
    /// </summary>
    public static ModerationRecognitionKeyRing Parse(
        ResolvedSecret secret, Guid expectedTrustDomainId, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(secret);
        ArgumentNullException.ThrowIfNull(timeProvider);
        var parsed = new Dictionary<string, Entry>(StringComparer.Ordinal);
        var transferred = false;
        try
        {
            ModerationHostBinding.RequireUuidV7(expectedTrustDomainId, nameof(expectedTrustDomainId));
            var now = timeProvider.GetUtcNow();
            if (secret.SettingKey != SecretSettingKey || secret.Scope != SecretScope.Instance
                || secret.ScopeId is not null
                || secret.Source is not (SecretSourceType.EnvironmentVariable or SecretSourceType.Infisical)
                || string.IsNullOrWhiteSpace(secret.BindingRevision)
                || secret.ResolvedAt.Offset != TimeSpan.Zero
                || secret.ResolvedAt > now
                || secret.ResolvedAt > DateTimeOffset.MaxValue - MaximumAuthorityAge
                || string.IsNullOrWhiteSpace(secret.Value)
                || secret.Value.Length > MaximumSerializedBytes
                || Encoding.UTF8.GetByteCount(secret.Value) > MaximumSerializedBytes)
                throw Invalid();

            using var document = JsonDocument.Parse(secret.Value, new JsonDocumentOptions
            {
                AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow, MaxDepth = 4
            });
            var root = document.RootElement;
            RequireProperties(root, ["version", "trustDomainId", "ringGeneration", "issuedAt", "validUntil", "keys"]);
            if (!root.GetProperty("version").TryGetInt32(out var version) || version != CurrentSerializationVersion)
                throw Invalid();
            var trustText = RequiredString(root, "trustDomainId");
            if (!Guid.TryParseExact(trustText, "D", out var trustDomainId)
                || trustDomainId != expectedTrustDomainId
                || trustText != trustDomainId.ToString("D"))
                throw Invalid();

            var ringGeneration = RequiredGeneration(root, "ringGeneration");
            var issuedAt = RequiredTimestamp(root, "issuedAt");
            var validUntil = RequiredTimestamp(root, "validUntil");
            var authorityUntil = secret.ResolvedAt + MaximumAuthorityAge;
            var currentUntil = validUntil < authorityUntil ? validUntil : authorityUntil;
            if (issuedAt > secret.ResolvedAt || validUntil == DateTimeOffset.MaxValue
                || validUntil <= issuedAt || now >= currentUntil)
                throw Invalid();
            var keys = root.GetProperty("keys");
            if (keys.ValueKind != JsonValueKind.Array || keys.GetArrayLength() is < 1 or > MaximumKeys)
                throw Invalid();

            var generations = new HashSet<long>();
            var writingCount = 0;
            foreach (var element in keys.EnumerateArray())
            {
                RequireProperties(element, ["kid", "generation", "k", "status"]);
                var keyId = RequiredString(element, "kid");
                var generation = RequiredGeneration(element, "generation");
                var status = RequiredString(element, "status");
                if (status is not ("active" or "readable")
                    || parsed.ContainsKey(keyId) || !generations.Add(generation))
                    throw Invalid();
                var material = DecodeMaterial(RequiredString(element, "k"));
                try
                {
                    using var key = new ModerationRecognitionKey(trustDomainId, keyId, generation, material);
                    var metadata = new ModerationRecognitionKeyRingKeyMetadata(
                        keyId, generation, status == "active", key.VerificationTag);
                    parsed.Add(keyId, new(metadata, material.ToArray()));
                    if (metadata.IsWriting) writingCount++;
                }
                finally { CryptographicOperations.ZeroMemory(material); }
            }
            if (writingCount != 1) throw Invalid();

            var snapshot = new ModerationRecognitionKeyRing(trustDomainId, ringGeneration, issuedAt, validUntil,
                secret, now, currentUntil, parsed, timeProvider);
            transferred = true;
            return snapshot;
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException
            or FormatException or OverflowException or InvalidOperationException)
        {
            // No payload, coordinates, identifiers or underlying provider/parser diagnostics escape.
            throw Invalid();
        }
        finally
        {
            if (!transferred)
                foreach (var entry in parsed.Values) CryptographicOperations.ZeroMemory(entry.Material);
        }
    }

    public ModerationRecognitionKey BorrowWritingKey(Guid trustDomainId, string keyId, long keyGeneration) =>
        Borrow(trustDomainId, keyId, keyGeneration, writing: true);

    public ModerationRecognitionKey BorrowReadableKey(Guid trustDomainId, string keyId, long keyGeneration) =>
        Borrow(trustDomainId, keyId, keyGeneration, writing: false);

    private ModerationRecognitionKey Borrow(Guid trustDomainId, string keyId, long keyGeneration, bool writing)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var now = timeProvider.GetUtcNow();
            if (now < ObservedAt || now >= CurrentUntil)
                throw new InvalidOperationException("moderation_recognition_ring_stale");
            if (trustDomainId != TrustDomainId || keyId is null
                || !entries.TryGetValue(keyId, out var entry) || entry.Metadata.KeyGeneration != keyGeneration
                || (writing && !entry.Metadata.IsWriting))
                throw new InvalidOperationException("moderation_recognition_key_unavailable");
            return new ModerationRecognitionKey(TrustDomainId, keyId, keyGeneration, entry.Material);
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            foreach (var entry in entries.Values) CryptographicOperations.ZeroMemory(entry.Material);
            disposed = true;
        }
    }

    public override string ToString() => nameof(ModerationRecognitionKeyRing);

    private static void RequireProperties(JsonElement element, string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object) throw Invalid();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
            if (!names.Contains(property.Name, StringComparer.Ordinal) || !seen.Add(property.Name)) throw Invalid();
        if (seen.Count != names.Length) throw Invalid();
    }

    private static string RequiredString(JsonElement element, string name) =>
        element.GetProperty(name).ValueKind == JsonValueKind.String
            ? element.GetProperty(name).GetString() ?? throw Invalid() : throw Invalid();

    private static long RequiredGeneration(JsonElement element, string name)
    {
        if (!element.GetProperty(name).TryGetInt64(out var generation) || generation is < 1 or long.MaxValue)
            throw Invalid();
        return generation;
    }

    private static DateTimeOffset RequiredTimestamp(JsonElement element, string name)
    {
        var value = RequiredString(element, name);
        if (!DateTimeOffset.TryParseExact(value, "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out var timestamp)
            || timestamp.Offset != TimeSpan.Zero || timestamp == DateTimeOffset.MinValue
            || value != timestamp.ToString("O", CultureInfo.InvariantCulture))
            throw Invalid();
        return timestamp;
    }

    private static byte[] DecodeMaterial(string value)
    {
        if (value.Length != 43 || value.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
            throw Invalid();
        var material = Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/') + "=");
        if (material.Length == 32
            && Convert.ToBase64String(material).TrimEnd('=').Replace('+', '-').Replace('/', '_') == value)
            return material;
        CryptographicOperations.ZeroMemory(material);
        throw Invalid();
    }

    private static InvalidOperationException Invalid() => new("moderation_recognition_ring_invalid");
    private sealed record Entry(ModerationRecognitionKeyRingKeyMetadata Metadata, byte[] Material);
}
