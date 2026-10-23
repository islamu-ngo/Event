using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class ParticipationRequirementAttachmentRepository(
    ExploreDbContext dbContext,
    PublicEventDiscoveryReadBindingFactory? publicDiscoveryBindings = null)
    : IParticipationRequirementAttachmentRepository
{
    private PublicEventDiscoveryReadBinding? _publicReadBinding;

    public Task<EventParticipationConfiguration?> GetConfigurationForUpdateAsync(
        Guid eventId,
        Guid tenantId,
        CancellationToken cancellationToken) =>
        ConfigurationGraph()
            .FirstOrDefaultAsync(configuration =>
                configuration.Id == eventId && configuration.TenantId == tenantId,
                cancellationToken);

    public Task<RegistrationWorkflow?> GetWorkflowForUpdateAsync(
        Guid eventId,
        Guid tenantId,
        Guid workflowId,
        CancellationToken cancellationToken) =>
        dbContext.RegistrationWorkflows
            .Include(workflow => workflow.Requirements)
            .ThenInclude(requirement => requirement.Channels)
            .FirstOrDefaultAsync(workflow =>
                workflow.Id == workflowId && workflow.EventId == eventId && workflow.TenantId == tenantId,
                cancellationToken);

    public Task<RegistrationFormVersion?> GetPublishedVersionAsync(
        Guid eventId,
        Guid tenantId,
        Guid formId,
        Guid versionId,
        CancellationToken cancellationToken) =>
        dbContext.RegistrationFormVersions
            .FirstOrDefaultAsync(version =>
                version.Id == versionId && version.RegistrationFormId == formId &&
                version.EventId == eventId && version.TenantId == tenantId &&
                version.StatusId == (int)RegistrationFormStatusEnum.Published,
                cancellationToken);

    public async Task<EventParticipationConfiguration?> GetOptionalQuestionnaireAsync(
        Guid eventId,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        if (DiscoveryRepositoryNativeOperations.For(dbContext) is { } native)
        {
            if (tenantId == Guid.Empty || dbContext.TenantFilterTenantId != tenantId)
                throw new UnauthorizedAccessException("Public questionnaire requires its selected tenant.");
            var binding = _publicReadBinding ??= (publicDiscoveryBindings
                ?? throw new UnauthorizedAccessException("Public questionnaire requires its original Runtime binding."))
                .Create(dbContext);
            var entity = await native.ReadPublicDetailAsync(
                binding, eventId, null, cancellationToken);
            var configuration = entity?.ParticipationConfiguration;
            return configuration?.ParticipationHandlingModeId == (int)ParticipationHandlingModeEnum.WalkIn
                && configuration.RequirementAttachments.Any(attachment => attachment.IsStandaloneQuestionnaire)
                ? configuration : null;
        }
        return await ConfigurationGraph()
            .AsNoTracking()
            .FirstOrDefaultAsync(configuration =>
                configuration.Id == eventId && configuration.TenantId == tenantId &&
                configuration.ParticipationHandlingModeId == (int)ParticipationHandlingModeEnum.WalkIn &&
                configuration.RequirementAttachments.Any(attachment => attachment.IsStandaloneQuestionnaire),
                cancellationToken);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new ConcurrencyConflictException(
                ConcurrencyConflictException.ConcurrentUpdate,
                "Participation configuration was modified by another request. Reload and retry.",
                innerException: exception);
        }
    }

    private IQueryable<EventParticipationConfiguration> ConfigurationGraph() =>
        dbContext.EventParticipationConfigurations
            .Include(configuration => configuration.RequirementAttachments)
            .ThenInclude(attachment => attachment.RegistrationRequirement)
            .ThenInclude(requirement => requirement!.Channels)
            .Include(configuration => configuration.RequirementAttachments)
            .ThenInclude(attachment => attachment.RegistrationFormVersion);
}
