namespace Explore.Application.Contracts.Persistence;

/// <summary>Observes whether a subject has an erasure saga without exposing its private workflow or receipt.</summary>
public interface IPrivacyErasurePresenceReader
{
    Task<bool> ExistsBySubjectAsync(Guid subjectId, CancellationToken cancellationToken);
}
