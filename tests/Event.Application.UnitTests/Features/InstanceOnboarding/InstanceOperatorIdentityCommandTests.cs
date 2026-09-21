using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Exceptions;
using Explore.Application.Responses;
using Explore.Application.Services;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Enums;
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
    public async Task SaveAsync_PendingBootstrap_SavesNormalizedDraftWithFreshServerControlledFields()
    {
        var scenario = new InstanceOperatorIdentityServiceScenario();

        BaseCommandResponse<InstanceOperatorIdentitySavedDocument> response = await scenario.Service.SaveAsync(
            new InstanceOperatorIdentitySettings { PublicName = "  Independent Operator  " },
            expectedRevision: null);

        await Assert.That(response.IsSuccess).IsTrue();
        await Assert.That(scenario.Upserted).IsNotNull();
        await Assert.That(scenario.Upserted!.SettingKey)
            .IsEqualTo(InstanceOperatorIdentitySettingKeys.OperatorIdentity);
        InstanceOperatorIdentitySettings saved = Parse(scenario.Upserted.Value);
        await Assert.That(saved.PublicName).IsEqualTo("Independent Operator");
        await Assert.That(saved.OperatorId is { } operatorId && operatorId.Version == 7).IsTrue();
        await Assert.That(saved.IsOfficialInstance).IsFalse();
        await Assert.That(saved.Revision is { } revision && revision.Version == 7).IsTrue();
        await Assert.That(response.Id!.PublicDisclosure.IsReady).IsFalse();
        await Assert.That(response.Id.PaidCommerce.IsReady).IsFalse();
        await Assert.That(response.Id.PaidCommerce.FailureCode)
            .IsEqualTo("instance_operator_identity_incomplete");
    }

    [Test]
    public async Task SaveAsync_StaleRevision_ThrowsConcurrencyConflict()
    {
        Guid storedRevision = Guid.CreateVersion7();
        var staleScenario = new InstanceOperatorIdentityServiceScenario()
            .WithStoredPayload(CompleteCandidate() with { Revision = storedRevision });
        var missingScenario = new InstanceOperatorIdentityServiceScenario();

        _ = await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
            staleScenario.Service.SaveAsync(CompleteCandidate(), expectedRevision: Guid.CreateVersion7()));
        _ = await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
            missingScenario.Service.SaveAsync(CompleteCandidate(), expectedRevision: Guid.CreateVersion7()));
        await Assert.That(staleScenario.Upserted).IsNull();
        await Assert.That(missingScenario.Upserted).IsNull();
    }

    [Test]
    public async Task SaveAsync_CompletedBootstrap_PersistsValidIncompleteDraftWithoutGrantingReadiness()
    {
        Guid storedRevision = Guid.CreateVersion7();
        var scenario = new InstanceOperatorIdentityServiceScenario()
            .WithCompletedBootstrap()
            .WithStoredPayload(CompleteCandidate() with { Revision = storedRevision });

        BaseCommandResponse<InstanceOperatorIdentitySavedDocument> response = await scenario.Service.SaveAsync(
            new InstanceOperatorIdentitySettings { PublicName = "Independent Operator" },
            expectedRevision: storedRevision);

        await Assert.That(response.IsSuccess).IsTrue();
        await Assert.That(scenario.Upserted).IsNotNull();
        await Assert.That(response.Id!.PublicDisclosure.IsReady).IsFalse();
        await Assert.That(response.Id.PaidCommerce.IsReady).IsFalse();
        await Assert.That(response.Id.PaidCommerce.ReasonCodes)
            .Contains("instance_operator_identity_terms_url_missing");
        await Assert.That(response.Id.Revision).IsNotEqualTo(storedRevision);
    }

    [Test]
    public async Task SaveAsync_CompletedBootstrap_AcceptsValidReplacementWithRotatedRevision()
    {
        Guid storedRevision = Guid.CreateVersion7();
        var scenario = new InstanceOperatorIdentityServiceScenario()
            .WithCompletedBootstrap()
            .WithStoredPayload(CompleteCandidate() with { Revision = storedRevision });

        BaseCommandResponse<InstanceOperatorIdentitySavedDocument> response = await scenario.Service.SaveAsync(
            CompleteCandidate(),
            expectedRevision: storedRevision);

        await Assert.That(response.IsSuccess).IsTrue();
        InstanceOperatorIdentitySettings saved = Parse(scenario.Upserted!.Value);
        await Assert.That(saved.Revision).IsNotEqualTo(storedRevision);
        await Assert.That(response.Id!.Revision).IsEqualTo(saved.Revision);
        await Assert.That(response.Id.PublicDisclosure.IsReady).IsTrue();
        await Assert.That(response.Id.PaidCommerce.IsReady).IsTrue();
    }

    [Test]
    public async Task SaveAsync_RejectsClientAssertionsOfServerControlledFields()
    {
        Guid storedOperatorId = Guid.Parse("0198e2a4-5340-7f89-8abc-b8bdf43e0ea8");
        Guid storedRevision = Guid.CreateVersion7();
        var scenario = new InstanceOperatorIdentityServiceScenario()
            .WithStoredPayload(CompleteCandidate() with
            {
                OperatorId = storedOperatorId,
                IsOfficialInstance = true,
                Revision = storedRevision
            });

        BaseCommandResponse<InstanceOperatorIdentitySavedDocument> response = await scenario.Service.SaveAsync(
            CompleteCandidate() with
            {
                OperatorId = Guid.CreateVersion7(),
                IsOfficialInstance = false
            },
            expectedRevision: storedRevision);

        await Assert.That(response.IsSuccess).IsTrue();
        InstanceOperatorIdentitySettings saved = Parse(scenario.Upserted!.Value);
        await Assert.That(saved.OperatorId).IsEqualTo(storedOperatorId);
        await Assert.That(saved.IsOfficialInstance).IsTrue();
        await Assert.That(saved.Revision).IsNotEqualTo(storedRevision);
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

    private static InstanceOperatorIdentitySettings Parse(string json) =>
        JsonSerializer.Deserialize<InstanceOperatorIdentitySettings>(json, SerializerOptions)!;

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
            BootstrapStates.GetCurrent(Arg.Any<CancellationToken>())
                .Returns(PendingBootstrap());
            SystemSettings.GetByKey(
                    InstanceOperatorIdentitySettingKeys.OperatorIdentity,
                    Arg.Any<CancellationToken>())
                .Returns((SystemSetting?)null);
            SystemSettings.UpsertInCurrentTransactionAsync(
                    Arg.Do<SystemSetting>(setting => Upserted = setting),
                    Arg.Any<CancellationToken>())
                .Returns((string?)null);
            UnitOfWork.ExecuteSerializableAsync(
                    Arg.Any<Func<CancellationToken, Task<BaseCommandResponse<InstanceOperatorIdentitySavedDocument>>>>(),
                    Arg.Any<CancellationToken>())
                .Returns(callInfo => callInfo.Arg<Func<CancellationToken, Task<BaseCommandResponse<InstanceOperatorIdentitySavedDocument>>>>()(
                    CancellationToken.None));

            Service = new InstanceOperatorIdentityService(SystemSettings, UnitOfWork);
        }

        public ISystemSettingRepository SystemSettings { get; } = Substitute.For<ISystemSettingRepository>();

        public IInstanceBootstrapStateRepository BootstrapStates { get; } =
            Substitute.For<IInstanceBootstrapStateRepository>();

        public IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();

        public InstanceOperatorIdentityService Service { get; }

        public SystemSetting? Upserted { get; private set; }

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

        public InstanceOperatorIdentityServiceScenario WithCompletedBootstrap()
        {
            BootstrapStates.GetCurrent(Arg.Any<CancellationToken>())
                .Returns(CompletedBootstrap());
            return this;
        }

        private static SystemSetting Stored(string value) => new()
        {
            Id = Guid.CreateVersion7(),
            SettingKey = InstanceOperatorIdentitySettingKeys.OperatorIdentity,
            Value = value,
            ValueType = SettingValueType.Json
        };

        private static InstanceBootstrapState PendingBootstrap() =>
            InstanceBootstrapState.CreateInteractivePending(
                Guid.CreateVersion7(),
                DeploymentMode.MultiTenant,
                DateTime.UtcNow);

        private static InstanceBootstrapState CompletedBootstrap()
        {
            InstanceBootstrapState bootstrap = PendingBootstrap();
            _ = bootstrap.CompleteInteractive(Guid.CreateVersion7(), DateTime.UtcNow);
            return bootstrap;
        }
    }
}
