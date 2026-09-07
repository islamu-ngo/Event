using Explore.Application.DTOs.EventFormat;
using MediatR;

namespace Explore.Application.Features.EventFormats.Requests.Queries;

public sealed record GetEventFormatDetailsRequest(int Id) : IRequest<EventFormatDto>;
