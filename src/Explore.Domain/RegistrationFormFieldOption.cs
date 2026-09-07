using Explore.Domain.Interfaces;
using Explore.Domain.Services.Registration;

namespace Explore.Domain;

public sealed class RegistrationFormFieldOption : ITenantEntity, IAuditableEntity, ISoftDeletable, IConcurrencyAware
{
    private RegistrationFormFieldOption()
    {
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; set; }
    public Guid EventId { get; private set; }
    public Guid RegistrationFormId { get; private set; }
    public Guid RegistrationFormVersionId { get; private set; }
    public Guid RegistrationFormSectionId { get; private set; }
    public Guid RegistrationFormFieldId { get; private set; }
    public int Ordinal { get; private set; }
    public string Key { get; private set; } = string.Empty;
    public string Label { get; private set; } = string.Empty;
    public DateTime? RetiredAt { get; private set; }
    public Guid ConcurrencyStamp { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }

    public static RegistrationFormFieldOption Create(
        Guid id,
        RegistrationFormField field,
        int ordinal,
        string key,
        string label,
        DateTime createdAt)
    {
        ArgumentNullException.ThrowIfNull(field);
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Option identifier is required.", nameof(id));
        }

        if (ordinal <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ordinal));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        FormVersionRules.RequireUtc(createdAt, nameof(createdAt));
        return new RegistrationFormFieldOption
        {
            Id = id,
            TenantId = field.TenantId,
            EventId = field.EventId,
            RegistrationFormId = field.RegistrationFormId,
            RegistrationFormVersionId = field.RegistrationFormVersionId,
            RegistrationFormSectionId = field.RegistrationFormSectionId,
            RegistrationFormFieldId = field.Id,
            Ordinal = ordinal,
            Key = FormVersionRules.NormalizeKey(key),
            Label = label.Trim(),
            CreatedAt = createdAt
        };
    }

    internal void Retire(DateTime retiredAt)
    {
        FormVersionRules.RequireUtc(retiredAt, nameof(retiredAt));
        RetiredAt ??= retiredAt;
    }

    internal void Update(int ordinal, string key, string label)
    {
        if (ordinal <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ordinal));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        Ordinal = ordinal;
        Key = FormVersionRules.NormalizeKey(key);
        Label = label.Trim();
    }

    internal RegistrationFormFieldOption CloneTo(Guid versionId, Guid sectionId, Guid fieldId) => new()
    {
        Id = Guid.CreateVersion7(),
        TenantId = TenantId,
        EventId = EventId,
        RegistrationFormId = RegistrationFormId,
        RegistrationFormVersionId = versionId,
        RegistrationFormSectionId = sectionId,
        RegistrationFormFieldId = fieldId,
        Ordinal = Ordinal,
        Key = Key,
        Label = Label,
        RetiredAt = RetiredAt,
        CreatedAt = CreatedAt
    };

    internal RegistrationFormFieldOption CloneTo(RegistrationFormVersion version, Guid sectionId, Guid fieldId) => new()
    {
        Id = Guid.CreateVersion7(),
        TenantId = version.TenantId,
        EventId = version.EventId,
        RegistrationFormId = version.RegistrationFormId,
        RegistrationFormVersionId = version.Id,
        RegistrationFormSectionId = sectionId,
        RegistrationFormFieldId = fieldId,
        Ordinal = Ordinal,
        Key = Key,
        Label = Label,
        RetiredAt = RetiredAt,
        CreatedAt = CreatedAt
    };
}
