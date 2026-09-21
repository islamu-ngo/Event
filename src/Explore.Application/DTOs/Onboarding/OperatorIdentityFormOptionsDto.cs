using System.Collections.Immutable;

namespace Explore.Application.DTOs.Onboarding;

/// <summary>Value-free form vocabulary. Never contains an operator identity document or current values.</summary>
public sealed record OperatorIdentityFormOptionsDto
{
    public IReadOnlyList<OperatorIdentityKindOptionDto> OperatorKinds { get; init => field = value.ToImmutableArray(); } = [];
    public IReadOnlyList<OperatorIdentityCountryOptionDto> Countries { get; init => field = value.ToImmutableArray(); } = [];
    public string CountryState { get; init; } = "Unavailable";
    public string? CountryFailureCode { get; init; }
    /// <summary>No authority registry exists in the identity model; clients must not invent one.</summary>
    public string RegistrationAuthorityState { get; init; } = "NotSupported";
    public IReadOnlyList<OperatorIdentityKindOptionDto> RegistrationAuthorities { get; init => field = value.ToImmutableArray(); } = [];
    public IReadOnlyList<OperatorIdentityFieldConstraintDto> Fields { get; init => field = value.ToImmutableArray(); } = [];
}

public sealed record OperatorIdentityKindOptionDto(string Code, string LabelId);

public sealed record OperatorIdentityCountryOptionDto(string Code, string DisplayName);

/// <summary>Stable localization/association identifiers and domain constraints, not current field values.</summary>
public sealed record OperatorIdentityFieldConstraintDto(
    string Name,
    string LabelId,
    string HelpId,
    string Format,
    int? MaxLength,
    bool RequiredForDisclosure,
    bool RequiredForPaidCommerce);
