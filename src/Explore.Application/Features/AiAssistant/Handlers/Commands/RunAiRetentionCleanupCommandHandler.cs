using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.AiAssistant.Requests.Commands;
using Explore.Application.Models;
using Explore.Application.Contracts.Services;
using Explore.Domain.Settings.Definitions;

namespace Explore.Application.Features.AiAssistant.Handlers.Commands;

public sealed class RunAiRetentionCleanupCommandHandler(
    IAiConversationRepository conversationRepository,
    ITenantContext tenantContext,
    ISystemSettingRepository systemSettings,
    ITenantSettingRepository tenantSettings,
    ISettingMutationLock mutationLock,
    IUnitOfWork unitOfWork,
    TimeProvider clock)
    : ICommandHandler<RunAiRetentionCleanupCommand, AiRetentionCleanupResult>
{
    public async Task<AiRetentionCleanupResult> ExecuteAsync(
        RunAiRetentionCleanupCommand request,
        CancellationToken cancellationToken)
    {
        Guid tenantId = tenantContext.TenantId;
        if (tenantId == Guid.Empty)
            throw new InvalidOperationException("AI retention requires an exact tenant execution scope.");
        var definition = AiAssistantSettingDefinitions.RetentionDays;
        DateTime utcNow = request.UtcNow ?? clock.GetUtcNow().UtcDateTime;
        return await mutationLock.ExecuteOrderedGroupsAsync(
            [[definition.Key]],
            token => unitOfWork.ExecuteSerializableAsync(async transactionToken =>
            {
                var system = await systemSettings.GetByKey(definition.Key, transactionToken);
                var tenant = system?.IsLocked == true
                    ? null
                    : await tenantSettings.GetByTenantAndKey(tenantId, definition.Key, transactionToken);
                string value = tenant?.Value ?? system?.Value ?? definition.DefaultValue;
                if (!int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int configuredDays))
                    throw new InvalidOperationException("AI retention policy is not a valid persisted duration.");
                int retentionDays = Math.Max(1, configuredDays);
                return await conversationRepository.RedactExpiredConversationsAsync(
                    utcNow.AddDays(-retentionDays), retentionDays, utcNow, request.DryRun, transactionToken);
            }, token), cancellationToken);
    }
}
