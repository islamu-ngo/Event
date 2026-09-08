namespace Explore.Application.Contracts.Services;

public interface ITenantResolver
{
    string Name { get; }

    int Priority { get; }

    Guid? ResolveTenantId();
}
