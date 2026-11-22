using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Domain.Constants;
using Explore.Persistence;
using ISLAMU.Wire.Contracts.Announcements;
using Microsoft.EntityFrameworkCore;

namespace Event.Api.IntegrationTests.Authentication;

public sealed partial class HumanSettingsMutationPreparationTests
{
    [Test]
    public async Task AnnouncementHttpDismissalIsIdempotentAndGenericResetCannotRemoveItsMarker()
    {
        var token = LocalIdentityLifecycleHttpFixture.CancellationToken;
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await SeedMixedTenantAdministratorAsync(fixture);
        fixture.Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await fixture.SignInAsync());
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        json.Converters.Add(new JsonStringEnumConverter());

        using var sourceResponse = await fixture.Client.GetAsync("/api/announcements/tenant", token);
        var sourceBody = await ReadAnnouncementLifecycleHalAsync(sourceResponse, token);
        var source = sourceBody.GetProperty("source").Deserialize<AnnouncementSourceDto>(json)
            ?? throw new InvalidOperationException("The own-source read must contain its typed source.");
        await Assert.That(source.Owner.TenantId).IsEqualTo(PlatformDefaults.DefaultTenantId);
        var material = new AnnouncementMaterialDto
        {
            Message = "Tenant announcement dismissal lifecycle",
            LinkText = "Browse events",
            LinkUrl = "/events"
        };
        using var publicationResponse = await fixture.Client.PutAsJsonAsync(
            AnnouncementLifecycleLink(sourceBody, "publish"), new AnnouncementUpdateDto
            {
                Change = new() { Kind = "publish", Material = material, Enabled = true, Redisplay = true },
                ExpectedSource = source
            }, token);
        var publication = await ReadAnnouncementLifecycleHalAsync(publicationResponse, token);
        await Assert.That(publication.GetProperty("outcome").Deserialize<AnnouncementPublicationOutcomeDto>(json))
            .IsEqualTo(AnnouncementPublicationOutcomeDto.Applied);
        await AssertAnnouncementLifecycleReceiptHasNoMutationAsync(publication);
        var publishedSource = publication.GetProperty("source").Deserialize<AnnouncementSourceDto>(json)
            ?? throw new InvalidOperationException("Publication must settle with its source.");
        var published = publishedSource.Group?.Publication
            ?? throw new InvalidOperationException("A complete publication is required.");
        await Assert.That(published.Material).IsEqualTo(material);

        using var selfResponse = await fixture.Client.GetAsync("/api/announcements/self", token);
        var self = await ReadAnnouncementLifecycleHalAsync(selfResponse, token);
        await Assert.That(self.GetProperty("tenantId").GetGuid()).IsEqualTo(PlatformDefaults.DefaultTenantId);
        await Assert.That(self.GetProperty("marker").ValueKind).IsEqualTo(JsonValueKind.Null);
        var observed = self.GetProperty("currentObservation").Deserialize<AnnouncementObservationDto>(json)
            ?? throw new InvalidOperationException("The published tenant material must be observable.");
        await Assert.That(observed.TenantId).IsEqualTo(PlatformDefaults.DefaultTenantId);
        await Assert.That(observed.Owner).IsEqualTo(source.Owner);
        await Assert.That(observed.Edition).IsEqualTo(published.Edition);
        await Assert.That(observed.SourceWitness.TenantSelectionEpoch)
            .IsEqualTo(publishedSource.Group!.SelectionEpoch);
        string dismissHref = AnnouncementLifecycleLink(self, "dismiss");
        var dismissal = new AnnouncementDismissRequestDto { Observed = observed, ExpectedMarker = null };
        using var dismissResponse = await fixture.Client.PostAsJsonAsync(dismissHref, dismissal, token);
        var receipt = await ReadAnnouncementLifecycleHalAsync(dismissResponse, token);
        await Assert.That(receipt.GetProperty("decision").Deserialize<AnnouncementDismissalDecisionDto>(json))
            .IsEqualTo(AnnouncementDismissalDecisionDto.Apply);
        await AssertAnnouncementLifecycleReceiptHasNoMutationAsync(receipt);
        var marker = receipt.GetProperty("marker").Deserialize<AnnouncementDismissalMarkerDto>(json)
            ?? throw new InvalidOperationException("A settled dismissal requires its marker.");
        await Assert.That(marker.Observation).IsEqualTo(observed);

