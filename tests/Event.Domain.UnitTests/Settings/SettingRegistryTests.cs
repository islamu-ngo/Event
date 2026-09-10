namespace Event.Domain.UnitTests.Settings;

using System.Reflection;
using System.Text.Json;
using Explore.Domain.Constants;
using Explore.Domain.Settings;
using Explore.Domain.Settings.Definitions;

public class SettingRegistryTests
{
    [Test]
    public async Task All_ReturnsNonEmptyCollection()
    {
        await Assert.That(SettingRegistry.All.Count).IsGreaterThan(0);
    }

    [Test]
    public async Task All_HasNoDuplicateKeys()
    {
        var keys = SettingRegistry.All.Select(d => d.Key).ToList();
        var distinctKeys = keys.Distinct().ToList();

        await Assert.That(keys.Count).IsEqualTo(distinctKeys.Count);
    }

    [Test]
    public async Task All_EveryDefinitionHasNonEmptyKey()
    {
        foreach (var definition in SettingRegistry.All)
        {
            await Assert.That(string.IsNullOrWhiteSpace(definition.Key)).IsFalse();
        }
    }

    [Test]
    public async Task All_EveryDefinitionHasCategory()
    {
        foreach (var definition in SettingRegistry.All)
        {
            await Assert.That(string.IsNullOrWhiteSpace(definition.Category)).IsFalse();
        }
    }

    [Test]
    public async Task All_EveryDefinitionHasDescription()
    {
        foreach (var definition in SettingRegistry.All)
        {
            await Assert.That(string.IsNullOrWhiteSpace(definition.Description)).IsFalse();
        }
    }

    [Test]
    public async Task All_MinScopeIsLessThanOrEqualToMaxScope()
    {
        foreach (var definition in SettingRegistry.All)
        {
            await Assert.That((int)definition.MinScope)
                .IsLessThanOrEqualTo((int)definition.MaxScope);
        }
    }

    [Test]
    public async Task Get_ReturnsDefinitionForKnownKey()
    {
        var definition = SettingRegistry.Get("email.smtp_host");

        await Assert.That(definition).IsNotNull();
        await Assert.That(definition!.Category).IsEqualTo("Email");
    }

    [Test]
    public async Task Get_ReturnsNullForUnknownKey()
    {
        var definition = SettingRegistry.Get("nonexistent.key");

        await Assert.That(definition).IsNull();
    }

    [Test]
    public async Task Contains_ReturnsTrueForRegisteredKey()
    {
        await Assert.That(SettingRegistry.Contains("deployment.mode")).IsTrue();
    }

    [Test]
    public async Task Contains_ReturnsFalseForUnregisteredKey()
    {
        await Assert.That(SettingRegistry.Contains("does.not.exist")).IsFalse();
    }

    [Test]
    public async Task GetByCategory_ReturnsDefinitionsForKnownCategory()
    {
        var emailSettings = SettingRegistry.GetByCategory("Email");

        await Assert.That(emailSettings.Count).IsGreaterThan(0);
        foreach (var d in emailSettings)
        {
            await Assert.That(d.Category).IsEqualTo("Email");
        }
    }

    [Test]
    public async Task GetByCategory_ReturnsEmptyForUnknownCategory()
    {
        var result = SettingRegistry.GetByCategory("NonExistentCategory");

        await Assert.That(result.Count).IsEqualTo(0);
    }

    [Test]
    public async Task AllCategories_ContainsExpectedCategories()
    {
        var categories = SettingRegistry.AllCategories;

        await Assert.That(categories).Contains("Email");
        await Assert.That(categories).Contains("Branding");
        await Assert.That(categories).Contains("Analytics");
        await Assert.That(categories).Contains("Routing");
    }

