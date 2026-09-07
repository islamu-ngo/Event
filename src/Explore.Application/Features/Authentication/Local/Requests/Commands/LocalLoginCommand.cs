using Explore.Application.Features.Authentication.Local.Models;
using MediatR;

namespace Explore.Application.Features.Authentication.Local.Requests.Commands;

public sealed record LocalLoginCommand(LocalAuthRequestDto Request) : IRequest<LocalAuthResponseDto>;
