using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Event.Api.IntegrationTests.Seeds;
using Explore.API.Controllers;
using Explore.API.Hateoas;
using Explore.Application.Operations;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.TenantSettingsDocuments;
using Explore.Application.Exceptions;
using Explore.Application.Features.TenantSettingsDocuments.Requests.Commands;
using Explore.Application.Features.TenantSettingsDocuments.Requests.Queries;
using Explore.Application.Models.Common;
using Explore.Application.Responses;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Settings.Documents;
using Explore.Domain.Settings.Documents.Payloads;
using Explore.Infrastructure.Services;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Secrets.Database;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Caching.Memory;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel("ApiTestFixture")]
public sealed class TenantSettingsDocumentsControllerTests
{
    private const string Root = "/api/tenant/settings/documents/";
    private const string Branding = "branding";
    private const string Identity = "directory-operator-identity";
    private const string CacheKey = "tenant-document-shell-sentinel";
    private static readonly byte[] CachedShell = [11, 23, 37];

    [Test]
    public async Task Controller_ClosesAllFourDecoratedPortsAndHostGraph()
    {
        Type[] parameters = typeof(TenantSettingsDocumentsController).GetConstructors().Single().GetParameters()
            .Select(parameter => parameter.ParameterType).ToArray();
        Type[] ports =
        [
            typeof(ICommandHandler<EnsureTenantBrandingSettingsDocumentCommand, TenantBrandingSettingsDocumentDto?>),
            typeof(ICommandHandler<PatchTenantBrandingSettingsDocumentCommand, BaseCommandResponse<TenantBrandingSettingsDocumentDto>>),
            typeof(IQueryHandler<GetTenantDirectoryOperatorIdentityDocumentQuery, TenantDirectoryOperatorIdentityDocumentDto?>),
            typeof(ICommandHandler<PatchTenantDirectoryOperatorIdentityDocumentCommand, BaseCommandResponse<TenantDirectoryOperatorIdentityDocumentDto>>)
        ];
        await Assert.That(parameters).IsEquivalentTo(ports.Concat(new[]
        {
            typeof(ITenantContext), typeof(ITenantBrandingSettingsDocumentLockService),
            typeof(IResourceAssembler<TenantBrandingSettingsDocumentDto, TenantBrandingSettingsDocumentDto>),
            typeof(IResourceAssembler<TenantDirectoryOperatorIdentityDocumentDto, TenantDirectoryOperatorIdentityDocumentDto>)
        }));
        await using var factory = await DocumentFactory.CreateAsync();
        await factory.Services.ValidateNativeOperationsDeepAsync();
        using var scope = factory.Services.CreateScope();
        foreach (Type port in ports)
            await Assert.That(scope.ServiceProvider.GetRequiredService(port).GetType().Namespace)
                .IsEqualTo("Explore.Application.Operations.Decorators");
    }

    [Test]
    public async Task Authority_RequiresAuthenticationAndPersistedGrantsWithoutDisclosingIdentity()
    {
        await using var factory = await DocumentFactory.CreateAsync();
        var seed = await SeedAsync(factory);
        using var anonymous = factory.CreateClient();
        using var forged = Client(factory, seed.MemberId);
        using var admin = Client(factory, seed.AdminId);
        using var instance = Client(factory, seed.InstanceAdminId);
        foreach (string path in new[] { Branding, Identity })
        {
            using var read = await anonymous.GetAsync(Root + path);
            using var patch = await anonymous.PatchAsJsonAsync(Root + path, new { });
            await ProblemAsync(read, HttpStatusCode.Unauthorized);
            await ProblemAsync(patch, HttpStatusCode.Unauthorized);
            using var denied = path == Branding
                ? await forged.PatchAsJsonAsync(Root + path, BrandPatch(Guid.CreateVersion7()))
                : await forged.PatchAsJsonAsync(Root + path, IdentityPatch(Guid.CreateVersion7()));
            await ProblemAsync(denied, HttpStatusCode.Forbidden);
        }
        using (var denied = await forged.GetAsync(Root + Identity))
        {
            await ProblemAsync(denied, HttpStatusCode.Forbidden);
            await Assert.That(await denied.Content.ReadAsStringAsync()).DoesNotContain("Community Events ASBL");
            await Assert.That(factory.ResolutionObservation.IdentityReads).IsEqualTo(0);
        }
        // Branding retains its existing authenticated-member read/provision authority.
        var memberBranding = await ReadAsync<TenantBrandingSettingsDocumentDto>(forged, Branding);
        await Assert.That(memberBranding.SourceScopeId).IsEqualTo(PlatformDefaults.DefaultTenantId);
        foreach (var client in new[] { admin, instance })
        {
            using var allowed = await client.GetAsync(Root + Identity);
            await Assert.That(allowed.StatusCode).IsEqualTo(HttpStatusCode.OK);
            using var body = JsonDocument.Parse(await allowed.Content.ReadAsStringAsync());
            await Assert.That(body.RootElement.GetProperty("payload").GetProperty("legalName").GetString())
                .IsEqualTo("Community Events ASBL");
            await Assert.That(body.RootElement.GetProperty("_links").TryGetProperty("self", out _)).IsTrue();
            await Assert.That(body.RootElement.GetProperty("_links").TryGetProperty("edit", out _)).IsEqualTo(client == admin);
        }
        await Assert.That(factory.ResolutionObservation.IdentityReads).IsGreaterThan(0);
        using var put = await admin.PutAsJsonAsync(Root + Branding, new { });
        await Assert.That(put.StatusCode).IsEqualTo(HttpStatusCode.MethodNotAllowed);
    }

