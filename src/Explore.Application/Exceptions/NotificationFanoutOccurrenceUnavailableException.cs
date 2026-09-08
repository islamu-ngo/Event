namespace Explore.Application.Exceptions;

public sealed class NotificationFanoutOccurrenceUnavailableException : Exception
{
    public NotificationFanoutOccurrenceUnavailableException()
        : base("The notification fanout occurrence is no longer available for recipient materialization.")
    {
    }
}
