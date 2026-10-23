using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Explore.API.BackgroundServices;
using Explore.API.Controllers;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Secrets;
using Explore.Application.DTOs.Ai;
using Explore.Application.Features.AiAssistant;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Features.AiAssistant.Handlers.Queries;
using Explore.Application.Features.AiAssistant.Requests.Commands;
using Explore.Application.Features.AiAssistant.Requests.Queries;
using Explore.Application.Features.TenantOnboarding.Requests.Queries;
using Explore.Application.Responses;
using Explore.Domain.Ai;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Infrastructure.Ai;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Event.Api.IntegrationTests.Features;

public sealed class AiAssistantModelDiscoverySecurityTests
{
    [Test]
    [Arguments(false, false, AiModelDiscoveryScope.Tenant)]
    [Arguments(true, false, AiModelDiscoveryScope.Instance)]
    public async Task UnauthorizedScope_IsForbiddenBeforeAnyOutboundOperation(
        bool tenantAdmin, bool platformAdmin, AiModelDiscoveryScope scope)
    {
        var query = Query(tenantAdmin, platformAdmin, new RejectClientFactory());
        var controller = Controller(query);

        var response = await controller.GetModels(new AiAssistantModelDiscoveryRequestDto
        {
            Scope = scope,
            EndpointUrl = "https://ai.operator.test/v1",
            CredentialScope = AiCredentialScope.Tenant
        });

        await Assert.That((response.Result as ObjectResult)?.StatusCode).IsEqualTo(StatusCodes.Status403Forbidden);
    }

    [Test]
    public async Task PlatformAdministrator_TenantRequestCannotBorrowInstanceLocalOptIn()
    {
        var controller = Controller(Query(false, true, new RejectClientFactory()));
        var response = await controller.GetModels(new AiAssistantModelDiscoveryRequestDto
        {
            EndpointUrl = "http://127.0.0.1:11434/v1",
            CredentialScope = AiCredentialScope.None
        });

        await Assert.That((response.Result as ObjectResult)?.StatusCode).IsEqualTo(StatusCodes.Status400BadRequest);
    }

    [Test]
    public async Task PlatformAdministrator_InstanceRequestCannotChooseUnconfiguredOrigin()
    {
        var controller = Controller(Query(false, true, new RejectClientFactory()));
        var response = await controller.GetModels(new AiAssistantModelDiscoveryRequestDto
        {
            Scope = AiModelDiscoveryScope.Instance,
            EndpointUrl = "https://different.operator.test/v1",
            CredentialScope = AiCredentialScope.None
        });

        await Assert.That((response.Result as ObjectResult)?.StatusCode).IsEqualTo(StatusCodes.Status400BadRequest);
    }

    [Test]
    public async Task UnknownScope_IsRejectedBeforeOutboundOperation()
    {
        var controller = Controller(Query(true, true, new RejectClientFactory()));
        var response = await controller.GetModels(new AiAssistantModelDiscoveryRequestDto
        {
            Scope = (AiModelDiscoveryScope)999,
            EndpointUrl = "https://ai.tenant.test/v1"
        });

        await Assert.That((response.Result as ObjectResult)?.StatusCode).IsEqualTo(StatusCodes.Status400BadRequest);
    }

    [Test]
    public async Task ProviderException_DoesNotReflectEndpointOrCredentialIntoProblemDetails()
    {
        string canary = Guid.NewGuid().ToString("N");
        using var client = new HttpClient(new DiscoveryHandler(_ =>
            throw new HttpRequestException($"https://sensitive.operator.test/{canary}")));
        var controller = Controller(Query(true, false, new SingleClientFactory(client), canary));
        var response = await controller.GetModels(new AiAssistantModelDiscoveryRequestDto
        {
            EndpointUrl = "https://ai.tenant.test/v1",
            CredentialScope = AiCredentialScope.Tenant
        });
        var result = response.Result as ObjectResult;
        var problem = result?.Value as ProblemDetails;

        await Assert.That(result?.StatusCode).IsEqualTo(StatusCodes.Status502BadGateway);
        await Assert.That(problem).IsNotNull();
        await Assert.That(problem!.Detail).DoesNotContain(canary);
        await Assert.That(problem.Detail).DoesNotContain("sensitive.operator.test");
    }

