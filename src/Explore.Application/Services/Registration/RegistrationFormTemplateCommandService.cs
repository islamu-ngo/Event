using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Application.Features.RegistrationForms.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Application.Services.Registration;

public sealed class RegistrationFormTemplateCommandService(
    IRegistrationFormTemplateRepository templates,
    IRegistrationFormAuthoringRepository forms,
    IEventRepository events,
    ITenantContext tenantContext,
    ICurrentUserService currentUser,
    IAdminContext adminContext,
    TimeProvider timeProvider)
{
    public async Task<BaseCommandResponse<Guid>> CreateAsync(
        CreateRegistrationFormTemplateCommand request,
        CancellationToken cancellationToken)
    {
        RegistrationFormVersion source = await forms.GetTemplateSourceVersionAsync(
            request.Input.SourceEventId,
            request.Input.SourceRegistrationFormId,
            request.Input.SourceRegistrationFormVersionId,
            cancellationToken) ?? throw new NotFoundException(nameof(RegistrationFormVersion), request.Input.SourceRegistrationFormVersionId);
        if (source.StatusId != (int)RegistrationFormStatusEnum.Published)
        {
            return Failure(source.Id, "registration_form_template_source_not_published", "Templates must point at a published registration form version.");
        }

        if (request.Input.IsPlatformOwned && !await adminContext.IsInstanceAdminAsync(cancellationToken))
        {
            throw new AuthorizationException("Only instance administrators can create platform registration form templates.");
        }

        Guid? ownerTenantId = request.Input.IsPlatformOwned ? null : tenantContext.TenantId;
        if (!request.Input.IsPlatformOwned && source.TenantId != tenantContext.TenantId)
        {
            throw new NotFoundException(nameof(RegistrationFormVersion), source.Id);
        }

        RegistrationFormTemplate template = RegistrationFormTemplate.Create(
            ownerTenantId,
            request.Input.Name,
            request.Input.Description,
            request.Input.Category,
            request.Input.PackKey,
            source,
            UtcNow());
        template.CreatedBy = currentUser.UserId;
        await templates.CreateAsync(template, cancellationToken);
        return Success(template.Id, "Registration form template created.");
    }

    public async Task<BaseCommandResponse<Guid>> InstantiateAsync(
        InstantiateRegistrationFormTemplateCommand request,
        CancellationToken cancellationToken)
    {
        RegistrationFormTemplate template = await templates.GetAsync(request.TemplateId, cancellationToken)
            ?? throw new NotFoundException(nameof(RegistrationFormTemplate), request.TemplateId);
        if (template.TenantId is { } ownerTenantId && ownerTenantId != tenantContext.TenantId)
        {
            throw new NotFoundException(nameof(RegistrationFormTemplate), request.TemplateId);
        }

        RegistrationWorkflow workflow = await forms.GetWorkflowForUpdateAsync(
            request.Input.EventId,
            request.Input.WorkflowId,
            cancellationToken) ?? throw new NotFoundException(nameof(RegistrationWorkflow), request.Input.WorkflowId);
        if (workflow.TenantId != tenantContext.TenantId)
        {
            throw new NotFoundException(nameof(RegistrationWorkflow), request.Input.WorkflowId);
        }

        EnsureStamp(workflow.ConcurrencyStamp, request.Input.ExpectedWorkflowConcurrencyStamp, nameof(RegistrationWorkflow), workflow.Id);
        Event targetEvent = await events.GetAuthorizationTargetByIdAsync(request.Input.EventId, cancellationToken)
            ?? throw new NotFoundException(nameof(Event), request.Input.EventId);
        if (targetEvent.TenantId != tenantContext.TenantId)
        {
            throw new NotFoundException(nameof(Event), request.Input.EventId);
        }

        RegistrationFormVersion source = await forms.GetTemplateSourceVersionAsync(
            template.SourceEventId,
            template.SourceRegistrationFormId,
            template.SourceRegistrationFormVersionId,
            cancellationToken) ?? throw new NotFoundException(nameof(RegistrationFormVersion), template.SourceRegistrationFormVersionId);
        DateTime now = UtcNow();
        RegistrationForm form = RegistrationForm.Create(
            tenantContext.TenantId,
            request.Input.EventId,
            request.Input.Namespace,
            request.Input.Key,
            request.Input.Name,
            now);
        RegistrationFormVersion version = source.CloneToTemplateInstance(
            form,
            now,
            template.SourceRegistrationFormId,
            template.SourceRegistrationFormVersionId);
        form.CreatedBy = currentUser.UserId;
        version.CreatedBy = currentUser.UserId;
        form.AddVersion(version);
        await forms.CreateFormAsync(form, cancellationToken);
        return Success(form.Id, "Registration form instantiated from template.");
    }

    private DateTime UtcNow() => timeProvider.GetUtcNow().UtcDateTime;

    private static BaseCommandResponse<Guid> Success(Guid id, string message) => BaseCommandResponse.Success(id, message);

    private static BaseCommandResponse<Guid> Failure(Guid id, string code, string message) =>
        BaseCommandResponse.Failure<Guid>(code, message, [message], id);

    private static void EnsureStamp(Guid actual, Guid expected, string entityType, Guid entityId)
    {
        if (expected == Guid.Empty || actual != expected)
        {
            throw new ConcurrencyConflictException(
                ConcurrencyConflictException.ConcurrentUpdate,
                "The registration workflow was modified by another request. Reload and retry.",
                entityType,
                entityId.ToString());
        }
    }
}
