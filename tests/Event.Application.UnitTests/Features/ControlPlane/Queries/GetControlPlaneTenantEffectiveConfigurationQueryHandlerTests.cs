using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.ControlPlane;
using Explore.Application.DTOs.Tenant;
using Explore.Application.Features.ControlPlane.Handlers.Queries;
using Explore.Application.Features.ControlPlane.Requests.Queries;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Settings;
using NSubstitute;

namespace Event.Application.UnitTests.Features.ControlPlane.Queries;

public sealed class GetControlPlaneTenantEffectiveConfigurationQueryHandlerTests
{
    [Test]
    public async Task Handle_NormalizesStorageValuesForApiDisplayAndFallsBackSafely()
    {
        Guid tenantId = Guid.NewGuid();
        var resolver = Substitute.For<IHierarchicalSettingsResolver>();
        var plans = Substitute.For<ITenantPlanRepository>();
        var storage = Substitute.For<ITenantStorageSettingService>();
        var rawValues = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [GovernanceSettingKeys.Email.SmtpHost] = "\"smtp.example.test\"",
            [GovernanceSettingKeys.Email.SmtpPort] = "2525",
            [GovernanceSettingKeys.Email.SmtpSkipCertValidation] = "true",
            [GovernanceSettingKeys.PublicExperience.HomeBlocks] = "{\"schemaVersion\":1,\"blocks\":[{\"type\":\"hero\"}]}",
            [GovernanceSettingKeys.Email.FromName] = "\"unterminated",
            ["email.smtp_password"] = "\"must-not-leak\""
        };
        resolver.ResolveBatchAsync(
                Arg.Any<IEnumerable<string>>(),
                Arg.Any<SettingContext>(),
                Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<IEnumerable<string>>(0)
                .Select(key => Resolve(key, rawValues))
                .ToArray());
        plans.GetActiveAssignmentForTenantAsync(tenantId, Arg.Any<CancellationToken>())
            .Returns((TenantPlanAssignment?)null);
        storage.ReadSettingsAsync(tenantId, Arg.Any<CancellationToken>())
            .Returns(new TenantStorageSettingsDto { TenantId = tenantId });
        var handler = new GetControlPlaneTenantEffectiveConfigurationQueryHandler(resolver, plans, storage);

        ControlPlaneTenantEffectiveConfigurationDto result = await handler.Handle(
            new GetControlPlaneTenantEffectiveConfigurationQuery(tenantId),
            CancellationToken.None);

        await Assert.That(Value(result, GovernanceSettingKeys.Email.SmtpHost)).IsEqualTo("smtp.example.test");
        await Assert.That(Value(result, GovernanceSettingKeys.Email.SmtpPort)).IsEqualTo("2525");
        await Assert.That(Value(result, GovernanceSettingKeys.Email.SmtpSkipCertValidation)).IsEqualTo("true");
        await Assert.That(Value(result, GovernanceSettingKeys.PublicExperience.HomeBlocks))
            .IsEqualTo("{\"schemaVersion\":1,\"blocks\":[{\"type\":\"hero\"}]}");
        await Assert.That(Value(result, GovernanceSettingKeys.Email.FromName)).IsEqualTo("Explore");
        await Assert.That(result.Settings.Select(setting => setting.Key)).DoesNotContain("email.smtp_password");
    }

    [Test]
    public async Task ToDisplayValue_NormalizesDateTimeAndMalformedTypedValues()
    {
        const string dateTime = "2026-07-11T10:15:30.0000000Z";

        await Assert.That(SettingValueSerializer.ToDisplayValue(
                $"\"{dateTime}\"",
                SettingValueType.DateTime,
                "\"2000-01-01T00:00:00.0000000Z\""))
            .IsEqualTo(dateTime);
        await Assert.That(SettingValueSerializer.ToDisplayValue(
                "not-an-integer",
                SettingValueType.Integer,
                "587"))
            .IsEqualTo("587");
        await Assert.That(SettingValueSerializer.ToDisplayValue(
                "{invalid-json",
                SettingValueType.Json,
                "{\"schemaVersion\":1}"))
            .IsEqualTo("{\"schemaVersion\":1}");
    }

    private static ResolvedSetting Resolve(string key, IReadOnlyDictionary<string, string> rawValues)
    {
        SettingDefinition definition = SettingRegistry.Get(key)
            ?? throw new InvalidOperationException($"Missing setting definition for '{key}'.");
        return new ResolvedSetting
        {
            Key = key,
            Value = rawValues.GetValueOrDefault(key, definition.DefaultValue),
            ValueType = definition.ValueType,
            Source = SettingSource.TenantOverride,
            IsLocked = false
        };
    }

    private static string Value(ControlPlaneTenantEffectiveConfigurationDto result, string key) =>
        result.Settings.Single(setting => setting.Key == key).Value;
}
