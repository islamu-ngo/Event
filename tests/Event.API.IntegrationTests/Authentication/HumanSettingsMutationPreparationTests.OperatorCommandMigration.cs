using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Exceptions;
using Explore.Application.Features.InstanceOnboarding.Commands;
using Explore.Application.Services;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Settings.Documents.Payloads;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

public sealed partial class HumanSettingsMutationPreparationTests
{
    private static readonly JsonSerializerOptions OperatorCommandJsonOptions = new(JsonSerializerDefaults.Web);

    [Test]
    public async Task SaveAsync_PendingBootstrap_SavesNormalizedDraftWithFreshServerControlledFields()
    {
        await using var scenario = await OperatorCommandScenario.CreateAsync();
        var response = await scenario.SaveAsync(new() { PublicName = "  Independent Operator  " });
        await Assert.That(response.IsSuccess).IsTrue();
        var stored = await scenario.ReadStoredAsync();
        await Assert.That(stored!.SettingKey).IsEqualTo(InstanceOperatorIdentitySettingKeys.OperatorIdentity);
        var saved = ParseOperatorCommand(stored.Value);
        await Assert.That(saved.PublicName).IsEqualTo("Independent Operator");
        await Assert.That(saved.OperatorId is { } operatorId && operatorId.Version == 7).IsTrue();
        await Assert.That(saved.IsOfficialInstance).IsFalse();
        await Assert.That(saved.Revision is { } revision && revision.Version == 7).IsTrue();
        await Assert.That(response.Id!.PublicDisclosure.IsReady).IsFalse();
        await Assert.That(response.Id.PaidCommerce.IsReady).IsFalse();
        await Assert.That(response.Id.PaidCommerce.FailureCode).IsEqualTo("instance_operator_identity_incomplete");
    }

