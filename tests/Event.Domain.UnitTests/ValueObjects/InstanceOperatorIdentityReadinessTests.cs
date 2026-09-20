using Explore.Domain.Settings.Documents.Payloads;
using Explore.Domain.ValueObjects;

namespace Event.Domain.UnitTests.ValueObjects;

public sealed class InstanceOperatorIdentityReadinessTests
{
    [Test]
    public async Task Evaluate_CompleteSettings_ReturnsReadyWithNormalizedPayload()
    {
        InstanceOperatorIdentitySettings settings = Complete();

        InstanceOperatorIdentityReadiness readiness = InstanceOperatorIdentityReadiness.Evaluate(settings, InstanceOperatorIdentityCapability.PaidCommerce);

        await Assert.That(readiness.IsReady).IsTrue();
        await Assert.That(readiness.FailureCode).IsNull();
        await Assert.That(readiness.ReasonCodes).IsEmpty();
        await Assert.That(readiness.Normalized).IsNotNull();
        await Assert.That(readiness.Normalized!.OperatorId).IsEqualTo(settings.OperatorId);
        await Assert.That(readiness.Normalized.PublicName).IsEqualTo("Independent Operator");
        await Assert.That(readiness.Normalized.OperatorKindCode).IsEqualTo("registered_organization");
        await Assert.That(readiness.Normalized.JurisdictionCountryCode).IsEqualTo("BE");
        await Assert.That(readiness.Normalized.PublicContactEmail).IsEqualTo("contact@example.test");
        await Assert.That(readiness.Normalized.OfficialOrigin).IsEqualTo("https://event.example.org");
        await Assert.That(readiness.Normalized.WebsiteUrl).IsEqualTo("https://example.test/");
        await Assert.That(readiness.Normalized.LegalNoticeUrl).IsEqualTo("https://example.test/legal");
        await Assert.That(readiness.Normalized.TermsUrl).IsEqualTo("https://example.test/terms");
        await Assert.That(readiness.Normalized.PrivacyUrl).IsEqualTo("https://example.test/privacy");
        await Assert.That(readiness.Normalized.IsOfficialInstance).IsFalse();
        await Assert.That(readiness.Normalized.Revision).IsEqualTo(settings.Revision);
    }

    [Test]
    public async Task Evaluate_EmptySettings_ReturnsIncompleteWithAllRequiredReasonCodes()
    {
        InstanceOperatorIdentityReadiness readiness =
            InstanceOperatorIdentityReadiness.Evaluate(new InstanceOperatorIdentitySettings(), InstanceOperatorIdentityCapability.PaidCommerce);

        await Assert.That(readiness.IsReady).IsFalse();
        await Assert.That(readiness.FailureCode)
            .IsEqualTo("instance_operator_identity_incomplete");
        await Assert.That(readiness.Normalized).IsNull();
        await Assert.That(readiness.ReasonCodes).IsEquivalentTo(
        [
            "instance_operator_identity_operator_id_invalid",
            "instance_operator_identity_public_name_missing",
            "instance_operator_identity_legal_name_missing",
            "instance_operator_identity_operator_kind_missing",
            "instance_operator_identity_jurisdiction_country_missing",
            "instance_operator_identity_public_contact_email_missing",
            "instance_operator_identity_legal_notice_url_missing",
            "instance_operator_identity_terms_url_missing",
            "instance_operator_identity_privacy_url_missing",
            "instance_operator_identity_official_origin_missing",
            "instance_operator_identity_website_url_missing"
        ]);
    }

    [Test]
    public async Task Evaluate_MalformedFields_ReturnBoundedInvalidReasonCodes()
    {
        InstanceOperatorIdentitySettings settings = new()
        {
            OperatorId = Guid.NewGuid(),
            PublicName = "Independent Operator",
            LegalName = "Independent Operator ASBL",
            OperatorKindCode = "cooperative",
            JurisdictionCountryCode = "BEL",
            PublicContactEmail = "not-an-email",
            WebsiteUrl = "ftp://example.test",
            LegalNoticeUrl = "http://example.test/legal",
            TermsUrl = "javascript:alert(1)",
            PrivacyUrl = "https://example.test/privacy",
            OfficialOrigin = "https://example.test/origin"
        };

        InstanceOperatorIdentityReadiness readiness = InstanceOperatorIdentityReadiness.Evaluate(settings, InstanceOperatorIdentityCapability.PaidCommerce);

        await Assert.That(readiness.IsReady).IsFalse();
        await Assert.That(readiness.FailureCode)
            .IsEqualTo("instance_operator_identity_incomplete");
        await Assert.That(readiness.ReasonCodes).IsEquivalentTo(
        [
            "instance_operator_identity_operator_id_invalid",
            "instance_operator_identity_operator_kind_invalid",
            "instance_operator_identity_jurisdiction_country_invalid",
            "instance_operator_identity_public_contact_email_invalid",
            "instance_operator_identity_legal_notice_url_invalid",
            "instance_operator_identity_terms_url_invalid",
            "instance_operator_identity_website_url_invalid",
            "instance_operator_identity_official_origin_invalid"
        ]);
    }

    [Test]
    public async Task ValidateDraft_EmptySettings_IsValidEmptyDraft()
    {
        InstanceOperatorIdentityDraftValidation draft =
            InstanceOperatorIdentityReadiness.ValidateDraft(new InstanceOperatorIdentitySettings());

        await Assert.That(draft.IsValid).IsTrue();
        await Assert.That(draft.ReasonCodes).IsEmpty();
        await Assert.That(draft.Normalized).IsNotNull();
    }

