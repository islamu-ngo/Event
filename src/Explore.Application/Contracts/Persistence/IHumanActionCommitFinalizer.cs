namespace Explore.Application.Contracts.Persistence;

/// <summary>
/// Registers current-source and timed-assignment admission on the actual original committing
/// owner. Credential subject/lifetime/clock are selected-scope custody, never caller arguments.
/// Registration alone is not an action grant; the prepared authority supplies its exact checks.
/// </summary>
public interface IHumanActionCommitFinalizer
{
    void Register(Func<CancellationToken, Task> validateSources, Action<DateTimeOffset> validateAssignments);
}
