using System.Collections.Frozen;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Explore.Application.Configuration;
using Explore.Domain.Authentication;

namespace Explore.Persistence.Identity;

/// <summary>
/// A single startup capture of a deployment-owned, digest-pinned file. No partial
/// dataset is published, and an unavailable capture denies every new credential.
/// </summary>
internal sealed class LocalPasswordBlocklistSnapshot
{
    public const int MaximumBytes = 16 * 1024 * 1024;
    public const int MaximumEntries = 100_000;
    private readonly FrozenSet<string> _entries;

    private LocalPasswordBlocklistSnapshot(FrozenSet<string> entries, string? revision)
    {
        _entries = entries;
        Revision = revision;
    }

    public bool IsUsable => Revision is not null;
    public string? Revision { get; }
    public int Count => _entries.Count;

    public bool Allows(string? password) =>
        IsUsable && LocalPasswordPolicy.TryNormalizeNewCredential(password, out string normalized)
        && !_entries.Contains(normalized);

    public static LocalPasswordBlocklistSnapshot Load(LocalIdentityOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.PasswordBlocklistPath)
            || options.PasswordBlocklistSha256 is not { Length: 64 } digest
            || !digest.All(char.IsAsciiHexDigit))
            return Unavailable();
        try
        {
            using var file = new FileStream(options.PasswordBlocklistPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (file.Length is <= 0 or > MaximumBytes)
                return Unavailable();
            // The extra byte detects growth without allocating or parsing unbounded input.
            byte[] bytes = new byte[MaximumBytes + 1];
            int length = 0;
            while (length < bytes.Length)
            {
                int read = file.Read(bytes, length, bytes.Length - length);
                if (read == 0) break;
                length += read;
            }
            if (length is 0 or > MaximumBytes
                || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(bytes.AsSpan(0, length)), Convert.FromHexString(digest)))
                return Unavailable();
            var jsonOptions = new JsonSerializerOptions
            {
                AllowDuplicateProperties = false,
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
                MaxDepth = 4
            };
            Document? document = JsonSerializer.Deserialize<Document>(bytes.AsSpan(0, length), jsonOptions);
            if (document is null || document.Version != 1
                || !BoundedMetadata(document.Revision, 128)
                || !BoundedMetadata(document.Source, 2048)
                || !BoundedMetadata(document.License, 256)
                || document.Passwords is not { Length: >= 1 and <= MaximumEntries })
                return Unavailable();
            var entries = new HashSet<string>(StringComparer.Ordinal);
            foreach (string? password in document.Passwords)
            {
                if (!LocalPasswordPolicy.TryNormalizeForVerification(password, out string normalized)
                    || !entries.Add(normalized))
                    return Unavailable();
            }
            return new LocalPasswordBlocklistSnapshot(entries.ToFrozenSet(StringComparer.Ordinal), document.Revision);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            // Neither paths, corpus values nor parser text belong in public readiness diagnostics.
            return Unavailable();
        }
    }

    private static bool BoundedMetadata(string? value, int maximum) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maximum && !value.Any(char.IsControl);

    private static LocalPasswordBlocklistSnapshot Unavailable() =>
        new(Array.Empty<string>().ToFrozenSet(StringComparer.Ordinal), null);

    private sealed class Document
    {
        public Document() { }

        [JsonRequired]
        public int Version { get; init; }
        [JsonRequired]
        public string? Revision { get; init; }
        [JsonRequired]
        public string? Source { get; init; }
        [JsonRequired]
        public string? License { get; init; }
        [JsonRequired]
        public string?[]? Passwords { get; init; }
    }
}
