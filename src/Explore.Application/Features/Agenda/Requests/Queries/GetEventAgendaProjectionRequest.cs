using Explore.Application.DTOs.Agenda;
using MediatR;

namespace Explore.Application.Features.Agenda.Requests.Queries;

public sealed record GetEventAgendaProjectionRequest(Guid EventId = default) : IRequest<EventAgendaProjectionDto?>;
