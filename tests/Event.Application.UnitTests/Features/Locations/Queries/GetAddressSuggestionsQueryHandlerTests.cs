using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Infrastructure.Geocoding;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Geocoding;
using Explore.Application.Features.Geocoding.Handlers.Queries;
using Explore.Application.Features.Geocoding.Requests.Queries;
using Explore.Domain.Enums;
using FluentValidation;
using NSubstitute;

namespace Event.Application.UnitTests.Features.Locations.Queries;

public sealed class GetAddressSuggestionsQueryHandlerTests
{
    private readonly ILocalAddressSuggestionQuery _localQuery =
        Substitute.For<ILocalAddressSuggestionQuery>();
    private readonly ITenantContext _tenantContext = Substitute.For<ITenantContext>();
    private readonly IUserContext _userContext = Substitute.For<IUserContext>();
    private readonly IAddressSuggestionProviderGateway _providerGateway =
        Substitute.For<IAddressSuggestionProviderGateway>();
    private readonly IAddressSelectionProtector _selectionProtector =
        Substitute.For<IAddressSelectionProtector>();

    public GetAddressSuggestionsQueryHandlerTests()
    {
        _providerGateway.SearchAsync(
                Arg.Any<AddressGeocoderRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(AddressGeocoderResult.None);
        _selectionProtector.ConfigurationFingerprint.Returns("configuration-v1");
    }

    [Test]
    public async Task ValidRequest_UsesTrustedContextAndMapsGovernedLocalResults()
    {
        Guid tenantId = Guid.CreateVersion7();
        Guid userId = Guid.CreateVersion7();
        Guid organizationId = Guid.CreateVersion7();
        Guid locationId = Guid.CreateVersion7();
        _tenantContext.TenantId.Returns(tenantId);
        _userContext.GetRequiredUserId().Returns(userId);
        _localQuery.SearchAsync(
                Arg.Any<LocalAddressSuggestionCriteria>(),
                Arg.Any<CancellationToken>())
            .Returns(
            [
                new LocalAddressSuggestion(
                    locationId,
                    Guid.CreateVersion7(),
                    "Community Hall",
                    "Synthetic Street",
                    "0000",
                    LocationAddressSourceEnum.Manual,
                    LocationAddressVisibilityEnum.OrganizationScoped)
            ]);
        var handler = CreateHandler();
        using var cancellation = new CancellationTokenSource();
        var request = new GetAddressSuggestionsQuery(
            tenantId,
            new AddressSuggestionsRequestDto
            {
                SearchText = "  café hall  ",
                Limit = 5,
                OrganizationId = organizationId
            });

        AddressSuggestionsResponseDto response =
            await handler.Handle(request, cancellation.Token);

        await Assert.That(response.ProviderOutcome).IsEqualTo(AddressProviderOutcome.None);
        await Assert.That(response.Suggestions).HasSingleItem();
        AddressSuggestionDto suggestion = response.Suggestions[0];
        await Assert.That(suggestion.LocationId).IsEqualTo(locationId);
        await Assert.That(suggestion.TenantId).IsEqualTo(tenantId);
        await Assert.That(suggestion.DisplayName).IsEqualTo("Community Hall");
        await Assert.That(suggestion.Address).IsEqualTo("Synthetic Street");
        await Assert.That(suggestion.Postcode).IsEqualTo("0000");
        await Assert.That(suggestion.Source).IsEqualTo(LocationAddressSourceEnum.Manual);
        await Assert.That(suggestion.Visibility)
            .IsEqualTo(LocationAddressVisibilityEnum.OrganizationScoped);
        await _localQuery.Received(1).SearchAsync(
            Arg.Is<LocalAddressSuggestionCriteria>(criteria =>
                criteria.TenantId == tenantId
                && criteria.ActorId == userId
                && criteria.UserId == userId
                && criteria.OrganizationId == organizationId
                && criteria.SearchText == "café hall"
                && criteria.Limit == 5),
            cancellation.Token);
    }

    [Test]
    public async Task ProviderResult_IsProtectedForTrustedTargetAndMergedWithoutCoordinates()
    {
        Guid tenantId = Guid.CreateVersion7();
        Guid userId = Guid.CreateVersion7();
        Guid organizationId = Guid.CreateVersion7();
        Guid locationId = Guid.CreateVersion7();
        Guid concurrencyStamp = Guid.CreateVersion7();
        DateTimeOffset expiresAt = new(2026, 8, 26, 12, 5, 0, TimeSpan.Zero);
        _tenantContext.TenantId.Returns(tenantId);
        _userContext.GetRequiredUserId().Returns(userId);
        var selection = new ProtectedAddressSelection
        {
            DisplayName = "Provider Hall",
            Address = "Provider Street 30",
            Postcode = "1000",
            City = "Brussels",
            Country = "BE",
            Timezone = "Europe/Brussels",
            Latitude = 50.8503,
            Longitude = 4.3517,
            Attribution = "Provider attribution",
            Provenance = new ProtectedAddressProvenance
            {
                Provider = "Photon",
                ProviderRecordId = "record-30",
                DatasetVersion = "dataset-v1"
            }
        };
        _providerGateway.SearchAsync(
                Arg.Any<AddressGeocoderRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(new AddressGeocoderResult([selection], AddressProviderOutcome.Ready));
        _selectionProtector.ProtectAsync(
                selection,
                Arg.Any<AddressSelectionContext>(),
                Arg.Any<CancellationToken>())
            .Returns(new AddressSelectionToken("opaque-token", expiresAt));
        var request = new GetAddressSuggestionsQuery(
            tenantId,
            new AddressSuggestionsRequestDto
            {
                SearchText = "provider hall",
                Limit = 3,
                OrganizationId = organizationId,
                LocationId = locationId,
                ExpectedConcurrencyStamp = concurrencyStamp
            });

        AddressSuggestionsResponseDto response =
            await CreateHandler().Handle(request, CancellationToken.None);

        await Assert.That(response.ProviderOutcome).IsEqualTo(AddressProviderOutcome.Ready);
        await Assert.That(response.Suggestions).HasSingleItem();
        AddressSuggestionDto suggestion = response.Suggestions[0];
        await Assert.That(suggestion.LocationId).IsNull();
        await Assert.That(suggestion.Source).IsEqualTo(LocationAddressSourceEnum.ProviderSelection);
        await Assert.That(suggestion.SelectionToken).IsEqualTo("opaque-token");
        await Assert.That(suggestion.SelectionExpiresAt).IsEqualTo(expiresAt);
        await Assert.That(suggestion.Attribution).IsEqualTo("Provider attribution");
        await _providerGateway.Received(1).SearchAsync(
            Arg.Is<AddressGeocoderRequest>(outbound =>
                outbound.SearchText == "provider hall" && outbound.Limit == 3),
            CancellationToken.None);
        await _selectionProtector.Received(1).ProtectAsync(
            selection,
            Arg.Is<AddressSelectionContext>(context =>
                context.TenantId == tenantId
                && context.ActorId == userId
                && context.OrganizationId == organizationId
                && context.Purpose == AddressSelectionPurpose.UpdateLocation
                && context.Target.LocationId == locationId
                && context.Target.ExpectedConcurrencyStamp == concurrencyStamp
                && context.ConfigurationFingerprint == "configuration-v1"),
            CancellationToken.None);
    }

    [Test]
    public async Task SelectionProtectionFailurePreservesProviderOutcome()
    {
        Guid tenantId = Guid.CreateVersion7();
        _tenantContext.TenantId.Returns(tenantId);
        _userContext.GetRequiredUserId().Returns(Guid.CreateVersion7());
        var selection = new ProtectedAddressSelection
        {
            DisplayName = "Provider Hall",
            Address = "Provider Street",
            Postcode = "1000",
            City = "Brussels",
            Country = "Belgium",
            Latitude = 50.8503,
            Longitude = 4.3517,
            Attribution = "Provider attribution",
            Provenance = new ProtectedAddressProvenance
            {
                Provider = "Photon",
                DatasetVersion = "dataset-v1"
            }
        };
        _providerGateway.SearchAsync(
                Arg.Any<AddressGeocoderRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(new AddressGeocoderResult(
                [selection],
                AddressProviderOutcome.Ready));
        _selectionProtector.ProtectAsync(
                selection,
                Arg.Any<AddressSelectionContext>(),
                Arg.Any<CancellationToken>())
            .Returns<AddressSelectionToken>(
                _ => throw new InvalidOperationException("protector unavailable"));

        AddressSuggestionsResponseDto result = await CreateHandler().Handle(
            ValidRequest(tenantId),
            CancellationToken.None);

        await Assert.That(result.ProviderOutcome)
            .IsEqualTo(AddressProviderOutcome.Ready);
        await Assert.That(result.Suggestions).IsEmpty();
    }

    [Test]
    public async Task TenantMismatch_FailsBeforeReadingExactAddressData()
    {
        _tenantContext.TenantId.Returns(Guid.CreateVersion7());
        _userContext.GetRequiredUserId().Returns(Guid.CreateVersion7());
        var handler = CreateHandler();
        GetAddressSuggestionsQuery request = ValidRequest(Guid.CreateVersion7());

        await Assert.ThrowsAsync<ValidationException>(
            () => handler.Handle(request, CancellationToken.None));
        await _localQuery.DidNotReceive().SearchAsync(
            Arg.Any<LocalAddressSuggestionCriteria>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task MissingAuthenticatedUser_FailsBeforeReadingExactAddressData()
    {
        Guid tenantId = Guid.CreateVersion7();
        _tenantContext.TenantId.Returns(tenantId);
        _userContext.GetRequiredUserId().Returns(_ => throw new UnauthorizedAccessException());
        var handler = CreateHandler();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => handler.Handle(ValidRequest(tenantId), CancellationToken.None));
        await _localQuery.DidNotReceive().SearchAsync(
            Arg.Any<LocalAddressSuggestionCriteria>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task InvalidBounds_FailBeforeReadingExactAddressData()
    {
        Guid tenantId = Guid.CreateVersion7();
        _tenantContext.TenantId.Returns(tenantId);
        _userContext.GetRequiredUserId().Returns(Guid.CreateVersion7());
        var handler = CreateHandler();
        var request = new GetAddressSuggestionsQuery(
            tenantId,
            new AddressSuggestionsRequestDto
            {
                SearchText = "x",
                Limit = 21,
                OrganizationId = Guid.Empty
            });

        await Assert.ThrowsAsync<ValidationException>(
            () => handler.Handle(request, CancellationToken.None));
        await _localQuery.DidNotReceive().SearchAsync(
            Arg.Any<LocalAddressSuggestionCriteria>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task PreCancelledRequest_PropagatesWithoutReadingExactAddressData()
    {
        Guid tenantId = Guid.CreateVersion7();
        _tenantContext.TenantId.Returns(tenantId);
        _userContext.GetRequiredUserId().Returns(Guid.CreateVersion7());
        var handler = CreateHandler();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => handler.Handle(ValidRequest(tenantId), cancellation.Token));
        await _localQuery.DidNotReceive().SearchAsync(
            Arg.Any<LocalAddressSuggestionCriteria>(),
            Arg.Any<CancellationToken>());
    }

    private GetAddressSuggestionsQueryHandler CreateHandler() =>
        new(
            _localQuery,
            _providerGateway,
            _selectionProtector,
            _tenantContext,
            _userContext);

    private static GetAddressSuggestionsQuery ValidRequest(Guid tenantId) =>
        new(
            tenantId,
            new AddressSuggestionsRequestDto
            {
                SearchText = "hall",
                Limit = 10
            });
}
