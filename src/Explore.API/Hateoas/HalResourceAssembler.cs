using Explore.Application.Contracts.Hateoas;

namespace Explore.API.Hateoas;

/// <summary>
/// Most DTO families assemble the same way: run the detail policy over one resource, the collection policy
/// over a page, and let <see cref="ResourceAssemblerBase{TDto,TListDto}"/> batch and de-duplicate the
/// authorization calls. Those families used to each declare an empty subclass that added nothing but a
/// constructor signature — a type per family whose only job was to be a distinct type.
/// <para>
/// This generic is that default made explicit. A family that genuinely needs custom assembly still declares
/// its own subclass and registers it, so the file list now distinguishes families with real assembly behavior
/// from families that simply have HAL links.
/// </para>
/// </summary>
public sealed class HalResourceAssembler<TDto, TListDto>(
    IHateoasLinkGenerator linkGenerator,
    ILinkPolicy<TDto> detailLinkPolicy,
    ICollectionLinkPolicy<TListDto> collectionLinkPolicy)
    : ResourceAssemblerBase<TDto, TListDto>(linkGenerator, detailLinkPolicy, collectionLinkPolicy)
    where TDto : class
    where TListDto : class;
