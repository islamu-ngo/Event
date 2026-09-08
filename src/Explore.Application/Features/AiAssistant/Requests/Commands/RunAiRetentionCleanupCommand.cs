using Explore.Application.Models;
using MediatR;

namespace Explore.Application.Features.AiAssistant.Requests.Commands;

public sealed record RunAiRetentionCleanupCommand : IRequest<AiRetentionCleanupResult>
{
    public bool DryRun { get; init; }
    public DateTime? UtcNow { get; init; }
}
