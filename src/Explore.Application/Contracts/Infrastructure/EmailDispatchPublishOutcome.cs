namespace Explore.Application.Contracts.Infrastructure;

public enum EmailDispatchPublishOutcome
{
    Disabled = 0,
    Confirmed = 1,
    Returned = 2,
    Nacked = 3,
    Failed = 4
}
