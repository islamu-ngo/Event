using Explore.Application.DTOs.Event;
using Explore.Application.DTOs.EventSeries;
using Explore.Domain;
using Riok.Mapperly.Abstractions;

namespace Explore.Application.Mappings;

public static partial class EventMapper
{
    // Series children are bounded event list snapshots; list projections never point back to a series.
    [MapperIgnoreSource(nameof(EventSeries.TotalViews))]
    [MapperIgnoreSource(nameof(EventSeries.VisibilityTypeId))]
    [MapperIgnoreSource(nameof(EventSeries.VisibilityType))]
    [MapperIgnoreSource(nameof(EventSeries.Tenant))]
    [MapperIgnoreSource(nameof(EventSeries.CreatedAt))]
    [MapperIgnoreSource(nameof(EventSeries.CreatedBy))]
    [MapperIgnoreSource(nameof(EventSeries.UpdatedAt))]
    [MapperIgnoreSource(nameof(EventSeries.UpdatedBy))]
    [MapperIgnoreSource(nameof(EventSeries.IsDeleted))]
    [MapperIgnoreSource(nameof(EventSeries.DeletedAt))]
    [MapperIgnoreSource(nameof(EventSeries.DeletedBy))]
    [MapProperty(nameof(EventSeries.Actor), nameof(EventSeriesDto.ActorDisplayName), Use = nameof(EventActorName))]
    [MapProperty(nameof(EventSeries.FeaturedImage), nameof(EventSeriesDto.FeaturedImageUri), Use = nameof(EventImageUri))]
    [MapProperty(nameof(EventSeries.Events), nameof(EventSeriesDto.Events), Use = nameof(SeriesEvents))]
    public static partial EventSeriesDto ToDetail(EventSeries source);

    // Public series listing omits publication state and image key as before; repository owns filtering.
    [MapperIgnoreSource(nameof(EventSeries.FeaturedImageId))]
    [MapperIgnoreSource(nameof(EventSeries.IsPublished))]
    [MapperIgnoreSource(nameof(EventSeries.TotalViews))]
    [MapperIgnoreSource(nameof(EventSeries.VisibilityTypeId))]
    [MapperIgnoreSource(nameof(EventSeries.VisibilityType))]
    [MapperIgnoreSource(nameof(EventSeries.Tenant))]
    [MapperIgnoreSource(nameof(EventSeries.CreatedAt))]
    [MapperIgnoreSource(nameof(EventSeries.CreatedBy))]
    [MapperIgnoreSource(nameof(EventSeries.UpdatedAt))]
    [MapperIgnoreSource(nameof(EventSeries.UpdatedBy))]
    [MapperIgnoreSource(nameof(EventSeries.IsDeleted))]
    [MapperIgnoreSource(nameof(EventSeries.DeletedAt))]
    [MapperIgnoreSource(nameof(EventSeries.DeletedBy))]
    [MapProperty(nameof(EventSeries.Actor), nameof(EventSeriesListDto.ActorDisplayName), Use = nameof(EventActorName))]
    [MapProperty(nameof(EventSeries.FeaturedImage), nameof(EventSeriesListDto.FeaturedImageUri), Use = nameof(EventImageUri))]
    [MapProperty(nameof(EventSeries.Events), nameof(EventSeriesListDto.EventCount), Use = nameof(SeriesEventCount))]
    public static partial EventSeriesListDto ToListItem(EventSeries source);

    private static IReadOnlyList<EventListDto> SeriesEvents(IReadOnlyList<Event> source) => source.Select(ToListItem).ToList();
    private static int SeriesEventCount(IReadOnlyList<Event> source) => source.Count;
}
