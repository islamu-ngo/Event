using System.Security.Claims;
using System.Text.Json;
using Explore.API.Hateoas;
using Explore.API.Hateoas.Assemblers;
using Explore.API.Hateoas.Policies;
using Explore.Application.Authorization;
using Explore.Application.DTOs.ControlPlane;
using Explore.Application.Features.ControlPlane.Requests.Commands;
using Explore.Application.Features.ControlPlane.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Domain.Constants;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Api.IntegrationTests.Features.Hateoas;

public sealed class ControlPlaneTenantEffectiveConfigurationHateoasTests
{
    [Test]
    public async Task DetailLinks_ExposeReadAndAssignmentAuthorizationMetadata()
    {
        var policy = new ControlPlaneTenantEffectiveConfigurationLinkPolicy();
        var configuration = CreateConfiguration();

        var links = policy.GetLinks(configuration, user: null).ToArray();

        var self = links.Single(link => link.Rel == LinkRelations.Self);
        await Assert.That(self.RouteName).IsEqualTo(RouteNames.GetControlPlaneTenantEffectiveConfiguration);
        await Assert.That(self.Method).IsEqualTo("GET");
        await Assert.That(RouteValues(self)["tenantId"]).IsEqualTo(configuration.TenantId);
        await Assert.That(self.RequiresAuth).IsTrue();
        await Assert.That(self.PermissionResourceKind).IsEqualTo(ResourceKinds.InstanceSetting);
        await Assert.That(self.PermissionAction).IsEqualTo(AuthorizationActions.InstanceSettings.View);
        await Assert.That(self.PermissionResourceId).IsEqualTo(GetControlPlaneTenantEffectiveConfigurationQuery.SettingKey);
        // Control-plane configuration is an instance-authority surface. The tenant it targets travels in the
        // route, not in the policy facts, so no tenant identifier is published to the provider.
        await Assert.That(self.PermissionFacts).IsEqualTo(InstanceScopedAuthorizationFacts.Instance);

        var assignment = links.Single(link => link.Rel == "plan-assignment");
        await Assert.That(assignment.RouteName).IsEqualTo(RouteNames.GetControlPlaneTenantPlanAssignment);
        await Assert.That(assignment.Method).IsEqualTo("GET");
        await Assert.That(assignment.PermissionAction).IsEqualTo(AuthorizationActions.InstanceSettings.View);
        await Assert.That(assignment.PermissionResourceId).IsEqualTo(GetControlPlaneTenantPlanAssignmentQuery.SettingKey);
        await Assert.That(RouteValues(assignment)["tenantId"]).IsEqualTo(configuration.TenantId);
        await Assert.That(links.Any(link =>
            link.Rel is LinkRelations.ExportConfigurationOverrides
                or LinkRelations.ExportConfigurationPortable)).IsFalse();
    }

