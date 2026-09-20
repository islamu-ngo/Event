using Explore.Application;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.CustomPropertyGovernance;
using Explore.Application.Features.CustomPropertyGovernance.Handlers.Queries;
using Explore.Application.Features.CustomPropertyGovernance.Requests.Queries;
using Explore.Application.Responses;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Application.UnitTests.Operations;

public sealed class NativeCustomPropertyGovernanceOperationTests
{
    [Test]
    public async Task Discovery_RegistersExactlyOneScopedQueryWithProtectedTenantFacts()
    {
        var services = new ServiceCollection();
        services.AddNativeOperations([
            typeof(GetCustomPropertyGovernanceReportQuery),
            typeof(GetCustomPropertyGovernanceReportQueryHandler)
        ]);
        var ports = services.Where(descriptor => descriptor.ServiceType.IsGenericType
            && descriptor.ServiceType.GetGenericTypeDefinition() == typeof(IQueryHandler<,>)).ToArray();
        await Assert.That(ports.Length).IsEqualTo(1);
        await Assert.That(ports[0].Lifetime).IsEqualTo(ServiceLifetime.Scoped);
        await Assert.That(ports[0].ServiceType.GetGenericArguments()).IsEquivalentTo(new[]
        {
            typeof(GetCustomPropertyGovernanceReportQuery), typeof(PaginatedResult<CustomPropertyGovernanceRowDto>)
        });
        var tenantId = Guid.CreateVersion7();
        ISecureRequest query = new GetCustomPropertyGovernanceReportQuery { TenantId = tenantId };
        await Assert.That(query.AuthorizationFacts).IsEqualTo(new TenantScopedAuthorizationFacts(tenantId));
    }
}
