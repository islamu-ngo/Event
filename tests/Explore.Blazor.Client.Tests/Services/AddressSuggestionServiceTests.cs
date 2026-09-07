using System.Reflection;

namespace Explore.Blazor.Client.Tests.Services;

public sealed class AddressSuggestionServiceTests
{
    private readonly IGeocodingClient _geocodingClient = Substitute.For<IGeocodingClient>();
    private readonly ILocationClient _locationClient = Substitute.For<ILocationClient>();
    private readonly ILogger<AddressSuggestionService> _logger =
        Substitute.For<ILogger<AddressSuggestionService>>();

    [Test]
    public async Task GeneratedSearchResponseExposesTypedEmbeddedHalItems()
    {
        Type responseType = typeof(HalResourceOfAddressSuggestionsResponseDto);
        Type? embeddedType = responseType.GetProperty("_embedded")?.PropertyType;

        await Assert.That(embeddedType)
            .IsEqualTo(typeof(HalCollectionEmbeddedOfAddressSuggestionDto));
    }

    [Test]
    public async Task SearchContractReturnsProviderOutcomeAndAcceptsTargetBinding()
    {
        MethodInfo method = typeof(IAddressSuggestionService)
            .GetMethod(nameof(IAddressSuggestionService.SearchAsync))
            ?? throw new InvalidOperationException("SearchAsync is missing.");
        Type resultType = method.ReturnType.GenericTypeArguments.Single();
        string[] parameterNames = method.GetParameters()
            .Select(parameter => parameter.Name ?? string.Empty)
            .ToArray();

        await Assert.That(resultType.Name)
            .IsEqualTo("AddressSuggestionSearchResult");
        await Assert.That(parameterNames).IsEquivalentTo(
            [
                "searchText",
                "organizationId",
                "locationId",
                "expectedConcurrencyStamp",
                "limit",
                "searchLink",
                "cancellationToken"
            ]);
        await Assert.That(resultType.GetProperty("Suggestions")).IsNotNull();
        await Assert.That(resultType.GetProperty("ProviderOutcome")).IsNotNull();
    }

    [Test]
    public async Task SearchAsync_ForwardsBodyAndCancellationThenUnwrapsHalItems()
    {
        using var cancellation = new CancellationTokenSource();
        Guid organizationId = Guid.CreateVersion7();
        Guid locationId = Guid.CreateVersion7();
        Guid concurrencyStamp = Guid.CreateVersion7();
        var searchLink = new HalLink
        {
            Href = "/api/geocoding/address-suggestions",
            Method = "POST"
        };
        var item = new HalResourceOfAddressSuggestionDto
        {
            LocationId = Guid.CreateVersion7(),
            ConcurrencyStamp = Guid.CreateVersion7(),
            DisplayName = "Synthetic Hall",
            Address = "Synthetic Address",
            Postcode = "0000",
            Source = LocationAddressSourceEnum.Manual,
            Visibility = LocationAddressVisibilityEnum.OrganizationScoped
        };
        _geocodingClient.GetAddressSuggestionsAsync(
                Arg.Any<AddressSuggestionsRequestDto>(),
                null,
                null,
                cancellation.Token)
            .Returns(new HalResourceOfAddressSuggestionsResponseDto
            {
                ProviderOutcome = AddressProviderOutcome.Limited,
                _embedded = new HalCollectionEmbeddedOfAddressSuggestionDto
                {
                    Items = [item]
                }
            });
        var service = CreateService();

        AddressSuggestionSearchResult result =
            await service.SearchAsync(
                "community",
                organizationId,
                locationId,
                concurrencyStamp,
                7,
                searchLink,
                cancellation.Token);

        await Assert.That(result.Suggestions).HasSingleItem();
        await Assert.That(result.ProviderOutcome).IsEqualTo(AddressProviderOutcome.Limited);
        HalResourceOfAddressSuggestionDto returned = result.Suggestions.Single();
        ArgumentNullException.ThrowIfNull(returned);
        await Assert.That(returned).IsSameReferenceAs(item);
        await _geocodingClient.Received(1).GetAddressSuggestionsAsync(
            Arg.Is<AddressSuggestionsRequestDto>(request =>
                request.SearchText == "community"
                && request.OrganizationId == organizationId
                && request.LocationId == locationId
                && request.ExpectedConcurrencyStamp == concurrencyStamp
                && request.Limit == 7),
            null,
            null,
            cancellation.Token);
    }

