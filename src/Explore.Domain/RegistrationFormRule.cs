using Explore.Domain.Interfaces;
using Explore.Domain.Services.Registration;

namespace Explore.Domain;

public sealed class RegistrationFormRule : ITenantEntity, IAuditableEntity, ISoftDeletable, IConcurrencyAware
{
    private RegistrationFormRule()
    {
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; set; }
    public Guid EventId { get; private set; }
    public Guid RegistrationFormId { get; private set; }
    public Guid RegistrationFormVersionId { get; private set; }
    public int Ordinal { get; private set; }
    public string TargetNamespace { get; private set; } = string.Empty;
    public string TargetKey { get; private set; } = string.Empty;
    public FormFieldReference Target => new(TargetNamespace, TargetKey);
    public RegistrationFormRuleEffect Effect { get; private set; }
    public FormCondition Condition { get; private set; } = null!;
    public Guid ConcurrencyStamp { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }

    public static RegistrationFormRule Create(
        Guid id,
        RegistrationFormVersion version,
        int ordinal,
        FormFieldReference target,
        RegistrationFormRuleEffect effect,
        FormCondition condition,
        DateTime createdAt)
    {
        ArgumentNullException.ThrowIfNull(version);
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Rule identifier is required.", nameof(id));
        }

        if (ordinal <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ordinal));
        }

        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(condition);
        if (!Enum.IsDefined(effect))
        {
            throw new ArgumentOutOfRangeException(nameof(effect));
        }

        FormVersionRules.RequireUtc(createdAt, nameof(createdAt));
        return new RegistrationFormRule
        {
            Id = id,
            TenantId = version.TenantId,
            EventId = version.EventId,
            RegistrationFormId = version.RegistrationFormId,
            RegistrationFormVersionId = version.Id,
            Ordinal = ordinal,
            TargetNamespace = target.Namespace,
            TargetKey = target.Key,
            Effect = effect,
            Condition = condition,
            CreatedAt = createdAt
        };
    }

    internal void Remove(DateTime removedAt)
    {
        FormVersionRules.RequireUtc(removedAt, nameof(removedAt));
        IsDeleted = true;
        DeletedAt = removedAt;
    }

    internal void Update(
        int ordinal,
        FormFieldReference target,
        RegistrationFormRuleEffect effect,
        FormCondition condition)
    {
        if (ordinal <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ordinal));
        }

        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(condition);
        if (!Enum.IsDefined(effect))
        {
            throw new ArgumentOutOfRangeException(nameof(effect));
        }

        Ordinal = ordinal;
        TargetNamespace = target.Namespace;
        TargetKey = target.Key;
        Effect = effect;
        Condition = condition;
    }

    internal void Reorder(int ordinal)
    {
        if (ordinal <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ordinal));
        }

        Ordinal = ordinal;
    }

    internal RegistrationFormRule CloneTo(Guid versionId) => new()
    {
        Id = Guid.CreateVersion7(),
        TenantId = TenantId,
        EventId = EventId,
        RegistrationFormId = RegistrationFormId,
        RegistrationFormVersionId = versionId,
        Ordinal = Ordinal,
        TargetNamespace = TargetNamespace,
        TargetKey = TargetKey,
        Effect = Effect,
        Condition = Condition,
        CreatedAt = CreatedAt
    };

    internal RegistrationFormRule CloneTo(RegistrationFormVersion version) => new()
    {
        Id = Guid.CreateVersion7(),
        TenantId = version.TenantId,
        EventId = version.EventId,
        RegistrationFormId = version.RegistrationFormId,
        RegistrationFormVersionId = version.Id,
        Ordinal = Ordinal,
        TargetNamespace = TargetNamespace,
        TargetKey = TargetKey,
        Effect = Effect,
        Condition = Condition,
        CreatedAt = CreatedAt
    };
}

public enum RegistrationFormRuleEffect
{
    Show = 1,
    Hide = 2,
    Require = 3,
    MakeOptional = 4
}
