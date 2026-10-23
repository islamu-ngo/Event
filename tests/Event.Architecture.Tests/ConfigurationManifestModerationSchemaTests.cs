namespace Event.Architecture.Tests;

using System.Text.Json;
using System.Collections.Immutable;
using Explore.Application.Features.ConfigurationManifest.Moderation;
using Explore.Application.Features.Moderation.Policy;
using Explore.Application.Services.Moderation;
using ISLAMU.Moderation.Domain.Policies;
using ISLAMU.Wire.Contracts.ConfigurationPortability;
using ISLAMU.ConfigurationManifest.SchemaGenerator;

public sealed class ConfigurationManifestModerationSchemaTests
{
    [Test]
    public async Task ModerationPreview_UsesNamedDefinitionsWithoutExposingTheDisclosureOwner()
    {
        using var schema = JsonDocument.Parse(GeneratedContractInputs.OpenSchema());
        var definitions = schema.RootElement.GetProperty("components").GetProperty("schemas");
        foreach (var owner in new[] { "ConfigurationImportPreviewResult", "HalResourceOfConfigurationImportPreviewResult" })
        {
            var properties = definitions.GetProperty(owner).GetProperty("properties");
            await Assert.That(properties.GetProperty("moderation").GetProperty("items").GetProperty("$ref").GetString())
                .IsEqualTo("#/components/schemas/ConfigurationImportModerationTargetPreview");
            await Assert.That(properties.TryGetProperty("disclosure", out _)).IsFalse();
        }
        var target = definitions.GetProperty("ConfigurationImportModerationTargetPreview").GetProperty("properties");
        await Assert.That(target.GetProperty("changes").GetProperty("items").GetProperty("$ref").GetString())
            .IsEqualTo("#/components/schemas/ConfigurationImportModerationChange");
        await Assert.That(target.GetProperty("diagnostics").GetProperty("items").GetProperty("$ref").GetString())
            .IsEqualTo("#/components/schemas/ConfigurationImportModerationDiagnostic");
    }

    [Test]
    public async Task AllNonChatNativeDefinitionsAndRecipes_SurviveTheWireBoundaryWithoutAuthority()
    {
        foreach (var reference in ModerationReferenceCatalogue.Profiles.Values)
        {
            var response = reference.Responses[0];
            var native = new ModerationConfigurationDocument(1, ModerationReferenceCatalogue.Revision,
                [ModerationNativeMode.Embedded, ModerationNativeMode.RemoteStrict],
                [new(new("class.reference", 1), "Complete class", reference.Ground, reference.Basis)],
                [new(new("reason.reference", 1), "Complete reason", new("class.reference", 1))],
                [new(new("profile.reference", 1), new("reason.reference", 1), reference.Id, reference.Revision,
                    response.Grade, reference.ScopeKinds.ToImmutableArray(), reference.SubjectKinds.ToImmutableArray(),
                    reference.Effects.ToImmutableArray(), reference.Bundle, reference.ProtectionPurpose,
                    reference.Strategies.Order().ToImmutableArray(), new ModerationPolicyTiming(response.Timing.Kind, response.Timing.Duration),
                    response.RequiredApprovals, 6, reference.Recognition,
                    reference.ConsequencePlans.Select((plan, index) => new ModerationConfigurationRecipe(
                        index == 0 ? null : reference.Strategies.Order().ElementAt(index - 1),
                        plan.Content, plan.Management, plan.Resources, plan.NewAdmission, plan.ExistingAdmission,
                        plan.PrivateDisclosure, plan.Settlement, plan.Capacity, plan.Credentials, plan.Notifications,
                        plan.Recognition, plan.PendingWork, plan.Restoration)).ToImmutableArray())],
                [new("profile.reference", 1)]);
            var wire = JsonSerializer.Deserialize(ModerationConfigurationDocumentProjection.Serialize(native),
                ConfigurationPortabilityJsonContext.Default.ConfigurationModerationDocumentV1)!;
            var package = new TenantConfigurationPackageV1Alpha2
            {
                Schema = TenantConfigurationPackageContractMetadata.SchemaId,
                ApiVersion = TenantConfigurationPackageContractMetadata.ApiVersion,
                Kind = TenantConfigurationPackageContractMetadata.Kind,
                Metadata = new() { Name = "reference", Source = new() { TenantName = "reference" } },
                Spec = new()
                {
                    DisplayName = "Reference", Settings = new Dictionary<string, JsonElement>(),
                    Documents = new Dictionary<string, ConfigurationManifestDocumentV1Alpha2>(),
                    LegalDocuments = new Dictionary<string, ConfigurationManifestLegalDocumentV1Alpha2>(), Moderation = wire
                }
            };
            var retained = ConfigurationPortabilityJsonCodec.ParseTenantConfigurationPackage(
                ConfigurationPortabilityJsonCodec.SerializeTenantConfigurationPackage(package)).Spec.Moderation!;
            var reverse = ModerationConfigurationDocumentProjection.Parse(JsonSerializer.SerializeToUtf8Bytes(retained,
                ConfigurationPortabilityJsonContext.Default.ConfigurationModerationDocumentV1));
            await Assert.That(reverse.Profiles[0]).IsEqualTo(native.Profiles[0] with
            {
                ScopeKinds = reverse.Profiles[0].ScopeKinds, SubjectKinds = reverse.Profiles[0].SubjectKinds,
                Effects = reverse.Profiles[0].Effects, Strategies = reverse.Profiles[0].Strategies,
                Recipes = reverse.Profiles[0].Recipes
            });
            await Assert.That(reverse.Profiles[0].Effects.SequenceEqual(native.Profiles[0].Effects)).IsTrue();
            await Assert.That(reverse.Profiles[0].Recipes.SequenceEqual(native.Profiles[0].Recipes)).IsTrue();
            await Assert.That(reverse.Profiles[0].ScopeKinds.SequenceEqual(native.Profiles[0].ScopeKinds)).IsTrue();
            await Assert.That(reverse.Profiles[0].SubjectKinds.SequenceEqual(native.Profiles[0].SubjectKinds)).IsTrue();
            await Assert.That(reverse.Profiles[0].Strategies.SequenceEqual(native.Profiles[0].Strategies)).IsTrue();
            await Assert.That(reverse.Classes.SequenceEqual(native.Classes)).IsTrue();
            await Assert.That(reverse.Reasons.SequenceEqual(native.Reasons)).IsTrue();
            await Assert.That(reverse.SupportedProfiles.SequenceEqual(native.SupportedProfiles)).IsTrue();
        }
    }

