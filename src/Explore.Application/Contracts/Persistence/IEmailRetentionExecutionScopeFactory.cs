namespace Explore.Application.Contracts.Persistence;

/// <summary>Acquires fresh selected-tenant Runtime authority before constructing retention ports.</summary>
public interface IEmailRetentionExecutionScopeFactory
{
    Task<IEmailRetentionExecutionScope> CreateAsync(Guid tenantId, CancellationToken cancellationToken);
}

public interface IEmailRetentionExecutionScope : IAsyncDisposable
{
    Task<int> CountEligibleAsync(DateTime cutoffUtc, int batchSize, CancellationToken cancellationToken);
    Task<int> RedactAsync(DateTime cutoffUtc, DateTime redactedAt, int batchSize, CancellationToken cancellationToken);
}
