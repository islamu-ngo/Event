namespace Explore.Application.Features.Appearance.Requests.Queries;

using Explore.Application.DTOs.Appearance;
using Explore.Application.Contracts.Operations;

public sealed record GetUiThemeCatalogQuery(
    bool IsPlatformCatalog = default,
    bool ActiveOnly = default) : IQuery<IReadOnlyList<UiThemeListItemDto>>;
