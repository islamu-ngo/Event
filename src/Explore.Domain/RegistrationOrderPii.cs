using Explore.Domain.Interfaces;

namespace Explore.Domain;

public sealed class RegistrationOrderPii : ITenantEntity, IAuditableEntity
{
    private RegistrationOrderPii()
    {
    }

    private RegistrationOrderPii(
        Guid registrationOrderId,
        Guid tenantId,
        string? contactName,
        string? email,
        string? phone,
        string? organizationName,
        int retentionPolicyId,
        DateTime createdAt)
    {
        RegistrationOrderId = registrationOrderId;
        TenantId = tenantId;
        ContactName = Normalize(contactName);
        Email = Normalize(email);
        NormalizedEmail = Email?.ToUpperInvariant();
        IsEmailVerified = false;
        Phone = Normalize(phone);
        OrganizationName = Normalize(organizationName);
        RetentionUntil = RegistrationRetentionDeadline.Resolve(retentionPolicyId, createdAt);
        CreatedAt = createdAt;
    }

    public Guid RegistrationOrderId { get; private set; }

    public Guid TenantId { get; set; }

    public RegistrationOrder? RegistrationOrder { get; private set; }

    public string? ContactName { get; private set; }

    public string? Email { get; private set; }

    public string? NormalizedEmail { get; private set; }

    public bool IsEmailVerified { get; private set; }

    public string? Phone { get; private set; }

    public string? OrganizationName { get; private set; }

    public DateTime? RetentionUntil { get; private set; }

    public DateTime CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public static RegistrationOrderPii Create(
        Guid registrationOrderId,
        Guid tenantId,
        string? contactName,
        string? email,
        string? phone,
        string? organizationName) => Create(
            registrationOrderId, tenantId, contactName, email, phone, organizationName,
            (int)Enums.RegistrationRetentionPolicyEnum.StandardOperational, DateTime.UtcNow);

    public static RegistrationOrderPii Create(
        Guid registrationOrderId,
        Guid tenantId,
        string? contactName,
        string? email,
        string? phone,
        string? organizationName,
        int retentionPolicyId,
        DateTime createdAt)
    {
        if (registrationOrderId == Guid.Empty || tenantId == Guid.Empty)
        {
            throw new ArgumentException("Registration order and tenant identifiers are required.");
        }

        return new RegistrationOrderPii(registrationOrderId, tenantId, contactName, email, phone, organizationName, retentionPolicyId, createdAt);
    }

    public static RegistrationOrderPii CreateFromVerifiedContact(
        Guid registrationOrderId,
        Guid tenantId,
        string? contactName,
        string email,
        string? phone,
        string? organizationName,
        string verifiedContactNormalizedEmail,
        int retentionPolicyId,
        DateTime createdAt)
    {
        RegistrationOrderPii pii = Create(registrationOrderId, tenantId, contactName, email, phone, organizationName, retentionPolicyId, createdAt);
        pii.MarkEmailVerified(verifiedContactNormalizedEmail);
        return pii;
    }

    public void Update(string? contactName, string? email, string? phone, string? organizationName, int retentionPolicyId, DateTime updatedAt)
    {
        ContactName = Normalize(contactName);
        string? previousNormalizedEmail = NormalizedEmail;
        Email = Normalize(email);
        NormalizedEmail = Email?.ToUpperInvariant();
        if (!string.Equals(previousNormalizedEmail, NormalizedEmail, StringComparison.Ordinal))
        {
            IsEmailVerified = false;
        }
        Phone = Normalize(phone);
        OrganizationName = Normalize(organizationName);
        RetentionUntil = RegistrationRetentionDeadline.Resolve(retentionPolicyId, updatedAt);
    }

    public void MarkEmailVerified(string verifiedNormalizedEmail)
    {
        if (string.IsNullOrWhiteSpace(verifiedNormalizedEmail) ||
            !string.Equals(NormalizedEmail, verifiedNormalizedEmail.Trim().ToUpperInvariant(), StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Verified contact email does not match the registration order email.");
        }

        IsEmailVerified = true;
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
