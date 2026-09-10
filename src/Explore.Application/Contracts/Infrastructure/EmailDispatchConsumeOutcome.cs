
namespace Explore.Application.Contracts.Infrastructure;

public enum EmailDispatchConsumeOutcome
{
    Other = 0,
    Acked = 1,
    Rejected = 2,
    Nacked = 3,
    Replayed = 4,
    Parked = 5
}