    [Test]
    public async Task ValidateDraft_MalformedValue_RejectsOnlyInvalidFieldsAndNormalizesTheRest()
    {
        InstanceOperatorIdentitySettings settings = new()
        {
            PublicName = "Independent Operator",
            PublicContactEmail = "not-an-email",
            OfficialOrigin = "https://Event.Example.org:443/",
            WebsiteUrl = "  https://Example.Test  "
        };

        InstanceOperatorIdentityDraftValidation draft =
            InstanceOperatorIdentityReadiness.ValidateDraft(settings);

        await Assert.That(draft.IsValid).IsFalse();
        await Assert.That(draft.ReasonCodes)
            .IsEquivalentTo(["instance_operator_identity_public_contact_email_invalid"]);
        await Assert.That(draft.Normalized.PublicName).IsEqualTo("Independent Operator");
        await Assert.That(draft.Normalized.OfficialOrigin).IsEqualTo("https://event.example.org");
        await Assert.That(draft.Normalized.WebsiteUrl).IsEqualTo("https://example.test/");
    }

    [Test]
    public async Task PublicDisclosure_WithoutCommercialTerms_IsReady()
    {
        var readiness = InstanceOperatorIdentityReadiness.Evaluate(
            Complete() with { TermsUrl = null }, InstanceOperatorIdentityCapability.PublicDisclosure);

        await Assert.That(readiness.IsReady).IsTrue();
        await Assert.That(readiness.ReasonCodes).IsEmpty();
        await Assert.That(readiness.Normalized!.TermsUrl).IsNull();
    }

    [Test]
    public async Task PaidCommerce_WithoutCommercialTerms_IsNotReady()
    {
        var readiness = InstanceOperatorIdentityReadiness.Evaluate(
            Complete() with { TermsUrl = null }, InstanceOperatorIdentityCapability.PaidCommerce);

        await Assert.That(readiness.IsReady).IsFalse();
        await Assert.That(readiness.Normalized).IsNull();
        await Assert.That(readiness.ReasonCodes).IsEquivalentTo(["instance_operator_identity_terms_url_missing"]);
    }

    [Test]
    public async Task ValidateDraft_WellFormedIncompleteIdentity_IsValidButCannotDiscloseOrSell()
    {
        var settings = new InstanceOperatorIdentitySettings { PublicName = "  Independent Operator  " };
        var draft = InstanceOperatorIdentityReadiness.ValidateDraft(settings);

        await Assert.That(draft.IsValid).IsTrue();
        await Assert.That(draft.Normalized.PublicName).IsEqualTo("Independent Operator");
        foreach (var capability in Enum.GetValues<InstanceOperatorIdentityCapability>())
        {
            var readiness = InstanceOperatorIdentityReadiness.Evaluate(draft.Normalized, capability);
            await Assert.That(readiness.IsReady).IsFalse();
            await Assert.That(readiness.Normalized).IsNull();
            await Assert.That(readiness.ReasonCodes).Contains("instance_operator_identity_legal_name_missing");
        }
    }

    [Test]
    public async Task Evaluate_WithoutRegistrationIdentifier_IsReadyForBothCapabilities()
    {
        foreach (var capability in Enum.GetValues<InstanceOperatorIdentityCapability>())
        {
            var readiness = InstanceOperatorIdentityReadiness.Evaluate(
                Complete() with { RegistrationIdentifier = null }, capability);
            await Assert.That(readiness.IsReady).IsTrue();
            await Assert.That(readiness.ReasonCodes).IsEmpty();
        }
    }

    [Test]
    public async Task PublicDisclosure_MalformedOptionalTerms_FailsClosed()
    {
        var readiness = InstanceOperatorIdentityReadiness.Evaluate(
            Complete() with { TermsUrl = "http://example.test/terms" }, InstanceOperatorIdentityCapability.PublicDisclosure);

        await Assert.That(readiness.IsReady).IsFalse();
        await Assert.That(readiness.ReasonCodes).IsEquivalentTo(["instance_operator_identity_terms_url_invalid"]);
    }

    [Test]
    public async Task Evaluate_UnsupportedCapability_IsRejected()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Task.FromResult(
            InstanceOperatorIdentityReadiness.Evaluate(Complete(), (InstanceOperatorIdentityCapability)99)));
    }

    private static InstanceOperatorIdentitySettings Complete() => new()
    {
        OperatorId = Guid.Parse("0198e2a4-5340-7f89-8abc-b8bdf43e0ea8"),
        PublicName = "  Independent Operator  ",
        LegalName = "Independent Operator ASBL",
        OperatorKindCode = "REGISTERED_ORGANIZATION",
        JurisdictionCountryCode = "be",
        RegistrationIdentifier = "BE 0123.456.789",
        PublicContactEmail = "Contact@Example.test",
        WebsiteUrl = "https://Example.Test:443",
        LegalNoticeUrl = "https://example.test/legal",
        TermsUrl = "https://example.test/terms",
        PrivacyUrl = "https://example.test/privacy",
        IsOfficialInstance = false,
        OfficialOrigin = "https://Event.Example.org:443/",
        Revision = Guid.CreateVersion7()
    };
}
