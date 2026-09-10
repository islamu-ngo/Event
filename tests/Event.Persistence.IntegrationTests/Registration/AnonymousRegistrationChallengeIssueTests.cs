
using System.Security.Cryptography;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.RegistrationOrders.Commands;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Persistence.IntegrationTests.Registration;

public sealed class AnonymousRegistrationChallengeIssueTests
{
    [Test]
    public async Task NativeIssue_CommitsBoundedChallengeWithoutAllocatingOrderOrHold()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        var target = await fixture.SeedEventAsync(published: true);
        var response = await IssueAsync(fixture, target.Id);
        await Assert.That(response.IsSuccess).IsTrue();
        await Assert.That(response.Challenge).IsNotNull();
        await Assert.That(response.Challenge!.Difficulty).IsEqualTo(18);
        await Assert.That(response.Challenge.Version).IsEqualTo(1);
        await Assert.That(response.Challenge.ProtectedChallenge.Length).IsLessThanOrEqualTo(4096);
        var inventory = fixture.Services.GetRequiredService<IRegistrationInventoryRepository>();
        await Assert.That(await inventory.GetOrdersByEventAsync(target.Id, fixture.TenantId, CancellationToken.None)).IsEmpty();
    }

    [Test]
    public async Task DraftPrivateAccountRequiredAndMissingEventCannotIssue()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        var draft = await fixture.SeedEventAsync();
        var account = await fixture.SeedEventAsync(accountRequired: true, published: true);
        var privateEvent = await fixture.SeedEventAsync(published: true);
        var tracked = await fixture.Context.Events.FindAsync(privateEvent.Id);
        tracked!.VisibilityTypeId = (int)VisibilityTypeEnum.Private;
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        foreach (Guid eventId in new[] { draft.Id, account.Id, privateEvent.Id, Guid.CreateVersion7() })
        {
            var response = await IssueAsync(fixture, eventId);
            await Assert.That(response.IsSuccess).IsFalse();
            await Assert.That(response.Challenge).IsNull();
            await Assert.That(response.FailureCode).IsEqualTo("anonymous_registration_challenge_unavailable");
        }
    }

    [Test]
    public async Task CurrentSharedVisitorAuthorityStopsIssuanceAfterDirectoryOnlyMutation()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        var target = await fixture.SeedEventAsync(published: true);
        await Assert.That((await IssueAsync(fixture, target.Id)).IsSuccess).IsTrue();
        var changed = await fixture.Services.GetRequiredService<IVisitorAccessSettingsWriter>().ApplyAsync(
            [new(null, GovernanceSettingKeys.PublicExperience.VisitorAccessMode,
                VisitorAccessSettingMutationKind.SetValue, "\"DirectoryListingOnly\"")], fixture.UserId);
        await Assert.That(changed.Success).IsTrue();
        var response = await IssueAsync(fixture, target.Id);
        await Assert.That(response.IsSuccess).IsFalse();
        await Assert.That(response.Challenge).IsNull();
        await Assert.That(response.FailureCode).IsEqualTo("anonymous_registration_challenge_unavailable");
    }

    [Test]
    public async Task MalformedTrustedBindingCannotIssueBearer()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        var response = await fixture.ExecuteAsync<IssueAnonymousRegistrationChallengeCommand, AnonymousRegistrationChallengeIssueResult>(
            new(Guid.CreateVersion7(), string.Empty, Guid.CreateVersion7().ToString("N")));
        await Assert.That(response.IsSuccess).IsFalse();
        await Assert.That(response.Challenge).IsNull();
        await Assert.That(response.FailureCode).IsEqualTo("anonymous_registration_challenge_invalid");
    }

    [Test]
    public async Task OperatorDifficultyIsUncachedAndTenantOverrideHonorsInstanceLock()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        var target = await fixture.SeedEventAsync(published: true);
        var instance = new SystemSetting
        {
            Id = Guid.CreateVersion7(),
            SettingKey = GovernanceSettingKeys.AnonymousRegistrationChallenge.Difficulty,
            Value = "\"20\"",
            ValueType = SettingValueType.String
        };
        var tenant = new TenantSetting
        {
            Id = Guid.CreateVersion7(),
            TenantId = fixture.TenantId,
            Tenant = null!,
            SettingKey = GovernanceSettingKeys.AnonymousRegistrationChallenge.Difficulty,
            Value = "\"16\""
        };
        fixture.Context.AddRange(instance, tenant);
        await fixture.Context.SaveChangesAsync();
        await Assert.That((await IssueAsync(fixture, target.Id)).Challenge!.Difficulty).IsEqualTo(16);
        tenant.Value = "\"22\"";
        await fixture.Context.SaveChangesAsync();
        await Assert.That((await IssueAsync(fixture, target.Id)).Challenge!.Difficulty).IsEqualTo(22);
        instance.IsLocked = true;
        await fixture.Context.SaveChangesAsync();
        await Assert.That((await IssueAsync(fixture, target.Id)).Challenge!.Difficulty).IsEqualTo(20);
    }

    [Test]
    public async Task InvalidEffectiveDifficultyFailsClosedIncludingInvalidLockedInstance()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        var target = await fixture.SeedEventAsync(published: true);
        var instance = new SystemSetting
        {
            Id = Guid.CreateVersion7(),
            SettingKey = GovernanceSettingKeys.AnonymousRegistrationChallenge.Difficulty,
            Value = "\"18\"",
            ValueType = SettingValueType.String,
            IsLocked = true
        };
        fixture.Context.AddRange(instance, new TenantSetting
        {
            Id = Guid.CreateVersion7(),
            TenantId = fixture.TenantId,
            Tenant = null!,
            SettingKey = GovernanceSettingKeys.AnonymousRegistrationChallenge.Difficulty,
            Value = "\"16\""
        });
        await fixture.Context.SaveChangesAsync();
        foreach (string invalid in new[] { "\"15\"", "\"23\"", "\"018\"", "18", "null", "{", "\"invalid\"" })
        {
            instance.Value = invalid;
            await fixture.Context.SaveChangesAsync();
            var response = await IssueAsync(fixture, target.Id);
            await Assert.That(response.IsSuccess).IsFalse();
            await Assert.That(response.Challenge).IsNull();
            await Assert.That(response.FailureCode).IsEqualTo("anonymous_registration_challenge_configuration_invalid");
        }
    }

    private static Task<AnonymousRegistrationChallengeIssueResult> IssueAsync(EventVisitorCapabilitySqliteFixture fixture, Guid eventId) =>
        fixture.ExecuteAsync<IssueAnonymousRegistrationChallengeCommand, AnonymousRegistrationChallengeIssueResult>(
            new(eventId, Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), Guid.CreateVersion7().ToString("N")));
}
