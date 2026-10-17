using Explore.Application.Features.CustomPropertyDefinitions.Requests.Commands;
using Explore.Application.Features.EventTemplates.Requests.Commands;
using Explore.Application.Features.InstanceAdmin.Requests.Commands;

namespace Explore.Application.Contracts.Persistence;

/// <summary>
/// Captures the original human, credential, selected policy inputs and support horizon
/// before the existing administrative data owner begins. These are not typed Settings operations.
/// </summary>
public partial interface IHumanAdministrativeActionPreparation
{
    Task<IPreparedHumanAdministrativeAction> PrepareAsync(CreateEventTemplateCommand request,
        Guid principalId, CancellationToken cancellationToken = default);
    Task<IPreparedHumanAdministrativeAction> PrepareAsync(UpdateEventTemplateCommand request,
        Guid principalId, CancellationToken cancellationToken = default);
    Task<IPreparedHumanAdministrativeAction> PrepareAsync(DeleteEventTemplateCommand request,
        Guid principalId, CancellationToken cancellationToken = default);
    Task<IPreparedHumanAdministrativeAction> PrepareAsync(CreateCustomPropertyDefinitionCommand request,
        Guid principalId, CancellationToken cancellationToken = default);
    Task<IPreparedHumanAdministrativeAction> PrepareAsync(UpdateCustomPropertyDefinitionCommand request,
        Guid principalId, CancellationToken cancellationToken = default);
    Task<IPreparedHumanAdministrativeAction> PrepareAsync(DeleteCustomPropertyDefinitionCommand request,
        Guid principalId, CancellationToken cancellationToken = default);
    Task<IPreparedHumanAdministrativeAction> PrepareAsync(CreateInstanceTenantPlanDraftCommand request,
        Guid principalId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Single-use original-action custody. Acquisition borrows the already selected Serializable
/// owner, holds original sources and registers terminal admission; it grants no administrator role.
/// </summary>
public interface IPreparedHumanAdministrativeAction
{
    Task AcquireAsync(object originalRequest, Guid principalId, CancellationToken cancellationToken = default);
}
