using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;

namespace Event.Domain.UnitTests.Entities;

public sealed class RegistrationProviderFoundationTests
{
    private static readonly DateTime Now = new(2026, 8, 9, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task SecretBinding_DefaultQualifierPreservesUnqualifiedIdentity()
    {
        SecretBinding binding = SecretBinding.CreateEnvironmentVariable(
            SecretDefinitionRegistry.Keys.Smtp.Password,
            SecretScope.Tenant,
            Guid.CreateVersion7(),
            "SMTP_PASSWORD");

        await Assert.That(binding.Qualifier).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task SecretBinding_ProviderQualifierIsTrimmedAndBounded()
    {
        Guid tenantId = Guid.CreateVersion7();
        SecretBinding binding = SecretBinding.CreateEnvironmentVariable(
            SecretDefinitionRegistry.Keys.RegistrationProviders.ApiToken,
            SecretScope.Tenant,
            tenantId,
            "REGISTRATION_PROVIDER_API_TOKEN",
            qualifier: " connection-a ");

        await Assert.That(binding.Qualifier).IsEqualTo("connection-a");
        await Assert.That(() => SecretBinding.CreateEnvironmentVariable(
                SecretDefinitionRegistry.Keys.RegistrationProviders.ApiToken,
                SecretScope.Tenant,
                tenantId,
                "REGISTRATION_PROVIDER_API_TOKEN",
                qualifier: new string('x', 129)))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task RegistrationChannel_NativeAndProviderShapesAreExclusive()
    {
        RegistrationRequirement requirement = Requirement();
        await Assert.That(RegistrationChannel.Create(requirement, 1, true, null, Now).IsNative).IsTrue();
        await Assert.That(RegistrationChannel.Create(requirement, 2, false, Guid.CreateVersion7(), Now).RegistrationProviderBindingId).IsNotNull();
        await Assert.That(() => RegistrationChannel.Create(requirement, 3, true, Guid.CreateVersion7(), Now)).Throws<ArgumentException>();
        await Assert.That(() => RegistrationChannel.Create(requirement, 4, false, null, Now)).Throws<ArgumentException>();
    }

    [Test]
    public async Task PublishedProviderBindingRejectsMappingMutation()
    {
        RegistrationProviderBinding binding = Binding();
        RegistrationProviderFieldMapping mapping = RegistrationProviderFieldMapping.Create(binding, "attendee.email", "email", true);
        binding.AddFieldMapping(mapping);
        binding.Publish(Hash(), Now.AddMinutes(1));

        await Assert.That(binding.StateId).IsEqualTo((int)RegistrationProviderBindingStateEnum.Published);
        await Assert.That(() => binding.AddFieldMapping(RegistrationProviderFieldMapping.Create(binding, "attendee.name", "name", false)))
            .Throws<InvalidOperationException>();
        await Assert.That(binding.PublishedMappingRevisionHash).IsEqualTo(Hash());
    }

    [Test]
    public async Task PublishedProviderBindingRejectsDirectMappingFactories()
    {
        RegistrationProviderBinding binding = Binding();
        RegistrationProviderFieldMapping field = RegistrationProviderFieldMapping.Create(binding, "attendee.email", "email", true);
        binding.AddFieldMapping(field);
        binding.Publish(Hash(), Now.AddMinutes(1));

        await Assert.That(() => RegistrationProviderCapability.Create(binding, "forms", "hosted", "v1", "policy-1", "evidence-1", "callback"))
            .Throws<InvalidOperationException>();
        await Assert.That(() => RegistrationProviderFieldMapping.Create(binding, "attendee.name", "name", false))
            .Throws<InvalidOperationException>();
        await Assert.That(() => RegistrationProviderOptionMapping.Create(binding, field, "yes", "1"))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task CapabilityTuplePersistsUnknownProviderIdentityShape()
    {
        RegistrationProviderBinding binding = Binding();
        RegistrationProviderCapability capability = RegistrationProviderCapability.Create(
            binding, "unknown-provider", "self-hosted", "2026-08", "policy-7", "rev-42", "schema.read");
        binding.AddCapability(capability);

        await Assert.That(capability.TupleKey).IsEqualTo("unknown-provider|self-hosted|2026-08|policy-7|rev-42|schema.read");
        await Assert.That(() => RegistrationProviderCapability.Create(binding, " ", "self-hosted", "v1", "p1", "r1", "schema.read"))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Registry_DefinesTenantScopedRegistrationProviderCredentials()
    {
        SecretDefinition apiToken = SecretDefinitionRegistry.GetRequired(SecretDefinitionRegistry.Keys.RegistrationProviders.ApiToken);
        SecretDefinition webhookSecret = SecretDefinitionRegistry.GetRequired(SecretDefinitionRegistry.Keys.RegistrationProviders.WebhookSecret);

        await Assert.That(apiToken.AllowedScopes).IsEquivalentTo([SecretScope.Tenant]);
        await Assert.That(webhookSecret.AllowedScopes).IsEquivalentTo([SecretScope.Tenant]);
        await Assert.That(apiToken.AllowedSources).Contains(SecretSourceType.EnvironmentVariable);
        await Assert.That(webhookSecret.DefaultInfisicalPath).IsEqualTo("/registration-providers");
    }

    [Test]
    public async Task ApprovedOrigins_AcceptOnlyHttpsOriginsAndBlockLocalTargets()
    {
        RegistrationProviderConnection connection = RegistrationProviderConnection.Create(
            Guid.CreateVersion7(), "provider", RegistrationProviderKindEnum.ExternalForm,
            RegistrationProviderDeploymentKindEnum.HostedSaas, "FORMBRICKS", "HOSTED_SAAS", "v1", "formbricks-policy-v1",
            "official-api-v1-2026-08", "https:/" + "/app.formbricks.com/api/v1/management", "https:/" + "/app.formbricks.com",
            "workspace_1", null, null, Now);

        connection.ReplaceApprovedOrigins([" HTTPS://Forms.Example.Org:8443/ "], Now);

        await Assert.That(connection.ApprovedOrigins.Single().Origin).IsEqualTo("https://forms.example.org:8443");
        await Assert.That(connection.IsOriginApproved(new Uri("https://forms.example.org:8443"))).IsTrue();
        await Assert.That(() => connection.ReplaceApprovedOrigins(["http://forms.example.org"], Now)).Throws<ArgumentException>();
        await Assert.That(() => connection.ReplaceApprovedOrigins(["https://127.0.0.1"], Now)).Throws<ArgumentException>();
        await Assert.That(() => connection.ReplaceApprovedOrigins(["https://forms.example.org/path"], Now)).Throws<ArgumentException>();
    }

    [Test]
    public async Task Connection_RequiresTupleAndHttpsProviderBaseUrls()
    {
        RegistrationProviderConnection connection = RegistrationProviderConnection.Create(
            Guid.CreateVersion7(), "provider", RegistrationProviderKindEnum.ExternalForm,
            RegistrationProviderDeploymentKindEnum.HostedSaas, " FORMBRICKS ", "HOSTED_SAAS", "v1", "policy",
            "evidence", "https:/" + "/app.formbricks.com/api/v1/management/", "https:/" + "/app.formbricks.com/",
            "workspace", null, null, Now);

        await Assert.That(connection.TupleKey).IsEqualTo("FORMBRICKS|HOSTED_SAAS|v1|policy|evidence");
        await Assert.That(connection.ManagementApiBaseUrl).IsEqualTo("https://app.formbricks.com/api/v1/management");
        await Assert.That(connection.PublicBaseUrl).IsEqualTo("https://app.formbricks.com");
        await Assert.That(() => RegistrationProviderConnection.Create(
                Guid.CreateVersion7(), "provider", RegistrationProviderKindEnum.ExternalForm,
                RegistrationProviderDeploymentKindEnum.HostedSaas, "FORMBRICKS", "HOSTED_SAAS", "v1", "policy",
                "evidence", "http:/" + "/app.formbricks.com", "https:/" + "/app.formbricks.com", "workspace", null, null, Now))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Connection_MetadataIsBoundedAndCheckpointsRequireUtc()
    {
        RegistrationProviderConnection connection = RegistrationProviderConnection.Create(
            Guid.CreateVersion7(), "provider", RegistrationProviderKindEnum.ExternalForm,
            RegistrationProviderDeploymentKindEnum.HostedSaas, "GOOGLE_FORMS", "GOOGLE_WORKSPACE", "v1", "policy",
            "evidence", "https:/" + "/forms.googleapis.com/v1", "https:/" + "/docs.google.com",
            "workspace", null, null, Now);

        connection.UpdateOAuthMetadata(
            " https://www.googleapis.com/auth/forms.responses.readonly  openid https://www.googleapis.com/auth/forms.body.readonly ",
            " user:forms-owner@example.test ",
            " projects/forms-project/topics/registration-watch ");
        connection.RecordCredentialRefresh(Now.AddMinutes(1));
        connection.RecordAccessValidated(Now.AddMinutes(2));

        await Assert.That(connection.GrantedOAuthScopes).IsEqualTo("https://www.googleapis.com/auth/forms.body.readonly https://www.googleapis.com/auth/forms.responses.readonly openid");
        await Assert.That(connection.ProviderIdentity).IsEqualTo("user:forms-owner@example.test");
        await Assert.That(connection.PubSubConfigurationReference).IsEqualTo("projects/forms-project/topics/registration-watch");
        await Assert.That(connection.LastCredentialRefreshAt).IsEqualTo(Now.AddMinutes(1));
        await Assert.That(connection.LastAccessValidatedAt).IsEqualTo(Now.AddMinutes(2));
        await Assert.That(() => connection.RecordCredentialRefresh(DateTime.SpecifyKind(Now, DateTimeKind.Local))).Throws<ArgumentException>();
        await Assert.That(() => connection.UpdateOAuthMetadata("openid\nemail", "identity", "pubsub")).Throws<ArgumentException>();
    }

    [Test]
    public async Task Binding_ProvisionedProviderIdsAreDraftOnly()
    {
        RegistrationProviderBinding binding = Binding();
        Guid webhookSecretBindingId = Guid.CreateVersion7();

        binding.SetDraftProvisionedSurvey(" survey-1 ", " revision-1 ");
        binding.SetDraftProvisionedSubscription(" webhook-1 ", webhookSecretBindingId);
        binding.Publish(Hash(), Now.AddMinutes(1));

        await Assert.That(binding.ProviderSurveyId).IsEqualTo("survey-1");
        await Assert.That(binding.ProviderSurveyRevisionId).IsEqualTo("revision-1");
        await Assert.That(binding.ProviderWebhookId).IsEqualTo("webhook-1");
        await Assert.That(binding.WebhookSecretBindingId).IsEqualTo(webhookSecretBindingId);
        await Assert.That(() => binding.SetDraftProvisionedSurvey("survey-2", null)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ExternalImportedFormVersionPinsImmutableProviderProvenance()
    {
        RegistrationForm form = RegistrationForm.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), "external", "survey", "Survey", Now);
        Guid connectionId = Guid.CreateVersion7();
        Guid revisionId = Guid.CreateVersion7();

        RegistrationFormVersion version = RegistrationFormVersion.CreateExternalImported(
            form,
            1,
            "en",
            connectionId,
            revisionId,
            " survey-1 ",
            " revision-1 ",
            Convert.ToBase64String(new byte[32]),
            Now);

        await Assert.That(version.SourceKindId).IsEqualTo((int)RegistrationFormVersionSourceKindEnum.ExternalImported);
        await Assert.That(version.ExternalRegistrationProviderConnectionId).IsEqualTo(connectionId);
        await Assert.That(version.ExternalRegistrationProviderSchemaRevisionId).IsEqualTo(revisionId);
        await Assert.That(version.ExternalProviderSurveyId).IsEqualTo("survey-1");
        await Assert.That(version.ExternalProviderSurveyRevisionId).IsEqualTo("revision-1");
        await Assert.That(version.ExternalImportMappingRevisionHash).IsEqualTo(Convert.ToBase64String(new byte[32]));
    }

    [Test]
    [Arguments("https://0.0.0.0")]
    [Arguments("https://224.0.0.1")]
    [Arguments("https://[::]")]
    [Arguments("https://[ff02::1]")]
    [Arguments("https://[fc00::1]")]
    [Arguments("https://[fd00:ec2::254]")]
    [Arguments("https://[::ffff:127.0.0.1]")]
    [Arguments("https://[::ffff:10.0.0.1]")]
    [Arguments("https://[::ffff:172.16.0.1]")]
    [Arguments("https://[::ffff:192.168.0.1]")]
    [Arguments("https://[::ffff:169.254.169.254]")]
    public async Task ApprovedOrigins_BlockSpecialAndPrivateIpLiterals(string origin)
    {
        RegistrationProviderConnection connection = RegistrationProviderConnection.Create(
            Guid.CreateVersion7(), "provider", RegistrationProviderKindEnum.ExternalForm,
            RegistrationProviderDeploymentKindEnum.HostedSaas, "FORMBRICKS", "HOSTED_SAAS", "v1", "formbricks-policy-v1",
            "official-api-v1-2026-08", "https:/" + "/app.formbricks.com/api/v1/management", "https:/" + "/app.formbricks.com",
            "workspace_1", null, null, Now);

        await Assert.That(() => connection.ReplaceApprovedOrigins([origin], Now)).Throws<ArgumentException>();
    }

    private static RegistrationProviderBinding Binding() => RegistrationProviderBinding.Create(
        Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
        RegistrationProviderPresentationModeEnum.Redirect, RegistrationProviderCollectionModeEnum.ProviderHosted,
        RegistrationProviderCompletionModeEnum.Callback, RegistrationProviderTrustLevelEnum.SelectedFields, null, Now);

    private static RegistrationRequirement Requirement()
    {
        RegistrationWorkflow workflow = RegistrationWorkflow.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), "attendee", Now);
        return RegistrationRequirement.Create(workflow, 1, RegistrationRequirementCriticalityEnum.Optional, true,
            RegistrationRequirementCompletionEffectEnum.EnrichesRegistration, RegistrationAnswerSyncModeEnum.SELECTED_FIELDS,
            RegistrationRequirementSubjectTypeEnum.AllOrders, null, Now);
    }

    internal static RegistrationEvidenceHash Hash() => RegistrationEvidenceHash.Create(Convert.ToBase64String(new byte[32]));
}
