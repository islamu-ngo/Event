using Explore.Blazor.Client.Helpers;
using Explore.Blazor.Client.Pages.Events.Components;
using MudBlazor;
using System.Text.RegularExpressions;

namespace Explore.Blazor.Client.Tests.Pages.Event;

public sealed class EventTeamManagerTests : IDisposable
{
    private readonly BlazorTestContext _ctx = new();

    [Test]
    public async Task Render_WhenAssignLinkExistsButNoAssignablePresets_HidesAssignAction()
    {
        var eventId = Guid.NewGuid();
        var eventTeamService = Substitute.For<IEventTeamService>();
        eventTeamService.GetTeamMembersAsync(eventId, includeInactive: false)
            .Returns(TeamCollection([Member()], withAssignLink: true));
        eventTeamService.GetAssignablePresetsAsync(eventId)
            .Returns(new List<EventRolePresetDto>());
        _ctx.Services.AddSingleton(eventTeamService);

        var cut = _ctx.RenderMudComponent<EventTeamManager>(parameters => parameters
            .Add(component => component.EventId, eventId));

        cut.WaitForState(
            () => cut.Markup.Contains("manager@example.test", StringComparison.Ordinal),
            TimeSpan.FromSeconds(3));

        CaptureMarkup("hal-absent.html", cut.Markup);

        await Assert.That(cut.Markup).Contains("Event Team");
        await Assert.That(cut.Markup.Contains("Assign Role", StringComparison.Ordinal)).IsFalse();
        await Assert.That(cut.FindComponents<MudMenu>().Count).IsEqualTo(0);
    }

