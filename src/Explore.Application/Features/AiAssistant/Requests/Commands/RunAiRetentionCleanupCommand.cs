using Explore.Application.Contracts.Operations;
using Explore.Application.Models;

namespace Explore.Application.Features.AiAssistant.Requests.Commands;

public sealed record RunAiRetentionCleanupCommand : ICommand<AiRetentionCleanupResult>
{
    public bool DryRun { get; init; }
    public DateTime? UtcNow { get; init; }
}
