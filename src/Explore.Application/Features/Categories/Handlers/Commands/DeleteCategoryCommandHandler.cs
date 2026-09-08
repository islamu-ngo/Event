using System;
using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Categories.Requests.Commands;
using MediatR;
using Microsoft.Extensions.Caching.Hybrid;

namespace Explore.Application.Features.Categories.Handlers.Commands;

public class DeleteCategoryCommandHandler : IRequestHandler<DeleteCategoryCommand, bool>
{
    private readonly ICategoryRepository _categoryRepository;
    private readonly HybridCache _cache;

    public DeleteCategoryCommandHandler(ICategoryRepository categoryRepository, HybridCache cache)
    {
        _categoryRepository = categoryRepository;
        _cache = cache;
    }

    public async Task<bool> Handle(DeleteCategoryCommand request, CancellationToken cancellationToken)
    {
        var category = await _categoryRepository.GetById(request.Id);

        if (category == null)
        {
            return false;
        }

        await _categoryRepository.Delete(category);
        await _cache.RemoveAsync("categories:list:1:20", cancellationToken);

        return true;
    }
}
