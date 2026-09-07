using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;

namespace Event.Domain.UnitTests.Entities;

public sealed class AtprotoIdentityDidBoundaryTests
{
    private static readonly DateTime FixedTimestamp = new(2026, 8, 31, 10, 0, 0, DateTimeKind.Utc);

    [Test]
    [Arguments("did:plc:z72i7hdynmk6r22z27h6tvur")]
    [Arguments("did:web:events.example.org")]
    [Arguments("did:web:sub.domain.example.com:user:alice")]
    [Arguments("did:custom:arbitrary-valid-method-spec-123")]
    public async Task LiveDid_ParsesAndPreservesExactCaseSensitiveValue(string validDid)
    {
        var did = AtprotoDid.Parse(validDid);

        await Assert.That(did.Value).IsEqualTo(validDid);
        await Assert.That(did.ToString()).IsEqualTo("[AT Protocol DID]");
    }

    [Test]
    [Arguments("DID:plc:uppercase-scheme")]
    [Arguments("did:PLC:uppercase-method")]
    [Arguments("did:plc:has whitespace")]
    [Arguments("did:plc:has\ncontrol")]
    [Arguments("did:plc:has?query=1")]
    [Arguments("did:plc:has#fragment")]
    [Arguments("did:plc:percent%20encoding")]
    [Arguments("did:plc:trailing-colon:")]
    public async Task MalformedDid_RejectsAtIngress(string malformedDid)
    {
        await Assert.That(() => AtprotoDid.Parse(malformedDid)).Throws<ArgumentException>();
        await Assert.That(AtprotoDid.TryParse(malformedDid, out _)).IsFalse();
    }

    [Test]
    public async Task OversizedDid_Above2048Chars_RejectsAtIngress()
    {
        var oversized = "did:plc:" + new string('x', 2050);

        await Assert.That(() => AtprotoDid.Parse(oversized)).Throws<ArgumentException>();
        await Assert.That(AtprotoDid.TryParse(oversized, out _)).IsFalse();
    }

    [Test]
    public async Task PrivacyErasureTombstone_IsNotLiveDid()
    {
        var tombstone = $"did:deleted:{Guid.CreateVersion7():N}";

        await Assert.That(() => AtprotoDid.Parse(tombstone)).Throws<ArgumentException>();
        await Assert.That(AtprotoDid.TryParse(tombstone, out _)).IsFalse();
    }

    [Test]
    public async Task AtprotoIdentity_DidHasNoPublicSetter()
    {
        var didProperty = typeof(AtprotoIdentity).GetProperty(nameof(AtprotoIdentity.Did));

        await Assert.That(didProperty).IsNotNull();
        await Assert.That(didProperty!.SetMethod?.IsPublic == true).IsFalse();
        await Assert.That(typeof(AtprotoIdentity).GetConstructors()
            .Any(constructor => constructor.GetParameters() is [{ ParameterType: var parameterType }]
                && parameterType == typeof(string))).IsFalse();
    }

    [Test]
    public async Task AtprotoIdentity_RefreshVerifiedMetadata_AcceptsOnlyValidLiveDid()
    {
        var actorId = Guid.CreateVersion7();
        var identity = new AtprotoIdentity(AtprotoDid.Parse("did:plc:z72i7hdynmk6r22z27h6tvur"))
        {
            Id = Guid.CreateVersion7(),

            ActorId = actorId,
            Actor = new Actor
            {
                Id = actorId,
                ActorTypeId = (int)ActorTypeEnum.User,
                ActorType = new ActorType { Id = (int)ActorTypeEnum.User, FullName = "User", MasterCode = "USER" },
                Pii = new ActorPii { ActorId = actorId, DisplayName = "Test User" }
            },
            PdsHost = "https://pds.example",
            IsActive = true,
            ConcurrencyStamp = Guid.CreateVersion7()
        };

        var matchingDid = AtprotoDid.Parse("did:plc:z72i7hdynmk6r22z27h6tvur");
        identity.RefreshVerifiedMetadata(matchingDid, "handle.example.com", "https://pds.example", "key", FixedTimestamp);

        await Assert.That(identity.Handle).IsEqualTo("handle.example.com");
    }
}
