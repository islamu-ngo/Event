using System.Collections.Concurrent;
using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.CustomPropertyDefinition;
using Explore.Application.Features.CustomPropertyDefinitions.Requests.Commands;
using Explore.Application.Features.CustomPropertyDefinitions.Requests.Queries;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Application.Contracts.Operations;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

public sealed partial class CustomPropertyDefinitionPrerequisiteTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Test]
    [Arguments("create")]
    [Arguments("metadata")]
    [Arguments("options")]
    [Arguments("delete")]
    [Arguments("purge")]
    public async Task CommittedMutation_RefreshesAllAffectedPages_WithoutEvictingForeignTenant(string mutation)
    {
        await using var factory = new DefinitionFactory();
        using var client = factory.CreateClient();
        var data = await SeedMutationAsync(factory, client);
        await WarmAsync(factory, data);
        using var response = await MutateAsync(client, data, mutation);
        await Assert.That(response.StatusCode).IsEqualTo(SuccessStatus(mutation));

        var removed = mutation is "metadata" or "delete" or "purge";
        var total = mutation == "create" ? 4 : removed ? 2 : 3;
        var first = await ListAsync(factory, PlatformDefaults.DefaultTenantId, EntityTypeName.Organization, 1, 1);
        var second = await ListAsync(factory, PlatformDefaults.DefaultTenantId, EntityTypeName.Organization, 2, 1);
        var wider = await ListAsync(factory, PlatformDefaults.DefaultTenantId, EntityTypeName.Organization, 1, 2);
        await Assert.That(first.TotalCount).IsEqualTo(total);
        await Assert.That(second.TotalCount).IsEqualTo(total);
        await Assert.That(wider.TotalCount).IsEqualTo(total);
        await Assert.That(wider.Items.Count).IsEqualTo(2);
        await Assert.That(first.Items.Single().Id).IsEqualTo(removed ? data.SecondId : data.Seed.OwnDefinitionId);
        if (mutation == "create")
        {
            var created = (await response.Content.ReadFromJsonAsync<BaseCommandResponse<Guid>>())!;
            await Assert.That(second.Items.Single().Id).IsEqualTo(created.Id);
        }
        else
        {
            await Assert.That(second.Items.Single().Id).IsEqualTo(removed ? data.ThirdId : data.SecondId);
        }
        var group = await ListAsync(factory, PlatformDefaults.DefaultTenantId, EntityTypeName.Group, 1, 1);
        await Assert.That(group.TotalCount).IsEqualTo(mutation == "metadata" ? 1 : 0);
        if (mutation == "metadata")
            await Assert.That(group.Items.Single().Id).IsEqualTo(data.Seed.OwnDefinitionId);
        if (mutation == "options")
            await Assert.That(first.Items.Single().OptionCount).IsEqualTo(3);

        using var detailResponse = await client.GetAsync($"{Root}/{data.Seed.OwnDefinitionId}");
        if (mutation is "delete" or "purge")
        {
            await Assert.That(detailResponse.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
            using var scope = TenantScope(factory, PlatformDefaults.DefaultTenantId);
            var dependencies = await scope.ServiceProvider.GetRequiredService<ICustomPropertyDefinitionRepository>()
                .GetPurgeDependencies(data.Seed.OwnDefinitionId, default);
            if (mutation == "delete")
            {
                await Assert.That(dependencies).IsNotNull();
                await Assert.That(dependencies!.OptionCount).IsEqualTo(2);
            }
            else
            {
                await Assert.That(dependencies).IsNull();
                var purged = (await response.Content.ReadFromJsonAsync<BaseCommandResponse<CustomPropertyPurgeResultDto>>())!.Id!;
                await Assert.That(purged.Purged).IsTrue();
                var audit = await scope.ServiceProvider.GetRequiredService<IAuditLogRepository>().GetById(purged.AuditLogId!.Value);
                await Assert.That(audit!.EntityId).IsEqualTo(data.Seed.OwnDefinitionId.ToString());
                await Assert.That(audit.TenantId).IsEqualTo(PlatformDefaults.DefaultTenantId);
            }
        }
        else
        {
            await Assert.That(detailResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
            var detail = (await detailResponse.Content.ReadFromJsonAsync<CustomPropertyDefinitionDto>(JsonOptions))!;
            await Assert.That(detail.Id).IsEqualTo(data.Seed.OwnDefinitionId);
            await Assert.That(detail.DefaultOptionId).IsEqualTo(data.KeptOptionId);
            await Assert.That(detail.Options.Single(option => option.Key == "kept").Id).IsEqualTo(data.KeptOptionId);
            await Assert.That(detail.Options.Single(option => option.Key == "retired").Id).IsEqualTo(data.RetiredOptionId);
            await Assert.That(detail.Options.Single(option => option.Key == "retired").IsActive).IsEqualTo(mutation != "options");
            await Assert.That(detail.Options.Count).IsEqualTo(mutation == "options" ? 3 : 2);
            await Assert.That(detail.Options.Where(option => option.Key != "new").Select(option => option.Key)
                .SequenceEqual(["kept", "retired"])).IsTrue();
            await Assert.That(detail.Options.Single(option => option.Key == "kept").SortOrder).IsEqualTo(10);
            await Assert.That(detail.Options.Single(option => option.Key == "retired").SortOrder).IsEqualTo(20);
            if (mutation == "options")
            {
                await Assert.That(detail.Options.Single(option => option.Key == "new").SortOrder).IsEqualTo(5);
                await Assert.That(detail.Options.Select(option => option.Key).SequenceEqual(["new", "kept", "retired"])).IsTrue();
            }
        }
        await AssertForeignStillCachedAsync(factory, data);
    }

    [Test]
    [Arguments("create")]
    [Arguments("metadata")]
    [Arguments("options")]
    [Arguments("delete")]
    [Arguments("purge")]
    public async Task FailedCommit_RollsBackDefinitionOptionsAndAudit_WithoutInvalidatingCachedPages(string mutation)
    {
        await using var factory = new DefinitionFactory();
        using var client = factory.CreateClient();
        var data = await SeedMutationAsync(factory, client);
        await WarmAsync(factory, data);
        factory.Commits.Fail = true;
        try
        {
            using var response = await MutateAsync(client, data, mutation);
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.InternalServerError);
            await Assert.That(response.Content.Headers.ContentType!.MediaType).IsEqualTo("application/problem+json");
        }
        finally
        {
            factory.Commits.Fail = false;
        }
        await AssertOwnStillCachedAsync(factory, data);
        await AssertForeignStillCachedAsync(factory, data);
        using var detailResponse = await client.GetAsync($"{Root}/{data.Seed.OwnDefinitionId}");
        await Assert.That(detailResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var detail = (await detailResponse.Content.ReadFromJsonAsync<CustomPropertyDefinitionDto>(JsonOptions))!;
        await Assert.That(detail.EntityTypeName).IsEqualTo(EntityTypeName.Organization);
        await Assert.That(detail.DisplayName).IsEqualTo("own_definition");
        await Assert.That(detail.Options.Count).IsEqualTo(2);
        await Assert.That(detail.DefaultOptionId).IsEqualTo(data.KeptOptionId);
        await Assert.That(detail.Options.Single(option => option.Key == "retired").IsActive).IsTrue();
        using var scope = TenantScope(factory, PlatformDefaults.DefaultTenantId);
        var repository = scope.ServiceProvider.GetRequiredService<ICustomPropertyDefinitionRepository>();
        await Assert.That((await repository.GetDefinitionsWithDetailsPaged(EntityTypeName.Organization, 1, 10)).TotalCount).IsEqualTo(3);
        await Assert.That((await scope.ServiceProvider.GetRequiredService<IAuditLogRepository>().GetAll()).Count).IsEqualTo(0);
    }

    [Test]
    public async Task InFlightCommit_LeavesOldCacheVisible_UntilCommitCompletes()
    {
        await using var factory = new DefinitionFactory();
        using var client = factory.CreateClient();
        var data = await SeedMutationAsync(factory, client);
        await WarmAsync(factory, data);
        var gate = factory.Commits.Pause();
        var mutation = MutateAsync(client, data, "create");
        try
        {
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await AssertOwnStillCachedAsync(factory, data);
            await AssertForeignStillCachedAsync(factory, data);
        }
        finally
        {
            gate.Release.TrySetResult();
        }
        using var response = await mutation.WaitAsync(TimeSpan.FromSeconds(15));
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Created);
        await Assert.That((await ListAsync(factory, PlatformDefaults.DefaultTenantId, EntityTypeName.Organization, 2, 1)).TotalCount).IsEqualTo(4);
        await AssertForeignStillCachedAsync(factory, data);
    }

    [Test]
    public async Task CancellationAfterCommit_DoesNotSkipInvalidationOrReportRolledBackMutation()
    {
        await using var factory = new DefinitionFactory();
        using var client = factory.CreateClient();
        var data = await SeedMutationAsync(factory, client);
        await WarmAsync(factory, data);
        using var cancellation = new CancellationTokenSource();
        factory.Commits.AfterCommit = cancellation.Cancel;
        using var scope = TenantScope(factory, PlatformDefaults.DefaultTenantId);
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", data.Seed.UserId.ToString())], "Test"))
        };
        var result = await scope.ServiceProvider.GetRequiredService<ICommandHandler<CreateCustomPropertyDefinitionCommand, BaseCommandResponse<Guid>>>().ExecuteAsync(
            new CreateCustomPropertyDefinitionCommand { DefinitionDto = CreateDto() }, cancellation.Token);
        await Assert.That(cancellation.IsCancellationRequested).IsTrue();
        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That((await ListAsync(factory, PlatformDefaults.DefaultTenantId, EntityTypeName.Organization, 2, 1)).TotalCount).IsEqualTo(4);
        await AssertForeignStillCachedAsync(factory, data);
    }

    [Test]
    public async Task PurgeRetiredDefinition_UsesPersistedTenantAndPreservesForeignCachedPages()
    {
        await using var factory = new DefinitionFactory();
        using var client = factory.CreateClient();
        var data = await SeedMutationAsync(factory, client);
        await WarmAsync(factory, data);
        using var deleted = await MutateAsync(client, data, "delete");
        await Assert.That(deleted.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That((await ListAsync(factory, PlatformDefaults.DefaultTenantId, EntityTypeName.Organization, 2, 1)).TotalCount).IsEqualTo(2);
        using var purged = await MutateAsync(client, data, "purge");
        await Assert.That(purged.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var result = (await purged.Content.ReadFromJsonAsync<BaseCommandResponse<CustomPropertyPurgeResultDto>>())!.Id!;
        await Assert.That(result.TenantId).IsEqualTo(PlatformDefaults.DefaultTenantId);
        await Assert.That(result.Purged).IsTrue();
        await Assert.That((await ListAsync(factory, PlatformDefaults.DefaultTenantId, EntityTypeName.Organization, 2, 1)).Items.Single().Id).IsEqualTo(data.ThirdId);
        using var scope = TenantScope(factory, PlatformDefaults.DefaultTenantId);
        await Assert.That(await scope.ServiceProvider.GetRequiredService<ICustomPropertyDefinitionRepository>()
            .GetPurgeDependencies(data.Seed.OwnDefinitionId, default)).IsNull();
        await Assert.That(await scope.ServiceProvider.GetRequiredService<IAuditLogRepository>()
            .GetById(result.AuditLogId!.Value)).IsNotNull();
        await AssertForeignStillCachedAsync(factory, data);
    }

    [Test]
    [Arguments("duplicate", HttpStatusCode.BadRequest)]
    [Arguments("invalid", HttpStatusCode.BadRequest)]
    [Arguments("stale", HttpStatusCode.Conflict)]
    [Arguments("blocked-purge", HttpStatusCode.BadRequest)]
    [Arguments("missing-delete", HttpStatusCode.NoContent)]
    [Arguments("missing-purge", HttpStatusCode.NotFound)]
    public async Task RejectedOrMissingMutation_DoesNotEvictOrAlterEitherTenantsCachedPages(string failure, HttpStatusCode status)
    {
        await using var factory = new DefinitionFactory();
        using var client = factory.CreateClient();
        var data = await SeedMutationAsync(factory, client);
        if (failure == "blocked-purge")
        {
            using var scope = TenantScope(factory, PlatformDefaults.DefaultTenantId);
            await scope.ServiceProvider.GetRequiredService<IAuditLogRepository>().Create(new AuditLog
            {
                Id = Guid.CreateVersion7(),
                TenantId = PlatformDefaults.DefaultTenantId,
                Tenant = null!,
                EntityType = nameof(CustomPropertyDefinition),
                EntityId = data.Seed.OwnDefinitionId.ToString(),
                Action = "RetainedReference",
                Timestamp = DateTime.UtcNow
            });
        }
        await WarmAsync(factory, data);
        using var response = failure switch
        {
            "duplicate" => await client.PostAsJsonAsync(Root, CreateDto() with { Key = "own_definition" }),
            "invalid" => await client.PostAsJsonAsync(Root, CreateDto() with { DisplayName = "" }),
            "stale" => await MutateAsync(client, data with { Stamp = Guid.CreateVersion7() }, "metadata"),
            "blocked-purge" => await MutateAsync(client, data, "purge"),
            "missing-delete" => await client.DeleteAsync($"{Root}/{Guid.CreateVersion7()}"),
            _ => await MutateAsync(client, data with { Seed = data.Seed with { OwnDefinitionId = Guid.CreateVersion7() } }, "purge")
        };
        await Assert.That(response.StatusCode).IsEqualTo(status);
        await AssertOwnStillCachedAsync(factory, data);
        await AssertForeignStillCachedAsync(factory, data);
    }

    private static async Task<MutationData> SeedMutationAsync(DefinitionFactory factory, HttpClient client)
    {
        var seed = await SeedAsync(factory);
        using var scope = TenantScope(factory, PlatformDefaults.DefaultTenantId);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var role = await db.Set<Role>().SingleAsync(item => item.MasterCode == "platform.admin");
        db.PlatformUserRoles.Add(new PlatformUserRole { Id = Guid.CreateVersion7(), UserId = seed.UserId, User = null!, RoleId = role.Id, Role = role });
        var own = await db.CustomPropertyDefinitions.SingleAsync(row => row.Id == seed.OwnDefinitionId);
        own.PropertyType = PropertyType.Option;
        var kept = Option(own.Id, "kept", 10, true);
        var retired = Option(own.Id, "retired", 20, false);
        db.CustomPropertyOptions.AddRange(kept, retired);
        var second = Definition(PlatformDefaults.DefaultTenantId, "second_definition");
        second.SortOrder = 10;
        var third = Definition(PlatformDefaults.DefaultTenantId, "third_definition");
        third.SortOrder = 20;
        db.CustomPropertyDefinitions.AddRange(second, third);
        await db.SaveChangesAsync();
        own.DefaultOptionId = kept.Id;
        await db.SaveChangesAsync();
        client.DefaultRequestHeaders.Add(TestAuthHandler.AuthHeaderName,
            TestAuthHandler.CreateAuthHeaderValue(seed.UserId, "Cache repair administrator", (ClaimTypes.Role, "Admin")));
        return new(seed, second.Id, third.Id, kept.Id, retired.Id, own.ConcurrencyStamp);
    }

    private static CustomPropertyOption Option(Guid definitionId, string key, int sortOrder, bool isDefault) => new()
    {
        Id = Guid.CreateVersion7(),
        ConcurrencyStamp = Guid.CreateVersion7(),
        CustomPropertyDefinitionId = definitionId,
        Namespace = "tenant.community",
        Key = key,
        DisplayName = key,
        Value = key,
        SortOrder = sortOrder,
        IsDefault = isDefault,
        IsActive = true
    };

    private static CreateCustomPropertyDefinitionDto CreateDto() => new()
    {
        EntityTypeName = EntityTypeName.Organization,
        Namespace = "tenant.community",
        Key = "created_definition",
        DisplayName = "Created definition",
        PropertyType = PropertyType.Text,
        ExposureLevel = ExposureLevel.Internal,
        SortOrder = 5
    };

    private static async Task<HttpResponseMessage> MutateAsync(HttpClient client, MutationData data, string mutation)
    {
        if (mutation == "create") return await client.PostAsJsonAsync(Root, CreateDto());
        if (mutation == "delete") return await client.DeleteAsync($"{Root}/{data.Seed.OwnDefinitionId}");
        if (mutation == "purge")
        {
            using var purge = new HttpRequestMessage(HttpMethod.Delete, $"{Root}/{data.Seed.OwnDefinitionId}/purge")
            {
                Content = JsonContent.Create(new PurgeCustomPropertyDefinitionDto("Disposable cache regression"))
            };
            return await client.SendAsync(purge);
        }
        var patch = mutation == "metadata"
            ? new UpdateCustomPropertyDefinitionDto
            {
                Relations = new() { EntityTypeName = EntityTypeName.Group },
                Metadata = new() { DisplayName = "Moved definition" }
            }
            : new UpdateCustomPropertyDefinitionDto
            {
                Options = new()
                {
                    Items =
                    [
                        new() { Namespace = "tenant.community", Key = "kept", DisplayName = "Kept", Value = "kept", IsDefault = true, IsActive = true, SortOrder = 10 },
                        new() { Namespace = "tenant.community", Key = "new", DisplayName = "New", Value = "new", IsActive = true, SortOrder = 5 }
                    ]
                }
            };
        using var request = new HttpRequestMessage(HttpMethod.Patch, $"{Root}/{data.Seed.OwnDefinitionId}") { Content = JsonContent.Create(patch) };
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{data.Stamp:D}\"");
        return await client.SendAsync(request);
    }

    private static HttpStatusCode SuccessStatus(string mutation) => mutation switch
    {
        "create" => HttpStatusCode.Created,
        "delete" => HttpStatusCode.NoContent,
        _ => HttpStatusCode.OK
    };

    private static IServiceScope TenantScope(DefinitionFactory factory, Guid tenantId)
    {
        var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(tenantId);
        return scope;
    }

    private static async Task<PaginatedResult<CustomPropertyDefinitionListDto>> ListAsync(
        DefinitionFactory factory, Guid tenantId, EntityTypeName entityType, int page, int size)
    {
        using var scope = TenantScope(factory, tenantId);
        return await scope.ServiceProvider.GetRequiredService<IQueryHandler<GetCustomPropertyDefinitionListQuery, PaginatedResult<CustomPropertyDefinitionListDto>>>()
            .QueryAsync(new GetCustomPropertyDefinitionListQuery(entityType, page, size), default);
    }

    private static async Task WarmAsync(DefinitionFactory factory, MutationData data)
    {
        foreach (var tenant in new[] { PlatformDefaults.DefaultTenantId, data.Seed.ForeignTenantId })
        {
            foreach (var entityType in new[] { EntityTypeName.Organization, EntityTypeName.Group })
            {
                await ListAsync(factory, tenant, entityType, 1, 1);
                await ListAsync(factory, tenant, entityType, 2, 1);
                await ListAsync(factory, tenant, entityType, 1, 2);
            }
        }
        await Assert.That(factory.Reads.Count(PlatformDefaults.DefaultTenantId)).IsGreaterThan(0);
        await Assert.That(factory.Reads.Count(data.Seed.ForeignTenantId)).IsGreaterThan(0);
        await AssertOwnStillCachedAsync(factory, data);
        await AssertForeignStillCachedAsync(factory, data);
    }

    private static async Task AssertOwnStillCachedAsync(DefinitionFactory factory, MutationData data)
    {
        var before = factory.Reads.Count(PlatformDefaults.DefaultTenantId);
        var first = await ListAsync(factory, PlatformDefaults.DefaultTenantId, EntityTypeName.Organization, 1, 1);
        var second = await ListAsync(factory, PlatformDefaults.DefaultTenantId, EntityTypeName.Organization, 2, 1);
        var wider = await ListAsync(factory, PlatformDefaults.DefaultTenantId, EntityTypeName.Organization, 1, 2);
        var group = await ListAsync(factory, PlatformDefaults.DefaultTenantId, EntityTypeName.Group, 1, 1);
        await Assert.That(first.TotalCount).IsEqualTo(3);
        await Assert.That(first.Items.Single().Id).IsEqualTo(data.Seed.OwnDefinitionId);
        await Assert.That(first.Items.Single().OptionCount).IsEqualTo(2);
        await Assert.That(second.Items.Single().Id).IsEqualTo(data.SecondId);
        await Assert.That(wider.Items.Select(row => row.Id).ToArray()).IsEquivalentTo(new[] { data.Seed.OwnDefinitionId, data.SecondId });
        await Assert.That(group.TotalCount).IsEqualTo(0);
        await Assert.That(factory.Reads.Count(PlatformDefaults.DefaultTenantId)).IsEqualTo(before);
    }

    private static async Task AssertForeignStillCachedAsync(DefinitionFactory factory, MutationData data)
    {
        var before = factory.Reads.Count(data.Seed.ForeignTenantId);
        var first = await ListAsync(factory, data.Seed.ForeignTenantId, EntityTypeName.Organization, 1, 1);
        var second = await ListAsync(factory, data.Seed.ForeignTenantId, EntityTypeName.Organization, 2, 1);
        var wider = await ListAsync(factory, data.Seed.ForeignTenantId, EntityTypeName.Organization, 1, 2);
        var group = await ListAsync(factory, data.Seed.ForeignTenantId, EntityTypeName.Group, 1, 1);
        await Assert.That(first.Items.Single().Id).IsEqualTo(data.Seed.ForeignDefinitionId);
        await Assert.That(first.TotalCount).IsEqualTo(1);
        await Assert.That(second.Items.Count).IsEqualTo(0);
        await Assert.That(wider.Items.Single().Id).IsEqualTo(data.Seed.ForeignDefinitionId);
        await Assert.That(group.TotalCount).IsEqualTo(0);
        await Assert.That(factory.Reads.Count(data.Seed.ForeignTenantId)).IsEqualTo(before);
    }

    private sealed record MutationData(SeedData Seed, Guid SecondId, Guid ThirdId, Guid KeptOptionId, Guid RetiredOptionId, Guid Stamp);

    private sealed class DefinitionReadMonitor : DbCommandInterceptor
    {
        private readonly ConcurrentDictionary<Guid, int> _reads = new();
        public int Count(Guid tenantId) => _reads.GetValueOrDefault(tenantId);
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context is ExploreDbContext { TenantContext: { } tenant }
                && command.CommandText.StartsWith("SELECT", StringComparison.Ordinal)
                && command.CommandText.Contains("ie_custom_property_definitions", StringComparison.Ordinal))
                _reads.AddOrUpdate(tenant.TenantId, 1, (_, count) => count + 1);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class CommitGate
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class DefinitionCommitControl : DbTransactionInterceptor
    {
        private CommitGate? _gate;
        public bool Fail { get; set; }
        public Action? AfterCommit { get; set; }
        public CommitGate Pause() => _gate = new();
        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _gate, null) is { } gate)
            {
                gate.Entered.TrySetResult();
                await gate.Release.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
            }
            if (Fail) throw new InvalidOperationException("Injected custom-property commit failure.");
            return result;
        }
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            AfterCommit?.Invoke();
            return Task.CompletedTask;
        }
    }
}
