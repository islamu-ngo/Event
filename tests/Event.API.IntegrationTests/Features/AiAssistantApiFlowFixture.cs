using System.Collections.Concurrent;
using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Event.Api.IntegrationTests.Authentication;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Infrastructure.Ai;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.Authentication.Local.Models;
using Explore.Domain;
using Explore.Domain.Ai;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Infrastructure.Ai;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Identity;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Event.Api.IntegrationTests.Features;

/// <summary>
/// Setup authority provisions data; HTTP and worker commands retain enrolled Runtime
/// owners, production Local credentials, source fences, graph repositories and receipts.
/// </summary>
internal sealed class AiAssistantApiFlowFixture : IAsyncDisposable
{
    private readonly PostgreSqlContainerFixture postgres;
    private readonly TenantBrandingNativeAuthenticationHost host;
    private readonly Dictionary<Guid, string> tokens = [];
    private readonly AiRunCompletion completion = new();
    internal Guid TenantId { get; } = Guid.CreateVersion7();
    internal Guid OtherTenantId { get; } = Guid.CreateVersion7();
    internal Guid UserId { get; private set; }
    internal Guid OtherUserId { get; private set; }
    internal HttpClient Client { get; }
    internal Exception? RequestFailure => host.RequestFailure;
    private string slug = null!;
    private string otherSlug = null!;

