using Explore.Application.Contracts.Webhooks;
using Explore.Application.DTOs.Webhooks;

namespace Event.Application.UnitTests.Features.Webhooks;

public sealed class OpenSvixPortalRequestAuthorityTests
{
    private static readonly string[] ForbiddenAuthorityProperties =
    [
        "ReadOnly",
        "FeatureFlags",
        "ProviderApplicationId",
        "ExternalApplicationId",
        "ApplicationUid",
        "Capabilities",
        "CapabilityFlags",
        "ProviderBindingId",
        "BindingId",
        "TenantId"
    ];

    [Test]
    public async Task RequestDto_ContainsOnlyConsumerAndExpiryIntent()
    {
        var properties = typeof(OpenSvixAppPortalRequestDto)
            .GetProperties()
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        await Assert.That(properties).IsEquivalentTo([
            nameof(OpenSvixAppPortalRequestDto.ConsumerId),
            nameof(OpenSvixAppPortalRequestDto.ExpiresInSeconds)
        ]);
        await Assert.That(properties.Intersect(ForbiddenAuthorityProperties, StringComparer.OrdinalIgnoreCase))
            .IsEmpty();
    }

    [Test]
    public async Task ProviderServiceInput_ContainsNoCallerSelectedProviderAuthority()
    {
        var properties = typeof(WebhookProviderPortalAccessInput)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();
        var providerAuthorityProperties = ForbiddenAuthorityProperties
            .Where(property => !string.Equals(property, "TenantId", StringComparison.Ordinal))
            .ToArray();

        await Assert.That(properties.Intersect(providerAuthorityProperties, StringComparer.OrdinalIgnoreCase))
            .IsEmpty();
    }
}
