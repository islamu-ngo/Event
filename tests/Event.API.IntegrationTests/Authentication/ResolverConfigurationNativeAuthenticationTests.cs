using System.Collections.Immutable;
using System.Security.Claims;
using System.Text.Json;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Constants;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Instance;
using Explore.Application.Features.InstanceOnboarding.Handlers.Commands;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Models.Common;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;

namespace Event.Api.IntegrationTests.Authentication;

[NotInParallel]
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
public sealed class ResolverConfigurationNativeAuthenticationTests(PostgreSqlContainerFixture postgres)
{
    [Test]
    [Arguments("partial")]
    [Arguments("full")]
    [Arguments("insert")]
    [Arguments("unchanged-value")]
    [Arguments("binding-revoked")]
    [Arguments("administrator-revoked")]
    [Arguments("principal-replaced")]
    [Arguments("path-replaced")]
    [Arguments("credential-replaced")]
    [Arguments("support-replaced")]
    [Arguments("source-changed")]
    [Arguments("terminal-expiry")]
    [Arguments("extended-ticket")]
    [Arguments("failure")]
    [Arguments("effect-tampered")]
    [Arguments("unpatched-tampered")]
    [Arguments("connection-closed")]
    [Arguments("connection-reopened")]
    public async Task SignedResolverCommandRetainsOriginalOwnerExactEffectsAndDeadline(string change)
    {
        await using var fixture = await ResolverConfigurationNativeFixture.CreateAsync(postgres);
        DateTime instant = fixture.Clock.GetUtcNow().UtcDateTime.AddTicks(-7);
        await Assert.That(fixture.Clock.GetUtcNow().Ticks % 10).IsEqualTo(7L);
        var original = await SeedAsync(instant, change == "insert", change == "unchanged-value");
        bool success = change is "partial" or "full" or "insert" or "unchanged-value";
        var command = Command(fixture.Actor, change == "full");
        fixture.Writer.AfterObservation = async token =>
        {
            await Assert.That(fixture.Owner.Database.CurrentTransaction).IsNull();
            if (change == "principal-replaced") fixture.Request.User = new ClaimsPrincipal(fixture.Request.User);
            if (change == "path-replaced") fixture.Request.Request.Path = "/api/instance/settings/other";
            if (change == "credential-replaced") fixture.Request.Request.Headers.Authorization = "Bearer replaced";
            if (change == "support-replaced")
                fixture.Request.Request.Headers[SupportAccessHeaderNames.SessionId] = Guid.CreateVersion7().ToString();
            if (change is "connection-closed" or "connection-reopened")
            {
                await fixture.Owner.Database.CloseConnectionAsync();
                if (change == "connection-reopened") await fixture.Owner.Database.OpenConnectionAsync(token);
            }
            await using var independent = postgres.CreateDbContext();
            if (change == "binding-revoked")
                await independent.UserExternalLogins.Where(row => row.Id == fixture.Binding).ExecuteDeleteAsync(token);
            if (change == "administrator-revoked")
                await independent.PlatformUserRoles.Where(row => row.Id == fixture.Grant).ExecuteDeleteAsync(token);
            if (change == "source-changed")
                await independent.SystemSettings.Where(row => row.SettingKey == ResolverConfigurationMutation.Keys[0])
                    .ExecuteUpdateAsync(set => set.SetProperty(row => row.Description, "Changed validation metadata"), token);
        };
        fixture.Writer.AfterWrite = async (mutation, token) =>
        {
            await Assert.That(ReferenceEquals(mutation.OriginalCommand, command)).IsTrue();
            await Assert.That(ReferenceEquals(mutation.OriginalOwner, fixture.Services.GetRequiredService<IUnitOfWork>())).IsTrue();
            await Assert.That(ReferenceEquals(mutation.Source.Lifetime, fixture.Lifetime)).IsTrue();
            foreach (var effect in mutation.Effects)
            {
                await Assert.That(effect.UpdatedAt).IsEqualTo((DateTime?)instant);
                await Assert.That(effect.UpdatedBy).IsEqualTo((Guid?)fixture.Actor);
            }
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Writer.Inner.ExecuteAsync(mutation, token));
            if (change == "extended-ticket")
            {
                var authentication = await fixture.Request.AuthenticateAsync();
                authentication.Properties!.ExpiresUtc = fixture.Lifetime.ExpiresAt.AddYears(1);
                var identity = (ClaimsIdentity)fixture.Request.User.Identity!;
                var expiry = identity.FindFirst("exp");
                if (expiry is not null) identity.RemoveClaim(expiry);
                identity.AddClaim(new("exp", fixture.Lifetime.ExpiresAt.AddYears(1).ToUnixTimeSeconds()
                    .ToString(System.Globalization.CultureInfo.InvariantCulture)));
            }
            if (change is "terminal-expiry" or "extended-ticket")
                fixture.Services.GetRequiredService<IHumanActionCommitFinalizer>().Register(_ =>
                {
                    fixture.Clock.Set(fixture.Lifetime.ExpiresAt.Add(fixture.Lifetime.ClockSkew).AddTicks(1));
                    return Task.CompletedTask;
                }, _ => { });
            if (change == "failure") throw new IOException("Abort the original provisional resolver patch.");
            if (change is "effect-tampered" or "unpatched-tampered")
            {
                var beforeTamper = mutation.Before.SetItems(mutation.Effects.Select(effect =>
                    KeyValuePair.Create<string, ResolverConfigurationSettingSnapshot?>(effect.Key, effect)));
                var changed = change == "effect-tampered"
                    ? mutation.Effects.Single() with { Value = "\"/substituted\"" }
                    : beforeTamper[ResolverConfigurationMutation.Keys[6]]! with
                    {
                        Value = "false",
                        IsLocked = false,
                        AllowedValues = null,
                        Category = "Domains",
                        Description = "Whether tenant administrators may configure a custom domain.",
                        DisplayOrder = 2,
                        UpdatedAt = instant,
                        UpdatedBy = fixture.Actor
                    };
                await NativeWriteAsync(fixture.Owner, fixture.Actor, beforeTamper, [changed], instant);
            }
        };
        Task Execute() => fixture.InRequestAsync(async () =>
        {
            var handler = ActivatorUtilities.CreateInstance<UpdateResolverConfigurationCommandHandler>(fixture.Services);
            await Assert.That((await handler.ExecuteAsync(command, default)).IsSuccess).IsTrue();
        });
        if (success) await Execute();
        else if (change == "failure") await Assert.ThrowsAsync<IOException>(Execute);
        else await Assert.ThrowsAsync<UnauthorizedAccessException>(Execute);
        await using var observer = postgres.CreateDbContext();
        var keys = ResolverConfigurationMutation.Keys.ToArray();
        var actual = (await observer.SystemSettings.AsNoTracking().Where(row => keys.Contains(row.SettingKey)).ToListAsync())
            .ToDictionary(row => row.SettingKey, ResolverConfigurationSettingSnapshot.From, StringComparer.Ordinal);
        foreach (var key in keys)
        {
            var expected = success
                ? fixture.Writer.Mutation!.Effects.SingleOrDefault(effect => effect.Key == key) ?? original[key]
                : original[key];
            if (change == "source-changed" && key == keys[0])
                expected = expected! with { Description = "Changed validation metadata" };
            await Assert.That(actual.GetValueOrDefault(key)).IsEqualTo(expected);
        }
        if (success)
        {
            await Assert.That(actual[keys[4]].Value).IsEqualTo("\"/communities\"");
            await Assert.That(actual[keys[4]].CreatedBy).IsEqualTo(change == "insert" ? (Guid?)fixture.Actor : null);
        }
        await Assert.That(fixture.Owner.Database.CurrentTransaction).IsNull();
        if (fixture.Writer.Mutation is { } ended)
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Writer.Inner.ExecuteAsync(ended, default));
    }

    [Test]
    public async Task ExactNativeEffectsCannotReplaceCompulsoryFixedWriterCompletion()
    {
        await using var fixture = await ResolverConfigurationNativeFixture.CreateAsync(postgres);
        DateTime instant = fixture.Clock.GetUtcNow().UtcDateTime.AddTicks(-7);
        var original = await SeedAsync(instant);
        await fixture.InRequestAsync(async () =>
        {
            var command = Command(fixture.Actor);
            var source = await fixture.Services.GetRequiredService<IResolverConfigurationMutationAuthority>().CaptureAsync(command, default);
            var owner = fixture.Services.GetRequiredService<IUnitOfWork>();
            var before = await fixture.Writer.ObserveAsync(source, owner, default);
            using var mutation = await ResolverConfigurationMutation.CompileAsync(command, source, owner, before,
                fixture.Writer.NormalizeEffectTimestamp(fixture.Clock.GetUtcNow().UtcDateTime), default);
            var preparation = fixture.Services.GetRequiredService<IHumanSettingsMutationPreparation>();
            var proof = await preparation.PrepareAsync(command, mutation);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => preparation.ExecutePreparedAsync(proof, async (_, _) =>
            {
                await NativeWriteAsync(fixture.Owner, fixture.Actor, before, mutation.Effects, instant);
                return true;
            }));
        });
        await using var observer = postgres.CreateDbContext();
        foreach (var key in ResolverConfigurationMutation.Keys)
            await Assert.That(ResolverConfigurationSettingSnapshot.From(await observer.SystemSettings.AsNoTracking()
                .SingleAsync(row => row.SettingKey == key))).IsEqualTo(original[key]);
    }

    [Test]
    public async Task GenuineSourceCannotReplaceItsOriginalPhysicalUnit()
    {
        await using var fixture = await ResolverConfigurationNativeFixture.CreateAsync(postgres);
        await fixture.InRequestAsync(async () =>
        {
            var source = await fixture.Services.GetRequiredService<IResolverConfigurationMutationAuthority>()
                .CaptureAsync(Command(fixture.Actor), default);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                fixture.Writer.ObserveAsync(source, new EfCoreUnitOfWork(fixture.Owner), default));
        });
        await Assert.That(fixture.Owner.Database.CurrentTransaction).IsNull();
    }

    private static UpdateResolverConfigurationCommand Command(Guid actor, bool full = false) => new()
    {
        UserId = actor,
        Patch = full ? new PatchResolverConfigurationDto
        {
            HeaderEnabled = OptionalUpdate<bool>.Set(true),
            CustomDomainEnabled = OptionalUpdate<bool>.Set(false),
            SubdomainEnabled = OptionalUpdate<bool>.Set(true),
            PathEnabled = OptionalUpdate<bool>.Set(true),
            PathPrefix = OptionalUpdate<string?>.Set(" /communities "),
            InstanceBaseDomain = OptionalUpdate<string?>.Set("https://EXAMPLE.TEST/"),
            AllowTenantCustomDomains = OptionalUpdate<bool>.Set(false)
        } : new() { PathPrefix = OptionalUpdate<string?>.Set(" /communities ") }
    };

    private async Task<ImmutableDictionary<string, ResolverConfigurationSettingSnapshot?>> SeedAsync(
        DateTime instant, bool insert = false, bool unchanged = false)
    {
        await using var seed = postgres.CreateDbContext();
        var keys = ResolverConfigurationMutation.Keys.ToArray();
        await seed.SystemSettings.Where(row => keys.Contains(row.SettingKey)).ExecuteDeleteAsync();
        for (int index = 0; index < keys.Length; index++)
        {
            if (insert && index == 4) continue;
            seed.SystemSettings.Add(new()
            {
                Id = Guid.CreateVersion7(),
                SettingKey = keys[index],
                Value = index == 4 ? unchanged ? "\"/communities\"" : "\"/original\"" : index == 5 ? "\"example.test\"" : "true",
                ValueType = index is 4 or 5 ? SettingValueType.String : SettingValueType.Boolean,
                IsLocked = true,
                Description = "Original metadata",
                Category = "Original",
                DisplayOrder = 73,
                CreatedAt = instant.AddDays(-1),
                CreatedBy = null
            });
        }
        await seed.SaveChangesAsync();
        var rows = await seed.SystemSettings.AsNoTracking().Where(row => keys.Contains(row.SettingKey)).ToListAsync();
        return keys.ToImmutableDictionary(key => key, key =>
            rows.SingleOrDefault(row => row.SettingKey == key) is { } row ? ResolverConfigurationSettingSnapshot.From(row) : null,
            StringComparer.Ordinal);
    }

    private static async Task NativeWriteAsync(ExploreDbContext owner, Guid actor,
        ImmutableDictionary<string, ResolverConfigurationSettingSnapshot?> before,
        IEnumerable<ResolverConfigurationSettingSnapshot> effects, DateTime instant)
    {
        await using var command = owner.Database.GetDbConnection().CreateCommand();
        command.Transaction = owner.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = $"SELECT {PostgresResolverConfigurationContract.FunctionSql(owner, "write")}(@actor,@before,@effects,@instant)";
        command.Parameters.Add(new NpgsqlParameter("actor", NpgsqlDbType.Uuid) { Value = actor });
        command.Parameters.Add(new NpgsqlParameter("before", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(before) });
        command.Parameters.Add(new NpgsqlParameter("effects", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(effects) });
        command.Parameters.Add(new NpgsqlParameter("instant", NpgsqlDbType.TimestampTz) { Value = instant });
        await command.ExecuteNonQueryAsync();
    }
}