    [Test]
    [Arguments(AiModelDiscoveryScope.Tenant, true, false, "")]
    [Arguments(AiModelDiscoveryScope.Instance, false, true, ".Instance")]
    public async Task AuthorizedExplicitScope_SelectsItsOwnTransportAndReturnsDiscoveredModels(
        AiModelDiscoveryScope scope, bool tenantAdmin, bool platformAdmin, string suffix)
    {
        Uri? target = null;
        AuthenticationHeaderValue? authorization = null;
        using var client = new HttpClient(new DiscoveryHandler(request =>
        {
            target = request.RequestUri;
            authorization = request.Headers.Authorization;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"data":[{"id":"model-a"}]}""", Encoding.UTF8, "application/json")
            };
        }));
        var factory = new SingleClientFactory(client);
        string canary = Guid.NewGuid().ToString("N");
        var controller = Controller(Query(tenantAdmin, platformAdmin, factory, canary));
        var response = await controller.GetModels(new AiAssistantModelDiscoveryRequestDto
        {
            Scope = scope,
            EndpointUrl = "https://ai.operator.test/v1",
            CredentialScope = scope == AiModelDiscoveryScope.Instance
                ? AiCredentialScope.Instance : AiCredentialScope.Tenant
        });

        var models = (response.Result as OkObjectResult)?.Value as IReadOnlyList<AiAssistantModelDto>;
        await Assert.That(models?.Single().Id).IsEqualTo("model-a");
        await Assert.That(factory.ClientName).IsEqualTo(OpenAiCompatibleChatProvider.HttpClientName + suffix);
        await Assert.That(target).IsEqualTo(new Uri("https://ai.operator.test/v1/models"));
        await Assert.That(authorization?.Parameter).IsEqualTo(canary);
    }

    private static DiscoverAiAssistantModelsQueryHandler Query(
        bool tenantAdmin, bool platformAdmin, IHttpClientFactory clients, string? credential = null)
    {
        var tenant = Substitute.For<ITenantContext>();
        var tenantId = Guid.CreateVersion7();
        tenant.TenantId.Returns(tenantId);
        var secrets = Substitute.For<ISecretResolver>();
        if (credential is not null)
        {
            foreach (var scope in new[] { SecretScope.Instance, SecretScope.Tenant })
            {
                Guid? scopeId = scope == SecretScope.Tenant ? tenantId : null;
                var resolved = SecretResolutionResult.Resolved(new ResolvedSecret(
                    SecretDefinitionRegistry.Keys.Ai.OpenAiCompatibleApiKey, credential,
                    SecretSourceType.EnvironmentVariable, scope, scopeId, DateTimeOffset.UtcNow));
                if (scope == SecretScope.Instance)
                    secrets.ResolveAsync(SecretDefinitionRegistry.Keys.Ai.OpenAiCompatibleApiKey,
                        null, Arg.Any<CancellationToken>()).Returns(resolved);
                else
                    secrets.ResolveQualifiedAsync(SecretDefinitionRegistry.Keys.Ai.OpenAiCompatibleApiKey,
                        scope, scopeId, "", Arg.Any<CancellationToken>()).Returns(resolved);
            }
        }

        return new(
        new StatusHandler(new TenantOnboardingStatusDto
        {
            IsAuthenticated = true,
            IsCurrentUserTenantAdministrator = tenantAdmin,
            IsCurrentUserPlatformAdministrator = platformAdmin
        }),
        new AiModelDiscoveryGateway(clients, new AiProviderConfigurationResolver(Options.Create(new AiProviderSettings
        {
            Enabled = true,
            Provider = AiProviderSettings.ProviderOpenAiCompatible,
            EndpointUrl = "https://ai.operator.test/v1",
            CredentialScope = credential is null ? AiCredentialScope.None : AiCredentialScope.Instance,
            ModelId = "configured-model",
            AllowLocalProviderEndpoints = true
        }), Substitute.For<IHierarchicalSettingsResolver>(), tenant, secrets, new AiProviderSettingsValidator())));
    }

    private static AiAssistantController Controller(
        IQueryHandler<DiscoverAiAssistantModelsQuery, AiModelDiscoveryResult> discovery) => new(
        Substitute.For<IQueryHandler<GetAiAssistantBootstrapQuery, AiAssistantBootstrapDto>>(),
        Substitute.For<IQueryHandler<GetAiConversationListQuery, IReadOnlyList<AiConversationSummaryDto>>>(),
        Substitute.For<IQueryHandler<GetAiConversationDetailQuery, AiConversationDto?>>(),
        Substitute.For<IQueryHandler<SearchAiReferencesQuery, IReadOnlyList<AiReferenceSearchResultDto>>>(),
        Substitute.For<IQueryHandler<GetAiRunStatusQuery, AiRunDto?>>(),
        Substitute.For<ICommandHandler<CreateAiConversationCommand, BaseCommandResponse<Guid>>>(),
        Substitute.For<ICommandHandler<SendAiMessageCommand, BaseCommandResponse<Guid>>>(),
        Substitute.For<ICommandHandler<ConfirmAiProposedActionCommand, BaseCommandResponse<Guid>>>(),
        Substitute.For<ICommandHandler<RejectAiProposedActionCommand, BaseCommandResponse<Guid>>>(),
        Substitute.For<ICommandHandler<CancelAiRunCommand, BaseCommandResponse<Guid>>>(),
        discovery,
        Substitute.For<IHateoasLinkGenerator>(),
        Substitute.For<IResourceAssembler<AiConversationDto, AiConversationSummaryDto>>(),
        Substitute.For<IAiAssistantRunQueue>(),
        Substitute.For<ITenantContext>())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

    private sealed class StatusHandler(TenantOnboardingStatusDto status)
        : IQueryHandler<GetTenantOnboardingStatusQuery, TenantOnboardingStatusDto>
    {
        public Task<TenantOnboardingStatusDto> QueryAsync(
            GetTenantOnboardingStatusQuery request, CancellationToken cancellationToken = default) =>
            Task.FromResult(status);
    }

    private sealed class RejectClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            throw new InvalidOperationException("A denied discovery scope must not create an outbound client.");
    }

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public string? ClientName { get; private set; }
        public HttpClient CreateClient(string name)
        {
            ClientName = name;
            return client;
        }
    }

    private sealed class DiscoveryHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }
}
