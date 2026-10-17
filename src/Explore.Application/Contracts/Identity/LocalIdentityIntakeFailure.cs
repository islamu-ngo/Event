using System.Data.Common;

namespace Explore.Application.Contracts.Identity;

public enum LocalIdentityIntakeFailure
{
    StoreUnavailable = 1,
    Conflict = 2
}

/// <summary>
/// The native store classified a failure before this invocation attempted a new commit
/// or observed an existing durable operation. Earlier operations are not invalidated.
/// This category carries no target, provider message or promise of durable delivery.
/// </summary>
public sealed class LocalIdentityIntakeException : Exception
{
    public LocalIdentityIntakeException(LocalIdentityIntakeFailure failure)
        : base("Local lifecycle intake did not establish durable ownership.")
    {
        if (!Enum.IsDefined(failure)) throw new ArgumentOutOfRangeException(nameof(failure));
        Failure = failure;
    }

    public LocalIdentityIntakeFailure Failure { get; }
}

public static class LocalIdentityIntakeFailureClassifier
{
    /// <summary>
    /// Classifies provider-declared transient read failures. Write callers must separately
    /// prove that no commit was attempted; this predicate does not establish transaction state.
    /// </summary>
    public static bool IsTransientStoreFailure(Exception exception)
    {
        bool transient = false;
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is OperationCanceledException or ArgumentException or InvalidOperationException) return false;
            if (current is DbException { IsTransient: true }) transient = true;
        }
        return transient;
    }
}
