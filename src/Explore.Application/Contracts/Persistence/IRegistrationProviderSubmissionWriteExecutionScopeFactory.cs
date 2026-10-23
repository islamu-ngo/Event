namespace Explore.Application.Contracts.Persistence;

/// <summary>Selects an exact ordinary Runtime tenant before constructing delivery dependencies.</summary>
public interface IRegistrationProviderSubmissionWriteExecutionScopeFactory
{
    ValueTask<IRegistrationProviderSubmissionWriteExecutionScope> CreateAsync(
        Guid tenantId, CancellationToken cancellationToken);
}

public interface IRegistrationProviderSubmissionWriteExecutionScope : IAsyncDisposable
{
    Task<bool> DrainAsync(RegistrationProviderSubmissionWriteClaim claim, CancellationToken cancellationToken);
}
