using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using Explore.Application.DTOs.ExternalApiKey;
using Explore.Application.Models;
using Explore.Application.Features.ExternalApiKeys.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Serialization;

namespace Event.Application.UnitTests.Features.ExternalApiKeys;

/// <summary>
/// Verifies native operation identity and the valid-state success payload published across process boundaries.
/// </summary>
public sealed class ExternalApiKeyIssuanceAuthorityTests
{
    /// <summary>Preserves the case-sensitive operation key through native command serialization.</summary>
    [Test]
    public async Task NativeCommand_PreservesItsOperationIdentityThroughSerialization()
    {
        var operationKey = Guid.CreateVersion7().ToString("N");
        string input = $$"""
            {
                "OperationKey":"{{operationKey}}",
                "ExternalApiKeyDto":{
                    "Name":"Personal automation",
                    "ExternalApiKeyOwnerTypeId":1,
                    "Scopes":["events:read"]
                }
            }
            """;
        var command = JsonSerializer.Deserialize<CreateExternalApiKeyCommand>(input)!;
        var restored = JsonSerializer.SerializeToElement(command);
        bool hasOperationIdentity = restored.TryGetProperty("OperationKey", out var identity)
            && identity.GetString() == operationKey;
        await Assert.That(hasOperationIdentity).IsTrue();
    }

    /// <summary>Retains explicit null credential material in source-generated metadata-recovery JSON.</summary>
    [Test]
    public async Task MetadataRecovery_PreservesExplicitPreviouslyIssuedOutcome()
    {
        Guid keyId = Guid.CreateVersion7();
        string input = $$"""
            {
                "id":"{{keyId}}",
                "apiKey":null,
                "keyId":"public-key-id",
                "disclosureStatus":"PreviouslyIssued"
            }
            """;
        var response = JsonSerializer.Deserialize(input, ExploreJsonContext.Default.ExternalApiKeyIssuanceDto)!;
        var restored = JsonSerializer.SerializeToElement(response, ExploreJsonContext.Default.ExternalApiKeyIssuanceDto);
        bool hasRecoveryOutcome = restored.TryGetProperty("disclosureStatus", out var status)
            && status.GetString() == "PreviouslyIssued";
        await Assert.That(hasRecoveryOutcome).IsTrue();
        await Assert.That(response.Id).IsEqualTo(keyId);
        await Assert.That(response.ApiKey is null).IsTrue();
        await Assert.That(restored.GetProperty("apiKey").ValueKind).IsEqualTo(JsonValueKind.Null);
    }

    /// <summary>Native failure has no success payload or fabricated disclosure outcome.</summary>
    [Test]
    public async Task NativeFailureContainsNoIssuancePayload()
    {
        var response = CreateExternalApiKeyCommandResponse.Failure(
            BaseCommandResponse.Validation<Guid>(["invalid.operation"]));

        await Assert.That(response.IsSuccess).IsFalse();
        await Assert.That(response.Issue is null).IsTrue();
    }

    /// <summary>Issuer diagnostics cannot traverse raw material in either payload or native wrapper.</summary>
    [Test]
    public async Task IssuedPayloadAndNativeDiagnosticsNeverRenderCredentialMaterial()
    {
        string raw = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        Guid id = Guid.CreateVersion7();
        var response = CreateExternalApiKeyCommandResponse.Issued(id, null, raw, "public-key-id");
        var issue = response.Issue!;
        var restored = JsonSerializer.Deserialize(
            JsonSerializer.Serialize(issue, ExploreJsonContext.Default.ExternalApiKeyIssuanceDto),
            ExploreJsonContext.Default.ExternalApiKeyIssuanceDto)!;

        await Assert.That(response.Id).IsEqualTo(issue.Id);
        await Assert.That(issue.DisclosureStatus).IsEqualTo(ExternalApiKeyDisclosureStatus.Issued);
        await Assert.That(restored.ApiKey == raw).IsTrue();
        await Assert.That(response.ToString().Contains(raw, StringComparison.Ordinal)).IsFalse();
        await Assert.That(issue.ToString().Contains(raw, StringComparison.Ordinal)).IsFalse();
    }

    /// <summary>Required wire fields cannot silently default into an acknowledged success state.</summary>
    [Test]
    [Arguments("id")]
    [Arguments("keyId")]
    [Arguments("disclosureStatus")]
    public async Task SuccessPayloadRejectsMissingRequiredWireField(string property)
    {
        var issue = ExternalApiKeyIssuanceDto.PreviouslyIssued(Guid.CreateVersion7(), "public-key-id");
        JsonObject wire = JsonSerializer.SerializeToNode(
            issue, ExploreJsonContext.Default.ExternalApiKeyIssuanceDto)!.AsObject();
        wire.Remove(property);

        await Assert.That(() => JsonSerializer.Deserialize(
            wire.ToJsonString(), ExploreJsonContext.Default.ExternalApiKeyIssuanceDto))
            .Throws<ArgumentException>();
    }

    /// <summary>A null or unknown disclosure token cannot pass the success-only JSON boundary.</summary>
    [Test]
    [Arguments(null)]
    [Arguments("Unknown")]
    public async Task SuccessPayloadRejectsInvalidDisclosureWireValue(string? disclosureStatus)
    {
        var issue = ExternalApiKeyIssuanceDto.PreviouslyIssued(Guid.CreateVersion7(), "public-key-id");
        JsonObject wire = JsonSerializer.SerializeToNode(
            issue, ExploreJsonContext.Default.ExternalApiKeyIssuanceDto)!.AsObject();
        wire["disclosureStatus"] = disclosureStatus;

        await Assert.That(() => JsonSerializer.Deserialize(
            wire.ToJsonString(), ExploreJsonContext.Default.ExternalApiKeyIssuanceDto))
            .Throws<JsonException>();
    }

    /// <summary>Unknown CLR enum values and contradictory secret/disclosure pairs fail controlled construction.</summary>
    [Test]
    public async Task SuccessPayloadRejectsInvalidConstructionStates()
    {
        Guid id = Guid.CreateVersion7();
        string raw = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        await Assert.That(() => ExternalApiKeyIssuanceDto.Issued(Guid.Empty, "public-key-id", raw))
            .Throws<ArgumentException>();
        await Assert.That(() => ExternalApiKeyIssuanceDto.Issued(id, "", raw))
            .Throws<ArgumentException>();
        await Assert.That(() => ExternalApiKeyIssuanceDto.Issued(id, "public-key-id", " "))
            .Throws<ArgumentException>();
        await Assert.That(() => new ExternalApiKeyIssuanceDto(id, "public-key-id", (ExternalApiKeyDisclosureStatus)99, null))
            .Throws<ArgumentException>();
        await Assert.That(() => new ExternalApiKeyIssuanceDto(id, "public-key-id", ExternalApiKeyDisclosureStatus.PreviouslyIssued, raw))
            .Throws<ArgumentException>();
    }
}
