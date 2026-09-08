using System.Net;
using System.Net.Http.Json;
using Event.Api.IntegrationTests.Fixtures;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel("ApiTestFixture")]
[ClassDataSource<ApiTestFixture>(Shared = SharedType.PerAssembly)]
public class FederationControllerTests
{
    private readonly ApiTestFixture _fixture;

    public FederationControllerTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    #region Removed ActorKeyStore Surface

    [Test]
    public async Task ActorKeyStore_GetAll_WhenPublicSurfaceIsAbsent_ShouldReturnNotFound()
    {
        var response = await _fixture.Client.GetAsync("/api/actorkeystore");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task ActorKeyStore_GetById_WhenPublicSurfaceIsAbsent_ShouldReturnNotFound()
    {
        var response = await _fixture.Client.GetAsync($"/api/actorkeystore/{Guid.NewGuid()}");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task ActorKeyStore_Create_WhenPublicSurfaceIsAbsent_ShouldReturnNotFound()
    {
        var response = await _fixture.Client.PostAsJsonAsync("/api/actorkeystore", new
        {
            ActorId = Guid.NewGuid(),
            KeyPurpose = "signing",
            PrivateKeyEncrypted = "encrypted-key",
            PublicKey = "public-key",
            IsActive = true
        });
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task ActorKeyStore_Update_WhenPublicSurfaceIsAbsent_ShouldReturnNotFound()
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/actorkeystore/{Guid.NewGuid()}")
        {
            Content = JsonContent.Create(new { PrivateKeyEncrypted = "encrypted-key" })
        };
        var response = await _fixture.Client.SendAsync(request);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task ActorKeyStore_Delete_WhenPublicSurfaceIsAbsent_ShouldReturnNotFound()
    {
        var response = await _fixture.Client.DeleteAsync($"/api/actorkeystore/{Guid.NewGuid()}");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    #endregion

    #region Removed IndexedDid Surface

    [Test]
    public async Task IndexedDid_GetAll_WhenPublicSurfaceIsAbsent_ShouldReturnNotFound()
    {
        var response = await _fixture.Client.GetAsync("/api/indexeddid");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task IndexedDid_GetById_WhenPublicSurfaceIsAbsent_ShouldReturnNotFound()
    {
        var response = await _fixture.Client.GetAsync("/api/indexeddid/did:plc:test");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task IndexedDid_Create_WhenPublicSurfaceIsAbsent_ShouldReturnNotFound()
    {
        var response = await _fixture.Client.PostAsJsonAsync("/api/indexeddid", new { Did = "did:plc:test" });
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task IndexedDid_Update_WhenPublicSurfaceIsAbsent_ShouldReturnNotFound()
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/indexeddid/did:plc:test")
        {
            Content = JsonContent.Create(new { Handle = "example.test" })
        };
        var response = await _fixture.Client.SendAsync(request);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task IndexedDid_Delete_WhenPublicSurfaceIsAbsent_ShouldReturnNotFound()
    {
        var response = await _fixture.Client.DeleteAsync("/api/indexeddid/did:plc:test");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    #endregion

    #region Removed SyncState Surface

    [Test]
    public async Task SyncState_GetAll_WhenPublicSurfaceIsAbsent_ShouldReturnNotFound()
    {
        var response = await _fixture.Client.GetAsync("/api/syncstate");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task SyncState_GetById_WhenPublicSurfaceIsAbsent_ShouldReturnNotFound()
    {
        var response = await _fixture.Client.GetAsync($"/api/syncstate/{1}");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task SyncState_Create_WhenPublicSurfaceIsAbsent_ShouldReturnNotFound()
    {
        var response = await _fixture.Client.PostAsJsonAsync("/api/syncstate", new { Service = "jetstream", Cursor = 1 });
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task SyncState_Update_WhenPublicSurfaceIsAbsent_ShouldReturnNotFound()
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/syncstate/1")
        {
            Content = JsonContent.Create(new { Id = 1, Service = "jetstream", Cursor = 2 })
        };
        var response = await _fixture.Client.SendAsync(request);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task SyncState_Delete_WhenPublicSurfaceIsAbsent_ShouldReturnNotFound()
    {
        var response = await _fixture.Client.DeleteAsync("/api/syncstate/1");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    #endregion

    #region AtprotoRecord Controller

    [Test]
    public async Task AtprotoRecord_GetAll_WhenRawSurfaceIsAbsent_ShouldReturnNotFound()
    {
        var response = await _fixture.Client.GetAsync("/api/atprotorecord");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task AtprotoRecord_GetById_WithRandomId_WhenAnonymous_ShouldReturnNotFound()
    {
        var response = await _fixture.Client.GetAsync($"/api/atprotorecord/{Guid.NewGuid()}");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task AtprotoRecord_Post_WhenRawSurfaceIsAbsent_ShouldReturnNotFound()
    {
        var response = await _fixture.Client.PostAsJsonAsync("/api/atprotorecord", new
        {
            Did = "did:plc:test",
            Collection = "app.bsky.feed.post",
            RecordKey = "test-key"
        });
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task AtprotoRecord_Delete_WhenRawSurfaceIsAbsent_ShouldReturnNotFound()
    {
        var response = await _fixture.Client.DeleteAsync($"/api/atprotorecord/{Guid.NewGuid()}");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    #endregion

    #region UserAuthenticationToken Controller

    [Test]
    public async Task UserAuthenticationToken_GetAll_ShouldReturnUnauthorized()
    {
        var response = await _fixture.Client.GetAsync("/api/userauthenticationtoken");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task UserAuthenticationToken_GetById_WithRandomId_ShouldNotReturnServerError()
    {
        var response = await _fixture.Client.GetAsync($"/api/userauthenticationtoken/{Guid.NewGuid()}");
        await Assert.That(response.StatusCode).IsNotEqualTo(HttpStatusCode.InternalServerError);
    }

    #endregion

    #region Removed UserExternalLogin Surface

    [Test]
    public async Task UserExternalLogin_GetAll_WhenPublicSurfaceIsAbsent_ShouldReturnNotFound()
    {
        var response = await _fixture.Client.GetAsync("/api/userexternallogin");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task UserExternalLogin_GetById_WhenPublicSurfaceIsAbsent_ShouldReturnNotFound()
    {
        var response = await _fixture.Client.GetAsync($"/api/userexternallogin/{Guid.NewGuid()}");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task UserExternalLogin_Create_WhenPublicSurfaceIsAbsent_ShouldReturnNotFound()
    {
        var response = await _fixture.Client.PostAsJsonAsync("/api/userexternallogin", new
        {
            UserId = Guid.NewGuid(),
            Provider = "atproto",
            ProviderKey = "did:plc:test"
        });
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task UserExternalLogin_Update_WhenPublicSurfaceIsAbsent_ShouldReturnNotFound()
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/userexternallogin/{Guid.NewGuid()}")
        {
            Content = JsonContent.Create(new { ProviderKey = "did:plc:other" })
        };
        var response = await _fixture.Client.SendAsync(request);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task UserExternalLogin_Delete_WhenPublicSurfaceIsAbsent_ShouldReturnNotFound()
    {
        var response = await _fixture.Client.DeleteAsync($"/api/userexternallogin/{Guid.NewGuid()}");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    #endregion
}
