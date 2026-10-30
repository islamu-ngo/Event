using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Data.Common;
using Explore.Domain;
using Explore.Domain.Settings.Documents;
using Explore.Persistence;
using ISLAMU.Wire.Contracts.Learning;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Event.API.IntegrationTests.Learning;

[ClassDataSource<LearningHttpFixture>(Shared = SharedType.PerClass)]
[NotInParallel]
public sealed class LearningPolicyAdmissionTests(LearningHttpFixture fixture)
{
    private const string Route = "/api/tenant/settings/documents/learning-guidance";
    private const string AcknowledgementRoute = "/api/learning/requirements/tenant-admin-orientation/acknowledge";

    [Test]
    public async Task AbsentPolicyGetReturnsPrivateDefaultsWithoutCreatingLearningRowsOrEditLink()
    {
        using var member = await fixture.CreateMemberAsync();
        await AssertNoPolicyRowAsync();
        await using var before = fixture.OpenInspection();
        int spacesBefore = await before.TenantLearningSpaces.CountAsync(row => row.TenantId == fixture.TenantId);
        int learnersBefore = await before.UserLearningStates.CountAsync(row => row.TenantId == fixture.TenantId);

        using var response = await member.Client.GetAsync(Route);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Headers.CacheControl?.Private).IsTrue();
        await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
        await Assert.That(response.Headers.GetValues("Referrer-Policy")).Contains("no-referrer");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement document = body.RootElement;
        await Assert.That(document.EnumerateObject().Select(property => property.Name).ToArray())
            .IsEquivalentTo(new[] { "schemaVersion", "defaultsVersion", "concurrencyStamp", "policy", "_links" });
        await Assert.That(document.GetProperty("schemaVersion").GetInt32()).IsGreaterThan(0);
        await Assert.That(string.IsNullOrWhiteSpace(document.GetProperty("defaultsVersion").GetString())).IsFalse();
        await Assert.That(document.GetProperty("concurrencyStamp").ValueKind).IsEqualTo(JsonValueKind.Null);
        JsonElement policy = document.GetProperty("policy");
        await Assert.That(policy.EnumerateObject().Select(property => property.Name).ToArray())
            .IsEquivalentTo(new[]
            {
                "enabled", "automaticInvitations", "invitationAudience",
                "manualAccessAudience", "requirementPolicy", "familyOverrides"
            });
        await Assert.That(policy.GetProperty("enabled").GetBoolean()).IsTrue();
        await Assert.That(policy.GetProperty("automaticInvitations").GetBoolean()).IsTrue();
        await Assert.That(policy.GetProperty("invitationAudience").GetString()).IsEqualTo("ActiveMembers");
        await Assert.That(policy.GetProperty("manualAccessAudience").GetString()).IsEqualTo("ActiveMembers");
        await Assert.That(policy.GetProperty("requirementPolicy").GetString()).IsEqualTo("Optional");
        await Assert.That(policy.GetProperty("familyOverrides").GetArrayLength()).IsEqualTo(0);
        JsonElement links = document.GetProperty("_links");
        await Assert.That(links.ValueKind).IsEqualTo(JsonValueKind.Object);
        await Assert.That(links.TryGetProperty("edit", out _)).IsFalse();
        await using var after = fixture.OpenInspection();
        await Assert.That(await after.TenantLearningSpaces.CountAsync(row => row.TenantId == fixture.TenantId))
            .IsEqualTo(spacesBefore);
        await Assert.That(await after.UserLearningStates.CountAsync(row => row.TenantId == fixture.TenantId))
            .IsEqualTo(learnersBefore);
        await AssertNoPolicyRowAsync();
    }

    [Test]
    public async Task AnonymousPolicyGetRequiresAuthenticationAndIsPrivateNoStore()
    {
        using var client = fixture.CreateClient();

        using var response = await client.GetAsync(Route);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(response.Headers.CacheControl?.Private).IsTrue();
        await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
    }

    [Test]
    public async Task OrdinaryMemberPolicyPatchIsForbiddenWithoutCreatingPolicyRow()
    {
        using var member = await fixture.CreateMemberAsync();
        await AssertNoPolicyRowAsync();
        using var content = JsonContent.Create(new
        {
            expectedConcurrencyStamp = (Guid?)null,
            policy = new
            {
                enabled = true,
                automaticInvitations = true,
                invitationAudience = "ActiveMembers",
                manualAccessAudience = "ActiveMembers",
                requirementPolicy = "Optional",
                familyOverrides = Array.Empty<object>()
            }
        });

        using var response = await member.Client.PatchAsync(Route, content);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        await AssertNoPolicyRowAsync();
    }

    [Test]
    public async Task CurrentAdministratorCannotRequireDisabledLearningOrCreatePolicyRow()
    {
        using var administrator = await fixture.CreateMemberAsync(administrator: true);
        await AssertNoPolicyRowAsync();
        using var content = JsonContent.Create(new
        {
            expectedConcurrencyStamp = (Guid?)null,
            policy = new
            {
                enabled = false,
                automaticInvitations = false,
                invitationAudience = "TenantAdministrators",
                manualAccessAudience = "TenantAdministrators",
                requirementPolicy = "TenantAdminOrientationAcknowledgement",
                familyOverrides = Array.Empty<object>()
            }
        });

        using var response = await administrator.Client.PatchAsync(Route, content);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await AssertNoPolicyRowAsync();
    }

    [Test]
    public async Task CurrentAdministratorPolicyPatchRejectsUnknownPropertyWithoutCreatingPolicyRow()
    {
        using var administrator = await fixture.CreateMemberAsync(administrator: true);
        await AssertNoPolicyRowAsync();
        using var content = JsonContent.Create(new
        {
            expectedConcurrencyStamp = (Guid?)null,
            policy = new
            {
                enabled = true,
                automaticInvitations = true,
                invitationAudience = "ActiveMembers",
                manualAccessAudience = "ActiveMembers",
                requirementPolicy = "Optional",
                familyOverrides = Array.Empty<object>(),
                unknownProperty = true
            }
        });

        using var response = await administrator.Client.PatchAsync(Route, content);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await AssertNoPolicyRowAsync();
    }

    [Test]
    public async Task PolicyCreationUsesObservedAbsenceAndRejectsStaleConcurrencyStamp()
    {
        await using var isolated = new LearningHttpFixture();
        await isolated.InitializeAsync();
        using var administrator = await isolated.CreateMemberAsync(administrator: true);
        using var initial = await administrator.Client.GetAsync(Route);
        await Assert.That(initial.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var initialBody = JsonDocument.Parse(await initial.Content.ReadAsStringAsync());
        await Assert.That(initialBody.RootElement.GetProperty("concurrencyStamp").ValueKind)
            .IsEqualTo(JsonValueKind.Null);
        await Assert.That(initialBody.RootElement.GetProperty("_links").TryGetProperty("edit", out _)).IsTrue();

        using var create = await administrator.Client.PatchAsJsonAsync(Route, new
        {
            expectedConcurrencyStamp = (Guid?)null,
            policy = OptionalPolicy()
        });
        await Assert.That(create.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var observed = await administrator.Client.GetAsync(Route);
        await Assert.That(observed.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var observedBody = JsonDocument.Parse(await observed.Content.ReadAsStringAsync());
        Guid firstStamp = observedBody.RootElement.GetProperty("concurrencyStamp").GetGuid();
        await Assert.That(firstStamp).IsNotEqualTo(Guid.Empty);

        using var absent = await administrator.Client.PatchAsJsonAsync(Route, new
        {
            expectedConcurrencyStamp = (Guid?)null,
            policy = OptionalPolicy(automaticInvitations: false)
        });
        await Assert.That(absent.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        using var update = await administrator.Client.PatchAsJsonAsync(Route, new
        {
            expectedConcurrencyStamp = firstStamp,
            policy = OptionalPolicy(automaticInvitations: false)
        });
        await Assert.That(update.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var stale = await administrator.Client.PatchAsJsonAsync(Route, new
        {
            expectedConcurrencyStamp = firstStamp,
            policy = OptionalPolicy()
        });
        await Assert.That(stale.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        using var final = await administrator.Client.GetAsync(Route);
        await Assert.That(final.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var finalBody = JsonDocument.Parse(await final.Content.ReadAsStringAsync());
        await Assert.That(finalBody.RootElement.GetProperty("concurrencyStamp").GetGuid()).IsNotEqualTo(firstStamp);
        await Assert.That(finalBody.RootElement.GetProperty("policy").GetProperty("automaticInvitations").GetBoolean())
            .IsFalse();
        await using var inspection = isolated.OpenInspection();
        await Assert.That(await inspection.TenantSettingsDocuments.CountAsync(row =>
            row.TenantId == isolated.TenantId && row.DocumentKey == SettingsDocumentKeys.Tenant.LearningGuidance))
            .IsEqualTo(1);
        await Assert.That(await inspection.UserLearningStates.AnyAsync(row =>
            row.TenantId == isolated.TenantId && row.UserId == administrator.UserId)).IsFalse();
    }

    [Test]
    public async Task InstanceDisabledTenantEditingRemovesEditAndRejectsPolicyCreation()
    {
        await using var isolated = new LearningHttpFixture();
        await isolated.InitializeAsync();
        using var administrator = await isolated.CreateMemberAsync(administrator: true);
        await using (var setup = isolated.OpenInspection())
        {
            const string key = "learning.guidance.allow_tenant_edits";
            var control = await setup.SystemSettings.SingleOrDefaultAsync(row => row.SettingKey == key);
            if (control is null)
                setup.SystemSettings.Add(new SystemSetting
                {
                    Id = Guid.CreateVersion7(), SettingKey = key, Value = "false",
                    ValueType = SettingValueType.Boolean
                });
            else
                control.Value = "false";
            var tenantOverride = await setup.Set<TenantSetting>().SingleOrDefaultAsync(row =>
                row.TenantId == isolated.TenantId && row.SettingKey == key);
            if (tenantOverride is null)
                setup.Set<TenantSetting>().Add(new TenantSetting
                {
                    Id = Guid.CreateVersion7(), TenantId = isolated.TenantId, Tenant = null!,
                    SettingKey = key, Value = "true"
                });
            else
                tenantOverride.Value = "true";
            await setup.SaveChangesAsync();
        }

        using var get = await administrator.Client.GetAsync(Route);
        await Assert.That(get.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await get.Content.ReadAsStringAsync());
        await Assert.That(body.RootElement.GetProperty("_links").TryGetProperty("edit", out _)).IsFalse();
        using var patch = await administrator.Client.PatchAsJsonAsync(Route, new
        {
            expectedConcurrencyStamp = (Guid?)null,
            policy = OptionalPolicy()
        });
        await Assert.That(patch.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        await using var inspection = isolated.OpenInspection();
        await Assert.That(await inspection.TenantSettingsDocuments.AnyAsync(row =>
            row.TenantId == isolated.TenantId && row.DocumentKey == SettingsDocumentKeys.Tenant.LearningGuidance))
            .IsFalse();
    }

    [Test]
    [Arguments("request")]
    [Arguments("override")]
    [Arguments("audience")]
    [Arguments("family")]
    [Arguments("duplicate")]
    [Arguments("excess")]
    public async Task PolicyPatchRejectsClosedContractViolations(string violation)
    {
        using var administrator = await fixture.CreateMemberAsync(administrator: true);
        await AssertNoPolicyRowAsync();
        var policy = OptionalPolicy();
        var request = new Dictionary<string, object?>
        {
            ["expectedConcurrencyStamp"] = null,
            ["policy"] = policy
        };
        var family = new Dictionary<string, object?>
        {
            ["family"] = "Shell", ["enabled"] = true, ["automaticInvitations"] = true,
            ["invitationAudience"] = null, ["manualAccessAudience"] = null
        };
        switch (violation)
        {
            case "request": request["canEdit"] = true; break;
            case "override":
                family["canEdit"] = true;
                policy["familyOverrides"] = new[] { family };
                break;
            case "audience": policy["invitationAudience"] = "Unknown"; break;
            case "family":
                family["family"] = "Unknown";
                policy["familyOverrides"] = new[] { family };
                break;
            case "duplicate": policy["familyOverrides"] = new[] { family, family }; break;
            case "excess": policy["familyOverrides"] = Enumerable.Repeat(family, 5).ToArray(); break;
        }

        using var response = await administrator.Client.PatchAsJsonAsync(Route, request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await AssertNoPolicyRowAsync();
    }

    [Test]
    public async Task RequirementAcknowledgementAnonymousRequiresAuthentication()
    {
        using var client = fixture.CreateClient();
        using var response = await client.PostAsJsonAsync(AcknowledgementRoute, new
        {
            intentId = Guid.CreateVersion7(),
            expectedScopeStamp = new string('0', 64),
            expectedRevision = 0,
            expectedPolicySourceStamp = new string('0', 64),
            materialVersion = 1
        });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
    }

    [Test]
    public async Task RequirementAcknowledgementOrdinaryMemberIsForbidden()
    {
        using var member = await fixture.CreateMemberAsync();
        using var contextResponse = await member.Client.GetAsync("/api/learning/context");
        await Assert.That(contextResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var context = (await contextResponse.Content.ReadFromJsonAsync<LearningContextDto>())!;
        using var planResponse = await member.Client.GetAsync("/api/learning/tours/Shell/plan?contextKey=self");
        await Assert.That(planResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var plan = (await planResponse.Content.ReadFromJsonAsync<LearningTourPlanDto>())!;
        using var response = await member.Client.PostAsJsonAsync(AcknowledgementRoute, new
        {
            intentId = Guid.CreateVersion7(),
            expectedScopeStamp = context.ScopeStamp,
            expectedRevision = context.LearnerRevision,
            expectedPolicySourceStamp = plan.PolicySourceStamp,
            materialVersion = 1
        });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        await using var inspection = fixture.OpenInspection();
        await Assert.That(await inspection.UserLearningStates.AnyAsync(row =>
            row.TenantId == fixture.TenantId && row.UserId == member.UserId)).IsFalse();
    }

    [Test]
    public async Task RequirementAcknowledgementOptionalPolicyRejectsAdministratorWithoutCreatingLearner()
    {
        using var administrator = await fixture.CreateMemberAsync(administrator: true);
        await AssertNoPolicyRowAsync();
        using var contextResponse = await administrator.Client.GetAsync("/api/learning/context");
        await Assert.That(contextResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var context = (await contextResponse.Content.ReadFromJsonAsync<LearningContextDto>())!;
        using var planResponse = await administrator.Client.GetAsync("/api/learning/tours/Shell/plan?contextKey=self");
        await Assert.That(planResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var plan = (await planResponse.Content.ReadFromJsonAsync<LearningTourPlanDto>())!;
        await using (var before = fixture.OpenInspection())
            await Assert.That(await before.UserLearningStates.AnyAsync(row =>
                row.TenantId == fixture.TenantId && row.UserId == administrator.UserId)).IsFalse();
        using var response = await administrator.Client.PostAsJsonAsync(AcknowledgementRoute, new
        {
            intentId = Guid.CreateVersion7(),
            expectedScopeStamp = context.ScopeStamp,
            expectedRevision = context.LearnerRevision,
            expectedPolicySourceStamp = plan.PolicySourceStamp,
            materialVersion = 1
        });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await using var after = fixture.OpenInspection();
        await Assert.That(await after.UserLearningStates.AnyAsync(row =>
            row.TenantId == fixture.TenantId && row.UserId == administrator.UserId)).IsFalse();
        await AssertNoPolicyRowAsync();
    }

    [Test]
    [Arguments("document-insert")]
    [Arguments("instance-value")]
    [Arguments("instance-lock")]
    public async Task PolicyMutationRejectsOriginalSourceChangesAfterObservation(string change)
    {
        var barrier = new PolicyObservationBarrier();
        await using var isolated = new LearningHttpFixture();
        await isolated.InitializeAsync(barrier, null);
        using var administrator = await isolated.CreateMemberAsync(administrator: true);
        const string controlKey = "learning.guidance.allow_tenant_edits";
        await using (var setup = isolated.OpenInspection())
        {
            barrier.DocumentTable = setup.Model.FindEntityType(typeof(TenantSettingsDocument))!.GetTableName()!;
            var control = await setup.SystemSettings.SingleOrDefaultAsync(row => row.SettingKey == controlKey);
            if (control is null)
                setup.SystemSettings.Add(new SystemSetting
                {
                    Id = Guid.CreateVersion7(), SettingKey = controlKey, Value = "true",
                    ValueType = SettingValueType.Boolean
                });
            else
            {
                control.Value = "true";
                control.IsLocked = false;
            }
            await setup.SaveChangesAsync();
        }
        barrier.Armed = true;
        var pending = administrator.Client.PatchAsJsonAsync(Route, new
        {
            expectedConcurrencyStamp = (Guid?)null, policy = OptionalPolicy()
        });
        try
        {
            await Task.WhenAny(barrier.Arrived.Task, pending).WaitAsync(TimeSpan.FromSeconds(30));
            await Assert.That(barrier.Arrived.Task.IsCompletedSuccessfully).IsTrue();
            await using var writer = isolated.OpenInspection();
            if (change == "document-insert")
            {
                var document = TenantSettingsDocument.Create(isolated.TenantId,
                    SettingsDocumentKeys.Tenant.LearningGuidance, 1, "learning-guidance:1",
                    JsonSerializer.Serialize(OptionalPolicy(automaticInvitations: false)));
                document.Id = Guid.CreateVersion7();
                writer.TenantSettingsDocuments.Add(document);
            }
            else
            {
                var control = await writer.SystemSettings.SingleAsync(row => row.SettingKey == controlKey);
                if (change == "instance-value") control.Value = "false";
                else control.IsLocked = true;
            }
            await writer.SaveChangesAsync();
        }
        finally { barrier.Release.TrySetResult(); }

        using var response = await pending.WaitAsync(TimeSpan.FromSeconds(30));
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        await using var inspection = isolated.OpenInspection();
        var stored = await inspection.TenantSettingsDocuments.AsNoTracking().SingleOrDefaultAsync(row =>
            row.TenantId == isolated.TenantId && row.DocumentKey == SettingsDocumentKeys.Tenant.LearningGuidance);
        if (change == "document-insert")
        {
            await Assert.That(stored).IsNotNull();
            using var payload = JsonDocument.Parse(stored!.PayloadJson);
            await Assert.That(payload.RootElement.GetProperty("automaticInvitations").GetBoolean()).IsFalse();
        }
        else
            await Assert.That(stored).IsNull();
        await Assert.That(await inspection.UserLearningStates.AnyAsync(row =>
            row.TenantId == isolated.TenantId && row.UserId == administrator.UserId)).IsFalse();
    }

    [Test]
    [Arguments("schema")]
    [Arguments("defaults")]
    [Arguments("unknown-property")]
    [Arguments("duplicate-property")]
    [Arguments("invalid-enum")]
    public async Task StoredUnsupportedOrMalformedPolicyNeverFallsBackOrAcceptsReplacement(string corruption)
    {
        await using var isolated = new LearningHttpFixture();
        await isolated.InitializeAsync();
        using var administrator = await isolated.CreateMemberAsync(administrator: true);
        string payload = JsonSerializer.Serialize(OptionalPolicy());
        payload = corruption switch
        {
            "unknown-property" => payload.Insert(1, "\"canEdit\":true,"),
            "duplicate-property" => payload.Insert(1, "\"enabled\":false,"),
            "invalid-enum" => payload.Replace("ActiveMembers", "Everyone", StringComparison.Ordinal),
            _ => payload
        };
        Guid stamp;
        await using (var setup = isolated.OpenInspection())
        {
            var document = TenantSettingsDocument.Create(isolated.TenantId,
                SettingsDocumentKeys.Tenant.LearningGuidance, corruption == "schema" ? 2 : 1,
                corruption == "defaults" ? "learning-guidance:unsupported" : "learning-guidance:1", payload);
            document.Id = Guid.CreateVersion7();
            setup.TenantSettingsDocuments.Add(document);
            await setup.SaveChangesAsync();
            stamp = document.ConcurrencyStamp;
        }
        using var get = await administrator.Client.GetAsync(Route);
        await Assert.That(get.StatusCode).IsEqualTo(HttpStatusCode.InternalServerError);
        await Assert.That(get.Headers.CacheControl?.NoStore).IsTrue();
        using var patch = await administrator.Client.PatchAsJsonAsync(Route, new
        {
            expectedConcurrencyStamp = stamp, policy = OptionalPolicy()
        });
        await Assert.That(patch.StatusCode).IsEqualTo(HttpStatusCode.InternalServerError);
        await using var inspection = isolated.OpenInspection();
        var retained = await inspection.TenantSettingsDocuments.AsNoTracking().SingleAsync(row =>
            row.TenantId == isolated.TenantId && row.DocumentKey == SettingsDocumentKeys.Tenant.LearningGuidance);
        await Assert.That(retained.ConcurrencyStamp).IsEqualTo(stamp);
        await Assert.That(retained.PayloadJson).IsEqualTo(payload);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task FinalAdmissionAcceptsJsonFormattingButRejectsChangedPolicyMeaning(bool changeMeaning)
    {
        var interruption = new PolicyWriteImageInterceptor(changeMeaning);
        await using var isolated = new LearningHttpFixture();
        await isolated.InitializeAsync(interruption, null);
        using var administrator = await isolated.CreateMemberAsync(administrator: true);
        interruption.Armed = true;

        using var response = await administrator.Client.PatchAsJsonAsync(Route, new
        {
            expectedConcurrencyStamp = (Guid?)null, policy = OptionalPolicy()
        });

        await Assert.That(interruption.Applied).IsTrue();
        await Assert.That(response.StatusCode).IsEqualTo(changeMeaning ? HttpStatusCode.Conflict : HttpStatusCode.OK);
        await using var inspection = isolated.OpenInspection();
        var document = await inspection.TenantSettingsDocuments.AsNoTracking().SingleOrDefaultAsync(row =>
            row.TenantId == isolated.TenantId && row.DocumentKey == SettingsDocumentKeys.Tenant.LearningGuidance);
        if (changeMeaning)
            await Assert.That(document).IsNull();
        else
        {
            await Assert.That(document).IsNotNull();
            using var payload = JsonDocument.Parse(document!.PayloadJson);
            await Assert.That(payload.RootElement.GetProperty("automaticInvitations").GetBoolean()).IsTrue();
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task DisabledOrExcludedMemberCannotCreateLearningThroughStartReplayOrPractice(bool excludedAudience)
    {
        await using var isolated = new LearningHttpFixture();
        await isolated.InitializeAsync();
        using var administrator = await isolated.CreateMemberAsync(administrator: true);
        using var member = await isolated.CreateMemberAsync();
        var policy = OptionalPolicy();
        policy["enabled"] = excludedAudience;
        policy["invitationAudience"] = excludedAudience ? "TenantAdministrators" : "ActiveMembers";
        policy["manualAccessAudience"] = excludedAudience ? "TenantAdministrators" : "ActiveMembers";
        policy["familyOverrides"] = new[]
        {
            new { family = "Shell", enabled = true, automaticInvitations = true }
        };
        await SetPolicyAsync(administrator.Client, policy);

        var context = await LearningOperationTests.Context(member.Client);
        var plan = await PolicyPlanAsync(member.Client);
        await Assert.That(context.Capabilities.CanLearn).IsTrue();
        await Assert.That(context.Capabilities.CanClaimTours).IsFalse();
        await Assert.That(context.Capabilities.CanSimulate).IsFalse();
        await Assert.That(context.Lessons).IsEmpty();
        await Assert.That(context.Capabilities.UnavailableReason).IsNotNull();
        await Assert.That(plan.CanClaim).IsFalse();
        await Assert.That(plan.CanInvite).IsFalse();
        await Assert.That(plan.UnavailableReason).IsNotNull();
        await Assert.That(((JsonElement)plan.AdditionalProperties["_links"]).TryGetProperty("claim-tour", out _)).IsFalse();
        await Assert.That(((JsonElement)context.AdditionalProperties["_links"]).TryGetProperty("start-simulation", out _)).IsFalse();
        foreach (bool replay in new[] { false, true })
        {
            using var denied = await member.Client.PostAsJsonAsync("/api/learning/tours/claim",
                LearningOperationTests.TourClaim(plan, replay));
            await Assert.That(denied.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        }
        using var practice = await member.Client.PostAsJsonAsync("/api/learning/practice/actions", StartPractice(context));
        await Assert.That(practice.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        await using var inspection = isolated.OpenInspection();
        await Assert.That(await inspection.UserLearningStates.AnyAsync(row => row.TenantId == isolated.TenantId)).IsFalse();
        await Assert.That(await inspection.TenantLearningSpaces.AnyAsync(row => row.TenantId == isolated.TenantId)).IsFalse();
    }

    [Test]
    public async Task ManualOnlyAllowsExplicitStartAndPracticeButFamilyCannotReenableInvitations()
    {
        await using var isolated = new LearningHttpFixture();
        await isolated.InitializeAsync();
        using var administrator = await isolated.CreateMemberAsync(administrator: true);
        using var member = await isolated.CreateMemberAsync();
        var policy = OptionalPolicy(automaticInvitations: false);
        policy["familyOverrides"] = new[]
        {
            new { family = "Shell", enabled = true, automaticInvitations = true }
        };
        await SetPolicyAsync(administrator.Client, policy);
        var plan = await PolicyPlanAsync(member.Client);
        await Assert.That(plan.CanInvite).IsFalse();
        await Assert.That(plan.CanClaim).IsTrue();
        var context = await LearningOperationTests.Context(member.Client);
        await Assert.That(context.Capabilities.CanInviteTours).IsFalse();
        await Assert.That(context.Tours.Any(tour => tour.CanInvite)).IsFalse();
        await Assert.That(context.Capabilities.CanSimulate).IsTrue();
        await Assert.That(context.Lessons.Count).IsGreaterThan(0);

        using var claimed = await member.Client.PostAsJsonAsync("/api/learning/tours/claim",
            LearningOperationTests.TourClaim(plan, replay: false));
        await Assert.That(claimed.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var current = await LearningOperationTests.Context(member.Client);
        using var practice = await member.Client.PostAsJsonAsync("/api/learning/practice/actions", StartPractice(current));
        await Assert.That(practice.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task IndependentAudiencesAffectOnlyTheirOwnInvitationAndManualObservations(bool administratorsInvited)
    {
        await using var isolated = new LearningHttpFixture();
        await isolated.InitializeAsync();
        using var administrator = await isolated.CreateMemberAsync(administrator: true);
        using var member = await isolated.CreateMemberAsync();
        var policy = OptionalPolicy();
        policy["invitationAudience"] = administratorsInvited ? "TenantAdministrators" : "ActiveMembers";
        policy["manualAccessAudience"] = administratorsInvited ? "ActiveMembers" : "TenantAdministrators";
        await SetPolicyAsync(administrator.Client, policy);

        var memberPlan = await PolicyPlanAsync(member.Client);
        var administratorPlan = await PolicyPlanAsync(administrator.Client);
        await Assert.That(memberPlan.CanInvite).IsEqualTo(!administratorsInvited);
        await Assert.That(memberPlan.CanClaim).IsTrue();
        await Assert.That(memberPlan.IsAvailable).IsTrue();
        await Assert.That(memberPlan.Steps.Count).IsGreaterThan(0);
        await Assert.That(administratorPlan.CanInvite).IsTrue();
        await Assert.That(administratorPlan.CanClaim).IsTrue();
        var memberContext = await LearningOperationTests.Context(member.Client);
        await Assert.That(memberContext.Capabilities.CanSimulate).IsEqualTo(administratorsInvited);
        using var claim = await member.Client.PostAsJsonAsync("/api/learning/tours/claim",
            LearningOperationTests.TourClaim(memberPlan, replay: true));
        await Assert.That(claim.StatusCode).IsEqualTo(administratorsInvited ? HttpStatusCode.OK : HttpStatusCode.Forbidden);
        if (!administratorsInvited)
        {
            using var started = await member.Client.PostAsJsonAsync("/api/learning/tours/claim",
                LearningOperationTests.TourClaim(memberPlan, replay: false));
            await Assert.That(started.StatusCode).IsEqualTo(HttpStatusCode.OK);
            var receipt = (await started.Content.ReadFromJsonAsync<LearningMutationReceiptDto>())!;
            var progressed = await LearningOperationTests.ProgressTourSteps(member.Client, memberPlan, receipt);
            var complete = LearningOperationTests.TourAction(memberPlan, receipt, LearningTourAction.Complete) with
            {
                ExpectedRevision = progressed.LearnerRevision
            };
            using var completed = await member.Client.PostAsJsonAsync("/api/learning/tours/outcome", complete);
            await Assert.That(completed.StatusCode).IsEqualTo(HttpStatusCode.OK);
            var completedReceipt = (await completed.Content.ReadFromJsonAsync<LearningMutationReceiptDto>())!;
            using var repeated = await member.Client.PostAsJsonAsync("/api/learning/tours/outcome", complete);
            await Assert.That(repeated.StatusCode).IsEqualTo(HttpStatusCode.OK);
            var final = await LearningOperationTests.Context(member.Client);
            await Assert.That(final.LearnerRevision).IsEqualTo(completedReceipt.LearnerRevision);
            var tour = final.Tours.Single(value => value.Family == LearningTourFamily.Shell);
            await Assert.That(tour.Outcome).IsEqualTo(LearningTourOutcome.Completed);
            await Assert.That(tour.CurrentCoverage).IsTrue();
            await Assert.That(tour.CanInvite).IsFalse();
        }
    }

    [Test]
    public async Task RevokedInvitationAudienceRejectsActiveTourEffectsWhenNoCurrentAccessBasisRemains()
    {
        await using var isolated = new LearningHttpFixture();
        await isolated.InitializeAsync();
        using var administrator = await isolated.CreateMemberAsync(administrator: true);
        using var member = await isolated.CreateMemberAsync();
        var policy = OptionalPolicy();
        policy["manualAccessAudience"] = "TenantAdministrators";
        await SetPolicyAsync(administrator.Client, policy);
        var plan = await PolicyPlanAsync(member.Client);
        var claim = LearningOperationTests.TourClaim(plan, replay: false);
        using var started = await member.Client.PostAsJsonAsync("/api/learning/tours/claim", claim);
        await Assert.That(started.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var receipt = (await started.Content.ReadFromJsonAsync<LearningMutationReceiptDto>())!;
        var progressed = await LearningOperationTests.ProgressTourSteps(member.Client, plan, receipt);
        policy["invitationAudience"] = "TenantAdministrators";
        await SetPolicyAsync(administrator.Client, policy);

        var unavailable = await PolicyPlanAsync(member.Client);
        await Assert.That(unavailable.CanClaim).IsFalse();
        await Assert.That(unavailable.CanInvite).IsFalse();
        await Assert.That(unavailable.IsAvailable).IsFalse();
        using var retry = await member.Client.PostAsJsonAsync("/api/learning/tours/claim", claim);
        await Assert.That(retry.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        foreach (LearningTourAction action in new[] { LearningTourAction.Progress, LearningTourAction.Complete })
        {
            var input = LearningOperationTests.TourAction(plan, receipt, action) with
            {
                ExpectedRevision = progressed.LearnerRevision,
                StepKey = action == LearningTourAction.Progress ? plan.Steps[0].StepKey : null,
                StepVersion = action == LearningTourAction.Progress ? plan.Steps[0].StepVersion : null
            };
            using var denied = await member.Client.PostAsJsonAsync("/api/learning/tours/outcome", input);
            await Assert.That(denied.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        }
        var retained = await LearningOperationTests.Context(member.Client);
        await Assert.That(retained.LearnerRevision).IsEqualTo(progressed.LearnerRevision);
        await Assert.That(retained.Claim!.Token).IsEqualTo(receipt.Claim!.Token);
    }

    [Test]
    public async Task DisabledNamedFamilyDoesNotDisableUnmappedPractice()
    {
        await using var isolated = new LearningHttpFixture();
        await isolated.InitializeAsync();
        using var administrator = await isolated.CreateMemberAsync(administrator: true);
        using var member = await isolated.CreateMemberAsync();
        var policy = OptionalPolicy();
        policy["familyOverrides"] = new[]
        {
            new { family = "Shell", enabled = false, automaticInvitations = false }
        };
        await SetPolicyAsync(administrator.Client, policy);
        var plan = await PolicyPlanAsync(member.Client);
        await Assert.That(plan.CanClaim).IsFalse();
        await Assert.That(plan.CanInvite).IsFalse();
        using var denied = await member.Client.PostAsJsonAsync("/api/learning/tours/claim",
            LearningOperationTests.TourClaim(plan, replay: true));
        await Assert.That(denied.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        var context = await LearningOperationTests.Context(member.Client);
        await Assert.That(context.Capabilities.CanSimulate).IsTrue();
        using var practice = await member.Client.PostAsJsonAsync("/api/learning/practice/actions", StartPractice(context));
        await Assert.That(practice.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task HiddenOrSuppressedGuidanceRequiresExplicitReplayWithoutChangingPolicy(bool hidden)
    {
        await using var isolated = new LearningHttpFixture();
        await isolated.InitializeAsync();
        using var member = await isolated.CreateMemberAsync();
        var context = await LearningOperationTests.Context(member.Client);
        using var preference = await member.Client.PatchAsJsonAsync("/api/learning/preferences", new LearningUpdatePreferencesRequest
        {
            ExpectedScopeStamp = context.ScopeStamp, ExpectedRevision = context.LearnerRevision,
            SuppressAutomaticTours = !hidden, HideLearning = hidden, PinLearning = false
        });
        await Assert.That(preference.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var plan = await PolicyPlanAsync(member.Client);
        await Assert.That(plan.CanInvite).IsFalse();
        await Assert.That(plan.CanClaim).IsTrue();
        using var ordinary = await member.Client.PostAsJsonAsync("/api/learning/tours/claim",
            LearningOperationTests.TourClaim(plan, replay: false));
        await Assert.That(ordinary.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        using var replay = await member.Client.PostAsJsonAsync("/api/learning/tours/claim",
            LearningOperationTests.TourClaim(plan, replay: true));
        await Assert.That(replay.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    [Arguments("claim", true)]
    [Arguments("claim", false)]
    [Arguments("complete", true)]
    [Arguments("complete", false)]
    [Arguments("practice", true)]
    [Arguments("practice", false)]
    public async Task RetainedInteractiveRetryRechecksPolicyAndAcceptedTourSources(string effect, bool disabled)
    {
        await using var isolated = new LearningHttpFixture();
        await isolated.InitializeAsync();
        using var administrator = await isolated.CreateMemberAsync(administrator: true);
        using var member = await isolated.CreateMemberAsync();
        var context = await LearningOperationTests.Context(member.Client);
        string route;
        object retry;
        long retainedRevision;
        Guid? practiceId = null;
        if (effect == "practice")
        {
            route = "/api/learning/practice/actions";
            retry = StartPractice(context);
            using var started = await member.Client.PostAsJsonAsync(route, retry);
            await Assert.That(started.StatusCode).IsEqualTo(HttpStatusCode.OK);
            var receipt = (await started.Content.ReadFromJsonAsync<LearningMutationReceiptDto>())!;
            retainedRevision = receipt.LearnerRevision;
            practiceId = receipt.Practice!.Id;
        }
        else
        {
            var plan = await PolicyPlanAsync(member.Client);
            var claim = LearningOperationTests.TourClaim(plan, replay: true);
            using var started = await member.Client.PostAsJsonAsync("/api/learning/tours/claim", claim);
            await Assert.That(started.StatusCode).IsEqualTo(HttpStatusCode.OK);
            var receipt = (await started.Content.ReadFromJsonAsync<LearningMutationReceiptDto>())!;
            route = "/api/learning/tours/claim";
            retry = claim;
            retainedRevision = receipt.LearnerRevision;
            if (effect == "complete")
            {
                var progressed = await LearningOperationTests.ProgressTourSteps(member.Client, plan, receipt);
                route = "/api/learning/tours/outcome";
                retry = LearningOperationTests.TourAction(plan, receipt, LearningTourAction.Complete) with
                {
                    ExpectedRevision = progressed.LearnerRevision
                };
                using var completed = await member.Client.PostAsJsonAsync(route, retry);
                await Assert.That(completed.StatusCode).IsEqualTo(HttpStatusCode.OK);
                retainedRevision = (await completed.Content.ReadFromJsonAsync<LearningMutationReceiptDto>())!.LearnerRevision;
            }
        }
        var policy = OptionalPolicy(automaticInvitations: false);
        policy["enabled"] = !disabled;
        if (!disabled && effect == "practice") policy["manualAccessAudience"] = "TenantAdministrators";
        await SetPolicyAsync(administrator.Client, policy);

        using var repeated = await member.Client.PostAsJsonAsync(route, retry);

        await Assert.That(repeated.StatusCode).IsEqualTo(disabled || effect == "practice"
            ? HttpStatusCode.Forbidden : HttpStatusCode.Conflict);
        var retained = await LearningOperationTests.Context(member.Client);
        await Assert.That(retained.LearnerRevision).IsEqualTo(retainedRevision);
        if (effect == "complete")
            await Assert.That(retained.Tours.Single(tour => tour.Family == LearningTourFamily.Shell).Outcome)
                .IsEqualTo(LearningTourOutcome.Completed);
        if (practiceId is { } id)
        {
            await Assert.That(retained.OwnedPractices.Single().PreviewAvailable).IsFalse();
            await Assert.That(retained.OwnedPractices.Single().UnavailableReason).IsNotNull();
            await Assert.That(retained.Capabilities.CanClearPractice).IsTrue();
            using var preview = await member.Client.GetAsync($"/api/learning/practice/{id:D}");
            await Assert.That(preview.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        }
    }

    [Test]
    [Arguments(LearningTourAction.Skip)]
    [Arguments(LearningTourAction.Interrupt)]
    [Arguments(LearningTourAction.Snooze)]
    public async Task DisabledPolicyAndRemovedSourceRetainClaimReleaseAndOwnedPracticeClear(LearningTourAction action)
    {
        await using var isolated = new LearningHttpFixture();
        await isolated.InitializeAsync();
        using var administrator = await isolated.CreateMemberAsync(administrator: true);
        using var member = await isolated.CreateMemberAsync();
        var context = await LearningOperationTests.Context(member.Client);
        var start = StartPractice(context);
        using var practiceResponse = await member.Client.PostAsJsonAsync("/api/learning/practice/actions", start);
        await Assert.That(practiceResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var practice = (await practiceResponse.Content.ReadFromJsonAsync<LearningMutationReceiptDto>())!.Practice!;
        var plan = await PolicyPlanAsync(member.Client);
        using var claimResponse = await member.Client.PostAsJsonAsync("/api/learning/tours/claim",
            LearningOperationTests.TourClaim(plan, replay: true));
        await Assert.That(claimResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var claim = (await claimResponse.Content.ReadFromJsonAsync<LearningMutationReceiptDto>())!;
        var policy = OptionalPolicy();
        policy["enabled"] = false;
        await SetPolicyAsync(administrator.Client, policy);
        var administratorContext = await LearningOperationTests.Context(administrator.Client);
        using var removed = await administrator.Client.PostAsJsonAsync("/api/learning/space/actions", new LearningManageSpaceRequest
        {
            ExpectedScopeStamp = administratorContext.ScopeStamp,
            ExpectedRevision = administratorContext.ResourceRevision, Action = LearningSpaceAction.Remove
        });
        await Assert.That(removed.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var unavailable = await LearningOperationTests.Context(member.Client);
        await Assert.That(unavailable.SpaceStatus).IsEqualTo(LearningSpaceStatus.Removed);
        await Assert.That(unavailable.OwnedPractices.Single().PreviewAvailable).IsFalse();
        await Assert.That(unavailable.Capabilities.CanClearPractice).IsTrue();
        var release = LearningOperationTests.TourAction(plan, claim, action) with
        {
            SnoozeDuration = action == LearningTourAction.Snooze ? LearningSnoozeDuration.OneDay : null
        };
        using var released = await member.Client.PostAsJsonAsync("/api/learning/tours/outcome", release);
        await Assert.That(released.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var releasedReceipt = (await released.Content.ReadFromJsonAsync<LearningMutationReceiptDto>())!;
        var clear = start with
        {
            IntentId = Guid.CreateVersion7(), PracticeId = practice.Id,
            ExpectedRevision = releasedReceipt.LearnerRevision,
            ExpectedPracticeRevision = practice.Revision, Action = LearningPracticeAction.Clear
        };
        using var cleared = await member.Client.PostAsJsonAsync("/api/learning/practice/actions", clear);
        await Assert.That(cleared.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var clearedReceipt = (await cleared.Content.ReadFromJsonAsync<LearningMutationReceiptDto>())!;
        using var retainedRelease = await member.Client.PostAsJsonAsync("/api/learning/tours/outcome", release);
        await Assert.That(retainedRelease.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var retainedClear = await member.Client.PostAsJsonAsync("/api/learning/practice/actions", clear);
        await Assert.That(retainedClear.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var final = await LearningOperationTests.Context(member.Client);
        await Assert.That(final.LearnerRevision).IsEqualTo(clearedReceipt.LearnerRevision);
        await Assert.That(final.Claim).IsNull();
        await Assert.That(final.OwnedPractices).IsEmpty();
        await Assert.That(final.Tours.Single(tour => tour.Family == LearningTourFamily.Shell).Outcome).IsNotNull();
        await using var inspection = isolated.OpenInspection();
        await Assert.That(await inspection.TenantLearningSpaces.CountAsync(row => row.TenantId == isolated.TenantId))
            .IsEqualTo(1);
        await Assert.That(await inspection.TenantLearningSpaces.Where(row => row.TenantId == isolated.TenantId)
            .Select(row => row.IsRemoved).SingleAsync()).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task PolicyCreationAfterInteractiveObservationRejectsWithoutCreatingLearningRows(bool practice)
    {
        var barrier = new PolicyObservationBarrier();
        await using var isolated = new LearningHttpFixture();
        await isolated.InitializeAsync(barrier, null);
        using var member = await isolated.CreateMemberAsync();
        var context = await LearningOperationTests.Context(member.Client);
        var plan = await PolicyPlanAsync(member.Client);
        await using (var metadata = isolated.OpenInspection())
            barrier.DocumentTable = metadata.Model.FindEntityType(typeof(TenantSettingsDocument))!.GetTableName()!;
        barrier.Armed = true;
        Task<HttpResponseMessage> pending = practice
            ? member.Client.PostAsJsonAsync("/api/learning/practice/actions", StartPractice(context))
            : member.Client.PostAsJsonAsync("/api/learning/tours/claim",
                LearningOperationTests.TourClaim(plan, replay: true));
        try
        {
            await Task.WhenAny(barrier.Arrived.Task, pending).WaitAsync(TimeSpan.FromSeconds(30));
            await Assert.That(barrier.Arrived.Task.IsCompletedSuccessfully).IsTrue();
            var policy = OptionalPolicy();
            policy["enabled"] = false;
            await using var writer = isolated.OpenInspection();
            var document = TenantSettingsDocument.Create(isolated.TenantId,
                SettingsDocumentKeys.Tenant.LearningGuidance, 1, "learning-guidance:1", JsonSerializer.Serialize(policy));
            document.Id = Guid.CreateVersion7();
            writer.TenantSettingsDocuments.Add(document);
            await writer.SaveChangesAsync();
        }
        finally { barrier.Release.TrySetResult(); }
        using var response = await pending.WaitAsync(TimeSpan.FromSeconds(30));
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        await using var inspection = isolated.OpenInspection();
        await Assert.That(await inspection.UserLearningStates.AnyAsync(row => row.TenantId == isolated.TenantId)).IsFalse();
        await Assert.That(await inspection.TenantLearningSpaces.AnyAsync(row => row.TenantId == isolated.TenantId)).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task FinalAdmissionRejectsPolicyDriftAfterInteractiveSaveAndRollsBackNewRows(bool practice)
    {
        var interruption = new InteractivePolicyDrift();
        await using var isolated = new LearningHttpFixture();
        await isolated.InitializeAsync(interruption, null);
        using var administrator = await isolated.CreateMemberAsync(administrator: true);
        using var member = await isolated.CreateMemberAsync();
        await SetPolicyAsync(administrator.Client, OptionalPolicy());
        var context = await LearningOperationTests.Context(member.Client);
        var plan = await PolicyPlanAsync(member.Client);
        await using (var setup = isolated.OpenInspection())
            interruption.DocumentId = await setup.TenantSettingsDocuments
                .Where(row => row.TenantId == isolated.TenantId && row.DocumentKey == SettingsDocumentKeys.Tenant.LearningGuidance)
                .Select(row => row.Id).SingleAsync();
        interruption.UserId = member.UserId;
        interruption.Armed = true;

        using var response = practice
            ? await member.Client.PostAsJsonAsync("/api/learning/practice/actions", StartPractice(context))
            : await member.Client.PostAsJsonAsync("/api/learning/tours/claim",
                LearningOperationTests.TourClaim(plan, replay: true));

        await Assert.That(interruption.Applied).IsTrue();
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        await using var inspection = isolated.OpenInspection();
        await Assert.That(await inspection.UserLearningStates.AnyAsync(row => row.TenantId == isolated.TenantId)).IsFalse();
        await Assert.That(await inspection.TenantLearningSpaces.AnyAsync(row => row.TenantId == isolated.TenantId)).IsFalse();
        var retained = await inspection.TenantSettingsDocuments.AsNoTracking().SingleAsync(row => row.Id == interruption.DocumentId);
        using var payload = JsonDocument.Parse(retained.PayloadJson);
        await Assert.That(payload.RootElement.GetProperty("enabled").GetBoolean()).IsTrue();
    }

    private sealed class InteractivePolicyDrift : SaveChangesInterceptor
    {
        private bool _saving;
        internal Guid UserId { get; set; }
        internal Guid DocumentId { get; set; }
        internal bool Armed { get; set; }
        internal bool Applied { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            _saving = Armed && eventData.Context is ExploreDbContext { HasHumanCommitContextLease: true } owner
                && owner.ChangeTracker.Entries<Explore.Domain.Learning.UserLearningState>()
                    .Any(entry => entry.Entity.UserId == UserId && entry.State is EntityState.Added or EntityState.Modified);
            return ValueTask.FromResult(result);
        }

        public override async ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (_saving && eventData.Context is ExploreDbContext owner)
            {
                _saving = false;
                Armed = false;
                if (owner.Database.CurrentTransaction is null)
                    throw new InvalidOperationException("Policy drift must occur under the original learning transaction.");
                var policy = OptionalPolicy();
                policy["enabled"] = false;
                string payload = JsonSerializer.Serialize(policy);
                int changed = await owner.TenantSettingsDocuments.Where(row => row.Id == DocumentId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.PayloadJson, payload), cancellationToken);
                if (changed != 1) throw new InvalidOperationException("The retained policy row was not changed.");
                Applied = true;
            }
            return result;
        }
    }

    private static LearningApplyPracticeActionRequest StartPractice(LearningContextDto context) => new()
    {
        ExpectedScopeStamp = context.ScopeStamp, ExpectedRevision = context.LearnerRevision,
        ExpectedPracticeRevision = 0, IntentId = Guid.CreateVersion7(), ContextKey = "self",
        RecipeKey = "event-details", MaterialVersion = 1, Action = LearningPracticeAction.Start
    };

    private static async Task<LearningTourPlanDto> PolicyPlanAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/learning/tours/Shell/plan?contextKey=self");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<LearningTourPlanDto>())!;
    }

    private static async Task SetPolicyAsync(HttpClient client, Dictionary<string, object?> policy)
    {
        using var observed = await client.GetAsync(Route);
        await Assert.That(observed.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await observed.Content.ReadAsStringAsync());
        JsonElement stamp = body.RootElement.GetProperty("concurrencyStamp");
        using var updated = await client.PatchAsJsonAsync(Route, new
        {
            expectedConcurrencyStamp = stamp.ValueKind == JsonValueKind.Null ? (Guid?)null : stamp.GetGuid(),
            policy
        });
        await Assert.That(updated.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    private sealed class PolicyObservationBarrier : DbCommandInterceptor
    {
        internal string DocumentTable { get; set; } = string.Empty;
        internal bool Armed { get; set; }
        internal TaskCompletionSource Arrived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (Armed && eventData.Context is ExploreDbContext { HasHumanCommitContextLease: true } owner
                && owner.Database.CurrentTransaction is null
                && command.CommandText.Contains(DocumentTable, StringComparison.Ordinal))
            {
                if (result.HasRows)
                    throw new InvalidOperationException("This witness requires observed guidance absence.");
                Armed = false;
                Arrived.TrySetResult();
                await Release.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            }
            return result;
        }
    }

    private sealed class PolicyWriteImageInterceptor(bool changeMeaning) : SaveChangesInterceptor
    {
        internal bool Armed { get; set; }
        internal bool Applied { get; private set; }

        public override async ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (Armed && eventData.Context is ExploreDbContext { HasHumanCommitContextLease: true } owner
                && owner.Database.CurrentTransaction is not null)
            {
                var document = owner.ChangeTracker.Entries<TenantSettingsDocument>()
                    .Select(entry => entry.Entity)
                    .SingleOrDefault(row => row.DocumentKey == SettingsDocumentKeys.Tenant.LearningGuidance);
                if (document is not null)
                {
                    Armed = false;
                    string payload = """
                        { "familyOverrides": [], "requirementPolicy": "Optional",
                          "manualAccessAudience": "ActiveMembers", "invitationAudience": "ActiveMembers",
                          "automaticInvitations": true, "enabled": true }
                        """;
                    if (changeMeaning)
                        payload = payload.Replace("\"automaticInvitations\": true",
                            "\"automaticInvitations\": false", StringComparison.Ordinal);
                    int affected = await owner.TenantSettingsDocuments.Where(row => row.Id == document.Id)
                        .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.PayloadJson, payload), cancellationToken);
                    if (affected != 1) throw new InvalidOperationException("The original policy row was not written.");
                    Applied = true;
                }
            }
            return result;
        }
    }

    private static Dictionary<string, object?> OptionalPolicy(bool automaticInvitations = true) => new()
    {
        ["enabled"] = true,
        ["automaticInvitations"] = automaticInvitations,
        ["invitationAudience"] = "ActiveMembers",
        ["manualAccessAudience"] = "ActiveMembers",
        ["requirementPolicy"] = "Optional",
        ["familyOverrides"] = Array.Empty<object>()
    };

    private async Task AssertNoPolicyRowAsync()
    {
        await using var inspection = fixture.OpenInspection();
        await Assert.That(await inspection.TenantSettingsDocuments.AnyAsync(row =>
            row.TenantId == fixture.TenantId && row.DocumentKey == SettingsDocumentKeys.Tenant.LearningGuidance))
            .IsFalse();
    }
}
