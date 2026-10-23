namespace Explore.Persistence.Database;

/// <summary>
/// Acquires identity-free Global leases authorized only for the bounded email
/// admission and recovery functions. It cannot acquire private tenant content.
/// </summary>
public interface IEmailDispatchAdmissionDbContextFactory
{
    Task<ExploreDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default);
}
