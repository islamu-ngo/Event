using Explore.Domain;
using Explore.Infrastructure.Configuration;
using Explore.Infrastructure.HealthChecks;
using Explore.Infrastructure.Tests.Fixtures;
using Explore.Infrastructure.Webhooks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Svix;
using Svix.Models;
using TUnit.Core;

namespace Explore.Infrastructure.Tests.Infrastructure.Webhooks;

[Category(InfrastructureTestCategories.Runtime)]
[Explicit]
[ClassDataSource<SvixConformanceContainerFixture>(Shared = SharedType.PerClass)]
[NotInParallel("SvixVersionConformance")]
public sealed class SvixVersionConformanceTests(SvixConformanceContainerFixture svix)
{
    private const string EventType = "islamu.conformance";

    [Test]
    public async Task SelfHostedProfile_ExecutesCompleteLiveMatrix()
    {
        var executedCases = new List<string>();

        await VerifyPinnedVersionAsync();
        executedCases.Add("pinned-version");
        await VerifyRepeatCreateInsideWindowAsync();
        executedCases.Add("repeat-create-inside-window");
        await VerifyCreateAfterWindowExpiryAsync();
        executedCases.Add("create-after-window-expiry");
        await VerifyDuplicateEventIdentityAsync();
        executedCases.Add("duplicate-event-identity");
        await VerifyAcceptanceTimeoutAsync();
        executedCases.Add("acceptance-timeout");
        await VerifyCredentialRotationAsync();
        executedCases.Add("credential-rotation");
        await VerifyListAndGetConsistencyAsync();
        executedCases.Add("list-get-consistency");
        await VerifyEndpointManagementAsync();
        executedCases.Add("endpoint-management");
        await VerifyAppPortalAsync();
        executedCases.Add("app-portal");
        await VerifyEventCatalogAsync();
        executedCases.Add("event-catalog");
        await VerifyPayloadInspectionAsync();
        executedCases.Add("payload-inspection");

        var profile = SvixConformanceProfileRegistry.Supported.Single();
        await Assert.That(executedCases).Count().IsEqualTo(profile.ExecutedTestCount);
        await Assert.That(executedCases).DoesNotContain(string.Empty);
        await Assert.That(profile.SupportsExactMessageLookup).IsFalse();
        await Assert.That(profile.Capabilities).IsEqualTo(
            WebhookProviderCapability.EndpointManagement |
            WebhookProviderCapability.PayloadInspection |
            WebhookProviderCapability.AppPortal |
            WebhookProviderCapability.EventCatalog);
    }

    [Test]
    public async Task UnsupportedAndUnprovenProfiles_FailClosed()
    {
        var policy = new ConformanceBackedWebhookProviderReconciliationCapabilityPolicy();
        var validator = new WebhookOptionsValidator();
        var unsupported = validator.Validate(null, new WebhookOptions
        {
            Provider = WebhookOptions.ProviderSvix,
            Svix = SelfHostedOptions(providerVersion: "unsupported")
        });
        var unprovenManaged = validator.Validate(null, new WebhookOptions
        {
            Provider = WebhookOptions.ProviderSvix,
            Svix = new WebhookSvixOptions
            {
                BaseUrl = null,
                Environment = SvixConformanceProfileRegistry.ManagedEnvironment,
                ProviderVersion = SvixConformanceProfileRegistry.ManagedProviderVersion,
                CapabilityPolicyVersion = SvixConformanceProfileRegistry.ManagedCapabilityPolicyVersion
            }
        });

        await Assert.That(unsupported.Succeeded).IsFalse();
        await Assert.That(unsupported.FailureMessage).Contains("not present in the conformance matrix");
        await Assert.That(unprovenManaged.Succeeded).IsFalse();
        await Assert.That(unprovenManaged.FailureMessage).Contains("no executed conformance evidence");
        await Assert.That(policy.SupportsExactMessageLookup(
            Explore.Domain.WebhookProviderKind.Svix,
            SvixConformanceProfileRegistry.SelfHostedProviderVersion,
            SvixConformanceProfileRegistry.SelfHostedEnvironment)).IsFalse();
        await Assert.That(policy.SupportsExactMessageLookup(
            Explore.Domain.WebhookProviderKind.Svix,
            SvixConformanceProfileRegistry.ManagedProviderVersion,
            SvixConformanceProfileRegistry.ManagedEnvironment)).IsFalse();
    }

