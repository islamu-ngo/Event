using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.CategoryTypeCategories.Requests.Commands;
using MediatR;

namespace Explore.Application.Features.CategoryTypeCategories.Handlers.Commands;

public class DeleteCategoryTypeCategoriesCommandHandler : IRequestHandler<DeleteCategoryTypeCategoriesCommand, bool>
{
    private readonly ICategoryTypeCategoriesRepository _repository;

    public DeleteCategoryTypeCategoriesCommandHandler(ICategoryTypeCategoriesRepository repository)
    {
        _repository = repository;
    }

    public async Task<bool> Handle(DeleteCategoryTypeCategoriesCommand request, CancellationToken cancellationToken)
    {
        var categoryTypeCategories = await _repository.GetById(request.Id);
        if (categoryTypeCategories == null)
        {
            return false;
        }

        await _repository.Delete(categoryTypeCategories);
        return true;
    }
}
