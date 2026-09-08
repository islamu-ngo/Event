namespace Explore.Application.Contracts.Infrastructure;

public interface IAtprotoDiscoveryCacheInvalidator
{
    ValueTask InvalidateAsync(CancellationToken cancellationToken = default);
}
