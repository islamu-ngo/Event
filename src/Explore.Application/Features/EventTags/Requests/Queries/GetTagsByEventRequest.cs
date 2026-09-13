using System;
using System.Collections.Generic;
using Explore.Application.DTOs.Tag;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.EventTags.Requests.Queries;

public sealed record GetTagsByEventRequest(Guid EventId = default) : IQuery<List<TagListDto>>;
