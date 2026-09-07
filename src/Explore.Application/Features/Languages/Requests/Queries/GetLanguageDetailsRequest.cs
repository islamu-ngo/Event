using Explore.Application.DTOs.Language;
using MediatR;

namespace Explore.Application.Features.Languages.Requests.Queries;

public sealed record GetLanguageDetailsRequest(int Id = default) : IRequest<LanguageDto>;
