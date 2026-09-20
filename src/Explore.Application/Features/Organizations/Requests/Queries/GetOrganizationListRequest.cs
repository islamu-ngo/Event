using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;
using Explore.Application.DTOs.Organization;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.Organizations.Requests.Queries;

public sealed record GetOrganizationListRequest : IQuery<PaginatedResult<OrganizationListDto>>
{
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}
