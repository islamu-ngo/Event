using Explore.Application;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Organization;
using Explore.Application.Features.Organizations.Handlers.Commands;
using Explore.Application.Features.Organizations.Handlers.Queries;
using Explore.Application.Features.Organizations.Requests.Commands;
using Explore.Application.Features.Organizations.Requests.Queries;
using Explore.Application.Responses;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Application.UnitTests.Operations;

public sealed class NativeOrganizationsOperationTests
{
    [Test]
    public async Task BoundedDiscovery_RegistersAllSevenPortsIncludingVoidApproval()
    {
        (Type Request, Type Handler, Type Port, Type? Result)[] operations =
        [
            (typeof(CreateOrganizationCommand), typeof(CreateOrganizationCommandHandler),
                typeof(ICommandHandler<,>), typeof(BaseCommandResponse<Guid>)),
            (typeof(UpdateOrganizationCommand), typeof(UpdateOrganizationCommandHandler),
                typeof(ICommandHandler<,>), typeof(BaseCommandResponse<Guid>)),
            (typeof(DeleteOrganizationCommand), typeof(DeleteOrganizationCommandHandler),
                typeof(ICommandHandler<,>), typeof(BaseCommandResponse<Guid>)),
            (typeof(UpdateOrganizationApprovalStatusCommand), typeof(UpdateOrganizationApprovalStatusCommandHandler),
                typeof(ICommandHandler<>), null),
            (typeof(GetOrganizationDetailsRequest), typeof(GetOrganizationDetailsRequestHandler),
                typeof(IQueryHandler<,>), typeof(OrganizationDto)),
            (typeof(GetOrganizationListRequest), typeof(GetOrganizationListRequestHandler),
                typeof(IQueryHandler<,>), typeof(PaginatedResult<OrganizationListDto>)),
            (typeof(GetMyOrganizationsRequest), typeof(GetMyOrganizationsRequestHandler),
                typeof(IQueryHandler<,>), typeof(PaginatedResult<OrganizationListDto>))
        ];
        var services = new ServiceCollection();
        services.AddNativeOperations(
            operations.SelectMany(operation => new[] { operation.Request, operation.Handler }));
        var ports = services
            .Where(descriptor => !descriptor.IsKeyedService && descriptor.ServiceType.IsGenericType)
            .Where(descriptor => descriptor.ServiceType.GetGenericTypeDefinition() == typeof(ICommandHandler<,>)
                || descriptor.ServiceType.GetGenericTypeDefinition() == typeof(ICommandHandler<>)
                || descriptor.ServiceType.GetGenericTypeDefinition() == typeof(IQueryHandler<,>))
            .ToArray();

        await Assert.That(ports.Length).IsEqualTo(7);
        await Assert.That(ports.All(port => port.Lifetime == ServiceLifetime.Scoped)).IsTrue();
        foreach (var operation in operations)
        {
            var port = ports.Single(descriptor =>
                descriptor.ServiceType.GetGenericArguments()[0] == operation.Request).ServiceType;
            await Assert.That(port.GetGenericTypeDefinition()).IsEqualTo(operation.Port);
            if (operation.Result is not null)
            {
                await Assert.That(port.GetGenericArguments()[1]).IsEqualTo(operation.Result);
            }
        }
    }
}
