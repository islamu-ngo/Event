namespace Explore.Persistence.Database;

/// <summary>Creates identity-free Global contexts for notification outbox admission only.</summary>
public interface INotificationOutboxAdmissionDbContextFactory
{
    Task<ExploreDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default);
}
