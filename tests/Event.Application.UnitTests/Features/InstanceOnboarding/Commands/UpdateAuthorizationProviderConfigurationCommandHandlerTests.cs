using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Instance;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Features.InstanceOnboarding.Handlers.Commands;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Models.Common;
using NSubstitute;

namespace Event.Application.UnitTests.Features.InstanceOnboarding.Commands;

public class UpdateAuthorizationProviderConfigurationCommandHandlerTests
{
    private static readonly Guid TestUserId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000222");

    private readonly IAdminContext _adminContext;
    private readonly IAuthorizationProviderConfigurationService _configurationService;
    private readonly ISetupSecretProvider _setupSecretProvider;
    private readonly UpdateAuthorizationProviderConfigurationCommandHandler _handler;

    public UpdateAuthorizationProviderConfigurationCommandHandlerTests()
    {
        _adminContext = Substitute.For<IAdminContext>();
        _configurationService = Substitute.For<IAuthorizationProviderConfigurationService>();
        _setupSecretProvider = Substitute.For<ISetupSecretProvider>();
        _setupSecretProvider.IsSetupModeActiveAsync(Arg.Any<CancellationToken>()).Returns(true);
        _configurationService.ReadConfigurationAsync().Returns(new AuthorizationProviderConfigurationDto());

        _handler = new UpdateAuthorizationProviderConfigurationCommandHandler(
            _adminContext,
            _configurationService,
            _setupSecretProvider);
    }

