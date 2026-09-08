namespace Explore.Application.Contracts.Services;

public interface ICustomPropertyQuotaResolver
{
    Task<int> GetIntAsync(string key, Guid tenantId, CancellationToken cancellationToken);

    Task<bool> GetBoolAsync(string key, Guid tenantId, CancellationToken cancellationToken);
}
