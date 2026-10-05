namespace Explore.Application.Exceptions;

public sealed class EventDiscoveryRestartRequiredException() :
    Exception("Discovery results changed. Start a new traversal.");
