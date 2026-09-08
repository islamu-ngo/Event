namespace Explore.Application.Exceptions;

public sealed class NotificationIntentDeduplicationConflictException(Exception innerException)
    : Exception("A notification intent with the same tenant deduplication key already exists.", innerException);
