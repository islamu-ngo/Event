using System.Reflection;
using System.Text.Json;
using Explore.API.Attributes;
using Explore.API.Controllers;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.User;
using Microsoft.AspNetCore.Authorization;
using ISLAMU.Moderation.Domain;

namespace Event.API.IntegrationTests.Features;

public sealed class PrivateProfileCompletionContractTests
{
    [Test]
    public async Task BirthDateStatusDoesNotReturnADeclarationAndWriteRejectsCallerAuthority()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        using var status = JsonDocument.Parse(JsonSerializer.Serialize(new PrivateBirthDateStatus(2, true), options));
        await Assert.That(status.RootElement.EnumerateObject().Select(property => property.Name).Order().ToArray())
            .IsEquivalentTo(new[] { "hasDeclaration", "revision" });
        await Assert.That(() => JsonSerializer.Deserialize<RecordPrivateBirthDateDto>(
            "{\"birthDate\":\"2000-02-29\",\"expectedRevision\":0,\"isQualified\":true}", options)).Throws<JsonException>();
    }

    [Test]
    public async Task ErasureDoesNotRequireTheCollectionAdmissionButRecordingDoes()
    {
        var record = typeof(UserProfileCompletionController).GetMethod(nameof(UserProfileCompletionController.RecordBirthDate))!;
        var clear = typeof(UserProfileCompletionController).GetMethod(nameof(UserProfileCompletionController.ClearBirthDate))!;
        await Assert.That(record.GetCustomAttribute<ProfileCollectionAdmissionAttribute>()).IsNotNull();
        await Assert.That(clear.GetCustomAttribute<ProfileCollectionAdmissionAttribute>()).IsNull();
        await Assert.That(record.GetCustomAttribute<AuthorizeAttribute>()).IsNotNull();
        await Assert.That(clear.GetCustomAttribute<AuthorizeAttribute>()).IsNotNull();
    }

    [Test]
    public async Task DeclaredThresholdExposesNeitherBirthDateNorReconstructiveProvenance()
    {
        var now = DateTimeOffset.UtcNow;
        var display = new ModerationDeclaredAgeDisplay(ModerationAgeEvidenceKind.Declared,
            ModerationDeclaredAgeOutcome.AtLeastThreshold, 18, 2, now, now.AddMinutes(1));
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(display, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        await Assert.That(json.RootElement.EnumerateObject().Select(property => property.Name).Order().ToArray())
            .IsEquivalentTo(new[] { "kind", "outcome", "minimumAge", "birthDateRevision", "evaluatedAt", "validUntil" });
        await Assert.That(display.Kind).IsNotEqualTo(ModerationAgeEvidenceKind.Assured);
    }
}
