using Explore.Application.Contracts.Identity;
using Explore.Application.Features.GroupMembers.Handlers.Commands;
using Explore.Domain.Enums;

namespace Event.Application.UnitTests.Operations;

public sealed class NativeGroupMemberMutationTests
{
    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task MissingAuthenticatedIdentityCannotReachMembershipRepositories(bool authenticated, bool emptyId)
    {
        var caller = new CallerContext
        {
            IsAuthenticated = authenticated,
            UserId = emptyId ? Guid.Empty : authenticated ? null : Guid.CreateVersion7()
        };
        var add = new AddGroupMemberCommandHandler(null!, null!, null!, null!, caller, null!, null!);
        var update = new UpdateGroupMemberRoleCommandHandler(null!, caller, null!, null!);
        var delete = new DeleteGroupMemberCommandHandler(null!, caller, null!, null!);
        await Assert.That((await add.ExecuteAsync(new()
        {
            AddGroupMemberDto = new() { GroupId = Guid.CreateVersion7(), Email = "member@example.test", Role = RoleEnum.GroupMember }
        }, default)).IsSuccess).IsFalse();
        await Assert.That((await update.ExecuteAsync(new()
        {
            UpdateGroupMemberRoleDto = new() { Id = Guid.CreateVersion7(), Role = RoleEnum.GroupMember }
        }, default)).IsSuccess).IsFalse();
        await Assert.That((await delete.ExecuteAsync(new() { MemberId = Guid.CreateVersion7() }, default)).IsSuccess).IsFalse();
    }

    private sealed class CallerContext : IUserContext
    {
        public Guid? UserId { get; set; }
        public bool IsAuthenticated { get; set; }
        public string? Email => null;
        public string? Username => null;
        public Guid GetRequiredUserId() => IsAuthenticated && UserId is Guid id && id != Guid.Empty
            ? id : throw new UnauthorizedAccessException();
    }
}
