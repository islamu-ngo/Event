using MediatR;

namespace Explore.Application.Features.Localization.Requests.Queries;

public sealed record GetAvailableLanguagesQuery : IRequest<List<string>>
{
}
