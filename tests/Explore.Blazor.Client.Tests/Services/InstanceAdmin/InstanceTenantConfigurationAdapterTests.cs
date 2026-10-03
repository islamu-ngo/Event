using Explore.Blazor.Client.Contracts.InstanceAdmin;
using Explore.Blazor.Client.Contracts.Services.InstanceAdmin;
using Explore.Blazor.Client.Extensions;
using Explore.Blazor.Client.Services.InstanceAdmin;

namespace Explore.Blazor.Client.Tests.Services.InstanceAdmin;

public sealed class InstanceTenantConfigurationAdapterTests
{
    [Test]
    public async Task GetEffectiveConfigurationAsync_MapsAssignmentSettingsQuotasAndHalLinks()
    {
        var tenantId = Guid.NewGuid();
        var assignmentId = Guid.NewGuid();
        var assignedAt = new DateTimeOffset(2026, 7, 11, 8, 30, 0, TimeSpan.Zero);
        var recalculatedAt = assignedAt.AddMinutes(15);
        var apiClient = Substitute.For<IInstanceTenantConfigurationClient>();
        var setting = new InstanceTenantEffectiveSettingDto
        {
            Key = "storage.max-bytes",
            Category = "Storage",
            Value = "1048576",
            SettingValueTypeId = 4,
            SettingValueTypeCode = "long",
            SettingValueTypeName = "Whole number",
            ValueSource = "TenantOverride",
            IsLocked = false,
            Description = "Maximum tenant storage.",
            IsSensitive = false,
            AllowedValues = ["1048576", "2097152"]
        };
        GeneratedHalLinkTestHelper.SetLinks(
            setting,
            (InstanceAdminLinkRelations.Override, $"/api/admin/instance/tenants/{tenantId}/settings/storage.max-bytes", "PUT"),
            (InstanceAdminLinkRelations.Lock, $"/api/admin/instance/tenants/{tenantId}/settings/storage.max-bytes/lock", "POST"));
        var response = new HalResourceOfInstanceTenantEffectiveConfigurationDto
        {
            TenantId = tenantId,
            PlanAssignment = new InstanceTenantPlanAssignmentDto
            {
                Id = assignmentId,
                TenantId = tenantId,
                PlanId = Guid.NewGuid(),
                PlanKey = "community",
                PlanVersionId = Guid.NewGuid(),
                VersionNumber = 3,
                StatusId = 2,
                StatusCode = "applied",
                AssignedAt = assignedAt,
                AssignedByUserId = Guid.NewGuid()
            },
            Settings = [setting],
            Quotas =
            [
                new InstanceTenantQuotaUsageDto
                {
                    Key = "storage.bytes",
                    Limit = 1000,
                    Used = 650,
                    Reserved = 100,
                    Quarantined = 25,
                    Available = 225,
                    ObjectCount = 42,
                    Provider = "s3",
                    Source = "plan",
                    LastRecalculatedAt = recalculatedAt
                }
            ]
        };
        GeneratedHalLinkTestHelper.SetLinks(
            response,
            (InstanceAdminLinkRelations.Self, $"/api/admin/instance/tenants/{tenantId}/effective-configuration", "GET"),
            (InstanceAdminLinkRelations.SwitchPlan, $"/api/admin/instance/tenants/{tenantId}/plan-assignment", "POST"));
        apiClient.GetInstanceAdminTenantEffectiveConfigurationAsync(tenantId, null, null, Arg.Any<CancellationToken>())
            .Returns(response);
        var adapter = CreateAdapter(apiClient);

        var result = await adapter.GetEffectiveConfigurationAsync(tenantId);

        await Assert.That(result.TenantId).IsEqualTo(tenantId);
        await Assert.That(result._links![InstanceAdminLinkRelations.Self].Method).IsEqualTo("GET");
        await Assert.That(result._links.ContainsKey(InstanceAdminLinkRelations.SwitchPlan)).IsTrue();

        var assignment = result.PlanAssignment!;
        await Assert.That(assignment.Id).IsEqualTo(assignmentId);
        await Assert.That(assignment.PlanKey).IsEqualTo("community");
        await Assert.That(assignment.VersionNumber).IsEqualTo(3);
        await Assert.That(assignment.AssignedAt).IsEqualTo(assignedAt);

        var mappedSetting = result.Settings!.Single();
        await Assert.That(mappedSetting.Key).IsEqualTo("storage.max-bytes");
        await Assert.That(mappedSetting.Category).IsEqualTo("Storage");
        await Assert.That(mappedSetting.Value).IsEqualTo("1048576");
        await Assert.That(mappedSetting.SettingValueTypeId).IsEqualTo(4);
        await Assert.That(mappedSetting.SettingValueTypeCode).IsEqualTo("long");
        await Assert.That(mappedSetting.SettingValueTypeName).IsEqualTo("Whole number");
        await Assert.That(mappedSetting.ValueSource).IsEqualTo("TenantOverride");
        await Assert.That(mappedSetting.Description).IsEqualTo("Maximum tenant storage.");
        await Assert.That(mappedSetting.AllowedValues).IsEquivalentTo(new[] { "1048576", "2097152" });
        await Assert.That(mappedSetting._links![InstanceAdminLinkRelations.Override].Method).IsEqualTo("PUT");
        await Assert.That(mappedSetting._links.ContainsKey(InstanceAdminLinkRelations.Lock)).IsTrue();

        var quota = result.Quotas!.Single();
        await Assert.That(quota.Key).IsEqualTo("storage.bytes");
        await Assert.That(quota.Limit).IsEqualTo(1000);
        await Assert.That(quota.Used).IsEqualTo(650);
        await Assert.That(quota.Reserved).IsEqualTo(100);
        await Assert.That(quota.Quarantined).IsEqualTo(25);
        await Assert.That(quota.Available).IsEqualTo(225);
        await Assert.That(quota.ObjectCount).IsEqualTo(42);
        await Assert.That(quota.Provider).IsEqualTo("s3");
        await Assert.That(quota.Source).IsEqualTo("plan");
        await Assert.That(quota.LastRecalculatedAt).IsEqualTo(recalculatedAt);
    }

