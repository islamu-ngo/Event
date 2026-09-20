using Explore.Application;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.OrganizationMember;
using Explore.Application.Features.OrganizationMembers.Handlers.Commands;
using Explore.Application.Features.OrganizationMembers.Handlers.Queries;
using Explore.Application.Features.OrganizationMembers.Requests.Commands;
using Explore.Application.Features.OrganizationMembers.Requests.Queries;
using Explore.Application.Responses;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Application.UnitTests.Operations;

public sealed class NativeOrganizationMemberOperationTests
{
    [Test]
    public async Task BoundedDiscovery_RegistersFourCommandsAndFourQueries()
    {
        (Type Request, Type Handler, Type Port, Type Result)[] operations =
        [
            (typeof(ValidateOrganizationInvitationQuery), typeof(ValidateOrganizationInvitationQueryHandler),
                typeof(IQueryHandler<,>), typeof(BaseCommandResponse<Guid>)),
            (typeof(AddOrganizationMemberCommand), typeof(AddOrganizationMemberCommandHandler),
                typeof(ICommandHandler<,>), typeof(BaseCommandResponse<Guid>)),
            (typeof(DeclineInvitationCommand), typeof(DeclineInvitationCommandHandler),
                typeof(ICommandHandler<,>), typeof(BaseCommandResponse<Guid>)),
            (typeof(DeleteOrganizationMemberCommand), typeof(DeleteOrganizationMemberCommandHandler),
                typeof(ICommandHandler<,>), typeof(BaseCommandResponse<Guid>)),
            (typeof(UpdateOrganizationMemberRoleCommand), typeof(UpdateOrganizationMemberRoleCommandHandler),
                typeof(ICommandHandler<,>), typeof(BaseCommandResponse<Guid>)),
            (typeof(GetMyInvitationsRequest), typeof(GetMyInvitationsRequestHandler),
                typeof(IQueryHandler<,>), typeof(List<OrganizationInvitationDto>)),
            (typeof(GetOrganizationMemberDetailsRequest), typeof(GetOrganizationMemberDetailsRequestHandler),
                typeof(IQueryHandler<,>), typeof(OrganizationMemberDto)),
            (typeof(GetOrganizationMembersRequest), typeof(GetOrganizationMembersRequestHandler),
                typeof(IQueryHandler<,>), typeof(List<OrganizationMemberDto>))
        ];
        var services = new ServiceCollection();
        services.AddNativeOperations(
            operations.SelectMany(operation => new[] { operation.Request, operation.Handler }));
        var ports = services
            .Where(descriptor => !descriptor.IsKeyedService && descriptor.ServiceType.IsGenericType)
            .Where(descriptor => descriptor.ServiceType.GetGenericTypeDefinition() == typeof(ICommandHandler<,>)
                || descriptor.ServiceType.GetGenericTypeDefinition() == typeof(IQueryHandler<,>))
            .ToArray();

        await Assert.That(ports.Length).IsEqualTo(8);
        await Assert.That(ports.All(port => port.Lifetime == ServiceLifetime.Scoped)).IsTrue();
        foreach (var operation in operations)
        {
            var port = ports.Single(descriptor =>
                descriptor.ServiceType.GetGenericArguments()[0] == operation.Request).ServiceType;
            await Assert.That(port.GetGenericTypeDefinition()).IsEqualTo(operation.Port);
            await Assert.That(port.GetGenericArguments()[1]).IsEqualTo(operation.Result);
        }
    }
}
