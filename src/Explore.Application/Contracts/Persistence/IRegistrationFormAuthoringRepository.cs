using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IRegistrationFormAuthoringRepository
{
    Task<RegistrationWorkflow?> GetWorkflowAsync(
        Guid eventId,
        string purpose,
        CancellationToken cancellationToken);

    Task<RegistrationWorkflow?> GetWorkflowForUpdateAsync(
        Guid eventId,
        Guid workflowId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<RegistrationForm>> GetFormsAsync(
        Guid eventId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<RegistrationFormVersion>> GetPublishedVersionsAsync(
        Guid eventId,
        int limit,
        CancellationToken cancellationToken);

    Task<IReadOnlySet<Guid>> GetAttachedRequirementIdsAsync(
        Guid eventId,
        CancellationToken cancellationToken);

    Task<RegistrationForm?> GetFormAsync(
        Guid eventId,
        Guid formId,
        CancellationToken cancellationToken);

    Task<RegistrationForm?> GetFormForUpdateAsync(
        Guid eventId,
        Guid formId,
        CancellationToken cancellationToken);

    Task<RegistrationFormVersion?> GetVersionAsync(
        Guid eventId,
        Guid formId,
        Guid versionId,
        CancellationToken cancellationToken);

    Task<RegistrationFormVersion?> GetTemplateSourceVersionAsync(
        Guid eventId,
        Guid formId,
        Guid versionId,
        CancellationToken cancellationToken);

    Task<RegistrationFormVersion?> GetVersionForUpdateAsync(
        Guid eventId,
        Guid formId,
        Guid versionId,
        CancellationToken cancellationToken);

    Task CreateWorkflowAsync(RegistrationWorkflow workflow, CancellationToken cancellationToken);
    Task UpdateWorkflowAsync(RegistrationWorkflow workflow, CancellationToken cancellationToken);
    Task CreateFormAsync(RegistrationForm form, CancellationToken cancellationToken);
    Task UpdateFormAsync(RegistrationForm form, CancellationToken cancellationToken);
    Task UpdateVersionAsync(RegistrationFormVersion version, CancellationToken cancellationToken);
    Task ReorderSectionsAsync(
        RegistrationFormVersion version,
        IReadOnlyList<Guid> orderedSectionIds,
        CancellationToken cancellationToken);
    Task ReorderFieldsAsync(
        RegistrationFormVersion version,
        IReadOnlyList<Guid> orderedFieldIds,
        CancellationToken cancellationToken);
}