    [Test]
    public async Task SearchAsync_InvalidAdvertisedLinkFailsBeforeApiCall()
    {
        var service = CreateService();
        var invalidLink = new HalLink
        {
            Href = "/api/geocoding/provider-address-suggestions",
            Method = "POST"
        };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SearchAsync(
                "community",
                organizationId: null,
                locationId: null,
                expectedConcurrencyStamp: null,
                limit: 7,
                invalidLink,
                CancellationToken.None));
        await _geocodingClient.DidNotReceive().GetAddressSuggestionsAsync(
            Arg.Any<AddressSuggestionsRequestDto>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ApproveAsync_ValidAdvertisedLinkUsesGeneratedConcurrencyWrite()
    {
        using var cancellation = new CancellationTokenSource();
        HalResourceOfAddressSuggestionDto suggestion = Suggestion();
        var link = new HalLink
        {
            Href = $"https://event.example.test/api/location/{suggestion.LocationId:D}/address-approval",
            Method = "POST"
        };
        _locationClient.ApproveTenantAddressAsync(
                suggestion.LocationId!.Value,
                $"\"{suggestion.ConcurrencyStamp:D}\"",
                null,
                null,
                cancellation.Token)
            .Returns(new BaseCommandResponseOfGuid { Success = true });
        var service = CreateService();

        await service.ApproveAsync(suggestion, link, cancellation.Token);

        await _locationClient.Received(1).ApproveTenantAddressAsync(
            suggestion.LocationId!.Value,
            $"\"{suggestion.ConcurrencyStamp:D}\"",
            null,
            null,
            cancellation.Token);
    }

    [Test]
    [Arguments("GET", "/api/location/{0}/address-approval")]
    [Arguments("POST", "/api/location/{0}/delete")]
    [Arguments("POST", "")]
    public async Task ApproveAsync_InvalidAdvertisedLinkFailsBeforeApiCall(
        string method,
        string hrefTemplate)
    {
        HalResourceOfAddressSuggestionDto suggestion = Suggestion();
        var link = new HalLink
        {
            Href = string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                hrefTemplate,
                suggestion.LocationId!.Value.ToString("D")),
            Method = method
        };
        var service = CreateService();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ApproveAsync(suggestion, link, CancellationToken.None));
        await _locationClient.DidNotReceive().ApproveTenantAddressAsync(
            Arg.Any<Guid>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ApproveAsync_LinkForDifferentLocationFailsBeforeApiCall()
    {
        HalResourceOfAddressSuggestionDto suggestion = Suggestion();
        var link = new HalLink
        {
            Href = $"/api/location/{Guid.CreateVersion7():D}/address-approval",
            Method = "POST"
        };
        var service = CreateService();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ApproveAsync(suggestion, link, CancellationToken.None));
        await _locationClient.DidNotReceive().ApproveTenantAddressAsync(
            Arg.Any<Guid>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    private AddressSuggestionService CreateService() => new(_geocodingClient, _locationClient, _logger);

    private static HalResourceOfAddressSuggestionDto Suggestion() => new()
    {
        LocationId = Guid.CreateVersion7(),
        ConcurrencyStamp = Guid.CreateVersion7(),
        DisplayName = "Synthetic Hall",
        Address = "Synthetic Address",
        Postcode = "0000",
        Source = LocationAddressSourceEnum.Manual,
        Visibility = LocationAddressVisibilityEnum.OrganizationScoped
    };
}
