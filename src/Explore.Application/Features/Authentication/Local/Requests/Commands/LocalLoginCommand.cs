using Explore.Application.Contracts.Operations;
using Explore.Application.Features.Authentication.Local.Models;

namespace Explore.Application.Features.Authentication.Local.Requests.Commands;

public sealed record LocalLoginCommand(LocalAuthRequestDto Request) : ICommand<LocalAuthResponseDto>;
