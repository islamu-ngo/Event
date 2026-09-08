using System;
using System.ComponentModel.DataAnnotations.Schema;
using Explore.Domain.Interfaces;

namespace Explore.Domain;

public class LocationRoom : ITenantEntity, IAuditableEntity, ISoftDeletable, IConcurrencyAware
{
    private const string PrivacyTombstonePrefix = "privacy-erased-";
    private string? _name;
    private string? _slug;
    private string? _description;
    private bool _isDeleted;

    public Guid Id { get; set; }

    [ForeignKey("Location")]
    public Guid LocationId { get; set; }
    public required Location Location { get; set; }

    public required string Name
    {
        get => _name ?? string.Empty;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (IsPrivacyTombstoned
                && !string.Equals(_name, value, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("A privacy-tombstoned room name cannot be changed.");
            }

            _name = value;
            if (IsPrivacyTombstoned)
            {
                _slug = null;
                _description = null;
                _isDeleted = true;
            }
        }
    }

    public string? Slug
    {
        get => _slug;
        set
        {
            GuardTombstonedText(value, nameof(Slug));
            _slug = value;
        }
    }

    public string? Description
    {
        get => _description;
        set
        {
            GuardTombstonedText(value, nameof(Description));
            _description = value;
        }
    }

    public int? Capacity { get; set; }
    public int SortOrder { get; set; }

    [ForeignKey("Tenant")]
    public Guid TenantId { get; set; }
    public required Tenant Tenant { get; set; }

    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public bool IsDeleted
    {
        get => _isDeleted;
        set
        {
            if (IsPrivacyTombstoned && !value)
            {
                throw new InvalidOperationException("A privacy-tombstoned room cannot be restored.");
            }

            _isDeleted = value;
        }
    }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }

    public Guid ConcurrencyStamp { get; set; }

    internal void TombstoneForPrivacyErasure(DateTime erasedAtUtc, Guid? erasedBy)
    {
        if (Id == Guid.Empty)
        {
            throw new InvalidOperationException("A persisted room id is required for a unique privacy tombstone.");
        }

        _name = $"{PrivacyTombstonePrefix}{Id:N}";
        _slug = null;
        _description = null;
        _isDeleted = true;
        DeletedAt = erasedAtUtc;
        DeletedBy = erasedBy;
        UpdatedAt = erasedAtUtc;
        UpdatedBy = erasedBy;
        ConcurrencyStamp = Guid.CreateVersion7();
    }

    private bool IsPrivacyTombstoned =>
        _name?.StartsWith(PrivacyTombstonePrefix, StringComparison.Ordinal) == true;

    private void GuardTombstonedText(string? value, string propertyName)
    {
        if (IsPrivacyTombstoned && value is not null)
        {
            throw new InvalidOperationException($"{propertyName} cannot be restored on a privacy-tombstoned room.");
        }
    }
}
