using Explore.Domain.Interfaces;
using Explore.Domain.ValueObjects;

namespace Explore.Domain;

public sealed class EventAddOnCatalogVersion :
    ITenantEntity,
    IAuditableEntity,
    ISoftDeletable,
    IConcurrencyAware
{
    private readonly List<EventAddOnCatalogItem> _items = [];
    private Guid _tenantId;

    private EventAddOnCatalogVersion()
    {
    }

    private EventAddOnCatalogVersion(
        Guid tenantId,
        Guid eventId,
        string currencyCode,
        int versionNumber)
    {
        Id = Guid.CreateVersion7();
        TenantId = tenantId;
        EventId = eventId;
        CurrencyCode = currencyCode;
        VersionNumber = versionNumber;
        ConcurrencyStamp = Guid.CreateVersion7();
    }

    public Guid Id { get; private set; }

    public Guid TenantId
    {
        get => _tenantId;
        set => TenantIdentity.Set(ref _tenantId, value, nameof(EventAddOnCatalogVersion));
    }

    public Guid EventId { get; private set; }

    public string CurrencyCode { get; private set; } = string.Empty;

    public int VersionNumber { get; private set; }

    public DateTime? PublishedAt { get; private set; }

    public DateTime? RetiredAt { get; private set; }

    public IReadOnlyCollection<EventAddOnCatalogItem> Items => _items.AsReadOnly();

    public Guid ConcurrencyStamp { get; set; }

    public DateTime CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime? DeletedAt { get; set; }

    public Guid? DeletedBy { get; set; }

    public bool IsPublished => PublishedAt.HasValue && !RetiredAt.HasValue;

    public static EventAddOnCatalogVersion Create(
        Guid tenantId,
        Guid eventId,
        string currencyCode,
        int versionNumber)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("Tenant is required.", nameof(tenantId));
        }

        if (eventId == Guid.Empty)
        {
            throw new ArgumentException("Event is required.", nameof(eventId));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(versionNumber);

        return new EventAddOnCatalogVersion(
            tenantId,
            eventId,
            CurrencyMetadata.Get(currencyCode).Code,
            versionNumber);
    }

    public void AddItem(EventAddOnCatalogItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        EnsureDraft();
        if (item.TenantId != TenantId ||
            item.EventAddOnCatalogVersionId != Id ||
            !string.Equals(item.CurrencyCode, CurrencyCode, StringComparison.Ordinal) ||
            _items.Any(existing =>
                existing.Id == item.Id ||
                string.Equals(existing.Name, item.Name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException(
                "Add-on item must be unique and belong to this catalog's tenant and currency.",
                nameof(item));
        }

        _items.Add(item);
        ConcurrencyStamp = Guid.CreateVersion7();
    }

    public void Publish(DateTime publishedAtUtc)
    {
        EnsureDraft();
        DateTime normalized = RequireUtc(publishedAtUtc, nameof(publishedAtUtc));
        if (_items.Count == 0)
        {
            throw new InvalidOperationException("An add-on catalog requires at least one item before publication.");
        }

        if (normalized > DateTime.UtcNow)
        {
            throw new ArgumentException(
                "Scheduled future add-on catalog publication is not supported.",
                nameof(publishedAtUtc));
        }

        PublishedAt = normalized;
        ConcurrencyStamp = Guid.CreateVersion7();
    }

    public void Retire(DateTime retiredAtUtc)
    {
        DateTime normalized = RequireUtc(retiredAtUtc, nameof(retiredAtUtc));
        if (!PublishedAt.HasValue || RetiredAt.HasValue || normalized < PublishedAt.Value)
        {
            throw new InvalidOperationException("Only a currently published catalog can be retired.");
        }

        RetiredAt = normalized;
        ConcurrencyStamp = Guid.CreateVersion7();
    }

    private void EnsureDraft()
    {
        if (PublishedAt.HasValue || RetiredAt.HasValue)
        {
            throw new InvalidOperationException("Published or retired add-on catalogs are immutable.");
        }
    }

    private static DateTime RequireUtc(DateTime value, string parameterName) =>
        value.Kind == DateTimeKind.Utc
            ? value
            : throw new ArgumentException("Timestamp must be UTC.", parameterName);
}
