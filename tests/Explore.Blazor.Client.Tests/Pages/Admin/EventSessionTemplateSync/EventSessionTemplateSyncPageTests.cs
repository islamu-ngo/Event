using Explore.Blazor.Client.Components.EventTemplateSync;
using Explore.Blazor.Client.Pages.Admin.EventSessionTemplateSync;
using Explore.Blazor.Client.Services.EventSessionTemplateSync;
using MudBlazor;

namespace Explore.Blazor.Client.Tests.Pages.Admin.EventSessionTemplateSync;

public sealed class EventSessionTemplateSyncPageTests : IDisposable
{
    private readonly BlazorTestContext _ctx;
    private readonly IEventSessionTemplateSyncService _templateSyncService;
    private readonly ISnackbar _snackbar;
    private readonly IDialogService _dialogService;

    public EventSessionTemplateSyncPageTests()
    {
        _ctx = new BlazorTestContext();
        _templateSyncService = Substitute.For<IEventSessionTemplateSyncService>();
        _snackbar = Substitute.For<ISnackbar>();
        _dialogService = Substitute.For<IDialogService>();

        _ctx.Services.AddSingleton(_templateSyncService);
        _ctx.Services.AddSingleton(_snackbar);
        _ctx.Services.AddSingleton(_dialogService);

        _ctx.SetAuthenticatedUser(Guid.NewGuid(), "Admin User", "admin@example.com");
    }

    public void Dispose()
    {
        _ctx.Dispose();
    }

    private IRenderedComponent<EventSessionTemplateSyncPage> RenderPage(Guid sessionId, int? version = null)
    {
        return _ctx.RenderMudComponent<EventSessionTemplateSyncPage>(p =>
        {
            p.Add(x => x.SessionId, sessionId);
            if (version.HasValue)
            {
                p.Add(x => x.TemplateVersion, version.Value);
            }
        });
    }

    [Test]
    public async Task EventSessionTemplateSyncPage_DiffFetchAndRender_ShowsCountsAndLocalChangesBanner()
    {
        var sessionId = Guid.NewGuid();
        var diff = CreateDiff(includeUntouched: true);

        _templateSyncService.GetDiffAsync(sessionId, 0).Returns(diff);

        var cut = RenderPage(sessionId);

        await Assert.That(cut.Markup).Contains("Warning: Session has 1 untouched local definitions");
        await Assert.That(cut.Markup).Contains("Modified (1)");
        await Assert.That(cut.Markup).Contains("Test.Field");
    }

    [Test]
    public async Task EventSessionTemplateSyncPage_HalLinkAbsence_ApplyButtonHidden()
    {
        var sessionId = Guid.NewGuid();
        var diff = CreateDiff();

        _templateSyncService.GetDiffAsync(sessionId, 0).Returns(diff);

        var cut = RenderPage(sessionId);

        var row = cut.FindComponent<TemplateDiffRow>();
        row.Find("input[type=\"checkbox\"]").Change(true);

        await Assert.That(cut.Markup).DoesNotContain("Apply Sync");
    }

    [Test]
    public async Task EventSessionTemplateSyncPage_HalLinkPresent_ApplyButtonVisibleAndEnabledOnSelection()
    {
        var sessionId = Guid.NewGuid();
        var diff = CreateDiff(canApply: true);

        _templateSyncService.GetDiffAsync(sessionId, 0).Returns(diff);

        var cut = RenderPage(sessionId);

        await Assert.That(cut.Markup).DoesNotContain("Apply Sync");

        var row = cut.FindComponent<TemplateDiffRow>();
        row.Find("input[type=\"checkbox\"]").Change(true);
        cut.WaitForState(() => cut.Markup.Contains("Apply Sync (1 changes)"));

        await Assert.That(cut.Markup).Contains("Apply Sync (1 changes)");
    }

