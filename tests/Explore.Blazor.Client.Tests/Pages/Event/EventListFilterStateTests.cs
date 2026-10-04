using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Models;
using Explore.Blazor.Client.Pages.Events;
using Explore.Blazor.Client.Pages.Events.Components;
using Explore.Blazor.Client.Services;
using MudBlazor;
using NSubstitute;

namespace Explore.Blazor.Client.Tests.Pages.Event;

public sealed class EventListFilterStateTests
{
    [Test]
    public async Task From_UsesSearchQueryWhenFilterBarIsUnavailable()
    {
        var actorId = Guid.NewGuid();
        var organizationId = Guid.NewGuid();
        var groupId = Guid.NewGuid();

        var state = EventListFilterState.From(
            filterBar: null,
            searchQuery: "community",
            actorId,
            organizationId,
            groupId);

        await Assert.That(state.SearchTerm).IsEqualTo("community");
        await Assert.That(state.SortBy).IsEqualTo("date");
        await Assert.That(state.SortDescending).IsTrue();
        await Assert.That(state.ActorId).IsEqualTo(actorId);
        await Assert.That(state.OrganizationId).IsEqualTo(organizationId);
        await Assert.That(state.GroupId).IsEqualTo(groupId);
        await Assert.That(state.FormatIds).IsNull();
    }

    [Test]
    public async Task From_CapturesFilterBarSelectionsAndInclusiveDateRange()
    {
        var filterBar = new EventFilterBar
        {
            SearchTerm = "lecture",
            SelectedFormatIds = new HashSet<int> { 2 },
            SelectedMadhabIds = new HashSet<int> { 3 },
            SelectedRegistrationModeIds = new HashSet<int> { 4 },
            SelectedLanguageIds = new HashSet<int> { 5 },
            SelectedEventTypeIds = new HashSet<int> { 6 },
            SelectedAudienceGenderIds = new HashSet<int> { 7 },
            SelectedAudienceAgeIds = new HashSet<int> { 8 },
            SelectedGenderModeIds = new HashSet<int> { 10 },
            SelectedReferencePrayerIds = new HashSet<int> { 11 },
            SelectedSkillLevel = SkillLevel.Intermediate,
            TechStackTag = "dotnet",
            SelectedSortBy = "title",
            SortDescending = false,
            SelectedDateRange = new DateRange(new DateTime(2026, 5, 1), new DateTime(2026, 5, 10))
        };

        var state = EventListFilterState.From(filterBar, searchQuery: "fallback", null, null, null);

        await Assert.That(state.SearchTerm).IsEqualTo("lecture");
        await Assert.That(state.FormatIds!.SequenceEqual([2])).IsTrue();
        await Assert.That(state.MadhabIds!.SequenceEqual([3])).IsTrue();
        await Assert.That(state.RegistrationModeIds!.SequenceEqual([4])).IsTrue();
        await Assert.That(state.LanguageIds!.SequenceEqual([5])).IsTrue();
        await Assert.That(state.EventTypeIds!.SequenceEqual([6])).IsTrue();
        await Assert.That(state.AudienceGenderIds!.SequenceEqual([7])).IsTrue();
        await Assert.That(state.AudienceAgeIds!.SequenceEqual([8])).IsTrue();
        await Assert.That(state.GenderModeIds!.SequenceEqual([10])).IsTrue();
        await Assert.That(state.ReferencePrayerIds!.SequenceEqual([11])).IsTrue();
        await Assert.That(state.SkillLevelId).IsEqualTo((int)SkillLevel.Intermediate);
        await Assert.That(state.TechStackTag).IsEqualTo("dotnet");
        await Assert.That(state.SortBy).IsEqualTo("title");
        await Assert.That(state.SortDescending).IsFalse();
        await Assert.That(state.DateFrom).IsEqualTo(new DateTimeOffset(new DateTime(2026, 5, 1), TimeSpan.Zero));
        await Assert.That(state.DateTo).IsEqualTo(new DateTimeOffset(new DateTime(2026, 5, 10).AddDays(1).AddTicks(-1), TimeSpan.Zero));
    }