    [Test]
    public async Task Handle_WhenUserIsNotInstanceAdmin_ReturnsUnauthorized()
    {
        _adminContext.IsInstanceAdminAsync(TestUserId, Arg.Any<CancellationToken>()).Returns(false);

        var result = await _handler.Handle(CreateCommand(new AuthorizationProviderConfigurationDto
        {
            Provider = "local"
        }), CancellationToken.None);

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Message).Contains("Only instance administrators");
        await _configurationService.DidNotReceive().ApplyConfigurationAsync(Arg.Any<AuthorizationProviderConfigurationDto>());
    }

    [Test]
    public async Task HandleSetup_WhenSetupModeIsActive_AppliesConfigurationWithoutAdminIdentity()
    {
        var command = new UpdateAuthorizationProviderConfigurationDuringSetupCommand
        {
            Patch = CreateCommand(new AuthorizationProviderConfigurationDto
            {
                Provider = "local"
            }).Patch
        };

        var result = await _handler.Handle(command, CancellationToken.None);

        await Assert.That(result.IsSuccess).IsTrue();
        await _adminContext.DidNotReceive().IsInstanceAdminAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _configurationService.Received(1).ApplyConfigurationAsync(Arg.Is<AuthorizationProviderConfigurationDto>(x =>
            x.Provider == "local"));
    }

    [Test]
    public async Task HandleSetup_WhenSetupModeBecomesInactive_DeniesMutation()
    {
        _setupSecretProvider.IsSetupModeActiveAsync(Arg.Any<CancellationToken>()).Returns(false);
        var command = new UpdateAuthorizationProviderConfigurationDuringSetupCommand
        {
            Patch = CreateCommand(new AuthorizationProviderConfigurationDto
            {
                Provider = "local"
            }).Patch
        };

        var result = await _handler.Handle(command, CancellationToken.None);

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Message).Contains("Setup mode is no longer active");
        await _configurationService.DidNotReceive().ApplyConfigurationAsync(Arg.Any<AuthorizationProviderConfigurationDto>());
    }

    [Test]
    public async Task Handle_WhenConfigurationIsInvalid_ReturnsValidationFailure()
    {
        _adminContext.IsInstanceAdminAsync(TestUserId, Arg.Any<CancellationToken>()).Returns(true);

        var result = await _handler.Handle(CreateCommand(new AuthorizationProviderConfigurationDto
        {
            Provider = "cerbos",
            CerbosGrpcEndpoint = string.Empty
        }), CancellationToken.None);

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Message).Contains("Invalid authorization provider configuration");
        await _configurationService.DidNotReceive().ApplyConfigurationAsync(Arg.Any<AuthorizationProviderConfigurationDto>());
    }

    [Test]
    public async Task Handle_WhenProviderIsLocal_AppliesConfigurationWithoutVerification()
    {
        _adminContext.IsInstanceAdminAsync(TestUserId, Arg.Any<CancellationToken>()).Returns(true);

        var configuration = new AuthorizationProviderConfigurationDto
        {
            Provider = "local"
        };

        var result = await _handler.Handle(CreateCommand(configuration), CancellationToken.None);

        await Assert.That(result.IsSuccess).IsTrue();
        await _configurationService.DidNotReceive().VerifyCerbosEndpointAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _configurationService.Received(1).ApplyConfigurationAsync(Arg.Is<AuthorizationProviderConfigurationDto>(x =>
            x.Provider == "local"));
    }

    [Test]
    public async Task Handle_WhenDeploymentOwnsProvider_RejectsAdminOverride()
    {
        _adminContext.IsInstanceAdminAsync(TestUserId, Arg.Any<CancellationToken>()).Returns(true);
        _configurationService.ReadConfigurationAsync().Returns(new AuthorizationProviderConfigurationDto
        {
            Provider = "cerbos",
            AuthorizationProviderManagedByDeployment = true
        });

        var result = await _handler.Handle(CreateCommand(new AuthorizationProviderConfigurationDto
        {
            Provider = "local"
        }), CancellationToken.None);

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Message).Contains("managed by the deployment");
        await _configurationService.DidNotReceive().ApplyConfigurationAsync(Arg.Any<AuthorizationProviderConfigurationDto>());
    }

    [Test]
    public async Task Handle_WhenCerbosEndpointVerifies_NormalizesEndpointAndMarksVerified()
    {
        _adminContext.IsInstanceAdminAsync(TestUserId, Arg.Any<CancellationToken>()).Returns(true);

        var configuration = new AuthorizationProviderConfigurationDto
        {
            Provider = "cerbos",
            CerbosGrpcEndpoint = "cerbosgrpc.example.com:443"
        };

        _configurationService.VerifyCerbosEndpointAsync("https://cerbosgrpc.example.com:443", Arg.Any<CancellationToken>())
            .Returns(true);

        var result = await _handler.Handle(CreateCommand(configuration), CancellationToken.None);

        await Assert.That(result.IsSuccess).IsTrue();
        await _configurationService.Received(1)
            .VerifyCerbosEndpointAsync("https://cerbosgrpc.example.com:443", Arg.Any<CancellationToken>());
        await _configurationService.Received(1)
            .ApplyConfigurationAsync(Arg.Is<AuthorizationProviderConfigurationDto>(x =>
                x.Provider == "cerbos"
                && x.CerbosGrpcEndpoint == "https://cerbosgrpc.example.com:443"
                && x.CerbosEndpointVerified));
    }

    [Test]
    public async Task Handle_WhenCerbosAdminEndpointIsRejected_ReturnsFailureWithoutSaving()
    {
        _adminContext.IsInstanceAdminAsync(TestUserId, Arg.Any<CancellationToken>()).Returns(true);

        var configuration = new AuthorizationProviderConfigurationDto
        {
            Provider = "cerbos",
            CerbosGrpcEndpoint = "https://cerbosgrpc.example.com:443",
            CerbosAdminEndpoint = "https://localhost:3592"
        };

        _configurationService.VerifyCerbosEndpointAsync(configuration.CerbosGrpcEndpoint, Arg.Any<CancellationToken>())
            .Returns(true);
        _configurationService.VerifyCerbosAdminEndpointAsync(configuration.CerbosAdminEndpoint, Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await _handler.Handle(CreateCommand(configuration), CancellationToken.None);

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Message).Contains("Admin API endpoint");
        await _configurationService.DidNotReceive().ApplyConfigurationAsync(Arg.Any<AuthorizationProviderConfigurationDto>());
    }

    [Test]
    public async Task Handle_WhenCerbosEndpointDoesNotVerify_ReturnsFailureWithoutSaving()
    {
        _adminContext.IsInstanceAdminAsync(TestUserId, Arg.Any<CancellationToken>()).Returns(true);

        var configuration = new AuthorizationProviderConfigurationDto
        {
            Provider = "cerbos",
            CerbosGrpcEndpoint = "https://cerbosgrpc.example.com:443"
        };

        _configurationService.VerifyCerbosEndpointAsync(configuration.CerbosGrpcEndpoint, Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await _handler.Handle(CreateCommand(configuration), CancellationToken.None);

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Message).Contains("could not be verified");
        await _configurationService.DidNotReceive().ApplyConfigurationAsync(Arg.Any<AuthorizationProviderConfigurationDto>());
    }

    private static UpdateAuthorizationProviderConfigurationCommand CreateCommand(AuthorizationProviderConfigurationDto configuration)
    {
        return new UpdateAuthorizationProviderConfigurationCommand
        {
            UserId = TestUserId,
            Patch = new PatchAuthorizationProviderConfigurationDto
            {
                Configuration = OptionalUpdate<AuthorizationProviderConfigurationWriteDto>.Set(new AuthorizationProviderConfigurationWriteDto
                {
                    Provider = configuration.Provider,
                    CerbosGrpcEndpoint = configuration.CerbosGrpcEndpoint,
                    CerbosAdminEndpoint = configuration.CerbosAdminEndpoint
                })
            }
        };
    }
}
