using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Models;
using Explore.Blazor.Client.Pages.Events.Components;
using MudBlazor;
using TriStateTagFilterDropdownComponent = Explore.Blazor.Client.Pages.Events.Components.TriStateTagFilterDropdown;

namespace Explore.Blazor.Client.Tests.Components.Event;

public class TriStateTagFilterDropdownTests : IDisposable
{
    private readonly BlazorTestContext _ctx;

    public TriStateTagFilterDropdownTests()
    {
        _ctx = new BlazorTestContext();
    }

    public void Dispose()
    {
        _ctx.Dispose();
    }

    private static List<TagTypeWithTagsDto> GetMockTagGroups()
    {
        return new List<TagTypeWithTagsDto>
        {
            new TagTypeWithTagsDto
            {
                Id = 1,
                FullName = "Genre",
                Tags = new List<TagListDto>
                {
                    new TagListDto { Id = Guid.NewGuid(), FullName = "Fiction" },
                    new TagListDto { Id = Guid.NewGuid(), FullName = "Non-Fiction" }
                }
            },
            new TagTypeWithTagsDto
            {
                Id = 2,
                FullName = "Format",
                Tags = new List<TagListDto>
                {
                    new TagListDto { Id = Guid.NewGuid(), FullName = "Workshop" }
                }
            }
        };
    }

    private async Task ToggleRenderedTagAsync(
        IRenderedComponent<TriStateTagFilterDropdownComponent> cut,
        Guid tagId)
    {
        string selector = $"[data-tag-id='{tagId}']";
        if (cut.FindAll(selector).Count == 0)
        {
            await cut.Find("[role='button']").ClickAsync(new MouseEventArgs());
            cut.WaitForElement(selector);
        }

        await cut.Find(selector).ClickAsync(new MouseEventArgs());
    }

    private IRenderedComponent<TriStateTagFilterDropdownComponent> RenderDropdown(List<TagTypeWithTagsDto>? groups = null)
    {
        return _ctx.RenderMudComponent<TriStateTagFilterDropdownComponent>(parameters => parameters
            .Add(component => component.TagGroups, groups ?? GetMockTagGroups()));
    }

    [Test]
    public async Task RendersTriggerButton_WithDefaultBadgeText()
    {
        var cut = RenderDropdown();

        await Assert.That(cut.Markup).Contains("Filter Tags");
    }

    [Test]
    public async Task ClickTriggerButton_TogglesPopoverVisibility()
    {
        var cut = RenderDropdown();
        var markupBefore = cut.Markup;

        // Click trigger to open — should change EndIcon and overlay visibility
        var triggerButton = cut.Find("[role='button']");
        await cut.InvokeAsync(() => triggerButton.Click());

        var markupAfterOpen = cut.Markup;
        await Assert.That(markupAfterOpen).IsNotEqualTo(markupBefore);

        // Click trigger to close — markup should revert from the open state
        triggerButton = cut.Find("[role='button']");
        await cut.InvokeAsync(() => triggerButton.Click());

        var markupAfterClose = cut.Markup;
        await Assert.That(markupAfterClose).IsNotEqualTo(markupAfterOpen);
    }

    [Test]
    public async Task TagCycling_CyclesThroughNeutralIncludeExclude()
    {
        var groups = GetMockTagGroups();
        var tagId = groups[0].Tags!.First().Id!.Value;
        var cut = RenderDropdown(groups);

        // Cycle 1: Neutral → Include
        await ToggleRenderedTagAsync(cut, tagId);
        var filter = cut.Instance.GetCurrentFilter();
        await Assert.That(filter.IncludedTagIds).Contains(tagId);
        await Assert.That(filter.ExcludedTagIds).DoesNotContain(tagId);

        // Cycle 2: Include → Exclude
        await ToggleRenderedTagAsync(cut, tagId);
        filter = cut.Instance.GetCurrentFilter();
        await Assert.That(filter.IncludedTagIds).DoesNotContain(tagId);
        await Assert.That(filter.ExcludedTagIds).Contains(tagId);

        // Cycle 3: Exclude → Neutral
        await ToggleRenderedTagAsync(cut, tagId);
        filter = cut.Instance.GetCurrentFilter();
        await Assert.That(filter.IncludedTagIds).DoesNotContain(tagId);
        await Assert.That(filter.ExcludedTagIds).DoesNotContain(tagId);
    }

    [Test]
    public async Task ResetAll_ClearsAllTagStates()
    {
        var groups = GetMockTagGroups();
        var tagId = groups[0].Tags!.First().Id!.Value;
        var cut = RenderDropdown(groups);

        // Setup: include a tag
        await ToggleRenderedTagAsync(cut, tagId);
        await Assert.That(cut.Instance.GetCurrentFilter().IncludedTagIds).Contains(tagId);

        // Act: reset via public API
        await cut.InvokeAsync(() => cut.Instance.ResetAll());

        // Assert: filter state is empty
        var filter = cut.Instance.GetCurrentFilter();
        await Assert.That(filter.IncludedTagIds).IsEmpty();
        await Assert.That(filter.ExcludedTagIds).IsEmpty();
    }

    [Test]
    public async Task GetCurrentFilter_ReturnsIncludedExcludedAndDefaultModes()
    {
        var groups = GetMockTagGroups();
        var tagId1 = groups[0].Tags!.ElementAt(0).Id!.Value;
        var tagId2 = groups[0].Tags!.ElementAt(1).Id!.Value;
        var cut = RenderDropdown(groups);

        // tagId1 → Include (1 toggle)
        await ToggleRenderedTagAsync(cut, tagId1);
        // tagId2 → Exclude (2 toggles: Neutral→Include→Exclude)
        await ToggleRenderedTagAsync(cut, tagId2);
        await ToggleRenderedTagAsync(cut, tagId2);

        var filter = cut.Instance.GetCurrentFilter();

        await Assert.That(filter.IncludedTagIds).Contains(tagId1);
        await Assert.That(filter.ExcludedTagIds).Contains(tagId2);
        await Assert.That(filter.InclusionMode).IsEqualTo("and");
        await Assert.That(filter.ExclusionMode).IsEqualTo("or");
    }
}
