using System.Data;
using Explore.Application.Contracts.Moderation;
using Explore.Application.Contracts.Secrets;
using Explore.Domain;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Security;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;
using NSubstitute;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationPolicyPublicationTests
{
    [Test]
    [Arguments("copy")]
    [Arguments("added-parent")]
    [Arguments("subject")]
    [Arguments("retention")]
    [Arguments("deadline")]
    [Arguments("transaction")]
    [Arguments("during-qualification")]
    [Arguments("failed-staging")]
    public async Task Tenant_recognition_reference_requires_its_exact_qualified_binding_and_original_owner(string attack)
    {
        await using var scenario = await ContinuityScenario.CreateAsync();
        var fixture = scenario.Fixture;
        await using var context = fixture.Database.Context();
        await using var human = new HumanSecuritySourceGate(context);
        await using var humanLease = await human.AcquireAsync();
        await using var discovery = new EventDiscoveryEnrollmentGate(context);
        await using var discoveryLease = await discovery.AcquireAsync();
        var service = scenario.Service(context);
        IDbContextTransaction? replacement = null;
        ModerationRecognitionContinuityBinding? interrupted = null;
        bool substituted = false;
        if (attack == "during-qualification")
        {
            async Task<SecretResolutionResult> ResolveAsync(CancellationToken token)
            {
                if (!substituted)
                {
                    substituted = true;
                    await context.Database.CurrentTransaction!.DisposeAsync();
                    replacement = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
                }
                return SecretResolutionResult.Resolved(scenario.Keys.Selection(fixture.Database.Scope.TrustDomainId));
            }
            fixture.SelectedSecrets.ResolveAsync(ModerationRecognitionKeyRing.SecretSettingKey, null, Arg.Any<CancellationToken>())
                .Returns(call => ResolveAsync(call.Arg<CancellationToken>()));
        }
        try
        {
            await service.ExecuteWithRecognitionLockAsync(token =>
                fixture.Retained.Repository.ExecuteSerializedAsync(async inner =>
                {
                    await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, inner);
                    context.Users.Add(new User
                    {
                        Id = scenario.FreshSubject,
                        Pii = new UserPii
                        { UserId = scenario.FreshSubject, Email = string.Empty, FirstName = "Fresh", LastName = "Subject" }
                    });
                    EventHandler<EntityTrackedEventArgs>? failStaging = null;
                    if (attack == "failed-staging")
                    {
                        failStaging = (_, args) =>
                        {
                            if (args.Entry is { State: EntityState.Added, Entity: ModerationRecognitionContinuityBinding binding })
                            {
                                interrupted = binding;
                                throw new InvalidOperationException("Injected continuity staging failure.");
                            }
                        };
                        context.ChangeTracker.Tracked += failStaging;
                    }
                    ModerationRecognitionContinuityResult result;
                    try
                    {
                        result = await service.MatchAndBindAsync(
                            new(scenario.Account, scenario.FreshSubject, fixture.Database.Scope.Id), inner);
                    }
                    finally
                    {
                        if (failStaging is not null) context.ChangeTracker.Tracked -= failStaging;
                    }
                    if (attack == "during-qualification")
                    {
                        await Assert.That(substituted).IsTrue();
                        await Assert.That(result.ActionFailure).IsNotEqualTo(ModerationRecognitionContinuityFailure.None);
                        await Assert.That(context.ChangeTracker.Entries<ModerationRecognitionContinuityBinding>()
                            .Any(entry => entry.State == EntityState.Added)).IsFalse();
                        return true;
                    }
                    ModerationRecognitionContinuityBinding qualified;
                    if (attack == "failed-staging")
                    {
                        await Assert.That(interrupted).IsNotNull();
                        await Assert.That(result.ActionFailure).IsNotEqualTo(ModerationRecognitionContinuityFailure.None);
                        qualified = interrupted!;
                        await Assert.That(context.Entry(qualified).State).IsEqualTo(EntityState.Detached);
                        context.Add(qualified);
                    }
                    else
                    {
                        await Assert.That(result.ActionFailure).IsEqualTo(ModerationRecognitionContinuityFailure.None);
                        qualified = context.ChangeTracker.Entries<ModerationRecognitionContinuityBinding>()
                            .Single(entry => entry.State == EntityState.Added).Entity;
                    }
                    switch (attack)
                    {
                        case "copy":
                        case "added-parent":
                            var copy = (ModerationRecognitionContinuityBinding)context.Entry(qualified).CurrentValues.ToObject();
                            context.Entry(qualified).State = EntityState.Detached;
                            context.Add(copy);
                            if (attack == "added-parent")
                            {
                                var parent = (await fixture.Repository(context).FindAsync(fixture.Request.IntentId, inner))!;
                                context.Add(parent);
                            }
                            break;
                        case "subject":
                            context.Entry(qualified).Property(row => row.FreshSubjectId).CurrentValue = Guid.CreateVersion7();
                            break;
                        case "retention":
                            context.Entry(qualified).Property(row => row.RetainUntil).CurrentValue = qualified.RetainUntil.AddDays(1);
                            break;
                        case "deadline":
                            fixture.Clock.UtcNow = result.CurrentUntil!.Value;
                            break;
                        case "transaction":
                            await transaction.DisposeAsync();
                            replacement = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, inner);
                            break;
                    }
                    var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync(inner))
                        ?? throw new InvalidOperationException("The altered recognition reference was not refused.");
                    if (attack is "copy" or "added-parent" or "retention" or "deadline" or "failed-staging")
                    {
                        await Assert.That(refusal.Data["TrackedEntityType"])
                            .IsEqualTo(nameof(ModerationRecognitionContinuityBinding));
                        await Assert.That(refusal.Data["PrincipalEntityType"])
                            .IsEqualTo(nameof(ModerationRecognitionCaptureItem));
                    }
                    return true;
                }, token), CancellationToken.None);
        }
        finally
        {
            if (replacement is not null) await replacement.DisposeAsync();
        }
        await using var verification = fixture.Database.MaintenanceContext();
        await Assert.That(await verification.Set<ModerationRecognitionContinuityBinding>()
            .IgnoreAllFilters(TenantFilterBypassReasons.UserPrivacyErasure).AnyAsync()).IsFalse();
    }
}
