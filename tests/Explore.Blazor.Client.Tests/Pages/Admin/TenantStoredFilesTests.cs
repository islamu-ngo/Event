using System.Net;
using System.Text;
using Microsoft.AspNetCore.Components.Web;
using Explore.Blazor.Client.Pages.Admin.Tenant.Components;
using Explore.Blazor.Client.Contracts.Services.Accessibility;
using MudBlazor;

namespace Explore.Blazor.Client.Tests.Pages.Admin;

public sealed class TenantStoredFilesTests : IDisposable
{
    private readonly BlazorTestContext _ctx = new();
    private readonly Queue<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> _responses = new();
    private readonly Guid _fileId = Guid.NewGuid();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Handler _handler;
    private readonly HttpClient _http;

    public TenantStoredFilesTests()
    {
        _handler = new Handler(_responses);
        _http = new HttpClient(_handler) { BaseAddress = new Uri("https://bff.example.test/") };
        _ctx.Services.AddSingleton<ITenantStorageSettingsAdminService>(new TenantStorageSettingsAdminService(
            Substitute.For<ITenantStorageSettingsClient>(),
            new StorageObjectClient(_http),
            Substitute.For<ILogger<TenantStorageSettingsAdminService>>()));
    }

    public void Dispose()
    {
        _ctx.Dispose();
        _http.Dispose();
        _handler.Dispose();
    }

    [Test]
    public async Task WithheldDeleteLink_DoesNotOfferRetirementDespiteEditLink()
    {
        Enqueue(Page(delete: false));
        var cut = await RenderAsync();

        await Assert.That(cut.FindAll("[data-action='retire']").Count).IsEqualTo(0);
        await Assert.That(cut.Find("[data-state='unavailable']").TextContent).IsNotEmpty();
        await Assert.That(_responses.Count).IsEqualTo(0);
    }

    [Test]
    public async Task DetailWithheldDeleteLink_DoesNotOfferConfirmation()
    {
        Enqueue(Page());
        Enqueue(Detail(delete: false));
        Enqueue(Page(delete: false));
        var cut = await RenderAsync();
        await BeginAsync(cut);

        await Assert.That(cut.FindAll("[data-action='confirm']").Count).IsEqualTo(0);
        await Assert.That(cut.FindAll("[data-action='retire']").Count).IsEqualTo(0);
    }

    [Test]
    public async Task AcceptedRetirement_RemovesMetadataAndAnnouncesPendingCleanup()
    {
        Enqueue(Page());
        Enqueue(Detail());
        Enqueue("""{"success":true}""", HttpStatusCode.Accepted);
        Enqueue("""{"pageNumber":1,"pageSize":20,"_embedded":{"items":[]}}""");
        var cut = await RenderAsync();
        await BeginAsync(cut);
        await ClickAsync(cut, "confirm");

        await Assert.That(cut.FindAll("[data-action='retire']").Count).IsEqualTo(0);
        await Assert.That(cut.Find("[data-outcome='pending']").TextContent).IsNotEmpty();
        await Assert.That(_responses.Count).IsEqualTo(0);
    }

