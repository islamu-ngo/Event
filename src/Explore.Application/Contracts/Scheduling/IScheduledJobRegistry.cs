namespace Explore.Application.Contracts.Scheduling;

public interface IScheduledJobRegistry
{
    IReadOnlyCollection<ScheduledJobDescriptor> ListJobs();

    ScheduledJobDescriptor? FindByName(string name);
}
