namespace Explore.Application.Exceptions;

/// <summary>The transaction was acknowledged as committed; cleanup failure must not authorize replay.</summary>
public sealed class TransactionCommittedCleanupException(Exception innerException)
    : Exception("The original transaction committed, but cleanup failed. Do not replay the operation.", innerException);
