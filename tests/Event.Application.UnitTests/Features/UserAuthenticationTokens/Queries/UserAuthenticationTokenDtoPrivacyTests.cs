using Explore.Application.DTOs.UserAuthenticationToken;
using TUnit.Core;

namespace Event.Application.UnitTests.Features.UserAuthenticationTokens.Queries;

public class UserAuthenticationTokenDtoPrivacyTests
{
    [Test]
    public async Task DetailDto_ExposesOnlySafeSessionMetadata()
    {
        var propertyNames = typeof(UserAuthenticationTokenDto)
            .GetProperties()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);

        await Assert.That(propertyNames)
            .IsEquivalentTo(["Id", "Provider", "PdsHost", "ExpiresAt"]);
    }

    [Test]
    public async Task ListDto_ExposesOnlySafeSessionMetadata()
    {
        var propertyNames = typeof(UserAuthenticationTokenListDto)
            .GetProperties()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);

        await Assert.That(propertyNames)
            .IsEquivalentTo(["Id", "Provider", "PdsHost", "ExpiresAt"]);
    }
}
