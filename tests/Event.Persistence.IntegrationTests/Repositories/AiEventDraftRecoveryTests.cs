using System.Data.Common;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Authorization;
using Explore.Application.Exceptions;
using Explore.Application.Models.Storage;
using Explore.Application.Features.Events.Requests.Commands;
using Explore.Application.Features.AiAssistant.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Ai;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;

namespace Event.Persistence.IntegrationTests.Repositories;

public sealed class AiEventDraftRecoveryTests
{
    // Existing project-native raster fixture from StorageContentSignaturePolicySecurityTests.
    private const string NativePng =
        "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAIAAAD91JpzAAAACXBIWXMAAAABAAAAAQBPJcTWAAAAEElEQVR4nGP8ywACLGCSAQANEQED1LYyQAAAAABJRU5ErkJggg==";
    private const string Payload = """
        {"title":"Durable AI draft","participationConfiguration":{"participationHandlingModeId":1,"advanceRegistrationObligationId":1,"identityAccessModeId":1},"session":{"startTime":"2027-01-01T12:00:00Z","endTime":"2027-01-01T13:00:00Z"}}
        """;

    private static Task<EventVisitorCapabilitySqliteFixture> CreateFixtureAsync(
        Action<IServiceCollection>? configureServices = null) =>
        EventVisitorCapabilitySqliteFixture.CreateAsync(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(new ConfirmationClock(
                new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc)));
            configureServices?.Invoke(services);
        });

    [Test]
    public async Task NativeConfirmationAndReplayUseTheSameCommittedPrimaryAndExecution()
    {
        DateTime now = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
        var clock = new RecoveryClock(now);
        await using var fixture = await CreateFixtureAsync(configureServices: services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
        });
        var identity = await SeedAsync(fixture);
        var handler = fixture.Services.GetRequiredService<ICommandHandler<ConfirmAiProposedActionCommand, BaseCommandResponse<Guid>>>();
        var request = new ConfirmAiProposedActionCommand
        {
            ConversationId = identity.ConversationId,
            ProposedActionId = identity.ProposedActionId,
            ExpectedVersion = await ReadConsentVersionAsync(fixture, identity.ProposedActionId)
        };
        var first = await handler.ExecuteAsync(request, CancellationToken.None);
        var replay = await handler.ExecuteAsync(request, CancellationToken.None);
        await Assert.That(first.IsSuccess).IsTrue();
        await Assert.That(replay.IsSuccess).IsTrue();
        await Assert.That(replay.Id).IsEqualTo(first.Id);
        clock.UtcNow = now.AddMinutes(30).AddTicks(1);
        var expiredReplay = await handler.ExecuteAsync(request, CancellationToken.None);
        await Assert.That(expiredReplay.IsSuccess).IsTrue();
        await Assert.That(expiredReplay.Id).IsEqualTo(first.Id);
        fixture.Context.ChangeTracker.Clear();
        await Assert.That(await fixture.Context.Events.CountAsync()).IsEqualTo(1);
        await Assert.That(await fixture.Context.AiToolExecutions.CountAsync()).IsEqualTo(1);
        var execution = await fixture.Context.AiToolExecutions.SingleAsync();
        await Assert.That(execution.PrimaryEventId).IsEqualTo(first.Id);
        await Assert.That(execution.Succeeded).IsTrue();
        await Assert.That((await fixture.Context.AiProposedActions.SingleAsync()).Status).IsEqualTo(AiProposedActionStatus.Executed);
    }

    [Test]
    [Arguments("conversation")]
    [Arguments("expired")]
    [Arguments("future")]
    public async Task NativeConfirmationRejectsWrongParentAndNonLiveProposalsBeforePrimary(string invalidity)
    {
        DateTime seededAt = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
        DateTime now = invalidity switch
        {
            "expired" => seededAt.AddMinutes(30).AddTicks(1),
            "future" => seededAt.AddTicks(-1),
            _ => seededAt
        };
        await using var fixture = await CreateFixtureAsync(configureServices: services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(new ConfirmationClock(now));
        });
        var identity = await SeedAsync(fixture);
        var handler = fixture.Services.GetRequiredService<ICommandHandler<ConfirmAiProposedActionCommand, BaseCommandResponse<Guid>>>();
        var response = await handler.ExecuteAsync(new ConfirmAiProposedActionCommand
        {
            ConversationId = invalidity == "conversation" ? Guid.CreateVersion7() : identity.ConversationId,
            ProposedActionId = identity.ProposedActionId,
            ExpectedVersion = await ReadConsentVersionAsync(fixture, identity.ProposedActionId)
        }, CancellationToken.None);
        await Assert.That(response.IsSuccess).IsFalse();
        await Assert.That(await fixture.Context.Events.CountAsync()).IsEqualTo(0);
        await Assert.That(await fixture.Context.AiToolExecutions.CountAsync()).IsEqualTo(0);
        await Assert.That(await fixture.Context.AiProposedActions
            .AnyAsync(value => value.Status == AiProposedActionStatus.Executed)).IsFalse();
    }

    private sealed class ConfirmationClock(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now);
    }

    [Test]
    public async Task InvalidImagePreservesTheCommittedPrimaryAndOriginalRecoveryWithoutAnUpload()
    {
        DateTime now = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
        await using var fixture = await CreateFixtureAsync(configureServices: services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(new ConfirmationClock(now));
        });
        var identity = await SeedAsync(fixture, withImage: true);
        var request = new ConfirmAiProposedActionCommand
        {
            ConversationId = identity.ConversationId,
            ProposedActionId = identity.ProposedActionId,
            ExpectedVersion = await ReadConsentVersionAsync(fixture, identity.ProposedActionId)
        };
        var first = await fixture.ExecuteAsync<ConfirmAiProposedActionCommand, BaseCommandResponse<Guid>>(request);
        var replay = await fixture.ExecuteAsync<ConfirmAiProposedActionCommand, BaseCommandResponse<Guid>>(request);
        await Assert.That(first.FailureCode).IsEqualTo("ai_draft_recovery_required");
        await Assert.That(replay.FailureCode).IsEqualTo("ai_draft_recovery_required");
        await Assert.That(replay.Id).IsEqualTo(first.Id);
        fixture.Context.ChangeTracker.Clear();
        var primary = await fixture.Context.Events.AsNoTracking().SingleAsync();
        var execution = await fixture.Context.AiToolExecutions.AsNoTracking().SingleAsync();
        await Assert.That(first.Id).IsEqualTo(primary.Id);
        await Assert.That(primary.FeaturedImageId).IsNull();
        await Assert.That(execution.PrimaryEventId).IsEqualTo(primary.Id);
        await Assert.That(execution.ReservedEventId).IsEqualTo(primary.Id);
        await Assert.That(execution.ImageSourceMessageId).IsNotNull();
        await Assert.That(execution.ImageUploadSessionId).IsNull();
        await Assert.That(execution.ImageStorageObjectId).IsNull();
        await Assert.That(execution.CompletedAt).IsNull();
        await Assert.That(execution.Succeeded).IsFalse();
        await Assert.That(await fixture.Context.StorageUploadSessions.CountAsync()).IsEqualTo(0);
        await Assert.That(await fixture.Context.StorageObjects.CountAsync()).IsEqualTo(0);
        await Assert.That((await fixture.Context.AiProposedActions.AsNoTracking().SingleAsync()).Status)
            .IsEqualTo(AiProposedActionStatus.Confirmed);
    }

    public static IEnumerable<AiProposedActionKind> ProposalOnlyFamilies() =>
        Enum.GetValues<AiProposedActionKind>().Where(kind => kind != AiProposedActionKind.CreateEventDraft);

    [Test]
    [MethodDataSource(nameof(ProposalOnlyFamilies))]
    public async Task ProposalOnlyFamilyCannotAcquireExecutionOrCreatePrimary(AiProposedActionKind kind)
    {
        DateTime now = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
        await using var fixture = await CreateFixtureAsync(configureServices: services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(new ConfirmationClock(now));
        });
        var identity = await SeedAsync(fixture);
        var action = await fixture.Context.AiProposedActions.SingleAsync();
        action.Kind = kind;
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        string version = await ReadConsentVersionAsync(fixture, identity.ProposedActionId);
        var response = await fixture.ExecuteAsync<ConfirmAiProposedActionCommand, BaseCommandResponse<Guid>>(new()
        {
            ConversationId = identity.ConversationId,
            ProposedActionId = identity.ProposedActionId,
            ExpectedVersion = version
        });
        await Assert.That(response.IsSuccess).IsFalse();
        await Assert.That(response.FailureCode).IsEqualTo("unsupported_action_kind");
        fixture.Context.ChangeTracker.Clear();
        await Assert.That(await fixture.Context.AiToolExecutions.CountAsync()).IsEqualTo(0);
        await Assert.That(await fixture.Context.Events.CountAsync()).IsEqualTo(0);
        var persisted = await fixture.Context.AiProposedActions.SingleAsync();
        await Assert.That(persisted.Status).IsEqualTo(AiProposedActionStatus.Confirmed);
        await Assert.That(persisted.ConsentVersionFor(fixture.UserId)).IsEqualTo(version);
    }

    private static async Task<string> ReadConsentVersionAsync(
        EventVisitorCapabilitySqliteFixture fixture, Guid actionId) =>
        (await fixture.Context.AiProposedActions.AsNoTracking().SingleAsync(value => value.Id == actionId))
        .ConsentVersionFor(fixture.UserId);

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ProposalPersistenceRequiresCurrentTargetAuthorityInsideCommitFence(bool revoke)
    {
        EventVisitorCapabilitySqliteFixture? currentFixture = null;
        bool observedCommitFence = false;
        var provider = Substitute.For<IAuthorizationProvider>();
        provider.AuthorizeAsync(Arg.Any<AuthorizationRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var request = call.Arg<AuthorizationRequest>()
                    ?? throw new InvalidOperationException("The authorization request is required.");
                bool insideFence = request.ResourceKind == ResourceKinds.Event
                    && request.Action == AuthorizationActions.Create
                    && currentFixture?.Context.Database.CurrentTransaction is not null;
                observedCommitFence |= insideFence;
                return insideFence && revoke
                    ? AuthorizationDecision.Deny(AuthorizationProviderMetadata.Runtime)
                    : AuthorizationDecision.Allow(AuthorizationProviderMetadata.Runtime);
            });
        await using var fixture = await CreateFixtureAsync(configureServices: services =>
        {
            services.RemoveAll<IAuthorizationProvider>();
            services.AddSingleton(provider);
        });
        currentFixture = fixture;
        var identity = await SeedAsync(fixture);
        var response = await fixture.ExecuteAsync<ProposeAiToolActionCommand, BaseCommandResponse<Guid>>(new()
        {
            ConversationId = identity.ConversationId,
            ToolName = nameof(AiProposedActionKind.CreateEventDraft),
            PayloadJson = Payload
        });
        await Assert.That(observedCommitFence).IsTrue();
        await Assert.That(response.IsSuccess).IsEqualTo(!revoke);
        if (revoke)
            await Assert.That(response.FailureCode).IsEqualTo("tool_authorization_denied");
        fixture.Context.ChangeTracker.Clear();
        await Assert.That(await fixture.Context.AiProposedActions.CountAsync()).IsEqualTo(revoke ? 1 : 2);
        await Assert.That(await fixture.Context.AiToolExecutions.CountAsync()).IsEqualTo(0);
        await Assert.That(await fixture.Context.Events.CountAsync()).IsEqualTo(0);
        if (!revoke)
            await Assert.That((await fixture.Context.AiProposedActions.SingleAsync(value => value.Id == response.Id)).Status)
                .IsEqualTo(AiProposedActionStatus.Proposed);
    }

    [Test]
    [Arguments("payload")]
    [Arguments("kind")]
    [Arguments("created")]
    [Arguments("actor")]
    [Arguments("provenance")]
    [Arguments("missing")]
    public async Task StaleOrMissingPreviewConsentCannotClaimOrCreateAnEffect(string changed)
    {
        DateTime now = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
        await using var fixture = await CreateFixtureAsync(configureServices: services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(new ConfirmationClock(now));
        });
        var identity = await SeedAsync(fixture);
        string displayedVersion = await ReadConsentVersionAsync(fixture, identity.ProposedActionId);
        var action = await fixture.Context.AiProposedActions.SingleAsync();
        if (changed == "payload")
            action.PayloadJson = Payload.Replace("Durable AI draft", "Changed draft", StringComparison.Ordinal);
        else if (changed == "kind")
            action.Kind = AiProposedActionKind.UpdateEventDraft;
        else if (changed == "created")
            action.CreatedAt = action.CreatedAt.AddTicks(-10);
        else if (changed == "actor")
            action.ActingActorId = null;
        else if (changed == "provenance")
        {
            var message = new AiMessage
            {
                Id = Guid.CreateVersion7(),
                TenantId = fixture.TenantId,
                ConversationId = identity.ConversationId,
                Role = AiMessageRole.Assistant,
                Content = "Different proposal source",
                Sequence = 1,
                CreatedAt = now
            };
            fixture.Context.AiMessages.Add(message);
            action.MessageId = message.Id;
        }
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        var response = await fixture.ExecuteAsync<ConfirmAiProposedActionCommand, BaseCommandResponse<Guid>>(new()
        {
            ConversationId = identity.ConversationId,
            ProposedActionId = identity.ProposedActionId,
            ExpectedVersion = changed == "missing" ? string.Empty : displayedVersion
        });
        await Assert.That(response.IsSuccess).IsFalse();
        await Assert.That(response.FailureCode).IsEqualTo("proposed_action_version_changed");
        await Assert.That(await fixture.Context.Events.CountAsync()).IsEqualTo(0);
        await Assert.That(await fixture.Context.AiToolExecutions.CountAsync()).IsEqualTo(0);
        await Assert.That((await fixture.Context.AiProposedActions.AsNoTracking().SingleAsync()).Status)
            .IsEqualTo(AiProposedActionStatus.Confirmed);
    }

    [Test]
    [Arguments("success")]
    [Arguments("write-ack")]
    [Arguments("finalize-ack")]
    public async Task NativeConfirmationCreatesPrimaryBeforeImageAndRecoversWithoutRepeatingTransfer(string uncertainty)
    {
        DateTime now = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
        var fault = new ImageFinalizationAcknowledgementFault(uncertainty == "finalize-ack");
        var bytesByKey = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        int writes = 0;
        EventVisitorCapabilitySqliteFixture? activeFixture = null;
        var provider = Substitute.For<IFileStorageProvider>();
        provider.Provider.Returns(StorageProviders.Local);
        provider.WriteAsync(Arg.Any<FileStorageWriteInput>(), Arg.Any<CancellationToken>()).Returns(async call =>
        {
            var context = activeFixture!.Context;
            writes++;
            await Assert.That(context.Database.CurrentTransaction).IsNull();
            await Assert.That(await context.Events.CountAsync()).IsEqualTo(1);
            await Assert.That((await context.AiToolExecutions.AsNoTracking().SingleAsync()).PrimaryEventId).IsNotNull();
            FileStorageWriteInput input = call.ArgAt<FileStorageWriteInput>(0);
            using var buffer = new MemoryStream();
            await input.Content.CopyToAsync(buffer, call.ArgAt<CancellationToken>(1));
            byte[] bytes = buffer.ToArray();
            bytesByKey.Add(input.ObjectKey!, bytes);
            if (uncertainty == "write-ack")
                throw new IOException("The byte handoff acknowledgement was lost.");
            return new FileStorageWriteResult(StorageProviders.Local, input.ObjectKey!, bytes.Length,
                input.ContentType, Convert.ToHexStringLower(SHA256.HashData(bytes)));
        });
        await using var fixture = await CreateFixtureAsync(configureServices: services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(new ConfirmationClock(now));
            services.ConfigureDbContext<ExploreDbContext>(options => options.AddInterceptors(fault), ServiceLifetime.Singleton);
            services.AddScoped<IStoragePolicyResolver>(_ =>
            {
                var policy = Substitute.For<IStoragePolicyResolver>();
                policy.ResolveAsync(Arg.Any<Guid?>(), Arg.Any<StoragePolicyIntent>(), Arg.Any<CancellationToken>())
                    .Returns(call => Task.FromResult(new ResolvedStoragePolicy(call.ArgAt<Guid?>(0),
                        StorageProviders.Local, 5 * 1024 * 1024, 20 * 1024 * 1024, 5 * 1024 * 1024,
                        false, true, SettingSource.SystemDefault, SettingSource.SystemDefault, SettingSource.SystemDefault)));
                return policy;
            });
            services.AddScoped<IStorageProviderBindingService>(services =>
            {
                var repository = services.GetRequiredService<IStorageProviderBindingRepository>();
                var bindings = Substitute.For<IStorageProviderBindingService>();
                bindings.CaptureAsync(StorageProviders.Local, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                    .Returns(async call =>
                    {
                        var binding = StorageProviderBinding.Local(Path.GetFullPath(Path.Combine(
                            Path.GetTempPath(), "ai-native-target-" + Guid.CreateVersion7().ToString("N"))));
                        await repository.AddAsync(binding, call.ArgAt<CancellationToken>(2));
                        return binding;
                    });
                bindings.ResolveAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(async call =>
                {
                    var binding = await repository.GetByIdAsync(call.ArgAt<Guid>(0), call.ArgAt<CancellationToken>(1));
                    if (binding?.Provider != StorageProviders.Local)
                        throw new InvalidOperationException("The original captured target is unavailable.");
                    return provider;
                });
                return bindings;
            });
        });
        activeFixture = fixture;
        var identity = await SeedAsync(fixture, withImage: true, imageData: NativePng);
        var handler = fixture.Services.GetRequiredService<ICommandHandler<ConfirmAiProposedActionCommand, BaseCommandResponse<Guid>>>();
        var request = new ConfirmAiProposedActionCommand
        {
            ConversationId = identity.ConversationId,
            ProposedActionId = identity.ProposedActionId,
            ExpectedVersion = await ReadConsentVersionAsync(fixture, identity.ProposedActionId)
        };

        var first = await handler.ExecuteAsync(request, CancellationToken.None);
        fixture.Context.ChangeTracker.Clear();
        var afterFirst = await fixture.Context.AiToolExecutions.AsNoTracking().SingleAsync();
        await Assert.That(afterFirst.PrimaryEventId).IsNotNull();
        await Assert.That(first.Id).IsEqualTo(afterFirst.PrimaryEventId!.Value);
        await Assert.That(first.IsSuccess).IsEqualTo(uncertainty == "success");
        if (uncertainty != "success")
            await Assert.That(first.FailureCode).IsEqualTo("ai_draft_recovery_required");
        var replay = await handler.ExecuteAsync(request, CancellationToken.None);

        await Assert.That(replay.IsSuccess).IsEqualTo(uncertainty != "write-ack");
        await Assert.That(replay.Id).IsEqualTo(first.Id);
        await Assert.That(writes).IsEqualTo(1);
        await Assert.That(bytesByKey.Single().Value.SequenceEqual(Convert.FromBase64String(NativePng))).IsTrue();
        fixture.Context.ChangeTracker.Clear();
        var execution = await fixture.Context.AiToolExecutions.AsNoTracking().SingleAsync();
        var session = await fixture.Context.StorageUploadSessions.AsNoTracking().SingleAsync();
        var primary = await fixture.Context.Events.AsNoTracking().SingleAsync();
        await Assert.That(execution.ImageUploadSessionId).IsEqualTo(session.Id);
        await Assert.That(session.IdempotencyKey).IsEqualTo(execution.ImageUploadOperationKey);
        await Assert.That(execution.PrimaryEventId).IsEqualTo(primary.Id);
        await Assert.That(execution.Succeeded).IsEqualTo(uncertainty != "write-ack");
        await Assert.That(primary.FeaturedImageId).IsEqualTo(uncertainty == "write-ack" ? null : execution.ImageStorageObjectId);
        await Assert.That(await fixture.Context.StorageObjects.CountAsync()).IsEqualTo(uncertainty == "write-ack" ? 0 : 1);
        if (uncertainty == "finalize-ack")
            await Assert.That(fault.Fired).IsTrue();
    }

    private sealed class ImageFinalizationAcknowledgementFault(bool armed) : DbTransactionInterceptor
    {
        public bool Fired { get; private set; }
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (armed && !Fired && eventData.Context is ExploreDbContext context
                && context.ChangeTracker.Entries<StorageUploadSession>().Any(entry =>
                    entry.Entity.Status == StorageUploadSessionStates.Finalized))
            {
                Fired = true;
                throw new IOException("The native finalization commit acknowledgement was lost.");
            }
            return Task.CompletedTask;
        }
    }

    [Test]
    [Arguments("owned")]
    [Arguments("wrong-parent")]
    [Arguments("canceled")]
    public async Task NativeRejectionBindsConversationAndStopsBeforeMutationOnCancellation(string disposition)
    {
        await using var fixture = await CreateFixtureAsync();
        var conversation = new AiConversation
        {
            Id = Guid.CreateVersion7(),
            TenantId = fixture.TenantId,
            UserId = fixture.UserId,
            ActorId = fixture.ActorId,
            CreatedAt = DateTime.UtcNow
        };
        var action = conversation.ProposeAction(
            AiProposedActionKind.CreateEventDraft, Payload, null, fixture.UserId, DateTime.UtcNow);
        fixture.Context.AiConversations.Add(conversation);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        var handler = fixture.Services.GetRequiredService<ICommandHandler<RejectAiProposedActionCommand, BaseCommandResponse<Guid>>>();
        var request = new RejectAiProposedActionCommand
        {
            ConversationId = disposition == "wrong-parent" ? Guid.CreateVersion7() : conversation.Id,
            ProposedActionId = action.Id
        };
        using var canceled = new CancellationTokenSource();
        if (disposition == "canceled")
        {
            canceled.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => handler.ExecuteAsync(request, canceled.Token));
        }
        else
        {
            var result = await handler.ExecuteAsync(request, CancellationToken.None);
            await Assert.That(result.IsSuccess).IsEqualTo(disposition == "owned");
            if (disposition == "owned")
                await Assert.That((await handler.ExecuteAsync(request, CancellationToken.None)).IsSuccess).IsTrue();
            else
                await Assert.That(result.FailureCode).IsEqualTo("proposed_action_not_found");
        }
        fixture.Context.ChangeTracker.Clear();
        var persisted = await fixture.Context.AiProposedActions.SingleAsync();
        await Assert.That(persisted.Status).IsEqualTo(disposition == "owned"
            ? AiProposedActionStatus.Rejected : AiProposedActionStatus.Proposed);
        await Assert.That(persisted.ConversationId).IsEqualTo(conversation.Id);
        await Assert.That(await fixture.Context.AiToolExecutions.CountAsync()).IsEqualTo(0);
        await Assert.That(await fixture.Context.Events.CountAsync()).IsEqualTo(0);
    }

    [Test]
    [Arguments("none")]
    [Arguments("selected")]
    [Arguments("session-owner")]
    [Arguments("session-key")]
    [Arguments("object-owner")]
    [Arguments("object-key")]
    [Arguments("object-purpose")]
    [Arguments("same-image-ineligible")]
    [Arguments("conversation")]
    [Arguments("expired")]
    public async Task NativeRetainedImageRequiresExactCurrentCustodyWithoutOverwritingASelectedImage(string drift)
    {
        DateTime now = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
        var clock = new RecoveryClock(now);
        await using var fixture = await CreateFixtureAsync(configureServices: services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
        });
        var identity = await SeedAsync(fixture, withImage: true);
        var repository = fixture.Services.GetRequiredService<IAiEventDraftRecoveryRepository>();
        var reserved = await repository.AcquireAsync(identity, CancellationToken.None);
        var created = await fixture.ExecuteAsync<CreateEventCommand, BaseCommandResponse<Guid>>(
            CreateEventCommand.FromAiDraft(reserved!, Payload));
        await Assert.That(created.IsSuccess).IsTrue();
        fixture.Context.ChangeTracker.Clear();
        var linked = await repository.ReadAsync(identity, CancellationToken.None);

        // Seed native producer settlement metadata, not a mock receipt or a claim that bytes were transferred.
        var binding = StorageProviderBinding.Local(Path.GetFullPath(Path.Combine(
            Path.GetTempPath(), "ai-image-custody-" + Guid.CreateVersion7().ToString("N"))));
        Guid imageId = Guid.CreateVersion7();
        var image = new StorageObject
        {
            Id = imageId,
            TenantId = fixture.TenantId,
            Tenant = null!,
            FileTypeId = (int)FileTypeEnum.Image,
            FileType = null!,
            Provider = binding.Provider,
            StorageProviderBindingId = binding.Id,
            ObjectKey = $"images/{imageId:N}.png",
            FullName = "draft.png",
            SafeDisplayName = "draft.png",
            Extension = "png",
            ContentType = "image/png",
            Purpose = StorageObjectPurposes.EventImage,
            Visibility = StorageObjectVisibilities.PublicImage,
            LifecycleState = StorageObjectLifecycleStates.Active,
            Size = 64,
            ActorId = fixture.ActorId,
            CreatedBy = fixture.UserId,
            CreatedAt = now,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        var session = new StorageUploadSession
        {
            Id = imageId,
            TenantId = fixture.TenantId,
            UserId = fixture.UserId,
            Provider = binding.Provider,
            StorageProviderBindingId = binding.Id,
            ObjectKey = image.ObjectKey,
            ContentType = image.ContentType,
            SafeDisplayName = image.SafeDisplayName,
            Extension = image.Extension,
            Purpose = image.Purpose,
            Visibility = image.Visibility,
            ExpectedSizeBytes = image.Size,
            ReservedBytes = image.Size,
            PolicyMaxUploadBytes = image.Size,
            IdempotencyKey = linked!.ImageUploadOperationKey,
            Status = StorageUploadSessionStates.Reserved,
            ExpiresAt = now.AddMinutes(15),
            CreatedAt = now,
            CreatedBy = fixture.UserId,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        session.MarkUploading(now);
        session.RecordProducerSettlement(image.Id, binding.Id, image.ObjectKey!, null);
        session.Finalize(image.Id, image.ObjectKey!, null, now);
        fixture.Context.AddRange(binding, image, session);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        var unit = fixture.Services.GetRequiredService<IUnitOfWork>();
        await unit.ExecuteSerializableAsync(async token =>
        {
            var execution = await repository.ReadForSettlementAsync(identity, linked.Id, linked.ConcurrencyStamp, token);
            execution!.BindImageUploadSession(execution.ImageSourceMessageId!.Value, execution.ImageSourceDigest!, session.Id);
            execution.BindImageStorageObject(session.Id, image.Id);
            await repository.SaveSettlementAsync(identity, execution, token);
            return true;
        });
        fixture.Context.ChangeTracker.Clear();
        var retained = await repository.ReadAsync(identity, CancellationToken.None);
        Guid? selectedId = null;
        if (drift == "selected")
        {
            // A separately selected native image must survive a delayed AI attachment.
            var selected = new StorageObject
            {
                Id = Guid.CreateVersion7(),
                TenantId = fixture.TenantId,
                Tenant = null!,
                FileTypeId = (int)FileTypeEnum.Image,
                FileType = null!,
                Provider = binding.Provider,
                StorageProviderBindingId = binding.Id,
                ObjectKey = $"images/{Guid.CreateVersion7():N}.png",
                FullName = "selected.png",
                SafeDisplayName = "selected.png",
                Extension = "png",
                ContentType = "image/png",
                Purpose = StorageObjectPurposes.EventImage,
                Visibility = StorageObjectVisibilities.PublicImage,
                LifecycleState = StorageObjectLifecycleStates.Active,
                Size = 64,
                ActorId = fixture.ActorId,
                CreatedBy = fixture.UserId,
                CreatedAt = now,
                ConcurrencyStamp = Guid.CreateVersion7()
            };
            fixture.Context.StorageObjects.Add(selected);
            await fixture.Context.SaveChangesAsync();
            selectedId = selected.Id;
            await fixture.Context.Events.Where(value => value.Id == created.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.FeaturedImageId, selectedId));
            fixture.Context.ChangeTracker.Clear();
        }
        if (drift is "session-owner" or "session-key")
        {
            var changed = await fixture.Context.StorageUploadSessions.SingleAsync();
            if (drift == "session-owner") changed.UserId = null;
            else changed.IdempotencyKey = "another-image-operation";
        }
        if (drift is "object-owner" or "object-key" or "object-purpose" or "same-image-ineligible")
        {
            var changed = await fixture.Context.StorageObjects.SingleAsync();
            if (drift == "object-owner") changed.CreatedBy = null;
            else if (drift == "object-key") changed.ObjectKey = null;
            else changed.Purpose = StorageObjectPurposes.ProfileImage;
            if (drift == "same-image-ineligible")
            {
                selectedId = image.Id;
                (await fixture.Context.Events.SingleAsync()).FeaturedImageId = image.Id;
            }
        }
        if (drift == "conversation")
            (await fixture.Context.AiConversations.SingleAsync()).Status = AiConversationStatus.Running;
        if (drift == "expired") clock.UtcNow = now.AddMinutes(30).AddTicks(1);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        var command = AttachAiDraftImageCommand.FromExecution(retained!);
        var response = await fixture.ExecuteAsync<AttachAiDraftImageCommand, BaseCommandResponse<Guid>>(command);
        await Assert.That(response.IsSuccess).IsEqualTo(drift == "none");
        if (drift != "none")
            await Assert.That(response.FailureCode).IsEqualTo("ai_draft_image_authority_changed");
        fixture.Context.ChangeTracker.Clear();
        await Assert.That((await fixture.Context.Events.SingleAsync()).FeaturedImageId)
            .IsEqualTo(drift == "none" ? image.Id : selectedId);
        var preserved = await fixture.Context.AiToolExecutions.AsNoTracking().SingleAsync();
        await Assert.That(preserved.PrimaryEventId).IsEqualTo(created.Id);
        await Assert.That(preserved.ImageUploadSessionId).IsEqualTo(session.Id);
        await Assert.That(preserved.ImageStorageObjectId).IsEqualTo(image.Id);
        await Assert.That(preserved.CompletedAt).IsNull();
        await Assert.That((await fixture.Context.AiProposedActions.AsNoTracking().SingleAsync()).Status)
            .IsEqualTo(AiProposedActionStatus.Confirmed);
        if (drift == "none")
        {
            var replay = await fixture.ExecuteAsync<AttachAiDraftImageCommand, BaseCommandResponse<Guid>>(command);
            await Assert.That(replay.IsSuccess).IsTrue();
            await unit.ExecuteSerializableAsync(async token =>
            {
                var execution = await repository.ReadForSettlementAsync(
                    identity, retained!.Id, retained.ConcurrencyStamp, token);
                execution!.MarkSucceeded(now);
                await repository.SaveSettlementAsync(identity, execution, token);
                return true;
            });
            fixture.Context.ChangeTracker.Clear();
            await Assert.That((await repository.ReadAsync(identity, CancellationToken.None))!.Succeeded).IsTrue();
        }
        await Assert.That(await fixture.Context.Events.CountAsync()).IsEqualTo(1);
        await Assert.That(await fixture.Context.StorageUploadSessions.CountAsync()).IsEqualTo(1);
    }

    private sealed class RecoveryClock(DateTime now) : TimeProvider
    {
        public DateTime UtcNow { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => new(UtcNow);
    }

    [Test]
    public async Task ExpiryDuringNativePrimaryCreationRollsBackTheGraphAndRetainsItsReservation()
    {
        DateTime now = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
        var clock = new RecoveryClock(now);
        var boundary = new PrimaryAdmissionExpiry(clock);
        await using var fixture = await CreateFixtureAsync(configureServices: services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
            services.ConfigureDbContext<ExploreDbContext>(options => options.AddInterceptors(boundary), ServiceLifetime.Singleton);
        });
        var identity = await SeedAsync(fixture);
        var repository = fixture.Services.GetRequiredService<IAiEventDraftRecoveryRepository>();
        var execution = await repository.AcquireAsync(identity, CancellationToken.None);
        var command = CreateEventCommand.FromAiDraft(execution!, Payload);
        boundary.Armed = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.ExecuteAsync<CreateEventCommand, BaseCommandResponse<Guid>>(command));
        await Assert.That(boundary.Fired).IsTrue();
        fixture.Context.ChangeTracker.Clear();
        await Assert.That(await fixture.Context.Events.CountAsync()).IsEqualTo(0);
        var preserved = await fixture.Context.AiToolExecutions.AsNoTracking().SingleAsync();
        await Assert.That(preserved.Id).IsEqualTo(execution!.Id);
        await Assert.That(preserved.ReservedEventId).IsEqualTo(execution.ReservedEventId);
        await Assert.That(preserved.PrimaryEventId).IsNull();
        await Assert.That(preserved.CompletedAt).IsNull();
        await Assert.That((await fixture.Context.AiProposedActions.AsNoTracking().SingleAsync()).Status)
            .IsEqualTo(AiProposedActionStatus.Confirmed);
    }

    private sealed class PrimaryAdmissionExpiry(RecoveryClock clock) : DbTransactionInterceptor
    {
        public bool Armed { get; set; }
        public bool Fired { get; private set; }
        public override ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
            DbConnection connection, TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result,
            CancellationToken cancellationToken = default)
        {
            if (Armed)
            {
                Armed = false;
                Fired = true;
                clock.UtcNow = clock.UtcNow.AddMinutes(30).AddTicks(1);
            }
            return ValueTask.FromResult(result);
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task NativePrimaryAdmissionRequiresActiveParentAfterReservation(bool startConversation)
    {
        var boundary = new PrimaryAdmissionLifecycle();
        await using var fixture = await CreateFixtureAsync(configureServices: services =>
            services.ConfigureDbContext<ExploreDbContext>(options => options.AddInterceptors(boundary), ServiceLifetime.Singleton));
        var identity = await SeedAsync(fixture);
        var repository = fixture.Services.GetRequiredService<IAiEventDraftRecoveryRepository>();
        var execution = await repository.AcquireAsync(identity, CancellationToken.None);
        boundary.BeforePrimary = async token =>
        {
            if (!startConversation) return;
            await using var competing = fixture.CreateScope();
            await competing.ServiceProvider.GetRequiredService<ExploreDbContext>().AiConversations
                .Where(value => value.Id == identity.ConversationId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.Status, AiConversationStatus.Running), token);
        };
        var response = await fixture.ExecuteAsync<CreateEventCommand, BaseCommandResponse<Guid>>(
            CreateEventCommand.FromAiDraft(execution!, Payload));
        await Assert.That(boundary.Fired).IsTrue();
        await Assert.That(response.IsSuccess).IsEqualTo(!startConversation);
        fixture.Context.ChangeTracker.Clear();
        var preserved = await fixture.Context.AiToolExecutions.AsNoTracking().SingleAsync();
        await Assert.That(preserved.Id).IsEqualTo(execution!.Id);
        await Assert.That(preserved.ReservedEventId).IsEqualTo(execution.ReservedEventId);
        await Assert.That(preserved.PrimaryEventId).IsEqualTo(startConversation ? (Guid?)null : execution.ReservedEventId);
        await Assert.That(preserved.CompletedAt).IsNull();
        await Assert.That(await fixture.Context.Events.CountAsync()).IsEqualTo(startConversation ? 0 : 1);
        await Assert.That((await fixture.Context.AiProposedActions.AsNoTracking().SingleAsync()).Status)
            .IsEqualTo(AiProposedActionStatus.Confirmed);
        // Nonactive state blocks only new primary admission, not independent recovery
        // of an already-owned reservation or primary.
        await using var recovery = fixture.CreateScope();
        var recovered = await recovery.ServiceProvider.GetRequiredService<IAiEventDraftRecoveryRepository>()
            .ReadAsync(identity, CancellationToken.None);
        await Assert.That(recovered!.Id).IsEqualTo(execution.Id);
        await Assert.That(recovered.PrimaryEventId).IsEqualTo(preserved.PrimaryEventId);
        if (!startConversation)
        {
            await fixture.Context.AiConversations.Where(value => value.Id == identity.ConversationId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.Status, AiConversationStatus.Running));
            await using var replay = fixture.CreateScope();
            var replayRepository = replay.ServiceProvider.GetRequiredService<IAiEventDraftRecoveryRepository>();
            var admitted = await replay.ServiceProvider.GetRequiredService<IUnitOfWork>().ExecuteSerializableAsync(
                token => replayRepository.ReadForPrimaryCreationAsync(
                    identity, execution.Id, execution.ReservedEventId!.Value, token));
            await Assert.That(admitted!.PrimaryEventId).IsEqualTo(preserved.PrimaryEventId);
            await Assert.That(await fixture.Context.Events.CountAsync()).IsEqualTo(1);
        }
    }

    private sealed class PrimaryAdmissionLifecycle : DbTransactionInterceptor
    {
        public Func<CancellationToken, Task>? BeforePrimary { get; set; }
        public bool Fired { get; private set; }
        public override async ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
            DbConnection connection, TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result,
            CancellationToken cancellationToken = default)
        {
            var beforePrimary = BeforePrimary;
            if (beforePrimary is not null)
            {
                BeforePrimary = null;
                Fired = true;
                await beforePrimary(cancellationToken);
            }
            return result;
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ConfirmationRechecksSelectedProviderInsideTheExactClaimFence(bool revoke)
    {
        DateTime now = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
        EventVisitorCapabilitySqliteFixture? currentFixture = null;
        bool sawClaimFence = false;
        var provider = Substitute.For<IAuthorizationProvider>();
        provider.AuthorizeAsync(Arg.Any<AuthorizationRequest>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var request = call.Arg<AuthorizationRequest>()
                ?? throw new InvalidOperationException("The authorization request is required.");
            bool insideClaim = request.ResourceKind == ResourceKinds.AiConversation
                && request.Action == AuthorizationActions.AiConversations.ConfirmAction
                && currentFixture?.Context.Database.CurrentTransaction is not null;
            sawClaimFence |= insideClaim;
            return insideClaim && revoke
                ? AuthorizationDecision.Deny(AuthorizationProviderMetadata.Runtime)
                : AuthorizationDecision.Allow(AuthorizationProviderMetadata.Runtime);
        });
        await using var fixture = await CreateFixtureAsync(configureServices: services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(new ConfirmationClock(now));
            services.RemoveAll<IAuthorizationProvider>();
            services.AddSingleton(provider);
        });
        currentFixture = fixture;
        var identity = await SeedAsync(fixture);
        var proposed = await fixture.Context.AiProposedActions.SingleAsync();
        proposed.Status = AiProposedActionStatus.Proposed;
        proposed.ConfirmedBy = null;
        proposed.ConfirmedAt = null;
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        var request = new ConfirmAiProposedActionCommand
        {
            ConversationId = identity.ConversationId,
            ProposedActionId = identity.ProposedActionId,
            ExpectedVersion = await ReadConsentVersionAsync(fixture, identity.ProposedActionId)
        };
        var handler = fixture.Services.GetRequiredService<ICommandHandler<ConfirmAiProposedActionCommand, BaseCommandResponse<Guid>>>();
        if (revoke)
            await Assert.ThrowsAsync<AuthorizationException>(() => handler.ExecuteAsync(request, CancellationToken.None));
        else
            await Assert.That((await handler.ExecuteAsync(request, CancellationToken.None)).IsSuccess).IsTrue();
        await Assert.That(sawClaimFence).IsTrue();
        fixture.Context.ChangeTracker.Clear();
        await Assert.That(await fixture.Context.Events.CountAsync()).IsEqualTo(revoke ? 0 : 1);
        await Assert.That(await fixture.Context.AiToolExecutions.CountAsync()).IsEqualTo(revoke ? 0 : 1);
        var action = await fixture.Context.AiProposedActions.AsNoTracking().SingleAsync();
        await Assert.That(action.Status).IsEqualTo(revoke ? AiProposedActionStatus.Proposed : AiProposedActionStatus.Executed);
        await Assert.That(action.ConfirmedAt.HasValue).IsEqualTo(!revoke);
    }

    [Test]
    public async Task AcquisitionReturnsTheOriginalExecutionAndReservedEvent()
    {
        await using var fixture = await CreateFixtureAsync();
        var identity = await SeedAsync(fixture);
        var repository = fixture.Services.GetRequiredService<IAiEventDraftRecoveryRepository>();

        var first = await repository.AcquireAsync(identity, CancellationToken.None);
        var replay = await repository.AcquireAsync(identity, CancellationToken.None);

        await Assert.That(first).IsNotNull();
        await Assert.That(replay!.Id).IsEqualTo(first!.Id);
        await Assert.That(replay.ReservedEventId).IsEqualTo(first.ReservedEventId);
        await Assert.That(replay.PrimaryEventId).IsNull();
        await Assert.That(await fixture.Context.AiToolExecutions.CountAsync()).IsEqualTo(1);
        await Assert.That(await fixture.Context.Events.CountAsync()).IsEqualTo(0);
    }

    [Test]
    [Arguments("payload")]
    [Arguments("principal")]
    [Arguments("actor")]
    [Arguments("action")]
    [Arguments("conversation")]
    [Arguments("confirmation")]
    [Arguments("message")]
    public async Task ChangedIdentityCannotAcquireOrRecoverExecution(string changed)
    {
        await using var fixture = await CreateFixtureAsync();
        var identity = await SeedAsync(fixture);
        var repository = fixture.Services.GetRequiredService<IAiEventDraftRecoveryRepository>();
        var first = await repository.AcquireAsync(identity, CancellationToken.None);
        var altered = changed switch
        {
            "payload" => identity with { PayloadDigest = AiToolExecution.DigestPayload(Payload + " ") },
            "principal" => identity with { PrincipalUserId = Guid.CreateVersion7() },
            "actor" => identity with { ActingActorId = Guid.CreateVersion7() },
            "action" => identity with { ProposedActionId = Guid.CreateVersion7() },
            "conversation" => identity with { ConversationId = Guid.CreateVersion7() },
            "message" => identity with { ActionMessageId = Guid.CreateVersion7() },
            _ => identity with { ConfirmedAt = identity.ConfirmedAt.AddTicks(1) }
        };

        await Assert.That(await repository.AcquireAsync(altered, CancellationToken.None)).IsNull();
        await Assert.That(await repository.ReadAsync(altered, CancellationToken.None)).IsNull();
        await Assert.That((await repository.ReadAsync(identity, CancellationToken.None))!.Id).IsEqualTo(first!.Id);
        await Assert.That(await fixture.Context.AiToolExecutions.CountAsync()).IsEqualTo(1);
    }

    [Test]
    [Arguments("user")]
    [Arguments("actor")]
    [Arguments("conversation")]
    [Arguments("payload")]
    [Arguments("confirmation")]
    [Arguments("tenant-membership")]
    [Arguments("tenant")]
    [Arguments("kind")]
    public async Task ChangedPersistedAuthorityCannotRecoverOrCreatePrimary(string changed)
    {
        await using var fixture = await CreateFixtureAsync();
        var identity = await SeedAsync(fixture);
        var repository = fixture.Services.GetRequiredService<IAiEventDraftRecoveryRepository>();
        var execution = await repository.AcquireAsync(identity, CancellationToken.None);
        switch (changed)
        {
            case "user":
                await fixture.Context.Users.Where(value => value.Id == fixture.UserId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.IsDeleted, true));
                break;
            case "actor":
                await fixture.Context.Actors.Where(value => value.Id == fixture.ActorId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.IsSuspended, true));
                break;
            case "conversation":
                await fixture.Context.AiConversations.Where(value => value.Id == identity.ConversationId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.IsDeleted, true));
                break;
            case "payload":
                await fixture.Context.AiProposedActions.Where(value => value.Id == identity.ProposedActionId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.PayloadJson, Payload + " "));
                break;
            case "tenant-membership":
                await fixture.Context.TenantUsers.Where(value => value.UserId == fixture.UserId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.IsDeleted, true));
                break;
            case "tenant":
                await fixture.Context.Tenants.Where(value => value.Id == fixture.TenantId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.TenantStatusId, (int)TenantStatusEnum.Suspended));
                break;
            case "kind":
                await fixture.Context.AiProposedActions.Where(value => value.Id == identity.ProposedActionId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.KindId, (int)AiProposedActionKind.PublishEvent));
                break;
            default:
                await fixture.Context.AiProposedActions.Where(value => value.Id == identity.ProposedActionId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.ConfirmedBy, (Guid?)null));
                break;
        }

        await Assert.That(await repository.ReadAsync(identity, CancellationToken.None)).IsNull();
        var admitted = await fixture.Services.GetRequiredService<IUnitOfWork>().ExecuteSerializableAsync(
            token => repository.ReadForPrimaryCreationAsync(identity, execution!.Id, execution.ReservedEventId!.Value, token));
        await Assert.That(admitted).IsNull();
        await Assert.That(await fixture.Context.Events.CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task PrimaryCommitReplayReturnsOneEventAndOneOwnershipGraph()
    {
        await using var fixture = await CreateFixtureAsync();
        var identity = await SeedAsync(fixture);
        var repository = fixture.Services.GetRequiredService<IAiEventDraftRecoveryRepository>();
        var execution = await repository.AcquireAsync(identity, CancellationToken.None);
        var command = CreateEventCommand.FromAiDraft(execution!, Payload);
        var first = await fixture.ExecuteAsync<CreateEventCommand, BaseCommandResponse<Guid>>(command);
        await Assert.That(first.IsSuccess).IsTrue().Because(first.FailureCode ?? first.Message ?? "Native creation failed.");
        fixture.Context.ChangeTracker.Clear();

        // The acknowledgement is deliberately discarded. A fresh scope must recover the persisted effect.
        await using var recovery = fixture.CreateScope();
        var recovered = await recovery.ServiceProvider.GetRequiredService<IAiEventDraftRecoveryRepository>()
            .ReadAsync(identity, CancellationToken.None);
        var replay = await recovery.ServiceProvider.GetRequiredService<ICommandHandler<CreateEventCommand, BaseCommandResponse<Guid>>>()
            .ExecuteAsync(CreateEventCommand.FromAiDraft(recovered!, Payload), CancellationToken.None);

        await Assert.That(replay.IsSuccess).IsTrue();
        await Assert.That(replay.Id).IsEqualTo(execution!.ReservedEventId);
        await Assert.That(recovered!.PrimaryEventId).IsEqualTo(first.Id);
        await Assert.That(await fixture.Context.Events.CountAsync()).IsEqualTo(1);
        await Assert.That(await fixture.Context.EventRoleAssignments.CountAsync()).IsEqualTo(0);
        await Assert.That(await fixture.Context.EventParticipationConfigurations.CountAsync()).IsEqualTo(1);
        await Assert.That(await fixture.Context.EventSessions.CountAsync()).IsEqualTo(1);
    }

    [Test]
    public async Task JsonRoundTripCannotCarryServerOnlyRecoveryBinding()
    {
        await using var fixture = await CreateFixtureAsync();
        var identity = await SeedAsync(fixture);
        var execution = await fixture.Services.GetRequiredService<IAiEventDraftRecoveryRepository>()
            .AcquireAsync(identity, CancellationToken.None);
        var trusted = CreateEventCommand.FromAiDraft(execution!, Payload);
        string json = JsonSerializer.Serialize(trusted);
        // Even an explicitly posted property cannot populate the trusted internal binding.
        using var document = JsonDocument.Parse(json);
        json = JsonSerializer.Serialize(new
        {
            EventDto = document.RootElement.GetProperty("EventDto"),
            AiDraftOrigin = new
            {
                Identity = identity,
                ExecutionId = execution!.Id,
                ReservedEventId = execution.ReservedEventId
            }
        });
        var ordinary = JsonSerializer.Deserialize<CreateEventCommand>(json)!;

        var first = await fixture.ExecuteAsync<CreateEventCommand, BaseCommandResponse<Guid>>(ordinary);
        var second = await fixture.ExecuteAsync<CreateEventCommand, BaseCommandResponse<Guid>>(trusted);

        await Assert.That(first.IsSuccess).IsTrue();
        await Assert.That(second.IsSuccess).IsTrue();
        await Assert.That(first.Id).IsNotEqualTo(execution!.ReservedEventId);
        await Assert.That(second.Id).IsEqualTo(execution.ReservedEventId);
        await Assert.That(await fixture.Context.Events.CountAsync()).IsEqualTo(2);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CommitBeforeLostAcknowledgementOrCancellationRetainsOnePrimary(bool cancel)
    {
        using var cancellation = new CancellationTokenSource();
        var fault = new PrimaryCommitControl(cancellation) { CancelAfterCommit = cancel, FailAfterCommit = !cancel };
        await using var fixture = await CreateFixtureAsync(
            services => services.ConfigureDbContext<ExploreDbContext>(
                options => options.AddInterceptors(fault), ServiceLifetime.Singleton));
        var identity = await SeedAsync(fixture);
        var execution = await fixture.Services.GetRequiredService<IAiEventDraftRecoveryRepository>()
            .AcquireAsync(identity, CancellationToken.None);
        fault.Target = execution!.ReservedEventId;
        var handler = fixture.Services.GetRequiredService<ICommandHandler<CreateEventCommand, BaseCommandResponse<Guid>>>();
        if (cancel)
            await Assert.ThrowsAsync<OperationCanceledException>(() =>
                handler.ExecuteAsync(CreateEventCommand.FromAiDraft(execution, Payload), cancellation.Token));
        else
            await Assert.ThrowsAsync<IOException>(() =>
                handler.ExecuteAsync(CreateEventCommand.FromAiDraft(execution, Payload), cancellation.Token));

        await using var scope = fixture.CreateScope();
        var recovered = await scope.ServiceProvider.GetRequiredService<IAiEventDraftRecoveryRepository>()
            .ReadAsync(identity, CancellationToken.None);
        var replay = await scope.ServiceProvider.GetRequiredService<ICommandHandler<CreateEventCommand, BaseCommandResponse<Guid>>>()
            .ExecuteAsync(CreateEventCommand.FromAiDraft(recovered!, Payload), CancellationToken.None);
        await Assert.That(replay.IsSuccess).IsTrue();
        await Assert.That(recovered!.PrimaryEventId).IsEqualTo(execution.ReservedEventId);
        await Assert.That(replay.Id).IsEqualTo(execution.ReservedEventId);
        await Assert.That(await fixture.Context.Events.CountAsync()).IsEqualTo(1);
        await Assert.That(await fixture.Context.EventSessions.CountAsync()).IsEqualTo(1);
        await Assert.That(await fixture.Context.EventParticipationConfigurations.CountAsync()).IsEqualTo(1);
    }

    [Test]
    public async Task CancellationBeforePrimaryCommitRollsBackGraphButRetainsOriginalReservation()
    {
        using var cancellation = new CancellationTokenSource();
        var fault = new PrimaryCommitControl(cancellation) { CancelBeforeCommit = true };
        await using var fixture = await CreateFixtureAsync(
            services => services.ConfigureDbContext<ExploreDbContext>(
                options => options.AddInterceptors(fault), ServiceLifetime.Singleton));
        var identity = await SeedAsync(fixture);
        var execution = await fixture.Services.GetRequiredService<IAiEventDraftRecoveryRepository>()
            .AcquireAsync(identity, CancellationToken.None);
        fault.Target = execution!.ReservedEventId;
        var handler = fixture.Services.GetRequiredService<ICommandHandler<CreateEventCommand, BaseCommandResponse<Guid>>>();
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            handler.ExecuteAsync(CreateEventCommand.FromAiDraft(execution, Payload), cancellation.Token));
        await Assert.That(await fixture.Context.Events.CountAsync()).IsEqualTo(0);
        await Assert.That(await fixture.Context.EventSessions.CountAsync()).IsEqualTo(0);

        await using var scope = fixture.CreateScope();
        var recovered = await scope.ServiceProvider.GetRequiredService<IAiEventDraftRecoveryRepository>()
            .AcquireAsync(identity, CancellationToken.None);
        await Assert.That(recovered!.Id).IsEqualTo(execution.Id);
        await Assert.That(recovered.ReservedEventId).IsEqualTo(execution.ReservedEventId);
        await Assert.That(recovered.PrimaryEventId).IsNull();
        var retry = await scope.ServiceProvider.GetRequiredService<ICommandHandler<CreateEventCommand, BaseCommandResponse<Guid>>>()
            .ExecuteAsync(CreateEventCommand.FromAiDraft(recovered, Payload), CancellationToken.None);
        await Assert.That(retry.IsSuccess).IsTrue();
        await Assert.That(retry.Id).IsEqualTo(execution.ReservedEventId);
        await Assert.That(await fixture.Context.Events.CountAsync()).IsEqualTo(1);
    }

    [Test]
    public async Task ConcurrentAcquisitionsConvergeOnOneReservation()
    {
        await using var fixture = await CreateFixtureAsync();
        var identity = await SeedAsync(fixture);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int arrived = 0;
        async Task<AiToolExecution?> AcquireAsync()
        {
            await using var scope = fixture.CreateScope();
            if (Interlocked.Increment(ref arrived) == 2) ready.TrySetResult();
            await start.Task.WaitAsync(TimeSpan.FromSeconds(15));
            return await scope.ServiceProvider.GetRequiredService<IAiEventDraftRecoveryRepository>()
                .AcquireAsync(identity, CancellationToken.None);
        }
        Task<AiToolExecution?> first = Task.Run(AcquireAsync);
        Task<AiToolExecution?> second = Task.Run(AcquireAsync);
        try
        {
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(15));
        }
        finally
        {
            start.TrySetResult();
        }
        var results = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(30));
        await Assert.That(results[0]).IsNotNull();
        await Assert.That(results[1]!.Id).IsEqualTo(results[0]!.Id);
        await Assert.That(results[1]!.ReservedEventId).IsEqualTo(results[0]!.ReservedEventId);
        await Assert.That(await fixture.Context.AiToolExecutions.CountAsync()).IsEqualTo(1);
    }

    [Test]
    public async Task ContendingNativeCreatorsObserveTheCommittedPrimaryInsteadOfBuildingAnotherGraph()
    {
        using var cancellation = new CancellationTokenSource();
        var control = new PrimaryCommitControl(cancellation) { HoldBeforeCommit = true };
        var contender = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var fixture = await CreateFixtureAsync(services =>
        {
            services.ConfigureDbContext<ExploreDbContext>(
                options => options.AddInterceptors(control), ServiceLifetime.Singleton);
            services.AddScoped<ISettingMutationLock>(provider => new RelationalSettingMutationLock(
                provider.GetRequiredService<ExploreDbContext>(), provider.GetRequiredService<IUnitOfWork>(),
                (_, _) =>
                {
                    if (control.Entered.Task.IsCompleted) contender.TrySetResult();
                    return Task.CompletedTask;
                }));
        });
        var identity = await SeedAsync(fixture);
        var execution = await fixture.Services.GetRequiredService<IAiEventDraftRecoveryRepository>()
            .AcquireAsync(identity, CancellationToken.None);
        control.Target = execution!.ReservedEventId;
        async Task<BaseCommandResponse<Guid>> CreateAsync()
        {
            await using var scope = fixture.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<ICommandHandler<CreateEventCommand, BaseCommandResponse<Guid>>>()
                .ExecuteAsync(CreateEventCommand.FromAiDraft(execution, Payload), cancellation.Token);
        }
        Task<BaseCommandResponse<Guid>> first = Task.Run(CreateAsync);
        Task<BaseCommandResponse<Guid>>? second = null;
        try
        {
            await control.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            second = Task.Run(CreateAsync);
            await contender.Task.WaitAsync(TimeSpan.FromSeconds(15));
        }
        finally
        {
            control.Release.TrySetResult();
        }
        var created = await first.WaitAsync(TimeSpan.FromSeconds(30));
        var replay = await second!.WaitAsync(TimeSpan.FromSeconds(30));
        await Assert.That(created.IsSuccess).IsTrue();
        await Assert.That(replay.IsSuccess).IsTrue();
        await Assert.That(replay.Id).IsEqualTo(created.Id);
        await Assert.That(await fixture.Context.Events.CountAsync()).IsEqualTo(1);
        await Assert.That(await fixture.Context.EventSessions.CountAsync()).IsEqualTo(1);
    }

    [Test]
    public async Task ChangedAuthenticatedPrincipalAndForeignTenantCannotUseRetainedIdentity()
    {
        await using var fixture = await CreateFixtureAsync();
        var identity = await SeedAsync(fixture);
        var repository = fixture.Services.GetRequiredService<IAiEventDraftRecoveryRepository>();
        var execution = await repository.AcquireAsync(identity, CancellationToken.None);
        var http = fixture.Services.GetRequiredService<IHttpContextAccessor>().HttpContext!;
        var principal = http.User;
        http.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, Guid.CreateVersion7().ToString())], "fixture"));
        await Assert.That(await repository.ReadAsync(identity, CancellationToken.None)).IsNull();
        http.User = principal;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repository.AcquireAsync(identity with { TenantId = Guid.CreateVersion7() }, CancellationToken.None));
        await Assert.That((await repository.ReadAsync(identity, CancellationToken.None))!.Id).IsEqualTo(execution!.Id);
    }

    [Test]
    public async Task PostedExecutionIdentityWithoutPersistedAcquisitionCannotBecomeProvenance()
    {
        await using var fixture = await CreateFixtureAsync();
        var identity = await SeedAsync(fixture);
        var action = await fixture.Context.AiProposedActions.AsNoTracking().SingleAsync(value => value.Id == identity.ProposedActionId);
        var forged = AiToolExecution.ReserveEventDraft(Guid.CreateVersion7(), Guid.CreateVersion7(),
            action, fixture.UserId, fixture.ActorId, null, null);
        var result = await fixture.ExecuteAsync<CreateEventCommand, BaseCommandResponse<Guid>>(
            CreateEventCommand.FromAiDraft(forged, Payload));
        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("ai_event_draft_authority_changed");
        await Assert.That(await fixture.Context.Events.CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task MutationOfMappedPayloadCannotReuseConfirmedIdentity()
    {
        await using var fixture = await CreateFixtureAsync();
        var identity = await SeedAsync(fixture);
        var execution = await fixture.Services.GetRequiredService<IAiEventDraftRecoveryRepository>()
            .AcquireAsync(identity, CancellationToken.None);
        var command = CreateEventCommand.FromAiDraft(execution!, Payload);
        var changed = command with { EventDto = command.EventDto with { Title = "Different event" } };
        var result = await fixture.ExecuteAsync<CreateEventCommand, BaseCommandResponse<Guid>>(changed);
        await Assert.That(result.FailureCode).IsEqualTo("ai_event_draft_authority_changed");
        await Assert.That(await fixture.Context.Events.CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task SettlementRequiresSameTransactionAndRejectsStaleOrUnlinkedCompletion()
    {
        await using var fixture = await CreateFixtureAsync();
        var identity = await SeedAsync(fixture);
        var repository = fixture.Services.GetRequiredService<IAiEventDraftRecoveryRepository>();
        var execution = await repository.AcquireAsync(identity, CancellationToken.None);
        var unit = fixture.Services.GetRequiredService<IUnitOfWork>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.ReadForSettlementAsync(
            identity, execution!.Id, execution.ConcurrencyStamp, CancellationToken.None));
        await unit.ExecuteSerializableAsync(async token =>
        {
            await Assert.That(await repository.ReadForSettlementAsync(
                identity, execution!.Id, Guid.CreateVersion7(), token)).IsNull();
            var admitted = await repository.ReadForSettlementAsync(identity, execution.Id, execution.ConcurrencyStamp, token);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
            {
                admitted!.MarkSucceeded(DateTime.UtcNow);
                return Task.CompletedTask;
            });
            return true;
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            unit.ExecuteSerializableAsync(async token =>
            {
                await repository.SaveSettlementAsync(identity, execution!, token);
                return true;
            }));
        await Assert.That(await fixture.Context.Events.CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task ImageSourceChangeCannotReuseReservedCustodyOrPrimaryIdentity()
    {
        await using var fixture = await CreateFixtureAsync();
        var identity = await SeedAsync(fixture, withImage: true);
        var repository = fixture.Services.GetRequiredService<IAiEventDraftRecoveryRepository>();
        var execution = await repository.AcquireAsync(identity, CancellationToken.None);
        await Assert.That(execution!.ImageSourceMessageId).IsNotNull();
        await Assert.That(execution.ImageUploadOperationKey).IsNotNull();
        await fixture.Context.AiMessages.Where(value => value.Id == execution.ImageSourceMessageId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.ImageAttachmentsJson, "[{\"data\":\"changed\"}]"));

        await Assert.That(await repository.ReadAsync(identity, CancellationToken.None)).IsNull();
        await Assert.That(await repository.AcquireAsync(identity, CancellationToken.None)).IsNull();
        await Assert.That(await fixture.Context.AiToolExecutions.CountAsync()).IsEqualTo(1);
        await Assert.That(await fixture.Context.Events.CountAsync()).IsEqualTo(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task NativeSettlementCannotInventImageCustodyOrCompleteWithoutTheRetainedImage(bool complete)
    {
        await using var fixture = await CreateFixtureAsync();
        var identity = await SeedAsync(fixture, withImage: true);
        var repository = fixture.Services.GetRequiredService<IAiEventDraftRecoveryRepository>();
        var reserved = await repository.AcquireAsync(identity, CancellationToken.None);
        var created = await fixture.ExecuteAsync<CreateEventCommand, BaseCommandResponse<Guid>>(
            CreateEventCommand.FromAiDraft(reserved!, Payload));
        await Assert.That(created.IsSuccess).IsTrue();
        fixture.Context.ChangeTracker.Clear();
        var linked = await repository.ReadAsync(identity, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Services.GetRequiredService<IUnitOfWork>().ExecuteSerializableAsync(async token =>
            {
                var execution = await repository.ReadForSettlementAsync(
                    identity, linked!.Id, linked.ConcurrencyStamp, token);
                if (complete)
                    execution!.MarkSucceeded(DateTime.UtcNow);
                else
                    execution!.BindImageUploadSession(execution.ImageSourceMessageId!.Value,
                        execution.ImageSourceDigest!, Guid.CreateVersion7());
                await repository.SaveSettlementAsync(identity, execution, token);
                return true;
            }));
        fixture.Context.ChangeTracker.Clear();
        var retained = await repository.ReadAsync(identity, CancellationToken.None);
        await Assert.That(retained!.PrimaryEventId).IsEqualTo(created.Id);
        await Assert.That(retained.CompletedAt).IsNull();
        await Assert.That(retained.ImageUploadSessionId).IsNull();
        await Assert.That(await fixture.Context.Events.CountAsync()).IsEqualTo(1);
        await Assert.That((await fixture.Context.AiProposedActions.SingleAsync()).Status)
            .IsEqualTo(AiProposedActionStatus.Confirmed);
    }

    [Test]
    [Arguments("member")]
    [Arguments("permission")]
    [Arguments("participation")]
    [Arguments("organization")]
    public async Task RevokedOrganizationCreationAuthorityCannotRecoverRetainedExecution(string revoked)
    {
        await using var fixture = await CreateFixtureAsync();
        var organization = new Organization
        {
            Id = Guid.CreateVersion7(),
            Pii = new OrganizationPii { FullName = "AI organizer" }
        };
        var participation = new OrganizationTenant
        {
            Id = Guid.CreateVersion7(),
            TenantId = fixture.TenantId,
            Tenant = null!,
            OrganizationId = organization.Id,
            Organization = organization,
            ApprovalStatusId = (int)ApprovalStatusEnum.Approved,
            ApprovalStatus = null!,
            IsOrganizerEligible = true
        };
        var member = new OrganizationMember
        {
            Id = Guid.CreateVersion7(),
            TenantId = fixture.TenantId,
            Tenant = null!,
            OrganizationTenantId = participation.Id,
            OrganizationTenant = participation,
            UserId = fixture.UserId,
            User = null!,
            RoleId = (int)RoleEnum.OrgAdmin,
            Role = null!
        };
        var actor = new Actor
        {
            Id = Guid.CreateVersion7(),
            ActorTypeId = (int)ActorTypeEnum.Organization,
            ActorType = null!,
            OrganizationId = organization.Id,
            Pii = new ActorPii { DisplayName = "AI organizer" }
        };
        fixture.Context.AddRange(organization, participation, member, actor);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        var identity = await SeedAsync(fixture, actingActorId: actor.Id);
        var repository = fixture.Services.GetRequiredService<IAiEventDraftRecoveryRepository>();
        var execution = await repository.AcquireAsync(identity, CancellationToken.None);
        await Assert.That(execution).IsNotNull();
        switch (revoked)
        {
            case "member":
                await fixture.Context.OrganizationMembers.Where(value => value.Id == member.Id)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.IsDeleted, true));
                break;
            case "permission":
                await fixture.Context.Set<RolePermission>().Where(value => value.RoleId == (int)RoleEnum.OrgAdmin
                    && value.Permission.MasterCode == PermissionCodes.EventCreate).ExecuteDeleteAsync();
                break;
            case "participation":
                await fixture.Context.OrganizationTenants.Where(value => value.Id == participation.Id)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.IsOrganizerEligible, false));
                break;
            default:
                await fixture.Context.Organizations.Where(value => value.Id == organization.Id)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.IsDeleted, true));
                break;
        }
        await Assert.That(await repository.ReadAsync(identity, CancellationToken.None)).IsNull();
        await Assert.That(await repository.AcquireAsync(identity, CancellationToken.None)).IsNull();
        await Assert.That(await fixture.Context.AiToolExecutions.CountAsync()).IsEqualTo(1);
    }

    [Test]
    public async Task SettlementAtomicallyCompletesExactActionAndDoesNotReopenPrimary()
    {
        await using var fixture = await CreateFixtureAsync();
        var identity = await SeedAsync(fixture);
        var repository = fixture.Services.GetRequiredService<IAiEventDraftRecoveryRepository>();
        var reserved = await repository.AcquireAsync(identity, CancellationToken.None);
        var created = await fixture.ExecuteAsync<CreateEventCommand, BaseCommandResponse<Guid>>(
            CreateEventCommand.FromAiDraft(reserved!, Payload));
        await Assert.That(created.IsSuccess).IsTrue();
        fixture.Context.ChangeTracker.Clear();
        var linked = await repository.ReadAsync(identity, CancellationToken.None);
        var unit = fixture.Services.GetRequiredService<IUnitOfWork>();
        await unit.ExecuteSerializableAsync(async token =>
        {
            await Assert.That(await repository.ReadForSettlementAsync(
                identity, reserved!.Id, reserved.ConcurrencyStamp, token)).IsNull();
            var execution = await repository.ReadForSettlementAsync(identity, linked!.Id, linked.ConcurrencyStamp, token);
            execution!.MarkSucceeded(DateTime.UtcNow);
            await repository.SaveSettlementAsync(identity, execution, token);
            return true;
        });
        fixture.Context.ChangeTracker.Clear();
        var settled = await repository.AcquireAsync(identity, CancellationToken.None);
        var action = await fixture.Context.AiProposedActions.AsNoTracking().SingleAsync(value => value.Id == identity.ProposedActionId);
        await Assert.That(action.Status).IsEqualTo(AiProposedActionStatus.Executed);
        await Assert.That(action.ResultResourceId).IsEqualTo(created.Id);
        await Assert.That(settled!.Succeeded).IsTrue();
        await Assert.That(settled.PrimaryEventId).IsEqualTo(created.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
        {
            settled.MarkFailed("late_failure", null, DateTime.UtcNow);
            return Task.CompletedTask;
        });
        await Assert.That(await fixture.Context.Events.CountAsync()).IsEqualTo(1);
    }

    [Test]
    public async Task DirtyOrAmbientCallerCannotAcquireOrFlushUnrelatedWork()
    {
        await using var fixture = await CreateFixtureAsync();
        var identity = await SeedAsync(fixture);
        var repository = fixture.Services.GetRequiredService<IAiEventDraftRecoveryRepository>();
        var user = await fixture.Context.Users.SingleAsync(value => value.Id == fixture.UserId);
        user.IsDeleted = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.AcquireAsync(identity, CancellationToken.None));
        fixture.Context.ChangeTracker.Clear();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Services.GetRequiredService<IUnitOfWork>().ExecuteSerializableAsync(
                token => repository.AcquireAsync(identity, token)));
        await Assert.That(await fixture.Context.Users.AnyAsync(value => value.Id == fixture.UserId)).IsTrue();
        await Assert.That(await fixture.Context.AiToolExecutions.CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task ExecutedActionWithoutDurableExecutionCannotAdoptAnUnrelatedExistingEvent()
    {
        await using var fixture = await CreateFixtureAsync();
        var identity = await SeedAsync(fixture);
        var existing = await fixture.SeedEventAsync();
        await fixture.Context.AiProposedActions.Where(value => value.Id == identity.ProposedActionId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(value => value.StatusId, (int)AiProposedActionStatus.Executed)
                .SetProperty(value => value.ResultResourceId, (Guid?)existing.Id));
        var repository = fixture.Services.GetRequiredService<IAiEventDraftRecoveryRepository>();
        await Assert.That(await repository.AcquireAsync(identity, CancellationToken.None)).IsNull();
        await Assert.That(await repository.ReadAsync(identity, CancellationToken.None)).IsNull();
        await Assert.That(await fixture.Context.AiToolExecutions.CountAsync()).IsEqualTo(0);
        await Assert.That(await fixture.Context.Events.CountAsync()).IsEqualTo(1);
    }

    [Test]
    public async Task AnotherUsersActorCannotAcquireDespiteMatchingActionAndConversationIds()
    {
        await using var fixture = await CreateFixtureAsync();
        var owner = new User
        {
            Id = Guid.CreateVersion7(),
            Pii = new UserPii { Email = string.Empty, FirstName = "Other", LastName = "Owner" }
        };
        var actor = new Actor
        {
            Id = Guid.CreateVersion7(),
            UserId = owner.Id,
            ActorTypeId = (int)ActorTypeEnum.User,
            ActorType = null!,
            Pii = new ActorPii { DisplayName = "Other owner" }
        };
        fixture.Context.AddRange(owner, actor);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        var identity = await SeedAsync(fixture, actingActorId: actor.Id);
        await Assert.That(await fixture.Services.GetRequiredService<IAiEventDraftRecoveryRepository>()
            .AcquireAsync(identity, CancellationToken.None)).IsNull();
        await Assert.That(await fixture.Context.AiToolExecutions.CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task DeletedPrimaryCannotBeReplacedOnConfirmedActionReplay()
    {
        await using var fixture = await CreateFixtureAsync();
        var identity = await SeedAsync(fixture);
        var repository = fixture.Services.GetRequiredService<IAiEventDraftRecoveryRepository>();
        var execution = await repository.AcquireAsync(identity, CancellationToken.None);
        var command = CreateEventCommand.FromAiDraft(execution!, Payload);
        var created = await fixture.ExecuteAsync<CreateEventCommand, BaseCommandResponse<Guid>>(command);
        await Assert.That(created.IsSuccess).IsTrue();
        fixture.Context.ChangeTracker.Clear();
        await fixture.Context.Events.Where(value => value.Id == created.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.IsDeleted, true));
        await Assert.That(await repository.ReadAsync(identity, CancellationToken.None)).IsNull();
        var replay = await fixture.ExecuteAsync<CreateEventCommand, BaseCommandResponse<Guid>>(command);
        await Assert.That(replay.FailureCode).IsEqualTo("ai_event_draft_authority_changed");
        await Assert.That(await fixture.Context.Events.IgnoreQueryFilters()
            .CountAsync(value => value.TenantId == fixture.TenantId)).IsEqualTo(1);
        await Assert.That(await fixture.Context.AiToolExecutions.CountAsync()).IsEqualTo(1);
    }

    private static async Task<AiEventDraftRecoveryIdentity> SeedAsync(
        EventVisitorCapabilitySqliteFixture fixture, bool withImage = false, Guid? actingActorId = null,
        string imageData = "fixture-only-not-decoded")
    {
        DateTime now = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
        var conversation = new AiConversation
        {
            Id = Guid.CreateVersion7(),
            TenantId = fixture.TenantId,
            UserId = fixture.UserId,
            ActorId = actingActorId ?? fixture.ActorId,
            Title = "Draft recovery",
            CreatedAt = now
        };
        var action = new AiProposedAction
        {
            Id = Guid.CreateVersion7(),
            TenantId = fixture.TenantId,
            ConversationId = conversation.Id,
            Conversation = conversation,
            ActingActorId = actingActorId ?? fixture.ActorId,
            Kind = AiProposedActionKind.CreateEventDraft,
            PayloadJson = Payload,
            CreatedAt = now,
            CreatedBy = fixture.UserId
        };
        if (withImage)
        {
            conversation.AddMessage(AiMessageRole.User, "An image source", fixture.UserId, now,
                JsonSerializer.Serialize(new[] { new { mediaType = "image/png", data = imageData, fileName = "poster.png" } }));
            var proposal = conversation.AddMessage(AiMessageRole.Assistant, "A proposed draft", null, now);
            action.MessageId = proposal.Id;
        }
        action.Confirm(fixture.UserId, now);
        fixture.Context.AddRange(conversation, action);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        return AiEventDraftRecoveryIdentity.FromConfirmedAction(action);
    }

    private sealed class PrimaryCommitControl(CancellationTokenSource cancellation) : DbTransactionInterceptor
    {
        public Guid? Target { get; set; }
        public bool CancelBeforeCommit { get; init; }
        public bool CancelAfterCommit { get; init; }
        public bool FailAfterCommit { get; init; }
        public bool HoldBeforeCommit { get; init; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private bool IsTarget(DbContext? context) => Target is { } target && context is ExploreDbContext db
            && db.ChangeTracker.Entries<AiToolExecution>().Any(value => value.Entity.PrimaryEventId == target);

        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction, TransactionEventData eventData, InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            if (!IsTarget(eventData.Context)) return result;
            if (CancelBeforeCommit)
            {
                Target = null;
                await cancellation.CancelAsync();
                cancellationToken.ThrowIfCancellationRequested();
            }
            if (HoldBeforeCommit)
            {
                Entered.TrySetResult();
                await Release.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
            }
            return result;
        }

        public override async Task TransactionCommittedAsync(
            DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            if (!IsTarget(eventData.Context)) return;
            Target = null;
            if (CancelAfterCommit)
            {
                await cancellation.CancelAsync();
                cancellationToken.ThrowIfCancellationRequested();
            }
            if (FailAfterCommit) throw new IOException("Native primary commit acknowledgement was lost.");
        }
    }
}
