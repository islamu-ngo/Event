using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Application.Features.RegistrationForms;
using Explore.Application.Features.RegistrationForms.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Services.Registration;

namespace Explore.Application.Services.Registration;

public sealed class RegistrationFormAuthoringCommandService(
    IRegistrationFormAuthoringRepository repository,
    IEventRepository eventRepository,
    ITenantContext tenantContext,
    ICurrentUserService currentUserService,
    RegistrationFormPublishPreflightService preflight,
    FormSchemaArtifactPublicationService publication,
    TimeProvider timeProvider)
{
    public async Task<BaseCommandResponse<Guid>> CreateWorkflowAsync(
        CreateRegistrationWorkflowCommand request,
        CancellationToken cancellationToken)
    {
        Event eventEntity = await eventRepository.GetAuthorizationTargetByIdAsync(request.EventId, cancellationToken)
            ?? throw new NotFoundException(nameof(Event), request.EventId);
        EnsureOwned(eventEntity.TenantId, eventEntity.Id, request.EventId);
        EnsureStamp(eventEntity.ConcurrencyStamp, request.ExpectedConcurrencyStamp, nameof(Event), eventEntity.Id);
        if (await repository.GetWorkflowAsync(request.EventId, request.Purpose, cancellationToken) is not null)
        {
            return Failure(request.EventId, "workflow.purpose_exists", "A workflow with this purpose already exists.");
        }

        RegistrationWorkflow workflow = RegistrationWorkflow.Create(
            tenantContext.TenantId, request.EventId, request.Purpose, UtcNow());
        workflow.CreatedBy = currentUserService.UserId;
        await repository.CreateWorkflowAsync(workflow, cancellationToken);
        return Success(workflow.Id, "Registration workflow created.");
    }

    public async Task<BaseCommandResponse<Guid>> UpdateWorkflowAsync(
        UpdateRegistrationWorkflowCommand request,
        CancellationToken cancellationToken)
    {
        RegistrationWorkflow workflow = await Workflow(request.EventId, request.WorkflowId, cancellationToken);
        EnsureStamp(workflow.ConcurrencyStamp, request.ExpectedConcurrencyStamp, nameof(RegistrationWorkflow), workflow.Id);
        workflow.UpdatePurpose(request.Purpose);
        workflow.UpdatedBy = currentUserService.UserId;
        await repository.UpdateWorkflowAsync(workflow, cancellationToken);
        return Success(workflow.Id, "Registration workflow updated.");
    }

    public async Task<BaseCommandResponse<Guid>> CreateRequirementAsync(
        CreateRegistrationRequirementCommand request,
        CancellationToken cancellationToken)
    {
        RegistrationWorkflow workflow = await Workflow(request.EventId, request.WorkflowId, cancellationToken);
        EnsureStamp(workflow.ConcurrencyStamp, request.ExpectedConcurrencyStamp, nameof(RegistrationWorkflow), workflow.Id);
        RegistrationRequirement requirement = RegistrationRequirement.Create(
            workflow,
            request.Ordinal,
            (RegistrationRequirementCriticalityEnum)request.CriticalityId,
            request.CanSkip,
            (RegistrationRequirementCompletionEffectEnum)request.CompletionEffectId,
            (RegistrationAnswerSyncModeEnum)request.AnswerSyncModeId,
            (RegistrationRequirementSubjectTypeEnum)request.AppliesToSubjectTypeId,
            request.AppliesToSubjectId,
            UtcNow());
        requirement.CreatedBy = currentUserService.UserId;
        workflow.AddRequirement(requirement);
        await repository.UpdateWorkflowAsync(workflow, cancellationToken);
        return Success(requirement.Id, "Registration requirement created.");
    }

    public async Task<BaseCommandResponse<Guid>> UpdateRequirementAsync(
        UpdateRegistrationRequirementCommand request,
        CancellationToken cancellationToken)
    {
        RegistrationWorkflow workflow = await Workflow(request.EventId, request.WorkflowId, cancellationToken);
        EnsureStamp(workflow.ConcurrencyStamp, request.ExpectedConcurrencyStamp, nameof(RegistrationWorkflow), workflow.Id);
        RegistrationRequirement requirement = Requirement(workflow, request.RequirementId);
        workflow.UpdateRequirement(
            requirement,
            request.Ordinal,
            (RegistrationRequirementCriticalityEnum)request.CriticalityId,
            request.CanSkip,
            (RegistrationRequirementCompletionEffectEnum)request.CompletionEffectId,
            (RegistrationAnswerSyncModeEnum)request.AnswerSyncModeId,
            (RegistrationRequirementSubjectTypeEnum)request.AppliesToSubjectTypeId,
            request.AppliesToSubjectId);
        requirement.UpdatedBy = currentUserService.UserId;
        await repository.UpdateWorkflowAsync(workflow, cancellationToken);
        return Success(requirement.Id, "Registration requirement updated.");
    }

    public async Task<BaseCommandResponse<Guid>> DeleteRequirementAsync(
        DeleteRegistrationRequirementCommand request,
        CancellationToken cancellationToken)
    {
        RegistrationWorkflow workflow = await Workflow(request.EventId, request.WorkflowId, cancellationToken);
        EnsureStamp(workflow.ConcurrencyStamp, request.ExpectedConcurrencyStamp, nameof(RegistrationWorkflow), workflow.Id);
        RegistrationRequirement requirement = Requirement(workflow, request.RequirementId);
        workflow.RemoveRequirement(requirement, UtcNow());
        requirement.DeletedBy = currentUserService.UserId;
        await repository.UpdateWorkflowAsync(workflow, cancellationToken);
        return Success(requirement.Id, "Registration requirement deleted.");
    }

    public async Task<BaseCommandResponse<Guid>> CreateFormAsync(
        CreateRegistrationFormCommand request,
        CancellationToken cancellationToken)
    {
        RegistrationWorkflow workflow = await Workflow(request.EventId, request.WorkflowId, cancellationToken);
        EnsureStamp(workflow.ConcurrencyStamp, request.ExpectedConcurrencyStamp, nameof(RegistrationWorkflow), workflow.Id);
        DateTime now = UtcNow();
        RegistrationForm form = RegistrationForm.Create(
            tenantContext.TenantId, request.EventId, request.Namespace, request.Key, request.Name, now);
        RegistrationFormVersion version = RegistrationFormVersion.Create(form, 1, request.LanguageTag, null, null, now);
        form.CreatedBy = currentUserService.UserId;
        version.CreatedBy = currentUserService.UserId;
        form.AddVersion(version);
        await repository.CreateFormAsync(form, cancellationToken);
        return Success(form.Id, "Registration form created.");
    }

    public async Task<BaseCommandResponse<Guid>> CreateVersionAsync(
        CreateRegistrationFormVersionCommand request,
        CancellationToken cancellationToken)
    {
        RegistrationForm form = await Form(request.EventId, request.FormId, cancellationToken);
        EnsureStamp(form.ConcurrencyStamp, request.ExpectedConcurrencyStamp, nameof(RegistrationForm), form.Id);
        int nextVersion = form.Versions.Select(value => value.Version).DefaultIfEmpty().Max() + 1;
        RegistrationFormVersion version = request.CloneFromVersionId is { } sourceId
            ? form.Versions.SingleOrDefault(value => value.Id == sourceId && !value.IsDeleted)?.CloneToDraft(nextVersion, UtcNow())
                ?? throw new NotFoundException(nameof(RegistrationFormVersion), sourceId)
            : RegistrationFormVersion.Create(form, nextVersion, request.LanguageTag, null, null, UtcNow());
        version.CreatedBy = currentUserService.UserId;
        form.AddVersion(version);
        form.UpdatedBy = currentUserService.UserId;
        await repository.UpdateFormAsync(form, cancellationToken);
        return Success(version.Id, "Registration form version created.");
    }

    public async Task<BaseCommandResponse<Guid>> AddSectionAsync(
        AddRegistrationFormSectionCommand request,
        CancellationToken cancellationToken)
    {
        RegistrationFormVersion version = await Version(request.EventId, request.FormId, request.VersionId, cancellationToken);
        EnsureStamp(version.ConcurrencyStamp, request.ExpectedConcurrencyStamp, nameof(RegistrationFormVersion), version.Id);
        RegistrationFormSection section = RegistrationFormSection.Create(
            Guid.CreateVersion7(), version, request.Ordinal, request.Title, UtcNow());
        section.CreatedBy = currentUserService.UserId;
        version.AddSection(section);
        await repository.UpdateVersionAsync(version, cancellationToken);
        return Success(section.Id, "Registration form section added.");
    }

    public async Task<BaseCommandResponse<Guid>> UpdateSectionAsync(
        UpdateRegistrationFormSectionCommand request,
        CancellationToken cancellationToken)
    {
        RegistrationFormVersion version = await Version(request.EventId, request.FormId, request.VersionId, cancellationToken);
        EnsureStamp(version.ConcurrencyStamp, request.ExpectedConcurrencyStamp, nameof(RegistrationFormVersion), version.Id);
        RegistrationFormSection section = Section(version, request.SectionId);
        version.UpdateSection(section, request.Ordinal, request.Title);
        section.UpdatedBy = currentUserService.UserId;
        await repository.UpdateVersionAsync(version, cancellationToken);
        return Success(section.Id, "Registration form section updated.");
    }

    public async Task<BaseCommandResponse<Guid>> ReorderSectionsAsync(
        ReorderRegistrationFormSectionsCommand request,
        CancellationToken cancellationToken)
    {
        RegistrationFormVersion version = await Version(request.EventId, request.FormId, request.VersionId, cancellationToken);
        EnsureStamp(version.ConcurrencyStamp, request.ExpectedConcurrencyStamp, nameof(RegistrationFormVersion), version.Id);
        if (version.StatusId != (int)RegistrationFormStatusEnum.Draft)
        {
            return Failure(version.Id, "registration_form_version_immutable",
                "Published or retired registration form versions cannot be reordered.");
        }

        if (!IsCompleteOrder(request.OrderedIds, version.Sections.Where(section => !section.IsDeleted).Select(section => section.Id)))
        {
            return Failure(version.Id, "registration_form_reorder_invalid",
                "Section reorder must contain every active section exactly once.");
        }

        version.ReorderSections(request.OrderedIds);
        version.UpdatedBy = currentUserService.UserId;
        await repository.ReorderSectionsAsync(version, request.OrderedIds, cancellationToken);
        return Success(version.Id, "Registration form sections reordered.");
    }

    public async Task<BaseCommandResponse<Guid>> DeleteSectionAsync(
        DeleteRegistrationFormSectionCommand request,
        CancellationToken cancellationToken)
    {
        RegistrationFormVersion version = await Version(request.EventId, request.FormId, request.VersionId, cancellationToken);
        EnsureStamp(version.ConcurrencyStamp, request.ExpectedConcurrencyStamp, nameof(RegistrationFormVersion), version.Id);
        RegistrationFormSection section = Section(version, request.SectionId);
        version.RemoveSection(section, UtcNow());
        section.DeletedBy = currentUserService.UserId;
        await repository.UpdateVersionAsync(version, cancellationToken);
        return Success(section.Id, "Registration form section deleted.");
    }

    public async Task<BaseCommandResponse<Guid>> AddFieldAsync(
        AddRegistrationFormFieldCommand request,
        CancellationToken cancellationToken)
    {
        RegistrationFormVersion version = await Version(request.EventId, request.FormId, request.VersionId, cancellationToken);
        EnsureStamp(version.ConcurrencyStamp, request.ExpectedConcurrencyStamp, nameof(RegistrationFormVersion), version.Id);
        RegistrationFormSection section = Section(version, request.SectionId);
        RegistrationFormField field = RegistrationFormField.Create(
            Guid.CreateVersion7(), section, request.Ordinal, request.Namespace, request.Key, request.Label,
            (RegistrationFieldTypeEnum)request.FieldTypeId, request.RetentionPolicyId,
            (RegistrationOrganizerVisibilityEnum)request.OrganizerVisibilityId, request.RequiresExplicitConsent,
            request.IsProviderTransferAllowed, request.IsExportable, request.ExportPurposeCode,
            request.IsAnalyticsRelevant, request.IsOperationallyFilterable, UtcNow(), request.ConsentPurposeCode,
            request.ConsentTextVersion, request.ConsentText);
        field.CreatedBy = currentUserService.UserId;
        version.AddField(section, field);
        await repository.UpdateVersionAsync(version, cancellationToken);
        return Success(field.Id, "Registration form field added.");
    }

    public async Task<BaseCommandResponse<Guid>> UpdateFieldAsync(
        UpdateRegistrationFormFieldCommand request,
        CancellationToken cancellationToken)
    {
        RegistrationFormVersion version = await Version(request.EventId, request.FormId, request.VersionId, cancellationToken);
        EnsureStamp(version.ConcurrencyStamp, request.ExpectedConcurrencyStamp, nameof(RegistrationFormVersion), version.Id);
        RegistrationFormField field = Field(Section(version, request.SectionId), request.FieldId);
        version.UpdateFieldDetails(field, request.Ordinal, request.Label);
        version.UpdateFieldGovernance(field, request.RetentionPolicyId,
            (RegistrationOrganizerVisibilityEnum)request.OrganizerVisibilityId, request.RequiresExplicitConsent,
            request.IsProviderTransferAllowed, request.IsExportable, request.ExportPurposeCode,
            request.IsAnalyticsRelevant, request.IsOperationallyFilterable,
            request.ConsentPurposeCode, request.ConsentTextVersion, request.ConsentText);
        version.UpdateFieldValidation(field, request.IsRequired, request.IsMulti, request.MinLength, request.MaxLength,
            request.RegexPattern, request.MinNumber, request.MaxNumber, request.MinDateTime, request.MaxDateTime,
            request.AllowedUrlSchemes);
        field.UpdatedBy = currentUserService.UserId;
        await repository.UpdateVersionAsync(version, cancellationToken);
        return Success(field.Id, "Registration form field updated.");
    }

    public async Task<BaseCommandResponse<Guid>> ReorderFieldsAsync(
        ReorderRegistrationFormFieldsCommand request,
        CancellationToken cancellationToken)
    {
        RegistrationFormVersion version = await Version(request.EventId, request.FormId, request.VersionId, cancellationToken);
        EnsureStamp(version.ConcurrencyStamp, request.ExpectedConcurrencyStamp, nameof(RegistrationFormVersion), version.Id);
        if (version.StatusId != (int)RegistrationFormStatusEnum.Draft)
        {
            return Failure(version.Id, "registration_form_version_immutable",
                "Published or retired registration form versions cannot be reordered.");
        }

        RegistrationFormSection section = Section(version, request.SectionId);
        if (!IsCompleteOrder(request.OrderedIds, section.Fields.Where(field => !field.IsDeleted).Select(field => field.Id)))
        {
            return Failure(version.Id, "registration_form_reorder_invalid",
                "Field reorder must contain every active field in the section exactly once.");
        }

        version.ReorderFields(section, request.OrderedIds);
        version.UpdatedBy = currentUserService.UserId;
        await repository.ReorderFieldsAsync(version, request.OrderedIds, cancellationToken);
        return Success(version.Id, "Registration form fields reordered.");
    }

    public async Task<BaseCommandResponse<Guid>> DeleteFieldAsync(
        DeleteRegistrationFormFieldCommand request,
        CancellationToken cancellationToken)
    {
        RegistrationFormVersion version = await Version(request.EventId, request.FormId, request.VersionId, cancellationToken);
        EnsureStamp(version.ConcurrencyStamp, request.ExpectedConcurrencyStamp, nameof(RegistrationFormVersion), version.Id);
        RegistrationFormField field = Field(Section(version, request.SectionId), request.FieldId);
        version.RemoveField(field, UtcNow());
        field.DeletedBy = currentUserService.UserId;
        await repository.UpdateVersionAsync(version, cancellationToken);
        return Success(field.Id, "Registration form field deleted.");
    }

    public async Task<BaseCommandResponse<Guid>> AddOptionAsync(
        AddRegistrationFormFieldOptionCommand request,
        CancellationToken cancellationToken)
    {
        RegistrationFormVersion version = await Version(request.EventId, request.FormId, request.VersionId, cancellationToken);
        EnsureStamp(version.ConcurrencyStamp, request.ExpectedConcurrencyStamp, nameof(RegistrationFormVersion), version.Id);
        RegistrationFormField field = Field(Section(version, request.SectionId), request.FieldId);
        RegistrationFormFieldOption option = RegistrationFormFieldOption.Create(
            Guid.CreateVersion7(), field, request.Ordinal, request.Key, request.Label, UtcNow());
        option.CreatedBy = currentUserService.UserId;
        version.AddOption(field, option);
        await repository.UpdateVersionAsync(version, cancellationToken);
        return Success(option.Id, "Registration form field option added.");
    }

    public async Task<BaseCommandResponse<Guid>> UpdateOptionAsync(
        UpdateRegistrationFormFieldOptionCommand request,
        CancellationToken cancellationToken)
    {
        RegistrationFormVersion version = await Version(request.EventId, request.FormId, request.VersionId, cancellationToken);
        EnsureStamp(version.ConcurrencyStamp, request.ExpectedConcurrencyStamp, nameof(RegistrationFormVersion), version.Id);
        RegistrationFormField field = Field(Section(version, request.SectionId), request.FieldId);
        RegistrationFormFieldOption option = Option(field, request.OptionId);
        version.UpdateFieldOption(field, option, request.Ordinal, request.Key, request.Label);
        option.UpdatedBy = currentUserService.UserId;
        await repository.UpdateVersionAsync(version, cancellationToken);
        return Success(option.Id, "Registration form field option updated.");
    }

    public async Task<BaseCommandResponse<Guid>> RetireOptionAsync(
        RetireRegistrationFormFieldOptionCommand request,
        CancellationToken cancellationToken)
    {
        RegistrationFormVersion version = await Version(request.EventId, request.FormId, request.VersionId, cancellationToken);
        EnsureStamp(version.ConcurrencyStamp, request.ExpectedConcurrencyStamp, nameof(RegistrationFormVersion), version.Id);
        RegistrationFormField field = Field(Section(version, request.SectionId), request.FieldId);
        RegistrationFormFieldOption option = Option(field, request.OptionId);
        version.RetireOption(field, option, UtcNow());
        option.UpdatedBy = currentUserService.UserId;
        await repository.UpdateVersionAsync(version, cancellationToken);
        return Success(option.Id, "Registration form field option retired.");
    }

    public async Task<BaseCommandResponse<Guid>> AddRuleAsync(
        AddRegistrationFormRuleCommand request,
        CancellationToken cancellationToken)
    {
        RegistrationFormVersion version = await Version(request.EventId, request.FormId, request.VersionId, cancellationToken);
        EnsureStamp(version.ConcurrencyStamp, request.ExpectedConcurrencyStamp, nameof(RegistrationFormVersion), version.Id);
        RegistrationFormRule rule = RegistrationFormRule.Create(Guid.CreateVersion7(), version, request.Ordinal,
            new(request.TargetNamespace, request.TargetKey), (RegistrationFormRuleEffect)request.Effect,
            RegistrationFormAuthoringMapper.ToDomain(request.Condition), UtcNow());
        rule.CreatedBy = currentUserService.UserId;
        version.AddRule(rule);
        await repository.UpdateVersionAsync(version, cancellationToken);
        return Success(rule.Id, "Registration form rule added.");
    }

    public async Task<BaseCommandResponse<Guid>> UpdateRuleAsync(
        UpdateRegistrationFormRuleCommand request,
        CancellationToken cancellationToken)
    {
        RegistrationFormVersion version = await Version(request.EventId, request.FormId, request.VersionId, cancellationToken);
        EnsureStamp(version.ConcurrencyStamp, request.ExpectedConcurrencyStamp, nameof(RegistrationFormVersion), version.Id);
        RegistrationFormRule rule = Rule(version, request.RuleId);
        version.UpdateRule(rule, request.Ordinal, new(request.TargetNamespace, request.TargetKey),
            (RegistrationFormRuleEffect)request.Effect, RegistrationFormAuthoringMapper.ToDomain(request.Condition));
        rule.UpdatedBy = currentUserService.UserId;
        await repository.UpdateVersionAsync(version, cancellationToken);
        return Success(rule.Id, "Registration form rule updated.");
    }

    public async Task<BaseCommandResponse<Guid>> DeleteRuleAsync(
        DeleteRegistrationFormRuleCommand request,
        CancellationToken cancellationToken)
    {
        RegistrationFormVersion version = await Version(request.EventId, request.FormId, request.VersionId, cancellationToken);
        EnsureStamp(version.ConcurrencyStamp, request.ExpectedConcurrencyStamp, nameof(RegistrationFormVersion), version.Id);
        RegistrationFormRule rule = Rule(version, request.RuleId);
        version.RemoveRule(rule, UtcNow());
        rule.DeletedBy = currentUserService.UserId;
        await repository.UpdateVersionAsync(version, cancellationToken);
        return Success(rule.Id, "Registration form rule deleted.");
    }

    public async Task<BaseCommandResponse<Guid>> PublishAsync(
        PublishRegistrationFormVersionCommand request,
        CancellationToken cancellationToken)
    {
        RegistrationFormVersion version = await Version(request.EventId, request.FormId, request.VersionId, cancellationToken);
        EnsureStamp(version.ConcurrencyStamp, request.ExpectedConcurrencyStamp, nameof(RegistrationFormVersion), version.Id);
        var result = preflight.Check(version);
        if (!result.CanPublish)
        {
            return BaseCommandResponse.Failure<Guid>(
                "registration_form_preflight_failed",
                "Registration form publication preflight failed.",
                result.Issues.Select(issue => $"{issue.Code}: {issue.Message}"),
                version.Id);
        }

        publication.Publish(version, UtcNow());
        version.UpdatedBy = currentUserService.UserId;
        await repository.UpdateVersionAsync(version, cancellationToken);
        return Success(version.Id, "Registration form version published.");
    }

    private async Task<RegistrationWorkflow> Workflow(Guid eventId, Guid workflowId, CancellationToken cancellationToken)
    {
        RegistrationWorkflow workflow = await repository.GetWorkflowForUpdateAsync(eventId, workflowId, cancellationToken)
            ?? throw new NotFoundException(nameof(RegistrationWorkflow), workflowId);
        EnsureOwned(workflow.TenantId, workflow.EventId, eventId);
        return workflow;
    }

    private async Task<RegistrationForm> Form(Guid eventId, Guid formId, CancellationToken cancellationToken)
    {
        RegistrationForm form = await repository.GetFormForUpdateAsync(eventId, formId, cancellationToken)
            ?? throw new NotFoundException(nameof(RegistrationForm), formId);
        EnsureOwned(form.TenantId, form.EventId, eventId);
        return form;
    }

    private async Task<RegistrationFormVersion> Version(
        Guid eventId,
        Guid formId,
        Guid versionId,
        CancellationToken cancellationToken)
    {
        RegistrationFormVersion version = await repository.GetVersionForUpdateAsync(eventId, formId, versionId, cancellationToken)
            ?? throw new NotFoundException(nameof(RegistrationFormVersion), versionId);
        EnsureOwned(version.TenantId, version.EventId, eventId);
        if (version.RegistrationFormId != formId)
        {
            throw new NotFoundException(nameof(RegistrationFormVersion), versionId);
        }

        return version;
    }

    private void EnsureOwned(Guid tenantId, Guid actualEventId, Guid expectedEventId)
    {
        if (tenantId != tenantContext.TenantId || actualEventId != expectedEventId)
        {
            throw new NotFoundException("Registration authoring resource", expectedEventId);
        }
    }

    private static void EnsureStamp(Guid actual, Guid expected, string entityType, Guid entityId)
    {
        if (expected == Guid.Empty || actual != expected)
        {
            throw new ConcurrencyConflictException(
                ConcurrencyConflictException.ConcurrentUpdate,
                "The registration authoring resource was modified by another request. Reload and retry.",
                entityType,
                entityId.ToString());
        }
    }

    private static RegistrationRequirement Requirement(RegistrationWorkflow workflow, Guid id) =>
        workflow.Requirements.SingleOrDefault(value => value.Id == id && !value.IsDeleted)
        ?? throw new NotFoundException(nameof(RegistrationRequirement), id);

    private static RegistrationFormSection Section(RegistrationFormVersion version, Guid id) =>
        version.Sections.SingleOrDefault(value => value.Id == id && !value.IsDeleted)
        ?? throw new NotFoundException(nameof(RegistrationFormSection), id);

    private static RegistrationFormField Field(RegistrationFormSection section, Guid id) =>
        section.Fields.SingleOrDefault(value => value.Id == id && !value.IsDeleted)
        ?? throw new NotFoundException(nameof(RegistrationFormField), id);

    private static RegistrationFormFieldOption Option(RegistrationFormField field, Guid id) =>
        field.Options.SingleOrDefault(value => value.Id == id && !value.IsDeleted)
        ?? throw new NotFoundException(nameof(RegistrationFormFieldOption), id);

    private static RegistrationFormRule Rule(RegistrationFormVersion version, Guid id) =>
        version.Rules.SingleOrDefault(value => value.Id == id && !value.IsDeleted)
        ?? throw new NotFoundException(nameof(RegistrationFormRule), id);

    private DateTime UtcNow() => timeProvider.GetUtcNow().UtcDateTime;

    private static bool IsCompleteOrder(IReadOnlyList<Guid> orderedIds, IEnumerable<Guid> activeIds)
    {
        Guid[] active = [.. activeIds];
        return orderedIds.Count is > 0 and <= 200 &&
            orderedIds.Count == active.Length &&
            orderedIds.Distinct().Count() == orderedIds.Count &&
            orderedIds.All(id => id != Guid.Empty && active.Contains(id));
    }

    private static BaseCommandResponse<Guid> Success(Guid id, string message) => BaseCommandResponse.Success(id, message);

    private static BaseCommandResponse<Guid> Failure(Guid id, string code, string message) =>
        BaseCommandResponse.Failure<Guid>(code, message, [$"{code}: {message}"], id);
}
