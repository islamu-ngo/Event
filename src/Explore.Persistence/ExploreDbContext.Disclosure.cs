using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence;

public partial class ExploreDbContext
{
    private EventDiscoveryDisclosureMutationScope? _disclosureMutations;

    internal EventDiscoveryDisclosureMutationScope DisclosureMutations =>
        _disclosureMutations ??= new(this);

    internal Task<T> ExecuteDisclosureMutationAsync<T>(
        Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken) =>
        !Database.IsRelational() || Database.CurrentTransaction is not null
            ? operation(cancellationToken)
            : new EfCoreUnitOfWork(this).ExecuteSerializableAsync(operation, cancellationToken);

    internal Task ExecuteDisclosureMutationAsync(
        Func<CancellationToken, Task> operation, CancellationToken cancellationToken) =>
        ExecuteDisclosureMutationAsync(async token =>
        {
            await operation(token);
            return true;
        }, cancellationToken);

    internal Task FlushDisclosureAsync(CancellationToken cancellationToken) =>
        _disclosureMutations?.FlushAsync(cancellationToken) ?? Task.CompletedTask;

    internal void ResetDisclosureMutations() => _disclosureMutations?.Reset();
}
