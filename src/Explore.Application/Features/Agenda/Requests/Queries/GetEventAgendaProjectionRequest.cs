using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Agenda;

namespace Explore.Application.Features.Agenda.Requests.Queries;

public sealed record GetEventAgendaProjectionRequest(Guid EventId = default) : IQuery<EventAgendaProjectionDto?>;