    private AiAssistantApiFlowFixture(PostgreSqlContainerFixture postgres, bool providerFails)
    {
        this.postgres = postgres;
        host = new TenantBrandingNativeAuthenticationHost(postgres, TenantId,
            [new ObserveRunSave(completion), new ObserveRunCommit(completion)], configureServices: services =>
        {
            // This is the only behavioral substitute: the external model provider.
            services.RemoveAll<IAiChatProvider>();
            services.RemoveAll<IAiModelCatalog>();
            if (providerFails)
            {
                services.AddSingleton<FailingProvider>();
                services.AddSingleton<IAiChatProvider>(sp => sp.GetRequiredService<FailingProvider>());
                services.AddSingleton<IAiModelCatalog>(sp => sp.GetRequiredService<FailingProvider>());
            }
            else
            {
                services.AddSingleton<FakeAiChatProvider>();
                services.AddSingleton<IAiChatProvider>(sp => sp.GetRequiredService<FakeAiChatProvider>());
                services.AddSingleton<IAiModelCatalog>(sp => sp.GetRequiredService<FakeAiChatProvider>());
            }
        });
        Client = host.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });
    }

    internal static async Task<AiAssistantApiFlowFixture> CreateAsync(
        PostgreSqlContainerFixture postgres, bool enabled = true, bool providerFails = false)
    {
        var fixture = new AiAssistantApiFlowFixture(postgres, providerFails);
        try
        {
            await fixture.InitializeAsync(enabled);
            return fixture;
        }
        catch
        {
            await fixture.DisposeAsync();
            throw;
        }
    }

    private async Task InitializeAsync(bool enabled)
    {
        await host.EnsurePrivacyAuthorityReadyAsync();
        await using (var setup = postgres.CreateDbContext())
        {
            slug = $"ai-{TenantId:N}";
            otherSlug = $"ai-{OtherTenantId:N}";
            setup.Tenants.AddRange(NewTenant(TenantId, slug), NewTenant(OtherTenantId, otherSlug));
            foreach (var (key, value, type) in new[]
            {
                (GovernanceSettingKeys.Security.AuthorizationProvider, "\"local\"", SettingValueType.String),
                (GovernanceSettingKeys.Deployment.Mode, "\"MultiTenant\"", SettingValueType.String),
                (GovernanceSettingKeys.AiAssistant.Enabled, enabled ? "true" : "false", SettingValueType.Boolean),
                (GovernanceSettingKeys.AiAssistant.Provider, JsonSerializer.Serialize(AiProviderDefaults.ProviderFake), SettingValueType.String),
                (GovernanceSettingKeys.AiAssistant.ModelId, JsonSerializer.Serialize(AiProviderDefaults.FakeModelId), SettingValueType.String),
                (GovernanceSettingKeys.AiAssistant.DailyMessageLimit, "50", SettingValueType.Integer),
                (GovernanceSettingKeys.AiAssistant.ToolProposalsEnabled, "true", SettingValueType.Boolean)
            })
            {
                var setting = await setup.SystemSettings.SingleOrDefaultAsync(row => row.SettingKey == key);
                if (setting is null)
                    setup.SystemSettings.Add(new SystemSetting { SettingKey = key, Value = value, ValueType = type });
                else
                {
                    setting.Value = value;
                    setting.ValueType = type;
                }
            }
            await setup.SaveChangesAsync();
            var sql = new HumanPrincipalSourceSql(setup);
            string runtime = sql.Quote(postgres.NativeQueueEnrollment.RuntimeLogin);
            // Private human/actor sources and receipts remain execute-only. Giving Runtime
            // ordinary table rights on them invalidates native actor-source admission.
            string settingsGrant = $"GRANT SELECT ON {sql.Table(typeof(SystemSetting))}, {sql.Table(typeof(TenantSetting))} TO {runtime}";
            await setup.Database.ExecuteSqlRawAsync(settingsGrant);
            foreach (Type type in new[]
            {
                typeof(AiConversation), typeof(AiMessage), typeof(AiRun), typeof(AiConversationReference),
                typeof(AiProposedAction), typeof(AiToolExecution)
            })
            {
                string graphGrant = $"GRANT SELECT, INSERT, UPDATE, DELETE ON {sql.Table(type)} TO {runtime}";
                await setup.Database.ExecuteSqlRawAsync(graphGrant);
            }
            foreach (Type type in new[]
            {
                typeof(AiConversationStatusLookup), typeof(AiMessageRoleLookup), typeof(AiRunStatusLookup),
                typeof(AiReferenceKindLookup), typeof(AiProposedActionKindLookup),
                typeof(AiProposedActionStatusLookup), typeof(AiProviderKindLookup)
            })
                await setup.Database.ExecuteSqlRawAsync($"GRANT SELECT ON {sql.Table(type)} TO {runtime}");
        }
        UserId = await ProvisionAndLoginAsync();
        OtherUserId = await ProvisionAndLoginAsync();
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var deployment = scope.ServiceProvider.GetRequiredService<IDeploymentModeProvider>();
            await deployment.InvalidateCacheAsync();
            await Assert.That(await deployment.GetCurrentModeAsync()).IsEqualTo(DeploymentMode.MultiTenant);
        }
        Client.DefaultRequestHeaders.Add("X-Tenant-Slug", slug);
        host.UseRuntimeRequestContexts();
    }

    private static Tenant NewTenant(Guid id, string tenantSlug) => new()
    {
        Id = id,
        FullName = "Native AI flow",
        Slug = tenantSlug,
        TenantStatusId = (int)TenantStatusEnum.Active,
        TenantStatus = null!
    };

    private async Task<Guid> ProvisionAndLoginAsync()
    {
        await using var scope = host.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().BindExecutionTenant(TenantId);
        await using var setup = postgres.CreateDbContext();
        setup.TenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        setup.ClearTenantFilterBypass();
        setup.BindTenantLease();
        scope.ServiceProvider.GetRequiredService<ExploreDbContextExecutionLease>().Bind(setup, TenantId);
        var database = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var credentials = scope.ServiceProvider.GetRequiredService<ILocalCredentialAdministration>();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<LocalIdentityUser>>();
        var initiator = new User
        {
            Id = Guid.CreateVersion7(),
            CreatedAt = DateTime.UtcNow,
            Pii = new UserPii { Email = string.Empty, FirstName = "Credential", LastName = "Provisioner" }
        };
        database.Users.Add(initiator);
        if (!await database.InstanceBootstrapStates.AnyAsync())
        {
            var bootstrap = InstanceBootstrapState.CreateInteractivePending(
                Guid.CreateVersion7(), DeploymentMode.MultiTenant, DateTime.UtcNow);
            bootstrap.CompleteInteractive(initiator.Id, DateTime.UtcNow);
            database.InstanceBootstrapStates.Add(bootstrap);
        }
        await database.SaveChangesAsync();
        string email = $"ai-{Guid.CreateVersion7():N}@example.test";
        string password = $"Aa1!{Convert.ToHexString(RandomNumberGenerator.GetBytes(32))}";
        var created = await credentials.CreatePendingAsync(new LocalCredentialCreateRequest(
            Guid.CreateVersion7(), initiator.Id, email, "AI", "Participant"), CancellationToken.None);
        await Assert.That(created.Outcome).IsEqualTo(LocalCredentialCreateOutcome.Created);
        var receipt = created.Receipt!;
        var user = new User
        {
            Id = receipt.LocalSubjectId,
            EmailVerified = true,
            CreatedAt = DateTime.UtcNow,
            Pii = new UserPii { Email = email, FirstName = "AI", LastName = "Participant" }
        };
        database.AddRange(user, new Actor
        {
            Id = receipt.PersonalActorId,
            UserId = user.Id,
            User = user,
            ActorTypeId = (int)ActorTypeEnum.User,
            ActorType = null!,
            Pii = new ActorPii { DisplayName = "AI Participant" },
            CreatedAt = DateTime.UtcNow
        }, new UserExternalLogin
        {
            Id = receipt.ExternalLoginId,
            UserId = user.Id,
            User = user,
            AuthenticationProviderId = (int)AuthenticationProviderKind.Local,
            AuthenticationProvider = null!,
            ProviderKey = user.Id.ToString("D"),
            CreatedAt = DateTime.UtcNow
        });
        await database.SaveChangesAsync();
        var pending = (await credentials.ReadProvisioningAsync(receipt.OperationId, CancellationToken.None))!;
        await Assert.That(await credentials.ActivateChangeRequiredAsync(
            new LocalCredentialActivationRequest(receipt.OperationId, pending.OperationConcurrencyStamp), CancellationToken.None))
            .IsEqualTo(LocalCredentialActivationOutcome.Activated);
        var identityUser = (await manager.FindByIdAsync(receipt.LocalSubjectId.ToString("D")))!;
        await database.Entry(identityUser).ReloadAsync();
        DateTimeOffset issuedAt = host.Services.GetRequiredService<TimeProvider>().GetUtcNow();
        await Assert.That(await credentials.ReplaceAsync(new LocalCredentialReplacementRequest(
            new LocalCredentialReplacementAuthority(
                new LocalCredentialReplacementSubject(identityUser.Id, receipt.OperationId, identityUser.SecurityStamp!),
                issuedAt, issuedAt.AddMinutes(5)), password), CancellationToken.None))
            .IsEqualTo(LocalCredentialReplacementOutcome.Replaced);
        await database.Entry(identityUser).ReloadAsync();
        identityUser.EmailConfirmed = true;
        await Assert.That((await manager.UpdateAsync(identityUser)).Succeeded).IsTrue();
        var binding = await credentials.ReadLinkedIdentityAsync(receipt.LocalSubjectId, CancellationToken.None);
        await Assert.That(binding!.CredentialState).IsEqualTo(LocalCredentialState.Ready);
        // Memberships are setup data, not substituted policy decisions.
        await using (var seed = postgres.CreateDbContext())
        {
            foreach (Guid tenant in new[] { TenantId, OtherTenantId })
                seed.TenantUsers.Add(new TenantUser
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = tenant,
                    Tenant = null!,
                    UserId = user.Id,
                    User = null!,
                    StatusId = (int)TenantUserStatusEnum.Active,
                    JoinedAt = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow
                });
            await seed.SaveChangesAsync();
        }
        using var login = new HttpRequestMessage(HttpMethod.Post, "/api/auth/local/login");
        login.Headers.Add("X-Tenant-Slug", slug);
        login.Content = JsonContent.Create(new LocalAuthRequestDto(email, password));
        using var response = await Client.SendAsync(login);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK)
            .Because(host.RequestFailure?.ToString() ?? await response.Content.ReadAsStringAsync());
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        await Assert.That(body.RootElement.GetProperty("success").GetBoolean()).IsTrue();
        tokens.Add(user.Id, body.RootElement.GetProperty("token").GetString()!);
        return user.Id;
    }

    internal HttpRequestMessage CreateRequest(HttpMethod method, string url, Guid? userId = null, Guid? tenantId = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens[userId ?? UserId]);
        request.Headers.Add("X-Tenant-Slug", tenantId == OtherTenantId ? otherSlug : slug);
        return request;
    }

    internal Task<Guid> SubscribeRun(Guid conversationId) => completion.Subscribe(conversationId);

    internal async Task AssertRunCompletedAsync(Task<Guid> signal, Guid runId)
    {
        await Assert.That(await signal.WaitAsync(TimeSpan.FromSeconds(30))).IsEqualTo(runId);
    }

    internal static async Task AssertReceiptMismatchAsync(PostgreSqlContainerFixture postgres)
    {
        Guid tenantId = Guid.CreateVersion7();
        await using var owner = postgres.CreateEnrolledTenantDbContext(
            new SelectedTenant(tenantId), PostgresTenantSessionInterceptor.RuntimeInstance);
        await Assert.That(owner.Database.IsNpgsql()).IsTrue();
        await Assert.That(owner.IsTenantFilterBypassed).IsFalse();
        await Assert.That(owner.BindTenantLease()).IsEqualTo((Guid?)tenantId);
        var repository = new IdempotencyRepository(owner, enrollment: postgres.NativeQueueEnrollment);
        // PostgreSQL timestamps retain microseconds; propose an exactly representable time.
        DateTime now = new(DateTime.UtcNow.Ticks / 10 * 10, DateTimeKind.Utc);
        var record = new IdempotencyRecord
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            Key = "receipt-store-key",
            UserId = Guid.CreateVersion7().ToString("D"),
            RequestMethod = "POST",
            RequestTarget = "/api/ai/assistant/conversations",
            RequestContentType = "application/json",
            RequestBodyHash = new string('a', 64),
            PrincipalFingerprint = new string('b', 64),
            StatusCode = IdempotencyRecord.InProgressStatusCode,
            CreatedAt = now,
            ExpiresAt = now.AddHours(1)
        };
        var claim = await repository.TryClaimAsync(record);
        using var custody = claim.Owner;
        var matching = await repository.LookupAsync(IdempotencyRequestDescriptor.FromRecord(record), now);
        var mismatch = await repository.LookupAsync(
            IdempotencyRequestDescriptor.FromRecord(record) with { RequestBodyHash = new string('c', 64) }, now);
        await Assert.That(claim.IsOwner).IsTrue();
        await Assert.That(matching.Outcome).IsEqualTo(IdempotencyLookupOutcome.Existing);
        await Assert.That(matching.Record).IsNotNull();
        await Assert.That(matching.Record!.Id).IsEqualTo(record.Id);
        await Assert.That(IdempotencyRequestDescriptor.FromRecord(matching.Record))
            .IsEqualTo(IdempotencyRequestDescriptor.FromRecord(record));
        await Assert.That(matching.Record.CreatedAt).IsEqualTo(record.CreatedAt);
        await Assert.That(matching.Record.ExpiresAt).IsEqualTo(record.ExpiresAt);
        await Assert.That(matching.Record.StatusCode).IsEqualTo(record.StatusCode);
        await Assert.That(matching.Record.ResponseBody).IsNull();
        await Assert.That(matching.Record.ContentType).IsNull();
        await Assert.That(mismatch.Outcome).IsEqualTo(IdempotencyLookupOutcome.RequestMismatch);
        await Assert.That(mismatch.Record).IsNull();
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await host.DisposeAsync();
        host.PrivacyKeys.Dispose();
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = host.PrivacyKeys.AuthorityPath
        }.ToString());
        SqliteConnection.ClearPool(connection);
        string directory = Path.GetDirectoryName(host.PrivacyKeys.AuthorityPath)!;
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }

    private sealed record SelectedTenant(Guid TenantId) : ITenantContext;

    private sealed class AiRunCompletion
    {
        private readonly ConcurrentDictionary<Guid, TaskCompletionSource<Guid>> signals = new();
        private readonly ConcurrentDictionary<DbTransaction, (Guid Conversation, Guid Run)[]> pending = new();
        private TaskCompletionSource<Guid> Signal(Guid conversation) => signals.GetOrAdd(conversation,
            _ => new TaskCompletionSource<Guid>(TaskCreationOptions.RunContinuationsAsynchronously));
        internal Task<Guid> Subscribe(Guid conversation) => Signal(conversation).Task;
        internal void Completed(Guid conversation, Guid run) => Signal(conversation).TrySetResult(run);
        internal void Capture(DbContext? database)
        {
            if (database?.Database.CurrentTransaction is not { } transaction)
                return;
            var ended = database.ChangeTracker.Entries<AiRun>()
                .Where(entry => (entry.State == EntityState.Added
                        || entry.Property(nameof(AiRun.StatusId)).IsModified)
                    && entry.Entity.Status is AiRunStatus.Succeeded or AiRunStatus.Failed or AiRunStatus.Cancelled)
                .Select(entry => (entry.Entity.ConversationId, entry.Entity.Id)).ToArray();
            if (ended.Length != 0)
                pending.AddOrUpdate(transaction.GetDbTransaction(), ended,
                    (_, previous) => previous.Concat(ended).Distinct().ToArray());
        }

        internal void Committed(DbTransaction transaction)
        {
            if (pending.TryRemove(transaction, out var ended))
                foreach (var run in ended)
                    Completed(run.Conversation, run.Run);
        }

        internal void RolledBack(DbTransaction transaction) => pending.TryRemove(transaction, out _);
    }

    private sealed class ObserveRunSave(AiRunCompletion completion) : SaveChangesInterceptor
    {
        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            completion.Capture(eventData.Context);
            return result;
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            completion.Capture(eventData.Context);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class ObserveRunCommit(AiRunCompletion completion) : DbTransactionInterceptor
    {
        public override void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData) =>
            completion.Committed(transaction);

        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            completion.Committed(transaction);
            return Task.CompletedTask;
        }

        public override void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData) =>
            completion.RolledBack(transaction);

        public override Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            completion.RolledBack(transaction);
            return Task.CompletedTask;
        }
    }

    private sealed class FailingProvider : IAiChatProvider, IAiModelCatalog
    {
        public Task<IReadOnlyList<AiModelDescriptor>> ListAvailableModelsAsync(CancellationToken cancellationToken = default)
            => new FakeAiChatProvider().ListAvailableModelsAsync(cancellationToken);

        public Task<AiChatProviderResult> SendAsync(AiChatPayload request, CancellationToken cancellationToken = default)
            => Task.FromResult(AiChatProviderResult.Failure(
                "provider_unreachable", "AI provider is unavailable.", isTransient: true));
    }
}
