using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.RegistrationForms;
using Explore.Application.Features.RegistrationForms.Requests.Queries;
using Explore.Domain;
using Explore.Domain.Enums;
using MediatR;

namespace Explore.Application.Features.RegistrationForms.Handlers.Queries;

public sealed class GetOptionalQuestionnaireQueryHandler(
    IParticipationRequirementAttachmentRepository repository,
    IEventRepository events,
    ITenantContext tenantContext)
    : IRequestHandler<GetOptionalQuestionnaireQuery, OptionalQuestionnaireDto?>
{
    public async Task<OptionalQuestionnaireDto?> Handle(
        GetOptionalQuestionnaireQuery request,
        CancellationToken cancellationToken)
    {
        if (request.EventId == Guid.Empty)
        {
            return null;
        }

        if (!await events.IsPubliclyEligibleAsync(
                tenantContext.TenantId, request.EventId, cancellationToken))
        {
            return null;
        }

        EventParticipationConfiguration? configuration = await repository.GetOptionalQuestionnaireAsync(
            request.EventId, tenantContext.TenantId, cancellationToken);
        ParticipationRequirementAttachment? attachment = configuration?.RequirementAttachments.SingleOrDefault(value =>
            !value.IsDeleted && value.IsStandaloneQuestionnaire);
        RegistrationRequirement? requirement = attachment?.RegistrationRequirement;
        RegistrationFormVersion? version = attachment?.RegistrationFormVersion;
        if (configuration?.ParticipationHandlingModeId != (int)ParticipationHandlingModeEnum.WalkIn ||
            requirement is null || requirement.IsDeleted ||
            requirement.CompletionEffectId != (int)RegistrationRequirementCompletionEffectEnum.NoRegistrationEffect ||
            version is null || version.IsDeleted || version.StatusId != (int)RegistrationFormStatusEnum.Published ||
            string.IsNullOrWhiteSpace(version.SchemaHash) ||
            string.IsNullOrWhiteSpace(version.DataSchemaArtifact) ||
            string.IsNullOrWhiteSpace(version.UiSchemaArtifact) ||
            string.IsNullOrWhiteSpace(version.LogicSchemaArtifact) ||
            string.IsNullOrWhiteSpace(version.MappingArtifact))
        {
            return null;
        }

        return new OptionalQuestionnaireDto(
            request.EventId,
            attachment!.RegistrationWorkflowId,
            attachment.RegistrationRequirementId,
            version.RegistrationFormId,
            version.Id,
            version.Version,
            version.LanguageTag,
            version.SchemaHash,
            version.DataSchemaArtifact,
            version.UiSchemaArtifact,
            version.LogicSchemaArtifact,
            version.MappingArtifact,
            configuration.ConcurrencyStamp);
    }
}
