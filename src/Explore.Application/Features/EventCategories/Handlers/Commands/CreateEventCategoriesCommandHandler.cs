using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventCategories.Validators;
using Explore.Application.Features.EventCategories.Requests.Commands;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.EventCategories.Handlers.Commands;

public class CreateEventCategoriesCommandHandler : IRequestHandler<CreateEventCategoriesCommand, BaseCommandResponse<Guid>>
{
    private readonly IEventCategoriesRepository _eventCategoriesRepository;
    private readonly IEventRepository _eventRepository;
    private readonly ICategoryRepository _categoryRepository;
    private readonly ITenantContext _tenantContext;

    public CreateEventCategoriesCommandHandler(
        IEventCategoriesRepository eventCategoriesRepository,
        IEventRepository eventRepository,
        ICategoryRepository categoryRepository,
        ITenantContext tenantContext)
    {
        _eventCategoriesRepository = eventCategoriesRepository;
        _eventRepository = eventRepository;
        _categoryRepository = categoryRepository;
        _tenantContext = tenantContext;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(CreateEventCategoriesCommand request, CancellationToken cancellationToken)
    {
        var validator = new CreateEventCategoriesDtoValidator(_eventRepository, _categoryRepository, _eventCategoriesRepository);
        var validationResult = await validator.ValidateAsync(request.EventCategoriesDto, cancellationToken);

        if (!validationResult.IsValid)
        {
            return BaseCommandResponse.Validation<Guid>(
                validationResult.Errors.Select(e => e.ErrorMessage),
                "Event Category assignment failed.");
        }

        var eventCategories = new Domain.EventCategories
        {
            EventId = request.EventCategoriesDto.EventId,
            CategoryId = request.EventCategoriesDto.CategoryId,
            Event = null!,
            Category = null!,
            Tenant = null!
        };

        // Set TenantId from the request context
        eventCategories.TenantId = _tenantContext.TenantId;

        eventCategories = await _eventCategoriesRepository.Create(eventCategories);

        return BaseCommandResponse.Success(eventCategories.Id, "Event Category assigned successfully.");
    }
}
