using System.Globalization;
using Explore.Application.Features.EventCustomProperties.Handlers.Commands;
using Explore.Application.Features.EventSessionCustomProperties.Handlers.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Settings.Definitions;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

public sealed partial class NativeEventPublicActionsHttpTests
{
    [Test]
    [Arguments(false, 2)]
    [Arguments(false, 3)]
    [Arguments(false, 4)]
    [Arguments(true, 1)]
    [Arguments(true, 2)]
    [Arguments(true, 3)]
    public async Task DefinitionCreationEnforcesPersistedQuotaAtEveryBoundary(bool session, int count)
    {
        await using var fixture = await RetainedAdministrativeFixture.CreateAsync();
        var data = fixture.Data;
        using var scope = fixture.CreateScope();
        await fixture.AuthenticateAsync(scope);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        await GrantTemplateAdministrator(db, data.OutsiderId);
        Guid tenantId = PlatformDefaults.DefaultTenantId;
        int limit = session ? 2 : 3;
        string quotaKey = session
            ? CustomPropertyQuotaSettingDefinitions.MaxDefinitionsPerEventSession.Key
            : CustomPropertyQuotaSettingDefinitions.MaxDefinitionsPerEvent.Key;
        var setting = await db.Set<TenantSetting>()
            .SingleOrDefaultAsync(row => row.TenantId == tenantId && row.SettingKey == quotaKey);
        if (setting is null)
        {
            db.Set<TenantSetting>().Add(new TenantSetting
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenantId,
                Tenant = null!,
                SettingKey = quotaKey,
                Value = limit.ToString(CultureInfo.InvariantCulture)
            });
        }
        else
            setting.Value = limit.ToString(CultureInfo.InvariantCulture);

        Guid parentId = data.PublicId;
        if (session)
        {
            parentId = Guid.CreateVersion7();
            db.EventSessions.Add(new EventSession
            {
                Id = parentId,
                EventId = data.PublicId,
                Event = null!,
                TenantId = tenantId,
                Tenant = null!,
                Title = "Quota boundary"
            });
        }
        for (int index = 0; index < count; index++)
        {
            if (session)
                db.EventSessionCustomPropertyDefinitions.Add(new EventSessionCustomPropertyDefinition
                {
                    Id = Guid.CreateVersion7(),
                    EventSessionId = parentId,
                    TenantId = tenantId,
                    Namespace = "tenant.community",
                    Key = $"existing_{index}",
                    DisplayName = $"Existing {index}",
                    PropertyType = PropertyType.Text,
                    ExposureLevel = ExposureLevel.OrganizerOnly,
                    IsActive = true
                });
            else
                db.EventCustomPropertyDefinitions.Add(new EventCustomPropertyDefinition
                {
                    Id = Guid.CreateVersion7(),
                    EventId = parentId,
                    TenantId = tenantId,
                    Namespace = "tenant.community",
                    Key = $"existing_{index}",
                    DisplayName = $"Existing {index}",
                    PropertyType = PropertyType.Text,
                    ExposureLevel = ExposureLevel.OrganizerOnly,
                    IsActive = true
                });
        }
        await db.SaveChangesAsync();

        await scope.ServiceProvider.GetRequiredService<Explore.Application.Contracts.Operations.IHumanCommitScopePreparation>()
            .PrepareAsync(default);
        var result = session
            ? await ActivatorUtilities.CreateInstance<CreateEventSessionCustomPropertyDefinitionCommandHandler>(scope.ServiceProvider)
                .ExecuteAsync(new()
                {
                    DefinitionDto = new()
                    {
                        EventSessionId = parentId,
                        Namespace = "tenant.community",
                        Key = "prayer_notes",
                        DisplayName = "Prayer Notes",
                        PropertyType = PropertyType.Text,
                        ExposureLevel = ExposureLevel.OrganizerOnly,
                        IsActive = true
                    }
                }, default)
            : await ActivatorUtilities.CreateInstance<CreateEventCustomPropertyDefinitionCommandHandler>(scope.ServiceProvider)
                .ExecuteAsync(new()
                {
                    DefinitionDto = new()
                    {
                        EventId = parentId,
                        Namespace = "tenant.community",
                        Key = "prayer_notes",
                        DisplayName = "Prayer Notes",
                        PropertyType = PropertyType.Text,
                        ExposureLevel = ExposureLevel.OrganizerOnly,
                        IsActive = true
                    }
                }, default);

        int storedCount = session
            ? await db.EventSessionCustomPropertyDefinitions.CountAsync(row => row.EventSessionId == parentId)
            : await db.EventCustomPropertyDefinitions.CountAsync(row => row.EventId == parentId);
        if (count < limit)
        {
            await Assert.That(result.IsSuccess).IsTrue();
            await Assert.That(result.FailureCode).IsNotEqualTo(FailureCodes.QuotaExceeded);
            await Assert.That(result.QuotaExceeded).IsNull();
            await Assert.That(storedCount).IsEqualTo(count + 1);
        }
        else
        {
            await Assert.That(result.IsSuccess).IsFalse();
            await Assert.That(result.FailureCode).IsEqualTo(FailureCodes.QuotaExceeded);
            await Assert.That(result.QuotaExceeded).IsNotNull();
            await Assert.That(result.QuotaExceeded!.QuotaKey).IsEqualTo(quotaKey);
            await Assert.That(result.QuotaExceeded.Limit).IsEqualTo(limit);
            await Assert.That(result.QuotaExceeded.Actual).IsEqualTo(count);
            await Assert.That(result.QuotaExceeded.Attempted).IsEqualTo(count + 1);
            await Assert.That(result.QuotaExceeded.Scope).IsEqualTo(
                session ? "event_session_custom_property_definitions" : "event_custom_property_definitions");
            await Assert.That(result.QuotaExceeded.TenantId).IsEqualTo(tenantId);
            await Assert.That(result.Errors!.Single()).Contains(FailureCodes.QuotaExceeded);
            await Assert.That(storedCount).IsEqualTo(count);
        }
    }
}
