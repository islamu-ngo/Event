namespace Explore.Application.Contracts.Infrastructure;

public interface IEventReportingOutputCacheInvalidator
{
    Task InvalidateAsync(CancellationToken cancellationToken);
}
