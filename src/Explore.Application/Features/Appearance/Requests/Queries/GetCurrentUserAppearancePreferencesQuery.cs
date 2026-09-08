namespace Explore.Application.Features.Appearance.Requests.Queries;

using Explore.Application.DTOs.Appearance;
using MediatR;

public sealed record GetCurrentUserAppearancePreferencesQuery : IRequest<UserAppearancePreferencesDto>
{
}
