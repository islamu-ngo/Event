
using Explore.Application.Models;
using Explore.Domain.Enums;

namespace Explore.Application.DTOs.PublicExperience;

public sealed record VisitorAccessCapabilityDto
{
    public VisitorAccessMode Mode { get; init; }
    public bool AllowsNewNativeAllocation { get; init; }
    public bool AllowsAnonymousParticipation { get; init; }
    public bool AllowsAccountRequiredParticipation { get; init; }
    public bool AllowsExistingAccountLogin { get; init; }
    private IReadOnlyList<VisitorSignupDestinationDto> _signupDestinations = Array.AsReadOnly(Array.Empty<VisitorSignupDestinationDto>());
    public IReadOnlyList<VisitorSignupDestinationDto> SignupDestinations
    {
        get => _signupDestinations;
        init => _signupDestinations = value is null ? null! : Array.AsReadOnly(value.ToArray());
    }

    public static VisitorAccessCapabilityDto From(VisitorAccessCapability capability) => new()
    {
        Mode = capability.Mode,
        AllowsNewNativeAllocation = capability.AllowsNewNativeAllocation,
        AllowsAnonymousParticipation = capability.AllowsAnonymousParticipation,
        AllowsAccountRequiredParticipation = capability.AllowsAccountRequiredParticipation,
        AllowsExistingAccountLogin = capability.AllowsExistingAccountLogin,
        SignupDestinations = capability.SignupDestinations
            .Select(destination => new VisitorSignupDestinationDto(destination.Provider, destination.Url))
            .ToArray()
    };
}

public sealed record VisitorSignupDestinationDto(AuthenticationProviderKind Provider, string Url);
