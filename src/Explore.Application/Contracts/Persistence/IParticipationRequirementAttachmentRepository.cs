using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IParticipationRequirementAttachmentRepository
{
    Task<EventParticipationConfiguration?> GetConfigurationForUpdateAsync(
        Guid eventId,
        Guid tenantId,
        CancellationToken cancellationToken);

    Task<RegistrationWorkflow?> GetWorkflowForUpdateAsync(
        Guid eventId,
        Guid tenantId,
        Guid workflowId,
        CancellationToken cancellationToken);

    Task<RegistrationFormVersion?> GetPublishedVersionAsync(
        Guid eventId,
        Guid tenantId,
        Guid formId,
        Guid versionId,
        CancellationToken cancellationToken);

    Task<EventParticipationConfiguration?> GetOptionalQuestionnaireAsync(
        Guid eventId,
        Guid tenantId,
        CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
