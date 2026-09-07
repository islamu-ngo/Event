namespace Explore.Application.Exceptions;

public sealed class TicketingNotFoundException : Exception
{
    public TicketingNotFoundException()
        : base("The ticketing target was not found.")
    {
    }
}
