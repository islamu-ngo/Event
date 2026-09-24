using Explore.Domain.Keycloak;

namespace Event.Domain.UnitTests.Keycloak;

public sealed class KeycloakOperationPolicyTests
{
    private readonly KeycloakOperationPolicy _policy = new();

    [Test]
    [Arguments(KeycloakChangeKind.UpdateRealm)]
    [Arguments(KeycloakChangeKind.UpdateExistingClient)]
    [Arguments(KeycloakChangeKind.ConvertClientKind)]
    [Arguments(KeycloakChangeKind.RotateClientSecret)]
    [Arguments(KeycloakChangeKind.UpdateUser)]
    [Arguments(KeycloakChangeKind.UpdateRealmRole)]
    [Arguments(KeycloakChangeKind.UpdateSharedClientScope)]
    [Arguments(KeycloakChangeKind.GrantOfflineAccess)]
    public async Task ExistingRealm_RejectsChangesOutsideNarrowClientResources(
        KeycloakChangeKind changeKind)
    {
        await Assert.That(_policy.IsAllowedOnExistingRealm(changeKind)).IsFalse();
    }

    [Test]
    public async Task ExistingRealm_AllowsOnlyExplicitClientAndMapperCreationOrRepair()
    {
        await Assert.That(_policy.IsAllowedOnExistingRealm(KeycloakChangeKind.CreateClient)).IsTrue();
        await Assert.That(_policy.IsAllowedOnExistingRealm(KeycloakChangeKind.CreateMapper)).IsTrue();
        await Assert.That(_policy.IsAllowedOnExistingRealm(KeycloakChangeKind.UpdateMapper)).IsTrue();
    }

    [Test]
    public async Task SameClientId_IsRejectedBeforeAnyOperationCanBePlanned()
    {
        var snapshot = new KeycloakInspectionSnapshot(
            "operators",
            "event-client",
            "EVENT-CLIENT",
            realmExists: true);

        await Assert.That(_policy.HasConflictingClientIds(snapshot)).IsTrue();
    }

    [Test]
    public async Task HealthyNativeAndInheritedMappings_NeedNoRepair()
    {
        var snapshot = new KeycloakInspectionSnapshot(
            "operators",
            "event-bff",
            "event-api",
            realmExists: true,
            [
                new(
                    ProviderId: "native-subject",
                    Semantic: KeycloakMapperSemantic.Subject,
                    Origin: KeycloakMapperOrigin.Native,
                    Audience: null,
                    AddsToAccessToken: true,
                    AddsToIdToken: true,
                    IsEffective: true),
                new(
                    ProviderId: "shared-api-audience",
                    Semantic: KeycloakMapperSemantic.Audience,
                    Origin: KeycloakMapperOrigin.Inherited,
                    Audience: "event-api",
                    AddsToAccessToken: true,
                    AddsToIdToken: false,
                    IsEffective: true)
            ]);

        await Assert.That(_policy.GetRequiredMapperRepairs(snapshot)).IsEmpty();
    }

    [Test]
    public async Task NativeSubjectAlongsideConflictingProducer_RequiresOperatorIntervention()
    {
        var snapshot = new KeycloakInspectionSnapshot(
            "operators",
            "event-bff",
            apiClientId: null,
            realmExists: true,
            [
                new(
                    ProviderId: "native-subject",
                    Semantic: KeycloakMapperSemantic.Subject,
                    Origin: KeycloakMapperOrigin.Native,
                    Audience: null,
                    AddsToAccessToken: true,
                    AddsToIdToken: true,
                    IsEffective: true),
                new(
                    ProviderId: "attribute-subject",
                    Semantic: KeycloakMapperSemantic.Subject,
                    Origin: KeycloakMapperOrigin.Direct,
                    Audience: null,
                    AddsToAccessToken: true,
                    AddsToIdToken: false,
                    IsEffective: false,
                    IsConflicting: true)
            ]);

        await Assert.That(_policy.HasConflictingMapper(
            snapshot,
            KeycloakMapperSemantic.Subject)).IsTrue();
    }

}