    [Test]
    public async Task EventSessionTemplateSyncPage_SlugConfirmationEnforcement_CallsApplyOnSuccess()
    {
        var sessionId = Guid.NewGuid();
        var diff = CreateDiff(canApply: true);

        _templateSyncService.GetDiffAsync(sessionId, 0).Returns(diff);

        var dialogReference = Substitute.For<IDialogReference>();
        dialogReference.Result.Returns(DialogResult.Ok(true));
        _dialogService.ShowAsync<TemplateSyncConfirmationDialog>(Arg.Any<string>(), Arg.Any<DialogParameters>(), Arg.Any<DialogOptions>())
            .Returns(dialogReference);

        var cut = RenderPage(sessionId);

        var row = cut.FindComponent<TemplateDiffRow>();
        row.Find("input[type=\"checkbox\"]").Change(true);
        cut.WaitForState(() => cut.Markup.Contains("Apply Sync (1 changes)"));

        cut.Find("button.template-sync-page__apply-button").Click();

        await _dialogService.Received(1).ShowAsync<TemplateSyncConfirmationDialog>("Confirm Sync", Arg.Is<DialogParameters>(p =>
            p.Get<string>("ExpectedSlug") == "session-slug" &&
            p.Get<int>("TargetTemplateVersion") == 2 &&
            p.Get<int>("TotalChangesToApply") == 1
        ), Arg.Any<DialogOptions>());

        await _templateSyncService.Received(1).ApplySyncAsync(sessionId, Arg.Is<EventSessionTemplateSyncApplyRequest>(r =>
            r.Plan.TargetTemplateVersion == 2 &&
            r.Plan.BaseProvenanceVersion == 1 &&
            r.Plan.ModifiedDefinitionKeys.Contains("Field")
        ));
    }

    [Test]
    public async Task EventSessionTemplateSyncPage_409StaleSyncBase_ShowsReFetchBanner()
    {
        var sessionId = Guid.NewGuid();
        _templateSyncService.GetDiffAsync(sessionId, 0).Throws(CreateConflictException());

        var cut = RenderPage(sessionId);

        await Assert.That(cut.Markup).Contains("Template has been modified by another operator.");
        await Assert.That(cut.Markup).Contains("Reload Diff");
    }

    [Test]
    public async Task EventSessionTemplateSyncPage_409ConcurrentUpdateOnApply_ShowsReFetchBanner()
    {
        var sessionId = Guid.NewGuid();
        var diff = CreateDiff(canApply: true);

        _templateSyncService.GetDiffAsync(sessionId, 0).Returns(diff);

        var dialogReference = Substitute.For<IDialogReference>();
        dialogReference.Result.Returns(DialogResult.Ok(true));
        _dialogService.ShowAsync<TemplateSyncConfirmationDialog>(Arg.Any<string>(), Arg.Any<DialogParameters>(), Arg.Any<DialogOptions>())
            .Returns(dialogReference);

        _templateSyncService.ApplySyncAsync(sessionId, Arg.Any<EventSessionTemplateSyncApplyRequest>())
            .Throws(CreateConflictException());

        var cut = RenderPage(sessionId);

        var row = cut.FindComponent<TemplateDiffRow>();
        row.Find("input[type=\"checkbox\"]").Change(true);
        cut.WaitForState(() => cut.Markup.Contains("Apply Sync (1 changes)"));

        cut.Find("button.template-sync-page__apply-button").Click();
        cut.WaitForState(() => cut.Markup.Contains("Template has been modified by another operator."), TimeSpan.FromSeconds(3));

        await Assert.That(cut.Markup).Contains("Template has been modified by another operator.");
    }

    private static HalResourceOfTemplateDiffDto CreateDiff(
        bool canApply = false,
        bool includeUntouched = false) => new()
        {
            TargetTemplateVersion = 2,
            BaseProvenanceVersion = 1,
            ModifiedDefinitions =
            [
                new ModifiedDefinitionDto
                {
                    Namespace = "Test",
                    Key = "Field",
                    CurrentConcurrencyStamp = Guid.Empty,
                    FieldChanges =
                    [
                        new FieldChangeDto
                        {
                            FieldName = "Name",
                            OldValue = "Old",
                            NewValue = "New",
                            ValueType = "string"
                        }
                    ]
                }
            ],
            UntouchedLocalDefinitions = includeUntouched
                ? [new UntouchedLocalDefinitionDto { Namespace = "Local", Key = "Item", Reason = "Test" }]
                : [],
            _links = canApply
                ? new Dictionary<string, HalLink>
                {
                    ["sync-apply"] = new() { Href = "/apply", Method = "POST" }
                }
                : new Dictionary<string, HalLink>()
        };

    private static ApiException CreateConflictException() =>
        new("Conflict", 409, null, new Dictionary<string, IEnumerable<string>>(), null);
}
