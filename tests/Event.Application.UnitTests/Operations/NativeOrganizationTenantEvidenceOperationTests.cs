using Explore.Application;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.OrganizationTenantEvidence;
using Explore.Application.DTOs.StorageObject;
using Explore.Application.Features.OrganizationTenantEvidence.Handlers.Commands;
using Explore.Application.Features.OrganizationTenantEvidence.Handlers.Queries;
using Explore.Application.Features.OrganizationTenantEvidence.Requests.Commands;
using Explore.Application.Features.OrganizationTenantEvidence.Requests.Queries;
using Explore.Application.Responses;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Application.UnitTests.Operations;

public sealed class NativeOrganizationTenantEvidenceOperationTests
{
    [Test]
    public async Task DiscoveryRegistersThreeCommandsAndTwoQueriesWithoutLegacyContracts()
    {
        (Type Request, Type Handler, Type Port, Type Result)[] operations =
        [
            (typeof(CreateOrganizationTenantEvidenceUploadSessionCommand), typeof(CreateOrganizationTenantEvidenceUploadSessionCommandHandler),
                typeof(ICommandHandler<,>), typeof(BaseCommandResponse<StorageUploadSessionDto>)),
            (typeof(SubmitOrganizationTenantEvidenceCommand), typeof(SubmitOrganizationTenantEvidenceCommandHandler),
                typeof(ICommandHandler<,>), typeof(BaseCommandResponse<Guid>)),
            (typeof(ReviewOrganizationTenantEvidenceCommand), typeof(ReviewOrganizationTenantEvidenceCommandHandler),
                typeof(ICommandHandler<,>), typeof(BaseCommandResponse<Guid>)),
            (typeof(GetOrganizationTenantEvidenceRequest), typeof(GetOrganizationTenantEvidenceRequestHandler),
                typeof(IQueryHandler<,>), typeof(OrganizationTenantEvidenceDto)),
            (typeof(GetOrganizationTenantEvidenceCollectionRequest), typeof(GetOrganizationTenantEvidenceCollectionRequestHandler),
                typeof(IQueryHandler<,>), typeof(IReadOnlyList<OrganizationTenantEvidenceDto>))
        ];
        var services = new ServiceCollection();
        services.AddNativeOperations(operations.SelectMany(operation => new[] { operation.Request, operation.Handler }));
        var ports = services.Where(descriptor => !descriptor.IsKeyedService && descriptor.ServiceType.IsGenericType)
            .Where(descriptor => descriptor.ServiceType.GetGenericTypeDefinition() == typeof(ICommandHandler<,>)
                || descriptor.ServiceType.GetGenericTypeDefinition() == typeof(IQueryHandler<,>)).ToArray();
        await Assert.That(ports.Length).IsEqualTo(5);
        foreach (var operation in operations)
        {
            var port = ports.Single(descriptor => descriptor.ServiceType.GetGenericArguments()[0] == operation.Request);
            await Assert.That(port.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
            await Assert.That(port.ServiceType).IsEqualTo(operation.Port.MakeGenericType(operation.Request, operation.Result));
            await Assert.That(operation.Request.GetInterfaces().Any(contract => contract.Namespace == "MediatR")).IsFalse();
        }
        var detailMethod = typeof(GetOrganizationTenantEvidenceRequestHandler).GetMethods()
            .Single(method => method.ReturnType == typeof(Task<OrganizationTenantEvidenceDto>));
        await Assert.That(new System.Reflection.NullabilityInfoContext().Create(detailMethod.ReturnParameter)
            .GenericTypeArguments.Single().ReadState).IsEqualTo(System.Reflection.NullabilityState.Nullable);
    }
}
