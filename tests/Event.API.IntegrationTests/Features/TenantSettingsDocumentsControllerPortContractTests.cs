using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Controllers;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.TenantSettingsDocuments;
using Explore.Application.Features.TenantSettingsDocuments.Requests.Commands;
using Explore.Application.Features.TenantSettingsDocuments.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Models.Common;
using Explore.Application.Responses;
using Explore.Domain.Constants;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Event.Api.IntegrationTests.Features;

/// <summary>
/// Focused controller contracts intentionally script exact native ports to distinguish command
/// results from authoritative reloads, including nullable results unreachable with today's real
/// branding provisioner. Real SQLite HTTP and authorization coverage remains in the adjacent suite.
/// </summary>
[NotInParallel("ApiTestFixture")]
public sealed class TenantSettingsDocumentsControllerPortContractTests
{
    private const string CacheKey = "controller-port-contract-shell";
    private static readonly byte[] Shell = [19, 41, 67];

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task FocusedBrandingPatch_AssemblesOnlyReloadAndKeepsCacheWhenReloadIsNull(bool reloadMissing)
    {
        await using var factory = new AuthenticatedWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var cache = scope.ServiceProvider.GetRequiredService<IOutputCacheStore>();
        await cache.SetAsync(CacheKey, Shell, ["public-experience-shell"], TimeSpan.FromMinutes(5), default);
        var commandDocument = new TenantBrandingSettingsDocumentDto
        {
            DocumentKey = "tenant.branding",
            SchemaVersion = 1,
            DefaultsVersion = "test",
            Payload = new() { DisplayName = "Command response must not be assembled" },
            Source = "Tenant",
            SourceScopeId = PlatformDefaults.DefaultTenantId,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        var authoritative = commandDocument with
        {
            Payload = new() { DisplayName = "Authoritative reload" },
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        var ensure = Substitute.For<ICommandHandler<EnsureTenantBrandingSettingsDocumentCommand, TenantBrandingSettingsDocumentDto?>>();
        ensure.ExecuteAsync(Arg.Any<EnsureTenantBrandingSettingsDocumentCommand>(), Arg.Any<CancellationToken>())
            .Returns(reloadMissing ? null : authoritative);
        var patch = Substitute.For<ICommandHandler<PatchTenantBrandingSettingsDocumentCommand, BaseCommandResponse<TenantBrandingSettingsDocumentDto>>>();
        patch.ExecuteAsync(Arg.Any<PatchTenantBrandingSettingsDocumentCommand>(), Arg.Any<CancellationToken>())
            .Returns(BaseCommandResponse.Success(commandDocument));
        var controller = Controller(scope, ensure, patch,
            Substitute.For<IQueryHandler<GetTenantDirectoryOperatorIdentityDocumentQuery, TenantDirectoryOperatorIdentityDocumentDto?>>(),
            Substitute.For<ICommandHandler<PatchTenantDirectoryOperatorIdentityDocumentCommand, BaseCommandResponse<TenantDirectoryOperatorIdentityDocumentDto>>>());

        var result = await controller.PatchBranding(new()
        {
            ExpectedConcurrencyStamp = commandDocument.ConcurrencyStamp,
            DisplayName = new() { Value = OptionalUpdate<string?>.Set("Patch intent") }
        }, cache);

        if (reloadMissing)
        {
            await AssertNotFoundWithRetainedCacheAsync(result.Result, cache);
        }
        else
        {
            var ok = result.Result as OkObjectResult;
            await Assert.That(ok).IsNotNull();
            var resource = ok!.Value as HalResource<TenantBrandingSettingsDocumentDto>;
            await Assert.That(resource).IsNotNull();
            await Assert.That(resource!.Data).IsEqualTo(authoritative);
            await Assert.That(resource.Data.ConcurrencyStamp).IsNotEqualTo(commandDocument.ConcurrencyStamp);
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(resource, JsonSerializerOptions.Web));
            await Assert.That(json.RootElement.GetProperty("payload").GetProperty("displayName").GetString()).IsEqualTo("Authoritative reload");
            await Assert.That(await cache.GetAsync(CacheKey, default)).IsNull();
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task FocusedIdentityPatch_AssemblesOnlyReloadAndKeepsCacheWhenReloadIsNull(bool reloadMissing)
    {
        await using var factory = new AuthenticatedWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var cache = scope.ServiceProvider.GetRequiredService<IOutputCacheStore>();
        await cache.SetAsync(CacheKey, Shell, ["public-experience-shell"], TimeSpan.FromMinutes(5), default);
        var commandDocument = new TenantDirectoryOperatorIdentityDocumentDto
        {
            DocumentKey = "tenant.directory_operator_identity",
            SchemaVersion = 1,
            DefaultsVersion = "test",
            Payload = new() { LegalName = "Command response must not be assembled" },
            Source = "Tenant",
            SourceScopeId = PlatformDefaults.DefaultTenantId,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        var authoritative = commandDocument with
        {
            Payload = new() { LegalName = "Authoritative operator ASBL" },
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        var query = Substitute.For<IQueryHandler<GetTenantDirectoryOperatorIdentityDocumentQuery, TenantDirectoryOperatorIdentityDocumentDto?>>();
        query.QueryAsync(Arg.Any<GetTenantDirectoryOperatorIdentityDocumentQuery>(), Arg.Any<CancellationToken>())
            .Returns(reloadMissing ? null : authoritative);
        var patch = Substitute.For<ICommandHandler<PatchTenantDirectoryOperatorIdentityDocumentCommand, BaseCommandResponse<TenantDirectoryOperatorIdentityDocumentDto>>>();
        patch.ExecuteAsync(Arg.Any<PatchTenantDirectoryOperatorIdentityDocumentCommand>(), Arg.Any<CancellationToken>())
            .Returns(BaseCommandResponse.Success(commandDocument));
        var controller = Controller(scope,
            Substitute.For<ICommandHandler<EnsureTenantBrandingSettingsDocumentCommand, TenantBrandingSettingsDocumentDto?>>(),
            Substitute.For<ICommandHandler<PatchTenantBrandingSettingsDocumentCommand, BaseCommandResponse<TenantBrandingSettingsDocumentDto>>>(),
            query, patch);

        var result = await controller.PatchDirectoryOperatorIdentity(new()
        {
            ExpectedConcurrencyStamp = commandDocument.ConcurrencyStamp,
            LegalEntity = new() { LegalName = OptionalUpdate<string?>.Set("Patch intent") }
        }, cache);

        if (reloadMissing)
        {
            await AssertNotFoundWithRetainedCacheAsync(result.Result, cache);
        }
        else
        {
            var ok = result.Result as OkObjectResult;
            await Assert.That(ok).IsNotNull();
            var resource = ok!.Value as HalResource<TenantDirectoryOperatorIdentityDocumentDto>;
            await Assert.That(resource).IsNotNull();
            await Assert.That(resource!.Data).IsSameReferenceAs(authoritative);
            await Assert.That(resource.Data.ConcurrencyStamp).IsNotEqualTo(commandDocument.ConcurrencyStamp);
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(resource, JsonSerializerOptions.Web));
            await Assert.That(json.RootElement.GetProperty("payload").GetProperty("legalName").GetString()).IsEqualTo("Authoritative operator ASBL");
            await Assert.That(await cache.GetAsync(CacheKey, default)).IsNull();
        }
    }

    private static TenantSettingsDocumentsController Controller(IServiceScope scope,
        ICommandHandler<EnsureTenantBrandingSettingsDocumentCommand, TenantBrandingSettingsDocumentDto?> ensure,
        ICommandHandler<PatchTenantBrandingSettingsDocumentCommand, BaseCommandResponse<TenantBrandingSettingsDocumentDto>> patchBranding,
        IQueryHandler<GetTenantDirectoryOperatorIdentityDocumentQuery, TenantDirectoryOperatorIdentityDocumentDto?> query,
        ICommandHandler<PatchTenantDirectoryOperatorIdentityDocumentCommand, BaseCommandResponse<TenantDirectoryOperatorIdentityDocumentDto>> patchIdentity)
    {
        var services = scope.ServiceProvider;
        services.GetRequiredService<ITenantContextAccessor>().SetTenant(PlatformDefaults.DefaultTenantId);
        var context = new DefaultHttpContext { RequestServices = services };
        // Use the real HAL assembler's public minimal representation; the HTTP suite owns links.
        context.Request.Headers["Prefer"] = "return=minimal";
        return new(ensure, patchBranding, query, patchIdentity,
            services.GetRequiredService<ITenantContext>(),
            services.GetRequiredService<ITenantBrandingSettingsDocumentLockService>(),
            services.GetRequiredService<IResourceAssembler<TenantBrandingSettingsDocumentDto, TenantBrandingSettingsDocumentDto>>(),
            services.GetRequiredService<IResourceAssembler<TenantDirectoryOperatorIdentityDocumentDto, TenantDirectoryOperatorIdentityDocumentDto>>())
        { ControllerContext = new ControllerContext { HttpContext = context } };
    }

    private static async Task AssertNotFoundWithRetainedCacheAsync(IActionResult? result, IOutputCacheStore cache)
    {
        var problem = result as ObjectResult;
        await Assert.That(problem).IsNotNull();
        await Assert.That(problem!.StatusCode).IsEqualTo(StatusCodes.Status404NotFound);
        await Assert.That(problem.Value).IsTypeOf<ProblemDetails>();
        await Assert.That(await cache.GetAsync(CacheKey, default)).IsEquivalentTo(Shell);
    }
}
