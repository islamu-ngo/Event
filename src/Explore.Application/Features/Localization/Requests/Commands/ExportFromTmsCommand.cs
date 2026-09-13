using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.Localization.Requests.Commands;

public sealed record ExportFromTmsCommand : ICommand<BaseCommandResponse<Guid>>
{
    public required string LanguageCode { get; init; }
}
