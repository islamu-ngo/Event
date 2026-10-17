using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Settings.Requests.Commands;
using Explore.Domain;
using Explore.Domain.Settings;
using Explore.Domain.Settings.Definitions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Event.Api.IntegrationTests.Authentication;

[NotInParallel]
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
public sealed class OrdinarySettingsNativeAuthenticationTests(PostgreSqlContainerFixture postgres)
{
    [Test]
    [Arguments("committed")]
    [Arguments("omitted")]
    [Arguments("expired-after-completion")]
    public async Task ActualHumanOwnerRequiresCanonicalNativeWriterAndOriginalDeadline(string outcome)
    {
        await using var fixture = await ResolverConfigurationNativeFixture.CreateAsync(postgres);
        string key = EventSettingDefinitions.CardClickOpensDetailPage.Key;
        await using (var seed = postgres.CreateDbContext())
            await seed.SystemSettings.Where(row => row.SettingKey == key).ExecuteDeleteAsync();
        await fixture.InRequestAsync(async () =>
        {
            var preparation = fixture.Services.GetRequiredService<IHumanSettingsMutationPreparation>();
            var command = new UpdateSettingCommand { Key = key, Value = "false", Scope = SettingScope.Instance };
            var proof = await preparation.PrepareAsync(command, "false");
            Task<string?> InvokeAsync() => preparation.ExecutePreparedAsync(proof, async (_, token) =>
            {
                if (outcome == "omitted")
                    return "false";
                var actual = await fixture.Services.GetRequiredService<ISystemSettingRepository>().UpsertAsync(new SystemSetting
                {
                    SettingKey = key,
                    Value = "false",
                    ValueType = SettingValueType.Boolean,
                    UpdatedBy = fixture.Actor
                }, token);
                if (outcome == "expired-after-completion")
                    fixture.Services.GetRequiredService<IHumanActionCommitFinalizer>().Register(_ =>
                    {
                        fixture.Clock.Set(fixture.Lifetime.ExpiresAt.Add(fixture.Lifetime.ClockSkew).AddTicks(1));
                        return Task.CompletedTask;
                    }, _ => { });
                return actual;
            });
            if (outcome == "committed")
                await Assert.That(await InvokeAsync()).IsNull();
            else if (outcome == "omitted")
            {
                var rejected = await Assert.ThrowsAsync<PostgresException>(() => InvokeAsync());
                await Assert.That(rejected!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
            }
            else
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => InvokeAsync());
            await Assert.That(fixture.Owner.Database.CurrentTransaction).IsNull();
        });
        await using var independent = postgres.CreateDbContext();
        var persisted = await independent.SystemSettings.AsNoTracking().SingleOrDefaultAsync(row => row.SettingKey == key);
        if (outcome != "committed")
            await Assert.That(persisted).IsNull();
        else
        {
            await Assert.That(persisted).IsNotNull();
            await Assert.That(persisted!.Value).IsEqualTo("false");
            await Assert.That(persisted.CreatedBy).IsEqualTo(fixture.Actor);
            await Assert.That(persisted.UpdatedBy).IsNull();
            await Assert.That(persisted.Category).IsEqualTo(EventSettingDefinitions.CardClickOpensDetailPage.Category);
            await Assert.That(persisted.Id.ToString("D")[14]).IsEqualTo('7');
        }
    }
}
