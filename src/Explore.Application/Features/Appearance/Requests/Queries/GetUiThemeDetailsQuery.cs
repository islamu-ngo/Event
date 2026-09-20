namespace Explore.Application.Features.Appearance.Requests.Queries;

using Explore.Application.DTOs.Appearance;
using Explore.Application.Contracts.Operations;

public sealed record GetUiThemeDetailsQuery(Guid Id = default) : IQuery<UiThemeDetailsDto?>;
