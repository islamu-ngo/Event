using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.SupportAccess;

namespace Explore.API.Hateoas.Assemblers;

public sealed class SupportAccessSessionResourceAssembler : ResourceAssemblerBase<SupportAccessSessionDto, SupportAccessSessionDto>
{
    public SupportAccessSessionResourceAssembler(
        IHateoasLinkGenerator linkGenerator,
        ILinkPolicy<SupportAccessSessionDto> detailLinkPolicy,
        ICollectionLinkPolicy<SupportAccessSessionDto> collectionLinkPolicy)
        : base(linkGenerator, detailLinkPolicy, collectionLinkPolicy)
    {
    }
}

public sealed class SupportAccessAuditEventResourceAssembler : ResourceAssemblerBase<SupportAccessAuditEventDto, SupportAccessAuditEventDto>
{
    public SupportAccessAuditEventResourceAssembler(
        IHateoasLinkGenerator linkGenerator,
        ILinkPolicy<SupportAccessAuditEventDto> detailLinkPolicy,
        ICollectionLinkPolicy<SupportAccessAuditEventDto> collectionLinkPolicy)
        : base(linkGenerator, detailLinkPolicy, collectionLinkPolicy)
    {
    }
}
