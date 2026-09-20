namespace Explore.Application.Features.Appearance.Requests.Queries;

using Explore.Application.DTOs.Appearance;
using Explore.Application.Contracts.Operations;

public sealed record GetAvailableThemesQuery : IQuery<IReadOnlyList<AvailableThemeDto>>
{
}
