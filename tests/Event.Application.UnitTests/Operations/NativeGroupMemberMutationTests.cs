using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.GroupMembers.Handlers.Commands;
using Explore.Application.Features.GroupMembers.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Event.Application.UnitTests.Operations;

public sealed class NativeGroupMemberMutationTests
{
    [Test]
    public async Task RoleChangesAndDeletion_PreserveAnAdministratorAcrossMembershipTransitions()
    {
        var state = new MembershipState();
        var administrator = state.AddMember(RoleEnum.GroupAdmin);
        await using var provider = state.Services().BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var update = scope.ServiceProvider.GetRequiredService<
            ICommandHandler<UpdateGroupMemberRoleCommand, BaseCommandResponse<Guid>>>();
        var delete = scope.ServiceProvider.GetRequiredService<
            ICommandHandler<DeleteGroupMemberCommand, BaseCommandResponse<Guid>>>();

        var demotion = new UpdateGroupMemberRoleCommand
        {
            RequesterUserId = administrator.UserId.ToString(),
            UpdateGroupMemberRoleDto = new() { Id = administrator.Id, Role = RoleEnum.GroupMember }
        };
        var removal = new DeleteGroupMemberCommand
        {
            RequesterUserId = administrator.UserId.ToString(),
            MemberId = administrator.Id
        };
        await Assert.That((await update.ExecuteAsync(demotion, default)).IsSuccess).IsFalse();
        await Assert.That((await delete.ExecuteAsync(removal, default)).IsSuccess).IsFalse();
        await Assert.That(administrator.RoleId).IsEqualTo((int)RoleEnum.GroupAdmin);
        await Assert.That(administrator.IsDeleted).IsFalse();

        var successor = state.AddMember(RoleEnum.GroupAdmin);
        var unauthorized = demotion with { RequesterUserId = Guid.NewGuid().ToString() };
        await Assert.That((await update.ExecuteAsync(unauthorized, default)).IsSuccess).IsFalse();
        await Assert.That((await delete.ExecuteAsync(
            removal with { RequesterUserId = "not-a-user-id" }, default)).IsSuccess).IsFalse();
        await Assert.That(administrator.RoleId).IsEqualTo((int)RoleEnum.GroupAdmin);
        await Assert.That(administrator.IsDeleted).IsFalse();

        var demoted = await update.ExecuteAsync(demotion, default);
        await Assert.That(demoted.IsSuccess).IsTrue();
        await Assert.That(demoted.Id).IsEqualTo(administrator.Id);
        await Assert.That(administrator.RoleId).IsEqualTo((int)RoleEnum.GroupMember);
        var removeSuccessor = new DeleteGroupMemberCommand
        {
            MemberId = successor.Id,
            RequesterUserId = successor.UserId.ToString()
        };
        await Assert.That((await delete.ExecuteAsync(removeSuccessor, default)).IsSuccess).IsFalse();
        await Assert.That(successor.IsDeleted).IsFalse();
        await Assert.That((await delete.ExecuteAsync(removal, default)).IsSuccess).IsFalse();
        await Assert.That(administrator.IsDeleted).IsFalse();
        var removed = await delete.ExecuteAsync(
            removal with { RequesterUserId = successor.UserId.ToString() }, default);
        await Assert.That(removed.IsSuccess).IsTrue();
        await Assert.That(removed.Id).IsEqualTo(administrator.Id);
        await Assert.That(administrator.IsDeleted).IsTrue();
        await Assert.That(state.Members.Where(member => !member.IsDeleted).Select(member => member.Id))
            .IsEquivalentTo(new[] { successor.Id });
    }

    [Test]
    public async Task Addition_RequiresTenantParticipationAuthorityAndUniqueMembership()
    {
        var state = new MembershipState();
        var administrator = state.AddMember(RoleEnum.GroupAdmin);
        var user = new User
        {
            Id = Guid.CreateVersion7(),
            Pii = new() { Email = "new-member@example.test", FirstName = "New", LastName = "Member" }
        };
        var users = Substitute.For<IUserRepository>();
        users.GetUserByEmail(user.Email).Returns(user);
        var services = state.Services();
        services.AddSingleton(users);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var add = scope.ServiceProvider.GetRequiredService<
            ICommandHandler<AddGroupMemberCommand, BaseCommandResponse<Guid>>>();
        var command = new AddGroupMemberCommand
        {
            RequesterUserId = administrator.UserId.ToString(),
            AddGroupMemberDto = new()
            {
                GroupId = state.Participation.GroupId,
                Email = user.Email,
                Role = RoleEnum.GroupMember,
                GroupPositionId = 4
            }
        };

        state.Participates = false;
        await Assert.That((await add.ExecuteAsync(command, default)).IsSuccess).IsFalse();
        state.Participates = true;
        foreach (var requester in new[] { "invalid", Guid.NewGuid().ToString() })
        {
            await Assert.That((await add.ExecuteAsync(
                command with { RequesterUserId = requester }, default)).IsSuccess).IsFalse();
        }
        await Assert.That((await add.ExecuteAsync(command with
        {
            AddGroupMemberDto = command.AddGroupMemberDto with { Email = "missing@example.test" }
        }, default)).IsSuccess).IsFalse();
        await Assert.That(state.Members.Count).IsEqualTo(1);

        var created = await add.ExecuteAsync(command, default);
        await Assert.That(created.IsSuccess).IsTrue();
        var member = state.Members.Single(item => item.Id == created.Id);
        await Assert.That(member.UserId).IsEqualTo(user.Id);
        await Assert.That(member.TenantId).IsEqualTo(state.Participation.TenantId);
        await Assert.That(member.GroupTenantId).IsEqualTo(state.Participation.Id);
        await Assert.That(member.RoleId).IsEqualTo((int)RoleEnum.GroupMember);
        await Assert.That(member.GroupPositionId).IsEqualTo((int?)4);
        await Assert.That((await add.ExecuteAsync(command, default)).IsSuccess).IsFalse();
        await Assert.That(state.Members.Count).IsEqualTo(2);
    }

