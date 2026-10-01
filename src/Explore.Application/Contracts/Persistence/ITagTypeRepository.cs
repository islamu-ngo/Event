using System;
using System.Collections.Generic;
using System.Text;
using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface ITagTypeRepository : IGenericRepository<TagType, int>
{
    Task<TagType?> GetTagTypeWithDetails(int id);
    Task<List<TagType>> GetTagTypesWithDetails();
}
