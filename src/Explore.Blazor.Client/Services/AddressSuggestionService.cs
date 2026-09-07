using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Services;

public interface IAddressSuggestionService
{
    Task<AddressSuggestionSearchResult> SearchAsync(
        string searchText,
        Guid? organizationId,
        Guid? locationId,
        Guid? expectedConcurrencyStamp,
        int limit,
        HalLink searchLink,
        CancellationToken cancellationToken);

    Task ApproveAsync(
        HalResourceOfAddressSuggestionDto suggestion,
        HalLink link,
        CancellationToken cancellationToken);
}

public sealed record AddressSuggestionSearchResult
{
    public IReadOnlyList<HalResourceOfAddressSuggestionDto> Suggestions { get; init; } = [];
    public AddressProviderOutcome ProviderOutcome { get; init; }
}

public sealed class AddressSuggestionService(
    IGeocodingClient geocodingClient,
    ILocationClient locationClient,
    ILogger<AddressSuggestionService> logger)
    : IAddressSuggestionService
{
    public async Task<AddressSuggestionSearchResult> SearchAsync(
        string searchText,
        Guid? organizationId,
        Guid? locationId,
        Guid? expectedConcurrencyStamp,
        int limit,
        HalLink searchLink,
        CancellationToken cancellationToken)
    {
        RequireLink(
            searchLink,
            "POST",
            "/api/geocoding/address-suggestions",
            "address suggestion");

        try
        {
            HalResourceOfAddressSuggestionsResponseDto response =
                await geocodingClient.GetAddressSuggestionsAsync(
                    new AddressSuggestionsRequestDto
                    {
                        SearchText = searchText,
                        Limit = limit,
                        OrganizationId = organizationId,
                        LocationId = locationId,
                        ExpectedConcurrencyStamp = expectedConcurrencyStamp
                    },
                    cancellationToken: cancellationToken);

            return new AddressSuggestionSearchResult
            {
                Suggestions = response._embedded?.Items?.ToArray() ?? [],
                ProviderOutcome = response.ProviderOutcome
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ApiException exception)
        {
            logger.LogWarning(
                "Address suggestion request failed with status {StatusCode}.",
                exception.StatusCode);
            throw;
        }
    }

    public async Task ApproveAsync(
        HalResourceOfAddressSuggestionDto suggestion,
        HalLink link,
        CancellationToken cancellationToken)
    {
        if (suggestion.LocationId is not { } locationId || locationId == Guid.Empty)
        {
            throw new InvalidOperationException(
                "The advertised address approval capability is invalid.");
        }

        RequireLink(
            link,
            "POST",
            $"/api/location/{locationId:D}/address-approval",
            "address approval");

        await locationClient.ApproveTenantAddressAsync(
            locationId,
            $"\"{suggestion.ConcurrencyStamp:D}\"",
            cancellationToken: cancellationToken);
    }

    private static void RequireLink(
        HalLink link,
        string method,
        string expectedPath,
        string capability)
    {
        if (!string.Equals(link.Method, method, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(
                LinkPath(link.Href),
                expectedPath,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The advertised {capability} capability is invalid.");
        }
    }

    private static string? LinkPath(string? href)
    {
        if (string.IsNullOrWhiteSpace(href))
        {
            return null;
        }

        return Uri.TryCreate(href, UriKind.Absolute, out Uri? absolute)
            ? absolute.AbsolutePath
            : href.Split(['?', '#'], 2)[0];
    }
}