    private async Task VerifyPinnedVersionAsync()
    {
        var version = await svix.GetServerVersionAsync();

        await Assert.That(version)
            .IsEqualTo($"svix-server {SvixConformanceProfileRegistry.SelfHostedProviderVersion}");
    }

    private async Task VerifyRepeatCreateInsideWindowAsync()
    {
        var client = svix.CreateClient();
        var app = await CreateApplicationAsync(client);
        var idempotencyKey = NewIdentity("inside");
        MessageOut first;
        try
        {
            first = await CreateMessageAsync(
                client,
                app.Id,
                NewIdentity("event"),
                "{\"value\":1}",
                idempotencyKey);
        }
        catch (ApiException exception)
        {
            throw new InvalidOperationException(
                $"Svix rejected the baseline conformance message: {exception.ErrorContent}",
                exception);
        }
        var replay = await CreateMessageAsync(
            client,
            app.Id,
            NewIdentity("changed-event"),
            "{\"value\":2}",
            idempotencyKey);

        await Assert.That(replay.Id).IsEqualTo(first.Id);
        await Assert.That(replay.EventId).IsEqualTo(first.EventId);
    }

    private async Task VerifyCreateAfterWindowExpiryAsync()
    {
        var client = svix.CreateClient();
        var app = await CreateApplicationAsync(client);
        var idempotencyKey = NewIdentity("expired");
        var first = await CreateMessageAsync(
            client,
            app.Id,
            NewIdentity("before-expiry"),
            "{\"value\":1}",
            idempotencyKey);

        await svix.ExpireIdempotencyCacheAsync();
        var afterExpiry = await CreateMessageAsync(
            client,
            app.Id,
            NewIdentity("after-expiry"),
            "{\"value\":2}",
            idempotencyKey);

        await Assert.That(afterExpiry.Id).IsNotEqualTo(first.Id);
        await Assert.That(afterExpiry.EventId).IsNotEqualTo(first.EventId);
    }

    private async Task VerifyDuplicateEventIdentityAsync()
    {
        var client = svix.CreateClient();
        var app = await CreateApplicationAsync(client);
        var eventId = NewIdentity("duplicate-event");
        await CreateMessageAsync(client, app.Id, eventId, "{\"value\":1}", NewIdentity("idem"));

        var samePayload = await Assert.ThrowsAsync<ApiException>(() => CreateMessageAsync(
            client,
            app.Id,
            eventId,
            "{\"value\":1}",
            NewIdentity("same-payload")));
        var changedPayload = await Assert.ThrowsAsync<ApiException>(() => CreateMessageAsync(
            client,
            app.Id,
            eventId,
            "{\"value\":2}",
            NewIdentity("changed-payload")));

        await Assert.That(samePayload.ErrorCode).IsEqualTo(409);
        await Assert.That(changedPayload.ErrorCode).IsEqualTo(409);
    }

    private async Task VerifyAcceptanceTimeoutAsync()
    {
        var directClient = svix.CreateClient();
        var app = await CreateApplicationAsync(directClient);
        var eventId = NewIdentity("acceptance-timeout");
        await using var proxy = await SvixAcceptThenDropProxy.StartAsync(new Uri(svix.ServerUrl));
        var proxyClient = svix.CreateClient(serverUrl: proxy.ServerUrl);
        Exception? transportFailure = null;

        try
        {
            await CreateMessageAsync(
                proxyClient,
                app.Id,
                eventId,
                "{\"value\":1}",
                NewIdentity("acceptance-timeout"));
        }
        catch (Exception exception)
        {
            transportFailure = exception;
        }

        await proxy.Forwarded;
        var listed = await directClient.Message.ListAsync(
            app.Id,
            new MessageListOptions
            {
                Limit = 100,
                EventTypes = [EventType],
                WithContent = false
            },
            CancellationToken.None);

        await Assert.That(transportFailure).IsNotNull();
        await Assert.That(listed.Data!.Count(message => message.EventId == eventId)).IsEqualTo(1);
    }

