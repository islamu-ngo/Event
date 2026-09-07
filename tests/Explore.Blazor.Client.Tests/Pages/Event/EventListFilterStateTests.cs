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
    public async Task FetchPageAsync_ForwardsSnapshotToEventService()
    {
        var actorId = Guid.NewGuid();
        var eventService = Substitute.For<IEventService>();
        eventService.GetEventsPagedAsync(
                Arg.Any<int>(),
                Arg.Any<int>(),
                searchTerm: Arg.Any<string?>(),
                categoryId: Arg.Any<Guid?>(),
                includedCategoryIds: Arg.Any<List<Guid>?>(),
                excludedCategoryIds: Arg.Any<List<Guid>?>(),
                categoryInclusionMode: Arg.Any<string?>(),
                categoryExclusionMode: Arg.Any<string?>(),
                includedTagIds: Arg.Any<List<Guid>?>(),
                excludedTagIds: Arg.Any<List<Guid>?>(),
                inclusionMode: Arg.Any<string?>(),
                exclusionMode: Arg.Any<string?>(),
                formatIds: Arg.Any<List<int>?>(),
                madhabIds: Arg.Any<List<int>?>(),
                registrationModeIds: Arg.Any<List<int>?>(),
                languageIds: Arg.Any<List<int>?>(),
                dateFrom: Arg.Any<DateTimeOffset?>(),
                dateTo: Arg.Any<DateTimeOffset?>(),
                sortBy: Arg.Any<string?>(),
                sortDescending: Arg.Any<bool?>(),
                eventTypeIds: Arg.Any<List<int>?>(),
                audienceGenderIds: Arg.Any<List<int>?>(),
                audienceAgeIds: Arg.Any<List<int>?>(),
                eventStatusIds: Arg.Any<List<int>?>(),
                genderModeIds: Arg.Any<List<int>?>(),
                includesQuranRecitation: Arg.Any<bool?>(),
                referencePrayerIds: Arg.Any<List<int>?>(),
                islamicPrimaryLanguageIds: Arg.Any<List<int>?>(),
                hasIslamicAspect: Arg.Any<bool?>(),
                skillLevelId: Arg.Any<int?>(),
                isCodingCompetition: Arg.Any<bool?>(),
                isHackathon: Arg.Any<bool?>(),
                requiresLaptop: Arg.Any<bool?>(),
                techStackTag: Arg.Any<string?>(),
                hasTechAspect: Arg.Any<bool?>(),
                actorId: Arg.Any<Guid?>(),
                organizationId: Arg.Any<Guid?>(),
                groupId: Arg.Any<Guid?>(),
                cancellationToken: Arg.Any<CancellationToken>())
            .Returns(new PaginatedResult<EventListDto>
            {
                Items = [],
                PageNumber = 3,
                PageSize = 25,
                TotalCount = 0
            });
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

        await state.FetchPageAsync(eventService, 3, 25, CancellationToken.None);

        await eventService.Received(1).GetEventsPagedAsync(
            pageNumber: 3,
            pageSize: 25,
            searchTerm: "community",
            categoryId: null,
            includedCategoryIds: Arg.Is<List<Guid>?>(ids => ids != null && ids.SequenceEqual(new[] { includedCategoryId })),
            excludedCategoryIds: Arg.Is<List<Guid>?>(ids => ids != null && ids.SequenceEqual(new[] { excludedCategoryId })),
            categoryInclusionMode: "any",
            categoryExclusionMode: "all",
            includedTagIds: Arg.Is<List<Guid>?>(ids => ids != null && ids.SequenceEqual(new[] { includedTagId })),
            excludedTagIds: Arg.Is<List<Guid>?>(ids => ids != null && ids.SequenceEqual(new[] { excludedTagId })),
            inclusionMode: "all",
            exclusionMode: "any",
            includesQuranRecitation: null,
            eventStatusIds: null,
            islamicPrimaryLanguageIds: null,
            hasIslamicAspect: null,
            isCodingCompetition: null,
            isHackathon: null,
            requiresLaptop: null,
            techStackTag: "dotnet",
            hasTechAspect: null,
            actorId: actorId,
            sortBy: "date",
            sortDescending: true,
            cancellationToken: CancellationToken.None);
    }
}
