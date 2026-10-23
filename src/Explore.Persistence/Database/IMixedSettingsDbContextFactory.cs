namespace Explore.Persistence.Database;

/// <summary>
/// An exact instance or tenant lease from Global credentials. This selection does not
/// grant scalar writes: the independent mixed native capability remains mandatory.
/// </summary>
public interface IMixedSettingsDbContextFactory
{
    Task<ExploreDbContext> CreateMixedSettingsAsync(Guid? tenantId, CancellationToken cancellationToken);
}
