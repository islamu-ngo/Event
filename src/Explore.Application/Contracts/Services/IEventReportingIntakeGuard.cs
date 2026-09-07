namespace Explore.Application.Contracts.Services;

public sealed record EventReportingIntakeDecision(
    bool TenantResolved,
    bool IntakeEnabled,
    string ReasonCode,
    string Message);

public interface IEventReportingIntakeGuard
{
    Task<EventReportingIntakeDecision> ResolveAsync(Guid tenantId, CancellationToken cancellationToken);
}
