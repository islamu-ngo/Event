using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.RegistrationForms;
using Explore.Application.Features.RegistrationForms.Requests.Queries;
using Explore.Application.Features.RegistrationForms.Validators;
using FluentValidation;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.RegistrationForms.Handlers.Queries;

public sealed class ListRegistrationFormTemplatesQueryHandler(IRegistrationFormTemplateRepository repository)
    : IQueryHandler<ListRegistrationFormTemplatesQuery, IReadOnlyList<RegistrationFormTemplateDto>>
{
    public async Task<IReadOnlyList<RegistrationFormTemplateDto>> QueryAsync(
        ListRegistrationFormTemplatesQuery request,
        CancellationToken cancellationToken = default)
    {
        await new RegistrationFormTemplateQueryValidator<ListRegistrationFormTemplatesQuery>()
            .ValidateAndThrowAsync(request, cancellationToken);
        return [.. (await repository.ListAsync(cancellationToken)).Select(RegistrationFormTemplateMapper.ToDto)];
    }
}

public sealed class GetRegistrationFormTemplateQueryHandler(IRegistrationFormTemplateRepository repository)
    : IQueryHandler<GetRegistrationFormTemplateQuery, RegistrationFormTemplateDto?>
{
    public async Task<RegistrationFormTemplateDto?> QueryAsync(
        GetRegistrationFormTemplateQuery request,
        CancellationToken cancellationToken = default)
    {
        await new RegistrationFormTemplateQueryValidator<GetRegistrationFormTemplateQuery>()
            .ValidateAndThrowAsync(request, cancellationToken);
        return await repository.GetAsync(request.TemplateId, cancellationToken) is { } template
            ? RegistrationFormTemplateMapper.ToDto(template)
            : null;
    }
}