    [Test]
    public async Task Registry_CoversAllGovernanceSettingKeys()
    {
        // All keys from GovernanceSettingKeys nested classes should exist in the registry
        var governanceKeys = new[]
        {
            GovernanceSettingKeys.Deployment.Mode,
            GovernanceSettingKeys.Tenants.SelfServiceRegistration,
            GovernanceSettingKeys.Tenants.WhiteLabelingEnabled,
            GovernanceSettingKeys.Routing.DefaultPublicHomePage,
            GovernanceSettingKeys.Events.UserSubmissionEnabled,
            GovernanceSettingKeys.Events.RequireApproval,
            GovernanceSettingKeys.Events.CardClickOpensDetailPage,
            GovernanceSettingKeys.Organizations.VerificationRequired,
            GovernanceSettingKeys.Organizations.TenantCanOmitVerification,
            GovernanceSettingKeys.Modules.IslamicEnabled,
            GovernanceSettingKeys.Modules.TechEnabled,
            GovernanceSettingKeys.Branding.DisplayName,
            GovernanceSettingKeys.Branding.LogoUrl,
            GovernanceSettingKeys.Branding.FaviconUrl,
            GovernanceSettingKeys.Branding.CustomCssUrl,
            GovernanceSettingKeys.Appearance.ActiveProfileId,
            GovernanceSettingKeys.Appearance.ThemeMode,
            GovernanceSettingKeys.Domains.InstanceBaseDomain,
            GovernanceSettingKeys.Domains.AllowTenantCustomDomain,
            GovernanceSettingKeys.Domains.TenantSubdomain,
            GovernanceSettingKeys.Domains.TenantCustomDomain,
            GovernanceSettingKeys.Email.SmtpHost,
            GovernanceSettingKeys.Email.SmtpPort,
            GovernanceSettingKeys.Email.SmtpSecurity,
            GovernanceSettingKeys.Email.FromAddress,
            GovernanceSettingKeys.Email.FromName,
            GovernanceSettingKeys.Email.SmtpTimeoutSeconds,
            GovernanceSettingKeys.Email.SmtpSkipCertValidation,
            GovernanceSettingKeys.Storage.Provider,
            GovernanceSettingKeys.Storage.DefaultMaxUploadBytes,
            GovernanceSettingKeys.Storage.DefaultTenantQuotaBytes,
            GovernanceSettingKeys.Storage.InstanceMaxUploadBytes,
            GovernanceSettingKeys.Storage.Endpoint,
            GovernanceSettingKeys.Storage.PublicEndpoint,
            GovernanceSettingKeys.Storage.BucketName,
            GovernanceSettingKeys.Storage.Region,
            GovernanceSettingKeys.Storage.ForcePathStyle,
            GovernanceSettingKeys.Storage.UploadUrlExpirationMinutes,
            GovernanceSettingKeys.Security.AuthorizationProvider,
            GovernanceSettingKeys.SupportAccess.Enabled,
            GovernanceSettingKeys.SupportAccess.MaxReadOnlyMinutes,
            GovernanceSettingKeys.SupportAccess.MaxWriteMinutes,
            GovernanceSettingKeys.SupportAccess.AllowWriteMode,
            GovernanceSettingKeys.SupportAccess.RequireTicketReference,
            GovernanceSettingKeys.SupportAccess.OneActiveSessionPerActor,
            GovernanceSettingKeys.Cerbos.TenantCustomizationEnabled,
            GovernanceSettingKeys.Cerbos.Mode,
            GovernanceSettingKeys.Cerbos.CustomEndpoint,
            GovernanceSettingKeys.Cerbos.CustomAdminEndpoint,
            GovernanceSettingKeys.Analytics.Provider,
            GovernanceSettingKeys.Analytics.Enabled,
            GovernanceSettingKeys.Analytics.ApiKey,
            GovernanceSettingKeys.Analytics.EndpointUrl,
            GovernanceSettingKeys.PublicExperience.Mode,
            GovernanceSettingKeys.PublicExperience.EventCatalogLabel,
            GovernanceSettingKeys.PublicExperience.PrimaryOrganizationId,
            GovernanceSettingKeys.PublicExperience.HomeBlocks,
            GovernanceSettingKeys.PublicExperience.Ctas,
            GovernanceSettingKeys.PublicExperience.EventSectionPresets,
        };

        var missingKeys = governanceKeys
            .Where(key => !SettingRegistry.Contains(key))
            .ToList();

        await Assert.That(missingKeys.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Registry_ExcludesExternalSecretKeys()
    {
        var secretKeys = new[]
        {
            InfrastructureSecretSettingKeys.Email.SmtpUsername,
            InfrastructureSecretSettingKeys.Email.SmtpPassword,
            InfrastructureSecretSettingKeys.Storage.AccessKeyId,
            InfrastructureSecretSettingKeys.Storage.SecretAccessKey,
            InfrastructureSecretSettingKeys.Cerbos.CustomAdminUsername,
            InfrastructureSecretSettingKeys.Cerbos.CustomAdminPassword,
        };

        await Assert.That(secretKeys.All(key => !SettingRegistry.Contains(key))).IsTrue();
    }

    [Test]
    public async Task Registry_DeploymentModeIsInstanceOnly()
    {
        var definition = SettingRegistry.Get("deployment.mode");

        await Assert.That(definition).IsNotNull();
        await Assert.That(definition!.MinScope).IsEqualTo(SettingScope.Instance);
        await Assert.That(definition.MaxScope).IsEqualTo(SettingScope.Instance);
    }

    [Test]
    public async Task Registry_TenantCanOmitVerificationIsInstanceOnly()
    {
        var definition = SettingRegistry.Get(GovernanceSettingKeys.Organizations.TenantCanOmitVerification);

        await Assert.That(definition).IsNotNull();
        await Assert.That(definition!.MinScope).IsEqualTo(SettingScope.Instance);
        await Assert.That(definition.MaxScope).IsEqualTo(SettingScope.Instance);
    }

    [Test]
    public async Task Registry_AppearanceThemeModeSupportsUserOverrides()
    {
        var definition = SettingRegistry.Get(GovernanceSettingKeys.Appearance.ThemeMode);

        await Assert.That(definition).IsNotNull();
        await Assert.That(definition!.Category).IsEqualTo("Appearance");
        await Assert.That(definition.MaxScope).IsEqualTo(SettingScope.User);
        await Assert.That(definition.AllowedValues).IsEquivalentTo(new[] { "system", "light", "dark" });
    }

    [Test]
    public async Task Registry_StorageProviderDefaultsToLocalAndSupportsTenantOverrides()
    {
        var definition = SettingRegistry.Get(GovernanceSettingKeys.Storage.Provider);

        await Assert.That(definition).IsNotNull();
        await Assert.That(definition!.Category).IsEqualTo("ObjectStorage");
        await Assert.That(definition.ValueType).IsEqualTo(SettingValueType.String);
        await Assert.That(definition.DefaultValue).IsEqualTo("\"local\"");
        await Assert.That(definition.MaxScope).IsEqualTo(SettingScope.Tenant);
        await Assert.That(definition.AllowedValues).IsEquivalentTo(new[] { "local", "s3_compatible", "legacy_external" });
    }

    [Test]
    public async Task Registry_StorageQuotaSettingsUseLongValues()
    {
        var tenantQuota = SettingRegistry.Get(GovernanceSettingKeys.Storage.DefaultTenantQuotaBytes);
        var uploadLimit = SettingRegistry.Get(GovernanceSettingKeys.Storage.DefaultMaxUploadBytes);
        var instanceLimit = SettingRegistry.Get(GovernanceSettingKeys.Storage.InstanceMaxUploadBytes);

        await Assert.That(tenantQuota).IsNotNull();
        await Assert.That(tenantQuota!.ValueType).IsEqualTo(SettingValueType.Long);
        await Assert.That(tenantQuota.MaxScope).IsEqualTo(SettingScope.Tenant);

        await Assert.That(uploadLimit).IsNotNull();
        await Assert.That(uploadLimit!.ValueType).IsEqualTo(SettingValueType.Long);
        await Assert.That(uploadLimit.MaxScope).IsEqualTo(SettingScope.Tenant);

        await Assert.That(instanceLimit).IsNotNull();
        await Assert.That(instanceLimit!.ValueType).IsEqualTo(SettingValueType.Long);
        await Assert.That(instanceLimit.MaxScope).IsEqualTo(SettingScope.Instance);
    }

    [Test]
    public async Task Registry_EventCardClickBehaviorSupportsUserOverrides()
    {
        var definition = SettingRegistry.Get(GovernanceSettingKeys.Events.CardClickOpensDetailPage);

        await Assert.That(definition).IsNotNull();
        await Assert.That(definition!.MaxScope).IsEqualTo(SettingScope.User);
    }

    [Test]
    public async Task Registry_ReportingIntakeEnabledIsCanonicalTenantPolicy()
    {
        const string intakeEnabledKey = "event_reporting.intake_enabled";

        await Assert.That(SettingRegistry.Contains(intakeEnabledKey)).IsTrue();

        var definition = SettingRegistry.Get(intakeEnabledKey);
        await Assert.That(definition).IsNotNull();
        await Assert.That(definition!.ValueType).IsEqualTo(SettingValueType.Boolean);
        await Assert.That(definition.DefaultValue).IsEqualTo("true");
        await Assert.That(definition.Category).IsEqualTo("EventReporting");
        await Assert.That(definition.MinScope).IsEqualTo(SettingScope.Instance);
        await Assert.That(definition.MaxScope).IsEqualTo(SettingScope.Tenant);
        await Assert.That(definition.IsLockable).IsTrue();
        await Assert.That(definition.IsSensitive).IsFalse();
    }

    [Test]
    public async Task Registry_CoordinatedMutationPolicyKeysRequireCoordinatedMutation()
    {
        var requiresCoordinatedMutation = typeof(SettingDefinition).GetProperty(
            "RequiresCoordinatedMutation",
            BindingFlags.Public | BindingFlags.Instance);

        await Assert.That(requiresCoordinatedMutation).IsNotNull();
        await Assert.That(requiresCoordinatedMutation!.PropertyType).IsEqualTo(typeof(bool));

        var coordinatedPolicyKeys = new[]
        {
            "event_reporting.intake_enabled",
            GovernanceSettingKeys.Events.UserSubmissionEnabled,
            GovernanceSettingKeys.Events.OrganizationSubmissionEnabled,
            GovernanceSettingKeys.Events.GroupSubmissionEnabled,
            GovernanceSettingKeys.Events.RequireApproval,
        };

        foreach (var key in coordinatedPolicyKeys)
        {
            var definition = SettingRegistry.Get(key);
            await Assert.That(definition).IsNotNull();
            await Assert.That(requiresCoordinatedMutation.GetValue(definition)).IsEqualTo(true);
        }
    }

    [Test]
    public async Task Registry_ExternalReportingProviderKeysRemainRegisteredAndUncoordinated()
    {
        foreach (var reportingDefinition in ReportingSettingDefinitions.All)
        {
            var registeredDefinition = SettingRegistry.Get(reportingDefinition.Key);
            await Assert.That(registeredDefinition).IsNotNull();
            await Assert.That(registeredDefinition!.Category).IsEqualTo("Reporting");
        }

        var requiresCoordinatedMutation = typeof(SettingDefinition).GetProperty(
            "RequiresCoordinatedMutation",
            BindingFlags.Public | BindingFlags.Instance);

        await Assert.That(requiresCoordinatedMutation).IsNotNull();

        foreach (var reportingDefinition in ReportingSettingDefinitions.All)
        {
            var registeredDefinition = SettingRegistry.Get(reportingDefinition.Key);
            await Assert.That(requiresCoordinatedMutation!.GetValue(registeredDefinition)).IsEqualTo(false);
        }
    }

    [Test]
    public async Task Registry_AtprotoFederationRegistersExactlyCapabilityProfileRecoveryAndSelfConsent()
    {
        var definitions = SettingRegistry.GetByCategory("AtprotoFederation");

        await Assert.That(definitions.Select(definition => definition.Key)).IsEquivalentTo(new[]
        {
            GovernanceSettingKeys.Federation.AtprotoEventsEnabled,
            GovernanceSettingKeys.Federation.AtprotoEventValidationProfile,
            GovernanceSettingKeys.Federation.AtprotoEventsBackfillEnabled,
            GovernanceSettingKeys.Federation.AtprotoEventsBackfillMode,
            GovernanceSettingKeys.Federation.AtprotoPublishMyEvents,
        });

        var capability = SettingRegistry.Get(GovernanceSettingKeys.Federation.AtprotoEventsEnabled)!;
        await Assert.That(capability.DefaultValue).IsEqualTo("false");
        await Assert.That(capability.MinScope).IsEqualTo(SettingScope.Instance);
        await Assert.That(capability.MaxScope).IsEqualTo(SettingScope.Tenant);
        await Assert.That(capability.IsLockable).IsTrue();

        var profile = SettingRegistry.Get(GovernanceSettingKeys.Federation.AtprotoEventValidationProfile)!;
        await Assert.That(profile.DefaultValue).IsEqualTo("\"platform\"");
        await Assert.That(profile.MinScope).IsEqualTo(SettingScope.Instance);
        await Assert.That(profile.MaxScope).IsEqualTo(SettingScope.Tenant);
        await Assert.That(profile.IsLockable).IsTrue();
        await Assert.That(profile.AllowedValues).IsEquivalentTo(new[] { "platform", "community_lexicon" });

        var consent = SettingRegistry.Get(GovernanceSettingKeys.Federation.AtprotoPublishMyEvents)!;
        await Assert.That(consent.DefaultValue).IsEqualTo("false");
        await Assert.That(consent.MinScope).IsEqualTo(SettingScope.User);
        await Assert.That(consent.MaxScope).IsEqualTo(SettingScope.User);
        await Assert.That(consent.IsLockable).IsFalse();
    }

    [Test]
    public async Task Registry_PublicExperienceKeysAreRegistered()
    {
        var keys = PublicExperienceSettingDefinitions.All.Select(d => d.Key).ToArray();

        await Assert.That(keys).IsEquivalentTo(new[]
        {
            GovernanceSettingKeys.PublicExperience.Mode,
            GovernanceSettingKeys.PublicExperience.VisitorAccessMode,
            GovernanceSettingKeys.PublicExperience.EventCatalogLabel,
            GovernanceSettingKeys.PublicExperience.PrimaryOrganizationId,
            GovernanceSettingKeys.PublicExperience.HomeBlocks,
            GovernanceSettingKeys.PublicExperience.Ctas,
            GovernanceSettingKeys.PublicExperience.EventSectionPresets,
            GovernanceSettingKeys.PublicExperience.DiscoveryAreas,
            GovernanceSettingKeys.PublicExperience.AnnouncementBarEnabled,
            GovernanceSettingKeys.PublicExperience.AnnouncementBarMessage,
            GovernanceSettingKeys.PublicExperience.AnnouncementBarLinkText,
            GovernanceSettingKeys.PublicExperience.AnnouncementBarLinkUrl,
            GovernanceSettingKeys.PublicExperience.AnnouncementBarRevision,
            GovernanceSettingKeys.PublicExperiencePreferences.AnnouncementBarDismissedRevision,
            GovernanceSettingKeys.HomeDiscoveryPreferences.AreaId,
            GovernanceSettingKeys.HomeDiscoveryPreferences.Mode,
        });

        foreach (var key in keys)
        {
            await Assert.That(SettingRegistry.Contains(key)).IsTrue();
        }
    }

    [Test]
    public async Task Registry_PublicExperienceSettingsAreInstanceToTenantScopedOnly()
    {
        foreach (var definition in PublicExperienceSettingDefinitions.All
                     .Where(d => d.Category == "PublicExperience"))
        {
            await Assert.That(definition.MinScope).IsEqualTo(SettingScope.Instance);
            await Assert.That(definition.MaxScope).IsEqualTo(SettingScope.Tenant);
        }
    }

    [Test]
    public async Task Registry_PublicExperienceDismissalPreferenceIsUserScoped()
    {
        var definition = SettingRegistry.Get(GovernanceSettingKeys.PublicExperiencePreferences.AnnouncementBarDismissedRevision);

        await Assert.That(definition).IsNotNull();
        await Assert.That(definition!.MinScope).IsEqualTo(SettingScope.User);
        await Assert.That(definition.MaxScope).IsEqualTo(SettingScope.User);
        await Assert.That(definition.IsLockable).IsFalse();
    }

    [Test]
    public async Task RegistryHomeDiscoveryPreferencesAreUserScoped()
    {
        var areaId = SettingRegistry.Get(GovernanceSettingKeys.HomeDiscoveryPreferences.AreaId);
        var mode = SettingRegistry.Get(GovernanceSettingKeys.HomeDiscoveryPreferences.Mode);

        await Assert.That(areaId).IsNotNull();
        await Assert.That(areaId!.MinScope).IsEqualTo(SettingScope.User);
        await Assert.That(areaId.MaxScope).IsEqualTo(SettingScope.User);
        await Assert.That(areaId.IsLockable).IsFalse();

        await Assert.That(mode).IsNotNull();
        await Assert.That(mode!.MinScope).IsEqualTo(SettingScope.User);
        await Assert.That(mode.MaxScope).IsEqualTo(SettingScope.User);
        await Assert.That(mode.IsLockable).IsFalse();
        await Assert.That(mode.AllowedValues).IsEquivalentTo(new[] { "area", "online", "all" });
    }

    [Test]
    public async Task RegistryDiscoveryAreasUseVersionedTenantJson()
    {
        var definition = SettingRegistry.Get(GovernanceSettingKeys.PublicExperience.DiscoveryAreas);

        await Assert.That(definition).IsNotNull();
        await Assert.That(definition!.ValueType).IsEqualTo(SettingValueType.Json);
        await Assert.That(definition.DefaultValue).IsEqualTo("{\"schemaVersion\":1,\"areas\":[]}");
        await Assert.That(definition.MinScope).IsEqualTo(SettingScope.Instance);
        await Assert.That(definition.MaxScope).IsEqualTo(SettingScope.Tenant);
    }

    [Test]
    public async Task Registry_PublicExperienceModeAllowedValuesAreConservativeModes()
    {
        var definition = SettingRegistry.Get(GovernanceSettingKeys.PublicExperience.Mode);

        await Assert.That(definition).IsNotNull();
        await Assert.That(definition!.ValueType).IsEqualTo(SettingValueType.String);
        await Assert.That(definition.DefaultValue).IsEqualTo("\"DiscoveryCentric\"");
        await Assert.That(definition.AllowedValues).IsEquivalentTo(new[]
        {
            "DiscoveryCentric",
            "OrganizationCentric",
        });
    }

    [Test]
    public async Task Registry_AiAssistantProviderAllowedValuesIncludeAnthropicCompatible()
    {
        var definition = SettingRegistry.Get(GovernanceSettingKeys.AiAssistant.Provider);

        await Assert.That(definition).IsNotNull();
        await Assert.That(definition!.AllowedValues).Contains("anthropic-compatible");
    }

    [Test]
    public async Task Registry_PublicExperienceJsonDefaultsAreVersionedEmptyConfigs()
    {
        var expectedArrayPropertiesByKey = new Dictionary<string, string>
        {
            [GovernanceSettingKeys.PublicExperience.HomeBlocks] = "blocks",
            [GovernanceSettingKeys.PublicExperience.Ctas] = "ctas",
            [GovernanceSettingKeys.PublicExperience.EventSectionPresets] = "presets",
        };

        foreach (var (key, arrayPropertyName) in expectedArrayPropertiesByKey)
        {
            var definition = SettingRegistry.Get(key);
            await Assert.That(definition).IsNotNull();
            await Assert.That(definition!.ValueType).IsEqualTo(SettingValueType.Json);

            using var document = JsonDocument.Parse(definition.DefaultValue);
            var root = document.RootElement;

            await Assert.That(root.GetProperty("schemaVersion").GetInt32()).IsEqualTo(1);
            await Assert.That(root.GetProperty(arrayPropertyName).ValueKind).IsEqualTo(JsonValueKind.Array);
            await Assert.That(root.GetProperty(arrayPropertyName).GetArrayLength()).IsEqualTo(0);
        }
    }

    [Test]
    public async Task Registry_PublicExperienceStringDefaultsAreMetadataOnly()
    {
        var eventCatalogLabel = SettingRegistry.Get(GovernanceSettingKeys.PublicExperience.EventCatalogLabel);
        var primaryOrganizationId = SettingRegistry.Get(GovernanceSettingKeys.PublicExperience.PrimaryOrganizationId);

        await Assert.That(eventCatalogLabel).IsNotNull();
        await Assert.That(eventCatalogLabel!.ValueType).IsEqualTo(SettingValueType.String);
        await Assert.That(eventCatalogLabel.DefaultValue).IsEqualTo("\"Events\"");

        await Assert.That(primaryOrganizationId).IsNotNull();
        await Assert.That(primaryOrganizationId!.ValueType).IsEqualTo(SettingValueType.String);
        await Assert.That(primaryOrganizationId.DefaultValue is "\"\"" or "null").IsTrue();
    }

    [Test]
    public async Task Registry_PublicExperienceSettingsDoNotIntroduceWorkspaceOwnershipModel()
    {
        var prohibitedFragments = new[]
        {
            "workspace",
            "tenant_workspace",
            "subtenant",
            "scope_id",
            "organization_scope_id",
        };

        var publicExperienceMetadata = PublicExperienceSettingDefinitions.All
            .SelectMany(definition => new[]
            {
                definition.Key,
                definition.Category,
                definition.Description,
                definition.DefaultValue,
            })
            .Select(value => value.ToLowerInvariant())
            .ToArray();

        foreach (var fragment in prohibitedFragments)
        {
            await Assert.That(publicExperienceMetadata.Any(value => value.Contains(fragment, StringComparison.Ordinal))).IsFalse();
        }
    }

    [Test]
    public async Task Count_MatchesAllCount()
    {
        await Assert.That(SettingRegistry.Count).IsEqualTo(SettingRegistry.All.Count);
    }
}
