namespace Explore.Application.Contracts.Infrastructure;

public interface IListmonkConnectionTester
{
    Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default);
}
