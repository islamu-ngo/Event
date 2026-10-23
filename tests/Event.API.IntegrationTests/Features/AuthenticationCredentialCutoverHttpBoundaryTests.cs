using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Secrets;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Hateoas;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel]
public sealed class AuthenticationCredentialCutoverHttpBoundaryTests
{
    private const string BaseUrl = "/api/admin/instance/authentication-credential-cutovers";
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    [Test]
    [Arguments("current")]
    [Arguments("receipt")]
    [Arguments("prepare")]
    [Arguments("activations")]
    [Arguments("fence")]
    public async Task GuestCannotReadOrMutateCutoverEvidence(string route)
    {
        await using var fixture = await Fixture.CreateAsync();
        using var response = await fixture.SendAsync(route, authenticated: false);
        await Assert.That(response.StatusCode).IsEqualTo(route is "current" or "receipt"
            ? HttpStatusCode.Forbidden : HttpStatusCode.Unauthorized);
        await fixture.AssertNoCurrentAsync();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SetupSecretDoesNotGrantPreparationAuthority(bool authenticated)
    {
        await using var fixture = await Fixture.CreateAsync();
        if (authenticated)
            await fixture.RevokeAsync();
        using var request = fixture.Request(HttpMethod.Post, BaseUrl, fixture.Input, authenticated);
        request.Headers.Add("X-Setup-Secret", OnboardingWebApplicationFactory.SetupSecret);
        using var response = await fixture.Client.SendAsync(request);
        await Assert.That(response.StatusCode).IsEqualTo(
            authenticated ? HttpStatusCode.Forbidden : HttpStatusCode.Unauthorized);
        await fixture.AssertNoCurrentAsync(reauthorize: authenticated);
    }

    [Test]
    public async Task CurrentWithoutOwnReceiptReturnsPrivateNoContentAndForeignReceiptIsNotFound()
    {
        await using var fixture = await Fixture.CreateAsync();
        var foreign = await fixture.SeedForeignAsync();
        await fixture.AssertNoCurrentAsync();
        using var request = fixture.Request(HttpMethod.Get, $"{BaseUrl}/{foreign.Id}");
        using var response = await fixture.Client.SendAsync(request);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await AssertPrivateAsync(response);
        using var missing = fixture.Request(HttpMethod.Get, $"{BaseUrl}/{Guid.CreateVersion7()}");
        using var missingResponse = await fixture.Client.SendAsync(missing);
        await Assert.That(missingResponse.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ExactFrozenInventoryAndFenceReachReadinessWithoutCleanup(bool fenceFirst)
    {
        await using var fixture = await Fixture.CreateAsync();
        var prepared = await fixture.PrepareAsync();
        await Assert.That(prepared.Data.CreatedBy).IsEqualTo(fixture.UserId);
        await Assert.That(prepared.Data.InstanceId).IsEqualTo(fixture.InstanceId);
        await Assert.That(prepared.Data.CreatedAt).IsEqualTo(Now);
        await Assert.That(prepared.Data.Deadline).IsEqualTo(Now.AddMinutes(15));
        await Assert.That(prepared.Data.Authority).IsEqualTo("Environment");
        await Assert.That(prepared.Data.AuthorityProfileRevision).IsEqualTo(fixture.Factory.Profile.Current.Revision);
        await Assert.That(prepared.Data.RequiredReplacements).IsEquivalentTo(fixture.Input.RequiredReplacements);
        await AssertActionsAsync(prepared, report: true, fence: true);

        var receipt = prepared;
        if (fenceFirst)
            receipt = await fixture.FenceAsync(receipt.Data);
        receipt = await fixture.ActivateAsync(receipt.Data, 0);
        await Assert.That(receipt.Data.State).IsEqualTo("Collecting");
        await Assert.That(receipt.Data.Activations.Count).IsEqualTo(1);
        await Assert.That(receipt.Data.RequiredReplacements).IsEquivalentTo(prepared.Data.RequiredReplacements);
        receipt = await fixture.ActivateAsync(receipt.Data, 1);
        if (!fenceFirst)
        {
            await Assert.That(receipt.Data.State).IsEqualTo("Collecting");
            receipt = await fixture.FenceAsync(receipt.Data);
        }
        await Assert.That(receipt.Data.State).IsEqualTo("ReadyForCleanup");
        await Assert.That(receipt.Data.FenceAttested).IsTrue();
        await Assert.That(receipt.Data.CleanedSettingCount).IsNull();
        await Assert.That(receipt.Data.CleanedBindingCount).IsNull();
        await Assert.That(receipt.Data.Activations.Select(report =>
            (report.ReplicaId, report.ProcessBootId, report.Consumer, report.Generation)))
            .IsEquivalentTo(fixture.Input.RequiredReplacements.Select(member =>
                (member.ReplicaId, member.ProcessBootId, member.Consumer, member.Generation)));
        await Assert.That(receipt.Data.Activations.All(report =>
            report.InstanceId == fixture.InstanceId && report.OperationId == prepared.Data.OperationId
            && report.DeploymentRevision == fixture.Input.DeploymentRevision)).IsTrue();
        await AssertActionsAsync(receipt, report: true, fence: false, cleanup: true);
        await AssertSameReceiptAsync(receipt.Data, (await fixture.CurrentAsync()).Data);
        await AssertSameReceiptAsync(receipt.Data, (await fixture.ByIdAsync(receipt.Data.OperationId)).Data);
        await fixture.AssertNoGenericReplayAsync();
    }

    [Test]
    [Arguments("profile")]
    [Arguments("authority")]
    [Arguments("operation")]
    [Arguments("head")]
    [Arguments("stamp")]
    [Arguments("deployment")]
    [Arguments("replica")]
    [Arguments("boot")]
    [Arguments("consumer")]
    [Arguments("generation")]
    [Arguments("foreign")]
    public async Task RejectedActivationPreservesReceiptAndHeadWithoutPartialEvidence(string mismatch)
    {
        await using var fixture = await Fixture.CreateAsync();
        var prepared = await fixture.PrepareAsync();
        var input = Activation(prepared.Data, 0);
        input = mismatch switch
        {
            "operation" => input with { OperationId = Guid.CreateVersion7() },
            "head" => input with { ExpectedHeadStamp = Guid.CreateVersion7() },
            "stamp" => input with { ExpectedOperationStamp = Guid.CreateVersion7() },
            "deployment" => input with { DeploymentRevision = Guid.CreateVersion7() },
            "replica" => input with { ReplicaId = Guid.CreateVersion7() },
            "boot" => input with { ProcessBootId = prepared.Data.RequiredReplacements[0].PreviousProcessBootId },
            "consumer" => input with { Consumer = AuthenticationCredentialCutover.CredentialConsumer.KeycloakOidc },
            "generation" => input with { Generation = Guid.CreateVersion7() },
            _ => input
        };
        if (mismatch == "profile")
            fixture.Factory.Profile.Current = fixture.Factory.Profile.Current with { Revision = Guid.CreateVersion7() };
        if (mismatch == "authority")
            fixture.Factory.Profile.Current = fixture.Factory.Profile.Current with
            {
                Authority = AuthenticationCredentialCutover.CredentialAuthority.Infisical
            };
        if (mismatch == "foreign")
        {
            var foreign = await fixture.SeedForeignAsync();
            var member = foreign.RequiredReplacements[0];
            input = input with
            {
                OperationId = foreign.Id,
                DeploymentRevision = foreign.DeploymentRevision,
                ReplicaId = member.ReplicaId,
                ProcessBootId = member.ProcessBootId,
                Consumer = member.Consumer,
                Generation = member.Generation,
                ExpectedOperationStamp = foreign.ConcurrencyStamp
            };
        }
        using var request = fixture.Request(HttpMethod.Post, $"{BaseUrl}/activations", input);
        using var response = await fixture.Client.SendAsync(request);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        await AssertSameReceiptAsync(prepared.Data, (await fixture.CurrentAsync()).Data);
        await AssertSameReceiptAsync(prepared.Data, (await fixture.ByIdAsync(prepared.Data.OperationId)).Data);
    }

    [Test]
    [Arguments("head", HttpStatusCode.Conflict)]
    [Arguments("stamp", HttpStatusCode.Conflict)]
    [Arguments("deployment", HttpStatusCode.Conflict)]
    [Arguments("sessions", HttpStatusCode.BadRequest)]
    public async Task RejectedFenceDoesNotPersistAttestationOrCollectingTransition(string mismatch, HttpStatusCode status)
    {
        await using var fixture = await Fixture.CreateAsync();
        var prepared = await fixture.PrepareAsync();
        var input = Fence(prepared.Data);
        input = mismatch switch
        {
            "head" => input with { ExpectedHeadStamp = Guid.CreateVersion7() },
            "stamp" => input with { ExpectedOperationStamp = Guid.CreateVersion7() },
            "deployment" => input with { DeploymentRevision = Guid.CreateVersion7() },
            _ => input with { OldSessionsFenced = false }
        };
        using var request = fixture.Request(HttpMethod.Post, $"{BaseUrl}/fence", input);
        using var response = await fixture.Client.SendAsync(request);
        await Assert.That(response.StatusCode).IsEqualTo(status);
        await AssertSameReceiptAsync(prepared.Data, (await fixture.CurrentAsync()).Data);
    }

    [Test]
    [Arguments("prepare", "createdBy")]
    [Arguments("prepare", "instanceId")]
    [Arguments("prepare", "authority")]
    [Arguments("prepare", "authorityProfileRevision")]
    [Arguments("prepare", "createdAt")]
    [Arguments("prepare", "deadline")]
    [Arguments("activations", "instanceId")]
    [Arguments("fence", "attestedBy")]
    [Arguments("fence", "now")]
    public async Task ClientCannotSupplyServerOwnedAuthorityOrTime(string route, string field)
    {
        await using var fixture = await Fixture.CreateAsync();
        var prepared = await fixture.PrepareAsync();
        object input = route switch
        {
            "prepare" => fixture.Input with
            {
                ExpectedCurrentOperationId = prepared.Data.OperationId,
                ExpectedHeadStamp = prepared.Data.CurrentHeadStamp,
                ExpectedPredecessorStamp = prepared.Data.ConcurrencyStamp
            },
            "activations" => Activation(prepared.Data, 0),
            _ => Fence(prepared.Data)
        };
        var json = JsonSerializer.SerializeToNode(input, input.GetType(), TestJsonOptions.Default)!.AsObject();
        json[field] = field is "createdAt" or "deadline" or "now"
            ? JsonValue.Create(Now.AddYears(1).ToString("O"))
            : JsonValue.Create(Guid.CreateVersion7().ToString());
        using var request = fixture.Request(HttpMethod.Post,
            route == "prepare" ? BaseUrl : $"{BaseUrl}/{route}", json);
        using var response = await fixture.Client.SendAsync(request);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await AssertSameReceiptAsync(prepared.Data, (await fixture.CurrentAsync()).Data);
    }

    [Test]
    [Arguments("current")]
    [Arguments("receipt")]
    [Arguments("prepare")]
    [Arguments("activations")]
    [Arguments("fence")]
    public async Task RevokedAdministratorCannotRecoverOrMutateDespiteAuthenticatedPrincipal(string route)
    {
        await using var fixture = await Fixture.CreateAsync();
        var prepared = await fixture.PrepareAsync();
        await fixture.RevokeAsync();
        using var response = await fixture.SendAsync(route, prepared.Data);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        await fixture.GrantAsync();
        await AssertSameReceiptAsync(prepared.Data, (await fixture.CurrentAsync()).Data);
        await AssertSameReceiptAsync(prepared.Data, (await fixture.ByIdAsync(prepared.Data.OperationId)).Data);
        await fixture.AssertNoGenericReplayAsync();
    }

    [Test]
    public async Task SupersessionRetainsReceiptButRemovesOldOperationMutationAffordances()
    {
        await using var fixture = await Fixture.CreateAsync();
        var first = await fixture.PrepareAsync();
        var activated = await fixture.ActivateAsync(first.Data, 0);
        var nextInput = fixture.Input with
        {
            DeploymentRevision = Guid.CreateVersion7(),
            ExpectedCurrentOperationId = activated.Data.OperationId,
            ExpectedHeadStamp = activated.Data.CurrentHeadStamp,
            ExpectedPredecessorStamp = activated.Data.ConcurrencyStamp
        };
        var next = await fixture.PrepareAsync(nextInput);
        var retained = await fixture.ByIdAsync(first.Data.OperationId);
        await Assert.That(retained.Data.State).IsEqualTo("Superseded");
        await Assert.That(retained.Data.CurrentHeadStamp).IsNull();
        await Assert.That(retained.Data.Activations).IsEquivalentTo(activated.Data.Activations);
        await Assert.That(retained.Data.RequiredReplacements).IsEquivalentTo(first.Data.RequiredReplacements);
        await Assert.That(retained.Data.ConcurrencyStamp).IsNotEqualTo(activated.Data.ConcurrencyStamp);
        await AssertActionsAsync(retained, report: false, fence: false);
        await AssertActionsAsync(next, report: true, fence: true);
        await AssertSameReceiptAsync(next.Data, (await fixture.CurrentAsync()).Data);
        using var staleRequest = fixture.Request(HttpMethod.Post, $"{BaseUrl}/activations", Activation(activated.Data, 1));
        using var staleResponse = await fixture.Client.SendAsync(staleRequest);
        await Assert.That(staleResponse.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        await AssertSameReceiptAsync(retained.Data, (await fixture.ByIdAsync(first.Data.OperationId)).Data);
        await AssertSameReceiptAsync(next.Data, (await fixture.CurrentAsync()).Data);
    }

    [Test]
    [Arguments("activations")]
    [Arguments("fence")]
    public async Task CallbackAtServerDeadlineExpiresReceiptWithoutRecordingEvidence(string route)
    {
        await using var fixture = await Fixture.CreateAsync();
        var prepared = await fixture.PrepareAsync();
        fixture.Factory.Clock.UtcNow = prepared.Data.Deadline;
        using var response = await fixture.SendAsync(route, prepared.Data);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        var expired = await fixture.CurrentAsync();
        await Assert.That(expired.Data.State).IsEqualTo("Expired");
        await Assert.That(expired.Data.Activations.Count).IsEqualTo(0);
        await Assert.That(expired.Data.FenceAttested).IsFalse();
        await Assert.That(expired.Data.CleanedSettingCount).IsNull();
        await Assert.That(expired.Data.CleanedBindingCount).IsNull();
        await AssertActionsAsync(expired, report: false, fence: false);
        await AssertSameReceiptAsync(expired.Data, (await fixture.ByIdAsync(prepared.Data.OperationId)).Data);
    }

    [Test]
    [Arguments("empty")]
    [Arguments("duplicate")]
    [Arguments("old-boot")]
    public async Task InvalidInventoryCannotCreateReceiptOrHead(string invalid)
    {
        await using var fixture = await Fixture.CreateAsync();
        var member = fixture.Input.RequiredReplacements[0];
        var input = fixture.Input with
        {
            RequiredReplacements = invalid switch
            {
                "empty" => [],
                "duplicate" => [member, member],
                _ => [member with { ProcessBootId = member.PreviousProcessBootId }]
            }
        };
        using var request = fixture.Request(HttpMethod.Post, BaseUrl, input);
        using var response = await fixture.Client.SendAsync(request);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await fixture.AssertNoCurrentAsync();
    }

    [Test]
    public async Task ApprovedExactLegacyCleanupReturnsDurableCountsWithoutCredentials()
    {
        await using var fixture = await Fixture.CreateAsync();
        var receipt = await fixture.PrepareAsync();
        receipt = await fixture.ActivateAsync(receipt.Data, 0);
        receipt = await fixture.ActivateAsync(receipt.Data, 1);
        receipt = await fixture.FenceAsync(receipt.Data);
        Guid legacyId = Guid.CreateVersion7();
        Guid unrelatedId = Guid.CreateVersion7();
        string canary = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            foreach (var (id, key) in new[]
                     { (legacyId, "auth.google_client_secret"), (unrelatedId, "cutover.untouched") })
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO ie_system_settings
                        (id, setting_key, value, setting_value_type_id, is_locked, display_order, created_at)
                    VALUES ({id}, {key}, {JsonSerializer.Serialize(canary)}, {0}, {true}, {0}, {Now.UtcDateTime})
                    """);
            }
        }
        var input = new
        {
            receipt.Data.OperationId,
            receipt.Data.DeploymentRevision,
            expectedHeadStamp = receipt.Data.CurrentHeadStamp,
            expectedOperationStamp = receipt.Data.ConcurrencyStamp,
            approvedSettingIds = new[] { legacyId },
            approvedBindingIds = Array.Empty<Guid>()
        };
        using (var inspectRequest = fixture.Request(HttpMethod.Get,
                   $"{BaseUrl}/{receipt.Data.OperationId}/cleanup-targets", null))
        using (var inspected = await fixture.Client.SendAsync(inspectRequest))
        {
            await Assert.That(inspected.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await AssertPrivateAsync(inspected);
            string metadata = await inspected.Content.ReadAsStringAsync();
            await Assert.That(metadata.Contains(canary, StringComparison.Ordinal)).IsFalse();
            var inventory = JsonSerializer.Deserialize<AuthenticationCredentialCleanupInventoryDto>(
                metadata, TestJsonOptions.Default)!;
            await Assert.That(inventory.Settings.Select(row => row.Id)).IsEquivalentTo(new[] { legacyId });
            await Assert.That(inventory.Settings.Single().SettingKey).IsEqualTo("auth.google_client_secret");
            await Assert.That(inventory.Bindings).IsEmpty();
            await Assert.That(inventory.HeadStamp).IsEqualTo(receipt.Data.CurrentHeadStamp);
            await Assert.That(inventory.OperationStamp).IsEqualTo(receipt.Data.ConcurrencyStamp);
        }
        using var request = fixture.Request(HttpMethod.Post, $"{BaseUrl}/cleanup", input);
        using var response = await fixture.Client.SendAsync(request);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await AssertPrivateAsync(response);
        string json = await response.Content.ReadAsStringAsync();
        await Assert.That(json.Contains(canary, StringComparison.Ordinal)).IsFalse();
        var cleaned = JsonSerializer.Deserialize<HalResource<AuthenticationCredentialCutoverDto>>(
            json, TestJsonOptions.Default)!;
        await Assert.That(cleaned.Data.State).IsEqualTo("Cleaned");
        await Assert.That(cleaned.Data.CleanedSettingCount).IsEqualTo(1);
        await Assert.That(cleaned.Data.CleanedBindingCount).IsEqualTo(0);
        await AssertActionsAsync(cleaned, report: false, fence: false);
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            await Assert.That(await db.SystemSettings.AnyAsync(row => row.Id == legacyId)).IsFalse();
            await Assert.That(await db.SystemSettings.AnyAsync(row => row.Id == unrelatedId)).IsTrue();
        }
        await AssertSameReceiptAsync(cleaned.Data, (await fixture.CurrentAsync()).Data);
        using (var retry = fixture.Request(HttpMethod.Post, $"{BaseUrl}/cleanup", input))
        using (var repeated = await fixture.Client.SendAsync(retry))
        {
            await Assert.That(repeated.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await AssertPrivateAsync(repeated);
            var retained = JsonSerializer.Deserialize<HalResource<AuthenticationCredentialCutoverDto>>(
                await repeated.Content.ReadAsStringAsync(), TestJsonOptions.Default)!;
            await AssertSameReceiptAsync(cleaned.Data, retained.Data);
        }
        await fixture.AssertNoGenericReplayAsync();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CleanupAndInspectionRequireCurrentAdministrator(bool revoked)
    {
        await using var fixture = await Fixture.CreateAsync();
        var receipt = await fixture.PrepareAsync();
        receipt = await fixture.ActivateAsync(receipt.Data, 0);
        receipt = await fixture.ActivateAsync(receipt.Data, 1);
        receipt = await fixture.FenceAsync(receipt.Data);
        if (revoked)
            await fixture.RevokeAsync();
        using (var request = fixture.Request(HttpMethod.Get,
                   $"{BaseUrl}/{receipt.Data.OperationId}/cleanup-targets", null, authenticated: revoked))
        using (var response = await fixture.Client.SendAsync(request))
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        using (var request = fixture.Request(HttpMethod.Post, $"{BaseUrl}/cleanup",
                   new AuthenticationCredentialCleanupInputDto
                   {
                       OperationId = receipt.Data.OperationId,
                       DeploymentRevision = receipt.Data.DeploymentRevision,
                       ExpectedHeadStamp = receipt.Data.CurrentHeadStamp!.Value,
                       ExpectedOperationStamp = receipt.Data.ConcurrencyStamp,
                       ApprovedSettingIds = [],
                       ApprovedBindingIds = []
                   }, authenticated: revoked))
        using (var response = await fixture.Client.SendAsync(request))
            await Assert.That(response.StatusCode).IsEqualTo(
                revoked ? HttpStatusCode.Forbidden : HttpStatusCode.Unauthorized);
        if (revoked)
            await fixture.GrantAsync();
        await AssertSameReceiptAsync(receipt.Data, (await fixture.CurrentAsync()).Data);
        await fixture.AssertNoGenericReplayAsync();
    }

    [Test]
    public async Task CleanupApprovalRejectsMalformedAndExpandedTargetsWithoutMovingReceipt()
    {
        await using var fixture = await Fixture.CreateAsync();
        var receipt = await fixture.PrepareAsync();
        receipt = await fixture.ActivateAsync(receipt.Data, 0);
        receipt = await fixture.ActivateAsync(receipt.Data, 1);
        receipt = await fixture.FenceAsync(receipt.Data);
        foreach (string field in new[] { "approvedSettingIds", "approvedBindingIds" })
            foreach (string mutation in new[] { "omitted", "null", "empty-id", "duplicate", "three", "foreign" })
            {
                var body = new Dictionary<string, object?>
                {
                    ["operationId"] = receipt.Data.OperationId,
                    ["deploymentRevision"] = receipt.Data.DeploymentRevision,
                    ["expectedHeadStamp"] = receipt.Data.CurrentHeadStamp,
                    ["expectedOperationStamp"] = receipt.Data.ConcurrencyStamp,
                    ["approvedSettingIds"] = Array.Empty<Guid>(),
                    ["approvedBindingIds"] = Array.Empty<Guid>()
                };
                Guid id = Guid.CreateVersion7();
                switch (mutation)
                {
                    case "omitted": body.Remove(field); break;
                    case "null": body[field] = null; break;
                    case "empty-id": body[field] = new[] { Guid.Empty }; break;
                    case "duplicate": body[field] = new[] { id, id }; break;
                    case "three": body[field] = new[] { id, Guid.CreateVersion7(), Guid.CreateVersion7() }; break;
                    case "foreign": body[field] = new[] { id }; break;
                    default: throw new ArgumentOutOfRangeException(nameof(mutation));
                }
                using var request = fixture.Request(HttpMethod.Post, $"{BaseUrl}/cleanup", body);
                using var response = await fixture.Client.SendAsync(request);
                await Assert.That(response.StatusCode).IsEqualTo(
                    mutation == "foreign" ? HttpStatusCode.Conflict : HttpStatusCode.BadRequest);
                await AssertPrivateAsync(response);
                await AssertSameReceiptAsync(receipt.Data, (await fixture.CurrentAsync()).Data);
            }
        await fixture.AssertNoGenericReplayAsync();
    }

    [Test]
    public async Task CommittedCleanupRetryStillRejectsRevokedAdministrator()
    {
        await using var fixture = await Fixture.CreateAsync();
        var receipt = await fixture.PrepareAsync();
        receipt = await fixture.ActivateAsync(receipt.Data, 0);
        receipt = await fixture.ActivateAsync(receipt.Data, 1);
        receipt = await fixture.FenceAsync(receipt.Data);
        var input = new AuthenticationCredentialCleanupInputDto
        {
            OperationId = receipt.Data.OperationId,
            DeploymentRevision = receipt.Data.DeploymentRevision,
            ExpectedHeadStamp = receipt.Data.CurrentHeadStamp!.Value,
            ExpectedOperationStamp = receipt.Data.ConcurrencyStamp,
            ApprovedSettingIds = [],
            ApprovedBindingIds = []
        };
        using (var request = fixture.Request(HttpMethod.Post, $"{BaseUrl}/cleanup", input))
        using (var response = await fixture.Client.SendAsync(request))
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var cleaned = await fixture.CurrentAsync();
        await fixture.RevokeAsync();
        using (var request = fixture.Request(HttpMethod.Post, $"{BaseUrl}/cleanup", input))
        using (var response = await fixture.Client.SendAsync(request))
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        await fixture.GrantAsync();
        await AssertSameReceiptAsync(cleaned.Data, (await fixture.CurrentAsync()).Data);
        await fixture.AssertNoGenericReplayAsync();
    }

    private static AuthenticationCredentialActivationInputDto Activation(AuthenticationCredentialCutoverDto receipt, int index)
    {
        var member = receipt.RequiredReplacements[index];
        return new(receipt.OperationId, receipt.DeploymentRevision, member.ReplicaId, member.ProcessBootId,
            member.Consumer, member.Generation, receipt.CurrentHeadStamp!.Value, receipt.ConcurrencyStamp);
    }

    private static AuthenticationCredentialFenceInputDto Fence(AuthenticationCredentialCutoverDto receipt) =>
        new(receipt.OperationId, receipt.DeploymentRevision, receipt.CurrentHeadStamp!.Value,
            receipt.ConcurrencyStamp, true, true, true);

    private static async Task AssertPrivateAsync(HttpResponseMessage response)
    {
        await Assert.That(response.Headers.CacheControl?.Private == true).IsTrue();
        await Assert.That(response.Headers.CacheControl?.NoStore == true).IsTrue();
    }

    private static async Task AssertActionsAsync(HalResource<AuthenticationCredentialCutoverDto> receipt,
        bool report, bool fence, bool cleanup = false)
    {
        await Assert.That(receipt.Links.ContainsKey(LinkRelations.Self)).IsTrue();
        await Assert.That(receipt.Links.ContainsKey(LinkRelations.ReportAuthenticationCredentialActivation)).IsEqualTo(report);
        await Assert.That(receipt.Links.ContainsKey(LinkRelations.AttestAuthenticationCredentialFence)).IsEqualTo(fence);
        var expected = new List<string> { LinkRelations.Self };
        if (report)
            expected.Add(LinkRelations.ReportAuthenticationCredentialActivation);
        if (fence)
            expected.Add(LinkRelations.AttestAuthenticationCredentialFence);
        if (cleanup)
        {
            expected.Add(LinkRelations.InspectAuthenticationCredentialCleanup);
            expected.Add(LinkRelations.CleanupAuthenticationCredentials);
        }
        await Assert.That(receipt.Links.Keys).IsEquivalentTo(expected);
    }

    private static async Task AssertSameReceiptAsync(AuthenticationCredentialCutoverDto expected, AuthenticationCredentialCutoverDto actual)
    {
        await Assert.That(actual.OperationId).IsEqualTo(expected.OperationId);
        await Assert.That(actual.InstanceId).IsEqualTo(expected.InstanceId);
        await Assert.That(actual.CreatedBy).IsEqualTo(expected.CreatedBy);
        await Assert.That(actual.CreatedAt).IsEqualTo(expected.CreatedAt);
        await Assert.That(actual.Deadline).IsEqualTo(expected.Deadline);
        await Assert.That(actual.UpdatedAt).IsEqualTo(expected.UpdatedAt);
        await Assert.That(actual.DeploymentRevision).IsEqualTo(expected.DeploymentRevision);
        await Assert.That(actual.Authority).IsEqualTo(expected.Authority);
        await Assert.That(actual.AuthorityProfileRevision).IsEqualTo(expected.AuthorityProfileRevision);
        await Assert.That(actual.State).IsEqualTo(expected.State);
        await Assert.That(actual.ConcurrencyStamp).IsEqualTo(expected.ConcurrencyStamp);
        await Assert.That(actual.CurrentHeadStamp).IsEqualTo(expected.CurrentHeadStamp);
        await Assert.That(actual.RequiredReplacements).IsEquivalentTo(expected.RequiredReplacements);
        await Assert.That(actual.Activations).IsEquivalentTo(expected.Activations);
        await Assert.That(actual.FenceAttested).IsEqualTo(expected.FenceAttested);
        await Assert.That(actual.CleanedSettingCount).IsEqualTo(expected.CleanedSettingCount);
        await Assert.That(actual.CleanedBindingCount).IsEqualTo(expected.CleanedBindingCount);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public CutoverFactory Factory { get; } = new();
        public HttpClient Client { get; private set; } = null!;
        public Guid UserId { get; } = Guid.CreateVersion7();
        public Guid InstanceId { get; private set; }
        private readonly List<string> _replayKeys = [];
        public PrepareAuthenticationCredentialCutoverInputDto Input { get; } = new()
        {
            DeploymentRevision = Guid.CreateVersion7(),
            RequiredReplacements =
            [
                new(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
                    AuthenticationCredentialCutover.CredentialConsumer.GoogleOidc, Guid.CreateVersion7()),
                new(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
                    AuthenticationCredentialCutover.CredentialConsumer.KeycloakOidc, Guid.CreateVersion7())
            ]
        };

        public static async Task<Fixture> CreateAsync()
        {
            var fixture = new Fixture();
            fixture.Client = fixture.Factory.CreateClient();
            using var scope = fixture.Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            db.Users.Add(new User
            {
                Id = fixture.UserId,
                CreatedAt = Now.UtcDateTime,
                CreatedBy = fixture.UserId,
                Pii = new UserPii
                {
                    UserId = fixture.UserId,
                    Email = $"{fixture.UserId:N}@integration.test",
                    FirstName = "Cutover",
                    LastName = "Administrator"
                }
            });
            db.UserExternalLogins.Add(new UserExternalLogin
            {
                Id = Guid.CreateVersion7(),
                UserId = fixture.UserId,
                User = null!,
                AuthenticationProviderId = (int)AuthenticationProviderKind.Keycloak,
                AuthenticationProvider = null!,
                ProviderKey = PlatformIdentityPrincipalExtensions.CreateOidcAccountKey(
                    OnboardingWebApplicationFactory.Issuer, fixture.UserId.ToString()).Value,
                ProviderDisplayName = "keycloak",
                CreatedAt = Now.UtcDateTime,
                CreatedBy = fixture.UserId
            });
            var bootstrap = InstanceBootstrapState.CreateInteractivePending(
                Guid.CreateVersion7(), DeploymentMode.SingleTenant, Now.UtcDateTime);
            bootstrap.CompleteInteractive(fixture.UserId, Now.UtcDateTime);
            db.InstanceBootstrapStates.Add(bootstrap);
            fixture.InstanceId = bootstrap.Id;
            await db.SaveChangesAsync();
            await fixture.GrantAsync();
            return fixture;
        }

        public HttpRequestMessage Request(HttpMethod method, string path, object? body = null, bool authenticated = true)
        {
            var request = new HttpRequestMessage(method, path);
            if (authenticated)
                request.Headers.Add(TestAuthHandler.AuthHeaderName, TestAuthHandler.CreateAuthHeaderValue(
                    UserId, "Cutover Administrator", ("iss", OnboardingWebApplicationFactory.Issuer), ("idp", "keycloak")));
            if (body is not null)
                request.Content = JsonContent.Create(body, body.GetType(), options: TestJsonOptions.Default);
            if (method == HttpMethod.Post)
            {
                string key = Guid.CreateVersion7().ToString("N");
                _replayKeys.Add(key);
                request.Headers.Add("Idempotency-Key", key);
            }
            return request;
        }

        public Task<HttpResponseMessage> SendAsync(string route, AuthenticationCredentialCutoverDto? receipt = null,
            bool authenticated = true)
        {
            var method = route is "current" or "receipt" ? HttpMethod.Get : HttpMethod.Post;
            var path = route switch
            {
                "prepare" => BaseUrl,
                "receipt" => $"{BaseUrl}/{receipt?.OperationId ?? Guid.CreateVersion7()}",
                _ => $"{BaseUrl}/{route}"
            };
            object? body = route switch
            {
                "prepare" => receipt is null ? Input : Input with
                {
                    ExpectedCurrentOperationId = receipt.OperationId,
                    ExpectedHeadStamp = receipt.CurrentHeadStamp,
                    ExpectedPredecessorStamp = receipt.ConcurrencyStamp
                },
                "activations" => receipt is null
                    ? new AuthenticationCredentialActivationInputDto(Guid.CreateVersion7(), Input.DeploymentRevision,
                        Input.RequiredReplacements[0].ReplicaId, Input.RequiredReplacements[0].ProcessBootId,
                        Input.RequiredReplacements[0].Consumer, Input.RequiredReplacements[0].Generation,
                        Guid.CreateVersion7(), Guid.CreateVersion7()) : Activation(receipt, 0),
                "fence" => receipt is null
                    ? new AuthenticationCredentialFenceInputDto(Guid.CreateVersion7(), Input.DeploymentRevision,
                        Guid.CreateVersion7(), Guid.CreateVersion7(), true, true, true) : Fence(receipt),
                _ => null
            };
            return SendRequestAsync(method, path, body, authenticated);
        }

        private async Task<HttpResponseMessage> SendRequestAsync(HttpMethod method, string path, object? body, bool authenticated)
        {
            using var request = Request(method, path, body, authenticated);
            return await Client.SendAsync(request);
        }

        public Task<HalResource<AuthenticationCredentialCutoverDto>> PrepareAsync(
            PrepareAuthenticationCredentialCutoverInputDto? input = null) =>
            ReadAsync(HttpMethod.Post, BaseUrl, input ?? Input);
        public Task<HalResource<AuthenticationCredentialCutoverDto>> CurrentAsync() =>
            ReadAsync(HttpMethod.Get, $"{BaseUrl}/current");
        public Task<HalResource<AuthenticationCredentialCutoverDto>> ByIdAsync(Guid id) =>
            ReadAsync(HttpMethod.Get, $"{BaseUrl}/{id}");
        public Task<HalResource<AuthenticationCredentialCutoverDto>> ActivateAsync(AuthenticationCredentialCutoverDto receipt, int index) =>
            ReadAsync(HttpMethod.Post, $"{BaseUrl}/activations", Activation(receipt, index));
        public Task<HalResource<AuthenticationCredentialCutoverDto>> FenceAsync(AuthenticationCredentialCutoverDto receipt) =>
            ReadAsync(HttpMethod.Post, $"{BaseUrl}/fence", Fence(receipt));

        private async Task<HalResource<AuthenticationCredentialCutoverDto>> ReadAsync(HttpMethod method, string path, object? body = null)
        {
            using var request = Request(method, path, body);
            using var response = await Client.SendAsync(request);
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await AssertPrivateAsync(response);
            string json = await response.Content.ReadAsStringAsync();
            // Assert booleans, never secret-bearing strings: failure diagnostics must remain value-free.
            await Assert.That(Factory.Canaries.Any(value => json.Contains(value, StringComparison.Ordinal))).IsFalse();
            return JsonSerializer.Deserialize<HalResource<AuthenticationCredentialCutoverDto>>(json, TestJsonOptions.Default)!;
        }

        public async Task AssertNoCurrentAsync(bool reauthorize = false)
        {
            if (reauthorize)
                await GrantAsync();
            using var request = Request(HttpMethod.Get, $"{BaseUrl}/current");
            using var response = await Client.SendAsync(request);
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
            await AssertPrivateAsync(response);
            await Assert.That((await response.Content.ReadAsByteArrayAsync()).Length).IsEqualTo(0);
        }

        public async Task RevokeAsync()
        {
            using var scope = Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            db.PlatformUserRoles.RemoveRange(db.PlatformUserRoles.Where(role => role.UserId == UserId));
            await db.SaveChangesAsync();
        }

        public async Task GrantAsync()
        {
            using var scope = Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            var role = await db.Roles.SingleAsync(candidate => candidate.Id == (int)RoleEnum.Admin);
            await Assert.That(role.MasterCode).IsEqualTo("platform.admin");
            await Assert.That(role.RoleScopeId).IsEqualTo((int)RoleScopeEnum.Platform);
            db.PlatformUserRoles.Add(new PlatformUserRole
            {
                Id = Guid.CreateVersion7(),
                UserId = UserId,
                User = null!,
                RoleId = role.Id,
                Role = role,
                GrantedBy = UserId,
                GrantedAt = Now.UtcDateTime
            });
            await db.SaveChangesAsync();
        }

        public async Task<AuthenticationCredentialCutover> SeedForeignAsync()
        {
            using var scope = Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            var foreign = AuthenticationCredentialCutover.Create(Guid.CreateVersion7(), Guid.CreateVersion7(),
                UserId, Now, Now.AddMinutes(15), Guid.CreateVersion7(), Factory.Profile.Current.Authority,
                Factory.Profile.Current.Revision, Input.RequiredReplacements.Select(member =>
                    new AuthenticationCredentialCutover.Replacement(member.ReplicaId, member.PreviousProcessBootId,
                        member.ProcessBootId, member.Consumer, member.Generation)));
            await using var transaction = await db.Database.BeginTransactionAsync();
            var repository = scope.ServiceProvider.GetRequiredService<IAuthenticationCredentialCutoverRepository>();
            await repository.AddAsync(foreign);
            await repository.AddHeadAsync(AuthenticationCredentialCutoverHead.Create(foreign));
            await transaction.CommitAsync();
            return foreign;
        }

        public async Task AssertNoGenericReplayAsync()
        {
            using var scope = Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            await Assert.That(await db.IdempotencyRecords.AsNoTracking().IgnoreQueryFilters()
                .CountAsync(record => _replayKeys.Contains(record.Key))).IsEqualTo(0);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await Factory.DisposeAsync();
        }
    }

    private sealed class CutoverFactory : OnboardingWebApplicationFactory
    {
        public ProfileReader Profile { get; } = new();
        public ControlledClock Clock { get; } = new();
        public string[] Canaries { get; } =
        [
            Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
            Convert.ToHexString(RandomNumberGenerator.GetBytes(32))
        ];

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IAuthenticationCredentialProfileReader>();
                services.AddSingleton<IAuthenticationCredentialProfileReader>(Profile);
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(Clock);
                var secrets = Substitute.For<ISecretResolver>();
                var values = new Dictionary<string, string>
                {
                    [SecretDefinitionRegistry.Keys.Authentication.GoogleClientSecret] = Canaries[0],
                    [SecretDefinitionRegistry.Keys.Keycloak.BlazorClientSecret] = Canaries[1]
                };
                secrets.ResolveAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
                    .Returns(call => call.ArgAt<Guid?>(1) is null
                        && values.TryGetValue(call.ArgAt<string>(0), out string? value)
                        ? SecretResolutionResult.Resolved(new ResolvedSecret(
                            call.ArgAt<string>(0), value, SecretSourceType.EnvironmentVariable,
                            SecretScope.Instance, null, Clock.GetUtcNow()))
                        : SecretResolutionResult.Unconfigured);
                services.RemoveAll<ISecretResolver>();
                services.AddSingleton(secrets);
            });
        }
    }

    private sealed class ProfileReader : IAuthenticationCredentialProfileReader
    {
        public AuthenticationCredentialProfile Current { get; set; } =
            new(AuthenticationCredentialCutover.CredentialAuthority.Environment, Guid.CreateVersion7());
        public AuthenticationCredentialProfile ReadCurrent() => Current;
    }

    private sealed class ControlledClock : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = Now;
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
