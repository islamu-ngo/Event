using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Constants;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Security;
using Explore.Persistence.Services;
using ISLAMU.Wire.Contracts.Learning;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using TUnit.Core.Interfaces;

namespace Event.API.IntegrationTests.Learning;

[ClassDataSource<LearningHttpFixture>(Shared = SharedType.PerClass)]
[NotInParallel]
public sealed class LearningContractTests(LearningHttpFixture fixture)
{
    [Test]
    public async Task OrdinaryLearnerCannotManageTheTenantLearningSource()
    {
        using var member = await fixture.CreateMemberAsync();
        using var contextResponse = await member.Client.GetAsync("/api/learning/context");
        await Assert.That(contextResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var context = (await contextResponse.Content.ReadFromJsonAsync<LearningContextDto>())!;
        using var response = await member.Client.PostAsJsonAsync("/api/learning/space/actions",
            new LearningManageSpaceRequest
            {
                ExpectedScopeStamp = context.ScopeStamp, ExpectedRevision = 0,
                Action = LearningSpaceAction.Remove
            });
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
        await Assert.That(context.Capabilities.CanManageSpace).IsFalse();
    }

    [Test]
    public async Task AnonymousPrivateContextRequiresHumanAuthentication()
    {
        using HttpClient client = fixture.CreateClient();
        using HttpResponseMessage response = await client.GetAsync("/api/learning/context");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
        await Assert.That(response.Headers.GetValues("Referrer-Policy")).Contains("no-referrer");
    }

    [Test]
    public async Task OrdinaryMemberContextIsPrivateNoStoreAndDoesNotCreateLearningState()
    {
        using LearningHttpMember member = await fixture.CreateMemberAsync();
        await using var before = fixture.OpenInspection();
        int spacesBefore = await before.TenantLearningSpaces.CountAsync(row => row.TenantId == fixture.TenantId);

        using HttpResponseMessage response = await member.Client.GetAsync("/api/learning/context");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
        LearningContextDto context = (await response.Content.ReadFromJsonAsync<LearningContextDto>())!;
        await Assert.That(context.UserId).IsEqualTo(member.UserId);
        await Assert.That(context.TenantId).IsEqualTo(fixture.TenantId);
        await Assert.That(context.ScopeStamp).IsNotEmpty();
        await Assert.That(context.StateId).IsNull();
        await Assert.That(context.AdditionalProperties.ContainsKey("_links")).IsTrue();
        await using var after = fixture.OpenInspection();
        await Assert.That(await after.UserLearningStates.AnyAsync(row =>
            row.TenantId == fixture.TenantId && row.UserId == member.UserId)).IsFalse();
        await Assert.That(await after.TenantLearningSpaces.CountAsync(row => row.TenantId == fixture.TenantId))
            .IsEqualTo(spacesBefore);
    }

    [Test]
    public async Task AnotherAccountsScopeStampCannotMaterializePrivatePreferences()
    {
        using LearningHttpMember first = await fixture.CreateMemberAsync();
        using LearningHttpMember second = await fixture.CreateMemberAsync();
        using HttpResponseMessage contextResponse = await second.Client.GetAsync("/api/learning/context");
        await Assert.That(contextResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        LearningContextDto foreign = (await contextResponse.Content.ReadFromJsonAsync<LearningContextDto>())!;

        using HttpResponseMessage response = await first.Client.PatchAsJsonAsync("/api/learning/preferences",
            new LearningUpdatePreferencesRequest
            {
                ExpectedScopeStamp = foreign.ScopeStamp,
                ExpectedRevision = 0,
                SuppressAutomaticTours = true,
                HideLearning = false,
                PinLearning = false
            });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        await using var stored = fixture.OpenInspection();
        await Assert.That(await stored.UserLearningStates.AnyAsync(row =>
            row.TenantId == fixture.TenantId && row.UserId == first.UserId)).IsFalse();
    }

    [Test]
    [Arguments("foreign-owner")]
    [Arguments("numeric-family")]
    public async Task ClosedWritesRejectOwnershipFieldsAndNumericEnums(string invalidInput)
    {
        using LearningHttpMember member = await fixture.CreateMemberAsync();
        using HttpResponseMessage response = invalidInput == "foreign-owner"
            ? await member.Client.PatchAsJsonAsync("/api/learning/preferences", new
            {
                expectedScopeStamp = new string('0', 64), expectedRevision = 0,
                suppressAutomaticTours = true, hideLearning = false, pinLearning = false,
                userId = Guid.CreateVersion7()
            })
            : await member.Client.PostAsJsonAsync("/api/learning/tours/claim", new
            {
                expectedScopeStamp = new string('0', 64), expectedRevision = 0,
                intentId = Guid.CreateVersion7(),
                family = 1, materialVersion = 1, contextKey = "self"
            });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
        await using var stored = fixture.OpenInspection();
        await Assert.That(await stored.UserLearningStates.AnyAsync(row =>
            row.TenantId == fixture.TenantId && row.UserId == member.UserId)).IsFalse();
    }
}

public sealed class LearningHttpFixture : IAsyncInitializer, IAsyncDisposable
{
    private LocalAdmissionWebApplicationFactory _factory = null!;
    internal Guid TenantId { get; private set; }
    private string _tenantSlug = string.Empty;

    public Task InitializeAsync() => InitializeAsync(null, null);

    internal async Task InitializeAsync(IInterceptor? interceptor, TimeProvider? clock, bool pooled = false)
    {
        _factory = await LocalAdmissionWebApplicationFactory.CreateAsync(
            persistenceInterceptor: interceptor, timeProvider: clock, poolContexts: pooled);
        await using var database = OpenInspection();
        Tenant tenant = await database.Tenants.SingleAsync(row => row.Id == PlatformDefaults.DefaultTenantId);
        TenantId = tenant.Id;
        _tenantSlug = tenant.Slug;
    }

    internal ExploreDbContext OpenInspection()
    {
        ExploreDbContext context = _factory.CreateDatabase();
        context.EnableTenantFilterBypass(TenantFilterBypassReasons.DatabaseSeeding);
        context.BindTenantLease();
        return context;
    }

    internal Microsoft.AspNetCore.Http.IHttpContextAccessor RequestAccessor =>
        _factory.Services.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>();

    internal async Task<ExploreDbContext> RentTenantAsync(Guid tenantId)
    {
        var context = await _factory.Services.GetRequiredService<IDbContextFactory<ExploreDbContext>>()
            .CreateDbContextAsync();
        context.TenantContext = new LearningTenantIdentity(tenantId);
        context.CurrentUserService = null;
        context.ClearTenantFilterBypass();
        context.BindTenantLease();
        return context;
    }

    private sealed record LearningTenantIdentity(Guid TenantId)
        : Explore.Application.Contracts.Infrastructure.ITenantContext;

    internal void ObserveContextReturn(Action<ExploreDbContext, DbContextId> returned) =>
        _factory.ContextReturned += returned;

    internal async Task<PrivacyErasureIntent> CommitRetainedErasureAsync(Guid userId)
    {
        var authority = new Explore.Persistence.Privacy.ErasureAuthority.Repositories.EmbeddedPrivacyErasureAuthorityRepository(
            _factory.Services.GetRequiredService<IDbContextFactory<
                Explore.Persistence.Privacy.ErasureAuthority.EmbeddedPrivacyErasureAuthorityDbContext>>(),
            _factory.Services.GetRequiredService<TimeProvider>(),
            _factory.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<
                Explore.Application.Configuration.PrivacyErasureOptions>>());
        return await authority.AppendAsync(new Explore.Application.Contracts.PrivacyErasure.PrivacyErasureRequest(
            Guid.CreateVersion7(), PrivacyErasureSubjectKind.User, userId,
            PrivacyErasureReasonCode.AccountDeletion, 1));
    }

    internal HttpClient CreateClient()
    {
        HttpClient client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost")
        });
        client.DefaultRequestHeaders.Add(TenantHeaderNames.TenantSlug, _tenantSlug);
        return client;
    }

