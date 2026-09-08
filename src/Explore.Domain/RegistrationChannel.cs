using Explore.Domain.Interfaces;

namespace Explore.Domain;

public sealed class RegistrationChannel : ITenantEntity, IAuditableEntity, ISoftDeletable, IConcurrencyAware
{
    private RegistrationChannel()
    {
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; set; }
    public Guid EventId { get; private set; }
    public Guid RegistrationWorkflowId { get; private set; }
    public Guid RegistrationRequirementId { get; private set; }
    public int Ordinal { get; private set; }
    public Guid? RegistrationProviderBindingId { get; private set; }
    public bool IsNative { get; private set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }
    public Guid ConcurrencyStamp { get; set; }

    public static RegistrationChannel Create(
        RegistrationRequirement requirement,
        int ordinal,
        bool isNative,
        Guid? registrationProviderBindingId,
        DateTime createdAt) => Create(
            Guid.CreateVersion7(), requirement, ordinal, isNative, registrationProviderBindingId, createdAt);

    public static RegistrationChannel Create(
        Guid id,
        RegistrationRequirement requirement,
        int ordinal,
        bool isNative,
        Guid? registrationProviderBindingId,
        DateTime createdAt)
    {
        ArgumentNullException.ThrowIfNull(requirement);
        if (ordinal <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ordinal), "Channel ordinal must be positive.");
        }

        if (id == Guid.Empty || registrationProviderBindingId == Guid.Empty)
        {
            throw new ArgumentException("Channel and provider-binding identities must be non-empty.");
        }

        if (isNative == registrationProviderBindingId.HasValue)
        {
            throw new ArgumentException("A native channel has no provider binding; a provider channel requires one.", nameof(registrationProviderBindingId));
        }

        return new RegistrationChannel
        {
            Id = id,
            TenantId = requirement.TenantId,
            EventId = requirement.EventId,
            RegistrationWorkflowId = requirement.RegistrationWorkflowId,
            RegistrationRequirementId = requirement.Id,
            Ordinal = ordinal,
            IsNative = isNative,
            RegistrationProviderBindingId = registrationProviderBindingId,
            CreatedAt = EnsureUtc(createdAt)
        };
    }

    public void Update(int ordinal, bool isNative, Guid? registrationProviderBindingId)
    {
        if (ordinal <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ordinal), "Channel ordinal must be positive.");
        }

        if (registrationProviderBindingId == Guid.Empty || isNative == registrationProviderBindingId.HasValue)
        {
            throw new ArgumentException("A native channel has no provider binding; a provider channel requires one.", nameof(registrationProviderBindingId));
        }

        Ordinal = ordinal;
        IsNative = isNative;
        RegistrationProviderBindingId = registrationProviderBindingId;
        ConcurrencyStamp = Guid.CreateVersion7();
    }

    public void Revive(int ordinal, bool isNative, Guid? registrationProviderBindingId)
    {
        Update(ordinal, isNative, registrationProviderBindingId);
        IsDeleted = false;
        DeletedAt = null;
        DeletedBy = null;
    }

    public void Remove(DateTime removedAt)
    {
        IsDeleted = true;
        DeletedAt = EnsureUtc(removedAt);
        ConcurrencyStamp = Guid.CreateVersion7();
    }

    private static DateTime EnsureUtc(DateTime value)
    {
        if (value == default || value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Timestamp must be a non-default UTC value.", nameof(value));
        }

        return value;
    }
}
