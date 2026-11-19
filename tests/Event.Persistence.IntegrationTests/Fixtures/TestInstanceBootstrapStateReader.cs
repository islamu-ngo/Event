using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Repositories;

namespace Event.Persistence.IntegrationTests.Fixtures;

/// <summary>Reads current committed bootstrap state from an explicit test context or repository.</summary>
internal sealed class TestInstanceBootstrapStateReader(IInstanceBootstrapStateRepository repository)
    : IInstanceBootstrapStateReader
{
    public TestInstanceBootstrapStateReader(ExploreDbContext context)
        : this(new InstanceBootstrapStateRepository(context))
    {
    }

    public Task<InstanceBootstrapState?> GetCurrent(CancellationToken cancellationToken = default) =>
        repository.GetCurrent(cancellationToken);
}
