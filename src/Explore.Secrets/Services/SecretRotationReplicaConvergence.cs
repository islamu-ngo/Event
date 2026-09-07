namespace Explore.Secrets.Services;

public enum SecretRotationLocalStatus
{
    Activated,
    Rejected,
    Failed,
}

public sealed record SecretRotationLocalAcknowledgement(
    Guid AttemptId,
    string ReplicaId,
    string Consumer,
    SecretRotationLocalStatus Status,
    DateTimeOffset RecordedAt);

public enum SecretRotationConvergenceStatus
{
    Pending,
    Converged,
    FailedClosed,
    CoordinatedRestartRequired,
}

public sealed record SecretRotationConvergenceResult(
    SecretRotationConvergenceStatus Status,
    int RequiredReplicaCount,
    int ActivatedReplicaCount)
{
    public bool IsConverged => Status == SecretRotationConvergenceStatus.Converged;
}

public static class SecretRotationReplicaConvergence
{
    public static SecretRotationConvergenceResult Evaluate(
        Guid attemptId,
        IReadOnlySet<string> requiredReplicas,
        IEnumerable<SecretRotationLocalAcknowledgement> acknowledgements,
        DateTimeOffset now,
        DateTimeOffset staleDeadline,
        bool providerSupportsOverlap)
    {
        ArgumentNullException.ThrowIfNull(requiredReplicas);
        ArgumentNullException.ThrowIfNull(acknowledgements);
        if (attemptId == Guid.Empty || requiredReplicas.Count == 0)
        {
            throw new ArgumentException("A rotation attempt and at least one replica are required.");
        }

        if (!providerSupportsOverlap)
        {
            return new(
                SecretRotationConvergenceStatus.CoordinatedRestartRequired,
                requiredReplicas.Count,
                ActivatedReplicaCount: 0);
        }

        var latest = acknowledgements
            .Where(item => item.AttemptId == attemptId && requiredReplicas.Contains(item.ReplicaId))
            .GroupBy(item => item.ReplicaId, StringComparer.Ordinal)
            .Select(group => group.MaxBy(item => item.RecordedAt)!)
            .ToArray();
        var activated = latest.Count(item => item.Status == SecretRotationLocalStatus.Activated);
        var status = latest.Any(item => item.Status is SecretRotationLocalStatus.Rejected or SecretRotationLocalStatus.Failed)
            || now >= staleDeadline
                ? SecretRotationConvergenceStatus.FailedClosed
                : activated == requiredReplicas.Count
                    ? SecretRotationConvergenceStatus.Converged
                    : SecretRotationConvergenceStatus.Pending;

        return new(status, requiredReplicas.Count, activated);
    }
}
