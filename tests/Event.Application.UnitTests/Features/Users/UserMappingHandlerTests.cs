using System.Text.Json;
using Event.Application.UnitTests.Profiles;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.User;
using Explore.Application.Exceptions;
using Explore.Application.Features.UserAuthenticationTokens.Handlers.Queries;
using Explore.Application.Features.UserAuthenticationTokens.Requests.Queries;
using Explore.Application.Features.Users.Handlers.Commands;
using Explore.Application.Features.Users.Handlers.Queries;
using Explore.Application.Features.Users.Requests.Commands;
using Explore.Application.Features.Users.Requests.Queries;
using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Event.Application.UnitTests.Features.Users;

public sealed class UserMappingHandlerTests
{
    [Test]
    [Arguments("https://images.example.invalid/profile.png", "https://images.example.invalid/profile.png")]
    [Arguments(null, null)]
    public async Task Read_ProfileImageStillUsesPresentationBoundary(string? source, string? expected)
    {
        var user = UserMapperTests.CreateUser();
        user.Actor!.Pii.ExternalProfilePictureUri = source;
        var repository = UserRepository(user);
        var handler = new GetUserRequestHandler(repository, Substitute.For<IObjectStorageService>(),
            NullLogger<GetUserRequestHandler>.Instance, new InlineCache(), Substitute.For<IPrivacyErasureStateRepository>());
        var result = await handler.QueryAsync(new GetUserRequest(user.Id), CancellationToken.None);
        await Assert.That(result).IsNotNull();
        await Assert.That(result?.Email).IsEqualTo("private@example.invalid");
        await Assert.That(result?.ActorHandle).IsEqualTo("first.example.invalid");
        await Assert.That(result?.ProfileImageUri).IsEqualTo(expected);
        await Assert.That(result?.ExternalProfilePictureUri).IsEqualTo(expected);
        await Assert.That(result?.ProfilePictureStorageObjectId).IsNull();
    }

    [Test]
    public async Task Read_RawProviderKeyCannotBecomeAnExternalProfileImage()
    {
        var user = UserMapperTests.CreateUser();
        await Assert.That(() => user.Actor!.Pii.ExternalProfilePictureUri = "private/raw-object-key")
            .Throws<ArgumentException>();
        var result = await new GetUserRequestHandler(
            UserRepository(user), Substitute.For<IObjectStorageService>(),
            NullLogger<GetUserRequestHandler>.Instance, new InlineCache(),
            Substitute.For<IPrivacyErasureStateRepository>())
            .QueryAsync(new GetUserRequest(user.Id), CancellationToken.None);
        await Assert.That(result!.ProfileImageUri).IsEqualTo("https://images.example.invalid/profile.png");
    }

    [Test]
    public async Task TokenQueries_UseCurrentOwner_AndReturnOnlyVisibleMetadataInOrder()
    {
        var token = UserMapperTests.CreateToken();
        var second = UserMapperTests.CreateToken();
        second.Id = Guid.Parse("018e4e5c-7f00-7000-8000-000000000034");
        var currentUser = Substitute.For<ICurrentUserService>();
        currentUser.UserId.Returns(token.UserId);
        var repository = Substitute.For<IUserAuthenticationTokenRepository>();
        List<UserAuthenticationToken> visible = [second, token];
        repository.GetUserAuthenticationTokensWithDetailsForUser(token.UserId, Arg.Any<CancellationToken>()).Returns(visible);
        repository.GetUserAuthenticationTokenWithDetailsForUser(token.Id, token.UserId, Arg.Any<CancellationToken>()).Returns(token);
        var detailHandler = new GetUserAuthenticationTokenDetailsRequestHandler(repository, currentUser);
        var listHandler = new GetUserAuthenticationTokenListRequestHandler(repository, currentUser);
        var detail = await detailHandler.QueryAsync(new GetUserAuthenticationTokenDetailsRequest(token.Id), CancellationToken.None);
        var list = await listHandler.QueryAsync(new GetUserAuthenticationTokenListRequest(), CancellationToken.None);
        visible.Clear();
        await Assert.That(detail!.Provider).IsEqualTo("atproto");
        await Assert.That(list.Select(item => item.Id).ToArray()).IsEquivalentTo(new[] { second.Id, token.Id });
        await Assert.That(list[0].Id).IsEqualTo(second.Id);
        await Assert.That(list[1].Id).IsEqualTo(token.Id);
        currentUser.UserId.Returns(second.Id);
        await Assert.That(await detailHandler.QueryAsync(new GetUserAuthenticationTokenDetailsRequest(token.Id), CancellationToken.None)).IsNull();
    }

    [Test]
    public async Task TokenQueries_AnonymousCallerIsRejectedBeforeProjection()
    {
        var currentUser = Substitute.For<ICurrentUserService>();
        currentUser.UserId.Returns((Guid?)null);
        var repository = Substitute.For<IUserAuthenticationTokenRepository>();
        var detail = new GetUserAuthenticationTokenDetailsRequestHandler(repository, currentUser);
        var list = new GetUserAuthenticationTokenListRequestHandler(repository, currentUser);
        await Assert.That(async () => await detail.QueryAsync(new GetUserAuthenticationTokenDetailsRequest(), CancellationToken.None))
            .Throws<AuthorizationException>();
        await Assert.That(async () => await list.QueryAsync(new GetUserAuthenticationTokenListRequest(), CancellationToken.None))
            .Throws<AuthorizationException>();
    }

    [Test]
    public async Task IdentityResolution_UsesProviderAccountBinding_NeverEmailFallback()
    {
        var user = UserMapperTests.CreateUser();
        var repository = Substitute.For<IExternalLoginAuthenticationRepository>();
        var key = new ProviderAccountKey(AuthenticationProviderKind.Google, "provider-account");
        repository.GetByProviderAndKey(key).Returns(new UserExternalLogin
        { User = user, UserId = user.Id, AuthenticationProvider = null!, AuthenticationProviderId = (int)AuthenticationProviderKind.Google, ProviderKey = key.Value });
        var handler = new ResolveCurrentUserIdByIdentityRequestHandler(repository);
        var request = new ResolveCurrentUserIdByIdentityRequest
        { Provider = "google", ProviderId = " provider-account ", Email = "forged@example.invalid", EmailVerified = true };
        await Assert.That(await handler.QueryAsync(request, CancellationToken.None)).IsEqualTo(user.Id);
        await Assert.That(await handler.QueryAsync(request with { ProviderId = "unlinked", Email = user.Email }, CancellationToken.None)).IsNull();
        await Assert.That(await handler.QueryAsync(request with { Provider = "local" }, CancellationToken.None)).IsNull();
    }

    private static IUserRepository UserRepository(User user)
    {
        var repository = Substitute.For<IUserRepository>();
        repository.GetById(user.Id).Returns(user);
        repository.GetUserWithDetails(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        return repository;
    }

    private sealed class InlineCache : HybridCache
    {
        public List<string> RemovedKeys { get; } = [];
        public override ValueTask<T> GetOrCreateAsync<TState, T>(string key, TState state, Func<TState, CancellationToken, ValueTask<T>> factory,
            HybridCacheEntryOptions? options = null, IEnumerable<string>? tags = null, CancellationToken cancellationToken = default) => factory(state, cancellationToken);
        public override ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default)
        { RemovedKeys.Add(key); return ValueTask.CompletedTask; }
        public override ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public override ValueTask SetAsync<T>(string key, T value, HybridCacheEntryOptions? options = null, IEnumerable<string>? tags = null,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
}
