using Explore.Domain.Interfaces;
using Explore.Domain.Services.Registration;

namespace Explore.Domain;

public sealed class RegistrationForm : ITenantEntity, IAuditableEntity, ISoftDeletable, IConcurrencyAware
{
    private readonly List<RegistrationFormVersion> _versions = [];

    private RegistrationForm()
    {
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; set; }
    public Guid EventId { get; private set; }
    public string Namespace { get; private set; } = string.Empty;
    public string Key { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public IReadOnlyCollection<RegistrationFormVersion> Versions => _versions.AsReadOnly();
    public Guid ConcurrencyStamp { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }

    public static RegistrationForm Create(
        Guid tenantId,
        Guid eventId,
        string @namespace,
        string key,
        string name,
        DateTime createdAt) => Create(Guid.CreateVersion7(), tenantId, eventId, @namespace, key, name, createdAt);

    public static RegistrationForm Create(
        Guid id,
        Guid tenantId,
        Guid eventId,
        string @namespace,
        string key,
        string name,
        DateTime createdAt)
    {
        if (id == Guid.Empty || tenantId == Guid.Empty || eventId == Guid.Empty)
        {
            throw new ArgumentException("Form, tenant, and event identifiers are required.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        FormVersionRules.RequireUtc(createdAt, nameof(createdAt));

        return new RegistrationForm
        {
            Id = id,
            TenantId = tenantId,
            EventId = eventId,
            Namespace = FormVersionRules.NormalizeNamespace(@namespace),
            Key = FormVersionRules.NormalizeKey(key),
            Name = name.Trim(),
            CreatedAt = createdAt
        };
    }

    public void AddVersion(RegistrationFormVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);
        if (version.RegistrationFormId != Id || version.TenantId != TenantId || version.EventId != EventId)
        {
            throw new ArgumentException("Version must belong to this form, event, and tenant.", nameof(version));
        }

        if (_versions.Any(existing => existing.Id == version.Id || existing.Version == version.Version))
        {
            throw new ArgumentException("Version identity and number must be unique within the form.", nameof(version));
        }

        _versions.Add(version);
    }
}