    private async Task VerifyCredentialRotationAsync()
    {
        var firstClient = svix.CreateClient();
        var app = await CreateApplicationAsync(firstClient);
        var idempotencyKey = NewIdentity("credential-rotation");
        var first = await CreateMessageAsync(
            firstClient,
            app.Id,
            NewIdentity("first-token"),
            "{\"value\":1}",
            idempotencyKey);
        var rotatedToken = await svix.RotateAuthTokenAsync();
        var rotatedClient = svix.CreateClient(rotatedToken);
        var second = await CreateMessageAsync(
            rotatedClient,
            app.Id,
            NewIdentity("rotated-token"),
            "{\"value\":1}",
            idempotencyKey);

        await Assert.That(second.Id).IsNotEqualTo(first.Id);
    }

    private async Task VerifyListAndGetConsistencyAsync()
    {
        var client = svix.CreateClient();
        var app = await CreateApplicationAsync(client);
        var eventId = NewIdentity("lookup");
        var tag = NewIdentity("evidence");
        var created = await CreateMessageAsync(
            client,
            app.Id,
            eventId,
            "{\"value\":1}",
            NewIdentity("lookup"),
            [tag]);
        var listed = await client.Message.ListAsync(
            app.Id,
            new MessageListOptions
            {
                Limit = 100,
                EventTypes = [EventType],
                WithContent = false,
                After = DateTime.UtcNow.AddMinutes(-5),
                Before = DateTime.UtcNow.AddMinutes(5)
            },
            CancellationToken.None);
        var match = listed.Data!.Single(message => message.EventId == eventId);
        var fetched = await client.Message.GetAsync(
            app.Id,
            created.Id,
            new MessageGetOptions { WithContent = false },
            CancellationToken.None);

        await Assert.That(match.Id).IsEqualTo(created.Id);
        await Assert.That(match.EventType).IsEqualTo(EventType);
        await Assert.That(match.EventId).IsEqualTo(eventId);
        await Assert.That(match.Timestamp).IsEqualTo(created.Timestamp);
        await Assert.That(fetched.Id).IsEqualTo(created.Id);
        await Assert.That(fetched.EventId).IsEqualTo(eventId);
        await Assert.That(created.Tags).IsNull();
        await Assert.That(match.Tags).IsNull();
        await Assert.That(fetched.Tags).IsNull();
    }

    private async Task VerifyEndpointManagementAsync()
    {
        var client = svix.CreateClient();
        var app = await CreateApplicationAsync(client);
        var uid = NewIdentity("endpoint");
        var created = await client.Endpoint.CreateAsync(
            app.Id,
            new EndpointIn
            {
                Uid = uid,
                Url = $"https://example.invalid/webhooks/{uid}",
                Description = "ISLAMU conformance endpoint"
            },
            new EndpointCreateOptions { IdempotencyKey = NewIdentity("endpoint-idem") },
            CancellationToken.None);
        var fetched = await client.Endpoint.GetAsync(app.Id, created.Id, CancellationToken.None);
        var listed = await client.Endpoint.ListAsync(
            app.Id,
            new EndpointListOptions { Limit = 100 },
            CancellationToken.None);

        await Assert.That(fetched.Uid).IsEqualTo(uid);
        await Assert.That(listed.Data!.Any(endpoint => endpoint.Id == created.Id)).IsTrue();
        await Assert.That(await client.Endpoint.DeleteAsync(app.Id, created.Id, CancellationToken.None)).IsTrue();
    }