    [Test]
    public async Task DetailLinks_ExposePlanAssignmentMutationAuthorizationMetadata()
    {
        var policy = new ControlPlaneTenantEffectiveConfigurationLinkPolicy();
        var configuration = CreateConfiguration();
        var assignmentId = configuration.PlanAssignment!.Id;
        var rollbackAssignmentId = configuration.RollbackAssignment!.Id;

        var links = policy.GetLinks(configuration, user: null).ToArray();

        var switchPlan = links.Single(link => link.Rel == "switch-plan");
        await Assert.That(switchPlan.RouteName).IsEqualTo(RouteNames.SwitchControlPlaneTenantPlanAssignment);
        await Assert.That(switchPlan.Method).IsEqualTo("POST");
        await Assert.That(RouteValues(switchPlan)["tenantId"]).IsEqualTo(configuration.TenantId);
        await Assert.That(switchPlan.PermissionAction).IsEqualTo(AuthorizationActions.InstanceSettings.Update);
        await Assert.That(switchPlan.PermissionResourceId).IsEqualTo(SwitchControlPlaneTenantPlanAssignmentCommand.SettingKey);
        await Assert.That(switchPlan.PermissionFacts).IsEqualTo(InstanceScopedAuthorizationFacts.Instance);

        var apply = links.Single(link => link.Rel == "apply");
        await Assert.That(apply.RouteName).IsEqualTo(RouteNames.ApplyControlPlaneTenantPlanAssignment);
        await Assert.That(apply.Method).IsEqualTo("POST");
        await Assert.That(RouteValues(apply)["tenantId"]).IsEqualTo(configuration.TenantId);
        await Assert.That(RouteValues(apply)["assignmentId"]).IsEqualTo(assignmentId);
        await Assert.That(apply.PermissionResourceId).IsEqualTo(ApplyControlPlaneTenantPlanAssignmentCommand.SettingKey);
        await Assert.That(apply.PermissionFacts).IsEqualTo(InstanceScopedAuthorizationFacts.Instance);

        var rollback = links.Single(link => link.Rel == "rollback");
        await Assert.That(rollback.RouteName).IsEqualTo(RouteNames.RollbackControlPlaneTenantPlanAssignment);
        await Assert.That(rollback.Method).IsEqualTo("POST");
        await Assert.That(RouteValues(rollback)["tenantId"]).IsEqualTo(configuration.TenantId);
        await Assert.That(RouteValues(rollback)["assignmentId"]).IsEqualTo(rollbackAssignmentId);
        await Assert.That(rollback.PermissionResourceId).IsEqualTo(RollbackControlPlaneTenantPlanAssignmentCommand.SettingKey);
        await Assert.That(rollback.PermissionFacts).IsEqualTo(InstanceScopedAuthorizationFacts.Instance);
    }

    [Test]
    public async Task DetailLinks_OmitApplyAndRollback_WhenNoAssignmentExists()
    {
        var policy = new ControlPlaneTenantEffectiveConfigurationLinkPolicy();
        var configuration = CreateConfiguration();
        configuration = configuration with
        {
            PlanAssignment = null,
            RollbackAssignment = null
        };

        var links = policy.GetLinks(configuration, user: null).ToArray();

        await Assert.That(links.Any(link => link.Rel == "switch-plan")).IsTrue();
        await Assert.That(links.Any(link => link.Rel == "apply")).IsFalse();
        await Assert.That(links.Any(link => link.Rel == "rollback")).IsFalse();
    }

    [Test]
    public async Task DetailLinks_OmitRollback_WhenNoEligiblePreviousAssignmentExists()
    {
        var policy = new ControlPlaneTenantEffectiveConfigurationLinkPolicy();
        ControlPlaneTenantEffectiveConfigurationDto configuration = CreateConfiguration();
        configuration = configuration with { RollbackAssignment = null };

        LinkDefinition[] links = policy.GetLinks(configuration, user: null).ToArray();

        await Assert.That(links.Any(link => link.Rel == "apply")).IsTrue();
        await Assert.That(links.Any(link => link.Rel == "rollback")).IsFalse();
    }

    [Test]
    public async Task DetailLinks_DoNotFlattenSettingMutationAffordancesAtRoot()
    {
        var policy = new ControlPlaneTenantEffectiveConfigurationLinkPolicy();
        var configuration = CreateConfiguration();
        configuration = configuration with
        {
            Settings =
            [
            new ControlPlaneTenantEffectiveSettingDto
            {
                Key = "branding.display_name",
                Category = "branding",
                Value = "Demo Tenant",
                ValueSource = "TenantOverride",
                IsLocked = false,
                IsSensitive = false
            },
            new ControlPlaneTenantEffectiveSettingDto
            {
                Key = "storage.max_upload_bytes",
                Category = "storage",
                Value = "104857600",
                ValueSource = "SystemLocked",
                IsLocked = true,
                IsSensitive = false
            },
            new ControlPlaneTenantEffectiveSettingDto
            {
                Key = "security.oidc_client_secret",
                Category = "security",
                Value = string.Empty,
                ValueSource = "TenantOverride",
                IsLocked = false,
                IsSensitive = true
            }
            ]
        };

        var relations = policy.GetLinks(configuration, user: null).Select(link => link.Rel).ToArray();

        await Assert.That(relations.Contains("override")).IsFalse();
        await Assert.That(relations.Contains("lock")).IsFalse();
        await Assert.That(relations.Contains("unlock")).IsFalse();
    }

