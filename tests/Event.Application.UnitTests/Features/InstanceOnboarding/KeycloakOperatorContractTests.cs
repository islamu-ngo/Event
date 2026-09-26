using System.Text.Json;
using System.Text.Json.Serialization;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Features.InstanceOnboarding.Handlers.Commands;
using Explore.Application.Features.InstanceOnboarding.Requests;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Features.InstanceOnboarding.Services;
using NSubstitute;

namespace Event.Application.UnitTests.Features.InstanceOnboarding;

public sealed class KeycloakOperatorContractTests
{
    [Test]
    [Arguments("plan")]
    [Arguments("apply")]
    [Arguments("reconcile")]
    [Arguments("cancel")]
    public async Task MutatingCommands_RejectMissingAuthorityBeforeSideEffects(string action)
    {
        var authority = Substitute.For<IKeycloakOperatorAuthority>();
        authority.RequireAsync(Arg.Any<CancellationToken>())
            .Returns<Task<KeycloakOperatorAuthority>>(_ => throw new UnauthorizedAccessException());
        var repository = Substitute.For<IKeycloakOperationRepository>();
        var coordinator = Substitute.For<IKeycloakOperationCoordinator>();
        var client = Substitute.For<IKeycloakAdminClient>();
        var secrets = Substitute.For<ISecretResolver>();
        var resolver = new KeycloakConnectionResolver(secrets);
        var service = new KeycloakOperationService(repository, coordinator, client);
        var credentials = new KeycloakOperationCredentialsDto
        {
            AdministratorUsername = Guid.CreateVersion7().ToString("N"),
            AdministratorPassword = Guid.CreateVersion7().ToString("N")
        };
        Guid operationId = Guid.CreateVersion7();

        Func<Task<KeycloakOperationDto>> execute = action switch
        {
            "plan" => () => new PlanKeycloakOperationCommandHandler(
                    authority, resolver, client, service, TimeProvider.System)
                .ExecuteAsync(new PlanKeycloakOperationCommand(new KeycloakOperationPlanInputDto
                {
                    AdministratorUsername = credentials.AdministratorUsername,
                    AdministratorPassword = credentials.AdministratorPassword,
                    Intent = KeycloakOperationIntent.RepairClient
                }), CancellationToken.None),
            "apply" => () => new ApplyKeycloakOperationCommandHandler(
                    repository, authority, resolver, service, TimeProvider.System)
                .ExecuteAsync(new ApplyKeycloakOperationCommand(operationId, credentials), CancellationToken.None),
            "reconcile" => () => new ReconcileKeycloakOperationCommandHandler(
                    repository, authority, resolver, service, TimeProvider.System)
                .ExecuteAsync(new ReconcileKeycloakOperationCommand(operationId, credentials), CancellationToken.None),
            "cancel" => () => new CancelKeycloakOperationCommandHandler(
                    repository, coordinator, authority, TimeProvider.System)
                .ExecuteAsync(new CancelKeycloakOperationCommand(operationId), CancellationToken.None),
            _ => throw new ArgumentOutOfRangeException(nameof(action))
        };

        await Assert.ThrowsAsync<UnauthorizedAccessException>(execute);
        await Assert.That(repository.ReceivedCalls()).IsEmpty();
        await Assert.That(coordinator.ReceivedCalls()).IsEmpty();
        await Assert.That(client.ReceivedCalls()).IsEmpty();
        await Assert.That(secrets.ReceivedCalls()).IsEmpty();
    }

    [Test]
    public async Task CredentialRecords_AndContainingRequests_DoNotDiscloseCredentialsInDiagnostics()
    {
        string username = Guid.CreateVersion7().ToString("N");
        string password = Guid.CreateVersion7().ToString("N");
        KeycloakInspectionCredentialsDto[] inputs =
        [
            new() { AdministratorUsername = username, AdministratorPassword = password },
            new KeycloakOperationCredentialsDto { AdministratorUsername = username, AdministratorPassword = password },
            new KeycloakOperationPlanInputDto
            {
                AdministratorUsername = username,
                AdministratorPassword = password,
                Intent = KeycloakOperationIntent.CreateRealm
            }
        ];
        object[] requests =
        [
            new InspectKeycloakOperationQuery(inputs[0]),
            new PlanKeycloakOperationCommand((KeycloakOperationPlanInputDto)inputs[2]),
            new ApplyKeycloakOperationCommand(Guid.CreateVersion7(), (KeycloakOperationCredentialsDto)inputs[1]),
            new ReconcileKeycloakOperationCommand(Guid.CreateVersion7(), (KeycloakOperationCredentialsDto)inputs[1])
        ];

        foreach (object value in inputs.Cast<object>().Concat(requests))
        {
            await Assert.That(value.ToString()).DoesNotContain(username);
            await Assert.That(value.ToString()).DoesNotContain(password);
        }
    }

    [Test]
    [Arguments(KeycloakOperationIntent.RepairClient)]
    [Arguments(KeycloakOperationIntent.CreateClients)]
    [Arguments(KeycloakOperationIntent.CreateRealm)]
    public async Task PlanInput_PreservesCredentialFieldsAndStringIntent(KeycloakOperationIntent intent)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        var input = new KeycloakOperationPlanInputDto
        {
            AdministratorUsername = Guid.CreateVersion7().ToString("N"),
            AdministratorPassword = Guid.CreateVersion7().ToString("N"),
            Intent = intent
        };
        string json = JsonSerializer.Serialize(input, options);
        using JsonDocument document = JsonDocument.Parse(json);

        await Assert.That(document.RootElement.GetProperty("intent").GetString()).IsEqualTo(intent.ToString());
        await Assert.That(document.RootElement.GetProperty("administratorUsername").GetString()).IsEqualTo(input.AdministratorUsername);
        await Assert.That(document.RootElement.GetProperty("administratorPassword").GetString()).IsEqualTo(input.AdministratorPassword);
        await Assert.That(JsonSerializer.Deserialize<KeycloakOperationPlanInputDto>(json, options)).IsEqualTo(input);
    }
}
