using System.Text.Json;
using Blazouter.Services;
using Explore.Blazor.Client.Helpers;
using Explore.Blazor.Client.Contracts.Services.Accessibility;
using Explore.Blazor.Client.Pages.Organizations;
using Explore.Blazor.Client.Pages.Organizations.Dialogs;
using Microsoft.AspNetCore.Components.Web;
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

    [Test]
    [Arguments(false, "null")]
    [Arguments(true, "null")]
    [Arguments(false, "cancel")]
    [Arguments(true, "cancel")]
    [Arguments(false, "success")]
    [Arguments(true, "success")]
    public async Task DialogCompletion_RestoresFocusAndReloadsOnlyOnSuccess(bool editRole, string completion)
    {
        var member = CreateMember(RoleHelper.OrgAdmin, withEditLink: true, withDeleteLink: false);
        var refreshed = member with { UserFullName = "Refreshed Member" };
        bool opened = false;
        bool focusSaved = false;
        bool focusRestored = false;
        var focus = Substitute.For<IAccessibilityFocusService>();
        focus.SaveFocusAsync().Returns(_ =>
        {
            focusSaved = true;
            return Task.CompletedTask;
        });
        focus.RestoreFocusAsync().Returns(_ =>
        {
            focusRestored = focusSaved && opened;
            return Task.CompletedTask;
        });
        _ctx.Services.AddSingleton(focus);
        _memberService.GetMembersWithAffordancesAsync(Arg.Any<Guid>())
            .Returns(_ => new OrganizationMembersResult([opened ? refreshed : member], CanCreate: true));
        var reference = Substitute.For<IDialogReference>();
        reference.Result.Returns(Task.FromResult<DialogResult?>(completion switch
        {
            "null" => null,
            "cancel" => DialogResult.Cancel(),
            _ => DialogResult.Ok(true)
        }));
        var dialogs = _ctx.Services.GetRequiredService<IDialogService>();
        Task<IDialogReference> Open()
        {
            opened = true;
            return Task.FromResult(reference);
        }
        dialogs.ShowAsync<InviteMemberDialog>(
                Arg.Any<string>(), Arg.Any<DialogParameters>(), Arg.Any<DialogOptions>())
            .Returns(_ => Open());
        dialogs.ShowAsync<EditMemberRoleDialog>(
                Arg.Any<string>(), Arg.Any<DialogParameters>(), Arg.Any<DialogOptions>())
            .Returns(_ => Open());
        var cut = _ctx.RenderMudComponent<OrganizationMembers>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        if (editRole)
        {
            // The shared popover mock does not render portal content.
            var actions = _ctx.Render(cut.FindComponent<MudMenu>().Instance.ChildContent!);
            await cut.InvokeAsync(() => actions.FindComponent<MudMenuItem>()
                .Instance.OnClick.InvokeAsync(new MouseEventArgs())).WaitAsync(timeout.Token);
        }
        else
        {
            await cut.FindAll("button").Single(button => button.TextContent.Contains("Invite Member", StringComparison.Ordinal))
                .ClickAsync(new MouseEventArgs()).WaitAsync(timeout.Token);
        }

        await Assert.That(opened).IsTrue();
        await Assert.That(focusRestored).IsTrue();
        await Assert.That(cut.Markup).Contains(completion == "success" ? "Refreshed Member" : "Member User");
        if (completion != "success")
        {
            await Assert.That(cut.Markup).DoesNotContain("Refreshed Member");
        }
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
