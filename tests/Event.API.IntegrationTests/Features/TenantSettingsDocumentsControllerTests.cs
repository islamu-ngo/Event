using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.DTOs.TenantSettingsDocuments;
using Explore.Application.Settings;
using Explore.Domain.Settings.Documents;
using Explore.Domain.Settings.Documents.Payloads;
using Explore.Application.Exceptions;
using Explore.Application.Features.TenantSettingsDocuments.Requests.Commands;
using Explore.Application.Features.TenantSettingsDocuments.Requests.Queries;
using Explore.Application.Models.Common;
using Explore.Application.Responses;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using TUnit.Core;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel("ApiTestFixture")]
[ClassDataSource<ApiTestFixture>(Shared = SharedType.PerClass)]
public sealed class TenantSettingsDocumentsControllerAnonymousTests
{
    private readonly ApiTestFixture _fixture;

    public TenantSettingsDocumentsControllerAnonymousTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Test]
    public async Task GetBranding_WithoutAuth_ShouldReturnUnauthorized()
    {
        var response = await _fixture.Client.GetAsync("/api/tenant/settings/documents/branding");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task PatchBranding_WithoutAuth_ShouldReturnUnauthorized()
    {
        var request = new PatchTenantBrandingSettingsDocumentDto
        {
            ExpectedConcurrencyStamp = Guid.NewGuid(),
            DisplayName = new PatchTenantBrandingDisplayNameDto
            {
                Value = OptionalUpdate<string?>.Set("Unauthenticated Brand")
            }
        };

        using var message = new HttpRequestMessage(HttpMethod.Patch, "/api/tenant/settings/documents/branding")
        {
            Content = JsonContent.Create(request)
        };
        var response = await _fixture.Client.SendAsync(message);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task GetDirectoryOperatorIdentity_WithoutAuth_ShouldReturnUnauthorized()
    {
        var response = await _fixture.Client.GetAsync(
            "/api/tenant/settings/documents/directory-operator-identity");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }
}

public sealed class TenantSettingsDocumentsControllerAuthorizedTests
{
    [Test]
    public async Task GetBranding_WithAuth_ShouldReturnHalDocument()
    {
        var documentId = Guid.NewGuid();
        using var factory = CreateFactoryWithMediator(new BrandingDocumentMediator(documentId));
        using var client = factory.CreateClient();
        using var request = CreateAuthenticatedRequest(HttpMethod.Get, "/api/tenant/settings/documents/branding");

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            return;
        }

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;
        await Assert.That(root.GetProperty("documentKey").GetString()).IsEqualTo("tenant.branding");
        await Assert.That(root.GetProperty("payload").GetProperty("displayName").GetString()).IsEqualTo("Typed Tenant");
        await Assert.That(root.GetProperty("canChangeDisplayName").GetBoolean()).IsTrue();
        await Assert.That(root.GetProperty("_links").TryGetProperty("self", out _)).IsTrue();
        await Assert.That(root.GetProperty("_links").TryGetProperty("edit", out _)).IsTrue();
    }

    [Test]
    public async Task GetDirectoryOperatorIdentity_WithAuth_ShouldReturnHalDocument()
    {
        using var factory = CreateFactoryWithMediator(
            new BrandingDocumentMediator(Guid.CreateVersion7()));
        using var client = factory.CreateClient();
        using var request = CreateAuthenticatedRequest(
            HttpMethod.Get,
            "/api/tenant/settings/documents/directory-operator-identity");

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            return;
        }

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement root = body.RootElement;
        await Assert.That(root.GetProperty("documentKey").GetString())
            .IsEqualTo("tenant.directory_operator_identity");
        await Assert.That(root.GetProperty("payload").GetProperty("legalName").GetString())
            .IsEqualTo("Community Events ASBL");
        await Assert.That(root.GetProperty("_links").TryGetProperty("self", out _)).IsTrue();
        await Assert.That(root.GetProperty("_links").TryGetProperty("edit", out _)).IsTrue();
    }

    [Test]
    public async Task GetDirectoryOperatorIdentity_WhenResourceDenied_ReturnsForbiddenWithoutDocumentRead()
    {
        var resolver = Substitute.For<ITypedSettingsDocumentResolver>();
        using var factory = CreateFactoryWithResolver(
            resolver,
            new StubAuthorizationProvider { AllowAll = false });
        using var client = factory.CreateClient();
        using var request = CreateAuthenticatedRequest(
            HttpMethod.Get,
            "/api/tenant/settings/documents/directory-operator-identity");

        HttpResponseMessage response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        await resolver.DidNotReceiveWithAnyArgs()
            .ResolveTenantDocumentAsync<TenantDirectoryOperatorIdentitySettings>(
                default!, default!, default);
    }

    [Test]
    [Arguments("tenant-admin")]
    [Arguments("instance-admin")]
    public async Task GetDirectoryOperatorIdentity_WhenResourceAdminAllowed_ReturnsHal(
        string adminKind)
    {
        _ = adminKind;
        var resolver = Substitute.For<ITypedSettingsDocumentResolver>();
        resolver.ResolveTenantDocumentAsync<TenantDirectoryOperatorIdentitySettings>(
                Arg.Any<SettingsResolutionContext>(),
                SettingsDocumentKeys.Tenant.DirectoryOperatorIdentity,
                Arg.Any<CancellationToken>())
            .Returns(ReadyIdentityDocument());
        using var factory = CreateFactoryWithResolver(
            resolver,
            new StubAuthorizationProvider { AllowAll = true });
        using var client = factory.CreateClient();
        using var request = CreateAuthenticatedRequest(
            HttpMethod.Get,
            "/api/tenant/settings/documents/directory-operator-identity");

        HttpResponseMessage response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        await Assert.That(body.RootElement.GetProperty("_links").TryGetProperty("self", out _)).IsTrue();
        await resolver.Received(1).ResolveTenantDocumentAsync<TenantDirectoryOperatorIdentitySettings>(
            Arg.Any<SettingsResolutionContext>(),
            SettingsDocumentKeys.Tenant.DirectoryOperatorIdentity,
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task PatchDirectoryOperatorIdentity_WithAuth_ShouldReturnUpdatedHalDocument()
    {
        Guid initialStamp = Guid.CreateVersion7();
        var mediator = new BrandingDocumentMediator(
            Guid.CreateVersion7(),
            initialStamp);
        var cacheStore = Substitute.For<IOutputCacheStore>();
        using var factory = CreateFactoryWithMediator(mediator, cacheStore);
        using var client = factory.CreateClient();
        using var request = CreateAuthenticatedRequest(
            HttpMethod.Patch,
            "/api/tenant/settings/documents/directory-operator-identity");
        request.Content = JsonContent.Create(
            new PatchTenantDirectoryOperatorIdentityDocumentDto
            {
                ExpectedConcurrencyStamp = initialStamp,
                LegalEntity = new PatchTenantDirectoryOperatorLegalEntityDto
                {
                    LegalName = OptionalUpdate<string?>.Set("Updated Community Events ASBL")
                }
            });

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            return;
        }

        await Assert.That(mediator.LastIdentityPatchCommand).IsNotNull();
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement root = body.RootElement;
        await Assert.That(root.GetProperty("payload").GetProperty("legalName").GetString())
            .IsEqualTo("Updated Community Events ASBL");
        await Assert.That(root.GetProperty("_links").TryGetProperty("edit", out _)).IsTrue();
        await cacheStore.Received(1).EvictByTagAsync(
            "public-experience-shell",
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task PatchBranding_WithAuth_ShouldReturnUpdatedHalDocumentAndEvictShellOnce()
    {
        var documentId = Guid.NewGuid();
        var initialStamp = Guid.NewGuid();
        var mediator = new BrandingDocumentMediator(documentId, initialStamp);
        var cacheStore = Substitute.For<IOutputCacheStore>();
        using var factory = CreateFactoryWithMediator(mediator, cacheStore);
        using var client = factory.CreateClient();
        using var request = CreateAuthenticatedRequest(HttpMethod.Patch, "/api/tenant/settings/documents/branding");
        request.Content = JsonContent.Create(new PatchTenantBrandingSettingsDocumentDto
        {
            ExpectedConcurrencyStamp = initialStamp,
            DisplayName = new PatchTenantBrandingDisplayNameDto
            {
                Value = OptionalUpdate<string?>.Set("Updated Tenant")
            }
        });

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(mediator.LastPatchCommand).IsNotNull();
        await Assert.That(mediator.LastPatchCommand!.Patch.ExpectedConcurrencyStamp).IsEqualTo(initialStamp);
        await Assert.That(mediator.BrandingQueryCount).IsEqualTo(1);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;
        await Assert.That(root.GetProperty("payload").GetProperty("displayName").GetString()).IsEqualTo("Updated Tenant");
        await Assert.That(root.GetProperty("payload").GetProperty("customCssUrl").GetString()).IsEqualTo("https://cdn.example.test/tenant.css");
        await Assert.That(root.GetProperty("concurrencyStamp").GetGuid()).IsEqualTo(mediator.ReloadedConcurrencyStamp!.Value);
        await Assert.That(root.GetProperty("concurrencyStamp").GetGuid()).IsNotEqualTo(mediator.CommandResponseConcurrencyStamp!.Value);
        await Assert.That(root.GetProperty("_links").TryGetProperty("edit", out _)).IsTrue();
        await cacheStore.Received(1).EvictByTagAsync("public-experience-shell", Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task PatchBranding_WhenCommandFails_ShouldNotEvictShell()
    {
        var cacheStore = Substitute.For<IOutputCacheStore>();
        var mediator = new BrandingDocumentMediator(Guid.NewGuid(), patchSucceeds: false);
        using var factory = CreateFactoryWithMediator(mediator, cacheStore);
        using var client = factory.CreateClient();
        using var request = CreateAuthenticatedRequest(HttpMethod.Patch, "/api/tenant/settings/documents/branding");
        request.Content = JsonContent.Create(new PatchTenantBrandingSettingsDocumentDto
        {
            ExpectedConcurrencyStamp = Guid.NewGuid(),
            DisplayName = new PatchTenantBrandingDisplayNameDto
            {
                Value = OptionalUpdate<string?>.Set("Rejected Tenant")
            }
        });

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await cacheStore.DidNotReceive().EvictByTagAsync("public-experience-shell", Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task PatchBranding_WhenAuthoritativeReloadIsMissing_ShouldReturnNotFoundWithoutEvictingShell()
    {
        var cacheStore = Substitute.For<IOutputCacheStore>();
        var mediator = new BrandingDocumentMediator(Guid.NewGuid(), reloadMissingAfterPatch: true);
        using var factory = CreateFactoryWithMediator(mediator, cacheStore);
        using var client = factory.CreateClient();
        using var request = CreateAuthenticatedRequest(HttpMethod.Patch, "/api/tenant/settings/documents/branding");
        request.Content = JsonContent.Create(new PatchTenantBrandingSettingsDocumentDto
        {
            ExpectedConcurrencyStamp = Guid.NewGuid(),
            DisplayName = new PatchTenantBrandingDisplayNameDto
            {
                Value = OptionalUpdate<string?>.Set("Updated Tenant")
            }
        });

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(mediator.LastPatchCommand).IsNotNull();
        await Assert.That(mediator.BrandingQueryCount).IsEqualTo(1);
        await cacheStore.DidNotReceive().EvictByTagAsync("public-experience-shell", Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task PatchBranding_WhenPersistenceRaceOccurs_ShouldReturnConflictWithoutReloadOrEviction()
    {
        var documentId = Guid.NewGuid();
        var cacheStore = Substitute.For<IOutputCacheStore>();
        var mediator = new BrandingDocumentMediator(
            documentId,
            patchException: new ConcurrencyConflictException(
                ConcurrencyConflictException.ConcurrentUpdate,
                "The TenantSettingsDocument was modified by another request. Reload and retry.",
                "TenantSettingsDocument",
                documentId.ToString()));
        using var factory = CreateFactoryWithMediator(mediator, cacheStore);
        using var client = factory.CreateClient();
        using var request = CreateAuthenticatedPatchRequest();

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        await Assert.That(body.RootElement.GetProperty("type").GetString()).IsEqualTo("/problems/concurrent_update");
        await Assert.That(body.RootElement.GetProperty("code").GetString()).IsEqualTo(ConcurrencyConflictException.ConcurrentUpdate);
        await Assert.That(mediator.BrandingQueryCount).IsEqualTo(0);
        await cacheStore.DidNotReceive().EvictByTagAsync("public-experience-shell", Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task PatchBranding_WhenPersistedPayloadIsIncompatible_ShouldReturnSafeErrorWithoutReloadOrEviction()
    {
        const string safeExceptionMessage = "Document 'tenant.branding' payload could not be deserialized.";
        var cacheStore = Substitute.For<IOutputCacheStore>();
        var mediator = new BrandingDocumentMediator(
            Guid.NewGuid(),
            patchException: new InvalidOperationException(safeExceptionMessage));
        using var factory = CreateFactoryWithMediator(mediator, cacheStore);
        using var client = factory.CreateClient();
        using var request = CreateAuthenticatedPatchRequest();

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.InternalServerError);
        var responseBody = await response.Content.ReadAsStringAsync();
        await Assert.That(responseBody).DoesNotContain(safeExceptionMessage);
        await Assert.That(mediator.BrandingQueryCount).IsEqualTo(0);
        await cacheStore.DidNotReceive().EvictByTagAsync("public-experience-shell", Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task PutBranding_WithAuth_ShouldReturnMethodNotAllowed()
    {
        var cacheStore = Substitute.For<IOutputCacheStore>();
        var mediator = new BrandingDocumentMediator(Guid.NewGuid());
        using var factory = CreateFactoryWithMediator(mediator, cacheStore);
        using var client = factory.CreateClient();
        using var request = CreateAuthenticatedRequest(HttpMethod.Put, "/api/tenant/settings/documents/branding");

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.MethodNotAllowed);
        await Assert.That(mediator.LastPatchCommand).IsNull();
        await cacheStore.DidNotReceive().EvictByTagAsync("public-experience-shell", Arg.Any<CancellationToken>());
    }

    private static WebApplicationFactory<Program> CreateFactoryWithResolver(
        ITypedSettingsDocumentResolver resolver,
        IAuthorizationProvider authorizationProvider)
    {
        var factory = new AuthenticatedWebApplicationFactory
        {
            AuthorizationProviderOverride = authorizationProvider
        };
        return factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<ITypedSettingsDocumentResolver>();
            services.AddSingleton(resolver);
        }));
    }

    private static ResolvedSettingsDocument<TenantDirectoryOperatorIdentitySettings> ReadyIdentityDocument() =>
        new()
        {
            DocumentKey = SettingsDocumentKeys.Tenant.DirectoryOperatorIdentity,
            SchemaVersion = TenantDirectoryOperatorIdentityDocumentDefaults.SchemaVersion,
            DefaultsVersion = TenantDirectoryOperatorIdentityDocumentDefaults.DefaultsVersion,
            Payload = new TenantDirectoryOperatorIdentitySettings
            {
                PublicName = "Community Events",
                LegalName = "Community Events ASBL",
                OperatorKindCode = "registered_organization",
                JurisdictionCountryCode = "BE",
                PublicContactEmail = "contact.test",
                LegalNoticeUrl = "https://example.test/legal",
                PrivacyUrl = "https://example.test/privacy"
            },
            Source = SettingsDocumentSource.Tenant,
            SourceScopeId = Explore.Domain.Constants.PlatformDefaults.DefaultTenantId,
            ConcurrencyStamp = Guid.CreateVersion7()
        };

    private static WebApplicationFactory<Program> CreateFactoryWithMediator(
        IMediator mediator,
        IOutputCacheStore? cacheStore = null)
    {
        var factory = new AuthenticatedWebApplicationFactory
        {
            AuthorizationProviderOverride = new StubAuthorizationProvider { AllowAll = true }
        };

        return factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IMediator>();
                services.AddSingleton(mediator);
                if (cacheStore is not null)
                {
                    services.RemoveAll<IOutputCacheStore>();
                    services.AddSingleton(cacheStore);
                }
            });
        });
    }

    private static HttpRequestMessage CreateAuthenticatedRequest(HttpMethod method, string url)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add(TestAuthHandler.AuthHeaderName, TestAuthHandler.CreateAuthHeaderValue(Guid.NewGuid()));
        return request;
    }

    private static HttpRequestMessage CreateAuthenticatedPatchRequest()
    {
        var request = CreateAuthenticatedRequest(HttpMethod.Patch, "/api/tenant/settings/documents/branding");
        request.Content = JsonContent.Create(new PatchTenantBrandingSettingsDocumentDto
        {
            ExpectedConcurrencyStamp = Guid.NewGuid(),
            DisplayName = new PatchTenantBrandingDisplayNameDto
            {
                Value = OptionalUpdate<string?>.Set("Updated Tenant")
            }
        });
        return request;
    }

    private sealed class BrandingDocumentMediator : IMediator
    {
        private readonly Guid _documentId;
        private readonly bool _patchSucceeds;
        private readonly bool _reloadMissingAfterPatch;
        private readonly Exception? _patchException;
        private TenantBrandingSettingsDocumentDto _document;
        private TenantDirectoryOperatorIdentityDocumentDto _identityDocument;
        private bool _patchHandled;

        public BrandingDocumentMediator(
            Guid documentId,
            Guid? concurrencyStamp = null,
            bool patchSucceeds = true,
            bool reloadMissingAfterPatch = false,
            Exception? patchException = null)
        {
            _documentId = documentId;
            _patchSucceeds = patchSucceeds;
            _reloadMissingAfterPatch = reloadMissingAfterPatch;
            _patchException = patchException;
            _document = CreateDocument(
                displayName: "Typed Tenant",
                logoUrl: "https://cdn.example.test/logo.svg",
                faviconUrl: "https://cdn.example.test/favicon.ico",
                customCssUrl: "https://cdn.example.test/tenant.css",
                concurrencyStamp: concurrencyStamp ?? Guid.NewGuid());
            _identityDocument = CreateIdentityDocument(_document.ConcurrencyStamp);
        }

        public PatchTenantBrandingSettingsDocumentCommand? LastPatchCommand { get; private set; }

        public PatchTenantDirectoryOperatorIdentityDocumentCommand? LastIdentityPatchCommand
        {
            get;
            private set;
        }

        public int BrandingQueryCount { get; private set; }

        public Guid? CommandResponseConcurrencyStamp { get; private set; }

        public Guid? ReloadedConcurrencyStamp { get; private set; }

        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification
            => Task.CompletedTask;

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            object? response = request switch
            {
                GetTenantBrandingSettingsDocumentQuery => GetDocument(),
                PatchTenantBrandingSettingsDocumentCommand command => Patch(command),
                GetTenantDirectoryOperatorIdentityDocumentQuery => _identityDocument,
                PatchTenantDirectoryOperatorIdentityDocumentCommand command =>
                    PatchIdentity(command),
                _ => throw new InvalidOperationException($"Unexpected request type {request.GetType().Name}.")
            };

            return Task.FromResult((TResponse)response);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
            => Task.CompletedTask;

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
            => request switch
            {
                GetTenantBrandingSettingsDocumentQuery => Task.FromResult<object?>(GetDocument()),
                PatchTenantBrandingSettingsDocumentCommand command => Task.FromResult<object?>(Patch(command)),
                GetTenantDirectoryOperatorIdentityDocumentQuery =>
                    Task.FromResult<object?>(_identityDocument),
                PatchTenantDirectoryOperatorIdentityDocumentCommand command =>
                    Task.FromResult<object?>(PatchIdentity(command)),
                _ => throw new InvalidOperationException($"Unexpected request type {request.GetType().Name}.")
            };

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        private TenantBrandingSettingsDocumentDto? GetDocument()
        {
            BrandingQueryCount++;
            return _patchHandled && _reloadMissingAfterPatch ? null : _document;
        }

        private BaseCommandResponse<TenantDirectoryOperatorIdentityDocumentDto> PatchIdentity(
            PatchTenantDirectoryOperatorIdentityDocumentCommand command)
        {
            LastIdentityPatchCommand = command;
            string? legalName = Apply(
                command.Patch.LegalEntity?.LegalName ?? default,
                _identityDocument.Payload.LegalName);
            _identityDocument = _identityDocument with
            {
                Payload = _identityDocument.Payload with { LegalName = legalName },
                ConcurrencyStamp = Guid.CreateVersion7()
            };
            return BaseCommandResponse.Success(
                _identityDocument,
                "Tenant directory operator identity patched.");
        }

        private BaseCommandResponse<TenantBrandingSettingsDocumentDto> Patch(
            PatchTenantBrandingSettingsDocumentCommand command)
        {
            LastPatchCommand = command;
            if (_patchException is not null)
            {
                throw _patchException;
            }

            if (!_patchSucceeds)
            {
                return BaseCommandResponse.Validation<TenantBrandingSettingsDocumentDto>(
                    ["Rejected for test."],
                    "Tenant branding settings patch failed.");
            }

            var displayName = Apply(command.Patch.DisplayName?.Value ?? default, _document.Payload.DisplayName);
            var logoUrl = Apply(command.Patch.Assets?.LogoUrl ?? default, _document.Payload.LogoUrl);
            var faviconUrl = Apply(command.Patch.Assets?.FaviconUrl ?? default, _document.Payload.FaviconUrl);
            var customCssUrl = Apply(command.Patch.Assets?.CustomCssUrl ?? default, _document.Payload.CustomCssUrl);
            _patchHandled = true;
            ReloadedConcurrencyStamp = Guid.NewGuid();
            _document = CreateDocument(
                displayName,
                logoUrl,
                faviconUrl,
                customCssUrl,
                ReloadedConcurrencyStamp.Value);

            CommandResponseConcurrencyStamp = Guid.NewGuid();
            var commandResponseDocument = CreateDocument(
                "Command response must not be assembled",
                logoUrl,
                faviconUrl,
                customCssUrl,
                CommandResponseConcurrencyStamp.Value);

            return BaseCommandResponse.Success(
                commandResponseDocument,
                "Tenant branding settings document patched.");
        }

        private static string? Apply(OptionalUpdate<string?> update, string? current)
            => update.HasValue ? update.Value : current;

        private TenantBrandingSettingsDocumentDto CreateDocument(
            string? displayName,
            string? logoUrl,
            string? faviconUrl,
            string? customCssUrl,
            Guid concurrencyStamp)
            => new()
            {
                DocumentKey = "tenant.branding",
                SchemaVersion = 1,
                DefaultsVersion = "2026-05-14",
                Payload = new TenantBrandingSettingsPayloadDto
                {
                    DisplayName = displayName,
                    LogoUrl = logoUrl,
                    FaviconUrl = faviconUrl,
                    CustomCssUrl = customCssUrl
                },
                Source = "tenant",
                SourceScopeId = _documentId,
                ConcurrencyStamp = concurrencyStamp,
                CanChangeDisplayName = true,
                CanChangeLogoUrl = true,
                CanChangeFaviconUrl = true,
                CanChangeCustomCssUrl = true,
                UpdatedAt = DateTime.UtcNow
            };

        private TenantDirectoryOperatorIdentityDocumentDto CreateIdentityDocument(
            Guid concurrencyStamp) =>
            new()
            {
                DocumentKey = "tenant.directory_operator_identity",
                SchemaVersion = 1,
                DefaultsVersion = "2026-08-28",
                Payload = new TenantDirectoryOperatorIdentityPayloadDto
                {
                    PublicName = "Community Events",
                    LegalName = "Community Events ASBL",
                    OperatorKindCode = "registered_organization",
                    JurisdictionCountryCode = "BE",
                    PublicContactEmail = "contact@example.test",
                    LegalNoticeUrl = "https://example.test/legal",
                    PrivacyUrl = "https://example.test/privacy"
                },
                Source = "tenant",
                SourceScopeId = _documentId,
                ConcurrencyStamp = concurrencyStamp,
                IsActivationReady = true,
                IsPublicDisclosureReady = true,
                CanEdit = true
            };
    }
}
