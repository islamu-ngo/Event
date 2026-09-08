using AutoMapper;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.User;
using Explore.Application.Features.Users.Handlers.Queries;
using Explore.Application.Features.Users.Requests.Queries;
using Explore.Domain;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Event.Application.UnitTests.Features.Users.Queries;

public sealed class GetUserRequestHandlerPrivacyErasureTests
{
    [Test]
    public async Task FencedUser_ReturnsNoProfileWithoutReadingCache()
    {
        Guid userId = Guid.CreateVersion7();
        DateTime nowUtc = DateTime.UtcNow;
        PrivacyErasureIntent intent = PrivacyErasureIntent.Record(
            Guid.CreateVersion7(),
            1,
            PrivacyErasureSubjectKind.User,
            userId,
            PrivacyErasureReasonCode.AccountDeletion,
            1,
            nowUtc,
            nowUtc);
        PrivacyErasureSaga saga = PrivacyErasureSaga.Start(
            intent,
            1,
            new byte[32],
            nowUtc.AddMinutes(5),
            nowUtc);
        IPrivacyErasureStateRepository stateRepository = Substitute.For<IPrivacyErasureStateRepository>();
        stateRepository.GetBySubjectAsync(userId, Arg.Any<CancellationToken>()).Returns(saga);
        var cache = new RecordingHybridCache();
        var handler = new GetUserRequestHandler(
            Substitute.For<IUserRepository>(),
            Substitute.For<IObjectStorageService>(),
            Substitute.For<IMapper>(),
            Substitute.For<ILogger<GetUserRequestHandler>>(),
            cache,
            stateRepository);

        UserDto result = await handler.Handle(new GetUserRequest { UserId = userId }, CancellationToken.None);

        await Assert.That(result).IsNull();
        await Assert.That(cache.WasRead).IsFalse();
    }

    [Test]
    public async Task FenceEstablishedDuringCacheRead_RemovesEntryAndReturnsNoProfile()
    {
        Guid userId = Guid.CreateVersion7();
        DateTime nowUtc = DateTime.UtcNow;
        PrivacyErasureIntent intent = PrivacyErasureIntent.Record(
            Guid.CreateVersion7(),
            1,
            PrivacyErasureSubjectKind.User,
            userId,
            PrivacyErasureReasonCode.AccountDeletion,
            1,
            nowUtc,
            nowUtc);
        PrivacyErasureSaga saga = PrivacyErasureSaga.Start(
            intent,
            1,
            new byte[32],
            nowUtc.AddMinutes(5),
            nowUtc);
        IPrivacyErasureStateRepository stateRepository = Substitute.For<IPrivacyErasureStateRepository>();
        stateRepository.GetBySubjectAsync(userId, Arg.Any<CancellationToken>())
            .Returns((PrivacyErasureSaga?)null, saga);
        IUserRepository userRepository = Substitute.For<IUserRepository>();
        userRepository.GetUserWithDetails(userId, Arg.Any<CancellationToken>())
            .Returns(new User
            {
                Id = userId,
                Pii = new UserPii
                {
                    UserId = userId,
                    Email = "stale@example.invalid",
                    FirstName = "Stale",
                    LastName = "Profile"
                }
            });
        IMapper mapper = Substitute.For<IMapper>();
        mapper.Map<UserDto>(Arg.Any<User>()).Returns(new UserDto
        {
            Id = userId,
            Email = "stale@example.invalid",
            FirstName = "Stale",
            LastName = "Profile"
        });
        var cache = new RecordingHybridCache();
        var handler = new GetUserRequestHandler(
            userRepository,
            Substitute.For<IObjectStorageService>(),
            mapper,
            Substitute.For<ILogger<GetUserRequestHandler>>(),
            cache,
            stateRepository);

        UserDto result = await handler.Handle(new GetUserRequest { UserId = userId }, CancellationToken.None);

        await Assert.That(result).IsNull();
        await Assert.That(cache.RemovedKeys).Contains($"user:detail:{userId}");
    }

    private sealed class RecordingHybridCache : HybridCache
    {
        public bool WasRead { get; private set; }
        public List<string> RemovedKeys { get; } = [];

        public override ValueTask<T> GetOrCreateAsync<TState, T>(
            string key,
            TState state,
            Func<TState, CancellationToken, ValueTask<T>> factory,
            HybridCacheEntryOptions? options = null,
            IEnumerable<string>? tags = null,
            CancellationToken cancellationToken = default)
        {
            WasRead = true;
            return factory(state, cancellationToken);
        }

        public override ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            RemovedKeys.Add(key);
            return ValueTask.CompletedTask;
        }

        public override ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public override ValueTask SetAsync<T>(
            string key,
            T value,
            HybridCacheEntryOptions? options = null,
            IEnumerable<string>? tags = null,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
}
