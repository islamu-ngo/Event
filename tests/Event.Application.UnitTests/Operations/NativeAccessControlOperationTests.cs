using Explore.Application;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Permission;
using Explore.Application.DTOs.Role;
using Explore.Application.DTOs.TenantUserRoleGrant;
using Explore.Application.Features.Permissions.Requests.Queries;
using Explore.Application.Features.Roles.Requests.Commands;
using Explore.Application.Features.Roles.Requests.Queries;
using Explore.Application.Features.TenantUserRoleGrants.Requests.Commands;
using Explore.Application.Features.TenantUserRoleGrants.Requests.Queries;
using Explore.Application.Features.TenantUsers.Requests.Commands;
using Explore.Application.Responses;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Application.UnitTests.Operations;

public sealed class NativeAccessControlOperationTests
{
    private static readonly Type[] Requests =
    [
        // Permissions Queries (3)
        typeof(GetAssignablePermissionsRequest),
        typeof(GetPermissionListRequest),
        typeof(GetRolePermissionsRequest),

        // Roles Commands (3) & Queries (2)
        typeof(CreateCustomRoleCommand),
        typeof(DeleteCustomRoleCommand),
        typeof(UpdateRolePermissionsCommand),
        typeof(GetRoleDetailsRequest),
        typeof(GetRoleListRequest),

        // TenantUserRoleGrants Commands (2) & Queries (2)
        typeof(CreateTenantUserRoleGrantCommand),
        typeof(RevokeTenantUserRoleGrantCommand),
        typeof(GetTenantUserRoleGrantDetailsRequest),
        typeof(GetTenantUserRoleGrantListRequest),

        // TenantUsers Command (1)
        typeof(RemoveTenantMembershipCommand)
    ];

    [Test]
    [Arguments(typeof(GetAssignablePermissionsRequest), typeof(IQuery<List<PermissionListDto>>))]
    [Arguments(typeof(GetPermissionListRequest), typeof(IQuery<List<PermissionListDto>>))]
    [Arguments(typeof(GetRolePermissionsRequest), typeof(IQuery<List<RolePermissionDto>>))]
    [Arguments(typeof(CreateCustomRoleCommand), typeof(ICommand<BaseCommandResponse<int>>))]
    [Arguments(typeof(DeleteCustomRoleCommand), typeof(ICommand<BaseCommandResponse<int>>))]
    [Arguments(typeof(UpdateRolePermissionsCommand), typeof(ICommand<BaseCommandResponse<int>>))]
    [Arguments(typeof(GetRoleDetailsRequest), typeof(IQuery<RoleDto?>))]
    [Arguments(typeof(GetRoleListRequest), typeof(IQuery<List<RoleListDto>>))]
    [Arguments(typeof(CreateTenantUserRoleGrantCommand), typeof(ICommand<BaseCommandResponse<Guid>>))]
    [Arguments(typeof(RevokeTenantUserRoleGrantCommand), typeof(ICommand<bool>))]
    [Arguments(typeof(GetTenantUserRoleGrantDetailsRequest), typeof(IQuery<TenantUserRoleGrantDto?>))]
    [Arguments(typeof(GetTenantUserRoleGrantListRequest), typeof(IQuery<List<TenantUserRoleGrantListDto>>))]
    [Arguments(typeof(RemoveTenantMembershipCommand), typeof(ICommand<bool>))]
    public async Task Requests_ExposeOnlyTheirExactNativeOperation(Type request, Type port)
    {
        await Assert.That(request.GetInterfaces()).Contains(port);
        await Assert.That(request.GetInterfaces().Any(type => type.Namespace == "MediatR")).IsFalse();
        await Assert.That(request.GetInterfaces().Count(type => type.IsGenericType
            && (type.GetGenericTypeDefinition() == typeof(ICommand<>)
                || type.GetGenericTypeDefinition() == typeof(IQuery<>)))).IsEqualTo(1);
    }

    [Test]
    public async Task Requests_HaveOneNativeShapeAndNoLegacyDispatchEscapeHatch()
    {
        await Assert.That(Requests.Length).IsEqualTo(13);

        foreach (var request in Requests)
        {
            var shapes = request.GetInterfaces().Where(type => type.IsGenericType &&
                (type.GetGenericTypeDefinition() == typeof(ICommand<>) ||
                 type.GetGenericTypeDefinition() == typeof(IQuery<>))).ToArray();
            await Assert.That(shapes.Length).IsEqualTo(1);
            await Assert.That(request.GetInterfaces().Any(type => type.Namespace == "MediatR")).IsFalse();
        }
    }

    [Test]
    public async Task ApplicationComposition_RegistersEveryAccessControlOperationAsAScopedProtectedPort()
    {
        var services = new ServiceCollection();
        services.AddNativeOperations(typeof(GetPermissionListRequest).Assembly.GetTypes());
        var ports = services.Where(descriptor => !descriptor.IsKeyedService &&
            OperationServicesRegistration.IsHandlerContract(descriptor.ServiceType) &&
            Requests.Contains(descriptor.ServiceType.GenericTypeArguments[0])).ToArray();
        await Assert.That(ports.Length).IsEqualTo(13);
        await Assert.That(ports.All(port => port.Lifetime == ServiceLifetime.Scoped)).IsTrue();
        await Assert.That(ports.All(port => port.ImplementationFactory is not null)).IsTrue();
    }
}
