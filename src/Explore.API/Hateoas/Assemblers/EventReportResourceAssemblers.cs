namespace Explore.API.Hateoas.Assemblers;

using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.EventReporting;

public sealed class EventReportOptionsResourceAssembler
    : ResourceAssemblerBase<EventReportOptionsDto, EventReportOptionsDto>
{
    public EventReportOptionsResourceAssembler(
        IHateoasLinkGenerator linkGenerator,
        ILinkPolicy<EventReportOptionsDto> detailLinkPolicy,
        ICollectionLinkPolicy<EventReportOptionsDto> collectionLinkPolicy)
        : base(linkGenerator, detailLinkPolicy, collectionLinkPolicy)
    {
    }
}

public sealed class MyEventReportResourceAssembler
    : ResourceAssemblerBase<MyEventReportDto, MyEventReportDto>
{
    public MyEventReportResourceAssembler(
        IHateoasLinkGenerator linkGenerator,
        ILinkPolicy<MyEventReportDto> detailLinkPolicy,
        ICollectionLinkPolicy<MyEventReportDto> collectionLinkPolicy)
        : base(linkGenerator, detailLinkPolicy, collectionLinkPolicy)
    {
    }
}
