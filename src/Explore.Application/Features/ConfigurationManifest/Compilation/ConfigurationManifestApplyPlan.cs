namespace Explore.Application.Features.ConfigurationManifest.Compilation;

using System.Collections.Immutable;
using ISLAMU.Wire.Contracts.ConfigurationPortability;
using Explore.Application.Features.ConfigurationManifest.Ingestion;

public sealed record ConfigurationManifestApplyPlan(
    Guid OperationId,
    Guid EffectOutboxId,
    ConfigurationManifestMode Mode,
    string ApiVersion,
    string Kind,
    string ManifestName,
    string Digest,
    string InstanceSectionDigest,
    ConfigurationManifestBootstrapState? BootstrapState,
    DateTime OccurredAt,
    ConfigurationManifestInstancePlan Instance,
    ImmutableArray<ConfigurationManifestTenantPlan> Tenants);

public sealed record ConfigurationManifestBootstrapState(
    string InstanceSectionDigest,
    int Generation);

public sealed record ConfigurationManifestInstancePlan(
    ImmutableArray<ConfigurationManifestSettingWrite> GuardedSettings,
    ImmutableArray<ConfigurationManifestSettingWrite> UnguardedSettings,
    ConfigurationManifestInstancePaidEventPolicyPlan? PaidEventPolicy,
    ImmutableArray<string> ChangedSettingKeyNames,
    ImmutableArray<string> ChangedDocumentKeyNames);

public sealed record ConfigurationManifestTenantPlan(
    int ManifestIndex,
    Guid PlannedTenantId,
    string Slug,
    string DisplayName,
    ImmutableArray<ConfigurationManifestSettingWrite> GuardedSettings,
    ImmutableArray<ConfigurationManifestSettingWrite> UnguardedSettings,
    ConfigurationManifestDocumentWrite BrandingDocument,
    ConfigurationManifestPaidEventPolicyPayloadV1Alpha2? PaidEventPolicy,
    ImmutableArray<string> ChangedSettingKeyNames,
    ImmutableArray<string> ChangedDocumentKeyNames);

public sealed record ConfigurationManifestInstancePaidEventPolicyPlan(
    ConfigurationManifestPaidEventPolicyPayloadV1Alpha2? ProposedRevision,
    int? ExpectedActivePolicyVersion)
{
    public int EffectivePolicyVersion =>
        ExpectedActivePolicyVersion is not { } expected
            ? throw new InvalidOperationException(
                "Paid-event policy authority has not been bound.")
            : ProposedRevision is null
                ? expected
                : checked(expected + 1);
}

public sealed record ConfigurationManifestSettingWrite(
    string Key,
    string JsonValue);

public sealed record ConfigurationManifestDocumentWrite(
    Guid DocumentId,
    string DocumentKey,
    int SchemaVersion,
    string DefaultsVersion,
    string PayloadJson);
