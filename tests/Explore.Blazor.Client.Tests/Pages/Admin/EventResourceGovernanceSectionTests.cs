using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services;
using Explore.Blazor.Client.Pages.Admin.Instance.Components;
using MudBlazor;

namespace Explore.Blazor.Client.Tests.Pages.Admin;

public sealed class EventResourceGovernanceSectionTests : IDisposable
{
    private readonly BlazorTestContext _ctx = new();
    private readonly IEventResourceGovernanceService _service = Substitute.For<IEventResourceGovernanceService>();

    public EventResourceGovernanceSectionTests() => _ctx.Services.AddSingleton(_service);
    public void Dispose() => _ctx.Dispose();

    [Test]
    public async Task NoEditLink_DisablesEveryWriteEvenWhenSettingCanEdit()
    {
        _service.GetAsync(true, Arg.Any<CancellationToken>()).Returns(Settings(edit: false));
        var cut = _ctx.RenderMudComponent<EventResourceGovernanceSection>(parameters => parameters.Add(p => p.InstanceScope, true));
        cut.WaitForState(() => cut.Markup.Contains("Maximum upload", StringComparison.Ordinal));
        await Assert.That(cut.Find("[data-resource-save]").HasAttribute("disabled")).IsTrue();
        await Assert.That(cut.FindAll("[data-resource-field]:not([disabled])").Count).IsEqualTo(0);
        await _service.DidNotReceive().UpdateAsync(Arg.Any<bool>(), Arg.Any<UpdateSettingBatchDto>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TenantCannotExposeOptInOrWidenValues()
    {
        _service.GetAsync(false, Arg.Any<CancellationToken>()).Returns(Settings(edit: true));
        var cut = _ctx.RenderMudComponent<EventResourceGovernanceSection>();
        cut.WaitForState(() => cut.Markup.Contains("Maximum upload", StringComparison.Ordinal));
        await Assert.That(cut.Markup).DoesNotContain("allow_unscanned_documents");
        await Assert.That(cut.FindAll("[data-resource-field='event_resources.allow_unscanned_documents']").Count).IsEqualTo(0);
        var input = cut.Find("[data-resource-field='event_resources.max_upload_bytes']");
        await Assert.That(input.GetAttribute("max")).IsEqualTo("100");
        await input.ChangeAsync("101");
        await cut.Find("[data-resource-save]").ClickAsync();
        await _service.DidNotReceive().UpdateAsync(Arg.Any<bool>(), Arg.Any<UpdateSettingBatchDto>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TenantArrayChoicesAreOnlyEffectiveOptions()
    {
        var settings = Settings(edit: true);
        settings.Settings.Add(new EffectiveSettingDto
        {
            Key = "event_resources.enabled_delivery_types", Value = "[\"StoredFile\"]", CanEdit = true
        });
        _service.GetAsync(false, Arg.Any<CancellationToken>()).Returns(settings);
        var cut = _ctx.RenderMudComponent<EventResourceGovernanceSection>();
        cut.WaitForState(() => cut.Markup.Contains("StoredFile", StringComparison.Ordinal));
        await Assert.That(cut.Markup).DoesNotContain("ExternalLink");
        await cut.Find("[data-resource-field='event_resources.enabled_delivery_types']").ChangeAsync(false);
        await cut.Find("[data-resource-save]").ClickAsync();
        await _service.Received(1).UpdateAsync(false, Arg.Is<UpdateSettingBatchDto>(batch =>
            batch.Values["event_resources.enabled_delivery_types"] == "[]"), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task InstanceOnlyOptInIsExplicit()
    {
        _service.GetAsync(true, Arg.Any<CancellationToken>()).Returns(Settings(edit: true));
        _service.UpdateAsync(true, Arg.Any<UpdateSettingBatchDto>(), Arg.Any<CancellationToken>())
            .Returns(new BatchUpdateResponseDto { Success = true });
        var cut = _ctx.RenderMudComponent<EventResourceGovernanceSection>(parameters => parameters.Add(p => p.InstanceScope, true));
        cut.WaitForState(() => cut.Markup.Contains("Maximum upload", StringComparison.Ordinal));
        await Assert.That(cut.Markup).Contains("without a malware scan");
        await cut.Find("[data-resource-field='event_resources.allow_unscanned_documents']").ChangeAsync(true);
        await cut.Find("[data-resource-save]").ClickAsync();
        await _service.Received(1).UpdateAsync(true, Arg.Is<UpdateSettingBatchDto>(batch =>
            batch.Values["event_resources.allow_unscanned_documents"] == "true"), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task LockedSettingIsReadOnly()
    {
        _service.GetAsync(false, Arg.Any<CancellationToken>()).Returns(Settings(edit: true, locked: true));
        var cut = _ctx.RenderMudComponent<EventResourceGovernanceSection>();
        cut.WaitForState(() => cut.Markup.Contains("Maximum upload", StringComparison.Ordinal));
        await Assert.That(cut.FindAll("[data-resource-field]:not([disabled])").Count).IsEqualTo(0);
        await Assert.That(cut.Markup).Contains("Instance lock");
    }

    [Test]
    public async Task InstanceAdministratorCanEditOwnLockedSettingWhenServerAllowsIt()
    {
        _service.GetAsync(true, Arg.Any<CancellationToken>()).Returns(Settings(edit: true, locked: true, canEdit: true));
        var cut = _ctx.RenderMudComponent<EventResourceGovernanceSection>(parameters => parameters.Add(p => p.InstanceScope, true));
        cut.WaitForState(() => cut.Markup.Contains("Maximum upload", StringComparison.Ordinal));
        await Assert.That(cut.Find("[data-resource-field='event_resources.max_upload_bytes']").HasAttribute("disabled")).IsFalse();
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task SaveReloadsAuthoritativeSettings(bool instance)
    {
        var reads = 0;
        _service.GetAsync(instance, Arg.Any<CancellationToken>()).Returns(_ => { reads++; return Settings(edit: true); });
        _service.UpdateAsync(instance, Arg.Any<UpdateSettingBatchDto>(), Arg.Any<CancellationToken>())
            .Returns(new BatchUpdateResponseDto { Success = true });
        var cut = _ctx.RenderMudComponent<EventResourceGovernanceSection>(parameters => parameters.Add(p => p.InstanceScope, instance));
        cut.WaitForState(() => cut.Markup.Contains("Maximum upload", StringComparison.Ordinal));
        await cut.Find("[data-resource-field='event_resources.max_upload_bytes']").ChangeAsync("50");
        await cut.Find("[data-resource-save]").ClickAsync();
        await Assert.That(reads).IsEqualTo(2);
        await _service.Received(1).UpdateAsync(instance, Arg.Is<UpdateSettingBatchDto>(batch =>
            batch.Mode == BatchUpdateMode.Strict && batch.Values["event_resources.max_upload_bytes"] == "50"), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task StaleDenialReloadsAndRemovesWriteAuthority()
    {
        var reads = 0;
        _service.GetAsync(false, Arg.Any<CancellationToken>()).Returns(_ => Settings(edit: ++reads == 1));
        _service.UpdateAsync(false, Arg.Any<UpdateSettingBatchDto>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<BatchUpdateResponseDto>(new ApiException("Denied", 403, "", new Dictionary<string, IEnumerable<string>>(), null)));
        var cut = _ctx.RenderMudComponent<EventResourceGovernanceSection>();
        cut.WaitForState(() => cut.Markup.Contains("Maximum upload", StringComparison.Ordinal));
        await cut.Find("[data-resource-field='event_resources.max_upload_bytes']").ChangeAsync("50");
        await cut.Find("[data-resource-save]").ClickAsync();
        await Assert.That(reads).IsEqualTo(2);
        await Assert.That(cut.Find("[data-resource-save]").HasAttribute("disabled")).IsTrue();
    }

    private static HalResourceOfSettingGroupResponseDto Settings(bool edit, bool locked = false, bool canEdit = false) => new()
    {
        Category = "EventResources",
        _links = edit ? new Dictionary<string, HalLink> { ["edit"] = new() { Href = "/api/settings/tenant/EventResources" } } : null,
        Settings = new List<EffectiveSettingDto>
        {
            new() { Key = "event_resources.max_upload_bytes", Value = "100", CanEdit = !locked || canEdit, IsLocked = locked, Source = SettingSource.SystemDefault, Reason = locked ? "Instance lock" : null },
            new() { Key = "event_resources.allow_unscanned_documents", Value = "false", CanEdit = true }
        }
    };
}
