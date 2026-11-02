using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Diagnostics;
using System.Data.Common;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.DTOs.Event;
using Explore.Application.Exceptions;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Event.Api.IntegrationTests.Features;

public sealed class LocalEventManagementHttpTests
{
    [Test]
    public async Task LostConnectorAfterDraftCommitPreservesOutcomeEvidenceWithoutReplayingCreation()
    {
        using var fault = new LoseCommittedDraftConnector();
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await JoinTenantAsync(fixture);
        string token = await fixture.SignInAsync();
        fault.Armed = true;
        using var response = await fixture.PostAuthorizedAsync("/api/event", new CreateEventDraftRequestDto
        {
            Title = "Committed draft custody regression",
            ParticipationConfiguration = new()
            {
                ParticipationHandlingModeId = (int)ParticipationHandlingModeEnum.InformationOnly,
                AdvanceRegistrationObligationId = (int)AdvanceRegistrationObligationEnum.NotApplicable
            },
            VisibilityTypeId = (int)VisibilityTypeEnum.Private,
            EventFormatId = 1,
            Timezone = "UTC"
        }, token);

        await Assert.That(fault.CommitCount).IsEqualTo(1)
            .Because($"owner={fault.OwnerObserved}; commands={fault.CommandsObserved}; commits={fault.CommitsObserved}");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.InternalServerError);
        await Assert.That(fixture.RequestFailure is HumanMutationOutcomeUnknownException
            or TransactionCommittedCleanupException).IsTrue().Because(fixture.FailureMetadata);
        await Assert.That(ContainsFailure(fixture.RequestFailure, fault.Failure)).IsTrue();

        using var read = new HttpRequestMessage(HttpMethod.Get, "/api/event/my?pageNumber=1&pageSize=50");
        read.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var persisted = await fixture.Client.SendAsync(read, LocalIdentityLifecycleHttpFixture.CancellationToken);
        await Assert.That(persisted.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var body = System.Text.Json.JsonDocument.Parse(
            await persisted.Content.ReadAsStringAsync(LocalIdentityLifecycleHttpFixture.CancellationToken));
        await Assert.That(body.RootElement.GetProperty("totalCount").GetInt32()).IsEqualTo(1);
    }

    private static bool ContainsFailure(Exception? observed, Exception expected) =>
        ReferenceEquals(observed, expected)
        || observed is AggregateException aggregate && aggregate.InnerExceptions.Any(cause => ContainsFailure(cause, expected))
        || observed?.InnerException is { } nested && ContainsFailure(nested, expected);

    private sealed class LoseCommittedDraftConnector : IObserver<DiagnosticListener>,
        IObserver<KeyValuePair<string, object?>>, IDisposable
    {
        private readonly IDisposable _listeners;
        private readonly List<IDisposable> _subscriptions = [];
        private ExploreDbContext? _owner;
        private DbTransaction? _physicalTransaction;
        private bool _disposed;
        private int _commitCount;
        internal LoseCommittedDraftConnector() => _listeners = DiagnosticListener.AllListeners.Subscribe(this);
        internal bool Armed { get; set; }
        internal int CommitCount => Volatile.Read(ref _commitCount);
        internal bool OwnerObserved => _owner is not null;
        internal int CommandsObserved { get; private set; }
        internal int CommitsObserved { get; private set; }
        internal IOException Failure { get; } = new("Synthetic draft commit transport failure.");

        public void OnNext(DiagnosticListener listener)
        {
            if (listener.Name == DbLoggerCategory.Name)
                lock (_subscriptions)
                    if (!_disposed) _subscriptions.Add(listener.Subscribe(this));
        }

        public void OnNext(KeyValuePair<string, object?> notification)
        {
            if (!Armed) return;
            if (notification.Key == RelationalEventId.CommandExecuting.Name) CommandsObserved++;
            if (notification.Key == RelationalEventId.TransactionCommitted.Name) CommitsObserved++;
            if (notification.Key == RelationalEventId.CommandExecuting.Name
                && notification.Value is CommandEventData { Context: ExploreDbContext context } command
                && command.Command.Transaction is { } transaction
                && command.Command.Parameters.Cast<DbParameter>().Any(parameter =>
                    parameter.Value is string value && value == "Committed draft custody regression"))
            {
                _owner ??= context;
                _physicalTransaction ??= transaction;
            }
            if (notification.Key == RelationalEventId.TransactionCommitted.Name
                && notification.Value is TransactionEndEventData committed
                && _owner is not null && ReferenceEquals(committed.Transaction, _physicalTransaction)
                && Interlocked.CompareExchange(ref _commitCount, 1, 0) == 0)
            {
                _owner.Database.GetDbConnection().Close();
                throw Failure;
            }
        }

        public void OnError(Exception error) => throw new InvalidOperationException("Draft settlement observation failed.", error);
        public void OnCompleted() { }
        public void Dispose()
        {
            _listeners.Dispose();
            lock (_subscriptions)
            {
                _disposed = true;
                foreach (var subscription in _subscriptions) subscription.Dispose();
            }
        }
    }

