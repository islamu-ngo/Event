namespace Explore.Application.Authentication;

/// <summary>Immutable identity facts constructed only by the validated-principal adapter.</summary>
public sealed record IdentityAuthorityEvidence
{
    internal IdentityAuthorityEvidence(
        ProviderAccountKey accountKey, string? issuer, string email, bool emailVerified)
    {
        AccountKey = accountKey;
        Issuer = issuer;
        Email = email;
        EmailVerified = emailVerified;
    }

    public ProviderAccountKey AccountKey { get; }
    public string? Issuer { get; }
    public string Email { get; }
    public bool EmailVerified { get; }

    public override string ToString() => nameof(IdentityAuthorityEvidence);
}