    [Test]
    [Arguments("storage_object_in_use", "in-use")]
    [Arguments("storage_object_retention_blocked", "retention")]
    [Arguments("storage_object_invalid_target", "invalid-target")]
    [Arguments("private_unrecognized_reason", "failed")]
    public async Task StaleConflict_UsesBoundedFeedbackAndRefreshesHal(string code, string outcome)
    {
        Enqueue(Page());
        Enqueue(Detail());
        Enqueue($$"""{"status":409,"code":"{{code}}","detail":"Private owner Alice"}""", HttpStatusCode.Conflict);
        Enqueue(Page(delete: false));
        var cut = await RenderAsync();
        await BeginAsync(cut);
        await ClickAsync(cut, "confirm");

        await Assert.That(cut.Find($"[data-outcome='{outcome}']").TextContent).IsNotEmpty();
        await Assert.That(cut.Markup).DoesNotContain("Alice");
        await Assert.That(cut.FindAll("[data-action='retire']").Count).IsEqualTo(0);
        await Assert.That(_responses.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Pagination_FollowsAdvertisedPageInsteadOfIncrementing()
    {
        Enqueue(Page(next: "/api/storageobject?PageNumber=4&PageSize=7"));
        _responses.Enqueue((request, _) =>
        {
            if (request.RequestUri!.Query != "?PageNumber=4&PageSize=7")
                throw new InvalidOperationException("Did not consume the advertised page.");
            return Task.FromResult(Response("""{"pageNumber":4,"pageSize":7,"_embedded":{"items":[]}}"""));
        });
        var cut = await RenderAsync();
        await ClickAsync(cut, "next");

        await Assert.That(cut.FindAll("[data-action='next']").Count).IsEqualTo(0);
        await Assert.That(_responses.Count).IsEqualTo(0);
    }

    [Test]
    public async Task TenantSwitch_DiscardsAnOldInFlightPage()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var oldPage = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        _responses.Enqueue((_, _) =>
        {
            started.SetResult();
            return oldPage.Task;
        });
        var cut = _ctx.RenderMudComponent<TenantStoredFiles>(p => p.Add(x => x.TenantId, _tenantId));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Enqueue("""{"pageNumber":1,"pageSize":20,"_embedded":{"items":[]}}""");
        var switched = Signal(cut, () => cut.FindAll("[data-state='empty']").Count == 1);
        await cut.InvokeAsync(() => cut.Render(parameters => parameters.Add(x => x.TenantId, Guid.NewGuid())));
        await switched.WaitAsync(TimeSpan.FromSeconds(5));
        var settled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        cut.OnMarkupUpdated += (_, _) => settled.TrySetResult();
        using var oldResponse = Response(Page());
        oldPage.SetResult(oldResponse);
        await settled.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(cut.FindAll("[data-action='retire']").Count).IsEqualTo(0);
        await Assert.That(cut.FindAll("[data-state='empty']").Count).IsEqualTo(1);
    }

    [Test]
    public async Task TenantSwitch_DiscardsAnOldAcceptedRetirementWithoutRefreshingNewTenant()
    {
        Enqueue(Page());
        Enqueue(Detail());
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var accepted = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        _responses.Enqueue((_, _) =>
        {
            started.SetResult();
            return accepted.Task;
        });
        var cut = await RenderAsync();
        await BeginAsync(cut);
        var retirement = ClickAsync(cut, "confirm");
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Enqueue("""{"pageNumber":1,"pageSize":20,"_embedded":{"items":[]}}""");
        var switched = Signal(cut, () => cut.FindAll("[data-state='empty']").Count == 1);
        await cut.InvokeAsync(() => cut.Render(p => p.Add(x => x.TenantId, Guid.NewGuid())));
        await switched.WaitAsync(TimeSpan.FromSeconds(5));
        using var acceptedResponse = Response("""{"success":true}""", HttpStatusCode.Accepted);
        accepted.SetResult(acceptedResponse);
        await retirement.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(cut.FindAll("[data-action='confirm']").Count).IsEqualTo(0);
        await Assert.That(cut.FindAll("[data-outcome='pending']").Count).IsEqualTo(0);
        await Assert.That(cut.FindAll("[data-state='empty']").Count).IsEqualTo(1);
        await Assert.That(_responses.Count).IsEqualTo(0);
    }

    [Test]
    public async Task ParentStorageSection_IntegratesFilesWithoutEnablingPolicyEditing()
    {
        Enqueue(Page());
        var cut = _ctx.RenderMudComponent<TenantStorageSection>(p => p.Add(x => x.Model,
            new HalResourceOfTenantStorageSettingsDto { TenantStorageLocked = true }));
        var loaded = Signal(cut, () => cut.FindAll("[data-action='retire']").Count == 1);
        await cut.InvokeAsync(() => cut.Render(p => p.Add(x => x.Model,
            new HalResourceOfTenantStorageSettingsDto { TenantId = _tenantId, TenantStorageLocked = true })));
        await loaded.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(cut.FindComponents<TenantStoredFiles>().Count).IsEqualTo(1);
        await Assert.That(cut.FindAll("[data-action='retire']").Count).IsEqualTo(1);
        await Assert.That(cut.FindComponents<MudSelect<string>>()[0].Instance.Disabled).IsTrue();
    }

    [Test]
    public async Task Confirmation_CancelRestoresFocusWithoutRequestingRetirement()
    {
        Enqueue(Page());
        Enqueue(Detail());
        var cut = await RenderAsync();
        var focus = _ctx.Services.GetRequiredService<IAccessibilityFocusService>();
        var confirmationFocused = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        focus.FocusByIdAsync(Arg.Any<string>(), true).Returns(call =>
        {
            confirmationFocused.TrySetResult(call.Arg<string>());
            return Task.CompletedTask;
        });
        await BeginAsync(cut);
        await Assert.That(await confirmationFocused.Task.WaitAsync(TimeSpan.FromSeconds(5)))
            .IsEqualTo(cut.Find("[data-action='cancel']").Id);
        var restored = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        focus.FocusByIdAsync(Arg.Any<string>(), true).Returns(call =>
        {
            restored.TrySetResult(call.Arg<string>());
            return Task.CompletedTask;
        });
        await ClickAsync(cut, "cancel");

        await Assert.That(await restored.Task.WaitAsync(TimeSpan.FromSeconds(5)))
            .IsEqualTo(cut.Find("[data-action='retire']").Id);
        await Assert.That(cut.FindAll("[data-action='confirm']").Count).IsEqualTo(0);
        await Assert.That(_responses.Count).IsEqualTo(0);
    }

    private async Task<IRenderedComponent<TenantStoredFiles>> RenderAsync()
    {
        var cut = _ctx.RenderMudComponent<TenantStoredFiles>();
        var loaded = Signal(cut, () => cut.Find("section").GetAttribute("aria-busy") == "false");
        await cut.InvokeAsync(() => cut.Render(p => p.Add(x => x.TenantId, _tenantId)));
        await loaded.WaitAsync(TimeSpan.FromSeconds(5));
        return cut;
    }

    private static Task Signal<T>(IRenderedComponent<T> cut, Func<bool> ready)
        where T : Microsoft.AspNetCore.Components.IComponent
    {
        var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        cut.OnMarkupUpdated += (_, _) => { if (ready()) signal.TrySetResult(); };
        return signal.Task;
    }

    private static Task ClickAsync(IRenderedComponent<TenantStoredFiles> cut, string action) =>
        cut.Find($"[data-action='{action}']").ClickAsync(new MouseEventArgs());

    private static Task BeginAsync(IRenderedComponent<TenantStoredFiles> cut) => ClickAsync(cut, "retire");

    private void Enqueue(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        _responses.Enqueue((_, _) => Task.FromResult(Response(json, status)));

    private static HttpResponseMessage Response(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private string Page(bool delete = true, string? next = null) =>
        $$$"""
        {"pageNumber":1,"pageSize":20,"_links":{"self":{"href":"/api/storageobject?PageNumber=1&PageSize=20","method":"GET"}{{{(next is null ? "" : $",\"next\":{{\"href\":\"{next}\",\"method\":\"GET\"}}")}}}},
         "_embedded":{"items":[{"id":"{{{_fileId}}}","tenantId":"{{{_tenantId}}}","safeDisplayName":"report.pdf","size":120,
         "_links":{"self":{"href":"/api/storageobject/{{{_fileId}}}","method":"GET"},"edit":{"href":"/api/storageobject/{{{_fileId}}}","method":"PUT"}{{{(delete ? $",\"delete\":{{\"href\":\"/api/storageobject/{_fileId}\",\"method\":\"DELETE\"}}" : "")}}}}}]}}
        """;

    private string Detail(bool delete = true) =>
        $$$"""
        {"id":"{{{_fileId}}}","tenantId":"{{{_tenantId}}}","safeDisplayName":"report.pdf",
         "_links":{ {{{(delete ? $"\"delete\":{{\"href\":\"/api/storageobject/{_fileId}\",\"method\":\"DELETE\"}}" : "")}}} }}
        """;

    private sealed class Handler(Queue<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> responses)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            responses.Dequeue()(request, cancellationToken);
    }
}
