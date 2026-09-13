using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.UiShell;

namespace Explore.Application.Features.UiShell.Requests.Queries;

public sealed record GetUiShellContextRequest : IQuery<UiShellContextDto>;
