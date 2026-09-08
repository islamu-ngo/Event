namespace Explore.Application.Contracts.Services;

public interface ITenantResolverService
{
    Guid ResolveTenantId();
}
