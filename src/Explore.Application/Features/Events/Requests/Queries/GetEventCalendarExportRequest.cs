using Explore.Application.Contracts.Operations;
using Explore.Application.Features.Events.Handlers.Queries;

namespace Explore.Application.Features.Events.Requests.Queries;

public sealed record GetEventCalendarExportRequest(Guid EventId) : IQuery<PublicEventCalendarProjection?>;