        using var persistedResponse = await fixture.Client.GetAsync(AnnouncementLifecycleLink(receipt, "self"), token);
        var persisted = await ReadAnnouncementLifecycleHalAsync(persistedResponse, token);
        await Assert.That(persisted.GetProperty("marker").Deserialize<AnnouncementDismissalMarkerDto>(json))
            .IsEqualTo(marker);
        await AssertAnnouncementLifecycleReceiptHasNoMutationAsync(persisted);

        // Reuse the original attempt, including expected absence, rather than overwriting the marker.
        fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        await using var beforeRepeatContext = fixture.Native.CreateDatabase();
        beforeRepeatContext.TenantContext = new SqliteGovernanceTenant(PlatformDefaults.DefaultTenantId);
        beforeRepeatContext.BindTenantLease();
        var beforeRepeat = await beforeRepeatContext.UserPreferences.AsNoTracking()
            .Where(row => row.TenantId == PlatformDefaults.DefaultTenantId
                && row.UserId == fixture.Binding.LocalSubjectId
                && row.SettingKey == "public_experience_preferences.announcement_bar.dismissed_edition")
            .Select(row => new { row.Id, row.Value, row.CreatedAt, row.CreatedBy, row.UpdatedAt, row.UpdatedBy })
            .SingleAsync(token);
        using var repeatResponse = await fixture.Client.PostAsJsonAsync(dismissHref, dismissal, token);
        var repeat = await ReadAnnouncementLifecycleHalAsync(repeatResponse, token);
        await using var afterRepeatContext = fixture.Native.CreateDatabase();
        afterRepeatContext.TenantContext = new SqliteGovernanceTenant(PlatformDefaults.DefaultTenantId);
        afterRepeatContext.BindTenantLease();
        var afterRepeat = await afterRepeatContext.UserPreferences.AsNoTracking()
            .Where(row => row.TenantId == PlatformDefaults.DefaultTenantId
                && row.UserId == fixture.Binding.LocalSubjectId
                && row.SettingKey == "public_experience_preferences.announcement_bar.dismissed_edition")
            .Select(row => new { row.Id, row.Value, row.CreatedAt, row.CreatedBy, row.UpdatedAt, row.UpdatedBy })
            .SingleAsync(token);
        await Assert.That(afterRepeat.Id).IsEqualTo(beforeRepeat.Id);
        await Assert.That(afterRepeat.Value).IsEqualTo(beforeRepeat.Value);
        await Assert.That(afterRepeat.CreatedAt).IsEqualTo(beforeRepeat.CreatedAt);
        await Assert.That(afterRepeat.CreatedBy).IsEqualTo(beforeRepeat.CreatedBy);
        await Assert.That(afterRepeat.UpdatedAt).IsEqualTo(beforeRepeat.UpdatedAt);
        await Assert.That(afterRepeat.UpdatedBy).IsEqualTo(beforeRepeat.UpdatedBy);
        await Assert.That(repeat.GetProperty("decision").Deserialize<AnnouncementDismissalDecisionDto>(json))
            .IsEqualTo(AnnouncementDismissalDecisionDto.AlreadyDismissed);
        await Assert.That(repeat.GetProperty("marker").Deserialize<AnnouncementDismissalMarkerDto>(json))
            .IsEqualTo(marker);
        await AssertAnnouncementLifecycleReceiptHasNoMutationAsync(repeat);
        using var repeatedSelfResponse = await fixture.Client.GetAsync(AnnouncementLifecycleLink(repeat, "self"), token);
        var repeatedSelf = await ReadAnnouncementLifecycleHalAsync(repeatedSelfResponse, token);
        await Assert.That(repeatedSelf.GetProperty("marker").Deserialize<AnnouncementDismissalMarkerDto>(json))
            .IsEqualTo(marker);
        await AssertAnnouncementLifecycleReceiptHasNoMutationAsync(repeatedSelf);

        using var resetResponse = await fixture.Client.DeleteAsync(
            "/api/settings/user/keys/public_experience_preferences.announcement_bar.dismissed_edition",
            token);
        await Assert.That(resetResponse.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        using var afterResetResponse = await fixture.Client.GetAsync(
            AnnouncementLifecycleLink(repeatedSelf, "self"), token);
        var afterReset = await ReadAnnouncementLifecycleHalAsync(afterResetResponse, token);
        await Assert.That(afterReset.GetProperty("marker").Deserialize<AnnouncementDismissalMarkerDto>(json))
            .IsEqualTo(marker);
        await AssertAnnouncementLifecycleReceiptHasNoMutationAsync(afterReset);
    }

