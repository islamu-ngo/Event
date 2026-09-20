using System.Text.Json;
using System.Text.Json.Nodes;
using Explore.Application.DTOs.User;
using Explore.Application.DTOs.UserAuthenticationToken;
using Explore.Application.Mappings;
using Explore.Domain;
using Explore.Domain.ValueObjects;

namespace Event.Application.UnitTests.Profiles;

public sealed class UserMapperTests
{
    private static readonly Guid UserId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000031");
    private static readonly Guid ActorId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000032");
    private static readonly Guid Stamp = Guid.Parse("018e4e5c-7f00-7000-8000-000000000033");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Test]
    public async Task User_ProjectsOnlyDisclosedScalars_FromCyclicIdentityGraph()
    {
        var source = CreateUser();
        var result = Detail(source);
        source.Pii.Email = "changed@example.invalid";
        source.Actor!.Pii.DisplayName = "Changed";
        source.Actor.AtprotoIdentities.Clear();
        await AssertJson(result, """
            {"id":"018e4e5c-7f00-7000-8000-000000000031","email":"private@example.invalid",
             "firstName":"First","lastName":"Last","actorId":"018e4e5c-7f00-7000-8000-000000000032",
             "actorDisplayName":"Public name","actorHandle":"first.example.invalid",
             "actorBackgroundColor":"","actorBackgroundEffect":"stars","actorBannerColor":"blue",
             "actorBannerPictureId":null,"actorBannerPictureUri":null,"actorBackgroundImageId":null,"actorBackgroundImageUri":null,
             "authProvider":null,"authProviderId":null,"emailVerified":false,
             "concurrencyStamp":"018e4e5c-7f00-7000-8000-000000000033","profileImageKey":null,"profileImageUri":null}
            """);
    }

    [Test]
    public async Task User_MissingActor_PreservesNullAppearanceAndEmptyActorId()
    {
        var source = CreateUser();
        source.Actor = null;
        source.EmailVerified = null;
        var result = Detail(source);
        await Assert.That(result.ActorId).IsEqualTo(Guid.Empty);
        await Assert.That(result.ActorDisplayName).IsNull();
        await Assert.That(result.ActorHandle).IsNull();
        await Assert.That(result.ActorBackgroundColor).IsNull();
        await Assert.That(result.ActorBackgroundEffect).IsNull();
        await Assert.That(result.ActorBannerColor).IsNull();
        await Assert.That(result.EmailVerified).IsNull();
        await Assert.That(result.Email).IsEqualTo("private@example.invalid");
    }

    [Test]
    public async Task User_UnloadedActorPii_PreservesNullRatherThanInventingIdentity()
    {
        var source = CreateUser();
        source.Actor!.Pii = null!;
        source.Actor.AtprotoIdentities.Clear();
        var result = Detail(source);
        await Assert.That(result.Email).IsEqualTo("private@example.invalid");
        await Assert.That(result.ActorDisplayName).IsNull();
        await Assert.That(result.ActorHandle).IsNull();
    }

    [Test]
    public async Task User_FirstHandleCanBeNull_WithoutSelectingAnotherIdentity()
    {
        var source = CreateUser();
        source.Actor!.AtprotoIdentities.First().Handle = null;
        await Assert.That(Detail(source).ActorHandle).IsNull();
        source.Actor.AtprotoIdentities.First().Handle = "";
        await Assert.That(Detail(source).ActorHandle).IsEqualTo("");
    }

    [Test]
    public async Task TokenDetail_OmitsCredentialEnvelopeAndOwnership()
    {
        await AssertJson(TokenDetail(CreateToken()), """
            {"id":"018e4e5c-7f00-7000-8000-000000000033","provider":"atproto",
             "pdsHost":"https://pds.example.invalid","expiresAt":"2026-09-12T10:00:00Z"}
            """);
    }

    [Test]
    public async Task TokenList_OmitsCredentialEnvelopeAndOwnership()
    {
        await AssertJson(TokenListItem(CreateToken()), """
            {"id":"018e4e5c-7f00-7000-8000-000000000033","provider":"atproto",
             "pdsHost":"https://pds.example.invalid","expiresAt":"2026-09-12T10:00:00Z"}
            """);
    }

    [Test]
    public async Task Tokens_PreserveNullAndEmptyMetadata_AndDetachedListOrder()
    {
        var first = CreateToken();
        first.PdsHost = null;
        first.ExpiresAt = null;
        var second = CreateToken();
        second.Id = ActorId;
        second.PdsHost = "";
        List<UserAuthenticationToken> source = [first, second];
        var result = Tokens(source);
        source.Clear();
        first.Provider = "changed";
        await Assert.That(result.Count).IsEqualTo(2);
        await Assert.That(result[0].Id).IsEqualTo(Stamp);
        await Assert.That(result[1].Id).IsEqualTo(ActorId);
        await Assert.That(result[0].PdsHost).IsNull();
        await Assert.That(result[0].ExpiresAt).IsNull();
        await Assert.That(result[1].PdsHost).IsEqualTo("");
        await Assert.That(result[0].Provider).IsEqualTo("atproto");
        await Assert.That(TokenDetail(first).ExpiresAt).IsNull();
        await Assert.That(TokenDetail(first).PdsHost).IsNull();
        await Assert.That(Tokens([]).Count).IsEqualTo(0);
    }

    private static async Task AssertJson<T>(T result, string expected)
    {
        string actual = JsonSerializer.Serialize(result, JsonOptions);
        await Assert.That(JsonNode.DeepEquals(JsonNode.Parse(actual), JsonNode.Parse(expected))).IsTrue();
    }

    internal static User CreateUser()
    {
        var user = new User
        {
            Id = UserId,
            Pii = new UserPii { Email = "private@example.invalid", FirstName = "First", LastName = "Last" },
            EmailVerified = false,
            ConcurrencyStamp = Stamp,
            LastActiveTenantId = ActorId,
            CreatedBy = ActorId,
            UpdatedBy = ActorId,
            DeletedBy = ActorId,
            IsDeleted = true,
            CreatedAt = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        var actor = new Actor
        {
            Id = ActorId,
            ActorType = null!,
            UserId = UserId,
            User = user,
            Pii = new ActorPii { DisplayName = "Public name", ProfilePictureUri = "https://images.example.invalid/profile.png" },
            BackgroundColor = "",
            BackgroundEffect = "stars",
            BannerColor = "blue",
            ModerationReasonCode = "private-reason"
        };
        actor.AtprotoIdentities.Add(new AtprotoIdentity(AtprotoDid.Parse("did:plc:abcdefghijklmnopqrstuvwx"))
        { Actor = actor, Handle = "first.example.invalid", PdsHost = "https://private.example.invalid" });
        actor.AtprotoIdentities.Add(new AtprotoIdentity(AtprotoDid.Parse("did:plc:zyxwvutsrqponmlkjihgfedc"))
        { Actor = actor, Handle = "second.example.invalid", PdsHost = "https://private.example.invalid" });
        user.Actor = actor;
        return user;
    }

    internal static UserAuthenticationToken CreateToken() => new()
    {
        Id = Stamp,
        Provider = "atproto",
        PdsHost = "https://pds.example.invalid",
        ExpiresAt = new DateTime(2026, 9, 12, 10, 0, 0, DateTimeKind.Utc),
        UserId = UserId,
        User = CreateUser(),
        TenantId = ActorId,
        Tenant = null!,
        SubjectDid = "did:plc:abcdefghijklmnopqrstuvwx",
        SessionCiphertext = new byte[32],
        EncryptionKeyId = "test-key-reference",
        OAuthClientKeyId = "test-client-key-reference",
        EnvelopeVersion = 9,
        ConcurrencyStamp = UserId,
        CreatedBy = ActorId,
        UpdatedBy = ActorId
    };

    private static UserDto Detail(User source) => UserMapper.ToDetail(source);
    private static UserAuthenticationTokenDto TokenDetail(UserAuthenticationToken source) => UserMapper.ToTokenDetail(source);
    private static UserAuthenticationTokenListDto TokenListItem(UserAuthenticationToken source) => UserMapper.ToTokenListItem(source);
    private static List<UserAuthenticationTokenListDto> Tokens(List<UserAuthenticationToken> source) => UserMapper.ToTokenList(source);
}
