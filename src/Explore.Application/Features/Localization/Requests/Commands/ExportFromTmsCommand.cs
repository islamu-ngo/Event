using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.Localization.Requests.Commands;

public sealed record ExportFromTmsCommand : IRequest<BaseCommandResponse<Guid>>
{
    public required string LanguageCode { get; init; }
}
