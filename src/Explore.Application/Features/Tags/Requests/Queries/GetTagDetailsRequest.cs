using System;
using Explore.Application.DTOs.Tag;
using MediatR;

namespace Explore.Application.Features.Tags.Requests.Queries;

public sealed record GetTagDetailsRequest(Guid Id = default) : IRequest<TagDto>;
