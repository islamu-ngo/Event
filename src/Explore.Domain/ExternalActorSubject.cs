using Explore.Domain.Interfaces;

namespace Explore.Domain;

public class ExternalActorSubject : IAuditableEntity, ISoftDeletable, IConcurrencyAware
{
    public Guid Id { get; set; }
    public Actor? Actor { get; set; }
    public DateTime FirstObservedAt { get; set; }
    public DateTime LastObservedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }
    public Guid ConcurrencyStamp { get; set; }

    public void Retire(DateTime when, Guid by)
    {
        if (IsDeleted)
        {
            throw new InvalidOperationException("The external Actor subject is already retired.");
        }

        if (by == Guid.Empty)
        {
            throw new ArgumentException("A retiring user is required.", nameof(by));
        }

        IsDeleted = true;
        DeletedAt = when;
        DeletedBy = by;
        UpdatedAt = when;
        UpdatedBy = by;
        ConcurrencyStamp = Guid.CreateVersion7();
    }
}
