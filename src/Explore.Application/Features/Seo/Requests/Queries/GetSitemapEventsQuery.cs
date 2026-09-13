using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Seo;

namespace Explore.Application.Features.Seo.Requests.Queries;

public sealed record GetSitemapEventsQuery(int MaxCount = 50_000) : IQuery<IReadOnlyList<SitemapEventEntryDto>>;
