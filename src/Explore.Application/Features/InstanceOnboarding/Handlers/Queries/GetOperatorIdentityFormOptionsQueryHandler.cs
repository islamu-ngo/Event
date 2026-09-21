using System.Collections.Immutable;
using System.Globalization;
using Explore.Domain.ValueObjects;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Features.InstanceOnboarding.Requests.Queries;

namespace Explore.Application.Features.InstanceOnboarding.Handlers.Queries;

public sealed class GetOperatorIdentityFormOptionsQueryHandler
    : IQueryHandler<GetOperatorIdentityFormOptionsQuery, OperatorIdentityFormOptionsDto>
{
    public Task<OperatorIdentityFormOptionsDto> QueryAsync(
        GetOperatorIdentityFormOptionsQuery request, CancellationToken cancellationToken) =>
        Task.FromResult(Create(CultureInfo.GetCultures(CultureTypes.SpecificCultures)));

    internal static OperatorIdentityFormOptionsDto Create(IEnumerable<CultureInfo> cultures)
    {
        ImmutableArray<OperatorIdentityCountryOptionDto> countries;
        try
        {
            countries = cultures.Select(culture => new RegionInfo(culture.Name))
                .Where(region => region.TwoLetterISORegionName is { Length: 2 } code
                    && code.All(character => character is >= 'A' and <= 'Z'))
                .DistinctBy(region => region.TwoLetterISORegionName)
                .OrderBy(region => region.TwoLetterISORegionName, StringComparer.Ordinal)
                .Select(region => new OperatorIdentityCountryOptionDto(region.TwoLetterISORegionName, region.DisplayName))
                .ToImmutableArray();
        }
        catch (ArgumentException)
        {
            // Invariant/globalization-limited hosts must advertise unavailability, never a partial selector.
            countries = [];
        }

        return new OperatorIdentityFormOptionsDto
        {
            OperatorKinds = TenantDirectoryOperatorKinds.All.Order(StringComparer.Ordinal)
                .Select(code => new OperatorIdentityKindOptionDto(code, $"operator-identity-kind-{code}-label"))
                .ToImmutableArray(),
            Countries = countries,
            CountryState = countries.IsEmpty ? "Unavailable" : "Available",
            CountryFailureCode = countries.IsEmpty ? "operator_identity_countries_unavailable" : null,
            Fields =
            [
                Field("publicName", "plain-text", TenantDirectoryOperatorIdentity.MaxPublicNameLength),
                Field("legalName", "plain-text", TenantDirectoryOperatorIdentity.MaxLegalNameLength),
                Field("operatorKindCode", "operator-kind", null),
                Field("jurisdictionCountryCode", "iso-alpha-2", 2),
                Field("registrationIdentifier", "plain-text", TenantDirectoryOperatorIdentity.MaxRegistrationIdentifierLength, false, false),
                Field("publicContactEmail", "email", TenantDirectoryOperatorIdentity.MaxPublicContactEmailLength),
                Field("legalNoticeUrl", "https-url", TenantDirectoryOperatorIdentity.MaxLegalUrlLength),
                Field("termsUrl", "https-url", TenantDirectoryOperatorIdentity.MaxLegalUrlLength, false),
                Field("privacyUrl", "https-url", TenantDirectoryOperatorIdentity.MaxLegalUrlLength)
            ]
        };
    }

    private static OperatorIdentityFieldConstraintDto Field(
        string name, string format, int? maxLength, bool disclosure = true, bool paidCommerce = true) =>
        new(name, $"operator-identity-{name}-label", $"operator-identity-{name}-help",
            format, maxLength, disclosure, paidCommerce);
}
