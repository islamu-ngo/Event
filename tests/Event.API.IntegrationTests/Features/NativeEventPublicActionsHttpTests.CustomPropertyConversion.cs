using System.Text.Json;
using Explore.Application.DTOs.CustomPropertyDefinition;
using Explore.Application.DTOs.EventCustomProperty;
using Explore.Application.Exceptions;
using Explore.Application.Features.EventCustomProperties.Handlers.Commands;
using Explore.Application.Models.Common;
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
    public async Task DefinitionCreateIgnoresForgedIdentityLifecycleAndDefaultOption()
    {
        await using var fixture = await RetainedAdministrativeFixture.CreateAsync();
        var data = fixture.Data;
        using var scope = fixture.CreateScope();
        await fixture.AuthenticateAsync(scope);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        await GrantTemplateAdministrator(db, data.OutsiderId);
        Guid forgedId = Guid.CreateVersion7();
        var input = JsonSerializer.Deserialize<CreateEventCustomPropertyDefinitionDto>($$"""
            {"EventId":"{{data.PublicId}}",
             "Namespace":"tenant.community","Key":"language","DisplayName":"Language",
             "PropertyType":0,"ExposureLevel":0,"IsActive":true,
             "TenantId":"{{forgedId}}","Id":"{{forgedId}}",
             "CreatedBy":"{{forgedId}}","IsDeleted":true,
             "SourceTemplateId":"{{forgedId}}","DefaultOptionId":"{{forgedId}}"}
            """)! with { PropertyType = PropertyType.Text, ExposureLevel = ExposureLevel.OrganizerOnly };
        await scope.ServiceProvider.GetRequiredService<Explore.Application.Contracts.Operations.IHumanCommitScopePreparation>()
            .PrepareAsync(default);
        var handler = ActivatorUtilities.CreateInstance<CreateEventCustomPropertyDefinitionCommandHandler>(scope.ServiceProvider);

        var result = await handler.ExecuteAsync(new() { DefinitionDto = input }, default);

        await Assert.That(result.IsSuccess).IsTrue();
        var saved = await db.EventCustomPropertyDefinitions.AsNoTracking().SingleAsync(row => row.Id == result.Id);
        await Assert.That(saved.TenantId).IsEqualTo(PlatformDefaults.DefaultTenantId);
        await Assert.That(saved.CreatedBy).IsEqualTo(data.OutsiderId);
        await Assert.That(saved.UpdatedBy).IsEqualTo(data.OutsiderId);
        await Assert.That(saved.IsDeleted).IsFalse();
        await Assert.That(saved.SourceTemplateId).IsNull();
        await Assert.That(saved.DefaultOptionId).IsNull();
        await Assert.That(saved.DisplayName).IsEqualTo("Language");
        await Assert.That(saved.Id).IsNotEqualTo(forgedId);
    }

    [Test]
    public async Task DefinitionUpdateClearsDescriptionAndPreservesProvenanceAndOptions()
    {
        await using var fixture = await RetainedAdministrativeFixture.CreateAsync();
        var data = fixture.Data;
        using var scope = fixture.CreateScope();
        await fixture.AuthenticateAsync(scope);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        Guid id = await SeedOwnedDefinition(db, data.PublicId, 1);
        Guid templateId = await SeedDeletionTemplate(db, false);
        var definition = await db.EventCustomPropertyDefinitions.SingleAsync(row => row.Id == id);
        definition.CreatedBy = data.OwnerId;
        definition.CreatedAt = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        definition.SourceTemplateId = templateId;
        definition.SourceTemplateKey = "conference";
        definition.SourceTemplateVersion = 7;
        definition.InstantiatedAt = new DateTimeOffset(2026, 9, 2, 0, 0, 0, TimeSpan.Zero);
        definition.LastSyncedFromTemplateAt = definition.InstantiatedAt;
        definition.PropertyType = PropertyType.Option;
        definition.Description = "previous";
        var option = new EventCustomPropertyOption
        {
            Id = Guid.CreateVersion7(),
            EventCustomPropertyDefinitionId = id,
            Namespace = "tenant.community",
            Key = "ar",
            DisplayName = "Arabic",
            Value = "ar",
            IsActive = true,
            IsDefault = true
        };
        db.EventCustomPropertyOptions.Add(option);
        await db.SaveChangesAsync();
        definition.DefaultOptionId = option.Id;
        await db.SaveChangesAsync();
        await GrantTemplateAdministrator(db, data.OutsiderId);
        Guid expectedStamp = definition.ConcurrencyStamp;
        Guid forgedId = Guid.CreateVersion7();
        var patch = JsonSerializer.Deserialize<UpdateEventCustomPropertyDefinitionDto>($$"""
            {"Metadata":{"DisplayName":"Updated"},"TenantId":"{{forgedId}}",
             "ConcurrencyStamp":"{{forgedId}}","IsDeleted":true}
            """)! with
        {
            Metadata = new UpdateCustomPropertyDefinitionMetadataDto
            {
                DisplayName = "Updated",
                Description = OptionalUpdate<string?>.Set(null)
            }
        };
        await scope.ServiceProvider.GetRequiredService<Explore.Application.Contracts.Operations.IHumanCommitScopePreparation>()
            .PrepareAsync(default);
        var handler = ActivatorUtilities.CreateInstance<UpdateEventCustomPropertyDefinitionCommandHandler>(scope.ServiceProvider);

        var result = await handler.ExecuteAsync(new()
        {
            DefinitionId = id,
            ExpectedConcurrencyStamp = expectedStamp,
            TenantId = forgedId,
            DefinitionDto = patch
        }, default);

        await Assert.That(result.IsSuccess).IsTrue();
        var saved = await db.EventCustomPropertyDefinitions.AsNoTracking()
            .Include(row => row.Options).SingleAsync(row => row.Id == id);
        await Assert.That(saved.DisplayName).IsEqualTo("Updated");
        await Assert.That(saved.Description).IsNull();
        await Assert.That(saved.DefaultTextValue).IsNull();
        await Assert.That(saved.Id).IsEqualTo(id);
        await Assert.That(saved.ConcurrencyStamp).IsNotEqualTo(expectedStamp);
        await Assert.That(saved.ConcurrencyStamp).IsNotEqualTo(forgedId);
        await Assert.That(saved.TenantId).IsEqualTo(PlatformDefaults.DefaultTenantId);
        await Assert.That(saved.EventId).IsEqualTo(data.PublicId);
        await Assert.That(saved.CreatedBy).IsEqualTo(data.OwnerId);
        await Assert.That(saved.CreatedAt).IsEqualTo(new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc));
        await Assert.That(saved.SourceTemplateId).IsEqualTo(templateId);
        await Assert.That(saved.SourceTemplateKey).IsEqualTo("conference");
        await Assert.That(saved.SourceTemplateVersion).IsEqualTo(7);
        await Assert.That(saved.LastSyncedFromTemplateAt).IsEqualTo(new DateTimeOffset(2026, 9, 2, 0, 0, 0, TimeSpan.Zero));
        await Assert.That(saved.DefaultOptionId).IsEqualTo(option.Id);
        await Assert.That(saved.Options.Single().Id).IsEqualTo(option.Id);
        await Assert.That(saved.IsDeleted).IsFalse();
        await Assert.That(saved.UpdatedBy).IsEqualTo(data.OutsiderId);
    }

    [Test]
    public async Task DefinitionUpdateWithStaleStampPreservesStoredState()
    {
        await using var fixture = await RetainedAdministrativeFixture.CreateAsync();
        var data = fixture.Data;
        using var scope = fixture.CreateScope();
        await fixture.AuthenticateAsync(scope);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        Guid id = await SeedOwnedDefinition(db, data.PublicId, 1);
        await GrantTemplateAdministrator(db, data.OutsiderId);
        var before = await DefinitionState(db, 1, id);

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
            UpdateOwnedDefinition(scope.ServiceProvider, 1, id, Guid.CreateVersion7()));

        await Assert.That(await DefinitionState(db, 1, id)).IsEqualTo(before);
    }

    [Test]
    [Arguments(PropertyType.Text)]
    [Arguments(PropertyType.Number)]
    [Arguments(PropertyType.Boolean)]
    [Arguments(PropertyType.DateTime)]
    public async Task DefinitionValueRoundTripsTypedPayloadWithTrustedAudit(PropertyType propertyType)
    {
        await using var fixture = await RetainedAdministrativeFixture.CreateAsync();
        var data = fixture.Data;
        using var scope = fixture.CreateScope();
        await fixture.AuthenticateAsync(scope);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        Guid id = await SeedOwnedDefinition(db, data.PublicId, 1);
        var definition = await db.EventCustomPropertyDefinitions.SingleAsync(row => row.Id == id);
        definition.PropertyType = propertyType;
        await db.SaveChangesAsync();
        await GrantTemplateAdministrator(db, data.OutsiderId);
        var input = new SetEventCustomPropertyValueDto
        {
            EventCustomPropertyDefinitionId = id,
            EventId = data.PublicId,
            TextValue = propertyType == PropertyType.Text ? "Arabic" : null,
            NumberValue = propertyType == PropertyType.Number ? 0m : null,
            BooleanValue = propertyType == PropertyType.Boolean ? false : null,
            DateTimeValue = propertyType == PropertyType.DateTime
                ? new DateTimeOffset(2026, 9, 11, 0, 0, 0, TimeSpan.Zero) : null
        };
        await scope.ServiceProvider.GetRequiredService<Explore.Application.Contracts.Operations.IHumanCommitScopePreparation>()
            .PrepareAsync(default);
        var handler = ActivatorUtilities.CreateInstance<SetEventCustomPropertyValueCommandHandler>(scope.ServiceProvider);

        var result = await handler.ExecuteAsync(new() { ValueDto = input }, default);

        await Assert.That(result.IsSuccess).IsTrue();
        var saved = await db.EventCustomPropertyValues.AsNoTracking()
            .SingleAsync(row => row.EventCustomPropertyDefinitionId == id);
        await Assert.That(saved.TenantId).IsEqualTo(PlatformDefaults.DefaultTenantId);
        await Assert.That(saved.CreatedBy).IsEqualTo(data.OutsiderId);
        await Assert.That(saved.IsDeleted).IsFalse();
        await Assert.That(saved.TextValue).IsEqualTo(input.TextValue);
        await Assert.That(saved.NumberValue).IsEqualTo(input.NumberValue);
        await Assert.That(saved.BooleanValue).IsEqualTo(input.BooleanValue);
        await Assert.That(saved.DateTimeValue).IsEqualTo(input.DateTimeValue);
        await Assert.That(saved.OptionId).IsNull();
    }

    [Test]
    public async Task DefinitionMultiValuesUseCommandScopeAndSequence()
    {
        await using var fixture = await RetainedAdministrativeFixture.CreateAsync();
        var data = fixture.Data;
        using var scope = fixture.CreateScope();
        await fixture.AuthenticateAsync(scope);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        Guid id = await SeedOwnedDefinition(db, data.PublicId, 1);
        var definition = await db.EventCustomPropertyDefinitions.SingleAsync(row => row.Id == id);
        definition.IsMulti = true;
        await db.SaveChangesAsync();
        await GrantTemplateAdministrator(db, data.OutsiderId);
        await scope.ServiceProvider.GetRequiredService<Explore.Application.Contracts.Operations.IHumanCommitScopePreparation>()
            .PrepareAsync(default);
        var handler = ActivatorUtilities.CreateInstance<SetEventCustomPropertyMultiValuesCommandHandler>(scope.ServiceProvider);

        var result = await handler.ExecuteAsync(new()
        {
            DefinitionId = id,
            EventId = data.PublicId,
            Values =
            [
                new SetEventCustomPropertyValueDto
                {
                    EventCustomPropertyDefinitionId = data.OutsiderId, EventId = data.OtherId,
                    Ordinal = 9, TextValue = "Arabic"
                },
                new SetEventCustomPropertyValueDto
                {
                    EventCustomPropertyDefinitionId = data.OutsiderId, EventId = data.OtherId,
                    Ordinal = 8, TextValue = "English"
                }
            ]
        }, default);

        await Assert.That(result.IsSuccess).IsTrue();
        var saved = await db.EventCustomPropertyValues.AsNoTracking()
            .Where(row => row.EventCustomPropertyDefinitionId == id).OrderBy(row => row.Ordinal).ToListAsync();
        await Assert.That(saved.Select(value => value.TextValue).SequenceEqual(["Arabic", "English"])).IsTrue();
        await Assert.That(saved.Select(value => value.Ordinal).SequenceEqual([0, 1])).IsTrue();
        await Assert.That(saved.All(value => value.EventCustomPropertyDefinitionId == id
            && value.EventId == data.PublicId && value.TenantId == PlatformDefaults.DefaultTenantId
            && value.CreatedBy == data.OutsiderId)).IsTrue();
    }
}