    [Test]
    public async Task SaveAsync_StaleRevision_ThrowsConcurrencyConflict()
    {
        Guid revision = Guid.CreateVersion7();
        await using (var stale = await OperatorCommandScenario.CreateAsync())
        {
            await stale.SeedAsync(OperatorCommandCandidate() with { Revision = revision });
            _ = await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
                stale.SaveAsync(OperatorCommandCandidate(), Guid.CreateVersion7()));
            await Assert.That(ParseOperatorCommand((await stale.ReadStoredAsync())!.Value).Revision).IsEqualTo(revision);
        }
        await using (var missing = await OperatorCommandScenario.CreateAsync())
        {
            _ = await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
                missing.SaveAsync(OperatorCommandCandidate(), Guid.CreateVersion7()));
            await Assert.That(await missing.ReadStoredAsync()).IsNull();
        }
    }

    [Test]
    public async Task SaveAsync_CompletedBootstrap_PersistsValidIncompleteDraftWithoutGrantingReadiness()
    {
        Guid revision = Guid.CreateVersion7();
        await using var scenario = await OperatorCommandScenario.CreateAsync();
        await scenario.SeedAsync(OperatorCommandCandidate() with { Revision = revision }, completed: true);
        var response = await scenario.SaveAsync(new() { PublicName = "Independent Operator" }, revision);
        await Assert.That(response.IsSuccess).IsTrue();
        await Assert.That(await scenario.ReadStoredAsync()).IsNotNull();
        await Assert.That(response.Id!.PublicDisclosure.IsReady).IsFalse();
        await Assert.That(response.Id.PaidCommerce.IsReady).IsFalse();
        await Assert.That(response.Id.PaidCommerce.ReasonCodes).Contains("instance_operator_identity_terms_url_missing");
        await Assert.That(response.Id.Revision).IsNotEqualTo(revision);
    }

    [Test]
    public async Task SaveAsync_CompletedBootstrap_AcceptsValidReplacementWithRotatedRevision()
    {
        Guid revision = Guid.CreateVersion7();
        await using var scenario = await OperatorCommandScenario.CreateAsync();
        await scenario.SeedAsync(OperatorCommandCandidate() with { Revision = revision }, completed: true);
        var response = await scenario.SaveAsync(OperatorCommandCandidate(), revision);
        await Assert.That(response.IsSuccess).IsTrue();
        var saved = ParseOperatorCommand((await scenario.ReadStoredAsync())!.Value);
        await Assert.That(saved.Revision).IsNotEqualTo(revision);
        await Assert.That(response.Id!.Revision).IsEqualTo(saved.Revision);
        await Assert.That(response.Id.PublicDisclosure.IsReady).IsTrue();
        await Assert.That(response.Id.PaidCommerce.IsReady).IsTrue();
    }

    [Test]
    public async Task SaveAsync_RejectsClientAssertionsOfServerControlledFields()
    {
        Guid operatorId = Guid.Parse("0198e2a4-5340-7f89-8abc-b8bdf43e0ea8");
        Guid revision = Guid.CreateVersion7();
        await using var scenario = await OperatorCommandScenario.CreateAsync();
        await scenario.SeedAsync(OperatorCommandCandidate() with
        {
            OperatorId = operatorId,
            IsOfficialInstance = true,
            Revision = revision
        });
        var response = await scenario.SaveAsync(OperatorCommandCandidate() with
        {
            OperatorId = Guid.CreateVersion7(),
            IsOfficialInstance = false
        }, revision);
        await Assert.That(response.IsSuccess).IsTrue();
        var saved = ParseOperatorCommand((await scenario.ReadStoredAsync())!.Value);
        await Assert.That(saved.OperatorId).IsEqualTo(operatorId);
        await Assert.That(saved.IsOfficialInstance).IsTrue();
        await Assert.That(saved.Revision).IsNotEqualTo(revision);
    }

    [Test]
    public async Task DedicatedServiceSave_RoundTripsDocumentAndRotatesRevisionUnderRealTransaction()
    {
        await using var scenario = await OperatorCommandScenario.CreateAsync();
        var first = await scenario.SaveAsync(OperatorCommandCandidate());
        await Assert.That(first.IsSuccess).IsTrue();
        var firstSaved = first.Id ?? throw new InvalidOperationException("Operator identity save returned no document.");
        await Assert.That(firstSaved.PublicDisclosure.IsReady).IsTrue();
        await Assert.That(firstSaved.PaidCommerce.IsReady).IsTrue();
        Guid revision = firstSaved.Revision;
        var document = await scenario.Service.GetCurrentAsync();
        await Assert.That(document.Settings).IsNotNull();
        await Assert.That(document.Settings!.OperatorId!.Value.Version).IsEqualTo(7);
        await Assert.That(document.Settings.Revision).IsEqualTo(revision);
        await Assert.That(document.Settings.PublicName).IsEqualTo("Independent Operator");
        await Assert.That(document.PublicDisclosure.IsReady).IsTrue();
        await Assert.That(document.PaidCommerce.IsReady).IsTrue();
        await Assert.That(document.PublicDisclosure.Identity).IsNotNull();
        await Assert.That(document.PublicDisclosure.DocumentRevision).IsEqualTo(revision);
        var second = await scenario.SaveAsync(OperatorCommandCandidate() with { PublicName = "Updated Operator" }, revision);
        await Assert.That(second.IsSuccess).IsTrue();
        var secondSaved = second.Id ?? throw new InvalidOperationException("Operator identity save returned no document.");
        await Assert.That(secondSaved.Revision).IsNotEqualTo(revision);
        await Assert.That(secondSaved.OperatorId).IsEqualTo(firstSaved.OperatorId);
        var updated = await scenario.Service.GetCurrentAsync();
        await Assert.That(updated.Settings!.Revision).IsEqualTo(secondSaved.Revision);
        await Assert.That(updated.Settings.PublicName).IsEqualTo("Updated Operator");
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => scenario.SaveAsync(OperatorCommandCandidate(), revision));
    }

    private static InstanceOperatorIdentitySettings ParseOperatorCommand(string json) =>
        JsonSerializer.Deserialize<InstanceOperatorIdentitySettings>(json, OperatorCommandJsonOptions)!;

    private static InstanceOperatorIdentitySettings OperatorCommandCandidate() => new()
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

    private sealed class OperatorCommandScenario(LocalIdentityLifecycleHttpFixture fixture, Request request)
        : IAsyncDisposable
    {
        internal InstanceOperatorIdentityService Service => request.Services.GetRequiredService<InstanceOperatorIdentityService>();
        internal static async Task<OperatorCommandScenario> CreateAsync()
        {
            var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
            try
            {
                await SeedPlatformAdministratorAsync(fixture);
                return new(fixture, await Request.CreateAsync(fixture));
            }
            catch { await fixture.DisposeAsync(); throw; }
        }

        internal Task<Explore.Application.Responses.BaseCommandResponse<InstanceOperatorIdentitySavedDocument>> SaveAsync(
            InstanceOperatorIdentitySettings payload, Guid? expectedRevision = null)
        {
            // Exercise the same DTO boundary as HTTP: client-owned server fields are ignored.
            var candidate = JsonSerializer.Deserialize<SaveInstanceOperatorIdentityRequestDto>(
                JsonSerializer.Serialize(payload, OperatorCommandJsonOptions), OperatorCommandJsonOptions)!;
            return Service.SaveAsync(new SaveInstanceOperatorIdentityCommand
            {
                Request = candidate with { ExpectedRevision = expectedRevision }
            });
        }

        internal async Task SeedAsync(InstanceOperatorIdentitySettings payload, bool completed = false)
        {
            await using var context = fixture.Native.CreateDatabase();
            context.SystemSettings.Add(new()
            {
                Id = Guid.CreateVersion7(),
                SettingKey = InstanceOperatorIdentitySettingKeys.OperatorIdentity,
                Value = JsonSerializer.Serialize(payload, OperatorCommandJsonOptions),
                ValueType = SettingValueType.Json
            });
            if (completed)
            {
                var bootstrap = await context.Set<InstanceBootstrapState>().SingleAsync();
                _ = bootstrap.CompleteInteractive(fixture.Binding.LocalSubjectId, fixture.Clock.GetUtcNow().UtcDateTime);
            }
            await context.SaveChangesAsync();
        }

        internal async Task<SystemSetting?> ReadStoredAsync()
        {
            await using var observer = fixture.Native.CreateDatabase();
            return await observer.SystemSettings.AsNoTracking().SingleOrDefaultAsync(
                row => row.SettingKey == InstanceOperatorIdentitySettingKeys.OperatorIdentity);
        }

        public async ValueTask DisposeAsync()
        {
            await request.DisposeAsync();
            await fixture.DisposeAsync();
        }
    }
}
