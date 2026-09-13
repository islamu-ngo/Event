namespace Explore.Application.Exceptions;

public sealed class EventSessionLanguageAlreadyAssignedException(Exception innerException)
    : Exception("The language is already assigned to this event session.", innerException);