    [Test]
    public async Task SignedInLocalUserCanSavePrivateInformationOnlyDraft()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await JoinTenantAsync(fixture);
        string token = await fixture.SignInAsync();
        var draft = new CreateEventDraftRequestDto
        {
            Title = "Local onboarding draft regression",
            Description = "Fixture-owned private draft.",
            ParticipationConfiguration = new()
            {
                ParticipationHandlingModeId = (int)ParticipationHandlingModeEnum.InformationOnly,
                AdvanceRegistrationObligationId = (int)AdvanceRegistrationObligationEnum.NotApplicable
            },
            VisibilityTypeId = (int)VisibilityTypeEnum.Private,
            EventFormatId = 1,
            Timezone = "UTC"
        };

        using var response = await fixture.PostAuthorizedAsync("/api/event", draft, token);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Created)
            .Because(response.StatusCode == HttpStatusCode.BadRequest
                ? await response.Content.ReadAsStringAsync(LocalIdentityLifecycleHttpFixture.CancellationToken)
                : fixture.FailureMetadata);
        var result = await response.Content.ReadFromJsonAsync<BaseCommandResponse<Guid>>(
            LocalIdentityLifecycleHttpFixture.CancellationToken);
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.IsSuccess).IsTrue();
        await Assert.That(result.Id).IsNotEqualTo(Guid.Empty);

        using var read = new HttpRequestMessage(HttpMethod.Get,
            $"/api/event/{result.Id}/management-detail");
        read.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var persisted = await fixture.Client.SendAsync(
            read, LocalIdentityLifecycleHttpFixture.CancellationToken);
        await Assert.That(persisted.StatusCode).IsEqualTo(HttpStatusCode.OK)
            .Because(fixture.FailureMetadata);
        await Assert.That(await persisted.Content.ReadAsStringAsync(
            LocalIdentityLifecycleHttpFixture.CancellationToken)).Contains(draft.Title);
    }

    [Test]
    [Arguments("/api/event/my?pageNumber=1&pageSize=50")]
    [Arguments("/api/event/creation-context")]
    [Arguments("/api/studio/context")]
    public async Task SignedInLocalUserCanReadStudioContext(string path)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await JoinTenantAsync(fixture);
        string token = await fixture.SignInAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await fixture.Client.SendAsync(
            request, LocalIdentityLifecycleHttpFixture.CancellationToken);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK)
            .Because(fixture.FailureMetadata);
    }

    private static async Task JoinTenantAsync(LocalIdentityLifecycleHttpFixture fixture)
    {
        await using var database = fixture.Native.CreateDatabase();
        database.BindTenantLease();
        Guid userId = fixture.Binding.LocalSubjectId;
        Guid actorId = await database.Actors.Where(actor => actor.UserId == userId)
            .Select(actor => actor.Id).SingleAsync(LocalIdentityLifecycleHttpFixture.CancellationToken);
        database.TenantUsers.Add(new TenantUser
        {
            Id = Guid.CreateVersion7(),
            TenantId = PlatformDefaults.DefaultTenantId,
            Tenant = null!,
            UserId = userId,
            User = null!,
            ActorId = actorId,
            Actor = null!,
            StatusId = (int)TenantUserStatusEnum.Active,
            JoinedAt = fixture.Clock.GetUtcNow().UtcDateTime,
            CreatedAt = fixture.Clock.GetUtcNow().UtcDateTime
        });
        await database.SaveChangesAsync(LocalIdentityLifecycleHttpFixture.CancellationToken);
    }
}
