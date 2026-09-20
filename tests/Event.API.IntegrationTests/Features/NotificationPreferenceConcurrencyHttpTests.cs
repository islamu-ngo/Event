using System.Data;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Notification;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel("ApiTestFixture")]
public sealed class NotificationPreferenceConcurrencyHttpTests
{
    private const string Path = "/api/notification/preferences/me";

    [Test]
    public async Task OverlappingTransactionAdmissionsConvergeOnOneCellWithSerializableIsolation()
    {
        var gate = new NotificationTransactionGate();
        await using var factory = await NotificationHttpFixture.CreateAsync(gate);
        using var first = factory.Client(factory.UserId);
        using var second = factory.Client(factory.UserId);
        gate.Arm();
        var pending = first.PatchAsJsonAsync(Path, Preference(true));
        try
        {
            await Assert.That(await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15))).IsEqualTo(IsolationLevel.Serializable);
            using var competing = await second.PatchAsJsonAsync(Path, Preference(false));
            await Assert.That(competing.StatusCode).IsEqualTo(HttpStatusCode.OK);
            var competingResult = (await competing.Content.ReadFromJsonAsync<BaseCommandResponse<Guid>>())!;
            gate.Release.TrySetResult();
            using var completed = await pending.WaitAsync(TimeSpan.FromSeconds(15));
            await Assert.That(completed.StatusCode).IsEqualTo(HttpStatusCode.OK);
            var completedResult = (await completed.Content.ReadFromJsonAsync<BaseCommandResponse<Guid>>())!;
            await Assert.That(completedResult.Id).IsEqualTo(competingResult.Id);
            await Assert.That(await MarketingEnabledAsync(first)).IsTrue();
        }
        finally
        {
            gate.Release.TrySetResult();
        }
    }

    [Test]
    public async Task ErasureFenceArrivingBeforeTransactionAdmissionLeavesEffectivePreferenceDisabled()
    {
        var gate = new NotificationTransactionGate();
        await using var factory = await NotificationHttpFixture.CreateAsync(gate);
        using var client = factory.Client(factory.UserId);
        gate.Arm();
        var pending = client.PatchAsJsonAsync(Path, Preference(true));
        try
        {
            await Assert.That(await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15))).IsEqualTo(IsolationLevel.Serializable);
            using (var scope = factory.Services.CreateScope())
            {
                var repository = scope.ServiceProvider.GetRequiredService<IPrivacyErasureStateRepository>();
                var now = DateTime.UtcNow;
                var intent = PrivacyErasureIntent.Record(Guid.CreateVersion7(), 1, PrivacyErasureSubjectKind.User,
                    factory.UserId, PrivacyErasureReasonCode.SubjectErasureRequest, 1, now, now);
                await repository.AddSagaAsync(PrivacyErasureSaga.Start(intent, 1, RandomNumberGenerator.GetBytes(32), now.AddDays(1), now), default);
                await repository.SaveChangesAsync(default);
            }
            gate.Release.TrySetResult();
            using var response = await pending.WaitAsync(TimeSpan.FromSeconds(15));
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
            await Assert.That(await MarketingEnabledAsync(client)).IsFalse();
            using var repeated = await client.PatchAsJsonAsync(Path, Preference(true));
            await Assert.That(repeated.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
            await Assert.That(await MarketingEnabledAsync(client)).IsFalse();
        }
        finally
        {
            gate.Release.TrySetResult();
        }
    }

    [Test]
    public async Task RequestAbortedBeforeTransactionAdmissionLeavesEffectivePreferenceDisabled()
    {
        var gate = new NotificationTransactionGate();
        await using var factory = await NotificationHttpFixture.CreateAsync(gate);
        using var client = factory.Client(factory.UserId);
        using var cancellation = new CancellationTokenSource();
        gate.Arm();
        var pending = client.PatchAsJsonAsync(Path, Preference(true), cancellation.Token);
        try
        {
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await cancellation.CancelAsync();
            await Assert.That(async () => { using var response = await pending; }).Throws<OperationCanceledException>();
            await gate.Exited.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await Assert.That(await MarketingEnabledAsync(client)).IsFalse();
        }
        finally
        {
            gate.Release.TrySetResult();
        }
    }

    [Test]
    [Arguments("user")]
    [Arguments("organization")]
    [Arguments("group")]
    public async Task HigherScopeCellAndMuteLocksRejectLowerScopeChanges(string scopeName)
    {
        await using var factory = await NotificationHttpFixture.CreateAsync(useCerbos: scopeName == "group");
        using var client = factory.Client(factory.UserId);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            db.NotificationChannelPreferences.Add(new NotificationChannelPreference
            {
                Id = Guid.CreateVersion7(),
                TenantId = PlatformDefaults.DefaultTenantId,
                Tenant = null!,
                ScopeId = (int)ConfigurationScopeEnum.Tenant,
                Scope = null!,
                CategoryId = (int)NotificationPreferenceCategoryEnum.Marketing,
                Category = null!,
                ChannelId = (int)NotificationPreferenceChannelEnum.Email,
                Channel = null!,
                IsEnabled = true,
                IsLocked = true,
                ConcurrencyStamp = Guid.CreateVersion7()
            });
            db.NotificationPreferenceProfiles.Add(new NotificationPreferenceProfile
            {
                Id = Guid.CreateVersion7(),
                TenantId = PlatformDefaults.DefaultTenantId,
                Tenant = null!,
                ScopeId = (int)ConfigurationScopeEnum.Tenant,
                Scope = null!,
                IsMuted = false,
                IsLocked = true
            });
            await db.SaveChangesAsync();
        }
        string path = scopeName == "user" ? Path
            : $"/api/{scopeName}/{(scopeName == "organization" ? factory.OrganizationId : factory.GroupId)}/notification-preferences";
        using var patch = await client.PatchAsJsonAsync(path, Preference(false));
        await Assert.That(patch.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        using var mute = await client.PutAsJsonAsync(path + "/mute", new SetNotificationPreferenceMuteDto { IsMuted = true });
        await Assert.That(mute.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        var matrix = (await client.GetFromJsonAsync<NotificationPreferenceMatrixDto>(path))!;
        var cell = matrix.Cells.Single(cell => cell.CategoryCode == "marketing" && cell.ChannelCode == "email");
        await Assert.That(cell.IsEnabled).IsTrue();
        await Assert.That(cell.IsLocked).IsTrue();
        await Assert.That(cell.IsEditable).IsFalse();
        await Assert.That(cell.IsMuted).IsFalse();
    }

    private static UpdateNotificationPreferenceMatrixDto Preference(bool enabled) => new()
    {
        Cells = [new UpdateNotificationPreferenceCellDto { CategoryCode = "marketing", ChannelCode = "email", IsEnabled = enabled }]
    };

    private static async Task<bool> MarketingEnabledAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<NotificationPreferenceMatrixDto>(Path))!.Cells
            .Single(cell => cell.CategoryCode == "marketing" && cell.ChannelCode == "email").IsEnabled;
}