    [Test]
    public async Task AnnouncementHttpDismissalRejectsSameEditionSourceAbaAndAcceptsTheFreshWitness()
    {
        var token = LocalIdentityLifecycleHttpFixture.CancellationToken;
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await SeedMixedTenantAdministratorAsync(fixture);
        fixture.Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await fixture.SignInAsync());
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        json.Converters.Add(new JsonStringEnumConverter());

        using var sourceResponse = await fixture.Client.GetAsync("/api/announcements/tenant", token);
        var sourceBody = await ReadAnnouncementLifecycleHalAsync(sourceResponse, token);
        var source = sourceBody.GetProperty("source").Deserialize<AnnouncementSourceDto>(json)
            ?? throw new InvalidOperationException("The own-source read must contain its typed source.");
        await Assert.That(source.Owner.TenantId).IsEqualTo(PlatformDefaults.DefaultTenantId);
        using var publicationResponse = await fixture.Client.PutAsJsonAsync(
            AnnouncementLifecycleLink(sourceBody, "publish"), new AnnouncementUpdateDto
            {
                Change = new()
                {
                    Kind = "publish",
                    Material = new()
                    {
                        Message = "Tenant announcement source witness lifecycle",
                        LinkText = "Browse events",
                        LinkUrl = "/events"
                    },
                    Enabled = true,
                    Redisplay = true
                },
                ExpectedSource = source
            }, token);
        var publication = await ReadAnnouncementLifecycleHalAsync(publicationResponse, token);
        await Assert.That(publication.GetProperty("outcome").Deserialize<AnnouncementPublicationOutcomeDto>(json))
            .IsEqualTo(AnnouncementPublicationOutcomeDto.Applied);
        await AssertAnnouncementLifecycleReceiptHasNoMutationAsync(publication);
        var publishedSource = publication.GetProperty("source").Deserialize<AnnouncementSourceDto>(json)
            ?? throw new InvalidOperationException("Publication must settle with its source.");
        var originalGroup = publishedSource.Group
            ?? throw new InvalidOperationException("Publication must establish its source group.");
        var published = originalGroup.Publication
            ?? throw new InvalidOperationException("A complete publication is required.");

        using var originalSelfResponse = await fixture.Client.GetAsync("/api/announcements/self", token);
        var originalSelf = await ReadAnnouncementLifecycleHalAsync(originalSelfResponse, token);
        await Assert.That(originalSelf.GetProperty("marker").ValueKind).IsEqualTo(JsonValueKind.Null);
        var original = originalSelf.GetProperty("currentObservation").Deserialize<AnnouncementObservationDto>(json)
            ?? throw new InvalidOperationException("The original publication must be observable.");
        await Assert.That(original.TenantId).IsEqualTo(PlatformDefaults.DefaultTenantId);
        await Assert.That(original.Owner).IsEqualTo(source.Owner);
        await Assert.That(original.Edition).IsEqualTo(published.Edition);
        await Assert.That(original.SourceWitness.TenantSelectionEpoch).IsEqualTo(originalGroup.SelectionEpoch);
        string originalDismissHref = AnnouncementLifecycleLink(originalSelf, "dismiss");
        Guid previousEpoch = originalGroup.SelectionEpoch;
        foreach (bool enabled in new[] { false, true })
        {
            using var currentSourceResponse = await fixture.Client.GetAsync("/api/announcements/tenant", token);
            var currentSourceBody = await ReadAnnouncementLifecycleHalAsync(currentSourceResponse, token);
            var currentSource = currentSourceBody.GetProperty("source").Deserialize<AnnouncementSourceDto>(json)
                ?? throw new InvalidOperationException("Each enable intent requires a fresh original source.");
            using var toggleResponse = await fixture.Client.PutAsJsonAsync(
                AnnouncementLifecycleLink(currentSourceBody, "publish"), new AnnouncementUpdateDto
                {
                    Change = new() { Kind = "set-enabled", Enabled = enabled },
                    ExpectedSource = currentSource
                }, token);
            var toggle = await ReadAnnouncementLifecycleHalAsync(toggleResponse, token);
            await Assert.That(toggle.GetProperty("outcome").Deserialize<AnnouncementPublicationOutcomeDto>(json))
                .IsEqualTo(AnnouncementPublicationOutcomeDto.Applied);
            await AssertAnnouncementLifecycleReceiptHasNoMutationAsync(toggle);
            var toggledSource = toggle.GetProperty("source").Deserialize<AnnouncementSourceDto>(json)
                ?? throw new InvalidOperationException("Enable settlement requires its source.");
            var toggledGroup = toggledSource.Group
                ?? throw new InvalidOperationException("Enable changes must retain the publication group.");
            await Assert.That(toggledSource.Owner).IsEqualTo(source.Owner);
            await Assert.That(toggledGroup.Enabled).IsEqualTo(enabled);
            await Assert.That(toggledGroup.Publication).IsEqualTo(published);
            await Assert.That(toggledGroup.SelectionEpoch).IsNotEqualTo(previousEpoch);
            await Assert.That(toggledGroup.SelectionEpoch).IsNotEqualTo(originalGroup.SelectionEpoch);
            previousEpoch = toggledGroup.SelectionEpoch;
        }

