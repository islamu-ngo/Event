using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.CustomPropertyDefinitions.Requests.Commands;
using Explore.Application.Features.EventCustomProperties.Requests.Commands;
using Explore.Application.Features.EventSessionCustomProperties.Requests.Commands;
using Explore.Application.Features.EventSessionTemplates.Requests.Commands;
using Explore.Application.Features.InstanceAdmin.Requests.Commands;
using System.Text.Json;

namespace Explore.Infrastructure.Services;

public sealed partial class HumanAdministrativeActionPreparation
{
    public Task<IPreparedHumanAdministrativeAction> PrepareAsync(PurgeCustomPropertyDefinitionCommand request,
        Guid principalId, CancellationToken cancellationToken = default) =>
        PrepareTenantActionAsync(request, principalId, cancellationToken);
    public Task<IPreparedHumanAdministrativeAction> PrepareAsync(CreateEventCustomPropertyDefinitionCommand request,
        Guid principalId, CancellationToken cancellationToken = default) =>
        PrepareTenantActionAsync(request, principalId, cancellationToken);
    public Task<IPreparedHumanAdministrativeAction> PrepareAsync(UpdateEventCustomPropertyDefinitionCommand request,
        Guid principalId, CancellationToken cancellationToken = default) =>
        PrepareTenantActionAsync(request, principalId, cancellationToken);
    public Task<IPreparedHumanAdministrativeAction> PrepareAsync(DeleteEventCustomPropertyDefinitionCommand request,
        Guid principalId, CancellationToken cancellationToken = default) =>
        PrepareTenantActionAsync(request, principalId, cancellationToken);
    public Task<IPreparedHumanAdministrativeAction> PrepareAsync(PurgeEventCustomPropertyDefinitionCommand request,
        Guid principalId, CancellationToken cancellationToken = default) =>
        PrepareTenantActionAsync(request, principalId, cancellationToken);
    public Task<IPreparedHumanAdministrativeAction> PrepareAsync(SetEventCustomPropertyValueCommand request,
        Guid principalId, CancellationToken cancellationToken = default) =>
        PrepareTenantActionAsync(request, principalId, cancellationToken);
    public Task<IPreparedHumanAdministrativeAction> PrepareAsync(SetEventCustomPropertyMultiValuesCommand request,
        Guid principalId, CancellationToken cancellationToken = default) =>
        PrepareTenantActionAsync(request, principalId, cancellationToken);
    public Task<IPreparedHumanAdministrativeAction> PrepareAsync(CreateEventSessionCustomPropertyDefinitionCommand request,
        Guid principalId, CancellationToken cancellationToken = default) =>
        PrepareTenantActionAsync(request, principalId, cancellationToken);
    public Task<IPreparedHumanAdministrativeAction> PrepareAsync(UpdateEventSessionCustomPropertyDefinitionCommand request,
        Guid principalId, CancellationToken cancellationToken = default) =>
        PrepareTenantActionAsync(request, principalId, cancellationToken);
    public Task<IPreparedHumanAdministrativeAction> PrepareAsync(DeleteEventSessionCustomPropertyDefinitionCommand request,
        Guid principalId, CancellationToken cancellationToken = default) =>
        PrepareTenantActionAsync(request, principalId, cancellationToken);
    public Task<IPreparedHumanAdministrativeAction> PrepareAsync(PurgeEventSessionCustomPropertyDefinitionCommand request,
        Guid principalId, CancellationToken cancellationToken = default) =>
        PrepareTenantActionAsync(request, principalId, cancellationToken);
    public Task<IPreparedHumanAdministrativeAction> PrepareAsync(SetEventSessionCustomPropertyValueCommand request,
        Guid principalId, CancellationToken cancellationToken = default) =>
        PrepareTenantActionAsync(request, principalId, cancellationToken);
    public Task<IPreparedHumanAdministrativeAction> PrepareAsync(SetEventSessionCustomPropertyMultiValuesCommand request,
        Guid principalId, CancellationToken cancellationToken = default) =>
        PrepareTenantActionAsync(request, principalId, cancellationToken);
    public Task<IPreparedHumanAdministrativeAction> PrepareAsync(CreateEventSessionTemplateCommand request,
        Guid principalId, CancellationToken cancellationToken = default) =>
        PrepareTenantActionAsync(request, principalId, cancellationToken);
    public Task<IPreparedHumanAdministrativeAction> PrepareAsync(UpdateEventSessionTemplateCommand request,
        Guid principalId, CancellationToken cancellationToken = default) =>
        PrepareTenantActionAsync(request, principalId, cancellationToken);
    public Task<IPreparedHumanAdministrativeAction> PrepareAsync(DeleteEventSessionTemplateCommand request,
        Guid principalId, CancellationToken cancellationToken = default) =>
        PrepareTenantActionAsync(request, principalId, cancellationToken);
    public Task<IPreparedHumanAdministrativeAction> PrepareAsync(CloneInstanceTenantPlanCommand request,
        Guid principalId, CancellationToken cancellationToken = default) =>
        PrepareInstanceActionAsync(request, principalId, cancellationToken);
    public Task<IPreparedHumanAdministrativeAction> PrepareAsync(CreateInstanceTenantPlanVersionDraftCommand request,
        Guid principalId, CancellationToken cancellationToken = default) =>
        PrepareInstanceActionAsync(request, principalId, cancellationToken);
    public Task<IPreparedHumanAdministrativeAction> PrepareAsync(UpdateInstanceTenantPlanVersionDraftCommand request,
        Guid principalId, CancellationToken cancellationToken = default) =>
        PrepareInstanceActionAsync(request, principalId, cancellationToken);
    public Task<IPreparedHumanAdministrativeAction> PrepareAsync(PublishInstanceTenantPlanVersionCommand request,
        Guid principalId, CancellationToken cancellationToken = default) =>
        PrepareInstanceActionAsync(request, principalId, cancellationToken);
    public Task<IPreparedHumanAdministrativeAction> PrepareAsync(ArchiveInstanceTenantPlanVersionCommand request,
        Guid principalId, CancellationToken cancellationToken = default) =>
        PrepareInstanceActionAsync(request, principalId, cancellationToken);
    public Task<IPreparedHumanAdministrativeAction> PrepareAsync(SwitchInstanceTenantPlanAssignmentCommand request,
        Guid principalId, CancellationToken cancellationToken = default) =>
        PrepareInstanceActionAsync(request, principalId, cancellationToken);
    public Task<IPreparedHumanAdministrativeAction> PrepareAsync(RollbackInstanceTenantPlanAssignmentCommand request,
        Guid principalId, CancellationToken cancellationToken = default) =>
        PrepareInstanceActionAsync(request, principalId, cancellationToken);

    private Task<IPreparedHumanAdministrativeAction> PrepareInstanceActionAsync(
        object request, Guid principalId, CancellationToken cancellationToken)
    {
        string originalContent = JsonSerializer.Serialize(request, request.GetType());
        void RequireContent()
        {
            if (!string.Equals(originalContent, JsonSerializer.Serialize(request, request.GetType()), StringComparison.Ordinal))
                throw new UnauthorizedAccessException("The original administrative request content changed.");
        }
        return PrepareAsync(request, principalId, instance: true, RequireContent, cancellationToken);
    }
}
