namespace Explore.Application.Features.Appearance.Requests.Queries;

using Explore.Application.DTOs.Appearance;
using MediatR;

public sealed record GetUiThemeCatalogQuery(
    bool IsPlatformCatalog = default,
    bool ActiveOnly = default) : IRequest<IReadOnlyList<UiThemeListItemDto>>;
