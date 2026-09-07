using Explore.Application.Caching;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Application.Features.RegistrationForms.Requests.Commands;
using Explore.Application.Features.RegistrationForms.Validators;
using Explore.Application.Responses;
using Explore.Domain;
using FluentValidation.Results;
using MediatR;
using Microsoft.Extensions.Caching.Hybrid;

namespace Explore.Application.Features.RegistrationForms.Handlers.Commands;

public sealed class AttachRegistrationRequirementCommandHandler(
    IParticipationRequirementAttachmentRepository repository,
    ITenantContext tenantContext,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    HybridCache cache)
    : IRequestHandler<AttachRegistrationRequirementCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(
        AttachRegistrationRequirementCommand request,
        CancellationToken cancellationToken)
    {
        ValidationResult validation = await new AttachRegistrationRequirementCommandValidator()
            .ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Failure(request.RequirementId, "registration_requirement_validation_failed",
                validation.Errors.Select(error => error.ErrorMessage));
        }

        Guid attachmentId = Guid.CreateVersion7();
        DateTime attachedAt = timeProvider.GetUtcNow().UtcDateTime;
        try
        {
            BaseCommandResponse<Guid> response = await unitOfWork.ExecuteInTransactionAsync(async token =>
            {
                EventParticipationConfiguration? configuration = await repository.GetConfigurationForUpdateAsync(
                    request.EventId, tenantContext.TenantId, token);
                if (configuration is null)
                {
                    return Missing(request.RequirementId);
                }

                if (configuration.ConcurrencyStamp != request.ExpectedConcurrencyStamp)
                {
                    return Conflict(request.RequirementId);
                }

                RegistrationWorkflow? workflow = await repository.GetWorkflowForUpdateAsync(
                    request.EventId, tenantContext.TenantId, request.WorkflowId, token);
                RegistrationRequirement? requirement = workflow?.Requirements.SingleOrDefault(value =>
                    !value.IsDeleted && value.Id == request.RequirementId);
                if (workflow is null || requirement is null)
                {
                    return Missing(request.RequirementId);
                }

                RegistrationFormVersion? version = null;
                if (request.StandaloneQuestionnaire)
                {
                    version = await repository.GetPublishedVersionAsync(
                        request.EventId,
                        tenantContext.TenantId,
                        request.RegistrationFormId!.Value,
                        request.RegistrationFormVersionId!.Value,
                        token);
                    if (version is null)
                    {
                        return Missing(request.RequirementId);
                    }
                }

                configuration.AttachRequirement(
                    attachmentId,
                    workflow,
                    requirement,
                    version,
                    request.StandaloneQuestionnaire,
                    attachedAt);
                await repository.SaveChangesAsync(token);
                return Success(request.RequirementId, "Registration requirement attached.");
            }, cancellationToken);
            if (response.IsSuccess)
            {
                await cache.RemoveAsync($"event:detail:{request.EventId}", cancellationToken);
                await cache.RemoveByTagAsync(
                    CacheTags.EventListByTenant(tenantContext.TenantId), cancellationToken);
            }

            return response;
        }
        catch (ConcurrencyConflictException)
        {
            return Conflict(request.RequirementId);
        }
        catch (InvalidOperationException exception)
        {
            string code = exception.Message == "The registration requirement is already attached."
                ? "registration_requirement_already_attached"
                : "registration_requirement_mode_invalid";
            return Failure(request.RequirementId, code, ["Registration requirement is incompatible with participation configuration."]);
        }
        catch (ArgumentException)
        {
            return Missing(request.RequirementId);
        }
    }

    internal static BaseCommandResponse<Guid> Success(Guid id, string message) => BaseCommandResponse.Success(id, message);

    internal static BaseCommandResponse<Guid> Missing(Guid id) => Failure(
        id, "registration_requirement_not_found", ["Registration requirement was not found."]);

    internal static BaseCommandResponse<Guid> Conflict(Guid id) => Failure(
        id, "registration_requirement_concurrency_conflict",
        ["Participation configuration changed since it was loaded."]);

    internal static BaseCommandResponse<Guid> Failure(Guid id, string code, IEnumerable<string> errors) =>
        BaseCommandResponse.Failure<Guid>(code, errors.First(), errors, id);
}

public sealed class DetachRegistrationRequirementCommandHandler(
    IParticipationRequirementAttachmentRepository repository,
    ITenantContext tenantContext,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    HybridCache cache)
    : IRequestHandler<DetachRegistrationRequirementCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(
        DetachRegistrationRequirementCommand request,
        CancellationToken cancellationToken)
    {
        ValidationResult validation = await new DetachRegistrationRequirementCommandValidator()
            .ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return AttachRegistrationRequirementCommandHandler.Failure(
                request.RequirementId,
                "registration_requirement_validation_failed",
                validation.Errors.Select(error => error.ErrorMessage));
        }

        DateTime detachedAt = timeProvider.GetUtcNow().UtcDateTime;
        try
        {
            BaseCommandResponse<Guid> response = await unitOfWork.ExecuteInTransactionAsync(async token =>
            {
                EventParticipationConfiguration? configuration = await repository.GetConfigurationForUpdateAsync(
                    request.EventId, tenantContext.TenantId, token);
                if (configuration is null)
                {
                    return AttachRegistrationRequirementCommandHandler.Missing(request.RequirementId);
                }

                if (configuration.ConcurrencyStamp != request.ExpectedConcurrencyStamp)
                {
                    return AttachRegistrationRequirementCommandHandler.Conflict(request.RequirementId);
                }

                configuration.DetachRequirement(request.RequirementId, detachedAt);
                await repository.SaveChangesAsync(token);
                return AttachRegistrationRequirementCommandHandler.Success(
                    request.RequirementId, "Registration requirement detached.");
            }, cancellationToken);
            if (response.IsSuccess)
            {
                await cache.RemoveAsync($"event:detail:{request.EventId}", cancellationToken);
                await cache.RemoveByTagAsync(
                    CacheTags.EventListByTenant(tenantContext.TenantId), cancellationToken);
            }

            return response;
        }
        catch (ConcurrencyConflictException)
        {
            return AttachRegistrationRequirementCommandHandler.Conflict(request.RequirementId);
        }
    }
}
