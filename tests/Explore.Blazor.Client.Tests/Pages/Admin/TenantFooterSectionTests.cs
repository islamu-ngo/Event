using System.Reflection;
using Explore.Blazor.Client.Contracts.Services.Footer;
using Explore.Blazor.Client.Pages.Admin.Tenant.Components;
using MudBlazor;

namespace Explore.Blazor.Client.Tests.Pages.Admin;

public sealed class TenantFooterSectionTests : IDisposable
{
    private readonly BlazorTestContext _ctx = new();
    private readonly IFooterAdminService _footerService = Substitute.For<IFooterAdminService>();
    private readonly List<PatchTenantFooterSettingsDto> _patches = [];

    public TenantFooterSectionTests()
    {
        _footerService.GetTenantFooterSettingsAsync(Arg.Any<CancellationToken>())
            .Returns(CreateSettings());
        _footerService.GetLinkGroupsAsync(Arg.Any<CancellationToken>())
            .Returns(Array.Empty<FooterLinkGroupListDto>());
        _footerService.PatchTenantFooterSettingsAsync(
                Arg.Do<PatchTenantFooterSettingsDto>(_patches.Add),
                Arg.Any<CancellationToken>())
            .Returns(new BaseCommandResponseOfGuid { Success = true });
        _footerService.ReorderLinkGroupsAsync(
                Arg.Any<IEnumerable<Guid>>(),
                Arg.Any<CancellationToken>())
            .Returns(new BaseCommandResponseOfGuid { Success = true });
        _ctx.Services.AddSingleton(_footerService);
    }

    public void Dispose() => _ctx.Dispose();

    [Test]
    public async Task GeneralSwitch_SavesCompleteGeneralGroupOnly()
    {
        var cut = RenderComponent();

        await cut.InvokeAsync(() => Switch(cut, "Footer Enabled").Instance.ValueChanged.InvokeAsync(false));

        await Assert.That(_patches.Count).IsEqualTo(1);
        var request = _patches[0];
        await Assert.That(request.General).IsNotNull();
        await Assert.That(request.General!.Enabled!.Value).IsFalse();
        await Assert.That(request.General.ShowCookieSettingsLink!.Value).IsTrue();
        await AssertOnlyGroupAsync(request, "general");
        var status = cut.Find("[role='status']");
        await Assert.That(status.TextContent).Contains("Footer settings saved.");
        await Assert.That(status.GetAttribute("aria-live")).IsEqualTo("polite");
        await Assert.That(status.GetAttribute("aria-atomic")).IsEqualTo("true");
    }

    [Test]
    public async Task TemplateSelection_SavesTemplateGroupOnly()
    {
        var cut = RenderComponent();

        await cut.InvokeAsync(() => Select(cut, "Template").Instance.ValueChanged.InvokeAsync("minimal"));

        await Assert.That(_patches.Count).IsEqualTo(1);
        await Assert.That(_patches[0].Template!.Value!.Value).IsEqualTo("minimal");
        await AssertOnlyGroupAsync(_patches[0], "template");
    }

    [Test]
    public async Task DescriptionText_BlurFlushesCompleteDescriptionGroupOnly()
    {
        var cut = RenderComponent();
        var field = TextField(cut, "Description Text");

        await cut.InvokeAsync(() => field.Instance.ValueChanged.InvokeAsync("Updated description"));
        field.Find("textarea").Blur();
        cut.WaitForState(() => _patches.Count == 1);

        await Assert.That(_patches[0].Description!.Show!.Value).IsTrue();
        await Assert.That(_patches[0].Description.Text!.Value).IsEqualTo("Updated description");
        await AssertOnlyGroupAsync(_patches[0], "description");
    }

