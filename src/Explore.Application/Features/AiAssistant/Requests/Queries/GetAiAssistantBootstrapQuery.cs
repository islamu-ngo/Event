using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Ai;

namespace Explore.Application.Features.AiAssistant.Requests.Queries;

public sealed record GetAiAssistantBootstrapQuery : IQuery<AiAssistantBootstrapDto>
{
}
