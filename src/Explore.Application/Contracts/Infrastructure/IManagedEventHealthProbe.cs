namespace Explore.Application.Contracts.Infrastructure;

public sealed record ManagedEventHealthObservation(string Status, DateTime ObservedAt);

public interface IManagedEventHealthProbe
{
    Task<ManagedEventHealthObservation> CheckAsync(CancellationToken cancellationToken = default);
}
