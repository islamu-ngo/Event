using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class ParticipationRequirementAttachmentRepository(ExploreDbContext dbContext)
    : IParticipationRequirementAttachmentRepository
{
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

    public Task<EventParticipationConfiguration?> GetOptionalQuestionnaireAsync(
        Guid eventId,
        Guid tenantId,
        CancellationToken cancellationToken) =>
        ConfigurationGraph()
            .AsNoTracking()
            .FirstOrDefaultAsync(configuration =>
                configuration.Id == eventId && configuration.TenantId == tenantId &&
                configuration.ParticipationHandlingModeId == (int)ParticipationHandlingModeEnum.WalkIn &&
                configuration.RequirementAttachments.Any(attachment => attachment.IsStandaloneQuestionnaire),
                cancellationToken);

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
