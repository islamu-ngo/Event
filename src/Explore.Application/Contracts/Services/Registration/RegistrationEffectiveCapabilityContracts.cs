namespace Explore.Application.Contracts.Services.Registration;

public sealed record RegistrationEffectiveCapabilityRequest(
    Guid TenantId,
    Guid BindingId,
    RegistrationProviderTuple Tuple,
    RegistrationProviderCapabilitySet GovernanceAllowlist,
    RegistrationProviderCapabilitySet RequestedCapabilities,
    RegistrationProviderSchemaDriftClass DriftClass,
    bool MappingCompatible,
    bool IsAuthorized);

public sealed record RegistrationEffectiveCapabilityResult(
    bool TupleKnown,
    bool RedirectAvailable,
    bool ManualAvailable,
    bool AutoFinalizable,
    RegistrationProviderCapabilitySet EffectiveCapabilities,
    IReadOnlyList<string> Blockers);

public enum RegistrationProviderSchemaDriftClass
{
    NoDrift = 1,
    AdditiveOptionalChange = 2,
    LabelOnlyChange = 3,
    MappingRequired = 4,
    RequiredFieldRemoved = 5,
    TypeChanged = 6,
    OptionSetChanged = 7,
    UnsupportedChange = 8
}
