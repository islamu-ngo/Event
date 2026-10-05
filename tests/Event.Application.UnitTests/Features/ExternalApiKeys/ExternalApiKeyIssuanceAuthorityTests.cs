using System.Text.Json;
using Explore.Application.Features.ExternalApiKeys.Requests.Commands;
using Explore.Application.Responses;

namespace Event.Application.UnitTests.Features.ExternalApiKeys;

public sealed class ExternalApiKeyIssuanceAuthorityTests
{
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

    [Test]
    public async Task MetadataRecovery_PreservesExplicitPreviouslyIssuedOutcome()
    {
        Guid keyId = Guid.CreateVersion7();
        string input = $$"""
            {
                "id":"{{keyId}}",
                "success":true,
                "message":null,
                "errors":null,
                "failureCode":null,
                "quotaExceeded":null,
                "apiKey":null,
                "keyId":"public-key-id",
                "disclosureStatus":"PreviouslyIssued"
            }
            """;
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var response = JsonSerializer.Deserialize<CreateExternalApiKeyCommandResponse>(input, options)!;
        var restored = JsonSerializer.SerializeToElement(response, options);
        bool hasRecoveryOutcome = restored.TryGetProperty("disclosureStatus", out var status)
            && status.GetString() == "PreviouslyIssued";
        await Assert.That(hasRecoveryOutcome).IsTrue();
        await Assert.That(response.Id).IsEqualTo(keyId);
        await Assert.That(response.ApiKey is null).IsTrue();
    }
}
