using Explore.Application.Authentication;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Features.Authentication.Local.Models;
using Explore.Application.Features.Authentication.Local.Handlers.Commands;
using Explore.Application.Features.Authentication.Local.Requests.Commands;
using Explore.Application.Features.Users.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain.Enums;
using MediatR;
using NSubstitute;
using System.Security.Cryptography;

namespace Event.Application.UnitTests.Features.Authentication.Local;

public sealed class LocalAuthenticationCommandHandlerTests
{
    private static readonly Guid UserId =
        Guid.Parse("01990aa7-4c67-7fb8-a303-8b301cc615af");

    private static readonly DateTimeOffset ExpiresAt =
        new(2026, 9, 4, 15, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task InvalidLoginNeverReachesCredentialService()
    {
        var authService = Substitute.For<ILocalIdentityAuthService>();
        var sender = Substitute.For<ISender>();
        var handler = new LocalLoginCommandHandler(
            authService,
            CreateActiveDispatcher(),
            sender);

        LocalAuthResponseDto result = await handler.Handle(
            new LocalLoginCommand(new LocalAuthRequestDto("invalid", string.Empty)),
            CancellationToken.None);

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("invalid_request");
        await authService.DidNotReceiveWithAnyArgs()
            .AuthenticateAsync(default!, default);
    }

    [Test]
    public async Task SuccessfulLoginSynchronizesNormalizedLocalAccountBeforeReturningToken()
    {
        var authService = Substitute.For<ILocalIdentityAuthService>();
        var sender = Substitute.For<ISender>();
        LocalAuthResponseDto authenticated = CreateAuthenticatedResponse();
        authService.AuthenticateAsync(
                Arg.Any<LocalAuthRequestDto>(),
                Arg.Any<CancellationToken>())
            .Returns(authenticated);
        sender.Send(
                Arg.Any<SyncUserCommand>(),
                Arg.Any<CancellationToken>())
            .Returns(BaseCommandResponse.Success(UserId));
        var handler = new LocalLoginCommandHandler(
            authService,
            CreateActiveDispatcher(),
            sender);

        LocalAuthResponseDto result = await handler.Handle(
            new LocalLoginCommand(new LocalAuthRequestDto(
                "admin@example.test",
                CreateValidPassword())),
            CancellationToken.None);

        await Assert.That(result).IsEqualTo(authenticated);
        await sender.Received().Send(
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
            Substitute.For<ISender>());

        LocalAuthResponseDto result = await handler.Handle(
            new LocalLoginCommand(new LocalAuthRequestDto(
                "admin@example.test",
                CreateValidPassword())),
            CancellationToken.None);

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("provider_inactive");
        await authService.DidNotReceiveWithAnyArgs()
            .AuthenticateAsync(default!, default);
    }

    [Test]
    public async Task SuccessfulRegistrationSynchronizesNormalizedLocalAccount()
    {
        var authService = Substitute.For<ILocalIdentityAuthService>();
        var sender = Substitute.For<ISender>();
        LocalRegistrationResponseDto registered =
            LocalRegistrationResponseDto.Registered(CreateAuthenticatedResponse());
        authService.RegisterAsync(
                Arg.Any<LocalRegistrationRequestDto>(),
                Arg.Any<CancellationToken>())
            .Returns(registered);
        sender.Send(
                Arg.Any<SyncUserCommand>(),
                Arg.Any<CancellationToken>())
            .Returns(BaseCommandResponse.Success(UserId));
        var handler = new LocalRegisterCommandHandler(
            authService,
            CreateActiveDispatcher(),
            sender);

        LocalRegistrationResponseDto result = await handler.Handle(
            new LocalRegisterCommand(CreateRegistrationRequest()),
            CancellationToken.None);

        await Assert.That(result).IsEqualTo(registered);
        await sender.Received().Send(
            Arg.Is<SyncUserCommand>(command =>
                command != null
                && command.AccountKey.ProviderKind == AuthenticationProviderKind.Local
                && command.AccountKey.Value == UserId.ToString("D")
                && command.UserDto.Id == UserId
                && command.UserDto.EmailVerified == true),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task FailedRegistrationNeverAttemptsDomainSynchronization()
    {
        var authService = Substitute.For<ILocalIdentityAuthService>();
        var sender = Substitute.For<ISender>();
        authService.RegisterAsync(
                Arg.Any<LocalRegistrationRequestDto>(),
                Arg.Any<CancellationToken>())
            .Returns(LocalRegistrationResponseDto.Failed("registration_failed"));
        var handler = new LocalRegisterCommandHandler(
            authService,
            CreateActiveDispatcher(),
            sender);

        LocalRegistrationResponseDto result = await handler.Handle(
            new LocalRegisterCommand(CreateRegistrationRequest()),
            CancellationToken.None);

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("registration_failed");
        await sender.DidNotReceiveWithAnyArgs()
            .Send(default(SyncUserCommand)!, default);
    }

    [Test]
    public async Task SynchronizationFailureDoesNotExposeNewlyIssuedRegistrationToken()
    {
        var authService = Substitute.For<ILocalIdentityAuthService>();
        var sender = Substitute.For<ISender>();
        authService.RegisterAsync(
                Arg.Any<LocalRegistrationRequestDto>(),
                Arg.Any<CancellationToken>())
            .Returns(LocalRegistrationResponseDto.Registered(CreateAuthenticatedResponse()));
        sender.Send(
                Arg.Any<SyncUserCommand>(),
                Arg.Any<CancellationToken>())
            .Returns(BaseCommandResponse.Validation<Guid>(
                ["Domain synchronization failed."],
                "Domain synchronization failed."));
        var handler = new LocalRegisterCommandHandler(
            authService,
            CreateActiveDispatcher(),
            sender);

        LocalRegistrationResponseDto result = await handler.Handle(
            new LocalRegisterCommand(CreateRegistrationRequest()),
            CancellationToken.None);

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("user_sync_failed");
        await Assert.That(result.Authentication).IsNull();
    }

    private static LocalRegistrationRequestDto CreateRegistrationRequest() =>
        new(
            "admin@example.test",
            CreateValidPassword(),
            "Site",
            "Administrator");

    private static LocalAuthResponseDto CreateAuthenticatedResponse() =>
        LocalAuthResponseDto.Authenticated(
            UserId,
            "admin@example.test",
            "Site",
            "Administrator",
            true,
            ["Admin"],
            Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
            ExpiresAt);

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
