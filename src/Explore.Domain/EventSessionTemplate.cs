using System.ComponentModel.DataAnnotations.Schema;
using Explore.Domain.Interfaces;

namespace Explore.Domain;

public class EventSessionTemplate : ITenantEntity, IAuditableEntity, ISoftDeletable, IConcurrencyAware
{
    public Guid Id { get; set; }
    public Guid ConcurrencyStamp { get; set; }

    [ForeignKey(nameof(EventTemplate))]
    public Guid EventTemplateId { get; set; }
    public EventTemplate? EventTemplate { get; set; }

    [ForeignKey(nameof(Tenant))]
    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }

    public required string SessionTemplateKey { get; set; }
    public required string DisplayName { get; set; }
    public string? Description { get; set; }

    public int Version { get; set; }
    public bool IsPublished { get; set; }
    public bool IsActive { get; set; }
    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }

    private readonly List<EventSessionTemplateCustomPropertyDefinition> _definitions = [];
    public IReadOnlyCollection<EventSessionTemplateCustomPropertyDefinition> Definitions => _definitions.AsReadOnly();

    internal void ReplaceDefinitions(IEnumerable<EventSessionTemplateCustomPropertyDefinition> definitions)
    {
        _definitions.Clear();
        _definitions.AddRange(definitions);
    }
}
