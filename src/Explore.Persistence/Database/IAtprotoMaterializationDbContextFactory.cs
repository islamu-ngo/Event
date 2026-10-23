namespace Explore.Persistence.Database;

/// <summary>
/// Acquires independent Global authority for one ATProto materialization operation.
/// The tenant identity is sealed null; private writes use bounded native capabilities.
/// </summary>
public interface IAtprotoMaterializationDbContextFactory
{
    Task<ExploreDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default);
}
