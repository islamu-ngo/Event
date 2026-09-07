using System.ComponentModel.DataAnnotations.Schema;
using Explore.Domain.Interfaces;

namespace Explore.Domain;

public class EventSessionTemplateCustomPropertyOption : IAuditableEntity, ISoftDeletable, IConcurrencyAware
{
    public Guid Id { get; set; }
    public Guid ConcurrencyStamp { get; set; }

    [ForeignKey(nameof(Definition))]
    public Guid EventSessionTemplateCustomPropertyDefinitionId { get; set; }
    public EventSessionTemplateCustomPropertyDefinition? Definition { get; set; }

    public required string Namespace { get; set; }
    public required string Key { get; set; }
    public required string DisplayName { get; set; }
    public string? Description { get; set; }
    public required string Value { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; }
    public int SortOrder { get; set; }

    [ForeignKey(nameof(ParentOption))]
    public Guid? ParentOptionId { get; set; }
    public EventSessionTemplateCustomPropertyOption? ParentOption { get; set; }

    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }

    private readonly List<EventSessionTemplateCustomPropertyOption> _childOptions = [];
    public IReadOnlyCollection<EventSessionTemplateCustomPropertyOption> ChildOptions => _childOptions.AsReadOnly();
}
