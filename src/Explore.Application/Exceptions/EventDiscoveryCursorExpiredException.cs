namespace Explore.Application.Exceptions;

public sealed class EventDiscoveryCursorExpiredException() :
    Exception("The discovery continuation has expired.");
