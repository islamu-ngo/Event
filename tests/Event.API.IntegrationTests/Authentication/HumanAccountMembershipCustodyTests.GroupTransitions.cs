using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.GroupMembers.Handlers.Commands;
using Explore.Application.Features.GroupMembers.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

public sealed partial class HumanAccountMembershipCustodyTests
{
    [Test]
    public async Task RoleChangesAndDeletion_PreserveAnAdministratorAcrossMembershipTransitions()
    {
        await using var fixture = await CreateFixture();
        var seed = await SeedMembers(fixture, group: true, secondAdmin: false);
        string originalToken = await fixture.SignInAsync();
        string actor = fixture.Binding.LocalSubjectId.ToString("D");
        var demotion = new UpdateGroupMemberRoleCommand
        {
            RequesterUserId = actor,
            UpdateGroupMemberRoleDto = new() { Id = seed.Self, Role = RoleEnum.GroupMember }
        };
        var removal = new DeleteGroupMemberCommand { RequesterUserId = actor, MemberId = seed.Self };

        async Task<BaseCommandResponse<Guid>> Update(UpdateGroupMemberRoleCommand command)
        {
            await using var request = new Request(fixture.Host.Services);
            await request.Authenticate(originalToken);
            return await ActivatorUtilities.CreateInstance<UpdateGroupMemberRoleCommandHandler>(request.Services)
                .ExecuteAsync(command, default);
        }

        async Task<BaseCommandResponse<Guid>> Delete(DeleteGroupMemberCommand command, string token)
        {
            await using var request = new Request(fixture.Host.Services);
            await request.Authenticate(token);
            return await ActivatorUtilities.CreateInstance<DeleteGroupMemberCommandHandler>(request.Services)
                .ExecuteAsync(command, default);
        }

        var original = await ReadGroupMemberships(fixture);
        await Assert.That(original.Single().RoleId).IsEqualTo((int)RoleEnum.GroupAdmin);
        await Assert.That(original.Single().IsDeleted).IsFalse();
        await Assert.That((await Update(demotion)).IsSuccess).IsFalse();
        await Assert.That(await ReadGroupMemberships(fixture)).IsEquivalentTo(original);
        await Assert.That((await Delete(removal, originalToken)).IsSuccess).IsFalse();
        await Assert.That(await ReadGroupMemberships(fixture)).IsEquivalentTo(original);

        BaseCommandResponse<Guid> added;
        await using (var request = new Request(fixture.Host.Services))
        {
            await request.Authenticate(originalToken);
            added = await ActivatorUtilities.CreateInstance<AddGroupMemberCommandHandler>(request.Services)
                .ExecuteAsync(new()
                {
                    RequesterUserId = actor,
                    AddGroupMemberDto = new()
                    {
                        GroupId = seed.Owner,
                        Email = seed.OtherEmail,
                        Role = RoleEnum.GroupAdmin
                    }
                }, default);
        }
        await Assert.That(added.IsSuccess).IsTrue();
        var withSuccessor = await ReadGroupMemberships(fixture);
        var successor = withSuccessor.Single(row => row.Id == added.Id);
        await Assert.That(successor.UserId).IsEqualTo(seed.OtherUser);
        await Assert.That(successor.TenantId).IsEqualTo(PlatformDefaults.DefaultTenantId);
        await Assert.That(successor.GroupTenantId).IsEqualTo(original.Single().GroupTenantId);
        await Assert.That(successor.RoleId).IsEqualTo((int)RoleEnum.GroupAdmin);
        await Assert.That(successor.IsDeleted).IsFalse();
        await Assert.That(withSuccessor.Length).IsEqualTo(2);
        string successorToken = await fixture.SignInAsync(seed.OtherLogin);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            Update(demotion with { RequesterUserId = seed.OtherUser.ToString("D") }));
        await Assert.That(await ReadGroupMemberships(fixture)).IsEquivalentTo(withSuccessor);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            Delete(removal with { RequesterUserId = "not-a-user-id" }, originalToken));
        await Assert.That(await ReadGroupMemberships(fixture)).IsEquivalentTo(withSuccessor);

        var demoted = await Update(demotion);
        await Assert.That(demoted.IsSuccess).IsTrue();
        await Assert.That(demoted.Id).IsEqualTo(seed.Self);
        var afterDemotion = await ReadGroupMemberships(fixture);
        await Assert.That(afterDemotion.Single(row => row.Id == seed.Self).RoleId)
            .IsEqualTo((int)RoleEnum.GroupMember);
        await Assert.That(afterDemotion.Single(row => row.Id == seed.Self).IsDeleted).IsFalse();

        var removeSuccessor = new DeleteGroupMemberCommand
        {
            RequesterUserId = seed.OtherUser.ToString("D"),
            MemberId = successor.Id
        };
        await Assert.That((await Delete(removeSuccessor, successorToken)).IsSuccess).IsFalse();
        await Assert.That(await ReadGroupMemberships(fixture)).IsEquivalentTo(afterDemotion);
        await Assert.That((await Delete(removal, originalToken)).IsSuccess).IsFalse();
        await Assert.That(await ReadGroupMemberships(fixture)).IsEquivalentTo(afterDemotion);

        var removed = await Delete(removal with { RequesterUserId = seed.OtherUser.ToString("D") }, successorToken);
        await Assert.That(removed.IsSuccess).IsTrue();
        await Assert.That(removed.Id).IsEqualTo(seed.Self);
        var final = await ReadGroupMemberships(fixture);
        var deleted = final.Single(row => row.Id == seed.Self);
        await Assert.That(deleted.IsDeleted).IsTrue();
        await Assert.That(deleted.DeletedBy).IsEqualTo(seed.OtherUser);
        await Assert.That(deleted.RoleId).IsEqualTo((int)RoleEnum.GroupMember);
        await Assert.That(final.Where(row => !row.IsDeleted).Select(row => row.Id))
            .IsEquivalentTo(new[] { successor.Id });
        await Assert.That(final.Single(row => row.Id == successor.Id)).IsEqualTo(successor);
    }

    [Test]
    public async Task Addition_RequiresTenantParticipationAuthorityAndUniqueMembership()
    {
        await using var fixture = await CreateFixture();
        var seed = await SeedMembers(fixture, group: true, secondAdmin: false);
        string token = await fixture.SignInAsync();
        var command = new AddGroupMemberCommand
        {
            RequesterUserId = fixture.Binding.LocalSubjectId.ToString("D"),
            AddGroupMemberDto = new()
            {
                GroupId = seed.Owner,
                Email = seed.OtherEmail,
                Role = RoleEnum.GroupMember,
                GroupPositionId = 4
            }
        };
        async Task<BaseCommandResponse<Guid>> Add(AddGroupMemberCommand input)
        {
            await using var request = new Request(fixture.Host.Services);
            await request.Authenticate(token);
            return await ActivatorUtilities.CreateInstance<AddGroupMemberCommandHandler>(request.Services)
                .ExecuteAsync(input, default);
        }

        var original = await ReadGroupMemberships(fixture);
        Guid placement = original.Single().GroupTenantId;
        await using (var database = fixture.Native.CreateDatabase())
            await database.GroupTenants.IgnoreQueryFilters([QueryFilterNames.Tenant])
                .Where(row => row.Id == placement && row.TenantId == PlatformDefaults.DefaultTenantId)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsDeleted, true));
        await using (var request = new Request(fixture.Host.Services))
        {
            await request.Authenticate(token);
            await Assert.That(await request.Services.GetRequiredService<IGroupRepository>().GetById(seed.Owner))
                .IsNotNull();
            await Assert.That(await request.Services.GetRequiredService<IGroupTenantRepository>()
                .GetByGroupAndTenant(seed.Owner, PlatformDefaults.DefaultTenantId, default)).IsNull();
        }
        await Assert.That((await Add(command)).IsSuccess).IsFalse();
        await Assert.That(await ReadGroupMemberships(fixture)).IsEquivalentTo(original);
        await using (var database = fixture.Native.CreateDatabase())
            await database.GroupTenants.IgnoreQueryFilters([QueryFilterNames.Tenant, QueryFilterNames.SoftDelete])
                .Where(row => row.Id == placement && row.TenantId == PlatformDefaults.DefaultTenantId)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsDeleted, false));

        foreach (string requester in new[] { "invalid", seed.OtherUser.ToString("D") })
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                Add(command with { RequesterUserId = requester }));
            await Assert.That(await ReadGroupMemberships(fixture)).IsEquivalentTo(original);
        }
        await Assert.That((await Add(command with
        {
            AddGroupMemberDto = command.AddGroupMemberDto with { Email = "missing@example.test" }
        })).IsSuccess).IsFalse();
        await Assert.That(await ReadGroupMemberships(fixture)).IsEquivalentTo(original);

        var created = await Add(command);
        await Assert.That(created.IsSuccess).IsTrue();
        var afterAddition = await ReadGroupMemberships(fixture);
        var member = afterAddition.Single(row => row.Id == created.Id);
        await Assert.That(member.UserId).IsEqualTo(seed.OtherUser);
        await Assert.That(member.TenantId).IsEqualTo(PlatformDefaults.DefaultTenantId);
        await Assert.That(member.GroupTenantId).IsEqualTo(placement);
        await Assert.That(member.RoleId).IsEqualTo((int)RoleEnum.GroupMember);
        await Assert.That(member.GroupPositionId).IsEqualTo((int?)4);
        await Assert.That(member.IsDeleted).IsFalse();
        await Assert.That(afterAddition.Single(row => row.Id == seed.Self)).IsEqualTo(original.Single());
        await Assert.That(afterAddition.Length).IsEqualTo(2);
        await Assert.That((await Add(command)).IsSuccess).IsFalse();
        await Assert.That(await ReadGroupMemberships(fixture)).IsEquivalentTo(afterAddition);
    }

    private sealed record GroupMembershipImage(Guid Id, Guid UserId, Guid TenantId, Guid GroupTenantId,
        int RoleId, int? GroupPositionId, bool IsDeleted, DateTime CreatedAt, Guid? CreatedBy,
        DateTime? UpdatedAt, Guid? UpdatedBy, DateTime? DeletedAt, Guid? DeletedBy);

    private static async Task<GroupMembershipImage[]> ReadGroupMemberships(
        LocalIdentityLifecycleHttpFixture fixture)
    {
        await using var database = fixture.Native.CreateDatabase();
        return await database.GroupMembers.IgnoreQueryFilters([QueryFilterNames.Tenant, QueryFilterNames.SoftDelete])
            .AsNoTracking().Where(row => row.TenantId == PlatformDefaults.DefaultTenantId)
            .Select(row => new GroupMembershipImage(row.Id, row.UserId, row.TenantId, row.GroupTenantId,
                row.RoleId, row.GroupPositionId, row.IsDeleted, row.CreatedAt, row.CreatedBy,
                row.UpdatedAt, row.UpdatedBy, row.DeletedAt, row.DeletedBy)).ToArrayAsync();
    }
}
