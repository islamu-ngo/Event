using Explore.Application.Authentication;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.Authentication.Local.Models;
using Explore.Application.Features.Authentication.Local.Handlers.Commands;
using Explore.Application.Features.Authentication.Local.Requests.Commands;
using Explore.Application.Features.Users.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain.Enums;
using NSubstitute;
using System.Security.Cryptography;

namespace Event.Application.UnitTests.Features.Authentication.Local;

public sealed class LocalAuthenticationCommandHandlerTests
{
    [Test]
    public async Task ReplacementChallengeNeverEntersOrdinaryUserSynchronization()
    {
        var service = Substitute.For<ILocalIdentityAuthService>();
        var syncUserCommandHandler = Substitute.For<ICommandHandler<SyncUserCommand, BaseCommandResponse<Guid>>>();
        var challenge = new LocalIssuedReplacementChallenge(
            token: Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
            expiresAt: DateTimeOffset.UtcNow.AddMinutes(5));
        service.AuthenticateAsync(Arg.Any<LocalAuthRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(LocalAuthResponseDto.ReplacementRequired(challenge: challenge));
        syncUserCommandHandler.ExecuteAsync(Arg.Any<SyncUserCommand>(), Arg.Any<CancellationToken>())
            .Returns<Task<BaseCommandResponse<Guid>>>(_ => throw new InvalidOperationException(
                "A replacement challenge must not synchronize an ordinary user session."));
        var handler = new LocalLoginCommandHandler(authService: service,
            providerDispatcher: CreateActiveDispatcher(), syncUserCommandHandler: syncUserCommandHandler);

        LocalAuthResponseDto response = await handler.ExecuteAsync(
            new LocalLoginCommand(new LocalAuthRequestDto(Identifier: "admin@example.test", Password: CreateValidPassword())),
            CancellationToken.None);

        await Assert.That(response.Outcome).IsEqualTo(LocalAuthOutcome.ReplacementRequired);
        await Assert.That(response.Success).IsFalse();
        await Assert.That(response.Token).IsNull();
        await Assert.That(response.ReplacementChallenge?.Token).IsEqualTo(challenge.Token);
    }

    private static readonly Guid UserId =
        Guid.Parse("01990aa7-4c67-7fb8-a303-8b301cc615af");

    private static readonly DateTimeOffset ExpiresAt =
        new(2026, 9, 4, 15, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task InvalidLoginNeverReachesCredentialService()
    {
        var authService = Substitute.For<ILocalIdentityAuthService>();
        var syncUserCommandHandler = Substitute.For<ICommandHandler<SyncUserCommand, BaseCommandResponse<Guid>>>();
        var handler = new LocalLoginCommandHandler(
            authService,
            CreateActiveDispatcher(),
            syncUserCommandHandler);

        LocalAuthResponseDto result = await handler.ExecuteAsync(
            new LocalLoginCommand(new LocalAuthRequestDto("invalid", string.Empty)),
            CancellationToken.None);

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.Failure).IsEqualTo(LocalAuthFailure.InvalidRequest);
        await authService.DidNotReceiveWithAnyArgs()
            .AuthenticateAsync(default!, default);
    }

    [Test]
    public async Task SuccessfulLoginSynchronizesNormalizedLocalAccountBeforeReturningToken()
    {
        var authService = Substitute.For<ILocalIdentityAuthService>();
        var syncUserCommandHandler = Substitute.For<ICommandHandler<SyncUserCommand, BaseCommandResponse<Guid>>>();
        LocalAuthResponseDto authenticated = CreateAuthenticatedResponse();
        authService.AuthenticateAsync(
                Arg.Any<LocalAuthRequestDto>(),
                Arg.Any<CancellationToken>())
            .Returns(authenticated);
        syncUserCommandHandler.ExecuteAsync(
                Arg.Any<SyncUserCommand>(),
                Arg.Any<CancellationToken>())
            .Returns(BaseCommandResponse.Success(UserId));
        var handler = new LocalLoginCommandHandler(
            authService,
            CreateActiveDispatcher(),
            syncUserCommandHandler);

        LocalAuthResponseDto result = await handler.ExecuteAsync(
            new LocalLoginCommand(new LocalAuthRequestDto(
                "admin@example.test",
                CreateValidPassword())),
            CancellationToken.None);

        await Assert.That(result).IsEqualTo(authenticated);
        await syncUserCommandHandler.Received().ExecuteAsync(
            Arg.Is<SyncUserCommand>(command =>
                command != null
                && command.AccountKey.ProviderKind == AuthenticationProviderKind.Local
                && command.AccountKey.Value == UserId.ToString("D")
                && command.UserDto.Id == UserId
                && command.UserDto.AuthProvider == "local"
                && command.UserDto.EmailVerified == true),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task InactiveLocalProviderRejectsNewLoginBeforeCredentialAccess()
    {
        var authService = Substitute.For<ILocalIdentityAuthService>();
        var dispatcher = Substitute.For<IAuthenticationProviderDispatcher>();
        dispatcher.GetActivePrimaryProviderAsync(Arg.Any<CancellationToken>())
            .Returns(AuthenticationProviderKind.Keycloak);
        var handler = new LocalLoginCommandHandler(
            authService,
            dispatcher,
            Substitute.For<ICommandHandler<SyncUserCommand, BaseCommandResponse<Guid>>>());

        LocalAuthResponseDto result = await handler.ExecuteAsync(
            new LocalLoginCommand(new LocalAuthRequestDto(
                "admin@example.test",
                CreateValidPassword())),
            CancellationToken.None);

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.Failure).IsEqualTo(LocalAuthFailure.ProviderInactive);
        await authService.DidNotReceiveWithAnyArgs()
            .AuthenticateAsync(default!, default);
    }

    [Test]
    [Arguments(LocalAuthFailure.EmailVerificationRequired)]
    [Arguments(LocalAuthFailure.AuthenticationFailed)]
    public async Task DeniedIssuanceNeverAttemptsDomainSynchronization(LocalAuthFailure failure)
    {
        var authService = Substitute.For<ILocalIdentityAuthService>();
        var syncUserCommandHandler = Substitute.For<ICommandHandler<SyncUserCommand, BaseCommandResponse<Guid>>>();
        authService.AuthenticateAsync(
                Arg.Any<LocalAuthRequestDto>(),
                Arg.Any<CancellationToken>())
            .Returns(LocalAuthResponseDto.Failed(failure: failure));
        syncUserCommandHandler.ExecuteAsync(Arg.Any<SyncUserCommand>(), Arg.Any<CancellationToken>())
            .Returns<Task<BaseCommandResponse<Guid>>>(_ =>
                throw new InvalidOperationException("Denied issuance must not synchronize a domain user."));
        var handler = new LocalLoginCommandHandler(
            authService,
            CreateActiveDispatcher(),
            syncUserCommandHandler);

        LocalAuthResponseDto result = await handler.ExecuteAsync(
            new LocalLoginCommand(new LocalAuthRequestDto(Identifier: "admin@example.test", Password: CreateValidPassword())),
            CancellationToken.None);

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.Failure).IsEqualTo(failure);
        await Assert.That(result.Token).IsNull();
    }

    [Test]
    public async Task SynchronizationFailureDoesNotExposeNewlyIssuedLoginToken()
    {
        var authService = Substitute.For<ILocalIdentityAuthService>();
        var syncUserCommandHandler = Substitute.For<ICommandHandler<SyncUserCommand, BaseCommandResponse<Guid>>>();
        authService.AuthenticateAsync(
                Arg.Any<LocalAuthRequestDto>(),
                Arg.Any<CancellationToken>())
            .Returns(CreateAuthenticatedResponse());
        syncUserCommandHandler.ExecuteAsync(
                Arg.Any<SyncUserCommand>(),
                Arg.Any<CancellationToken>())
            .Returns(BaseCommandResponse.Validation<Guid>(
                ["Domain synchronization failed."],
                "Domain synchronization failed."));
        var handler = new LocalLoginCommandHandler(
            authService,
            CreateActiveDispatcher(),
            syncUserCommandHandler);

        LocalAuthResponseDto result = await handler.ExecuteAsync(
            new LocalLoginCommand(new LocalAuthRequestDto(Identifier: "admin@example.test", Password: CreateValidPassword())),
            CancellationToken.None);

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.Failure).IsEqualTo(LocalAuthFailure.UserSynchronizationFailed);
        await Assert.That(result.Token).IsNull();
    }

    private static LocalAuthResponseDto CreateAuthenticatedResponse() =>
        LocalAuthResponseDto.Authenticated(
            userId: UserId,
            email: "admin@example.test",
            firstName: "Site",
            lastName: "Administrator",
            emailVerified: true,
            roles: ["Admin"],
            token: Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
            expiresAt: ExpiresAt);

    private static string CreateValidPassword() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));

    private static IAuthenticationProviderDispatcher CreateActiveDispatcher()
    {
        var dispatcher = Substitute.For<IAuthenticationProviderDispatcher>();
        dispatcher.GetActivePrimaryProviderAsync(Arg.Any<CancellationToken>())
            .Returns(AuthenticationProviderKind.Local);
        return dispatcher;
    }
}
