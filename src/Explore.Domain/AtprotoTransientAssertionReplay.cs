// ABOUTME: Models an immutable instance-wide replay claim for a transient-service assertion.
// ABOUTME: Derives a lowercase namespaced SHA-256 digest and retains it until final acceptance expiry.

using System.Security.Cryptography;
using System.Text;
namespace Explore.Domain;

public sealed class AtprotoTransientAssertionReplay
{
    private const string DigestNamespace = "atproto-transient-service:";
    private const int MaximumAssertionIdLength = 128;

    private AtprotoTransientAssertionReplay() { }
    public Guid Id { get; private set; }
    public string AssertionDigest { get; private set; } = string.Empty;
    public long ExpiresAtUnixMilliseconds { get; private set; }

    public static AtprotoTransientAssertionReplay CreateFromAssertionId(
        string assertionId,
        long expiresAtUnixMilliseconds)
    {
        if (string.IsNullOrWhiteSpace(assertionId) || assertionId.Length > MaximumAssertionIdLength)
            throw new ArgumentException("A bounded assertion identifier is required.", nameof(assertionId));
        if (expiresAtUnixMilliseconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(expiresAtUnixMilliseconds));

        byte[] input = Encoding.UTF8.GetBytes(DigestNamespace + assertionId);
        return new()
        {
            Id = Guid.CreateVersion7(),
            AssertionDigest = Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant(),
            ExpiresAtUnixMilliseconds = expiresAtUnixMilliseconds,
        };
    }
}
