using System.Text.Json;
using System.Text.Json.Nodes;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Domain;

namespace Event.Application.UnitTests.Contracts;

public sealed class PrivacyErasureRequestContractTests
{
    [Test]
    public async Task PrivacyErasureRequest_AcceptsOnlyTypedUserErasureData()
    {
        PrivacyErasureRequest request = PrivacyErasureRequest.Create(
            Guid.CreateVersion7(),
            PrivacyErasureSubjectKind.User,
            Guid.CreateVersion7(),
            PrivacyErasureReasonCode.AccountDeletion,
            1);

        await Assert.That(request.IntentId.Version).IsEqualTo(7);
        await Assert.That(request.SubjectKind).IsEqualTo(PrivacyErasureSubjectKind.User);
        await Assert.That(request.SubjectId).IsNotEqualTo(Guid.Empty);
        await Assert.That(request.ReasonCode).IsEqualTo(PrivacyErasureReasonCode.AccountDeletion);
        await Assert.That(request.PolicyVersion).IsEqualTo(1);
        await Assert.That(typeof(PrivacyErasureRequest).GetProperties()
            .Any(property => property.SetMethod?.IsPublic == true)).IsFalse();
    }

    [Test]
    public async Task PrivacyErasureRequest_RejectsMalformedAndInstructionShapedData()
    {
        await Assert.That(() => PrivacyErasureRequest.Create(
                Guid.Empty,
                PrivacyErasureSubjectKind.User,
                Guid.CreateVersion7(),
                PrivacyErasureReasonCode.AccountDeletion,
                1))
            .Throws<ArgumentException>();
        await Assert.That(() => PrivacyErasureRequest.Create(
                Guid.CreateVersion7(),
                (PrivacyErasureSubjectKind)2,
                Guid.CreateVersion7(),
                PrivacyErasureReasonCode.AccountDeletion,
                1))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => PrivacyErasureRequest.Create(
                Guid.CreateVersion7(),
                PrivacyErasureSubjectKind.User,
                Guid.Empty,
                PrivacyErasureReasonCode.AccountDeletion,
                1))
            .Throws<ArgumentException>();
        await Assert.That(() => PrivacyErasureRequest.Create(
                Guid.CreateVersion7(),
                PrivacyErasureSubjectKind.User,
                Guid.CreateVersion7(),
                default,
                1))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => PrivacyErasureRequest.Create(
                Guid.CreateVersion7(),
                PrivacyErasureSubjectKind.User,
                Guid.CreateVersion7(),
                PrivacyErasureReasonCode.AccountDeletion,
                0))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => new PrivacyErasureRequest(
                Guid.Empty,
                PrivacyErasureSubjectKind.User,
                Guid.CreateVersion7(),
                PrivacyErasureReasonCode.AccountDeletion,
                1))
            .Throws<ArgumentException>();

        string[] forbidden =
        [
            "LocationIds", "Table", "Column", "Sql", "Json", "Metadata", "Instructions"
        ];
        await Assert.That(typeof(PrivacyErasureRequest).GetProperties()
            .Any(property => forbidden.Contains(property.Name, StringComparer.OrdinalIgnoreCase)))
            .IsFalse();
    }

    [Test]
    public async Task PrivacyErasureContractSurface_ValidFactIsBoundedAndInjectedInstructionsAreRejected()
    {
        PrivacyErasureRequest request = PrivacyErasureRequest.Create(
            Guid.CreateVersion7(),
            PrivacyErasureSubjectKind.User,
            Guid.CreateVersion7(),
            PrivacyErasureReasonCode.SubjectErasureRequest,
            3);
        string serialized = JsonSerializer.Serialize(request);
        PrivacyErasureRequest? rebound = JsonSerializer.Deserialize<PrivacyErasureRequest>(serialized);

        await Assert.That(rebound).IsNotNull();
        await Assert.That(rebound!.IntentId).IsEqualTo(request.IntentId);
        await Assert.That(rebound.SubjectKind).IsEqualTo(request.SubjectKind);
        await Assert.That(rebound.SubjectId).IsEqualTo(request.SubjectId);
        await Assert.That(rebound.ReasonCode).IsEqualTo(request.ReasonCode);
        await Assert.That(rebound.PolicyVersion).IsEqualTo(request.PolicyVersion);

        (string Name, JsonNode Value)[] forbiddenMembers =
        [
            ("table", JsonValue.Create("users")),
            ("column", JsonValue.Create("email")),
            ("json", JsonNode.Parse("""{"action":"delete"}""")!)
        ];

        foreach ((string name, JsonNode value) in forbiddenMembers)
        {
            JsonObject injected = JsonNode.Parse(serialized)!.AsObject();
            injected[name] = value.DeepClone();

            JsonException exception = DeserializeExpectingUnmappedMember(injected.ToJsonString());

            await Assert.That(exception.Message).Contains(name);
        }

        Console.WriteLine(
            $"valid_binding=success shape=IntentId,PolicyVersion,ReasonCode,SubjectId,SubjectKind unmapped_rejection=column,json,table");
    }

    private static JsonException DeserializeExpectingUnmappedMember(string json)
    {
        try
        {
            _ = JsonSerializer.Deserialize<PrivacyErasureRequest>(json);
        }
        catch (JsonException exception)
        {
            return exception;
        }

        throw new InvalidOperationException("An unmapped JSON member was accepted.");
    }
}
