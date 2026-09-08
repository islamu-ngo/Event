namespace Explore.Domain;

public sealed class WebhookProviderBindingVerificationStateLookup
{
    public int Id { get; set; }
    public required string MasterCode { get; set; }
    public required string FullName { get; set; }
    public string? Description { get; set; }
}

public enum WebhookProviderBindingVerificationState
{
    LegacyUnverified = 1,
    Pending = 2,
    Verified = 3,
    Rejected = 4,
    Revoked = 5
}
