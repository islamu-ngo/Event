using Explore.Application.DTOs.UiShell;
using MediatR;

namespace Explore.Application.Features.UiShell.Requests.Queries;

public sealed record GetUiShellContextRequest : IRequest<UiShellContextDto>;