    [Test]
    public async Task Branding_ProvisionsOnceAndPreservesTenantDocumentOnRepeatedGets()
    {
        await using var factory = await DocumentFactory.CreateAsync();
        var seed = await SeedAsync(factory);
        using var client = Client(factory, seed.AdminId);
        using (var scope = factory.Services.CreateScope())
            await Assert.That(await scope.ServiceProvider.GetRequiredService<ITenantSettingsDocumentRepository>()
                .GetByTenantAndDocumentKey(PlatformDefaults.DefaultTenantId, SettingsDocumentKeys.Tenant.Branding)).IsNull();
        using var firstResponse = await client.GetAsync(Root + Branding);
        await Assert.That(firstResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await AssertHalLinksAsync(firstResponse, Branding);
        var first = (await firstResponse.Content.ReadFromJsonAsync<TenantBrandingSettingsDocumentDto>())!;
        var second = await ReadAsync<TenantBrandingSettingsDocumentDto>(client, Branding);
        await Assert.That(first.Payload.DisplayName).IsEqualTo(seed.TenantName);
        await Assert.That(first.SourceScopeId).IsEqualTo(PlatformDefaults.DefaultTenantId);
        await Assert.That(first.ConcurrencyStamp).IsNotEqualTo(Guid.Empty);
        await Assert.That(second).IsEqualTo(first);
        using var finalScope = factory.Services.CreateScope();
        var documents = await finalScope.ServiceProvider.GetRequiredService<ITenantSettingsDocumentRepository>()
            .GetManyForTenant(PlatformDefaults.DefaultTenantId, [SettingsDocumentKeys.Tenant.Branding]);
        await Assert.That(documents.Count).IsEqualTo(1);
        await Assert.That(documents[0].ConcurrencyStamp).IsEqualTo(first.ConcurrencyStamp);
    }

    [Test]
    public async Task ConcurrentProvisioning_BothMissingDocumentCallersReadTheUnchangedWinner()
    {
        await using var factory = await DocumentFactory.CreateAsync();
        var seed = await SeedAsync(factory);
        using var first = Client(factory, seed.AdminId);
        using var second = Client(factory, seed.MemberId);
        factory.SaveBoundary.RaceProvisioning = true;
        Task<HttpResponseMessage> firstRequest = first.GetAsync(Root + Branding);
        Task<HttpResponseMessage> secondRequest = second.GetAsync(Root + Branding);
        await factory.SaveBoundary.BothProvisionersEntered.Task.WaitAsync(TimeSpan.FromSeconds(15));
        factory.SaveBoundary.ReleaseProvisioners.TrySetResult();
        using var firstResponse = await firstRequest.WaitAsync(TimeSpan.FromSeconds(15));
        using var secondResponse = await secondRequest.WaitAsync(TimeSpan.FromSeconds(15));
        await Assert.That(firstResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(secondResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var created = (await firstResponse.Content.ReadFromJsonAsync<TenantBrandingSettingsDocumentDto>())!;
        var converged = (await secondResponse.Content.ReadFromJsonAsync<TenantBrandingSettingsDocumentDto>())!;
        await Assert.That(converged).IsEqualTo(created);
        factory.SaveBoundary.RaceProvisioning = false;
        var retried = await ReadAsync<TenantBrandingSettingsDocumentDto>(second, Branding);
        await Assert.That(retried).IsEqualTo(created);
        using var scope = factory.Services.CreateScope();
        await Assert.That((await scope.ServiceProvider.GetRequiredService<ITenantSettingsDocumentRepository>()
            .GetManyForTenant(PlatformDefaults.DefaultTenantId, [SettingsDocumentKeys.Tenant.Branding])).Count).IsEqualTo(1);
    }

    [Test]
    public async Task IdentityRead_IsNullableReadOnlyAndRejectsUnsupportedSchemaWithoutFallback()
    {
        await using var factory = await DocumentFactory.CreateAsync();
        var seed = await SeedAsync(factory, identity: false);
        using var admin = Client(factory, seed.AdminId);
        for (int attempt = 0; attempt < 2; attempt++)
        {
            using var missing = await admin.GetAsync(Root + Identity);
            await ProblemAsync(missing, HttpStatusCode.NotFound);
        }
        using (var scope = factory.Services.CreateScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<ITenantSettingsDocumentRepository>();
            await Assert.That(await repository.GetByTenantAndDocumentKey(PlatformDefaults.DefaultTenantId,
                SettingsDocumentKeys.Tenant.DirectoryOperatorIdentity)).IsNull();
            var document = ReadyIdentity(PlatformDefaults.DefaultTenantId);
            document.UpdatePayload(2, document.DefaultsVersion, document.PayloadJson);
            await repository.Create(document);
            scope.ServiceProvider.GetRequiredService<ITypedSettingsDocumentResolver>()
                .InvalidateTenantDocumentCache(PlatformDefaults.DefaultTenantId);
        }
        using (var unsupported = await admin.GetAsync(Root + Identity))
            await ProblemAsync(unsupported, HttpStatusCode.NotFound);
        using var nativeScope = factory.Services.CreateScope();
        SetPrincipal(nativeScope, seed.InstanceAdminId, Guid.Empty);
        var query = nativeScope.ServiceProvider.GetRequiredService<IQueryHandler<GetTenantDirectoryOperatorIdentityDocumentQuery, TenantDirectoryOperatorIdentityDocumentDto?>>();
        await Assert.That(await query.QueryAsync(new(Guid.Empty), default)).IsNull();
        Guid missingTenant = Guid.CreateVersion7();
        SetPrincipal(nativeScope, seed.InstanceAdminId, missingTenant);
        await Assert.That(await query.QueryAsync(new(missingTenant), default)).IsNull();
    }

    [Test]
    public async Task Patches_MergeNormalizeAndClearLeavesRotateRevisionAndEvictBothCaches()
    {
        await using var factory = await DocumentFactory.CreateAsync();
        var seed = await SeedAsync(factory);
        using var admin = Client(factory, seed.AdminId);
        var branding = await ReadAsync<TenantBrandingSettingsDocumentDto>(admin, Branding);
        await WarmShellAsync(factory);
        using (var patched = await admin.PatchAsJsonAsync(Root + Branding, new PatchTenantBrandingSettingsDocumentDto
        {
            ExpectedConcurrencyStamp = branding.ConcurrencyStamp,
            DisplayName = new() { Value = OptionalUpdate<string?>.Set("  Updated Tenant  ") },
            Assets = new() { CustomCssUrl = OptionalUpdate<string?>.Set("https://cdn.example.test/tenant.css") }
        }))
        {
            await Assert.That(patched.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await AssertHalLinksAsync(patched, Branding);
            var updated = (await patched.Content.ReadFromJsonAsync<TenantBrandingSettingsDocumentDto>())!;
            await Assert.That(updated.Payload.DisplayName).IsEqualTo("Updated Tenant");
            await Assert.That(updated.ConcurrencyStamp).IsNotEqualTo(branding.ConcurrencyStamp);
            await Assert.That(await ReadAsync<TenantBrandingSettingsDocumentDto>(admin, Branding)).IsEqualTo(updated);
            branding = updated;
        }
        await AssertShellAsync(factory, evicted: true);
        using (var cleared = await admin.PatchAsJsonAsync(Root + Branding, new PatchTenantBrandingSettingsDocumentDto
        {
            ExpectedConcurrencyStamp = branding.ConcurrencyStamp,
            DisplayName = new() { Value = OptionalUpdate<string?>.Set(null) }
        }))
        {
            await Assert.That(cleared.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await AssertHalLinksAsync(cleared, Branding);
            var updated = (await cleared.Content.ReadFromJsonAsync<TenantBrandingSettingsDocumentDto>())!;
            await Assert.That(updated.Payload.DisplayName).IsNull();
            await Assert.That(updated.Payload.CustomCssUrl).IsEqualTo("https://cdn.example.test/tenant.css");
        }
        var identity = await ReadAsync<TenantDirectoryOperatorIdentityDocumentDto>(admin, Identity);
        await Assert.That(identity.IsActivationReady).IsTrue();
        await Assert.That(identity.IsPublicDisclosureReady).IsTrue();
        await Assert.That(identity.IsPaidCommerceReady).IsFalse();
        await WarmShellAsync(factory);
        using var changed = await admin.PatchAsJsonAsync(Root + Identity, new PatchTenantDirectoryOperatorIdentityDocumentDto
        {
            ExpectedConcurrencyStamp = identity.ConcurrencyStamp,
            LegalEntity = new() { LegalName = OptionalUpdate<string?>.Set("  Updated Community Events ASBL  ") },
            LegalLinks = new() { TermsUrl = OptionalUpdate<string?>.Set("https://example.test/terms") }
        });
        await Assert.That(changed.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await AssertHalLinksAsync(changed, Identity);
        var changedIdentity = (await changed.Content.ReadFromJsonAsync<TenantDirectoryOperatorIdentityDocumentDto>())!;
        await Assert.That(changedIdentity.Payload.LegalName).IsEqualTo("Updated Community Events ASBL");
        await Assert.That(changedIdentity.Payload.PublicName).IsEqualTo(identity.Payload.PublicName);
        await Assert.That(changedIdentity.Payload.PublicContactEmail).IsEqualTo(identity.Payload.PublicContactEmail);
        await Assert.That(changedIdentity.Payload.PrivacyUrl).IsEqualTo(identity.Payload.PrivacyUrl);
        await Assert.That(changedIdentity.ConcurrencyStamp).IsNotEqualTo(identity.ConcurrencyStamp);
        await Assert.That(changedIdentity.IsPaidCommerceReady).IsTrue();
        var reloaded = await ReadAsync<TenantDirectoryOperatorIdentityDocumentDto>(admin, Identity);
        await Assert.That(reloaded.ConcurrencyStamp).IsEqualTo(changedIdentity.ConcurrencyStamp);
        await Assert.That(reloaded.Payload).IsEqualTo(changedIdentity.Payload);
        await AssertShellAsync(factory, evicted: true);
    }

    [Test]
    public async Task InvalidStaleAndActiveIdentityDowngradePatches_DoNotMutateOrEvict()
    {
        await using var factory = await DocumentFactory.CreateAsync();
        var seed = await SeedAsync(factory);
        using var admin = Client(factory, seed.AdminId);
        var branding = await ReadAsync<TenantBrandingSettingsDocumentDto>(admin, Branding);
        var identity = await ReadAsync<TenantDirectoryOperatorIdentityDocumentDto>(admin, Identity);
        await WarmShellAsync(factory);
        using (var invalid = await admin.PatchAsJsonAsync(Root + Branding, new { expectedConcurrencyStamp = branding.ConcurrencyStamp }))
            await ProblemAsync(invalid, HttpStatusCode.BadRequest);
        using (var invalid = await admin.PatchAsJsonAsync(Root + Identity, new PatchTenantDirectoryOperatorIdentityDocumentDto
        {
            ExpectedConcurrencyStamp = identity.ConcurrencyStamp,
            Contacts = new() { PublicContactEmail = OptionalUpdate<string?>.Set("not-an-email") }
        }))
            await ProblemAsync(invalid, HttpStatusCode.BadRequest);
        using (var stale = await admin.PatchAsJsonAsync(Root + Branding, BrandPatch(Guid.CreateVersion7())))
            await ProblemAsync(stale, HttpStatusCode.Conflict);
        using (var stale = await admin.PatchAsJsonAsync(Root + Identity, IdentityPatch(Guid.CreateVersion7())))
            await ProblemAsync(stale, HttpStatusCode.Conflict);
        using (var downgrade = await admin.PatchAsJsonAsync(Root + Identity, new PatchTenantDirectoryOperatorIdentityDocumentDto
        {
            ExpectedConcurrencyStamp = identity.ConcurrencyStamp,
            LegalEntity = new() { LegalName = OptionalUpdate<string?>.Set(null) }
        }))
        {
            await ProblemAsync(downgrade, HttpStatusCode.Conflict);
            await Assert.That(await downgrade.Content.ReadAsStringAsync()).DoesNotContain(identity.Payload.LegalName!);
        }
        await Assert.That((await ReadAsync<TenantBrandingSettingsDocumentDto>(admin, Branding)).ConcurrencyStamp).IsEqualTo(branding.ConcurrencyStamp);
        await Assert.That((await ReadAsync<TenantDirectoryOperatorIdentityDocumentDto>(admin, Identity)).Payload).IsEqualTo(identity.Payload);
        await AssertShellAsync(factory, evicted: false);
    }

    [Test]
    public async Task BrandingLocks_AreEvaluatedByHandlerAndCannotBeOverriddenByRequestFlag()
    {
        await using var factory = await DocumentFactory.CreateAsync();
        var seed = await SeedAsync(factory);
        using var admin = Client(factory, seed.AdminId);
        var branding = await ReadAsync<TenantBrandingSettingsDocumentDto>(admin, Branding);
        using var scope = factory.Services.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<IHierarchicalSettingsResolver>();
        await settings.SetValueAsync(GovernanceSettingKeys.Deployment.Mode, "\"MultiTenant\"", Explore.Domain.Settings.SettingScope.Instance, Guid.Empty, seed.AdminId);
        await settings.LockAsync(GovernanceSettingKeys.Branding.DisplayName, Explore.Domain.Settings.SettingScope.Instance, Guid.Empty, seed.AdminId);
        SetPrincipal(scope, seed.AdminId, PlatformDefaults.DefaultTenantId);
        var command = scope.ServiceProvider.GetRequiredService<ICommandHandler<PatchTenantBrandingSettingsDocumentCommand, BaseCommandResponse<TenantBrandingSettingsDocumentDto>>>();
        var response = await command.ExecuteAsync(new()
        {
            TenantId = PlatformDefaults.DefaultTenantId,
            Patch = BrandPatch(branding.ConcurrencyStamp),
            IsLockedByInstance = false
        }, default);
        await Assert.That(response.IsSuccess).IsFalse();
        await Assert.That(response.Errors).IsNotNull();
        await Assert.That(response.Errors!.Count).IsGreaterThan(0);
        var current = await scope.ServiceProvider.GetRequiredService<ICommandHandler<EnsureTenantBrandingSettingsDocumentCommand, TenantBrandingSettingsDocumentDto?>>()
            .ExecuteAsync(new(), default);
        await Assert.That(current!.Payload).IsEqualTo(branding.Payload);
        await Assert.That(current.CanChangeDisplayName).IsFalse();
    }

    [Test]
    [Arguments(Branding)]
    [Arguments(Identity)]
    public async Task FailedCommit_RollsBackDocumentAndDoesNotEvictShell(string path)
    {
        await using var factory = await DocumentFactory.CreateAsync();
        var seed = await SeedAsync(factory);
        using var admin = Client(factory, seed.AdminId);
        Guid revision = path == Branding
            ? (await ReadAsync<TenantBrandingSettingsDocumentDto>(admin, path)).ConcurrencyStamp
            : (await ReadAsync<TenantDirectoryOperatorIdentityDocumentDto>(admin, path)).ConcurrencyStamp;
        await WarmShellAsync(factory);
        factory.CommitBoundary.FailCommit = true;
        using (var failed = path == Branding
            ? await admin.PatchAsJsonAsync(Root + path, BrandPatch(revision))
            : await admin.PatchAsJsonAsync(Root + path, IdentityPatch(revision)))
            await ProblemAsync(failed, HttpStatusCode.InternalServerError);
        factory.CommitBoundary.FailCommit = false;
        using (var scope = factory.Services.CreateScope())
            scope.ServiceProvider.GetRequiredService<ITypedSettingsDocumentResolver>().InvalidateTenantDocumentCache(PlatformDefaults.DefaultTenantId);
        Guid after = path == Branding
            ? (await ReadAsync<TenantBrandingSettingsDocumentDto>(admin, path)).ConcurrencyStamp
            : (await ReadAsync<TenantDirectoryOperatorIdentityDocumentDto>(admin, path)).ConcurrencyStamp;
        await Assert.That(after).IsEqualTo(revision);
        await AssertShellAsync(factory, evicted: false);
    }

    [Test]
    public async Task PersistenceConcurrencyAndIncompatiblePayload_ReturnSafeProblemsWithoutEviction()
    {
        await using var factory = await DocumentFactory.CreateAsync();
        var seed = await SeedAsync(factory);
        using var admin = Client(factory, seed.AdminId);
        var branding = await ReadAsync<TenantBrandingSettingsDocumentDto>(admin, Branding);
        await WarmShellAsync(factory);
        factory.SaveBoundary.FailConcurrentUpdate = true;
        using (var conflict = await admin.PatchAsJsonAsync(Root + Branding, BrandPatch(branding.ConcurrencyStamp)))
        {
            await ProblemAsync(conflict, HttpStatusCode.Conflict);
            using var body = JsonDocument.Parse(await conflict.Content.ReadAsStringAsync());
            await Assert.That(body.RootElement.GetProperty("code").GetString()).IsEqualTo(ConcurrencyConflictException.ConcurrentUpdate);
        }
        factory.SaveBoundary.FailConcurrentUpdate = false;
        using (var scope = factory.Services.CreateScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<ITenantSettingsDocumentRepository>();
            var document = (await repository.GetTrackedByTenantAndDocumentKey(PlatformDefaults.DefaultTenantId, SettingsDocumentKeys.Tenant.Branding))!;
            document.UpdatePayload(document.SchemaVersion, document.DefaultsVersion, "{\"displayName\":{\"privateCanary\":\"undisclosed-document-payload\"}}");
            await repository.Update(document);
            branding = branding with { ConcurrencyStamp = document.ConcurrencyStamp };
        }
        using (var failed = await admin.PatchAsJsonAsync(Root + Branding, BrandPatch(branding.ConcurrencyStamp)))
        {
            await ProblemAsync(failed, HttpStatusCode.InternalServerError);
            string body = await failed.Content.ReadAsStringAsync();
            await Assert.That(body).DoesNotContain("undisclosed-document-payload");
            await Assert.That(body).DoesNotContain("payload could not be deserialized");
        }
        await AssertShellAsync(factory, evicted: false);
    }

    [Test]
    public async Task NativeIdentityPorts_RejectForeignTenantGrantAndInstanceReadDoesNotGrantWrite()
    {
        await using var factory = await DocumentFactory.CreateAsync();
        var seed = await SeedAsync(factory);
        using var scope = factory.Services.CreateScope();
        SetPrincipal(scope, seed.AdminId, seed.OtherTenantId);
        var query = scope.ServiceProvider.GetRequiredService<IQueryHandler<GetTenantDirectoryOperatorIdentityDocumentQuery, TenantDirectoryOperatorIdentityDocumentDto?>>();
        await Assert.That(async () => await query.QueryAsync(new(seed.OtherTenantId), default)).Throws<AuthorizationException>();
        SetPrincipal(scope, seed.InstanceAdminId, seed.OtherTenantId);
        var foreign = await query.QueryAsync(new(seed.OtherTenantId), default);
        await Assert.That(foreign!.SourceScopeId).IsEqualTo(seed.OtherTenantId);
        await Assert.That(foreign.Payload.LegalName).IsEqualTo("Foreign Operator ASBL");
        var patch = scope.ServiceProvider.GetRequiredService<ICommandHandler<PatchTenantDirectoryOperatorIdentityDocumentCommand, BaseCommandResponse<TenantDirectoryOperatorIdentityDocumentDto>>>();
        await Assert.That(async () => await patch.ExecuteAsync(new()
        {
            TenantId = seed.OtherTenantId,
            Patch = IdentityPatch(foreign.ConcurrencyStamp)
        }, default)).Throws<AuthorizationException>();
        await Assert.That((await query.QueryAsync(new(seed.OtherTenantId), default))!.ConcurrencyStamp).IsEqualTo(foreign.ConcurrencyStamp);
    }

    [Test]
    public async Task IdentityPatch_ContextGuardRejectsForeignTargetEvenWhenProviderAllows()
    {
        await using var factory = await DocumentFactory.CreateAsync();
        factory.AuthorizationProviderOverride = new StubAuthorizationProvider { AllowAll = true };
        var seed = await SeedAsync(factory);
        using var scope = factory.Services.CreateScope();
        SetPrincipal(scope, seed.AdminId, seed.OtherTenantId);
        var patch = scope.ServiceProvider.GetRequiredService<ICommandHandler<PatchTenantDirectoryOperatorIdentityDocumentCommand, BaseCommandResponse<TenantDirectoryOperatorIdentityDocumentDto>>>();
        var mismatch = await patch.ExecuteAsync(new()
        {
            TenantId = PlatformDefaults.DefaultTenantId,
            Patch = IdentityPatch(Guid.CreateVersion7())
        }, default);
        await Assert.That(mismatch.FailureCode).IsEqualTo(FailureCodes.AdminRequired);
        var query = scope.ServiceProvider.GetRequiredService<IQueryHandler<GetTenantDirectoryOperatorIdentityDocumentQuery, TenantDirectoryOperatorIdentityDocumentDto?>>();
        await Assert.That((await query.QueryAsync(new(seed.OtherTenantId), default))!.Payload.LegalName).IsEqualTo("Foreign Operator ASBL");
    }

    private static PatchTenantBrandingSettingsDocumentDto BrandPatch(Guid revision) => new()
    {
        ExpectedConcurrencyStamp = revision,
        DisplayName = new() { Value = OptionalUpdate<string?>.Set("Changed brand") }
    };

    private static PatchTenantDirectoryOperatorIdentityDocumentDto IdentityPatch(Guid revision) => new()
    {
        ExpectedConcurrencyStamp = revision,
        LegalEntity = new() { LegalName = OptionalUpdate<string?>.Set("Changed operator ASBL") }
    };

    private static async Task<T> ReadAsync<T>(HttpClient client, string path)
    {
        using var response = await client.GetAsync(Root + path);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK).Because(await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private static async Task AssertHalLinksAsync(HttpResponseMessage response, string path)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var links = body.RootElement.GetProperty("_links");
        foreach (var (relation, method) in new[] { ("self", "GET"), ("edit", "PATCH") })
        {
            var link = links.GetProperty(relation);
            await Assert.That(new Uri(new Uri("http://localhost"), link.GetProperty("href").GetString()!).AbsolutePath)
                .IsEqualTo(Root + path);
            await Assert.That(link.GetProperty("method").GetString()).IsEqualTo(method);
        }
    }

    private static async Task ProblemAsync(HttpResponseMessage response, HttpStatusCode status)
    {
        await Assert.That(response.StatusCode).IsEqualTo(status).Because(await response.Content.ReadAsStringAsync());
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        await Assert.That(body.RootElement.GetProperty("status").GetInt32()).IsEqualTo((int)status);
    }

    private static async Task WarmShellAsync(DocumentFactory factory) =>
        await factory.Services.GetRequiredService<IOutputCacheStore>().SetAsync(CacheKey, CachedShell,
            ["public-experience-shell"], TimeSpan.FromMinutes(5), default);

    private static async Task AssertShellAsync(DocumentFactory factory, bool evicted)
    {
        var cached = await factory.Services.GetRequiredService<IOutputCacheStore>().GetAsync(CacheKey, default);
        if (evicted) await Assert.That(cached).IsNull();
        else await Assert.That(cached).IsEquivalentTo(CachedShell);
    }

    private static HttpClient Client(DocumentFactory factory, Guid userId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.AuthHeaderName,
            TestAuthHandler.CreateTenantAdminHeaderValue(userId, PlatformDefaults.DefaultTenantId));
        return client;
    }

    private static void SetPrincipal(IServiceScope scope, Guid userId, Guid tenantId)
    {
        scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(tenantId);
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("internal_user_id", userId.ToString())], "Test"))
        };
    }

    private static TenantSettingsDocument ReadyIdentity(Guid tenantId, string legalName = "Community Events ASBL") =>
        TenantDirectoryOperatorIdentityDocumentDefaults.Create(tenantId, new TenantDirectoryOperatorIdentitySettings
        {
            PublicName = "Community Events",
            LegalName = legalName,
            OperatorKindCode = "registered_organization",
            JurisdictionCountryCode = "BE",
            RegistrationIdentifier = "BE 0123.456.789",
            PublicContactEmail = "contact@example.test",
            LegalNoticeUrl = "https://example.test/legal",
            PrivacyUrl = "https://example.test/privacy"
        });

    private static async Task<SeedData> SeedAsync(DocumentFactory factory, bool identity = true)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var admin = await TenantScenarioSeed.SeedActiveTenantWithUserAsync(db);
        var member = await TenantScenarioSeed.SeedActiveTenantWithUserAsync(db);
        var instance = await TenantScenarioSeed.SeedActiveTenantWithUserAsync(db);
        var other = await TenantScenarioSeed.SeedSecondaryTenantWithUserAsync(db);
        var membership = await db.TenantUsers.Include(item => item.Tenant).SingleAsync(item => item.UserId == admin.UserId);
        membership.Tenant.TenantStatusId = (int)TenantStatusEnum.Active;
        db.TenantUserRoleGrants.Add(new TenantUserRoleGrant
        {
            Id = Guid.CreateVersion7(),
            TenantId = admin.TenantId,
            Tenant = membership.Tenant,
            TenantUserId = membership.Id,
            TenantUser = membership,
            RoleId = (int)RoleEnum.TenantAdmin,
            Role = null!,
            RoleScopeId = (int)RoleScopeEnum.Tenant
        });
        var role = await db.Roles.SingleAsync(item => item.MasterCode == "platform.admin");
        db.PlatformUserRoles.Add(new PlatformUserRole
        {
            Id = Guid.CreateVersion7(),
            UserId = instance.UserId,
            User = null!,
            RoleId = role.Id,
            Role = role
        });
        if (identity) db.TenantSettingsDocuments.Add(ReadyIdentity(admin.TenantId));
        db.TenantSettingsDocuments.Add(ReadyIdentity(other.TenantId, "Foreign Operator ASBL"));
        await db.SaveChangesAsync();
        return new(admin.UserId, member.UserId, instance.UserId, other.TenantId, membership.Tenant.FullName);
    }

