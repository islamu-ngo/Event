namespace Explore.Persistence.Database;

/// <summary>Owns the separate purpose-bound Global pool used for local webhook admission.</summary>
public interface IWebhookLocalDeliveryAdmissionDbContextFactory
{
    Task<ExploreDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default);
}