    [Test]
    public async Task SetSettingAsync_UsesGeneratedOverrideOperationAndMapsSuccess()
    {
        var tenantId = Guid.NewGuid();
        var apiClient = Substitute.For<IInstanceTenantConfigurationClient>();
        apiClient.SetInstanceAdminTenantSettingAsync(
                tenantId,
                "registration.capacity",
                Arg.Is<SetInstanceTenantSettingRequest>(request => request.Value == "250"),
                null,
                null,
                Arg.Any<CancellationToken>())
            .Returns(new BaseCommandResponseOfGuid { Success = true, Message = "Setting overridden." });
        var adapter = CreateAdapter(apiClient);

        var result = await adapter.SetSettingAsync(tenantId, "registration.capacity", "250");

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Message).IsEqualTo("Setting overridden.");
    }

    [Test]
    public async Task StringValue_RoundTripsThroughAdapterWithoutStorageQuotes()
    {
        Guid tenantId = Guid.NewGuid();
        var apiClient = Substitute.For<IInstanceTenantConfigurationClient>();
        apiClient.GetInstanceAdminTenantEffectiveConfigurationAsync(
                tenantId,
                null,
                null,
                Arg.Any<CancellationToken>())
            .Returns(new HalResourceOfInstanceTenantEffectiveConfigurationDto
            {
                TenantId = tenantId,
                Settings =
                [
                    new InstanceTenantEffectiveSettingDto
                    {
                        Key = "email.smtp_host",
                        Category = "Email",
                        Value = "smtp.example.test",
                        SettingValueTypeId = 1,
                        SettingValueTypeCode = "string",
                        SettingValueTypeName = "String",
                        ValueSource = "TenantOverride"
                    }
                ]
            });
        apiClient.SetInstanceAdminTenantSettingAsync(
                tenantId,
                "email.smtp_host",
                Arg.Is<SetInstanceTenantSettingRequest>(request => request.Value == "smtp.example.test"),
                null,
                null,
                Arg.Any<CancellationToken>())
            .Returns(new BaseCommandResponseOfGuid { Success = true });
        var adapter = CreateAdapter(apiClient);

        var configuration = await adapter.GetEffectiveConfigurationAsync(tenantId);
        InstanceTenantEffectiveSettingDto setting = configuration.Settings!.Single();
        var result = await adapter.SetSettingAsync(tenantId, setting.Key, setting.Value);

        await Assert.That(setting.Value).IsEqualTo("smtp.example.test");
        await Assert.That(result.Success).IsTrue();
        await apiClient.Received(1).SetInstanceAdminTenantSettingAsync(
            tenantId,
            "email.smtp_host",
            Arg.Is<SetInstanceTenantSettingRequest>(request => request.Value == "smtp.example.test"),
            null,
            null,
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task LockSettingAsync_UsesGeneratedLockOperationAndMapsFailure()
    {
        var tenantId = Guid.NewGuid();
        var apiClient = Substitute.For<IInstanceTenantConfigurationClient>();
        apiClient.LockInstanceAdminTenantSettingAsync(
                tenantId,
                "registration.capacity",
                null,
                null,
                Arg.Any<CancellationToken>())
            .Returns(new BaseCommandResponseOfGuid
            {
                Success = false,
                Message = "Setting cannot be locked.",
                FailureCode = "setting_not_overridden",
                Errors = ["Override the setting first."]
            });
        var adapter = CreateAdapter(apiClient);

        var result = await adapter.LockSettingAsync(tenantId, "registration.capacity");

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("setting_not_overridden");
        await Assert.That(result.Errors).IsEquivalentTo(["Override the setting first."]);
    }

    [Test]
    public async Task UnlockSettingAsync_UsesGeneratedUnlockOperationAndMapsSuccess()
    {
        var tenantId = Guid.NewGuid();
        var apiClient = Substitute.For<IInstanceTenantConfigurationClient>();
        apiClient.UnlockInstanceAdminTenantSettingAsync(
                tenantId,
                "registration.capacity",
                null,
                null,
                Arg.Any<CancellationToken>())
            .Returns(new BaseCommandResponseOfGuid { Success = true, Message = "Setting unlocked." });
        var adapter = CreateAdapter(apiClient);

        var result = await adapter.UnlockSettingAsync(tenantId, "registration.capacity");

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Message).IsEqualTo("Setting unlocked.");
    }

    [Test]
    public async Task GetEffectiveConfigurationAsync_WhenApiReturnsForbidden_PropagatesGeneratedApiException()
    {
        var apiClient = Substitute.For<IInstanceTenantConfigurationClient>();
        apiClient.GetInstanceAdminTenantEffectiveConfigurationAsync(Arg.Any<Guid>(), null, null, Arg.Any<CancellationToken>())
            .Returns<Task<HalResourceOfInstanceTenantEffectiveConfigurationDto>>(_ => throw new ApiException(
                "Forbidden",
                403,
                "raw secret response",
                new Dictionary<string, IEnumerable<string>>(),
                null));
        var adapter = CreateAdapter(apiClient);

        await Assert.That(() => adapter.GetEffectiveConfigurationAsync(Guid.NewGuid()))
            .ThrowsExactly<ApiException>();
    }

    [Test]
    public async Task SetSettingAsync_WhenApiThrowsUnexpectedException_PropagatesException()
    {
        var apiClient = Substitute.For<IInstanceTenantConfigurationClient>();
        apiClient.SetInstanceAdminTenantSettingAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<SetInstanceTenantSettingRequest>(),
                null,
                null,
                Arg.Any<CancellationToken>())
            .Returns<Task<BaseCommandResponseOfGuid>>(_ => throw new InvalidOperationException("database-password"));
        var adapter = CreateAdapter(apiClient);

        await Assert.That(() => adapter.SetSettingAsync(Guid.NewGuid(), "registration.capacity", "250"))
            .ThrowsExactly<InvalidOperationException>();
    }

    [Test]
    public async Task AddSharedApplicationServices_RegistersConfigurationInterfaceToScopedAdapter()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<IInstanceAdminClient>());
        services.AddSingleton(Substitute.For<IInstanceDeploymentModeClient>());
        services.AddSingleton(Substitute.For<IInstanceTenantConfigurationClient>());
        services.AddSingleton(Substitute.For<IInstanceTenantLifecycleClient>());
        services.AddSingleton(Substitute.For<IInstanceTenantPlanClient>());
        services.AddSharedApplicationServices();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var service = scope.ServiceProvider.GetRequiredService<IInstanceTenantConfigurationService>();
        var adapter = scope.ServiceProvider.GetRequiredService<InstanceAdminApiAdapter>();

        await Assert.That(service).IsSameReferenceAs(adapter);
    }

    private static InstanceAdminApiAdapter CreateAdapter(IInstanceTenantConfigurationClient apiClient) =>
        new(
            Substitute.For<IInstanceAdminClient>(),
            Substitute.For<IInstanceDeploymentModeClient>(),
            apiClient,
            Substitute.For<IInstanceTenantLifecycleClient>(),
            Substitute.For<IInstanceTenantPlanClient>());
}