    private sealed class MembershipState
    {
        public List<GroupMember> Members { get; } = [];
        public bool Participates { get; set; } = true;
        public GroupTenant Participation { get; } = new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = Guid.CreateVersion7(),
            Tenant = null!,
            GroupId = Guid.CreateVersion7(),
            Group = null!,
            ApprovalStatus = null!
        };

        public GroupMember AddMember(RoleEnum role)
        {
            var member = new GroupMember
            {
                Id = Guid.CreateVersion7(),
                UserId = Guid.CreateVersion7(),
                User = null!,
                GroupTenantId = Participation.Id,
                GroupTenant = Participation,
                TenantId = Participation.TenantId,
                Tenant = null!,
                RoleId = (int)role,
                Role = null!
            };
            Members.Add(member);
            return member;
        }

        public IServiceCollection Services()
        {
            var repository = Substitute.For<IGroupMemberRepository>();
            repository.GetById(Arg.Any<Guid>()).Returns(call =>
                Members.SingleOrDefault(member => member.Id == call.Arg<Guid>() && !member.IsDeleted));
            repository.GetMembersByGroupId(Arg.Any<Guid>()).Returns(call =>
                Members.Where(member => !member.IsDeleted &&
                    member.GroupTenant.GroupId == call.Arg<Guid>()).ToList());
            repository.GetByGroupAndUser(Arg.Any<Guid>(), Arg.Any<Guid>()).Returns(call =>
                Members.SingleOrDefault(member => !member.IsDeleted &&
                    member.GroupTenant.GroupId == call.ArgAt<Guid>(0) && member.UserId == call.ArgAt<Guid>(1)));
            repository.HasPermissionInGroup(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>())
                .Returns(call => Members.Any(member => !member.IsDeleted &&
                    member.GroupTenant.GroupId == call.ArgAt<Guid>(0) &&
                    member.UserId == call.ArgAt<Guid>(1) && member.RoleId == (int)RoleEnum.GroupAdmin));
            repository.Exists(Arg.Any<Guid>(), Arg.Any<Guid>()).Returns(call =>
                Members.Any(member => !member.IsDeleted &&
                    member.GroupTenant.GroupId == call.ArgAt<Guid>(0) && member.UserId == call.ArgAt<Guid>(1)));
            repository.Create(Arg.Any<GroupMember>()).Returns(call =>
            {
                var member = call.Arg<GroupMember>()
                    ?? throw new InvalidOperationException("Expected a membership to create.");
                member.Id = Guid.CreateVersion7();
                Members.Add(member);
                return member;
            });
            repository.Delete(Arg.Any<GroupMember>()).Returns(call =>
            {
                var member = call.Arg<GroupMember>()
                    ?? throw new InvalidOperationException("Expected a membership to delete.");
                member.IsDeleted = true;
                return Task.CompletedTask;
            });
            repository.Update(Arg.Any<GroupMember>()).Returns(Task.CompletedTask);

            var tenant = Substitute.For<ITenantContext>();
            tenant.TenantId.Returns(Participation.TenantId);
            var groups = Substitute.For<IGroupRepository>();
            groups.GetById(Participation.GroupId).Returns(
                new Group { Id = Participation.GroupId, FullName = "Member operations" });
            var participations = Substitute.For<IGroupTenantRepository>();
            participations.GetByGroupAndTenant(Participation.GroupId, Participation.TenantId,
                Arg.Any<CancellationToken>()).Returns(_ => Participates ? Participation : null);
            var services = OperationCompositionTests.Services(
                typeof(AddGroupMemberCommand), typeof(AddGroupMemberCommandHandler),
                typeof(DeleteGroupMemberCommand), typeof(DeleteGroupMemberCommandHandler),
                typeof(UpdateGroupMemberRoleCommand), typeof(UpdateGroupMemberRoleCommandHandler));
            services.AddSingleton(repository);
            services.AddSingleton(groups);
            services.AddSingleton(participations);
            services.AddSingleton(tenant);
            services.AddSingleton(Substitute.For<IUserContext>());
            services.AddSingleton(Substitute.For<IUserRepository>());
            return services;
        }
    }
}
