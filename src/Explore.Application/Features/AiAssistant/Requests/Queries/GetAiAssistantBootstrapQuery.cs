using Explore.Application.DTOs.Ai;
using MediatR;

namespace Explore.Application.Features.AiAssistant.Requests.Queries;

public sealed record GetAiAssistantBootstrapQuery : IRequest<AiAssistantBootstrapDto>
{
}
