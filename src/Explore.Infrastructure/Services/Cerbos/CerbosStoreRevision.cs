using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Explore.Infrastructure.Services;

/// <summary>
/// Computes a deterministic revision token for the set of policies a Cerbos store is serving.
/// <para>
/// Cerbos has no store-wide revision endpoint, but it returns a content hash per policy. Folding those
/// hashes in a stable order produces a value with the property that matters: it changes if any policy is
/// added, removed, or edited in place, and it does not change for a store that is genuinely unchanged.
/// </para>
/// <para>
/// The token is comparable only against other observations of the same Cerbos version. The underlying
/// per-policy hash is a Cerbos implementation detail, so a PDP upgrade may shift every token at once
/// without any policy having changed. That is why it is reported as an opaque revision to compare, never
/// as a checksum to validate against the app-owned package hash.
/// </para>
/// </summary>
internal static class CerbosStoreRevision
{
    /// <summary>
    /// Separates identifier from hash inside one entry (ASCII unit separator), and entries from each
    /// other (ASCII record separator). Neither can occur in a Cerbos policy identifier or hash, so no
    /// pair of distinct policy sets can fold to the same input string.
    /// </summary>
    private const char FieldSeparator = (char)0x1F;

    private const char EntrySeparator = (char)0x1E;

    /// <summary>Characters of the SHA-256 digest kept. Bounded so the value stays log- and label-safe.</summary>
    private const int TokenLength = 16;

    /// <summary>
    /// Folds <paramref name="policies"/> into a revision token, or returns <c>null</c> when the input
    /// cannot identify a policy set — an empty store, or entries missing the identity fields the fold
    /// needs. A <c>null</c> return is an uncertainty signal and must not be treated as "no changes".
    /// </summary>
    public static string? Compute(IEnumerable<(string? StoreIdentifier, string? Hash)> policies)
    {
        var entries = policies
            .Where(policy => !string.IsNullOrWhiteSpace(policy.StoreIdentifier)
                && !string.IsNullOrWhiteSpace(policy.Hash))
            .Select(policy => (Identifier: policy.StoreIdentifier!, Hash: policy.Hash!))
            .OrderBy(policy => policy.Identifier, StringComparer.Ordinal)
            .ToArray();

        if (entries.Length == 0)
            return null;

        var builder = new StringBuilder();
        builder.Append(entries.Length.ToString(CultureInfo.InvariantCulture)).Append(EntrySeparator);

        foreach (var (identifier, hash) in entries)
        {
            builder.Append(identifier).Append(FieldSeparator).Append(hash).Append(EntrySeparator);
        }

        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexString(digest)[..TokenLength].ToLowerInvariant();
    }
}
