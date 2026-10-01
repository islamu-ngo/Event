using System;
using System.Collections.Generic;
using System.Text;
using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface ITagRepository : IGenericRepository<Tag, Guid>
{
    Task<Tag?> GetTagWithDetails(Guid id);
    Task<List<Tag>> GetTagsWithDetails();
    Task<(List<Tag> Items, int TotalCount)> GetTagsWithDetailsPaged(int pageNumber, int pageSize);
}