    internal async Task<LearningHttpMember> CreateMemberAsync(bool administrator = false)
    {
        var credentials = await _factory.SeedLocalUserAsync(emailConfirmed: true);
        Guid userId;
        await using (var database = OpenInspection())
        {
            User user = await database.Users.Include(row => row.Pii)
                .SingleAsync(row => row.Pii.Email == credentials.Identifier);
            userId = user.Id;
            Tenant tenant = await database.Tenants.SingleAsync(row => row.Id == TenantId);
            var membership = new TenantUser
            {
                Id = Guid.CreateVersion7(), TenantId = tenant.Id, Tenant = tenant,
                UserId = user.Id, User = user, StatusId = (int)TenantUserStatusEnum.Active,
                JoinedAt = _factory.Services.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime
            };
            database.TenantUsers.Add(membership);
            if (administrator)
                database.TenantUserRoleGrants.Add(new TenantUserRoleGrant
                {
                    Id = Guid.CreateVersion7(), TenantId = tenant.Id, Tenant = tenant,
                    TenantUserId = membership.Id, TenantUser = membership,
                    RoleId = (int)RoleEnum.TenantAdmin, Role = null!,
                    RoleScopeId = (int)RoleScopeEnum.Tenant,
                    GrantedAt = _factory.Services.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime
                });
            await using var humanOwner = new HumanSecuritySourceGate(database);
            await using var human = await humanOwner.AcquireAsync();
            await using var discoveryOwner = new EventDiscoveryEnrollmentGate(database);
            await using var discovery = await discoveryOwner.AcquireAsync();
            await database.SaveChangesAsync();
        }
        HttpClient client = CreateClient();
        using HttpResponseMessage login = await client.PostAsJsonAsync("/api/auth/local/login", credentials);
        await Assert.That(login.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var body = System.Text.Json.JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        string token = body.RootElement.GetProperty("token").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return new LearningHttpMember(client, userId);
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();
}

internal sealed record LearningHttpMember(HttpClient Client, Guid UserId) : IDisposable
{
    public void Dispose() => Client.Dispose();
}
