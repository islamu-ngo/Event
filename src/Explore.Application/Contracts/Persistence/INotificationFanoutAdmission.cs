namespace Explore.Application.Contracts.Persistence;

/// <summary>Owns bounded cross-tenant claim admission and aggregate processor observations.</summary>
public interface INotificationFanoutAdmission
{
    Task<NotificationFanoutClaimRoundResult> ClaimDueRoundAsync(
        NotificationFanoutClaimRoundRequest request,
        CancellationToken cancellationToken);

    Task<NotificationFanoutProcessorSnapshot> GetProcessorSnapshotAsync(
        DateTime observedAt,
        CancellationToken cancellationToken);
}
