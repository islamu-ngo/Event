using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.RegistrationForms;
using Explore.Application.Features.RegistrationForms.Requests.Queries;
using Explore.Application.Features.RegistrationForms.Validators;
using Explore.Application.Services.Registration;
using FluentValidation;
using MediatR;

namespace Explore.Application.Features.RegistrationForms.Handlers.Queries;

public sealed class GetRegistrationWorkflowQueryHandler(IRegistrationFormAuthoringRepository repository)
    : IRequestHandler<GetRegistrationWorkflowQuery, RegistrationWorkflowDto?>
{
    public async Task<RegistrationWorkflowDto?> Handle(GetRegistrationWorkflowQuery request, CancellationToken cancellationToken)
    {
        await new RegistrationFormAuthoringQueryValidator<GetRegistrationWorkflowQuery>()
            .ValidateAndThrowAsync(request, cancellationToken);
        if (await repository.GetWorkflowAsync(request.EventId, request.Purpose, cancellationToken) is not { } workflow)
        {
            return null;
        }

        var forms = await repository.GetFormsAsync(request.EventId, cancellationToken);
        var attachedRequirementIds = await repository.GetAttachedRequirementIdsAsync(
            request.EventId, cancellationToken);
        return RegistrationFormAuthoringMapper.ToDto(workflow, forms, attachedRequirementIds);
    }
}

public sealed class GetRegistrationFormQueryHandler(IRegistrationFormAuthoringRepository repository)
    : IRequestHandler<GetRegistrationFormQuery, RegistrationFormDto?>
{
    public async Task<RegistrationFormDto?> Handle(GetRegistrationFormQuery request, CancellationToken cancellationToken)
    {
        await new RegistrationFormAuthoringQueryValidator<GetRegistrationFormQuery>()
            .ValidateAndThrowAsync(request, cancellationToken);
        return await repository.GetFormAsync(request.EventId, request.FormId, cancellationToken) is { } form
            ? RegistrationFormAuthoringMapper.ToDto(form)
            : null;
    }
}

public sealed class GetRegistrationFormVersionQueryHandler(IRegistrationFormAuthoringRepository repository)
    : IRequestHandler<GetRegistrationFormVersionQuery, RegistrationFormVersionDto?>
{
    public async Task<RegistrationFormVersionDto?> Handle(
        GetRegistrationFormVersionQuery request,
        CancellationToken cancellationToken)
    {
        await new RegistrationFormAuthoringQueryValidator<GetRegistrationFormVersionQuery>()
            .ValidateAndThrowAsync(request, cancellationToken);
        return await repository.GetVersionAsync(request.EventId, request.FormId, request.VersionId, cancellationToken) is { } version
            ? RegistrationFormAuthoringMapper.ToDto(version)
            : null;
    }
}

public sealed class GetRegistrationFormPublishPreflightQueryHandler(
    IRegistrationFormAuthoringRepository repository,
    RegistrationFormPublishPreflightService preflight)
    : IRequestHandler<GetRegistrationFormPublishPreflightQuery, RegistrationFormPublishPreflightDto?>
{
    public async Task<RegistrationFormPublishPreflightDto?> Handle(
        GetRegistrationFormPublishPreflightQuery request,
        CancellationToken cancellationToken)
    {
        await new RegistrationFormAuthoringQueryValidator<GetRegistrationFormPublishPreflightQuery>()
            .ValidateAndThrowAsync(request, cancellationToken);
        return await repository.GetVersionAsync(request.EventId, request.FormId, request.VersionId, cancellationToken) is { } version
            ? preflight.Check(version)
            : null;
    }
}
