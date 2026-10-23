using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Services;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Settings.Documents.Payloads;
using Explore.Domain.ValueObjects;
using NSubstitute;

namespace Event.Application.UnitTests.Features.InstanceOnboarding;

public sealed class InstanceOperatorIdentityCommandTests
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    [Test]
    public async Task EvaluateAsync_MissingDocument_ReportsMissingFailureCode()
    {
        var scenario = new InstanceOperatorIdentityServiceScenario();

        InstanceOperatorIdentityReadinessAssessment assessment =
            await scenario.Service.EvaluateAsync(InstanceOperatorIdentityCapability.PaidCommerce);

        await Assert.That(assessment.IsReady).IsFalse();
        await Assert.That(assessment.FailureCode)
            .IsEqualTo(InstanceOperatorIdentityFailureCodes.Missing);
        await Assert.That(assessment.Identity).IsNull();
        await Assert.That(assessment.DocumentRevision).IsNull();
        await Assert.That(assessment.ReasonCodes).IsEmpty();
    }

    [Test]
    public async Task EvaluateAsync_CorruptJson_ReportsIntegrityError()
    {
        var scenario = new InstanceOperatorIdentityServiceScenario()
            .WithStoredSetting("{ this is not json");

        InstanceOperatorIdentityReadinessAssessment assessment =
            await scenario.Service.EvaluateAsync(InstanceOperatorIdentityCapability.PaidCommerce);

        await Assert.That(assessment.IsReady).IsFalse();
        await Assert.That(assessment.FailureCode)
            .IsEqualTo(InstanceOperatorIdentityFailureCodes.IntegrityError);
        await Assert.That(assessment.Identity).IsNull();
    }

    [Test]
    public async Task EvaluateAsync_IncompleteDocument_ReportsBoundedReasonCodesAndRevision()
    {
        Guid storedRevision = Guid.CreateVersion7();
        var scenario = new InstanceOperatorIdentityServiceScenario()
            .WithStoredPayload(new InstanceOperatorIdentitySettings
            {
                PublicName = "Independent Operator",
                Revision = storedRevision
            });

        InstanceOperatorIdentityReadinessAssessment assessment =
            await scenario.Service.EvaluateAsync(InstanceOperatorIdentityCapability.PaidCommerce);

        await Assert.That(assessment.IsReady).IsFalse();
        await Assert.That(assessment.FailureCode)
            .IsEqualTo("instance_operator_identity_incomplete");
        await Assert.That(assessment.Identity).IsNull();
        await Assert.That(assessment.DocumentRevision).IsEqualTo(storedRevision);
        await Assert.That(assessment.ReasonCodes).IsEquivalentTo(
        [
            "instance_operator_identity_operator_id_invalid",
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
    public async Task EvaluateAsync_ReadyDocument_ReturnsValidatedIdentityWithRevision()
    {
        Guid storedRevision = Guid.CreateVersion7();
        var scenario = new InstanceOperatorIdentityServiceScenario()
            .WithStoredPayload(CompleteCandidate() with { Revision = storedRevision });

        InstanceOperatorIdentityReadinessAssessment assessment =
            await scenario.Service.EvaluateAsync(InstanceOperatorIdentityCapability.PaidCommerce);

        await Assert.That(assessment.IsReady).IsTrue();
        await Assert.That(assessment.FailureCode).IsNull();
        await Assert.That(assessment.Identity).IsNotNull();
        await Assert.That(assessment.Identity!.PublicName).IsEqualTo("Independent Operator");
        await Assert.That(assessment.Identity.OperatorId)
            .IsEqualTo(Guid.Parse("0198e2a4-5340-7f89-8abc-b8bdf43e0ea8"));
        await Assert.That(assessment.DocumentRevision).IsEqualTo(storedRevision);
    }

    [Test]
    public async Task EvaluateAsync_DisclosureWithoutTerms_DoesNotAuthorizePaidCommerce()
    {
        var scenario = new InstanceOperatorIdentityServiceScenario()
            .WithStoredPayload(CompleteCandidate() with { TermsUrl = null });

        var disclosure = await scenario.Service.EvaluateAsync(InstanceOperatorIdentityCapability.PublicDisclosure);
        var commerce = await scenario.Service.EvaluateAsync(InstanceOperatorIdentityCapability.PaidCommerce);

        await Assert.That(disclosure.IsReady).IsTrue();
        await Assert.That(disclosure.Identity!.TermsUrl).IsNull();
        await Assert.That(commerce.IsReady).IsFalse();
        await Assert.That(commerce.Identity).IsNull();
        await Assert.That(commerce.ReasonCodes).IsEquivalentTo(["instance_operator_identity_terms_url_missing"]);
    }

    private static InstanceOperatorIdentitySettings CompleteCandidate() => new()
    {
        OperatorId = Guid.Parse("0198e2a4-5340-7f89-8abc-b8bdf43e0ea8"),
        PublicName = "Independent Operator",
        LegalName = "Independent Operator ASBL",
        OperatorKindCode = "registered_organization",
        JurisdictionCountryCode = "BE",
        RegistrationIdentifier = "BE 0123.456.789",
        PublicContactEmail = "contact@example.test",
        WebsiteUrl = "https://example.test",
        LegalNoticeUrl = "https://example.test/legal",
        TermsUrl = "https://example.test/terms",
        PrivacyUrl = "https://example.test/privacy",
        IsOfficialInstance = false,
        OfficialOrigin = "https://event.example.org"
    };

    private sealed class InstanceOperatorIdentityServiceScenario
    {
        public InstanceOperatorIdentityServiceScenario()
        {
            SystemSettings.GetByKey(
                    InstanceOperatorIdentitySettingKeys.OperatorIdentity,
                    Arg.Any<CancellationToken>())
                .Returns((SystemSetting?)null);
            Service = new InstanceOperatorIdentityService(SystemSettings, new ObservationOnlyScopeFactory(), TimeProvider.System);
        }

        public ISystemSettingRepository SystemSettings { get; } = Substitute.For<ISystemSettingRepository>();

        public InstanceOperatorIdentityService Service { get; }

        public InstanceOperatorIdentityServiceScenario WithStoredSetting(string value)
        {
            SystemSettings.GetByKey(
                    InstanceOperatorIdentitySettingKeys.OperatorIdentity,
                    Arg.Any<CancellationToken>())
                .Returns(Stored(value));
            return this;
        }

        public InstanceOperatorIdentityServiceScenario WithStoredPayload(
            InstanceOperatorIdentitySettings payload) =>
            WithStoredSetting(JsonSerializer.Serialize(payload, SerializerOptions));

        private static SystemSetting Stored(string value) => new()
        {
            Id = Guid.CreateVersion7(),
            SettingKey = InstanceOperatorIdentitySettingKeys.OperatorIdentity,
            Value = value,
            ValueType = SettingValueType.Json
        };

    }

    private sealed class ObservationOnlyScopeFactory : Explore.Application.Contracts.Operations.IPolicyMutationExecutionScopeFactory
    {
        public ValueTask<Explore.Application.Contracts.Operations.IPolicyMutationExecutionScope> CreateAsync(
            Explore.Application.Contracts.Operations.PolicyMutationScope selection, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("An observation fixture cannot acquire mutation authority.");
    }
}
