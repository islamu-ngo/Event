using MediatR;

namespace Explore.Application.Features.Localization.Requests.Queries;

public sealed record GetLocalizationTmsApiKeyConfiguredQuery : IRequest<bool>
{
}
