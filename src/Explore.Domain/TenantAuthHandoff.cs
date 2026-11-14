using System.Text;

namespace Explore.Domain;

/// <summary>A short-lived protected ticket grant; storage consumption supplies the single-use boundary.</summary>
public sealed class TenantAuthHandoff
{
    public const int MaximumProtectedTicketBytes = 64 * 1024;
    public const long MaximumLifetimeMilliseconds = 120_000;

    private TenantAuthHandoff() { }

    public Guid Id { get; private set; }
    public Guid OwnerUserId { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid FlowId { get; private set; }
    public string SourceSessionDigest { get; private set; } = string.Empty;
    public string SourceRevisionDigest { get; private set; } = string.Empty;
    public string CodeDigest { get; private set; } = string.Empty;
    public string BrowserProofDigest { get; private set; } = string.Empty;
    public string RoutingDigest { get; private set; } = string.Empty;
    public string SourceOrigin { get; private set; } = string.Empty;
    public string DestinationOrigin { get; private set; } = string.Empty;
    public string LocalPath { get; private set; } = string.Empty;
    public string ProtectedTicket { get; private set; } = string.Empty;
    public long CreatedAtUnixMilliseconds { get; private set; }
    public long ExpiresAtUnixMilliseconds { get; private set; }

    public static TenantAuthHandoff Create(TenantAuthHandoffBinding binding, string codeDigest,
        string protectedTicket, long createdAtUnixMilliseconds, long expiresAtUnixMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(binding);
        if (binding.OwnerUserId == Guid.Empty || binding.TenantId == Guid.Empty || binding.FlowId == Guid.Empty)
            throw new ArgumentException("The original subject, target and flow are required.", nameof(binding));
        RequireDigest(binding.SourceSessionDigest);
        RequireDigest(binding.SourceRevisionDigest);
        RequireDigest(binding.BrowserProofDigest);
        RequireDigest(binding.RoutingDigest);
        RequireDigest(codeDigest);
        string source = RequireOrigin(binding.SourceOrigin);
        string destination = RequireOrigin(binding.DestinationOrigin);
        if (source == destination)
            throw new ArgumentException("Same-origin authentication does not require a ticket transfer.", nameof(binding));
        if (string.IsNullOrEmpty(binding.LocalPath) || binding.LocalPath.Length > 2048
            || binding.LocalPath[0] != '/' || binding.LocalPath.StartsWith("//", StringComparison.Ordinal)
            || binding.LocalPath.Any(character => char.IsControl(character) || character == '\\')
            || binding.LocalPath.Contains("%2f", StringComparison.OrdinalIgnoreCase)
            || binding.LocalPath.Contains("%5c", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("A bounded local destination path is required.", nameof(binding));
        if (string.IsNullOrWhiteSpace(protectedTicket)
            || Encoding.UTF8.GetByteCount(protectedTicket) > MaximumProtectedTicketBytes)
            throw new ArgumentException("A bounded protected ticket is required.", nameof(protectedTicket));
        if (createdAtUnixMilliseconds <= 0 || expiresAtUnixMilliseconds <= createdAtUnixMilliseconds
            || expiresAtUnixMilliseconds - createdAtUnixMilliseconds > MaximumLifetimeMilliseconds)
            throw new ArgumentOutOfRangeException(nameof(expiresAtUnixMilliseconds));
        return new()
        {
            Id = Guid.CreateVersion7(), OwnerUserId = binding.OwnerUserId, TenantId = binding.TenantId,
            FlowId = binding.FlowId, SourceSessionDigest = binding.SourceSessionDigest,
            SourceRevisionDigest = binding.SourceRevisionDigest, CodeDigest = codeDigest,
            BrowserProofDigest = binding.BrowserProofDigest, RoutingDigest = binding.RoutingDigest,
            SourceOrigin = source, DestinationOrigin = destination, LocalPath = binding.LocalPath,
            ProtectedTicket = protectedTicket, CreatedAtUnixMilliseconds = createdAtUnixMilliseconds,
            ExpiresAtUnixMilliseconds = expiresAtUnixMilliseconds
        };
    }

    private static void RequireDigest(string value)
    {
        if (value is null || value.Length != 64
            || value.Any(character => character is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
            throw new ArgumentException("A lowercase hexadecimal SHA-256 binding is required.", nameof(value));
    }

    private static string RequireOrigin(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var origin) || origin.Scheme != Uri.UriSchemeHttps
            || origin.AbsolutePath != "/" || !string.IsNullOrEmpty(origin.UserInfo)
            || !string.IsNullOrEmpty(origin.Query) || !string.IsNullOrEmpty(origin.Fragment))
            throw new ArgumentException("An exact HTTPS origin is required.", nameof(value));
        return origin.GetLeftPart(UriPartial.Authority) + "/";
    }

    public override string ToString() => nameof(TenantAuthHandoff);
}

public sealed record TenantAuthHandoffBinding
{
    public required Guid OwnerUserId { get; init; }
    public required Guid TenantId { get; init; }
    public required Guid FlowId { get; init; }
    public required string SourceSessionDigest { get; init; }
    public required string SourceRevisionDigest { get; init; }
    public required string BrowserProofDigest { get; init; }
    public required string RoutingDigest { get; init; }
    public required string SourceOrigin { get; init; }
    public required string DestinationOrigin { get; init; }
    public required string LocalPath { get; init; }

    public override string ToString() => nameof(TenantAuthHandoffBinding);
}