    [Test]
    public async Task SettingLinks_FollowEffectiveStateMatrix()
    {
        var configuration = CreateConfiguration();
        configuration = configuration with
        {
            Settings =
            [
            Setting(InfrastructureSecretSettingKeys.Email.SmtpPassword, "TenantOverride", isSensitive: true),
            Setting(GovernanceSettingKeys.Email.SmtpHost, "SystemLocked", isLocked: true),
            Setting(GovernanceSettingKeys.Email.SmtpHost, "SystemDefault"),
            Setting(GovernanceSettingKeys.Email.SmtpSecurity, "OrganizationOverride"),
            Setting(GovernanceSettingKeys.Branding.DisplayName, "TenantOverride"),
            Setting(GovernanceSettingKeys.Email.SmtpSecurity, "TenantLocked", isLocked: true),
            Setting("unknown.setting", "TenantOverride"),
            Setting(GovernanceSettingKeys.AdminPortal.Enabled, "TenantOverride")
            ]
        };

        var result = await AssembleAsync(configuration, _ => true);

        await Assert.That(Relations(configuration.Settings[0])).IsEmpty();
        await Assert.That(Relations(configuration.Settings[1])).IsEmpty();
        await Assert.That(Relations(configuration.Settings[2])).IsEquivalentTo(["override"]);
        await Assert.That(Relations(configuration.Settings[3])).IsEquivalentTo(["override"]);
        await Assert.That(Relations(configuration.Settings[4])).IsEquivalentTo(["lock", "override"]);
        await Assert.That(Relations(configuration.Settings[5])).IsEquivalentTo(["override", "unlock"]);
        await Assert.That(Relations(configuration.Settings[6])).IsEmpty();
        await Assert.That(Relations(configuration.Settings[7])).IsEmpty();
        await Assert.That(result.Resource.Links.Keys.Any(IsSettingMutationRelation)).IsFalse();
    }

    [Test]
    public async Task SettingLinks_AreIndependentlyAuthorizedWithCommandSpecificStringMetadata()
    {
        var configuration = CreateConfiguration();
        const string targetKey = GovernanceSettingKeys.Branding.DisplayName;
        configuration = configuration with { Settings = [Setting(targetKey, "TenantOverride")] };

        var result = await AssembleAsync(configuration, definition => definition.Rel != "lock");

        await Assert.That(Relations(configuration.Settings.Single())).IsEquivalentTo(["override"]);

        var settingDefinitions = result.Batches
            .SelectMany(batch => batch)
            .Where(definition => IsSettingMutationRelation(definition.Rel))
            .ToArray();
        await Assert.That(settingDefinitions.Select(definition => definition.Rel))
            .IsEquivalentTo(["lock", "override"]);

        var overrideDefinition = settingDefinitions.Single(definition => definition.Rel == "override");
        await AssertSettingDefinition(
            overrideDefinition,
            RouteNames.SetControlPlaneTenantSetting,
            HttpMethods.Put,
            SetControlPlaneTenantSettingCommand.SettingKey,
            configuration.TenantId,
            targetKey);

        var lockDefinition = settingDefinitions.Single(definition => definition.Rel == "lock");
        await AssertSettingDefinition(
            lockDefinition,
            RouteNames.LockControlPlaneTenantSetting,
            HttpMethods.Post,
            LockControlPlaneTenantSettingCommand.SettingKey,
            configuration.TenantId,
            targetKey);
    }

    [Test]
    public async Task SettingLinks_UseUnlockCommandMetadataForTenantLockedSetting()
    {
        var configuration = CreateConfiguration();
        const string targetKey = GovernanceSettingKeys.Email.SmtpSecurity;
        configuration = configuration with { Settings = [Setting(targetKey, "TenantLocked", isLocked: true)] };

        var result = await AssembleAsync(configuration, _ => true);

        var unlockDefinition = result.Batches
            .SelectMany(batch => batch)
            .Single(definition => definition.Rel == "unlock");
        await AssertSettingDefinition(
            unlockDefinition,
            RouteNames.UnlockControlPlaneTenantSetting,
            HttpMethods.Delete,
            UnlockControlPlaneTenantSettingCommand.SettingKey,
            configuration.TenantId,
            targetKey);
    }

