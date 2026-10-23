using Explore.Application.Authorization;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.EventSessionTemplates.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;
using Explore.Domain;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Features.EventSessionTemplates.Handlers.Commands;

public class DeleteEventSessionTemplateCommandHandler(
    IEventSessionTemplateRepository sessionTemplateRepository,
    HybridCache cache,
    ISerializableAttemptOwner owner,
    ITenantContext tenantContext,
    IUserContext userContext,
    IAdminContext adminContext,
    ITenantAdministratorCommitAuthority tenantAuthority,
    IInstanceAdministratorCommitAuthority instanceAuthority,
    RequestAuthorization<DeleteEventSessionTemplateCommand> authorization,
    ILogger<DeleteEventSessionTemplateCommandHandler> logger,
    IHumanAdministrativeActionPreparation preparation) : ICommandHandler<DeleteEventSessionTemplateCommand, bool>
{
    public async Task<bool> ExecuteAsync(DeleteEventSessionTemplateCommand request, CancellationToken cancellationToken)
    {
        Guid tenantId = tenantContext.TenantId;
        if (!userContext.IsAuthenticated || tenantId == Guid.Empty
            || await adminContext.ResolveUserIdAsync(cancellationToken) is not { } userId)
            return false;
        await authorization.AuthorizeAsync(request, logger, cancellationToken);
        EventSessionTemplate? deletedTemplate = null;
        var prepared = await preparation.PrepareAsync(request, userId, cancellationToken);
        bool deleted = await owner.ExecuteAsync(async token =>
        {
            await prepared.AcquireAsync(request, userId, token);
            deletedTemplate = null;
            if (!await instanceAuthority.IsInstanceAdministratorForCommitAsync(userId, token)
                && !await tenantAuthority.IsTenantAdministratorForCommitAsync(userId, tenantId, token))
                return false;
            var template = await sessionTemplateRepository.GetSessionTemplateWithDetails(request.Id, token);
            if (template is null || template.TenantId != tenantId || template.IsDeleted
                || template.EventTemplate is not { IsDeleted: false } parent || parent.TenantId != tenantId)
                return false;
            if (!await sessionTemplateRepository.DeleteSessionTemplate(template.Id, token))
                return false;
            deletedTemplate = template;
            return true;
        }, cancellationToken);
        if (!deleted || deletedTemplate is null)
            return false;

        await cache.RemoveAsync(
            $"session-templates:list:{deletedTemplate.EventTemplateId}:1:{PaginatedResult<object>.DefaultPageSize}",
            cancellationToken);
        await cache.RemoveAsync($"session-templates:detail:{deletedTemplate.Id}", cancellationToken);

        return true;
    }
}
