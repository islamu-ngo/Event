namespace Explore.Persistence.Database;

/// <summary>Purpose-specific entry to the independent Global pool; it never selects a scoped Runtime context.</summary>
public interface ICheckoutDispatchAdmissionDbContextFactory
{
    Task<ExploreDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default);
}

public sealed class CheckoutDispatchAdmissionDbContextFactory(IGlobalExploreDbContextFactory global)
    : ICheckoutDispatchAdmissionDbContextFactory
{
    public Task<ExploreDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
        global.CreateDbContextAsync(cancellationToken);
}
