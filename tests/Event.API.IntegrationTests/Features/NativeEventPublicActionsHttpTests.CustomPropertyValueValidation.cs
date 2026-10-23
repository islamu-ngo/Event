using Explore.Application.DTOs.EventSessionCustomProperty;
using Explore.Application.Features.EventSessionCustomProperties.Handlers.Commands;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

public sealed partial class NativeEventPublicActionsHttpTests
{
    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task SessionValueRejectsInvalidOrdinalDuplicateOrInactiveOption(int scenario)
    {
        await using var fixture = await RetainedAdministrativeFixture.CreateAsync();
        var data = fixture.Data;
        using var scope = fixture.CreateScope();
        await fixture.AuthenticateAsync(scope);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        Guid id = await SeedOwnedDefinition(db, data.PublicId, 2);
        var definition = await db.EventSessionCustomPropertyDefinitions.SingleAsync(row => row.Id == id);
        definition.IsMulti = scenario == 1;
        definition.PropertyType = scenario == 2 ? PropertyType.Option : PropertyType.Text;
        EventSessionCustomPropertyOption? option = null;
        if (scenario == 1)
            db.EventSessionCustomPropertyValues.Add(new EventSessionCustomPropertyValue
            {
                Id = Guid.CreateVersion7(),
                TenantId = PlatformDefaults.DefaultTenantId,
                EventSessionId = definition.EventSessionId,
                EventSessionCustomPropertyDefinitionId = id,
                Ordinal = 0,
                TextValue = "Alpha"
            });
        if (scenario == 2)
        {
            option = new EventSessionCustomPropertyOption
            {
                Id = Guid.CreateVersion7(),
                EventSessionCustomPropertyDefinitionId = id,
                Namespace = "tenant.session",
                Key = "arabic",
                DisplayName = "Arabic",
                Value = "arabic",
                IsActive = false
            };
            db.EventSessionCustomPropertyOptions.Add(option);
        }
        await db.SaveChangesAsync();
        await GrantTemplateAdministrator(db, data.OutsiderId);
        var input = new SetEventSessionCustomPropertyValueDto
        {
            EventSessionCustomPropertyDefinitionId = id,
            EventSessionId = definition.EventSessionId,
            Ordinal = scenario == 2 ? 0 : 1,
            TextValue = scenario switch { 0 => "Arabic", 1 => " alpha ", _ => null },
            OptionId = option?.Id
        };
        await scope.ServiceProvider.GetRequiredService<Explore.Application.Contracts.Operations.IHumanCommitScopePreparation>()
            .PrepareAsync(default);
        var handler = ActivatorUtilities.CreateInstance<SetEventSessionCustomPropertyValueCommandHandler>(scope.ServiceProvider);

        var rejected = await handler.ExecuteAsync(new() { ValueDto = input }, default);

        await Assert.That(rejected.IsSuccess).IsFalse();
        await Assert.That(rejected.FailureCode).IsNull();
        await Assert.That(rejected.Errors).IsNotNull();
        await Assert.That(await db.EventSessionCustomPropertyValues.CountAsync(
            row => row.EventSessionCustomPropertyDefinitionId == id)).IsEqualTo(scenario == 1 ? 1 : 0);

        if (option is not null)
        {
            option.IsActive = true;
            await db.SaveChangesAsync();
        }
        var valid = scenario switch
        {
            0 => input with { Ordinal = 0 },
            1 => input with { TextValue = "Beta" },
            _ => input
        };

        var accepted = await handler.ExecuteAsync(new() { ValueDto = valid }, default);

        await Assert.That(accepted.IsSuccess).IsTrue();
        var stored = await db.EventSessionCustomPropertyValues.AsNoTracking()
            .SingleAsync(row => row.EventSessionCustomPropertyDefinitionId == id && row.Ordinal == valid.Ordinal);
        await Assert.That(stored.TextValue).IsEqualTo(valid.TextValue);
        await Assert.That(stored.OptionId).IsEqualTo(valid.OptionId);
    }
}
