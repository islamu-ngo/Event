using Explore.Domain.Interfaces;
using Explore.Domain.Enums;
using Explore.Domain.Services.Registration;

namespace Explore.Domain;

public sealed class RegistrationFormTemplate : IAuditableEntity, ISoftDeletable, IConcurrencyAware
{
    private RegistrationFormTemplate()
    {
    }

    public Guid Id { get; private set; }
    public Guid? TenantId { get; private set; }
    public bool IsPlatformOwned => TenantId is null;
    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public string Category { get; private set; } = string.Empty;
    public string? PackKey { get; private set; }
    public Guid SourceEventId { get; private set; }
    public Guid SourceRegistrationFormId { get; private set; }
    public Guid SourceRegistrationFormVersionId { get; private set; }
    public Guid ConcurrencyStamp { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }

    public static RegistrationFormTemplate Create(
        Guid? tenantId,
        string name,
        string description,
        string category,
        string? packKey,
        RegistrationFormVersion sourceVersion,
        DateTime createdAt)
    {
        ArgumentNullException.ThrowIfNull(sourceVersion);
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("Tenant template owner cannot be empty.", nameof(tenantId));
        }

        FormVersionRules.RequireUtc(createdAt, nameof(createdAt));
        if (sourceVersion.StatusId != (int)RegistrationFormStatusEnum.Published)
        {
            throw new InvalidOperationException("Registration form templates must point at a published source version.");
        }

        return new RegistrationFormTemplate
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            Name = Bound(name, 200, nameof(name)),
            Description = Bound(description, 1000, nameof(description)),
            Category = Bound(category, 100, nameof(category)),
            PackKey = string.IsNullOrWhiteSpace(packKey) ? null : FormVersionRules.NormalizeKey(packKey),
            SourceEventId = sourceVersion.EventId,
            SourceRegistrationFormId = sourceVersion.RegistrationFormId,
            SourceRegistrationFormVersionId = sourceVersion.Id,
            CreatedAt = createdAt
        };
    }

    private static string Bound(string value, int maxLength, string parameterName)
    {
        string normalized = value?.Trim() ?? string.Empty;
        return normalized.Length is > 0 && normalized.Length <= maxLength && !normalized.Any(char.IsControl)
            ? normalized
            : throw new ArgumentException($"{parameterName} must be non-blank and at most {maxLength} characters.", parameterName);
    }
}
