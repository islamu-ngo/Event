using System.Text.Json;
using Explore.Blazor.Client.Helpers;

namespace Explore.Blazor.Client.Tests.Services;

public sealed class GroupServiceTests
{
    private readonly IGroupClient _apiClient;
    private readonly IGroupMemberClient _groupMemberClient;
    private readonly GroupService _service;

    public GroupServiceTests()
    {
        _apiClient = Substitute.For<IGroupClient>();
        _groupMemberClient = Substitute.For<IGroupMemberClient>();
        var logger = Substitute.For<ILogger<GroupService>>();
        _service = new GroupService(_apiClient, _groupMemberClient, logger);
    }

    [Test]
    public async Task GetMyGroupsAsync_UsesGeneratedClientAndMapsHalItems()
    {
        var groupId = Guid.NewGuid();
        _apiClient.GetMyGroupsAsync(
                1,
                100,
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(new HalCollectionResourceOfGroupListDto
            {
                _embedded = new HalCollectionEmbeddedOfGroupListDto
                {
                    Items =
                    [
                        new HalResourceOfGroupListDto
                        {
                            Id = groupId,
                            FullName = "Community Group",
                            CurrentUserRoleId = 2
                        }
                    ]
                }
            });

        var groups = await _service.GetMyGroupsAsync();

        await Assert.That(groups.Count).IsEqualTo(1);
        var group = groups.Single();
        await Assert.That(group.Id).IsEqualTo(groupId);
        await Assert.That(group.FullName).IsEqualTo("Community Group");
        await Assert.That(group.CurrentUserRoleId).IsEqualTo(2);
        await _apiClient.Received(1).GetMyGroupsAsync(
            1,
            100,
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetGroupDetailsAsync_UsesGeneratedClientAndPreservesHalLinks()
    {
        var groupId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var expected = new HalResourceOfGroupDto
        {
            Id = groupId,
            FullName = "Community Group",
            Description = "Group description",
            ActorId = actorId,
            _links = new Dictionary<string, HalLink>
            {
                ["edit"] = new() { Href = $"/api/Group/{groupId}", Method = "PUT" },
                ["delete"] = new() { Href = $"/api/Group/{groupId}", Method = "DELETE" }
            }
        };
        _apiClient.GetGroupByIdAsync(
                groupId,
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(expected);

        var group = await _service.GetGroupDetailsAsync(groupId);

        await Assert.That(group).IsSameReferenceAs(expected);
        await Assert.That(group!.Id).IsEqualTo(groupId);
        await Assert.That(group.ActorId).IsEqualTo(actorId);
        await Assert.That(group.HasHalLink("edit")).IsTrue();
        await Assert.That(group.HasHalLink("delete")).IsTrue();
    }

    [Test]
    public async Task GetGroupMembersWithAffordancesAsync_PreservesCollectionCreateAndItemLinks()
    {
        var groupId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var response = CreateGroupMemberCollection(memberId, withCreateLink: true, withItemLinks: true);

        _groupMemberClient.GetGroupMembersAsync(groupId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(response);

        var result = await _service.GetGroupMembersWithAffordancesAsync(groupId);

        await Assert.That(result.CanCreate).IsTrue();
        await Assert.That(result.Members.Count).IsEqualTo(1);
        var member = result.Members.Single();
        await Assert.That(member.Id).IsEqualTo(memberId);
        await Assert.That(member.HasHalLink("edit")).IsTrue();
        await Assert.That(member.HasHalLink("delete")).IsTrue();
    }

    [Test]
    public async Task GetGroupMembersAsync_ReturnsMembersForExistingCompatibilityCallers()
    {
        var groupId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        _groupMemberClient.GetGroupMembersAsync(groupId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(CreateGroupMemberCollection(memberId, withCreateLink: false, withItemLinks: false));

        var members = await _service.GetGroupMembersAsync(groupId);

        await Assert.That(members.Count).IsEqualTo(1);
        await Assert.That(members.Single().Id).IsEqualTo(memberId);
    }

    [Test]
    public async Task GroupMembersResult_CopiesThePublishedMemberSnapshot()
    {
        var member = new GroupMemberDto { Id = Guid.NewGuid() };
        var source = new List<GroupMemberDto> { member };
        var result = new GroupMembersResult(source, CanCreate: true);

        source.Clear();

        await Assert.That(result.Members).Contains(member);
        await Assert.That(result.Members.Count).IsEqualTo(1);
        Assert.Throws<NotSupportedException>(() => ((ICollection<GroupMemberDto>)result.Members).Clear());
    }

    [Test]
    public async Task GetGroupMembersWithAffordancesAsync_ReturnsEmptyResult_WhenApiFails()
    {
        var groupId = Guid.NewGuid();
        _groupMemberClient.GetGroupMembersAsync(groupId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new Explore.Blazor.Client.Clients.ApiException("API Error", 500, null, null, null));

        var result = await _service.GetGroupMembersWithAffordancesAsync(groupId);

        await Assert.That(result.CanCreate).IsFalse();
        await Assert.That(result.Members).IsEmpty();
    }

    private static HalCollectionResourceOfGroupMemberDto CreateGroupMemberCollection(Guid memberId, bool withCreateLink, bool withItemLinks)
    {
        var collection = new HalCollectionResourceOfGroupMemberDto
        {
            _links = withCreateLink
                ? new Dictionary<string, HalLink>
                {
                    ["create"] = new() { Href = "/api/groupmember", Method = "POST" }
                }
                : new Dictionary<string, HalLink>(),
            _embedded = new HalCollectionEmbeddedOfGroupMemberDto
            {
                Items =
                [
                    new HalResourceOfGroupMemberDto
                    {
                        Id = memberId,
                        UserEmail = "member@example.com",
                        RoleId = RoleHelper.GroupAdmin
                    }
                ]
            }
        };

        if (withItemLinks)
        {
            using var links = JsonDocument.Parse("{\"edit\":{\"href\":\"/api/groupmember/1\",\"method\":\"PUT\"},\"delete\":{\"href\":\"/api/groupmember/1\",\"method\":\"DELETE\"}}");
            collection._embedded.Items.Single().AdditionalProperties["_links"] = links.RootElement.Clone();
        }

        return collection;
    }

}
