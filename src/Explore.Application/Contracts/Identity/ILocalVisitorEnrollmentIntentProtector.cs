namespace Explore.Application.Contracts.Identity;

/// <summary>
/// Local enrollment correlation data only, never authentication or enrollment authority.
/// Consumers must compare every field against the authoritative tenant-bound primary
/// receipt and verify the digest against the original browser proof.
/// </summary>
public sealed record LocalVisitorEnrollmentIntent(
    Guid OperationId, Guid TenantId, string ProofDigest, DateTime ExpiresAt)
{
    public override string ToString() => nameof(LocalVisitorEnrollmentIntent);
}

/// <summary>
/// Protects local enrollment bindings without including raw proof, passwords,
/// bearer credentials, sessions, or user information.
/// </summary>
public interface ILocalVisitorEnrollmentIntentProtector
{
    string Protect(LocalVisitorEnrollmentIntent intent);
    LocalVisitorEnrollmentIntent? Unprotect(string protectedIntent);
}