    [Test]
    public async Task Render_WhenAssignablePresetsExistButCollectionLinkIsMissing_HidesAssignAction()
    {
        var eventId = Guid.NewGuid();
        var eventTeamService = Substitute.For<IEventTeamService>();
        eventTeamService.GetTeamMembersAsync(eventId, includeInactive: false)
            .Returns(TeamCollection());
        eventTeamService.GetAssignablePresetsAsync(eventId)
            .Returns(new List<EventRolePresetDto>
            {
                new()
                {
                    RoleId = RoleHelper.RegistrationManager,
                    MasterCode = "event.registration_manager",
                    FullName = "Registration Manager",
                    PermissionCodes = new List<string> { "event_registration:manage" }
                }
            });
        _ctx.Services.AddSingleton(eventTeamService);

        var cut = _ctx.RenderMudComponent<EventTeamManager>(parameters => parameters
            .Add(component => component.EventId, eventId));

        cut.WaitForState(
            () => cut.Markup.Contains("No team members found", StringComparison.Ordinal),
            TimeSpan.FromSeconds(3));

        await Assert.That(cut.Markup.Contains("Assign Role", StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task Render_WhenCollectionAndMemberLinksExist_ShowsActionsForOwnerAndInactiveRows()
    {
        var eventId = Guid.NewGuid();
        var eventTeamService = Substitute.For<IEventTeamService>();
        eventTeamService.GetTeamMembersAsync(eventId, includeInactive: false)
            .Returns(TeamCollection(
                Member(roleId: RoleHelper.EventOwner, isEffective: false, email: "owner@example.test", withRevokeLink: true),
                Member(roleId: RoleHelper.EventManager, isEffective: false, email: "inactive@example.test", withRevokeLink: true),
                withAssignLink: true));
        eventTeamService.GetAssignablePresetsAsync(eventId)
            .Returns(new List<EventRolePresetDto>
            {
                new() { RoleId = RoleHelper.RegistrationManager, MasterCode = "event.registration_manager", FullName = "Registration Manager" }
            });
        _ctx.Services.AddSingleton(eventTeamService);

        var cut = _ctx.RenderMudComponent<EventTeamManager>(parameters => parameters
            .Add(component => component.EventId, eventId));

        cut.WaitForState(
            () => cut.Markup.Contains("inactive@example.test", StringComparison.Ordinal),
            TimeSpan.FromSeconds(3));

        CaptureMarkup("hal-present.html", cut.Markup);

        await Assert.That(cut.Markup).Contains("Assign Role");
        await Assert.That(cut.FindComponents<MudMenu>().Count).IsEqualTo(2);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AssignmentCompletion_RestoresFocusAndRefreshesOnlyOnSuccess(bool success)
    {
        var eventId = Guid.NewGuid();
        var service = Substitute.For<IEventTeamService>();
        service.GetTeamMembersAsync(eventId, false).Returns(
            TeamCollection([Member()], true),
            TeamCollection([Member(email: "assigned@example.test")], true));
        service.GetAssignablePresetsAsync(eventId).Returns(new List<EventRolePresetDto>
        {
            new() { RoleId = RoleHelper.RegistrationManager, FullName = "Registration Manager" }
        });
        var dialogs = Substitute.For<IDialogService>();
        var dialog = Substitute.For<IDialogReference>();
        dialog.Result.Returns(Task.FromResult<DialogResult?>(success ? DialogResult.Ok(true) : null));
        dialogs.ShowAsync<Explore.Blazor.Client.Pages.Events.Dialogs.AssignEventTeamRoleDialog>(
            Arg.Any<string>(), Arg.Any<DialogParameters>(), Arg.Any<DialogOptions>()).Returns(dialog);
        var focus = Substitute.For<Explore.Blazor.Client.Contracts.Services.Accessibility.IAccessibilityFocusService>();
        var restored = false;
        focus.RestoreFocusAsync().Returns(_ => { restored = true; return Task.CompletedTask; });
        _ctx.Services.AddSingleton(service);
        _ctx.Services.AddSingleton(dialogs);
        _ctx.Services.AddSingleton(focus);
        var cut = _ctx.RenderMudComponent<EventTeamManager>(p => p.Add(x => x.EventId, eventId));

        await cut.FindAll("button").Single(button => button.TextContent.Contains("Assign Role")).ClickAsync(new());

        await Assert.That(restored).IsTrue();
        await Assert.That(cut.Markup.Contains("assigned@example.test", StringComparison.Ordinal)).IsEqualTo(success);
        await Assert.That(cut.Markup.Contains("manager@example.test", StringComparison.Ordinal)).IsEqualTo(!success);
    }

    private static HalCollectionResourceOfEventTeamMemberDto TeamCollection(
        params HalResourceOfEventTeamMemberDto[] members)
        => TeamCollection(members, withAssignLink: false);

    private static HalCollectionResourceOfEventTeamMemberDto TeamCollection(
        HalResourceOfEventTeamMemberDto first,
        HalResourceOfEventTeamMemberDto second,
        bool withAssignLink)
        => TeamCollection([first, second], withAssignLink);

    private static HalCollectionResourceOfEventTeamMemberDto TeamCollection(
        HalResourceOfEventTeamMemberDto[] members,
        bool withAssignLink)
        => new()
        {
            _links = withAssignLink
                ? new Dictionary<string, HalLink> { ["assign-event-role"] = new() { Href = "/api/eventteam/assign", Method = "POST" } }
                : [],
            _embedded = new HalCollectionEmbeddedOfEventTeamMemberDto { Items = members }
        };

    private static HalResourceOfEventTeamMemberDto Member(
        int roleId = RoleHelper.EventManager,
        bool isEffective = true,
        string email = "manager@example.test",
        bool withRevokeLink = false)
        => new()
        {
            AssignmentId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            UserEmail = email,
            UserFullName = "Event Manager",
            RoleId = roleId,
            RoleName = "Manager",
            RoleMasterCode = "event.manager",
            Status = 2,
            StartsAtUtc = TestTime.UtcNow,
            IsEffective = isEffective,
            CreatedAt = TestTime.UtcNow,
            _links = withRevokeLink
                ? new Dictionary<string, HalLink> { ["revoke"] = new() { Href = "/api/eventteam/revoke", Method = "DELETE" } }
                : []
        };

    private static void CaptureMarkup(string fileName, string markup)
    {
        var evidenceDirectory = Environment.GetEnvironmentVariable("EVENT_TEAM_EVIDENCE_DIR");
        if (string.IsNullOrWhiteSpace(evidenceDirectory)) return;

        Directory.CreateDirectory(evidenceDirectory);
        var sanitized = Regex.Replace(markup, @"\s(?:id|for|blazor:[^=]+)=""[^""]*""", string.Empty);
        sanitized = Regex.Replace(sanitized, @"\b[\w.-]+@example\.test\b", "<redacted>");
        File.WriteAllText(Path.Combine(evidenceDirectory, fileName), sanitized);
    }

    public void Dispose() => _ctx.Dispose();
}
