using System.Text.Json;
using Explore.Blazor.Client.Helpers;

namespace Explore.Blazor.Client.Tests.Services;

public class OrganizationMemberServiceTests
{
    private readonly IOrganizationMemberClient _apiClient;
    private readonly OrganizationMemberService _service;

    public OrganizationMemberServiceTests()
    {
        _apiClient = Substitute.For<IOrganizationMemberClient>();
        var logger = Substitute.For<ILogger<OrganizationMemberService>>();
        _service = new OrganizationMemberService(_apiClient, logger);
    }

    [Test]
    public async Task GetMembersWithAffordancesAsync_PreservesCollectionCreateAndItemLinks()
    {
        var organizationId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        _apiClient.GetOrganizationMembersByOrganizationAsync(organizationId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(CreateOrganizationMemberCollection(memberId, withCreateLink: true, withItemLinks: true));

        var result = await _service.GetMembersWithAffordancesAsync(organizationId);

        await Assert.That(result.CanCreate).IsTrue();
        await Assert.That(result.Members.Count).IsEqualTo(1);
        var member = result.Members.Single();
        await Assert.That(member.Id).IsEqualTo(memberId);
        await Assert.That(member.HasHalLink("edit")).IsTrue();
        await Assert.That(member.HasHalLink("delete")).IsTrue();
    }

    [Test]
    public async Task GetMembersAsync_ReturnsMembersForExistingCompatibilityCallers()
    {
        var organizationId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        _apiClient.GetOrganizationMembersByOrganizationAsync(organizationId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(CreateOrganizationMemberCollection(memberId, withCreateLink: false, withItemLinks: false));

        var result = await _service.GetMembersAsync(organizationId);

        await Assert.That(result.Count).IsEqualTo(1);
        await Assert.That(result.Single().Id).IsEqualTo(memberId);
    }

    [Test]
    public async Task OrganizationMembersResult_CopiesThePublishedMemberSnapshot()
    {
        var member = new OrganizationMemberDto { Id = Guid.NewGuid() };
        var source = new List<OrganizationMemberDto> { member };
        var result = new OrganizationMembersResult(source, CanCreate: true);

        source.Clear();

        await Assert.That(result.Members).Contains(member);
        await Assert.That(result.Members.Count).IsEqualTo(1);
        Assert.Throws<NotSupportedException>(() => ((ICollection<OrganizationMemberDto>)result.Members).Clear());
    }

    [Test]
    public async Task GetMembersWithAffordancesAsync_ReturnsEmptyResult_WhenApiThrows()
    {
        var organizationId = Guid.NewGuid();
        _apiClient.GetOrganizationMembersByOrganizationAsync(organizationId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ApiException("API Error", 500, null, null, null));

        var result = await _service.GetMembersWithAffordancesAsync(organizationId);

        await Assert.That(result.CanCreate).IsFalse();
        await Assert.That(result.Members).IsEmpty();
    }

    [Test]
    public async Task GetMyInvitationsAsync_ReturnsInvitations_WhenApiSucceeds()
    {
        var invitations = new List<OrganizationInvitationDto>
        {
            new() { Id = Guid.NewGuid(), OrganizationId = Guid.NewGuid(), Email = "user1@example.com" },
            new() { Id = Guid.NewGuid(), OrganizationId = Guid.NewGuid(), Email = "user2@example.com" }
        };

        _apiClient.GetMyOrganizationInvitationsAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(invitations);

        var result = await _service.GetMyInvitationsAsync();

        await Assert.That(result.Count).IsEqualTo(2);
    }

    [Test]
    public async Task GetMyInvitationsAsync_ReturnsEmptyList_WhenApiThrows()
    {
        _apiClient.GetMyOrganizationInvitationsAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ApiException("API Error", 500, null, null, null));

        var result = await _service.GetMyInvitationsAsync();

        await Assert.That(result).IsEmpty();
    }

    [Test]
    public async Task InviteMemberAsync_ReturnsResponse_WhenApiSucceeds()
    {
        var dto = new AddOrganizationMemberDto
        {
            OrganizationId = Guid.NewGuid(),
            Email = "test@example.com"
        };
        var expected = ComponentDataBuilder.SuccessResponse();

        _apiClient.AddOrganizationMemberAsync(dto, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(expected);

        var result = await _service.InviteMemberAsync(dto);

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Success).IsTrue();
    }

    [Test]
    public async Task InviteMemberAsync_Throws_WhenApiThrows()
    {
        var dto = new AddOrganizationMemberDto
        {
            OrganizationId = Guid.NewGuid(),
            Email = "test@example.com"
        };
        _apiClient.AddOrganizationMemberAsync(dto, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ApiException("Bad Request", 400, null, null, null));

        await Assert.ThrowsAsync<ApiException>(async () => await _service.InviteMemberAsync(dto));
    }

    [Test]
    public async Task AcceptInvitationAsync_Throws_WhenApiThrows()
    {
        var invitationId = Guid.NewGuid();
        _apiClient.AcceptOrganizationInvitationAsync(invitationId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ApiException("Not Found", 404, null, null, null));

        await Assert.ThrowsAsync<ApiException>(async () => await _service.AcceptInvitationAsync(invitationId));
    }

    [Test]
    public async Task DeclineInvitationAsync_Throws_WhenApiThrows()
    {
        var invitationId = Guid.NewGuid();
        _apiClient.DeclineOrganizationInvitationAsync(invitationId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ApiException("Not Found", 404, null, null, null));

        await Assert.ThrowsAsync<ApiException>(async () => await _service.DeclineInvitationAsync(invitationId));
    }

    [Test]
    public async Task DeleteMemberAsync_Throws_WhenApiThrows()
    {
        var memberId = Guid.NewGuid();
        _apiClient.DeleteOrganizationMemberAsync(memberId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ApiException("Forbidden", 403, null, null, null));

        await Assert.ThrowsAsync<ApiException>(async () => await _service.DeleteMemberAsync(memberId));
    }

    private static HalCollectionResourceOfOrganizationMemberDto CreateOrganizationMemberCollection(Guid memberId, bool withCreateLink, bool withItemLinks)
    {
        var collection = new HalCollectionResourceOfOrganizationMemberDto
        {
            _links = withCreateLink
                ? new Dictionary<string, HalLink>
                {
                    ["create"] = new() { Href = "/api/organizationmember", Method = "POST" }
                }
                : new Dictionary<string, HalLink>(),
            _embedded = new HalCollectionEmbeddedOfOrganizationMemberDto
            {
                Items =
                [
                    new HalResourceOfOrganizationMemberDto
                    {
                        Id = memberId,
                        OrganizationId = Guid.NewGuid(),
                        UserId = Guid.NewGuid(),
                        UserEmail = "member@example.com",
                        RoleId = RoleHelper.OrgAdmin
                    }
                ]
            }
        };

        if (withItemLinks)
        {
            using var links = JsonDocument.Parse("{\"edit\":{\"href\":\"/api/organizationmember/role\",\"method\":\"PUT\"},\"delete\":{\"href\":\"/api/organizationmember/1\",\"method\":\"DELETE\"}}");
            collection._embedded.Items.Single().AdditionalProperties["_links"] = links.RootElement.Clone();
        }

        return collection;
    }
}
