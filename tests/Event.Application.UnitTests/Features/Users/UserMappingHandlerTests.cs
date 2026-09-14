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
    public async Task Update_NamesOnly_IgnoresForgedIdentityAuthorityAuditAndLifecycle()
    {
        var user = UserMapperTests.CreateUser();
        var originalPii = user.Pii;
        var originalActor = user.Actor;
        var repository = UserRepository(user);
        var cache = new InlineCache();
        var handler = UpdateHandler(repository, cache);
        var input = JsonSerializer.Deserialize<UpdateUserDto>("""
            {"Names":{"FirstName":"Updated","LastName":"Person","Email":"forged@example.invalid",
              "EmailVerified":true,"Id":"00000000-0000-0000-0000-000000000099","Pii":{"Email":"forged@example.invalid"}},
             "Email":"forged@example.invalid","EmailVerified":true,"IsDeleted":false,
             "ConcurrencyStamp":"00000000-0000-0000-0000-000000000099","IsInstanceAdmin":true,
             "LastActiveTenantId":"00000000-0000-0000-0000-000000000099",
             "CreatedAt":"2000-01-01T00:00:00Z","CreatedBy":"00000000-0000-0000-0000-000000000099",
             "UpdatedAt":"2000-01-01T00:00:00Z","UpdatedBy":"00000000-0000-0000-0000-000000000099",
             "DeletedAt":"2000-01-01T00:00:00Z","DeletedBy":"00000000-0000-0000-0000-000000000099",
             "Actor":{"Id":"00000000-0000-0000-0000-000000000099"}}
            """)!;
        var result = await handler.Handle(new UpdateUserCommand
        { UserId = user.Id, ExpectedConcurrencyStamp = user.ConcurrencyStamp, UpdateUserDto = input }, CancellationToken.None);
        var saved = (await repository.GetById(user.Id))!;
        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(saved.FirstName).IsEqualTo("Updated");
        await Assert.That(saved.LastName).IsEqualTo("Person");
        await Assert.That(saved.Email).IsEqualTo("private@example.invalid");
        await Assert.That(saved.EmailVerified).IsEqualTo(false);
        await Assert.That(saved.Id).IsEqualTo(Guid.Parse("018e4e5c-7f00-7000-8000-000000000031"));
        await Assert.That(saved.ConcurrencyStamp).IsEqualTo(Guid.Parse("018e4e5c-7f00-7000-8000-000000000033"));
        await Assert.That(saved.LastActiveTenantId).IsEqualTo(Guid.Parse("018e4e5c-7f00-7000-8000-000000000032"));
        await Assert.That(saved.CreatedBy).IsEqualTo(Guid.Parse("018e4e5c-7f00-7000-8000-000000000032"));
        await Assert.That(saved.CreatedAt).IsEqualTo(new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc));
        await Assert.That(saved.UpdatedBy).IsEqualTo(Guid.Parse("018e4e5c-7f00-7000-8000-000000000032"));
        await Assert.That(saved.UpdatedAt).IsNull();
        await Assert.That(saved.IsDeleted).IsTrue();
        await Assert.That(saved.DeletedAt).IsNull();
        await Assert.That(saved.DeletedBy).IsEqualTo(Guid.Parse("018e4e5c-7f00-7000-8000-000000000032"));
        await Assert.That(ReferenceEquals(saved.Pii, originalPii)).IsTrue();
        await Assert.That(ReferenceEquals(saved.Actor, originalActor)).IsTrue();
        await Assert.That(saved.Actor!.DisplayName).IsEqualTo("Public name");
        await Assert.That(cache.RemovedKeys).Contains($"user:detail:{saved.Id}");
    }

    [Test]
    public async Task Update_StaleStamp_DoesNotChangeNamesOrEvictCache()
    {
        var user = UserMapperTests.CreateUser();
        var cache = new InlineCache();
        var handler = UpdateHandler(UserRepository(user), cache);
        await Assert.That(async () => await handler.Handle(new UpdateUserCommand
        {
            UserId = user.Id, ExpectedConcurrencyStamp = Guid.Empty,
            UpdateUserDto = new UpdateUserDto { Names = new UpdateUserNamesDto { FirstName = "Changed", LastName = "Person" } }
        }, CancellationToken.None)).Throws<ConcurrencyConflictException>();
        await Assert.That(user.FirstName).IsEqualTo("First");
        await Assert.That(user.LastName).IsEqualTo("Last");
        await Assert.That(cache.RemovedKeys.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Update_InvalidNames_DoesNotChangeTrackedState()
    {
        var user = UserMapperTests.CreateUser();
        var cache = new InlineCache();
        var result = await UpdateHandler(UserRepository(user), cache).Handle(new UpdateUserCommand
        {
            UserId = user.Id, ExpectedConcurrencyStamp = user.ConcurrencyStamp,
            UpdateUserDto = new UpdateUserDto { Names = new UpdateUserNamesDto { FirstName = "", LastName = "Person" } }
        }, CancellationToken.None);
        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(user.FirstName).IsEqualTo("First");
        await Assert.That(user.LastName).IsEqualTo("Last");
        await Assert.That(cache.RemovedKeys.Count).IsEqualTo(0);
    }

    [Test]
    [Arguments("https://images.example.invalid/profile.png", "https://images.example.invalid/profile.png")]
    [Arguments("private/raw-object-key", null)]
    [Arguments(null, null)]
    public async Task Read_ProfileImageStillUsesPresentationBoundary(string? source, string? expected)
    {
        var user = UserMapperTests.CreateUser();
        user.Actor!.ProfilePictureUri = source;
        var repository = UserRepository(user);
        var handler = new GetUserRequestHandler(repository, Substitute.For<IObjectStorageService>(),
            NullLogger<GetUserRequestHandler>.Instance, new InlineCache(), Substitute.For<IPrivacyErasureStateRepository>());
        var result = await handler.Handle(new GetUserRequest(user.Id), CancellationToken.None);
        await Assert.That(result.Email).IsEqualTo("private@example.invalid");
        await Assert.That(result.ActorHandle).IsEqualTo("first.example.invalid");
        await Assert.That(result.ProfileImageUri).IsEqualTo(expected);
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
        var repository = Substitute.For<IUserExternalLoginRepository>();
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

    private static UpdateUserCommandHandler UpdateHandler(IUserRepository repository, InlineCache cache) => new(
        repository, Substitute.For<IActorRepository>(), Substitute.For<IStorageObjectRepository>(),
        Substitute.For<IPrivacyErasureStateRepository>(), Substitute.For<ITenantContext>(), new InlineUnitOfWork(), cache);

    private sealed class InlineUnitOfWork : IUnitOfWork
    {
        public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken ct = default) => operation(ct);
        public Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default) => operation(ct);
        public Task<T> ExecuteSerializableAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default) => operation(ct);
        public Task<T> ExecuteReadCommittedAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default) => operation(ct);
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
