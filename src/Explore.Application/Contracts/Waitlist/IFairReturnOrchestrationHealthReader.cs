namespace Explore.Application.Contracts.Waitlist;

/// <summary>Reads only cross-tenant orchestration aggregates under independent Global authority.</summary>
public interface IFairReturnOrchestrationHealthReader
{
    Task<FairReturnOrchestrationHealth> ReadAsync(DateTime observedAt, CancellationToken cancellationToken);
}
