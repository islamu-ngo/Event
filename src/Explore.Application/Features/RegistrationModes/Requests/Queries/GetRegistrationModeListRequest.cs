using System.Collections.Generic;
using Explore.Application.DTOs.RegistrationMode;
using MediatR;

namespace Explore.Application.Features.RegistrationModes.Requests.Queries;

public sealed record GetRegistrationModeListRequest : IRequest<List<RegistrationModeListDto>>
{
}
