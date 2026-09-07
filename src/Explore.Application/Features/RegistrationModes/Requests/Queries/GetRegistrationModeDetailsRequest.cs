using Explore.Application.DTOs.RegistrationMode;
using MediatR;

namespace Explore.Application.Features.RegistrationModes.Requests.Queries;

public sealed record GetRegistrationModeDetailsRequest(int Id = default) : IRequest<RegistrationModeDto>;
