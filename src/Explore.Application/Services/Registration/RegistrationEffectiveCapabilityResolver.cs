using Explore.Application.Contracts.Services.Registration;
using Explore.Domain;

namespace Explore.Application.Services.Registration;

public sealed class RegistrationEffectiveCapabilityResolver(IRegistrationProviderRegistry registry)
{
    public RegistrationEffectiveCapabilityResult Resolve(
        RegistrationProviderBinding binding,
        RegistrationEffectiveCapabilityRequest request)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(request);

        IRegistrationProviderDescriptor? descriptor = registry.TryResolve(request.Tuple);
        RegistrationProviderCapabilitySet persisted = RegistrationProviderCapabilitySet.FromCodes(
            binding.Capabilities.Where(capability => !capability.IsDeleted && CapabilityBelongsToTuple(capability, request.Tuple))
                .Select(capability => capability.CapabilityCode));
        RegistrationProviderCapabilitySet configured = persisted.Intersect(request.RequestedCapabilities);
        RegistrationProviderCapabilitySet proven = descriptor?.ProvenCapabilities ?? RegistrationProviderCapabilitySet.None;
        RegistrationProviderCapabilitySet effective = proven.Intersect(configured).Intersect(request.GovernanceAllowlist);
        RegistrationProviderCapabilitySet configuredPresentation = configured.Intersect(request.GovernanceAllowlist);

        List<string> blockers = [];
        if (descriptor is null) blockers.Add("unknown_tuple");
        if (!request.MappingCompatible) blockers.Add("mapping_incompatible");
        if (SchemaDriftClassifier.BlocksPublication(request.DriftClass)) blockers.Add("blocking_drift");
        if (!request.IsAuthorized) blockers.Add("authorization_denied");

        bool policyAllowsAuto = descriptor is not null && request.MappingCompatible && request.IsAuthorized &&
            !SchemaDriftClassifier.BlocksPublication(request.DriftClass);
        return new(
            descriptor is not null,
            descriptor is null ? configuredPresentation.Redirect : effective.Redirect,
            descriptor is null ? configuredPresentation.Manual : effective.Manual,
            policyAllowsAuto && effective.AutoFinalize,
            effective with { AutoFinalize = policyAllowsAuto && effective.AutoFinalize },
            blockers);
    }

    private static bool CapabilityBelongsToTuple(RegistrationProviderCapability capability, RegistrationProviderTuple tuple) =>
        StringComparer.Ordinal.Equals(capability.ProviderCode, tuple.ProviderCode) &&
        StringComparer.Ordinal.Equals(capability.DeploymentKind, tuple.DeploymentKind) &&
        StringComparer.Ordinal.Equals(capability.ApiVersion, tuple.ApiVersion) &&
        StringComparer.Ordinal.Equals(capability.AdapterPolicyVersion, tuple.AdapterPolicyVersion) &&
        StringComparer.Ordinal.Equals(capability.ConformanceEvidenceRevision, tuple.ConformanceEvidenceRevision);
}
