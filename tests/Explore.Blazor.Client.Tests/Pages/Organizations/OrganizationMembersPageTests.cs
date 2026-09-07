using System.Text.Json;
using Blazouter.Services;
using Explore.Blazor.Client.Helpers;
using Explore.Blazor.Client.Pages.Organizations;
using MudBlazor;

namespace Explore.Blazor.Client.Tests.Pages.Organizations;

public sealed class OrganizationMembersPageTests : IDisposable
{
    private readonly BlazorTestContext _ctx = new();
    private readonly IOrganizationMemberService _memberService = Substitute.For<IOrganizationMemberService>();
    private readonly Guid _userId = Guid.NewGuid();

    public OrganizationMembersPageTests()
    {
        _ctx.SetAuthenticatedUser(_userId, "Org Admin", "admin@example.com");
        _ctx.Services.AddSingleton(_memberService);
        _ctx.Services.AddSingleton(Substitute.For<IOrganizationService>());
        _ctx.Services.AddScoped<RouterStateService>();
        _ctx.Services.AddSingleton(Substitute.For<IDialogService>());
    }

    public void Dispose()
    {
        _ctx.Dispose();
    }

    [Test]
    public async Task WithoutHalActionLinks_HidesInviteAndRowActions_EvenForAdminRole()
    {
        _memberService.GetMembersWithAffordancesAsync(Arg.Any<Guid>())
            .Returns(new OrganizationMembersResult(
                [CreateMember(RoleHelper.OrgAdmin, withEditLink: false, withDeleteLink: false)],
                CanCreate: false));

        var cut = _ctx.RenderMudComponent<OrganizationMembers>();

        await Assert.That(cut.Markup.Contains("Invite Member", StringComparison.Ordinal)).IsFalse();
        await Assert.That(cut.Markup.Contains("Actions", StringComparison.Ordinal)).IsFalse();
        await Assert.That(cut.Markup.Contains("Change Role", StringComparison.Ordinal)).IsFalse();
        await Assert.That(cut.Markup.Contains("Remove", StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task WithHalActionLinks_ShowsInviteAndRowActions()
    {
        _memberService.GetMembersWithAffordancesAsync(Arg.Any<Guid>())
            .Returns(new OrganizationMembersResult(
                [CreateMember(RoleHelper.OrgAdmin, withEditLink: true, withDeleteLink: true)],
                CanCreate: true));

        var cut = _ctx.RenderMudComponent<OrganizationMembers>();

        await Assert.That(cut.Markup).Contains("Invite Member");
        await Assert.That(cut.Markup).Contains("Actions");
        await Assert.That(cut.FindAll("button.mud-menu-icon-button-activator").Count).IsEqualTo(1);
    }

    [Test]
    public async Task ExactHalRelations_RenderActions_ForSelfCreatorWithoutLocalInference()
    {
        _memberService.GetMembersWithAffordancesAsync(Arg.Any<Guid>())
            .Returns(new OrganizationMembersResult(
                [CreateMember(
                    RoleHelper.OrgCreator,
                    withEditLink: true,
                    withDeleteLink: true,
                    userId: _userId)],
                CanCreate: false));

        var cut = _ctx.RenderMudComponent<OrganizationMembers>();

        await Assert.That(cut.Markup).Contains("Actions");
        await Assert.That(cut.FindAll("button.mud-menu-icon-button-activator"))
            .HasSingleItem();
    }

    private static OrganizationMemberDto CreateMember(
        int roleId,
        bool withEditLink,
        bool withDeleteLink,
        Guid? userId = null)
    {
        var member = new OrganizationMemberDto
        {
            Id = Guid.NewGuid(),
            OrganizationId = Guid.NewGuid(),
            UserId = userId ?? Guid.NewGuid(),
            UserEmail = "member@example.com",
            UserFullName = "Member User",
            RoleId = roleId,
            RoleName = RoleHelper.GetRoleName(roleId),
            AdditionalProperties = new Dictionary<string, object>()
        };

        var links = new List<string>();
        if (withEditLink)
        {
            links.Add("\"edit\":{\"href\":\"/api/organizationmember/role\",\"method\":\"PUT\"}");
        }

        if (withDeleteLink)
        {
            links.Add("\"delete\":{\"href\":\"/api/organizationmember/1\",\"method\":\"DELETE\"}");
        }

        if (links.Count > 0)
        {
            using var doc = JsonDocument.Parse("{" + string.Join(',', links) + "}");
            member.AdditionalProperties["_links"] = doc.RootElement.Clone();
        }

        return member;
    }
}
