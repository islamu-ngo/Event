using Explore.Application.DTOs.RegistrationMode;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.RegistrationModes.Requests.Queries;

public sealed record GetRegistrationModeDetailsRequest(int Id = default) : IQuery<RegistrationModeDto?>;