    [Test]
    public async Task SocialEdits_CoalesceIntoOneCompleteSocialLinksGroup()
    {
        var cut = RenderComponent();
        var url = TextField(cut, "URL");
        var label = TextField(cut, "Label (optional)");

        await cut.InvokeAsync(() => url.Instance.ValueChanged.InvokeAsync("https://new.example"));
        await cut.InvokeAsync(() => label.Instance.ValueChanged.InvokeAsync("New label"));
        cut.WaitForState(() => _patches.Count == 1, TimeSpan.FromSeconds(2));

        await Assert.That(_patches.Count).IsEqualTo(1);
        var social = _patches[0].SocialLinks!;
        await Assert.That(social.Show!.Value).IsTrue();
        await Assert.That(social.Items!.Value!.Single().Url).IsEqualTo("https://new.example");
        await Assert.That(social.Items.Value.Single().Label).IsEqualTo("New label");
        await AssertOnlyGroupAsync(_patches[0], "socialLinks");
    }

    [Test]
    public async Task CopyrightText_BlurFlushesCopyrightGroupOnly()
    {
        var cut = RenderComponent();
        var field = TextField(cut, "Copyright Text");

        await cut.InvokeAsync(() => field.Instance.ValueChanged.InvokeAsync("Copyright 2026"));
        field.Find("input").Blur();
        cut.WaitForState(() => _patches.Count == 1);

        await Assert.That(_patches[0].Copyright!.Text!.Value).IsEqualTo("Copyright 2026");
        await AssertOnlyGroupAsync(_patches[0], "copyright");
    }

    [Test]
    public async Task ShowSwitches_SaveTheirCompleteOwningGroupsImmediately()
    {
        var cut = RenderComponent();

        await cut.InvokeAsync(() => Switch(cut, "Show Cookie Settings Link").Instance.ValueChanged.InvokeAsync(false));
        await cut.InvokeAsync(() => Switch(cut, "Show Description").Instance.ValueChanged.InvokeAsync(false));
        await cut.InvokeAsync(() => Switch(cut, "Show Social Links").Instance.ValueChanged.InvokeAsync(false));

        await Assert.That(_patches.Count).IsEqualTo(3);
        await Assert.That(_patches[0].General!.Enabled!.Value).IsTrue();
        await Assert.That(_patches[0].General.ShowCookieSettingsLink!.Value).IsFalse();
        await Assert.That(_patches[1].Description!.Show!.Value).IsFalse();
        await Assert.That(_patches[1].Description.Text!.Value).IsEqualTo("Initial description");
        await Assert.That(_patches[2].SocialLinks!.Show!.Value).IsFalse();
        await Assert.That(_patches[2].SocialLinks.Items!.Value!.Count).IsEqualTo(1);
        await AssertOnlyGroupAsync(_patches[0], "general");
        await AssertOnlyGroupAsync(_patches[1], "description");
        await AssertOnlyGroupAsync(_patches[2], "socialLinks");
    }

