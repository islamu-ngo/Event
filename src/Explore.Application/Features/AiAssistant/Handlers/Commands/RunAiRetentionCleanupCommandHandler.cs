using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.AiAssistant.Requests.Commands;
using Explore.Application.Models;
using Explore.Application.Settings;
using Explore.Application.Settings.Groups;
using MediatR;

namespace Explore.Application.Features.AiAssistant.Handlers.Commands;

public sealed class RunAiRetentionCleanupCommandHandler(
    IAiConversationRepository conversationRepository,
    IHierarchicalSettingsResolver settingsResolver,
    ITenantContext tenantContext)
    : IRequestHandler<RunAiRetentionCleanupCommand, AiRetentionCleanupResult>
{
    public async Task<AiRetentionCleanupResult> Handle(
        RunAiRetentionCleanupCommand request,
        CancellationToken cancellationToken)
    {
        var settings = await settingsResolver.ResolveGroupAsync<AiAssistantSettingGroup>(
            new SettingContext(TenantId: tenantContext.TenantId),
            cancellationToken);

        var retentionDays = Math.Max(1, settings.RetentionDays);
        var utcNow = request.UtcNow ?? DateTime.UtcNow;
        var cutoffUtc = utcNow.AddDays(-retentionDays);

        return await conversationRepository.RedactExpiredConversationsAsync(
            cutoffUtc,
            retentionDays,
            utcNow,
            request.DryRun,
            cancellationToken);
    }
}
