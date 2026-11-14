namespace Explore.Application.Contracts.Identity;

public interface ILocalVisitorEnrollmentRecoveryService
{
    Task<VisitorEnrollmentRecoveryResult> RecoverOnceAsync(CancellationToken cancellationToken);
}

public sealed record VisitorEnrollmentRecoveryResult(int ObservedCount, int SettledCount);