    private sealed record SeedData(Guid AdminId, Guid MemberId, Guid InstanceAdminId, Guid OtherTenantId, string TenantName);

    private sealed class DocumentFactory : AuthenticatedWebApplicationFactory
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), $"native-tenant-documents-{Guid.CreateVersion7():N}.db");
        public CommitFailure CommitBoundary { get; } = new();
        public DocumentSaveBoundary SaveBoundary { get; } = new();
        public DocumentResolutionObservation ResolutionObservation { get; } = new();

        public static async Task<DocumentFactory> CreateAsync()
        {
            var factory = new DocumentFactory();
            try
            {
                var options = new DbContextOptionsBuilder<ExploreDbContext>();
                factory.ConfigureDatabase(options);
                await using var db = new ExploreDbContext(options.Options);
                await db.Database.EnsureCreatedAsync();
                await SqliteDatabaseInitializer.InitializeAsync(db, CancellationToken.None);
                return factory;
            }
            catch
            {
                await factory.DisposeAsync();
                throw;
            }
        }

        private void ConfigureDatabase(DbContextOptionsBuilder options)
        {
            PrimaryDatabaseProviderComposition.ConfigureApplication(options, new PrimaryDatabaseConnectionOptions
            {
                Role = PrimaryDatabaseRole.Runtime,
                Provider = PrimaryDatabaseProvider.Sqlite,
                Database = _path
            });
            options.UseSnakeCaseNamingConvention().AddInterceptors(CommitBoundary, SaveBoundary);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveExploreDbContextRegistrations();
                services.AddDbContextFactory<ExploreDbContext>(ConfigureDatabase);
                services.RemoveAll<ITypedSettingsDocumentResolver>();
                services.AddScoped<ITypedSettingsDocumentResolver>(provider => new ObservedDocumentResolver(
                    new TypedSettingsDocumentResolver(provider.GetRequiredService<ITenantSettingsDocumentRepository>(),
                        provider.GetRequiredService<IMemoryCache>()), ResolutionObservation));
                services.AddScoped(provider =>
                {
                    var db = provider.GetRequiredService<IDbContextFactory<ExploreDbContext>>().CreateDbContext();
                    db.TenantContext = provider.GetRequiredService<ITenantContext>();
                    db.CurrentUserService = provider.GetRequiredService<ICurrentUserService>();
                    return db;
                });
            });
        }

        public override async ValueTask DisposeAsync()
        {
            SaveBoundary.ReleaseProvisioners.TrySetResult();
            await base.DisposeAsync();
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _path }.ConnectionString);
            SqliteConnection.ClearPool(connection);
            File.Delete(_path);
            File.Delete(_path + "-wal");
            File.Delete(_path + "-shm");
        }
    }

    private sealed class DocumentResolutionObservation
    {
        private int _identityReads;
        public int IdentityReads => Volatile.Read(ref _identityReads);
        public void Observe(string documentKey)
        {
            if (documentKey == SettingsDocumentKeys.Tenant.DirectoryOperatorIdentity)
                Interlocked.Increment(ref _identityReads);
        }
    }

    // Observation only: every read and invalidation still reaches the real resolver and repository.
    private sealed class ObservedDocumentResolver(ITypedSettingsDocumentResolver inner,
        DocumentResolutionObservation observation) : ITypedSettingsDocumentResolver
    {
        public Task<ResolvedSettingsDocument<TPayload>?> ResolveTenantDocumentAsync<TPayload>(
            SettingsResolutionContext context, string documentKey, CancellationToken cancellationToken = default)
            where TPayload : notnull
        {
            observation.Observe(documentKey);
            return inner.ResolveTenantDocumentAsync<TPayload>(context, documentKey, cancellationToken);
        }

        public Task<IReadOnlyList<ResolvedSettingsDocument<TPayload>>> ResolveTenantDocumentsAsync<TPayload>(
            SettingsResolutionContext context, IEnumerable<string> documentKeys, CancellationToken cancellationToken = default)
            where TPayload : notnull
        {
            string[] keys = documentKeys.ToArray();
            foreach (string key in keys) observation.Observe(key);
            return inner.ResolveTenantDocumentsAsync<TPayload>(context, keys, cancellationToken);
        }

        public void InvalidateTenantDocumentCache(Guid tenantId, string? documentKey = null) =>
            inner.InvalidateTenantDocumentCache(tenantId, documentKey);
    }

    private sealed class CommitFailure : DbTransactionInterceptor
    {
        public bool FailCommit { get; set; }
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (FailCommit) throw new InvalidOperationException("Injected document commit failure.");
            return ValueTask.FromResult(result);
        }
    }

    private sealed class DocumentSaveBoundary : SaveChangesInterceptor
    {
        public bool FailConcurrentUpdate { get; set; }
        public bool RaceProvisioning { get; set; }
        private int _provisioners;
        public TaskCompletionSource BothProvisionersEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseProvisioners { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var entries = eventData.Context!.ChangeTracker.Entries<TenantSettingsDocument>().ToArray();
            if (FailConcurrentUpdate && entries.Any(entry => entry.State == EntityState.Modified))
                throw new DbUpdateConcurrencyException("Injected concurrent document write.");
            if (RaceProvisioning && entries.Any(entry => entry.State == EntityState.Added && entry.Entity.DocumentKey == SettingsDocumentKeys.Tenant.Branding))
            {
                if (Interlocked.Increment(ref _provisioners) == 2) BothProvisionersEntered.TrySetResult();
                await ReleaseProvisioners.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
            }
            return result;
        }
    }
}