        using var staleResponse = await fixture.Client.PostAsJsonAsync(originalDismissHref,
            new AnnouncementDismissRequestDto { Observed = original, ExpectedMarker = null }, token);
        await Assert.That(staleResponse.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        using var staleProblem = JsonDocument.Parse(await staleResponse.Content.ReadAsStringAsync(token));
        await Assert.That(staleProblem.RootElement.GetProperty("code").GetString())
            .IsEqualTo("announcement_source_changed");

        using var freshSelfResponse = await fixture.Client.GetAsync(AnnouncementLifecycleLink(originalSelf, "self"), token);
        var freshSelf = await ReadAnnouncementLifecycleHalAsync(freshSelfResponse, token);
        await Assert.That(freshSelf.GetProperty("tenantId").GetGuid()).IsEqualTo(PlatformDefaults.DefaultTenantId);
        await Assert.That(freshSelf.GetProperty("marker").ValueKind).IsEqualTo(JsonValueKind.Null);
        var fresh = freshSelf.GetProperty("currentObservation").Deserialize<AnnouncementObservationDto>(json)
            ?? throw new InvalidOperationException("Re-enabling must expose the same material with a fresh witness.");
        await Assert.That(fresh.TenantId).IsEqualTo(original.TenantId);
        await Assert.That(fresh.Owner).IsEqualTo(original.Owner);
        await Assert.That(fresh.Edition).IsEqualTo(original.Edition);
        await Assert.That(fresh.SourceWitness.TenantSelectionEpoch).IsEqualTo(previousEpoch);
        await Assert.That(fresh.SourceWitness).IsNotEqualTo(original.SourceWitness);
        using var freshDismissResponse = await fixture.Client.PostAsJsonAsync(
            AnnouncementLifecycleLink(freshSelf, "dismiss"),
            new AnnouncementDismissRequestDto { Observed = fresh, ExpectedMarker = null }, token);
        var freshReceipt = await ReadAnnouncementLifecycleHalAsync(freshDismissResponse, token);
        await Assert.That(freshReceipt.GetProperty("decision").Deserialize<AnnouncementDismissalDecisionDto>(json))
            .IsEqualTo(AnnouncementDismissalDecisionDto.Apply);
        await AssertAnnouncementLifecycleReceiptHasNoMutationAsync(freshReceipt);
        var marker = freshReceipt.GetProperty("marker").Deserialize<AnnouncementDismissalMarkerDto>(json)
            ?? throw new InvalidOperationException("The fresh dismissal must settle with a marker.");
        await Assert.That(marker.Observation).IsEqualTo(fresh);
        using var persistedResponse = await fixture.Client.GetAsync(AnnouncementLifecycleLink(freshReceipt, "self"), token);
        var persisted = await ReadAnnouncementLifecycleHalAsync(persistedResponse, token);
        await Assert.That(persisted.GetProperty("marker").Deserialize<AnnouncementDismissalMarkerDto>(json))
            .IsEqualTo(marker);
        await AssertAnnouncementLifecycleReceiptHasNoMutationAsync(persisted);
    }

    private static async Task<JsonElement> ReadAnnouncementLifecycleHalAsync(
        HttpResponseMessage response, CancellationToken token)
    {
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        return document.RootElement.Clone();
    }

    private static string AnnouncementLifecycleLink(JsonElement body, string relation) =>
        body.GetProperty("_links").GetProperty(relation).GetProperty("href").GetString()
        ?? throw new InvalidOperationException($"The announcement {relation} relation requires an href.");

    private static async Task AssertAnnouncementLifecycleReceiptHasNoMutationAsync(JsonElement body)
    {
        if (!body.TryGetProperty("_links", out var links))
            return;
        await Assert.That(links.TryGetProperty("publish", out _)).IsFalse();
        await Assert.That(links.TryGetProperty("dismiss", out _)).IsFalse();
    }
}
