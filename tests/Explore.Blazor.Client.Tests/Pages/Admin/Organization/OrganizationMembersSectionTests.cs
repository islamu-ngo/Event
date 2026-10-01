using System.Text.Json;
using System.Text.RegularExpressions;
using Explore.Blazor.Client.Helpers;
using Explore.Blazor.Client.Pages.Admin.Organization.Components;
using MudBlazor;

namespace Explore.Blazor.Client.Tests.Pages.Admin.Organization;

public sealed class OrganizationMembersSectionTests : IDisposable
{
    private readonly BlazorTestContext _ctx = new();
    private readonly IOrganizationMemberService _memberService = Substitute.For<IOrganizationMemberService>();
    private readonly Guid _authenticatedUserId = Guid.NewGuid();

    public OrganizationMembersSectionTests()
    {
        _ctx.SetAuthenticatedUser(_authenticatedUserId, "Org Admin", "admin@example.com");
        _ctx.Services.AddSingleton(_memberService);
        _ctx.Services.AddSingleton(Substitute.For<ISnackbar>());
        _ctx.Services.AddSingleton(Substitute.For<IDialogService>());
    }

    public void Dispose()
    {
        _ctx.Dispose();
    }

    [Test]
    public async Task WithoutHalActionLinks_HidesInviteAndRowActions_EvenForAdminRole()
    {
        var organizationId = Guid.NewGuid();
        _memberService.GetMembersWithAffordancesAsync(organizationId)
            .Returns(new OrganizationMembersResult(
                [CreateMember(RoleHelper.OrgAdmin, withEditLink: false, withDeleteLink: false)],
                CanCreate: false));

        var cut = Render(organizationId);

        WriteSanitizedMarkupEvidence("hal-absent.html", cut.Markup);
        await Assert.That(cut.Markup.Contains("Invite Member", StringComparison.Ordinal)).IsFalse();
        await Assert.That(cut.Markup.Contains("Actions", StringComparison.Ordinal)).IsFalse();
        await Assert.That(cut.Markup.Contains("Change Role", StringComparison.Ordinal)).IsFalse();
        await Assert.That(cut.Markup.Contains("Remove", StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task WithHalActionLinks_ShowsInviteAndRowActions()
    {
        var organizationId = Guid.NewGuid();
        _memberService.GetMembersWithAffordancesAsync(organizationId)
            .Returns(new OrganizationMembersResult(
                [CreateMember(RoleHelper.OrgAdmin, withEditLink: true, withDeleteLink: true)],
                CanCreate: true));

        var cut = Render(organizationId);

        WriteSanitizedMarkupEvidence("hal-present.html", cut.Markup);
        await Assert.That(cut.Markup).Contains("Invite Member");
        await Assert.That(cut.Markup).Contains("Actions");
        await Assert.That(cut.FindAll("button.mud-menu-icon-button-activator").Count).IsEqualTo(1);
    }

    [Test]
    public async Task WithHalActionLinks_ShowsActionsForCreatorAndCurrentUser()
    {
        var organizationId = Guid.NewGuid();
        _memberService.GetMembersWithAffordancesAsync(organizationId)
            .Returns(new OrganizationMembersResult(
                [CreateMember(RoleHelper.OrgCreator, withEditLink: true, withDeleteLink: true, userId: _authenticatedUserId)],
                CanCreate: false));

        var cut = Render(organizationId);

        WriteSanitizedMarkupEvidence("hal-present-creator-self.html", cut.Markup);
        await Assert.That(cut.Markup).Contains("Actions");
        await Assert.That(cut.FindAll("button.mud-menu-icon-button-activator").Count).IsEqualTo(1);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task DialogCompletion_RefreshesMembersOnlyOnSuccess(bool edit, bool success)
    {
        var organizationId = Guid.NewGuid();
        var member = CreateMember(RoleHelper.OrgAdmin, true, true);
        _memberService.GetMembersWithAffordancesAsync(organizationId).Returns(
            new OrganizationMembersResult([member], CanCreate: true),
            new OrganizationMembersResult([], CanCreate: true));
        var dialogs = _ctx.Services.GetRequiredService<IDialogService>();
        var dialog = Substitute.For<IDialogReference>();
        dialog.Result.Returns(Task.FromResult<DialogResult?>(success ? DialogResult.Ok(true) : null));
        dialogs.ShowAsync<Explore.Blazor.Client.Pages.Organizations.Dialogs.InviteMemberDialog>(
            Arg.Any<string>(), Arg.Any<DialogParameters>(), Arg.Any<DialogOptions>()).Returns(dialog);
        dialogs.ShowAsync<Explore.Blazor.Client.Pages.Organizations.Dialogs.EditMemberRoleDialog>(
            Arg.Any<string>(), Arg.Any<DialogParameters>(), Arg.Any<DialogOptions>()).Returns(dialog);
        var cut = Render(organizationId);

        if (edit)
        {
            await cut.InvokeAsync(() => EventCallback.Factory.Create(cut.Instance, async () =>
            {
                var method = typeof(OrganizationMembersSection).GetMethod(
                    "OpenEditRoleDialog",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                await (Task)method.Invoke(cut.Instance, [member])!;
            }).InvokeAsync());
        }
        else
        {
            await cut.FindAll("button").Single(button => button.TextContent.Contains("Invite Member")).ClickAsync(new());
        }

        await Assert.That(cut.Markup.Contains("Member User", StringComparison.Ordinal)).IsEqualTo(!success);
        await Assert.That(cut.Markup.Contains("No members found", StringComparison.Ordinal)).IsEqualTo(success);
    }

    private IRenderedComponent<OrganizationMembersSection> Render(Guid organizationId)
    {
        return _ctx.RenderMudComponent<OrganizationMembersSection>(parameters => parameters
            .Add(component => component.OrganizationId, organizationId));
    }

    private static OrganizationMemberDto CreateMember(int roleId, bool withEditLink, bool withDeleteLink, Guid? userId = null)
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

    private static void WriteSanitizedMarkupEvidence(string fileName, string markup)
    {
        var directory = Environment.GetEnvironmentVariable("AUTH_PLATFORM_EVIDENCE_DIR");
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        Directory.CreateDirectory(directory);
        var sanitized = Regex.Replace(markup, @"[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}", "[redacted-guid]", RegexOptions.IgnoreCase);
        sanitized = sanitized.Replace("member@example.com", "[redacted-email]", StringComparison.Ordinal);
        File.WriteAllText(Path.Combine(directory, fileName), sanitized);
    }
}
