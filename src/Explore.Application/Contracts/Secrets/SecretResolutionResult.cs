namespace Explore.Application.Contracts.Secrets;

using Explore.Domain.Enums;

/// <summary>
/// Describes one secret-resolution attempt without carrying provider diagnostics or
/// coordinates. Only <see cref="Resolved"/> carries secret material.
/// </summary>
public sealed record SecretResolutionResult
{
    private SecretResolutionResult(SecretResolutionStatus status, ResolvedSecret? secret)
    {
        Status = status;
        Secret = secret;
    }

    public SecretResolutionStatus Status { get; }

    public ResolvedSecret? Secret { get; }

    public bool IsResolved => Status == SecretResolutionStatus.Resolved;

    public string? Value => Secret?.Value;

    public SecretSourceType? Source => Secret?.Source;

    public SecretScope? Scope => Secret?.Scope;

    public Guid? ScopeId => Secret?.ScopeId;

    public static SecretResolutionResult Resolved(ResolvedSecret secret) =>
        new(SecretResolutionStatus.Resolved, secret ?? throw new ArgumentNullException(nameof(secret)));

    public static SecretResolutionResult Unconfigured { get; } =
        new(SecretResolutionStatus.Unconfigured, secret: null);

    public static SecretResolutionResult Unavailable { get; } =
        new(SecretResolutionStatus.Unavailable, secret: null);

    public static SecretResolutionResult Unauthorized { get; } =
        new(SecretResolutionStatus.Unauthorized, secret: null);

    public static SecretResolutionResult Invalid { get; } =
        new(SecretResolutionStatus.Invalid, secret: null);

    public override string ToString() => $"{nameof(SecretResolutionResult)} {{ Status = {Status} }}";
}

public enum SecretResolutionStatus
{
    Resolved,
    Unconfigured,
    Unavailable,
    Unauthorized,
    Invalid,
}
