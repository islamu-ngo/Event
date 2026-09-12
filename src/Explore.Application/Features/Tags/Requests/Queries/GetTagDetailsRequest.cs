using System;
using Explore.Application.DTOs.Tag;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.Tags.Requests.Queries;

public sealed record GetTagDetailsRequest(Guid Id = default) : IQuery<TagDto?>;
