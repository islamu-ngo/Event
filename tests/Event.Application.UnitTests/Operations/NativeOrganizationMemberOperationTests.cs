using Explore.Application;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Identity;
using Explore.Application.DTOs.OrganizationMember;
using Explore.Application.Features.OrganizationMembers.Handlers.Commands;
using Explore.Application.Features.OrganizationMembers.Handlers.Queries;
using Explore.Application.Features.OrganizationMembers.Requests.Commands;
using Explore.Application.Features.OrganizationMembers.Requests.Queries;
using Explore.Application.Responses;
using Explore.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Application.UnitTests.Operations;

public sealed class NativeOrganizationMemberOperationTests
{
    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task MissingAuthenticatedIdentityCannotReachOrganizationMembershipRepositories(
        bool authenticated, bool emptyId)
    {
        var caller = new CallerContext(
            emptyId ? Guid.Empty : authenticated ? null : Guid.CreateVersion7(), authenticated);
        var add = new AddOrganizationMemberCommandHandler(null!, null!, null!, null!, caller, null!, null!);
        var update = new UpdateOrganizationMemberRoleCommandHandler(null!, null!, caller, null!, null!);
        var delete = new DeleteOrganizationMemberCommandHandler(null!, null!, caller, null!, null!);
        var decline = new DeclineInvitationCommandHandler(null!, caller, null!);

        await Assert.That((await add.ExecuteAsync(new()
        {
            TenantId = Guid.CreateVersion7(),
            AddOrganizationMemberDto = new()
            {
                OrganizationId = Guid.CreateVersion7(),
                Email = "member@example.test",
                Role = RoleEnum.OrgMember
            }
        }, default)).IsSuccess).IsFalse();
        await Assert.That((await update.ExecuteAsync(new()
        {
            UpdateOrganizationMemberRoleDto = new() { Id = Guid.CreateVersion7(), Role = RoleEnum.OrgMember }
        }, default)).IsSuccess).IsFalse();
        await Assert.That((await delete.ExecuteAsync(new() { MemberId = Guid.CreateVersion7() }, default))
            .IsSuccess).IsFalse();
        await Assert.That((await decline.ExecuteAsync(new() { InvitationId = Guid.CreateVersion7() }, default))
            .IsSuccess).IsFalse();
    }

    [Test]
    public async Task UnauthenticatedInvitationValidationCannotReachMembershipRepositories()
    {
        var query = new ValidateOrganizationInvitationQueryHandler(null!,
            new CallerContext(Guid.CreateVersion7(), false), null!, null!);
        await Assert.That((await query.QueryAsync(new() { InvitationId = Guid.CreateVersion7() }, default))
            .IsSuccess).IsFalse();
        var invitations = new GetMyInvitationsRequestHandler(null!,
            new CallerContext(Guid.CreateVersion7(), false), null!, null!);
        await Assert.That(await invitations.QueryAsync(new(), default)).IsEmpty();
    }

    private sealed class CallerContext(Guid? userId, bool authenticated) : IUserContext
    {
        public Guid? UserId => userId;
        public bool IsAuthenticated => authenticated;
        public string? Email => null;
        public string? Username => null;
        public Guid GetRequiredUserId() => IsAuthenticated && UserId is Guid id && id != Guid.Empty
            ? id : throw new UnauthorizedAccessException();
    }

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
