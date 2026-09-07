using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.RegistrationOrders;

namespace Explore.API.Hateoas.Policies;

public sealed class RegistrationOrderCollectionLinkPolicy :
    ICollectionLinkPolicy<RegistrationOrderDto>;
