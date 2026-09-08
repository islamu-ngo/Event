using System.Security.Claims;
using System.Text.Json;
using Explore.Blazor.Client.Clients;
using Event.Web.BffHosting.Security;
using Explore.Blazor.Services;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;

using Options = Microsoft.Extensions.Options.Options;

namespace Explore.Blazor.IntegrationTests.Services;

public sealed class StorageUploadSessionStoreTests
{
    private readonly MemoryDistributedCache _cache = new(Options.Create(new MemoryDistributedCacheOptions()));
    private readonly StorageUploadSessionStore _store;

    public StorageUploadSessionStoreTests()
    {
        _store = new StorageUploadSessionStore(_cache);
    }

    [Test]
    public async Task IssueAsync_WithMissingApiUploadSessionId_ReturnsFailure()
    {
        var result = await _store.IssueAsync(
            CreateUser("user-1"),
            new StorageUploadSessionDto
            {
                Id = Guid.Empty,
                Provider = "local",
                ExpectedSizeBytes = 4,
                ReservedBytes = 4,
                ContentType = "image/png",
                SafeDisplayName = "probe.png",
                Purpose = "legacy_image",
                Visibility = "public_image",
                Status = "reserved",
                ExpiresAt = DateTime.UtcNow.AddMinutes(15)
            },
            "image/png");

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("missing_upload_session_id");
    }

    [Test]
    public async Task IssueAsync_WithExpiredApiUploadSession_ReturnsFailure()
    {
        var result = await _store.IssueAsync(
            CreateUser("user-1"),
            new StorageUploadSessionDto
            {
                Id = ApiUploadSessionId,
                Provider = "local",
                ExpectedSizeBytes = 4,
                ReservedBytes = 4,
                ContentType = "image/png",
                SafeDisplayName = "probe.png",
                Purpose = "legacy_image",
                Visibility = "public_image",
                Status = "reserved",
                ExpiresAt = DateTime.UtcNow.AddMinutes(-1)
            },
            "image/png");

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("upload_session_expired");
    }

    [Test]
    public async Task ResolveAsync_WithDifferentUser_ReturnsFailure()
    {
        var issued = await IssueTrustedSessionAsync("user-1", "image/png");

        var resolved = await _store.ResolveAsync(CreateUser("user-2"), issued.SessionId!, "image/png");

        await Assert.That(resolved.Success).IsFalse();
        await Assert.That(resolved.FailureCode).IsEqualTo("session_owner_mismatch");
    }

    [Test]
    public async Task ResolveAsync_WithDifferentContentType_ReturnsFailure()
    {
        var issued = await IssueTrustedSessionAsync("user-1", "image/png");

        var resolved = await _store.ResolveAsync(CreateUser("user-1"), issued.SessionId!, "image/jpeg");

        await Assert.That(resolved.Success).IsFalse();
        await Assert.That(resolved.FailureCode).IsEqualTo("content_type_mismatch");
    }

    [Test]
    public async Task ResolveAsync_WithSameUserAndContentType_ReturnsApiUploadSession()
    {
        var issued = await IssueTrustedSessionAsync("user-1", "image/png");

        var resolved = await _store.ResolveAsync(CreateUser("user-1"), issued.SessionId!, "image/png");

        await Assert.That(resolved.Success).IsTrue();
        await Assert.That(resolved.Session).IsNotNull();
        await Assert.That(resolved.Session!.ApiUploadSessionId).IsEqualTo(ApiUploadSessionId);
        await Assert.That(resolved.Session.ExpectedSizeBytes).IsEqualTo(4);
    }

    [Test]
    public async Task ResolveAsync_WithExpiredCachedSession_ReturnsFailureAndConsumesSession()
    {
        const string sessionId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        var expiredSession = new StorageUploadSession(
            sessionId,
            OwnerKey("user-1"),
            ApiUploadSessionId,
            "image/png",
            4,
            DateTimeOffset.UtcNow.AddMinutes(-1));
        var payload = JsonSerializer.Serialize(expiredSession, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await _cache.SetStringAsync("storage-upload-session:" + sessionId, payload);

        var resolved = await _store.ResolveAsync(CreateUser("user-1"), sessionId, "image/png");
        var secondResolve = await _store.ResolveAsync(CreateUser("user-1"), sessionId, "image/png");

        await Assert.That(resolved.Success).IsFalse();
        await Assert.That(resolved.FailureCode).IsEqualTo("session_expired");
        await Assert.That(secondResolve.Success).IsFalse();
        await Assert.That(secondResolve.FailureCode).IsEqualTo("session_not_found");
    }

    private static readonly Guid ApiUploadSessionId = Guid.CreateVersion7();

    private Task<StorageUploadSessionIssueResult> IssueTrustedSessionAsync(string userId, string contentType) =>
        _store.IssueAsync(
            CreateUser(userId),
            new StorageUploadSessionDto
            {
                Id = ApiUploadSessionId,
                Provider = "local",
                ExpectedSizeBytes = 4,
                ReservedBytes = 4,
                ContentType = contentType,
                SafeDisplayName = "probe.png",
                Purpose = "legacy_image",
                Visibility = "public_image",
                Status = "reserved",
                ExpiresAt = DateTime.UtcNow.AddMinutes(15)
            },
            contentType);

    private static string OwnerKey(string userId)
    {
        CreateUser(userId).TryGetCircuitSubject(out var identity);
        return identity.PartitionKey;
    }

    private static ClaimsPrincipal CreateUser(string userId) =>
        new(new ClaimsIdentity(
        [
            new Claim("sub", userId),
            new Claim(ClaimTypes.Name, "Storage Tester")
        ], "Cookies"));
}
