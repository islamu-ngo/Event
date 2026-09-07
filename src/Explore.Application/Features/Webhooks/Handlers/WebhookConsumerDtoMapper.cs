using Explore.Application.Contracts.Webhooks;
using Explore.Application.DTOs.Webhooks;
using Explore.Application.Lookups;
using Explore.Domain;

namespace Explore.Application.Features.Webhooks.Handlers;

internal static class WebhookConsumerDtoMapper
{
    private static readonly WebhookProviderCapability[] IndividualCapabilities =
        Enum.GetValues<WebhookProviderCapability>()
            .Where(capability => capability != WebhookProviderCapability.None && IsSingleFlag(capability))
            .ToArray();

    public static WebhookConsumerDto Map(
        WebhookConsumer consumer,
        WebhookProviderModeCapabilityResolution resolution,
        WebhookConsumerProviderBinding? providerBinding)
    {
        var consumerKind = NormalizedLookupMetadata.WebhookConsumerKind(consumer.ConsumerKindId);
        var status = NormalizedLookupMetadata.WebhookConsumerStatus(consumer.StatusId);
        var providerMode = NormalizedLookupMetadata.WebhookProviderMode(consumer.ProviderModeId);
        var exactBinding = ResolveExactBinding(consumer, resolution, providerBinding);
        var providerCapabilities = exactBinding?.EffectiveGovernedCapabilities
            ?? WebhookProviderCapability.None;
        var authorityAvailable = resolution.IsProviderModeAvailable &&
            (consumer.ProviderMode is not (WebhookProviderMode.Svix or WebhookProviderMode.Composite) ||
             exactBinding is not null);
        return new WebhookConsumerDto
        {
            Id = consumer.Id,
            TenantId = consumer.TenantId,
            InstanceId = consumer.InstanceId,
            OrganizationId = consumer.OrganizationId,
            GroupId = consumer.GroupId,
            OwnerUserId = consumer.OwnerUserId,
            OwnerId = consumer.OwnerId,
            ConsumerKindId = consumerKind.Id,
            ConsumerKindCode = consumerKind.Code,
            ConsumerKindName = consumerKind.Name,
            StatusId = status.Id,
            StatusCode = status.Code,
            StatusName = status.Name,
            ProviderModeId = providerMode.Id,
            ProviderModeCode = providerMode.Code,
            ProviderModeName = providerMode.Name,
            ProviderCapabilityAuthorityAvailable = authorityAvailable,
            CapabilityResolutionVersion = resolution.ResolutionVersion,
            CapabilityUnavailableReasonCode = ResolveAuthorityFailure(consumer, resolution, exactBinding),
            ProviderCapabilities = IndividualCapabilities
                .Select(capability => MapCapability(capability, resolution, providerCapabilities, exactBinding))
                .ToArray(),
            Name = consumer.Name,
            ConfigurationVersion = consumer.ConfigurationVersion,
            CreatedAt = consumer.CreatedAt,
            UpdatedAt = consumer.UpdatedAt
        };
    }

    private static WebhookProviderCapabilityDto MapCapability(
        WebhookProviderCapability capability,
        WebhookProviderModeCapabilityResolution resolution,
        WebhookProviderCapability providerCapabilities,
        WebhookConsumerProviderBinding? exactBinding)
    {
        var localAvailable = (resolution.LocalCapabilities & capability) == capability;
        var providerAvailable = (providerCapabilities & capability) == capability;
        var sources = new List<string>(2);
        if (localAvailable)
        {
            sources.Add("LOCAL");
        }

        if (providerAvailable)
        {
            sources.Add("SVIX");
        }

        var metadata = NormalizedLookupMetadata.WebhookProviderCapability((int)capability);
        return new WebhookProviderCapabilityDto
        {
            CapabilityId = metadata.Id,
            CapabilityCode = metadata.Code,
            CapabilityName = metadata.Name,
            IsAvailable = sources.Count > 0,
            AvailableFromProviderCodes = sources,
            UnavailableReasonCode = ResolveCapabilityFailure(
                capability,
                resolution,
                exactBinding,
                sources.Count > 0)
        };
    }

    private static WebhookConsumerProviderBinding? ResolveExactBinding(
        WebhookConsumer consumer,
        WebhookProviderModeCapabilityResolution resolution,
        WebhookConsumerProviderBinding? binding) =>
        binding is not null &&
        binding.IsVerifiedFor(consumer.TenantId, consumer.Id) &&
        binding.ProviderKind == WebhookProviderKind.Svix &&
        string.Equals(binding.ProviderEnvironment, resolution.ProviderEnvironment, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(binding.ProviderVersion, resolution.ProviderVersion, StringComparison.Ordinal) &&
        string.Equals(binding.CapabilityResolutionVersion, resolution.ResolutionVersion, StringComparison.Ordinal)
            ? binding
            : null;

    private static string? ResolveAuthorityFailure(
        WebhookConsumer consumer,
        WebhookProviderModeCapabilityResolution resolution,
        WebhookConsumerProviderBinding? exactBinding)
    {
        if (!resolution.IsProviderModeAvailable)
        {
            return resolution.UnavailableReasonCode;
        }

        return consumer.ProviderMode is WebhookProviderMode.Svix or WebhookProviderMode.Composite && exactBinding is null
            ? "webhook_provider_binding_unverified"
            : null;
    }

    private static string? ResolveCapabilityFailure(
        WebhookProviderCapability capability,
        WebhookProviderModeCapabilityResolution resolution,
        WebhookConsumerProviderBinding? exactBinding,
        bool isAvailable)
    {
        if (isAvailable)
        {
            return null;
        }

        if (!resolution.IsProviderModeAvailable)
        {
            return resolution.UnavailableReasonCode;
        }

        if (resolution.ProviderMode is WebhookProviderMode.Svix or WebhookProviderMode.Composite &&
            exactBinding is null &&
            (resolution.ProviderCapabilities & capability) == capability)
        {
            return "webhook_provider_binding_unverified";
        }

        return resolution.ProviderMode is WebhookProviderMode.Disabled or WebhookProviderMode.DryRun
            ? "webhook_provider_capability_not_applicable"
            : "webhook_provider_capability_unproven";
    }

    private static bool IsSingleFlag(WebhookProviderCapability capability)
    {
        var value = (long)capability;
        return (value & (value - 1)) == 0;
    }
}