    [Test]
    public async Task SettingLinks_AreOmittedForMinimalResponse()
    {
        var configuration = CreateConfiguration();
        configuration = configuration with { Settings = [Setting(GovernanceSettingKeys.Branding.DisplayName, "TenantOverride")] };

        var result = await AssembleAsync(configuration, _ => true, minimal: true);

        await Assert.That(result.Resource.Links).IsEmpty();
        await Assert.That(configuration.Settings.Single().Links).IsNull();
        await Assert.That(result.Batches).IsEmpty();
    }

    [Test]
    public async Task SettingLinks_AreOmittedWhenRootHasNoAuthorizedLinks()
    {
        var configuration = CreateConfiguration();
        configuration = configuration with { Settings = [Setting(GovernanceSettingKeys.Branding.DisplayName, "TenantOverride")] };

        var result = await AssembleAsync(configuration, _ => false);

        await Assert.That(result.Resource.Links).IsEmpty();
        await Assert.That(configuration.Settings.Single().Links).IsNull();
        await Assert.That(result.Batches.Count).IsEqualTo(1);
        await Assert.That(result.Batches.Single().Any(definition => IsSettingMutationRelation(definition.Rel))).IsFalse();
    }

    [Test]
    public async Task EffectiveSettingJson_RoundTripsNestedLinksAndOmitsNullLinks()
    {
        var setting = Setting(GovernanceSettingKeys.Branding.DisplayName, "TenantOverride");
        setting = setting with { Value = "smtp.example.test" };
        setting.Links = new Dictionary<string, HalLink>
        {
            ["override"] = new HalLink
            {
                Href = "/api/control-plane/tenants/tenant-id/configuration/branding.display_name",
                Method = HttpMethods.Put,
                Title = "Override setting"
            }
        };

        var json = JsonSerializer.Serialize(setting, JsonOptions);
        using var document = JsonDocument.Parse(json);
        var roundTripped = JsonSerializer.Deserialize<ControlPlaneTenantEffectiveSettingDto>(json, JsonOptions);

        await Assert.That(document.RootElement.TryGetProperty("_links", out var links)).IsTrue();
        await Assert.That(links.GetProperty("override").GetProperty("method").GetString()).IsEqualTo(HttpMethods.Put);
        await Assert.That(roundTripped).IsNotNull();
        await Assert.That(roundTripped!.Links).IsNotNull();
        await Assert.That(roundTripped.Value).IsEqualTo("smtp.example.test");
        await Assert.That(roundTripped.Links!["override"].Href).IsEqualTo(setting.Links["override"].Href);
        await Assert.That(roundTripped.Links["override"].Method).IsEqualTo(HttpMethods.Put);

        var withoutLinksJson = JsonSerializer.Serialize(Setting("storage.provider", "SystemLocked"), JsonOptions);
        using var withoutLinksDocument = JsonDocument.Parse(withoutLinksJson);
        await Assert.That(withoutLinksDocument.RootElement.TryGetProperty("_links", out _)).IsFalse();
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static ControlPlaneTenantEffectiveConfigurationDto CreateConfiguration()
    {
        Guid tenantId = Guid.NewGuid();

        return new ControlPlaneTenantEffectiveConfigurationDto
        {
            TenantId = tenantId,
            PlanAssignment = new ControlPlaneTenantPlanAssignmentDto
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                PlanId = Guid.NewGuid(),
                PlanKey = "community",
                PlanVersionId = Guid.NewGuid(),
                VersionNumber = 1,
                StatusCode = "ACTIVE",
                AssignedAt = DateTime.UtcNow
            },
            RollbackAssignment = new ControlPlaneTenantPlanAssignmentDto
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                PlanId = Guid.NewGuid(),
                PlanKey = "starter",
                PlanVersionId = Guid.NewGuid(),
                VersionNumber = 1,
                StatusCode = "SUPERSEDED",
                AssignedAt = DateTime.UtcNow.AddDays(-30)
            }
        };
    }

    private static ControlPlaneTenantEffectiveSettingDto Setting(
        string key,
        string valueSource,
        bool isLocked = false,
        bool isSensitive = false) =>
        new()
        {
            Key = key,
            Category = key.Split('.', 2)[0],
            Value = isSensitive ? string.Empty : "value",
            ValueSource = valueSource,
            IsLocked = isLocked,
            IsSensitive = isSensitive
        };

    private static async Task<AssemblerResult> AssembleAsync(
        ControlPlaneTenantEffectiveConfigurationDto configuration,
        Func<LinkDefinition, bool> isAllowed,
        bool minimal = false)
    {
        var batches = new List<IReadOnlyList<LinkDefinition>>();
        var evaluator = Substitute.For<IHateoasAuthorizationEvaluator>();
        evaluator.AreLinksAllowedAsync(
                Arg.Any<IReadOnlyList<LinkDefinition>>(),
                Arg.Any<ClaimsPrincipal?>(),
                Arg.Any<HttpContext>())
            .Returns(call =>
            {
                var definitions = call.Arg<IReadOnlyList<LinkDefinition>>().ToArray();
                batches.Add(definitions);
                return Task.FromResult<IReadOnlyList<bool>>(definitions.Select(isAllowed).ToArray());
            });

        var linkGenerator = Substitute.For<IHateoasLinkGenerator>();
        linkGenerator.GenerateLink(Arg.Any<LinkDefinition>(), Arg.Any<HttpContext>())
            .Returns(call =>
            {
                var definition = call.Arg<LinkDefinition>();
                return new HalLink
                {
                    Href = $"/{definition.Rel}",
                    Method = definition.Method,
                    Title = definition.Title
                };
            });

        var services = new ServiceCollection();
        services.AddSingleton(evaluator);
        using var serviceProvider = services.BuildServiceProvider();
        var context = new DefaultHttpContext
        {
            RequestServices = serviceProvider,
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", Guid.NewGuid().ToString())], "test"))
        };
        if (minimal)
        {
            context.Items[HateoasConstants.MinimalResponseKey] = true;
        }

        var assembler = new ControlPlaneTenantEffectiveConfigurationResourceAssembler(
            linkGenerator,
            new ControlPlaneTenantEffectiveConfigurationLinkPolicy(),
            new ControlPlaneTenantEffectiveConfigurationCollectionLinkPolicy());

        var resource = await assembler.ToResource(configuration, context);
        return new AssemblerResult(resource, batches);
    }

    private static async Task AssertSettingDefinition(
        LinkDefinition definition,
        string routeName,
        string method,
        string resourceId,
        Guid tenantId,
        string targetKey)
    {
        await Assert.That(definition.RouteName).IsEqualTo(routeName);
        await Assert.That(definition.Method).IsEqualTo(method);
        await Assert.That(definition.RequiresAuth).IsTrue();
        await Assert.That(definition.PermissionResourceKind).IsEqualTo(ResourceKinds.InstanceSetting);
        await Assert.That(definition.PermissionAction).IsEqualTo(AuthorizationActions.InstanceSettings.Update);
        await Assert.That(definition.PermissionResourceId).IsEqualTo(resourceId);
        await Assert.That(RouteValues(definition)["tenantId"]).IsEqualTo(tenantId);
        await Assert.That(RouteValues(definition)["key"]).IsEqualTo(targetKey);
        await Assert.That(definition.PermissionFacts).IsEqualTo(InstanceScopedAuthorizationFacts.Instance);
    }

    private static string[] Relations(ControlPlaneTenantEffectiveSettingDto setting) =>
        setting.Links?.Keys.Order(StringComparer.Ordinal).ToArray() ?? [];

    private static bool IsSettingMutationRelation(string relation) =>
        relation is "override" or "lock" or "unlock";

    private static RouteValueDictionary RouteValues(LinkDefinition link) => new(link.RouteValues);

    private sealed record AssemblerResult(
        HalResource<ControlPlaneTenantEffectiveConfigurationDto> Resource,
        IReadOnlyList<IReadOnlyList<LinkDefinition>> Batches);
}
