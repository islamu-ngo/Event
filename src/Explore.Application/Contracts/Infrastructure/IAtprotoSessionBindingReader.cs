using System.Collections.Immutable;

namespace Explore.Application.Contracts.Infrastructure;

public sealed record AtprotoSessionBinding(string? SubjectDid, string? PdsHost);

/// <summary>Reads at most two metadata bindings under the exact tenant lease; never reads credentials.</summary>
public interface IAtprotoSessionBindingReader
{
    Task<ImmutableArray<AtprotoSessionBinding>> ReadAsync(
        Guid tenantId, Guid userId, CancellationToken cancellationToken);
}
