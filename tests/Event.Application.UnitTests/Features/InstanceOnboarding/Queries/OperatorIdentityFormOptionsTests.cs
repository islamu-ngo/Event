using System.Globalization;
using Explore.Application.Features.InstanceOnboarding.Handlers.Queries;
using Explore.Application.Features.InstanceOnboarding.Requests.Queries;
using Explore.Domain.ValueObjects;

namespace Event.Application.UnitTests.Features.InstanceOnboarding.Queries;

public sealed class OperatorIdentityFormOptionsTests
{
    [Test]
    public async Task Query_projects_domain_vocabulary_and_typed_limits()
    {
        var result = await new GetOperatorIdentityFormOptionsQueryHandler()
            .QueryAsync(new GetOperatorIdentityFormOptionsQuery(), CancellationToken.None);
        await Assert.That(result.OperatorKinds.Select(kind => kind.Code).ToHashSet()
            .SetEquals(TenantDirectoryOperatorKinds.All)).IsTrue();
        var fields = result.Fields.ToDictionary(field => field.Name);
        await Assert.That(fields["publicName"].MaxLength).IsEqualTo(TenantDirectoryOperatorIdentity.MaxPublicNameLength);
        await Assert.That(fields["legalName"].MaxLength).IsEqualTo(TenantDirectoryOperatorIdentity.MaxLegalNameLength);
        await Assert.That(fields["registrationIdentifier"].MaxLength).IsEqualTo(TenantDirectoryOperatorIdentity.MaxRegistrationIdentifierLength);
        await Assert.That(fields["publicContactEmail"].MaxLength).IsEqualTo(TenantDirectoryOperatorIdentity.MaxPublicContactEmailLength);
        await Assert.That(fields["legalNoticeUrl"].MaxLength).IsEqualTo(TenantDirectoryOperatorIdentity.MaxLegalUrlLength);
        await Assert.That(fields["jurisdictionCountryCode"].Format).IsEqualTo("iso-alpha-2");
        await Assert.That(fields["jurisdictionCountryCode"].MaxLength).IsEqualTo(2);
        await Assert.That(result.Fields.SelectMany(field => new[] { field.LabelId, field.HelpId }).Distinct().Count())
            .IsEqualTo(result.Fields.Count * 2);
    }

    [Test]
    public async Task Country_projection_deduplicates_regions_and_uses_runtime_display_names()
    {
        var result = GetOperatorIdentityFormOptionsQueryHandler.Create(
            [CultureInfo.GetCultureInfo("en-GB"), CultureInfo.GetCultureInfo("cy-GB"), CultureInfo.GetCultureInfo("en-US")]);
        await Assert.That(result.CountryState).IsEqualTo("Available");
        await Assert.That(result.CountryFailureCode).IsNull();
        await Assert.That(result.Countries.Select(country => country.Code).SequenceEqual(["GB", "US"])).IsTrue();
        await Assert.That(result.Countries.Single(country => country.Code == "GB").DisplayName)
            .IsEqualTo(new RegionInfo("en-GB").DisplayName);
    }

    [Test]
    public async Task Missing_globalization_data_is_explicit_and_preserves_other_metadata()
    {
        var result = GetOperatorIdentityFormOptionsQueryHandler.Create([]);
        await Assert.That(result.CountryState).IsEqualTo("Unavailable");
        await Assert.That(result.CountryFailureCode).IsEqualTo("operator_identity_countries_unavailable");
        await Assert.That(result.Countries).IsEmpty();
        await Assert.That(result.OperatorKinds.Count).IsEqualTo(TenantDirectoryOperatorKinds.All.Count);
        await Assert.That(result.Fields.Count > 0).IsTrue();
    }

    [Test]
    public async Task Invalid_region_data_is_unavailable_not_a_partial_selector()
    {
        var result = GetOperatorIdentityFormOptionsQueryHandler.Create([CultureInfo.InvariantCulture]);
        await Assert.That(result.CountryState).IsEqualTo("Unavailable");
        await Assert.That(result.CountryFailureCode).IsEqualTo("operator_identity_countries_unavailable");
        await Assert.That(result.Countries).IsEmpty();
    }

    [Test]
    public async Task Registration_is_optional_and_authority_choices_are_explicitly_unsupported()
    {
        var result = await new GetOperatorIdentityFormOptionsQueryHandler()
            .QueryAsync(new GetOperatorIdentityFormOptionsQuery(), CancellationToken.None);
        await Assert.That(result.RegistrationAuthorityState).IsEqualTo("NotSupported");
        await Assert.That(result.RegistrationAuthorities).IsEmpty();
        var registration = result.Fields.Single(field => field.Name == "registrationIdentifier");
        await Assert.That(registration.RequiredForDisclosure).IsFalse();
        await Assert.That(registration.RequiredForPaidCommerce).IsFalse();
        var terms = result.Fields.Single(field => field.Name == "termsUrl");
        await Assert.That(terms.RequiredForDisclosure).IsFalse();
        await Assert.That(terms.RequiredForPaidCommerce).IsTrue();
    }
}
