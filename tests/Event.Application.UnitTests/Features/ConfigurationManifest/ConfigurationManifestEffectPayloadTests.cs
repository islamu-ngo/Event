using System.Text.Json;
using System.Text.Json.Nodes;
using Explore.Application.Features.ConfigurationManifest.Application;
using Explore.Domain;

namespace Event.Application.UnitTests.Features.ConfigurationManifest;

public sealed class ConfigurationManifestEffectPayloadTests
{
    private static readonly DateTime Started = new(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Completed = Started.AddSeconds(1);
    private static readonly Guid FirstTenant = Guid.Parse("019a0000-0000-7000-8000-000000000001");
    private static readonly Guid SecondTenant = Guid.Parse("019a0000-0000-7000-8000-000000000002");

    [Test]
    public async Task CanonicalInstructionsContainOnlyCreatedTenantsAndChangedKeyNames()
    {
        var operation = Operation();
        var results = Results(operation);
        string json = ConfigurationManifestEffectPayload.Create(operation, results);
        var payload = ConfigurationManifestEffectPayload.Read(json, Completed);

        await Assert.That(payload.Version).IsEqualTo(1);
        await Assert.That(payload.NotificationTimeUtc).IsEqualTo(Started);
        await Assert.That(payload.InstanceSettingKeys).IsEquivalentTo(["branding.display_name"]);
        await Assert.That(payload.Tenants.Select(tenant => tenant.TenantId)).IsEquivalentTo([FirstTenant, SecondTenant]);
        await Assert.That(payload.Tenants[0].TenantId).IsEqualTo(FirstTenant);
        await Assert.That(payload.Tenants[0].SettingKeys).IsEmpty();
        await Assert.That(payload.Tenants[1].SettingKeys).IsEquivalentTo(["appearance.default_theme_mode"]);
        await Assert.That(ConfigurationManifestEffectPayload.Create(operation, results.Reverse().ToArray())).IsEqualTo(json);

        using var document = JsonDocument.Parse(json);
        await Assert.That(document.RootElement.EnumerateObject().Select(property => property.Name))
            .IsEquivalentTo(["version", "notificationTimeUtc", "instanceSettingKeys", "tenants"]);
        await Assert.That(document.RootElement.GetProperty("tenants")[0].EnumerateObject().Select(property => property.Name))
            .IsEquivalentTo(["tenantId", "settingKeys"]);
    }

    [Test]
    [Arguments("version")]
    [Arguments("missing")]
    [Arguments("unknown")]
    [Arguments("null-keys")]
    [Arguments("null-tenants")]
    [Arguments("null-tenant")]
    [Arguments("null-key")]
    [Arguments("private-key")]
    [Arguments("tenant-private-key")]
    [Arguments("tenant-unknown")]
    [Arguments("tenant-missing")]
    [Arguments("tenant-null-keys")]
    [Arguments("duplicate-key")]
    [Arguments("duplicate-tenant")]
    [Arguments("empty-tenant")]
    [Arguments("non-v7-tenant")]
    [Arguments("non-utc")]
    [Arguments("after-completion")]
    [Arguments("null-time")]
    public async Task MalformedInstructionsAreRejectedBeforeTheyCanSelectEffects(string mutation)
    {
        var operation = Operation();
        var json = JsonNode.Parse(ConfigurationManifestEffectPayload.Create(operation, Results(operation)))!.AsObject();
        var tenant = json["tenants"]![1]!.AsObject();
        switch (mutation)
        {
            case "version": json["version"] = 2; break;
            case "missing": json.Remove("version"); break;
            case "unknown": json["settingValue"] = "not an allowed field"; break;
            case "null-keys": json["instanceSettingKeys"] = null; break;
            case "null-tenants": json["tenants"] = null; break;
            case "null-tenant": json["tenants"]![0] = null; break;
            case "null-key": json["instanceSettingKeys"] = new JsonArray((JsonNode?)null); break;
            case "private-key": json["instanceSettingKeys"] = new JsonArray("smtp.password"); break;
            case "tenant-private-key": tenant["settingKeys"] = new JsonArray("smtp.password"); break;
            case "tenant-unknown": tenant["settingValue"] = "not an allowed field"; break;
            case "tenant-missing": tenant.Remove("settingKeys"); break;
            case "tenant-null-keys": tenant["settingKeys"] = null; break;
            case "duplicate-key": tenant["settingKeys"] = new JsonArray("appearance.default_theme_mode", "appearance.default_theme_mode"); break;
            case "duplicate-tenant": json["tenants"]!.AsArray().Add(tenant.DeepClone()); break;
            case "empty-tenant": tenant["tenantId"] = Guid.Empty; break;
            case "non-v7-tenant": tenant["tenantId"] = Guid.Parse("419a0000-0000-4000-8000-000000000002"); break;
            case "non-utc": json["notificationTimeUtc"] = "2026-10-19T13:00:00+01:00"; break;
            case "after-completion": json["notificationTimeUtc"] = Completed.AddTicks(1); break;
            case "null-time": json["notificationTimeUtc"] = null; break;
        }
        await Assert.ThrowsAsync<JsonException>(() => Task.FromResult(
            ConfigurationManifestEffectPayload.Read(json.ToJsonString(), Completed)));
    }

    [Test]
    [Arguments(null)]
    [Arguments("null")]
    [Arguments("{}")]
    [Arguments("[]")]
    [Arguments("{\"version\":1,\"version\":1}")]
    public async Task AbsentOrAmbiguousPayloadHasNoLegacyFallback(string? json) =>
        await Assert.ThrowsAsync<JsonException>(() => Task.FromResult(ConfigurationManifestEffectPayload.Read(json, Completed)));

    [Test]
    [Arguments("root")]
    [Arguments("tenant-id")]
    [Arguments("tenant-keys")]
    public async Task DuplicateKnownPropertyIsRejectedEvenWhenBothValuesAreEqual(string member)
    {
        var operation = Operation();
        string json = ConfigurationManifestEffectPayload.Create(operation, Results(operation));
        string original = member switch
        {
            "tenant-id" => $"\"tenantId\":\"{FirstTenant:D}\"",
            "tenant-keys" => "\"settingKeys\":[]",
            _ => "\"version\":1"
        };
        json = json.Replace(original, $"{original},{original}", StringComparison.Ordinal);
        await Assert.ThrowsAsync<JsonException>(() => Task.FromResult(ConfigurationManifestEffectPayload.Read(json, Completed)));
    }

    [Test]
    [Arguments("unbound")]
    [Arguments("missing-result")]
    [Arguments("duplicate-result")]
    [Arguments("foreign-operation")]
    [Arguments("completion")]
    public async Task OnlyTheCompleteOriginalAppliedReceiptCanConstructInstructions(string mutation)
    {
        var operation = Operation(bind: mutation != "unbound");
        var results = Results(operation).ToList();
        switch (mutation)
        {
            case "missing-result": results.RemoveAt(0); break;
            case "duplicate-result": results[0] = results[1]; break;
            case "foreign-operation": results[0] = ConfigurationManifestTenantResult.Create(Guid.CreateVersion7(), FirstTenant,
                ConfigurationManifestTenantResultStatus.Created, [], [], Completed); break;
            case "completion": results[0] = ConfigurationManifestTenantResult.Create(operation.Id, FirstTenant,
                ConfigurationManifestTenantResultStatus.Created, [], [], Completed.AddSeconds(1)); break;
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => Task.FromResult(ConfigurationManifestEffectPayload.Create(operation, results)));
    }

    [Test]
    public async Task OversizedInstructionsAreRejectedBeforeJsonMaterialization() =>
        await Assert.ThrowsAsync<JsonException>(() => Task.FromResult(ConfigurationManifestEffectPayload.Read(
            new string(' ', ConfigurationManifestEffectPayload.MaximumPayloadByteCount + 1), Completed)));

    private static ConfigurationManifestOperation Operation(bool bind = true)
    {
        var operation = ConfigurationManifestOperation.Create(ConfigurationManifestAuditMode.Bootstrap,
            "configuration.islamu.org/v1alpha2", "ConfigurationManifest", "effect-contract",
            new string('a', 64), ConfigurationManifestOperationStatus.Applied,
            3, 2, 1, 0, null, null, Started, Completed,
            instanceSectionDigest: new string('b', 64), bootstrapGeneration: 1,
            instanceChangedSettingKeyNames: ["branding.display_name"]);
        if (bind) operation.BindEffectOutbox(Guid.CreateVersion7());
        return operation;
    }

    private static ConfigurationManifestTenantResult[] Results(ConfigurationManifestOperation operation) =>
    [
        ConfigurationManifestTenantResult.Create(operation.Id, SecondTenant, ConfigurationManifestTenantResultStatus.Created,
            ["appearance.default_theme_mode"], [], Completed),
        ConfigurationManifestTenantResult.Create(operation.Id, FirstTenant, ConfigurationManifestTenantResultStatus.Created, [], [], Completed),
        ConfigurationManifestTenantResult.Create(operation.Id, Guid.CreateVersion7(), ConfigurationManifestTenantResultStatus.SkippedExisting, [], [], Completed)
    ];
}
