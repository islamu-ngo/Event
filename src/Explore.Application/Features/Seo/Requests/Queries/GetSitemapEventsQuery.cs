using Explore.Application.DTOs.Seo;
using MediatR;

namespace Explore.Application.Features.Seo.Requests.Queries;

public sealed record GetSitemapEventsQuery(int MaxCount = 50_000) : IRequest<IReadOnlyList<SitemapEventEntryDto>>;