    [Test]
    public async Task MissingEdit_DisablesAllScalarMutations()
    {
        _footerService.GetTenantFooterSettingsAsync(Arg.Any<CancellationToken>())
            .Returns(CreateSettings(canEdit: false));
        var cut = RenderComponent();

        await Assert.That(cut.FindComponents<MudSwitch<bool>>().All(item => item.Instance.Disabled)).IsTrue();
        await cut.InvokeAsync(() => Switch(cut, "Footer Enabled").Instance.ValueChanged.InvokeAsync(false));
        await _footerService.DidNotReceive().PatchTenantFooterSettingsAsync(
            Arg.Any<PatchTenantFooterSettingsDto>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GroupLocks_DisableOnlyTheirOwningGroups()
    {
        _footerService.GetTenantFooterSettingsAsync(Arg.Any<CancellationToken>())
            .Returns(CreateSettings(lockTemplate: true, lockDescription: true, lockSocial: true, lockCopyright: true));
        var cut = RenderComponent();

        await Assert.That(Switch(cut, "Footer Enabled").Instance.Disabled).IsFalse();
        await Assert.That(Select(cut, "Template").Instance.Disabled).IsTrue();
        await Assert.That(Switch(cut, "Show Description").Instance.Disabled).IsTrue();
        await Assert.That(Switch(cut, "Show Social Links").Instance.Disabled).IsTrue();
        await Assert.That(TextField(cut, "Copyright Text").Instance.Disabled).IsTrue();
        await cut.InvokeAsync(() => Switch(cut, "Footer Enabled").Instance.ValueChanged.InvokeAsync(false));
        await Assert.That(_patches.Count).IsEqualTo(1);
        await Assert.That(_patches[0].General).IsNotNull();
    }

    [Test]
    public async Task FailedSave_RetainsLocalValueAndExposesPersistentAccessibleFeedback()
    {
        var pending = new TaskCompletionSource<BaseCommandResponseOfGuid>();
        _footerService.PatchTenantFooterSettingsAsync(
                Arg.Any<PatchTenantFooterSettingsDto>(),
                Arg.Any<CancellationToken>())
            .Returns(pending.Task);
        var cut = RenderComponent();
        var initialAlert = cut.Find("[role='alert']");
        await Assert.That(cut.FindAll("[role='alert']").Count).IsEqualTo(1);
        await Assert.That(initialAlert.TextContent).IsEmpty();
        await Assert.That(initialAlert.GetAttribute("aria-live")).IsEqualTo("assertive");
        await Assert.That(initialAlert.GetAttribute("aria-atomic")).IsEqualTo("true");

        var save = cut.InvokeAsync(() => Switch(cut, "Footer Enabled").Instance.ValueChanged.InvokeAsync(false));
        cut.WaitForState(() => cut.Find("[role='status']").TextContent.Contains("Saving", StringComparison.Ordinal));
        var status = cut.Find("[role='status']");
        await Assert.That(status.GetAttribute("aria-live")).IsEqualTo("polite");
        await Assert.That(status.GetAttribute("aria-atomic")).IsEqualTo("true");

        pending.SetResult(new BaseCommandResponseOfGuid { Success = false, Message = "Footer save failed." });
        await save;
        cut.WaitForState(() => cut.Find("[role='alert']").TextContent.Contains("Footer save failed.", StringComparison.Ordinal));

        await Assert.That(Switch(cut, "Footer Enabled").Instance.Value).IsFalse();
        await Assert.That(cut.FindAll("[role='alert']").Count).IsEqualTo(1);
        await Assert.That(cut.Find("[role='alert']").TextContent).Contains("Footer save failed.");
        await Assert.That(cut.FindAll("[role='status']").Count).IsEqualTo(1);
        await Assert.That(cut.Markup).DoesNotContain("Save Settings", StringComparison.Ordinal);
    }

    [Test]
    public async Task ConcurrentImmediateSaves_AreSerializedAndNewestFailureWins()
    {
        var firstRelease = new TaskCompletionSource();
        var callCount = 0;
        var active = 0;
        var maxActive = 0;
        _footerService.PatchTenantFooterSettingsAsync(
                Arg.Any<PatchTenantFooterSettingsDto>(),
                Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                var call = Interlocked.Increment(ref callCount);
                var nowActive = Interlocked.Increment(ref active);
                maxActive = Math.Max(maxActive, nowActive);
                try
                {
                    if (call == 1)
                    {
                        await firstRelease.Task;
                        return new BaseCommandResponseOfGuid { Success = true };
                    }

                    return new BaseCommandResponseOfGuid { Success = false, Message = "Newest save failed." };
                }
                finally
                {
                    Interlocked.Decrement(ref active);
                }
            });
        var cut = RenderComponent();

        var first = cut.InvokeAsync(() => Switch(cut, "Footer Enabled").Instance.ValueChanged.InvokeAsync(false));
        cut.WaitForState(() => callCount == 1);
        var second = cut.InvokeAsync(() => Select(cut, "Template").Instance.ValueChanged.InvokeAsync("minimal"));
        await Assert.That(callCount).IsEqualTo(1);

        firstRelease.SetResult();
        await Task.WhenAll(first, second);
        cut.WaitForState(() => cut.Find("[role='alert']").TextContent.Contains("Newest save failed.", StringComparison.Ordinal));

        await Assert.That(callCount).IsEqualTo(2);
        await Assert.That(maxActive).IsEqualTo(1);
        await Assert.That(cut.Find("[role='alert']").TextContent).Contains("Newest save failed.");
        await Assert.That(cut.Find("[role='status']").TextContent).DoesNotContain("saved", StringComparison.OrdinalIgnoreCase);
    }

    [Test]
    public async Task ManageLinkGroupsRelationPresent_AllowsReadNavigationAndExistingReorderFlow()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        _footerService.GetLinkGroupsAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new FooterLinkGroupListDto { Id = firstId, Title = "First", Order = 1, IsActive = true },
            new FooterLinkGroupListDto { Id = secondId, Title = "Second", Order = 2, IsActive = true }
        ]);
        _footerService.GetLinkGroupAsync(firstId, Arg.Any<CancellationToken>()).Returns(new FooterLinkGroupDetailsDto
        {
            Id = firstId,
            Title = "First",
            Links = [new FooterLinkItemDto { Id = Guid.NewGuid(), Label = "Docs", Url = "/docs", Order = 1 }]
        });
        var cut = RenderComponent();

        await Assert.That(Button(cut, "Create Group").HasAttribute("disabled")).IsFalse();
        await Assert.That(cut.Find("button[title='Move down']").HasAttribute("disabled")).IsFalse();
        await Assert.That(cut.FindAll("button[title='Edit']").All(button => !button.HasAttribute("disabled"))).IsTrue();
        cut.Find("button[title='Manage Links']").Click();
        cut.WaitForState(() => cut.Markup.Contains("Links in", StringComparison.Ordinal));
        await Assert.That(Button(cut, "Add Link").HasAttribute("disabled")).IsFalse();
        await Assert.That(cut.FindAll("button[title='Edit']").All(button => !button.HasAttribute("disabled"))).IsTrue();
        await _footerService.Received(1).GetLinkGroupAsync(
            firstId,
            Arg.Is<CancellationToken>(token => token.CanBeCanceled));

        cut.Find("button[title='Move down']").Click();

        await _footerService.Received(1).ReorderLinkGroupsAsync(
            Arg.Is<IEnumerable<Guid>>(ids => ids.SequenceEqual(new[] { secondId, firstId })),
            Arg.Is<CancellationToken>(token => token.CanBeCanceled));
        await _footerService.DidNotReceive().PatchTenantFooterSettingsAsync(
            Arg.Any<PatchTenantFooterSettingsDto>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ManageLinkGroupsRelationAbsent_DisablesAndGuardsEveryMutationCallback()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var link = new FooterLinkItemDto { Id = Guid.NewGuid(), Label = "Docs", Url = "/docs", Order = 1 };
        var first = new FooterLinkGroupListDto { Id = firstId, Title = "First", Order = 1, IsActive = true };
        var second = new FooterLinkGroupListDto { Id = secondId, Title = "Second", Order = 2, IsActive = true };
        _footerService.GetTenantFooterSettingsAsync(Arg.Any<CancellationToken>())
            .Returns(CreateSettings(canManageLinkGroups: false));
        _footerService.GetLinkGroupsAsync(Arg.Any<CancellationToken>()).Returns([first, second]);
        _footerService.GetLinkGroupAsync(firstId, Arg.Any<CancellationToken>()).Returns(new FooterLinkGroupDetailsDto
        {
            Id = firstId,
            Title = "First",
            Links = [link]
        });
        var cut = RenderComponent();

        await Assert.That(Button(cut, "Create Group").HasAttribute("disabled")).IsTrue();
        await Assert.That(cut.FindAll("button[title='Move up'], button[title='Move down'], button[title='Edit'], button[title='Delete']")
            .All(button => button.HasAttribute("disabled"))).IsTrue();
        var manageLinks = cut.Find("button[title='Manage Links']");
        await Assert.That(manageLinks.HasAttribute("disabled")).IsFalse();
        manageLinks.Click();
        cut.WaitForState(() => cut.Markup.Contains("Links in", StringComparison.Ordinal));
        await Assert.That(cut.Markup).Contains("Docs");
        await Assert.That(Button(cut, "Add Link").HasAttribute("disabled")).IsTrue();
        await Assert.That(cut.FindAll("button[title='Edit'], button[title='Delete']")
            .All(button => button.HasAttribute("disabled"))).IsTrue();

        await InvokePrivateAsync(cut.Instance, "OpenCreateGroupDialog");
        await InvokePrivateAsync(cut.Instance, "OpenEditGroupDialog", first);
        await InvokePrivateAsync(cut.Instance, "CreateGroupAsync", new CreateFooterLinkGroupRequest { Title = "Blocked" });
        await InvokePrivateAsync(cut.Instance, "UpdateGroupAsync", firstId, new PatchFooterLinkGroupDto { Title = new() { Value = "Blocked" } });
        await InvokePrivateAsync(cut.Instance, "DeleteGroup", first);
        await InvokePrivateAsync(cut.Instance, "MoveGroupUp", second);
        await InvokePrivateAsync(cut.Instance, "MoveGroupDown", first);
        await InvokePrivateAsync(cut.Instance, "SwapGroupOrder", 0, 1);
        await InvokePrivateAsync(cut.Instance, "OpenCreateLinkDialog");
        await InvokePrivateAsync(cut.Instance, "OpenEditLinkDialog", link);
        await InvokePrivateAsync(cut.Instance, "CreateLinkAsync", new CreateFooterLinkRequest { Label = "Blocked", Url = "/blocked" });
        await InvokePrivateAsync(cut.Instance, "UpdateLinkAsync", link.Id!.Value, new PatchFooterLinkDto
        {
            Label = new() { Value = "Blocked" },
            Url = new() { Value = "/blocked" }
        });
        await InvokePrivateAsync(cut.Instance, "DeleteLink", link);

        await _footerService.DidNotReceive().CreateLinkGroupAsync(Arg.Any<CreateFooterLinkGroupRequest>(), Arg.Any<CancellationToken>());
        await _footerService.DidNotReceive().UpdateLinkGroupAsync(Arg.Any<Guid>(), Arg.Any<PatchFooterLinkGroupDto>(), Arg.Any<CancellationToken>());
        await _footerService.DidNotReceive().DeleteLinkGroupAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _footerService.DidNotReceive().ReorderLinkGroupsAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>());
        await _footerService.DidNotReceive().CreateLinkAsync(Arg.Any<Guid>(), Arg.Any<CreateFooterLinkRequest>(), Arg.Any<CancellationToken>());
        await _footerService.DidNotReceive().UpdateLinkAsync(Arg.Any<Guid>(), Arg.Any<PatchFooterLinkDto>(), Arg.Any<CancellationToken>());
        await _footerService.DidNotReceive().DeleteLinkAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Dispose_CancelsInitialLoadTokensWithoutRenderingAnError()
    {
        CancellationToken settingsToken = default;
        CancellationToken groupsToken = default;
        _footerService.GetTenantFooterSettingsAsync(Arg.Any<CancellationToken>())
            .Returns(call => AwaitCancellationAsync<HalResourceOfTenantFooterSettingsDto?>(
                settingsToken = call.Arg<CancellationToken>()));
        _footerService.GetLinkGroupsAsync(Arg.Any<CancellationToken>())
            .Returns(call => AwaitCancellationAsync<IReadOnlyList<FooterLinkGroupListDto>>(
                groupsToken = call.Arg<CancellationToken>()));
        var cut = _ctx.RenderMudComponent<TenantFooterSection>();
        cut.WaitForState(() => settingsToken.CanBeCanceled && groupsToken.CanBeCanceled);

        cut.Instance.Dispose();

        await Assert.That(settingsToken.IsCancellationRequested).IsTrue();
        await Assert.That(groupsToken.IsCancellationRequested).IsTrue();
        cut.Dispose();
    }

    private IRenderedComponent<TenantFooterSection> RenderComponent()
    {
        var cut = _ctx.RenderMudComponent<TenantFooterSection>();
        cut.WaitForState(() => cut.Markup.Contains("Footer Enabled", StringComparison.Ordinal));
        return cut;
    }

    private static IRenderedComponent<MudSwitch<bool>> Switch(
        IRenderedComponent<TenantFooterSection> cut,
        string label) => cut.FindComponents<MudSwitch<bool>>().Single(item => item.Instance.Label == label);

    private static IRenderedComponent<MudSelect<string>> Select(
        IRenderedComponent<TenantFooterSection> cut,
        string label) => cut.FindComponents<MudSelect<string>>().Single(item => item.Instance.Label == label);

    private static IRenderedComponent<MudTextField<string>> TextField(
        IRenderedComponent<TenantFooterSection> cut,
        string label) => cut.FindComponents<MudTextField<string>>().Single(item => item.Instance.Label == label);

    private static AngleSharp.Dom.IElement Button(IRenderedComponent<TenantFooterSection> cut, string text) =>
        cut.FindAll("button").Single(button => button.TextContent.Trim().Equals(text, StringComparison.Ordinal));

    private static HalResourceOfTenantFooterSettingsDto CreateSettings(
        bool canEdit = true,
        bool canManageLinkGroups = true,
        bool lockTemplate = false,
        bool lockDescription = false,
        bool lockSocial = false,
        bool lockCopyright = false) => new()
        {
            TenantId = Guid.NewGuid(),
            Enabled = true,
            Template = "standard-3-col",
            ShowDescription = true,
            DescriptionText = "Initial description",
            ShowSocialLinks = true,
            SocialLinks =
            [
                new SocialLinks { Platform = "github", Url = "https://github.com/example", Label = "GitHub" }
            ],
            CopyrightText = "Initial copyright",
            ShowCookieSettingsLink = true,
            LockTenantTemplate = lockTemplate,
            LockTenantDescription = lockDescription,
            LockTenantSocialLinks = lockSocial,
            LockTenantCopyright = lockCopyright,
            _links = CreateLinks(canEdit, canManageLinkGroups)
        };

    private static Dictionary<string, HalLink> CreateLinks(bool canEdit, bool canManageLinkGroups)
    {
        var links = new Dictionary<string, HalLink>();
        if (canEdit) links["edit"] = new() { Href = "/api/footer/settings" };
        if (canManageLinkGroups) links["manage-link-groups"] = new() { Href = "/api/footer/link-groups" };
        return links;
    }

    private static async Task InvokePrivateAsync(TenantFooterSection component, string methodName, params object[] arguments)
    {
        var method = typeof(TenantFooterSection).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Method '{methodName}' was not found.");
        if (method.Invoke(component, arguments) is Task task)
        {
            await task;
        }
    }

    private static Task<T> AwaitCancellationAsync<T>(CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<T>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        cancellationToken.Register(
            () => completion.TrySetCanceled(cancellationToken));
        return completion.Task;
    }

    private static async Task AssertOnlyGroupAsync(PatchTenantFooterSettingsDto request, string expected)
    {
        await Assert.That(request.General is not null).IsEqualTo(expected == "general");
        await Assert.That(request.Template is not null).IsEqualTo(expected == "template");
        await Assert.That(request.Description is not null).IsEqualTo(expected == "description");
        await Assert.That(request.SocialLinks is not null).IsEqualTo(expected == "socialLinks");
        await Assert.That(request.Copyright is not null).IsEqualTo(expected == "copyright");
    }
}
