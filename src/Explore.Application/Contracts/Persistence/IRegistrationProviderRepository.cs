using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IRegistrationProviderRepository
{
    Task<RegistrationProviderConnection?> GetConnectionAsync(Guid tenantId, Guid connectionId, CancellationToken cancellationToken);

    Task<IReadOnlyList<RegistrationProviderConnection>> GetConnectionsAsync(Guid tenantId, CancellationToken cancellationToken);

    Task<RegistrationProviderBinding?> GetBindingAsync(Guid tenantId, Guid bindingId, CancellationToken cancellationToken);

    Task<bool> FormVersionBelongsToEventAsync(Guid tenantId, Guid eventId, Guid formId, Guid formVersionId, CancellationToken cancellationToken);

    Task<IReadOnlyList<RegistrationProviderBinding>> GetBindingsAsync(Guid tenantId, CancellationToken cancellationToken);

    Task<RegistrationRequirement?> GetRequirementAsync(Guid tenantId, Guid eventId, Guid workflowId, Guid requirementId, CancellationToken cancellationToken);

    Task<RegistrationChannel?> GetChannelAsync(Guid tenantId, Guid eventId, Guid workflowId, Guid requirementId, Guid channelId, CancellationToken cancellationToken);

    Task<RegistrationProviderBinding?> GetBindingForCallbackAsync(Guid bindingId, CancellationToken cancellationToken);

    Task<bool> HasSubmissionForBindingAsync(Guid tenantId, Guid bindingId, CancellationToken cancellationToken);

    Task<IReadOnlyList<RegistrationProviderBinding>> GetBindingsForEventAsync(Guid tenantId, Guid eventId, CancellationToken cancellationToken);

    Task<DateTime?> GetLastCallbackAtAsync(Guid tenantId, Guid bindingId, CancellationToken cancellationToken);

    Task<int> CountParkedItemsAsync(Guid tenantId, Guid bindingId, CancellationToken cancellationToken);

    Task<DateTime?> GetOldestPendingItemAtAsync(Guid tenantId, Guid bindingId, CancellationToken cancellationToken);

    Task<RegistrationForm?> GetFormForExternalImportAsync(Guid tenantId, Guid eventId, Guid formId, CancellationToken cancellationToken);

    Task<RegistrationForm?> GetExternalImportFormAsync(
        Guid tenantId,
        Guid eventId,
        Guid connectionId,
        string providerSurveyId,
        CancellationToken cancellationToken);

    Task<RegistrationProviderSchemaRevision?> GetLatestExternalImportSchemaRevisionAsync(
        Guid tenantId,
        Guid eventId,
        Guid formId,
        Guid connectionId,
        string providerSurveyId,
        CancellationToken cancellationToken);

    Task<RegistrationProviderSchemaRevision?> GetSchemaRevisionByHashAsync(
        Guid tenantId,
        Guid connectionId,
        string providerSurveyId,
        RegistrationEvidenceHash revisionHash,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<RegistrationProviderParkedItem>> GetParkedItemsForEventAsync(
        Guid tenantId,
        Guid eventId,
        int limit,
        CancellationToken cancellationToken);

    Task<RegistrationSubmission?> GetParkedSubmissionAsync(Guid tenantId, Guid eventId, Guid submissionId, CancellationToken cancellationToken);

    Task AddSubmissionIssueAsync(RegistrationSubmissionIssue issue, CancellationToken cancellationToken);

    Task AddConnectionAsync(RegistrationProviderConnection connection, CancellationToken cancellationToken);

    Task AddBindingAsync(RegistrationProviderBinding binding, CancellationToken cancellationToken);

    Task AddFormAsync(RegistrationForm form, CancellationToken cancellationToken);

    Task AddChannelAsync(RegistrationChannel channel, CancellationToken cancellationToken);

    Task AddSchemaRevisionAsync(RegistrationProviderSchemaRevision revision, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public sealed record RegistrationProviderParkedSubmission(
    RegistrationSubmission Submission,
    IReadOnlyList<RegistrationSubmissionIssue> Issues);

public sealed record RegistrationProviderParkedEffect(
    IncomingWebhookEffectOutbox Effect,
    Guid BindingId,
    Guid EventId);

public sealed record RegistrationProviderParkedItem(
    RegistrationProviderParkedSubmission? Submission,
    RegistrationProviderParkedEffect? Effect);
