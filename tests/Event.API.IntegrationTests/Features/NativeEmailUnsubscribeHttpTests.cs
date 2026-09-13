using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Event.Api.IntegrationTests.Builders;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Controllers;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.EmailUnsubscribe;
using Explore.Application.Features.EmailUnsubscribe.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Secrets.Database;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel("ApiTestFixture")]
public sealed class NativeEmailUnsubscribeHttpTests
{
    [Test]
    public async Task Controller_ConsumesOnlyTheClosedCommandAndRealTokenAuthority()
    {
        var parameters = typeof(EmailUnsubscribeController).GetConstructors().Single().GetParameters()
            .Select(parameter => parameter.ParameterType).ToArray();
        await Assert.That(parameters.Any(type => type.Namespace == "MediatR")).IsFalse();
        var port = parameters.Single(type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ICommandHandler<,>));
        await Assert.That(port.GetGenericArguments()).IsEquivalentTo(new[]
        {
            typeof(UnsubscribeFromEmailCategoryCommand), typeof(BaseCommandResponse<Guid>)
        });
        await using var factory = new UnsubscribeFactory();
        using var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService(port);
        await Assert.That(handler.GetType().GetGenericTypeDefinition().Name).IsEqualTo("AuthorizationCommandHandlerDecorator`2");
        await Assert.That(scope.ServiceProvider.GetRequiredService<IUserNotificationPreferenceRepository>())
            .IsTypeOf<UserNotificationPreferenceRepository>();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Http_OnlySignedTargetChangesAndReplayKeepsOneDurablePreference(bool authenticated)
    {
        await using var factory = new UnsubscribeFactory();
        using var client = factory.CreateClient();
        var (userId, otherUserId) = await SeedAsync(factory);
        var foreignTenantId = Guid.CreateVersion7();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            db.Tenants.Add(new TenantBuilder().WithId(foreignTenantId).Build());
            var foreign = Preference(userId, NotificationPreferenceCategories.EventReminders);
            foreign.TenantId = foreignTenantId;
            db.UserNotificationPreferences.Add(foreign);
            await db.SaveChangesAsync();
        }
        if (authenticated)
            client.DefaultRequestHeaders.Add(TestAuthHandler.AuthHeaderName, TestAuthHandler.CreateAuthHeaderValue(otherUserId));
        var token = Token(factory, userId, " EVENT-REMINDERS ");
        var path = PathFor(token) + $"&tenantId={foreignTenantId}&userId={otherUserId}&category=event-updates&channel=web-push";
        using (var get = await client.GetAsync(path))
        {
            var confirmation = await BodyAsync(get);
            await Assert.That(confirmation.Status).IsEqualTo("confirmation_required");
            await Assert.That(confirmation.Category).IsEqualTo(NotificationPreferenceCategories.EventReminders);
            await Assert.That(confirmation.RequiresConfirmation).IsTrue();
        }
        await Assert.That(await PreferencesAsync(factory, userId)).IsEmpty();
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var fields = new Dictionary<string, string>
            {
                ["List-Unsubscribe"] = "One-Click", ["userId"] = otherUserId.ToString(), ["category"] = "event-updates"
            };
            using var content = new FormUrlEncodedContent(fields);
            using var post = await client.PostAsync(path, content);
            var body = await BodyAsync(post);
            await Assert.That(body.Status).IsEqualTo("unsubscribed");
            await Assert.That(body.Category).IsNull();
            await Assert.That(body.RequiresConfirmation).IsFalse();
            var rows = await PreferencesAsync(factory, userId);
            await Assert.That(rows.Count).IsEqualTo(1);
            await Assert.That(rows[0].Category).IsEqualTo(NotificationPreferenceCategories.EventReminders);
            await Assert.That(rows[0].IsEnabled).IsFalse();
            await Assert.That(rows[0].TenantId).IsEqualTo(PlatformDefaults.DefaultTenantId);
            await Assert.That(rows[0].UserId).IsEqualTo(userId);
        }
        var other = await PreferencesAsync(factory, otherUserId);
        await Assert.That(other.Single().IsEnabled).IsTrue();
        await Assert.That(other.Single().Category).IsEqualTo(NotificationPreferenceCategories.EventReminders);
        await Assert.That((await PreferencesAsync(factory, userId, foreignTenantId)).Single().IsEnabled).IsTrue();
        using var isolated = factory.Services.CreateScope();
        isolated.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(PlatformDefaults.DefaultTenantId);
        await Assert.That(await isolated.ServiceProvider.GetRequiredService<IUserNotificationPreferenceRepository>()
            .GetByUserAndCategory(foreignTenantId, userId, NotificationPreferenceCategories.EventReminders)).IsNull();
    }

    [Test]
    public async Task InvalidCategoryAndInvalidToken_PreserveValidationBeforePersistenceEvenWhenCanceled()
    {
        await using var factory = new UnsubscribeFactory();
        using var client = factory.CreateClient();
        var (userId, _) = await SeedAsync(factory);
        using var scope = factory.Services.CreateScope();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var command = scope.ServiceProvider.GetRequiredService<ICommandHandler<UnsubscribeFromEmailCategoryCommand, BaseCommandResponse<Guid>>>();
        var result = await command.ExecuteAsync(new UnsubscribeFromEmailCategoryCommand
        {
            TenantId = PlatformDefaults.DefaultTenantId, UserId = userId, Category = "password-reset"
        }, cancellation.Token);
        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("unknown_notification_category");
        var controller = ActivatorUtilities.CreateInstance<EmailUnsubscribeController>(scope.ServiceProvider);
        var action = await controller.Post(null, cancellation.Token);
        await Assert.That(((EmailUnsubscribeResponseDto)((Microsoft.AspNetCore.Mvc.OkObjectResult)action.Result!).Value!).Status)
            .IsEqualTo("unsubscribed");
        await Assert.That(await PreferencesAsync(factory, userId)).IsEmpty();
    }

    [Test]
    public async Task Http_ExistingPreferenceIsDisabledWithoutChangingOtherCategories()
    {
        await using var factory = new UnsubscribeFactory();
        using var client = factory.CreateClient();
        var (_, userId) = await SeedAsync(factory);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            db.UserNotificationPreferences.Add(Preference(userId, NotificationPreferenceCategories.EventUpdates));
            await db.SaveChangesAsync();
        }
        var original = (await PreferencesAsync(factory, userId)).Single(row => row.Category == NotificationPreferenceCategories.EventReminders);
        using var post = await client.PostAsync(PathFor(Token(factory, userId)), null);
        await BodyAsync(post);
        var rows = await PreferencesAsync(factory, userId);
        await Assert.That(rows.Count).IsEqualTo(2);
        var changed = rows.Single(row => row.Category == NotificationPreferenceCategories.EventReminders);
        await Assert.That(changed.Id).IsEqualTo(original.Id);
        await Assert.That(changed.CreatedAt).IsEqualTo(original.CreatedAt);
        await Assert.That(changed.IsEnabled).IsFalse();
        await Assert.That(changed.UpdatedBy).IsEqualTo((Guid?)userId);
        await Assert.That(rows.Single(row => row.Category == NotificationPreferenceCategories.EventUpdates).IsEnabled).IsTrue();
    }

    [Test]
    [Arguments("missing")]
    [Arguments("malformed")]
    [Arguments("tampered")]
    [Arguments("expired")]
    [Arguments("foreign-authority")]
    [Arguments("unknown-category")]
    public async Task Http_InvalidProofDoesNotMutateAndDoesNotDiscloseTokenOrIdentity(string failure)
    {
        await using var factory = new UnsubscribeFactory();
        using var client = factory.CreateClient();
        var (_, userId) = await SeedAsync(factory);
        var token = failure switch
        {
            "missing" => string.Empty,
            "malformed" => "not-a-protected-token",
            "expired" => Token(factory, userId, lifetime: TimeSpan.FromDays(-1)),
            "foreign-authority" => new Explore.Infrastructure.Mail.Unsubscribe.EmailUnsubscribeTokenService(new EphemeralDataProtectionProvider())
                .GenerateToken(new(PlatformDefaults.DefaultTenantId, userId, NotificationPreferenceCategories.EventReminders, DateTime.UtcNow)),
            "unknown-category" => Token(factory, userId, "password-reset"),
            _ => Token(factory, userId)
        };
        if (failure == "tampered")
        {
            var index = token.Length / 2;
            token = token[..index] + (token[index] == 'A' ? 'B' : 'A') + token[(index + 1)..];
        }
        using var get = await client.GetAsync(PathFor(token));
        var invalid = await BodyAsync(get);
        await Assert.That(invalid.Status).IsEqualTo("invalid");
        await Assert.That(invalid.Category).IsNull();
        using var post = await client.PostAsync(PathFor(token), null);
        var accepted = await BodyAsync(post);
        await Assert.That(accepted.Status).IsEqualTo("unsubscribed");
        await Assert.That(accepted.Category).IsNull();
        await Assert.That((await PreferencesAsync(factory, userId)).Single().IsEnabled).IsTrue();
        var json = await post.Content.ReadAsStringAsync();
        await Assert.That(json).DoesNotContain(userId.ToString());
        await Assert.That(json).DoesNotContain(PlatformDefaults.DefaultTenantId.ToString());
        if (token.Length > 0) await Assert.That(json).DoesNotContain(token);
    }

    [Test]
    public async Task OpenApi_PreservesBothPublicOperationsAndResponseSchema()
    {
        await using var factory = new UnsubscribeFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/openapi/islamu-event.json");
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var path = json.RootElement.GetProperty("paths").GetProperty("/api/email/unsubscribe");
        foreach (var (verb, operationId) in new[] { ("get", "GetEmailUnsubscribe"), ("post", "OneClickEmailUnsubscribe") })
        {
            var operation = path.GetProperty(verb);
            await Assert.That(operation.GetProperty("operationId").GetString()).IsEqualTo(operationId);
            await Assert.That(operation.GetProperty("x-endpoint-class").GetString()).IsEqualTo("Public");
            await Assert.That(operation.GetProperty("tags")[0].GetString()).IsEqualTo("EmailUnsubscribe");
            await Assert.That(operation.GetProperty("responses").TryGetProperty("200", out _)).IsTrue();
        }
        var properties = json.RootElement.GetProperty("components").GetProperty("schemas")
            .GetProperty("EmailUnsubscribeResponseDto").GetProperty("properties");
        await Assert.That(properties.EnumerateObject().Select(property => property.Name).ToArray())
            .IsEquivalentTo(new[] { "status", "message", "category", "isSubscribed", "requiresConfirmation" });
    }

    private static async Task<(Guid UserId, Guid OtherUserId)> SeedAsync(UnsubscribeFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        db.Tenants.Add(new TenantBuilder().WithId(PlatformDefaults.DefaultTenantId).Build());
        var first = new UserBuilder().WithId(Guid.CreateVersion7()).Build();
        var second = new UserBuilder().WithId(Guid.CreateVersion7()).Build();
        db.Users.AddRange(first, second);
        db.UserNotificationPreferences.Add(Preference(second.Id, NotificationPreferenceCategories.EventReminders));
        await db.SaveChangesAsync();
        return (first.Id, second.Id);
    }

    private static UserNotificationPreference Preference(Guid userId, string category) => new()
    {
        TenantId = PlatformDefaults.DefaultTenantId, Tenant = null!, UserId = userId, Category = category, IsEnabled = true
    };

    private static async Task<List<UserNotificationPreference>> PreferencesAsync(UnsubscribeFactory factory, Guid userId, Guid? tenantId = null)
    {
        var tenant = tenantId ?? PlatformDefaults.DefaultTenantId;
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(tenant);
        return await scope.ServiceProvider.GetRequiredService<IUserNotificationPreferenceRepository>()
            .GetAllForUser(tenant, userId);
    }

    private static string Token(UnsubscribeFactory factory, Guid userId,
        string category = NotificationPreferenceCategories.EventReminders, TimeSpan? lifetime = null)
    {
        using var scope = factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IEmailUnsubscribeTokenService>()
            .GenerateToken(new(PlatformDefaults.DefaultTenantId, userId, category, DateTime.UtcNow), lifetime);
    }

    private static string PathFor(string token) => $"/api/email/unsubscribe?token={Uri.EscapeDataString(token)}";

    private static async Task<EmailUnsubscribeResponseDto> BodyAsync(HttpResponseMessage response)
    {
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<EmailUnsubscribeResponseDto>()
            ?? throw new InvalidOperationException("Expected unsubscribe response.");
    }

    private sealed class UnsubscribeFactory : AuthenticatedWebApplicationFactory
    {
        private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"native-unsubscribe-{Guid.CreateVersion7():N}.db");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IDataProtectionProvider>();
                services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
                services.RemoveExploreDbContextRegistrations();
                var options = new DbContextOptionsBuilder<ExploreDbContext>();
                ConfigureDatabase(options);
                using (var db = new ExploreDbContext(options.Options)) db.Database.EnsureCreated();
                services.AddDbContextFactory<ExploreDbContext>(ConfigureDatabase);
                services.AddScoped(provider =>
                {
                    var db = provider.GetRequiredService<IDbContextFactory<ExploreDbContext>>().CreateDbContext();
                    db.TenantContext = provider.GetRequiredService<ITenantContext>();
                    db.CurrentUserService = provider.GetRequiredService<ICurrentUserService>();
                    return db;
                });
            });
        }

        private void ConfigureDatabase(DbContextOptionsBuilder options)
        {
            PrimaryDatabaseProviderComposition.ConfigureApplication(options, new PrimaryDatabaseConnectionOptions
            {
                Role = PrimaryDatabaseRole.Runtime, Provider = PrimaryDatabaseProvider.Sqlite, Database = _databasePath
            });
            options.UseSnakeCaseNamingConvention();
        }

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            var options = new DbContextOptionsBuilder<ExploreDbContext>();
            ConfigureDatabase(options);
            await using var db = new ExploreDbContext(options.Options);
            SqliteConnection.ClearPool((SqliteConnection)db.Database.GetDbConnection());
            File.Delete(_databasePath);
            File.Delete(_databasePath + "-wal");
            File.Delete(_databasePath + "-shm");
        }
    }
}
