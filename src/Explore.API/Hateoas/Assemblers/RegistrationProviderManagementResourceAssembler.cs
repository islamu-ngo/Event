using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.RegistrationProviders;

namespace Explore.API.Hateoas.Assemblers;

public sealed class RegistrationProviderHealthResourceAssembler(
    IHateoasLinkGenerator linkGenerator,
    ILinkPolicy<RegistrationProviderBindingHealthDto> detailLinkPolicy,
    ICollectionLinkPolicy<RegistrationProviderBindingHealthDto> collectionLinkPolicy)
    : ResourceAssemblerBase<RegistrationProviderBindingHealthDto, RegistrationProviderBindingHealthDto>(linkGenerator, detailLinkPolicy, collectionLinkPolicy);

public sealed class RegistrationProviderQueueResourceAssembler(
    IHateoasLinkGenerator linkGenerator,
    ILinkPolicy<RegistrationProviderParkedQueueItemDto> detailLinkPolicy,
    ICollectionLinkPolicy<RegistrationProviderParkedQueueItemDto> collectionLinkPolicy)
    : ResourceAssemblerBase<RegistrationProviderParkedQueueItemDto, RegistrationProviderParkedQueueItemDto>(linkGenerator, detailLinkPolicy, collectionLinkPolicy);

public sealed class RegistrationProviderConnectionResourceAssembler(
    IHateoasLinkGenerator linkGenerator,
    ILinkPolicy<RegistrationProviderConnectionDto> detailLinkPolicy,
    ICollectionLinkPolicy<RegistrationProviderConnectionDto> collectionLinkPolicy)
    : ResourceAssemblerBase<RegistrationProviderConnectionDto, RegistrationProviderConnectionDto>(linkGenerator, detailLinkPolicy, collectionLinkPolicy);

public sealed class RegistrationProviderBindingResourceAssembler(
    IHateoasLinkGenerator linkGenerator,
    ILinkPolicy<RegistrationProviderBindingDto> detailLinkPolicy,
    ICollectionLinkPolicy<RegistrationProviderBindingDto> collectionLinkPolicy)
    : ResourceAssemblerBase<RegistrationProviderBindingDto, RegistrationProviderBindingDto>(linkGenerator, detailLinkPolicy, collectionLinkPolicy);

public sealed class RegistrationChannelResourceAssembler(
    IHateoasLinkGenerator linkGenerator,
    ILinkPolicy<RegistrationChannelDto> detailLinkPolicy,
    ICollectionLinkPolicy<RegistrationChannelDto> collectionLinkPolicy)
    : ResourceAssemblerBase<RegistrationChannelDto, RegistrationChannelDto>(linkGenerator, detailLinkPolicy, collectionLinkPolicy);

public sealed class RegistrationProviderLaunchDescriptorResourceAssembler(
    IHateoasLinkGenerator linkGenerator,
    ILinkPolicy<RegistrationProviderLaunchDescriptorDto> detailLinkPolicy,
    ICollectionLinkPolicy<RegistrationProviderLaunchDescriptorDto> collectionLinkPolicy)
    : ResourceAssemblerBase<RegistrationProviderLaunchDescriptorDto, RegistrationProviderLaunchDescriptorDto>(linkGenerator, detailLinkPolicy, collectionLinkPolicy);
