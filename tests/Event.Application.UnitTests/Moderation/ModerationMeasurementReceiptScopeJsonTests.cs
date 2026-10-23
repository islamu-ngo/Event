using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Services.Moderation;
using ISLAMU.Moderation.Domain;

namespace Event.Application.UnitTests.Moderation;

public sealed class ModerationMeasurementReceiptScopeJsonTests
{
    [Test]
    [Arguments(ModerationScopeKind.Event)]
    [Arguments(ModerationScopeKind.Resource)]
    public async Task ReceiptRoundTripPreservesDistinctLeafCoordinateAndExactAncestry(ModerationScopeKind kind)
    {
        var receipt = Receipt(kind);
        var json = JsonSerializer.SerializeToNode(receipt)!;
        var coordinates = json["binding"]!["jurisdiction"]!;
        await Assert.That(coordinates.AsObject().Count).IsEqualTo(8);
        await Assert.That(coordinates[kind == ModerationScopeKind.Event ? "eventId" : "resourceId"]!.GetValue<Guid>())
            .IsEqualTo(receipt.Binding.Jurisdiction.Id);
        await Assert.That(coordinates[kind == ModerationScopeKind.Event ? "resourceId" : "eventId"]).IsNull();
        var restored = JsonSerializer.Deserialize<ModerationMeasurementReceipt>(json.ToJsonString())!;
        await Assert.That(restored.Binding).IsEqualTo(receipt.Binding);
        await Assert.That(restored.Binding.Jurisdiction.Coordinates).IsEqualTo(receipt.Binding.Jurisdiction.Coordinates);
    }

    [Test]
    [Arguments("communityId")]
    [Arguments("resourceId")]
    public async Task EventReceiptRejectsOtherLeafCoordinates(string field)
    {
        var json = JsonSerializer.SerializeToNode(Receipt(ModerationScopeKind.Event))!;
        json["binding"]!["jurisdiction"]![field] = Guid.CreateVersion7().ToString("D");
        await Assert.That(() => JsonSerializer.Deserialize<ModerationMeasurementReceipt>(json.ToJsonString()))
            .Throws<JsonException>();
    }

    [Test]
    [Arguments(ModerationScopeKind.TrustDomain)]
    [Arguments(ModerationScopeKind.Solution)]
    [Arguments(ModerationScopeKind.Instance)]
    [Arguments(ModerationScopeKind.Tenant)]
    [Arguments(ModerationScopeKind.Community)]
    [Arguments(ModerationScopeKind.Resource)]
    public async Task NonEventReceiptRejectsEventCoordinate(ModerationScopeKind kind)
    {
        var json = JsonSerializer.SerializeToNode(Receipt(kind))!;
        json["binding"]!["jurisdiction"]!["eventId"] = Guid.CreateVersion7().ToString("D");
        await Assert.That(() => JsonSerializer.Deserialize<ModerationMeasurementReceipt>(json.ToJsonString()))
            .Throws<JsonException>();
    }

    [Test]
    [Arguments(ModerationScopeKind.Event)]
    [Arguments(ModerationScopeKind.Tenant)]
    public async Task ReceiptRejectsMissingMandatoryEventCoordinateWithoutOldFormatFallback(ModerationScopeKind kind)
    {
        var json = JsonSerializer.SerializeToNode(Receipt(kind))!;
        json["binding"]!["jurisdiction"]!.AsObject().Remove("eventId");
        await Assert.That(() => JsonSerializer.Deserialize<ModerationMeasurementReceipt>(json.ToJsonString()))
            .Throws<JsonException>();
    }

    private static ModerationMeasurementReceipt Receipt(ModerationScopeKind kind)
    {
        var trust = AuthorityScope.TrustDomain(Guid.CreateVersion7());
        var solution = AuthorityScope.Solution(trust, Guid.CreateVersion7());
        var instance = AuthorityScope.Instance(solution, Guid.CreateVersion7());
        var tenant = AuthorityScope.Tenant(instance, Guid.CreateVersion7());
        var scope = kind switch
        {
            ModerationScopeKind.TrustDomain => trust,
            ModerationScopeKind.Solution => solution,
            ModerationScopeKind.Instance => instance,
            ModerationScopeKind.Tenant => tenant,
            ModerationScopeKind.Community => AuthorityScope.Community(tenant, Guid.CreateVersion7()),
            ModerationScopeKind.Resource => AuthorityScope.Resource(tenant, Guid.CreateVersion7()),
            _ => AuthorityScope.Event(tenant, Guid.CreateVersion7())
        };
        var at = new DateTimeOffset(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);
        var digest = new string('a', 64);
        return new()
        {
            Issuer = "native.scope-test", ReceiptId = Guid.CreateVersion7(), ChallengeId = Guid.CreateVersion7(),
            ChallengeNonce = digest, RunId = Guid.CreateVersion7(), HostBindingId = Guid.CreateVersion7(),
            TrustDomainId = trust.Id, TenantId = tenant.Id,
            Binding = new(Guid.CreateVersion7(), 1, Guid.CreateVersion7(), scope,
                ModerationRuleReference.Create(scope.Id, "scope.transport", 1),
                ModerationCapability.Participate, ModerationNativeMode.Embedded),
            ConsumerId = "scope.transport", ConsumerContractDigest = digest, InstallationId = Guid.CreateVersion7(),
            InstallationRevision = 1, InstallationWriterGeneration = 1, RuntimeGeneration = "scope-test-v1",
            InventoryFingerprint = digest, RunnerDigest = digest, CatalogueDigest = digest, TestBundleDigest = digest,
            ProductArtifactDigest = digest, ConfigurationDigest = digest, PrimaryConfigurationDigest = digest,
            TestConfigurationDigest = digest, ProviderCode = "in-memory", ProviderVersion = "1",
            CaseIds = ["scope.transport"], ExecutedCount = 1, AttemptCount = 1, ResultArtifactDigest = digest,
            StartedAt = at, CompletedAt = at.AddMinutes(1), IssuedAt = at.AddMinutes(2), ValidUntil = at.AddHours(1)
        };
    }
}