    private async Task VerifyAppPortalAsync()
    {
        var client = svix.CreateClient();
        var app = await CreateApplicationAsync(client);
        var access = await client.Authentication.AppPortalAccessAsync(
            app.Id,
            new AppPortalAccessIn
            {
                SessionId = NewIdentity("portal-session"),
                ReadOnly = false,
                Expiry = 60,
                FeatureFlags = ["ViewBase", "ManageEndpoint"]
            },
            new AuthenticationAppPortalAccessOptions { IdempotencyKey = NewIdentity("portal-idem") },
            CancellationToken.None);

        await Assert.That(access.Url).IsNotNull().And.IsNotEmpty();
        await Assert.That(access.Token).IsNotNull().And.IsNotEmpty();
    }

    private async Task VerifyEventCatalogAsync()
    {
        var client = svix.CreateClient();
        var name = $"islamu.conformance.{Guid.CreateVersion7():N}";
        var created = await client.EventType.CreateAsync(
            new EventTypeIn
            {
                Name = name,
                Description = "ISLAMU conformance event type",
                GroupName = "ISLAMU conformance"
            },
            new EventTypeCreateOptions { IdempotencyKey = NewIdentity("event-type-idem") },
            CancellationToken.None);
        var fetched = await client.EventType.GetAsync(name, CancellationToken.None);

        await Assert.That(created.Name).IsEqualTo(name);
        await Assert.That(fetched.Name).IsEqualTo(name);
        await Assert.That(await client.EventType.DeleteAsync(name, new EventTypeDeleteOptions(), CancellationToken.None)).IsTrue();
    }

    private async Task VerifyPayloadInspectionAsync()
    {
        var client = svix.CreateClient();
        var app = await CreateApplicationAsync(client);
        var created = await CreateMessageAsync(
            client,
            app.Id,
            NewIdentity("payload-inspection"),
            "{\"value\":42}",
            NewIdentity("payload-inspection-idem"));
        var fetched = await client.Message.GetAsync(
            app.Id,
            created.Id,
            new MessageGetOptions { WithContent = true },
            CancellationToken.None);

        await Assert.That(fetched.Payload).IsNotNull();
        await Assert.That(fetched.EventId).IsEqualTo(created.EventId);
    }

    private async Task<ApplicationOut> CreateApplicationAsync(SvixClient client)
    {
        var uid = NewIdentity("app");
        return await client.Application.GetOrCreateAsync(
            new ApplicationIn
            {
                Name = "ISLAMU Svix conformance",
                Uid = uid
            },
            new ApplicationCreateOptions { IdempotencyKey = NewIdentity("app-idem") },
            CancellationToken.None);
    }

    private static Task<MessageOut> CreateMessageAsync(
        SvixClient client,
        string applicationId,
        string eventId,
        string payloadJson,
        string idempotencyKey,
        List<string>? tags = null)
    {
        var message = Message.messageInRaw(
            EventType,
            payloadJson,
            "application/json",
            application: null,
            channels: null,
            eventId: eventId,
            payloadRetentionHours: null,
            payloadRetentionPeriod: 5,
            tags: tags,
            transformationsParams: null);
        return client.Message.CreateAsync(
            applicationId,
            message,
            new MessageCreateOptions { IdempotencyKey = idempotencyKey },
            CancellationToken.None);
    }

    private static WebhookSvixOptions SelfHostedOptions(string? providerVersion = null) =>
        new()
        {
            BaseUrl = "http://svix:8071",
            Environment = SvixConformanceProfileRegistry.SelfHostedEnvironment,
            ProviderVersion = providerVersion ?? SvixConformanceProfileRegistry.SelfHostedProviderVersion,
            CapabilityPolicyVersion = SvixConformanceProfileRegistry.SelfHostedCapabilityPolicyVersion
        };

    private static string NewIdentity(string prefix) => $"{prefix}-{Guid.CreateVersion7():N}";
}