    [Test]
    public async Task CompleteModeration_IsOptionalAtAllThreeOwnersAndNeverOpaque()
    {
        using var manifest = JsonDocument.Parse(ConfigurationManifestJsonSchemaGenerator.GenerateConfigurationManifest());
        using var package = JsonDocument.Parse(ConfigurationManifestJsonSchemaGenerator.GenerateTenantConfigurationPackage());
        foreach (var (root, owner) in new[]
        {
            (manifest.RootElement, "manifestInstance"),
            (manifest.RootElement, "tenantSpec"),
            (package.RootElement, "tenantPackageSpec")
        })
        {
            var definitions = root.GetProperty("$defs");
            var section = definitions.GetProperty(owner);
            await Assert.That(section.GetProperty("properties").GetProperty("moderation").GetProperty("$ref").GetString())
                .IsEqualTo("#/$defs/moderationDocument");
            await Assert.That(section.GetProperty("required").EnumerateArray().Any(item => item.GetString() == "moderation"))
                .IsFalse();
            var document = definitions.GetProperty("moderationDocument");
            await Assert.That(document.GetProperty("additionalProperties").GetBoolean()).IsFalse();
            await Assert.That(document.GetProperty("properties").GetProperty("profiles").GetProperty("maxItems").GetInt32())
                .IsEqualTo(256);
            var recipe = definitions.GetProperty("moderationRecipe");
            await Assert.That(definitions.GetProperty("moderationProfile").GetProperty("properties")
                .GetProperty("scopeKinds").GetProperty("items").GetProperty("enum")
                .EnumerateArray().Select(item => item.GetString())).Contains("Event");
            await Assert.That(recipe.GetProperty("required").GetArrayLength()).IsEqualTo(14);
            await Assert.That(recipe.GetProperty("properties").EnumerateObject().Count()).IsEqualTo(14);
            await Assert.That(recipe.GetProperty("additionalProperties").GetBoolean()).IsFalse();
            await Assert.That(recipe.GetProperty("properties").GetProperty("restoration").GetProperty("enum")
                .EnumerateArray().Select(item => item.GetString()))
                .Contains("QualifiedAgeCorrectionNoHistoryPenalty");
            var reference = definitions.GetProperty("moderationReference");
            await Assert.That(reference.GetProperty("properties").GetProperty("revision").GetProperty("minimum").GetInt64())
                .IsEqualTo(1);
            await Assert.That(reference.GetProperty("properties").GetProperty("code").GetProperty("maxLength").GetInt32())
                .IsEqualTo(4000);
        }
    }
}
