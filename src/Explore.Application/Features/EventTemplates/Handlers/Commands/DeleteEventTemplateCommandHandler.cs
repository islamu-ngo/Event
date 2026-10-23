using Explore.Application.Authorization;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.EventTemplates.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;
using Explore.Domain;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Features.EventTemplates.Handlers.Commands;

public class DeleteEventTemplateCommandHandler(
    IEventTemplateRepository eventTemplateRepository,
    HybridCache cache,
    ISerializableAttemptOwner owner,
    ITenantContext tenantContext,
    IUserContext userContext,
    IAdminContext adminContext,
    ITenantAdministratorCommitAuthority tenantAuthority,
    IInstanceAdministratorCommitAuthority instanceAuthority,
    RequestAuthorization<DeleteEventTemplateCommand> authorization,
    ILogger<DeleteEventTemplateCommandHandler> logger,
    IHumanAdministrativeActionPreparation preparation) : ICommandHandler<DeleteEventTemplateCommand, bool>
{
    public async Task<bool> ExecuteAsync(DeleteEventTemplateCommand request, CancellationToken cancellationToken)
    {
        Guid tenantId = tenantContext.TenantId;
        if (!userContext.IsAuthenticated || tenantId == Guid.Empty
            || await adminContext.ResolveUserIdAsync(cancellationToken) is not { } userId)
            return false;
        await authorization.AuthorizeAsync(request, logger, cancellationToken);
        EventTemplate? deletedTemplate = null;
        var prepared = await preparation.PrepareAsync(request, userId, cancellationToken);
        bool deleted = await owner.ExecuteAsync(async token =>
        {
            await prepared.AcquireAsync(request, userId, token);
            deletedTemplate = null;
            if (!await instanceAuthority.IsInstanceAdministratorForCommitAsync(userId, token)
                && !await tenantAuthority.IsTenantAdministratorForCommitAsync(userId, tenantId, token))
                return false;
            var template = await eventTemplateRepository.GetTemplateWithDetails(request.Id, token);
            if (template is null || template.TenantId != tenantId || template.IsDeleted)
                return false;
            if (!await eventTemplateRepository.DeleteTemplate(template.Id, token))
                return false;
            deletedTemplate = template;
            return true;
        }, cancellationToken);
        if (!deleted || deletedTemplate is null)
            return false;

        await cache.RemoveAsync(
            $"event-templates:list:{deletedTemplate.TenantId}:{(int?)null}:1:{PaginatedResult<object>.DefaultPageSize}",
            cancellationToken);
        await cache.RemoveAsync($"event-templates:detail:{deletedTemplate.Id}", cancellationToken);

        return true;
    }
}
