namespace Explore.Application.Exceptions;

public sealed class KeycloakOperationConflictException(
    string message,
    Exception? innerException = null)
    : ApplicationException(message, innerException);
