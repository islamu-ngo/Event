using System;
using System.Collections.Generic;
using System.Text;
using Explore.Application.DTOs.StatusType;
using MediatR;

namespace Explore.Application.Features.StatusTypes.Requests.Queries;

public sealed record GetStatusTypeListRequest : IRequest<List<StatusTypeListDto>>
{
    public int Id { get; init; }
    public required string FullName { get; init; }
    public string? Description { get; init; }
}
