using Explore.Application.Features.CustomPropertyDefinitions.Requests.Commands;
using Explore.Application.Features.EventCustomProperties.Requests.Commands;
using Explore.Application.Features.EventSessionCustomProperties.Requests.Commands;
using Explore.Application.Features.EventSessionTemplates.Requests.Commands;
using Explore.Application.Features.InstanceAdmin.Requests.Commands;

namespace Explore.Application.Contracts.Persistence;

public partial interface IHumanAdministrativeActionPreparation
{
    Task<IPreparedHumanAdministrativeAction> PrepareAsync(PurgeCustomPropertyDefinitionCommand request,
        Guid principalId, CancellationToken cancellationToken = default);
    Task<IPreparedHumanAdministrativeAction> PrepareAsync(CreateEventCustomPropertyDefinitionCommand request,
        Guid principalId, CancellationToken cancellationToken = default);
    Task<IPreparedHumanAdministrativeAction> PrepareAsync(UpdateEventCustomPropertyDefinitionCommand request,
        Guid principalId, CancellationToken cancellationToken = default);
    Task<IPreparedHumanAdministrativeAction> PrepareAsync(DeleteEventCustomPropertyDefinitionCommand request,
        Guid principalId, CancellationToken cancellationToken = default);
    Task<IPreparedHumanAdministrativeAction> PrepareAsync(PurgeEventCustomPropertyDefinitionCommand request,
        Guid principalId, CancellationToken cancellationToken = default);
    Task<IPreparedHumanAdministrativeAction> PrepareAsync(SetEventCustomPropertyValueCommand request,
        Guid principalId, CancellationToken cancellationToken = default);
    Task<IPreparedHumanAdministrativeAction> PrepareAsync(SetEventCustomPropertyMultiValuesCommand request,
        Guid principalId, CancellationToken cancellationToken = default);
    Task<IPreparedHumanAdministrativeAction> PrepareAsync(CreateEventSessionCustomPropertyDefinitionCommand request,
        Guid principalId, CancellationToken cancellationToken = default);
    Task<IPreparedHumanAdministrativeAction> PrepareAsync(UpdateEventSessionCustomPropertyDefinitionCommand request,
        Guid principalId, CancellationToken cancellationToken = default);
    Task<IPreparedHumanAdministrativeAction> PrepareAsync(DeleteEventSessionCustomPropertyDefinitionCommand request,
        Guid principalId, CancellationToken cancellationToken = default);
    Task<IPreparedHumanAdministrativeAction> PrepareAsync(PurgeEventSessionCustomPropertyDefinitionCommand request,
        Guid principalId, CancellationToken cancellationToken = default);
    Task<IPreparedHumanAdministrativeAction> PrepareAsync(SetEventSessionCustomPropertyValueCommand request,
        Guid principalId, CancellationToken cancellationToken = default);
    Task<IPreparedHumanAdministrativeAction> PrepareAsync(SetEventSessionCustomPropertyMultiValuesCommand request,
        Guid principalId, CancellationToken cancellationToken = default);
    Task<IPreparedHumanAdministrativeAction> PrepareAsync(CreateEventSessionTemplateCommand request,
        Guid principalId, CancellationToken cancellationToken = default);
    Task<IPreparedHumanAdministrativeAction> PrepareAsync(UpdateEventSessionTemplateCommand request,
        Guid principalId, CancellationToken cancellationToken = default);
    Task<IPreparedHumanAdministrativeAction> PrepareAsync(DeleteEventSessionTemplateCommand request,
        Guid principalId, CancellationToken cancellationToken = default);
    Task<IPreparedHumanAdministrativeAction> PrepareAsync(CloneInstanceTenantPlanCommand request,
        Guid principalId, CancellationToken cancellationToken = default);
    Task<IPreparedHumanAdministrativeAction> PrepareAsync(CreateInstanceTenantPlanVersionDraftCommand request,
        Guid principalId, CancellationToken cancellationToken = default);
    Task<IPreparedHumanAdministrativeAction> PrepareAsync(UpdateInstanceTenantPlanVersionDraftCommand request,
        Guid principalId, CancellationToken cancellationToken = default);
    Task<IPreparedHumanAdministrativeAction> PrepareAsync(PublishInstanceTenantPlanVersionCommand request,
        Guid principalId, CancellationToken cancellationToken = default);
    Task<IPreparedHumanAdministrativeAction> PrepareAsync(ArchiveInstanceTenantPlanVersionCommand request,
        Guid principalId, CancellationToken cancellationToken = default);
    Task<IPreparedHumanAdministrativeAction> PrepareAsync(SwitchInstanceTenantPlanAssignmentCommand request,
        Guid principalId, CancellationToken cancellationToken = default);
    Task<IPreparedHumanAdministrativeAction> PrepareAsync(RollbackInstanceTenantPlanAssignmentCommand request,
        Guid principalId, CancellationToken cancellationToken = default);
}
