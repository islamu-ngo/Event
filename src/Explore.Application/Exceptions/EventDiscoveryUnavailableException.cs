namespace Explore.Application.Exceptions;

public sealed class EventDiscoveryUnavailableException() :
    Exception("Event discovery is temporarily unavailable.");
