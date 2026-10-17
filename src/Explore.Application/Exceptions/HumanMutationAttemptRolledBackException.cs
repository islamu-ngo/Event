namespace Explore.Application.Exceptions;

/// <summary>
/// A single-attempt owner acknowledged rollback of a retryable provider conflict.
/// Only a whole operation with fresh source observations and a new provider decision may retry.
/// Connection loss or uncertain commit/rollback outcomes must never produce this receipt.
/// </summary>
public sealed class HumanMutationAttemptRolledBackException(Exception innerException)
    : ConcurrencyConflictException(ConcurrentUpdate,
        "The prepared mutation attempt was rolled back after a retryable conflict.", innerException: innerException);
