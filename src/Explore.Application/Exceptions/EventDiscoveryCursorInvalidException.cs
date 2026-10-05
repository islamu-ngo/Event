namespace Explore.Application.Exceptions;

public sealed class EventDiscoveryCursorInvalidException() :
    Exception("The discovery continuation is invalid for this search.");
