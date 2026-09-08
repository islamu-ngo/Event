using Explore.Domain.Interfaces;
using Explore.Domain.Services.Registration;

namespace Explore.Domain;

public sealed class RegistrationFormSection : ITenantEntity, IAuditableEntity, ISoftDeletable, IConcurrencyAware
{
    private readonly List<RegistrationFormField> _fields = [];

    private RegistrationFormSection()
    {
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; set; }
    public Guid EventId { get; private set; }
    public Guid RegistrationFormId { get; private set; }
    public Guid RegistrationFormVersionId { get; private set; }
    public int Ordinal { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public IReadOnlyCollection<RegistrationFormField> Fields => _fields.AsReadOnly();
    public Guid ConcurrencyStamp { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }

    public static RegistrationFormSection Create(
        Guid id,
        RegistrationFormVersion version,
        int ordinal,
        string title,
        DateTime createdAt)
    {
        ArgumentNullException.ThrowIfNull(version);
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Section identifier is required.", nameof(id));
        }

        if (ordinal <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ordinal));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        FormVersionRules.RequireUtc(createdAt, nameof(createdAt));
        return new RegistrationFormSection
        {
            Id = id,
            TenantId = version.TenantId,
            EventId = version.EventId,
            RegistrationFormId = version.RegistrationFormId,
            RegistrationFormVersionId = version.Id,
            Ordinal = ordinal,
            Title = title.Trim(),
            CreatedAt = createdAt
        };
    }

    internal void Rename(string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        Title = title.Trim();
    }

    internal void Update(int ordinal, string title)
    {
        if (ordinal <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ordinal));
        }

        Rename(title);
        Ordinal = ordinal;
    }

    internal void Reorder(int ordinal)
    {
        if (ordinal <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ordinal));
        }

        Ordinal = ordinal;
    }

    internal void Remove(DateTime removedAt)
    {
        FormVersionRules.RequireUtc(removedAt, nameof(removedAt));
        IsDeleted = true;
        DeletedAt = removedAt;
    }

    internal void AddField(RegistrationFormField field)
    {
        ArgumentNullException.ThrowIfNull(field);
        if (field.RegistrationFormSectionId != Id || field.RegistrationFormVersionId != RegistrationFormVersionId ||
            field.TenantId != TenantId || field.EventId != EventId)
        {
            throw new ArgumentException("Field must belong to this section, version, event, and tenant.", nameof(field));
        }

        if (_fields.Any(existing => existing.Id == field.Id || existing.Ordinal == field.Ordinal ||
                existing.Namespace == field.Namespace && existing.Key == field.Key))
        {
            throw new ArgumentException("Field identity, machine key, and ordinal must be unique within the section.", nameof(field));
        }

        _fields.Add(field);
    }

    internal RegistrationFormSection CloneTo(Guid versionId)
    {
        RegistrationFormSection clone = new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = TenantId,
            EventId = EventId,
            RegistrationFormId = RegistrationFormId,
            RegistrationFormVersionId = versionId,
            Ordinal = Ordinal,
            Title = Title,
            CreatedAt = CreatedAt
        };
        foreach (RegistrationFormField field in _fields.Where(field => !field.IsDeleted))
        {
            clone._fields.Add(field.CloneTo(versionId, clone.Id));
        }

        return clone;
    }

    internal RegistrationFormSection CloneTo(RegistrationFormVersion version)
    {
        RegistrationFormSection clone = Create(Guid.CreateVersion7(), version, Ordinal, Title, CreatedAt);
        foreach (RegistrationFormField field in _fields.Where(field => !field.IsDeleted))
        {
            clone._fields.Add(field.CloneTo(version, clone.Id));
        }

        return clone;
    }
}
