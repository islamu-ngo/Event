using Explore.Application.Contracts.Operations;
using Explore.Application.Settings;

namespace Explore.Application.Features.EventResourceProviderActivation.Requests.Queries;

public sealed record GetEventResourceProviderBindingsQuery : IQuery<EventResourceProviderBindingDocument>;