    [Test]
    public async Task FetchTraversalAsync_SendsOriginalCriteriaThroughGeneratedHttpClient()
    {
        var actorId = Guid.NewGuid();
        using var handler = new DiscoveryHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };
        var eventService = new EventService(
            new EventClient(http),
            Substitute.For<IEventLifecycleClient>(),
            Substitute.For<IEventManagementReadClient>(),
            Substitute.For<IEventParticipationClient>(),
            Substitute.For<IEventPublicActionClient>(),
            Substitute.For<Microsoft.Extensions.Logging.ILogger<EventService>>());
        var includedCategoryId = Guid.NewGuid();
        var excludedCategoryId = Guid.NewGuid();
        var includedTagId = Guid.NewGuid();
        var excludedTagId = Guid.NewGuid();
        var state = new EventListFilterState(
            SearchTerm: "community",
            IncludedCategoryIds: [includedCategoryId],
            ExcludedCategoryIds: [excludedCategoryId],
            CategoryInclusionMode: "any",
            CategoryExclusionMode: "all",
            IncludedTagIds: [includedTagId],
            ExcludedTagIds: [excludedTagId],
            TagInclusionMode: "all",
            TagExclusionMode: "any",
            FormatIds: null,
            MadhabIds: null,
            RegistrationModeIds: null,
            LanguageIds: null,
            DateFrom: null,
            DateTo: null,
            SortBy: "date",
            SortDescending: true,
            EventTypeIds: null,
            AudienceGenderIds: null,
            AudienceAgeIds: null,
            GenderModeIds: null,
            ReferencePrayerIds: null,
            SkillLevelId: null,
            TechStackTag: "dotnet",
            ActorId: actorId,
            OrganizationId: null,
            GroupId: null);

        var result = await state.FetchTraversalAsync(eventService, "opaque+cursor", 25, CancellationToken.None);
        var query = System.Web.HttpUtility.ParseQueryString(handler.RequestUri!.Query);
        await Assert.That(query["cursor"]).IsEqualTo("opaque+cursor");
        await Assert.That(query["pageSize"]).IsEqualTo("25");
        await Assert.That(query["pageNumber"]).IsNull();
        await Assert.That(query["searchTerm"]).IsEqualTo("community");
        await Assert.That(query["actorId"]).IsEqualTo(actorId.ToString());
        await Assert.That(query["includedCategoryIds"]).IsEqualTo(includedCategoryId.ToString());
        await Assert.That(query["excludedCategoryIds"]).IsEqualTo(excludedCategoryId.ToString());
        await Assert.That(query["categoryInclusionMode"]).IsEqualTo("any");
        await Assert.That(query["categoryExclusionMode"]).IsEqualTo("all");
        await Assert.That(query["includedTagIds"]).IsEqualTo(includedTagId.ToString());
        await Assert.That(query["excludedTagIds"]).IsEqualTo(excludedTagId.ToString());
        await Assert.That(query["inclusionMode"]).IsEqualTo("all");
        await Assert.That(query["exclusionMode"]).IsEqualTo("any");
        await Assert.That(query["techStackTag"]).IsEqualTo("dotnet");
        await Assert.That(query["sortBy"]).IsEqualTo("date");
        await Assert.That(query["sortDescending"]).IsEqualTo("true");
        await Assert.That(result.SnapshotCount).IsEqualTo(43);
        await Assert.That(result.Truncated).IsTrue();
    }

    private sealed class DiscoveryHandler : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    {"snapshotCount":43,"truncated":true,"expiresAt":"2026-10-03T12:15:00Z","hasMore":false,"_links":{},"_embedded":{"items":[]}}
                    """, System.Text.Encoding.UTF8, "application/hal+json")
            });
        }
    }
}
