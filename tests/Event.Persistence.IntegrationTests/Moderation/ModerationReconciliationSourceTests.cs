using System.Collections.Immutable;
using System.Security.Claims;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Authentication;
using Explore.Application.Configuration;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Contracts.Services;
using Explore.Application.Services;
using Explore.Application.Services.Moderation;
using Explore.Application.Features.Moderation.Policy;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure.Identity;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Queries;
using Explore.Persistence.Repositories;
using Explore.Persistence.Services;
using ISLAMU.Moderation.Application;
using ISLAMU.Moderation.Domain;
using ISLAMU.Moderation.Domain.Policies;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Event.Persistence.IntegrationTests.Moderation;

/// <summary>Real native SQLite source and transaction semantics; all bodies deferred to programme assurance.</summary>
public sealed class ModerationReconciliationSourceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    [Test]
    [Arguments("scheduled", 1)]
    [Arguments("future", 0)]
    [Arguments("active", 0)]
    [Arguments("expiry", 1)]
    [Arguments("missed-interval", 1)]
    [Arguments("review-overdue", 0)]
    [Arguments("revoked", 0)]
    [Arguments("superseded", 0)]
    [Arguments("expired", 0)]
    public async Task Discovery_selects_only_due_native_contributions_without_review_auto_unban(string state, int expected)
    {
        await using var database = await Database.CreateAsync();
        var contribution = await database.SeedAsync(state);
        await using var context = database.Context();
        var machine = new MachinePrincipalAccessor(new HttpContextAccessor());
        var credential = await database.Credentials(context).AuthenticateAsync(database.TenantId, CancellationToken.None);
        machine.SetPrincipal(credential!);
        var targets = (await new ModerationReconciliationQuery(context, machine, database.Bindings(context, machine))
            .ReadDueAsync(database.TenantId, Now, 100, null, CancellationToken.None)).Targets;
        await Assert.That(targets.Count).IsEqualTo(expected);
        if (expected == 1)
        {
            await Assert.That(targets.Single().SanctionId).IsEqualTo(contribution.Id);
            await Assert.That(targets.Single().DecisionRevision).IsEqualTo(5);
            await Assert.That(targets.Single().SanctionRevision).IsEqualTo(1);
            await Assert.That(targets.Single().PolicyRevision).IsEqualTo(1);
            await Assert.That(targets.Single().PolicyId).IsEqualTo(database.Publications[database.Scope].Proposed.PolicyId);
        }
        await Assert.That(context.ModerationTrustDomainId is null).IsTrue();
        await Assert.That(context.ChangeTracker.Entries().Any()).IsFalse();
        await Assert.That(machine.Current).IsEqualTo(credential);
    }

    [Test]
    [Arguments("scheduled")]
    [Arguments("expiry")]
    [Arguments("missed-interval")]
    public async Task Retiring_support_retains_selected_declarations_for_existing_sanctions(string state)
    {
        await using var database = await Database.CreateAsync();
        var sanction = await database.SeedAsync(state);
        var policyId = database.Publications[database.Scope].Proposed.PolicyId;
        await database.PublishAsync(database.Scope, retire: true);
        var target = await database.DiscoverAsync();
        await Assert.That(target.SanctionId).IsEqualTo(sanction.Id);
        await Assert.That(target.PolicyId).IsEqualTo(policyId);
        await Assert.That(target.PolicyRevision).IsEqualTo(2);
        await using var reader = database.Context();
        reader.ModerationTrustDomainId = database.Instance.TrustDomainId;
        await Assert.That(await reader.ModerationPolicies.CountAsync()).IsEqualTo(0);
        await Assert.That(await reader.ModerationPolicyRules.CountAsync()).IsEqualTo(0);
        await Assert.That((await reader.ModerationPolicySelections.SingleAsync()).ActivePublicationId).IsNull();
    }

    [Test]
    [Arguments("instance")]
    [Arguments("tenant")]
    [Arguments("resource")]
    public async Task Rich_jurisdiction_is_separate_from_exact_sanction_scope(string kind)
    {
        await using var database = await Database.CreateAsync();
        var scope = kind == "instance" ? database.Instance : kind == "resource"
            ? AuthorityScope.Resource(database.Scope, Guid.CreateVersion7()) : database.Scope;
        var sanction = await database.SeedAsync("expiry", scope);
        var target = await database.DiscoverAsync();
        var jurisdiction = kind == "instance" ? database.Instance : database.Scope;
        await Assert.That(target.Scope).IsEqualTo(scope);
        await Assert.That(target.SanctionId).IsEqualTo(sanction.Id);
        await Assert.That(target.PolicyId).IsEqualTo(database.Publications[jurisdiction].Proposed.PolicyId);
        await using var reader = database.Context();
        reader.ModerationTrustDomainId = database.Instance.TrustDomainId;
        var selected = await reader.ModerationPolicyPublications.SingleAsync();
        await Assert.That(selected.Jurisdiction).IsEqualTo(jurisdiction);
        await Assert.That(selected.PublishedActive).IsFalse();
    }

    [Test]
    [Arguments("resource")]
    [Arguments("event")]
    public async Task Discovery_preserves_the_exact_typed_subject_origin(string kind)
    {
        await using var database = await Database.CreateAsync();
        var id = Guid.CreateVersion7();
        var origin = kind == "event"
            ? AuthorityScope.Event(database.Scope, id)
            : AuthorityScope.Resource(database.Scope, id);
        var sanction = await database.SeedAsync("expiry", subjectOrigin: origin);

        var target = await database.DiscoverAsync();

        await Assert.That(target.SanctionId).IsEqualTo(sanction.Id);
        await Assert.That(target.Subject).IsEqualTo(sanction.Subject);
        await Assert.That(target.Subject.Origin).IsEqualTo(origin);
        await Assert.That(target.Command().Subject).IsEqualTo(sanction.Subject);
        await Assert.That(target.Scope).IsEqualTo(database.Scope);
    }

    [Test]
    public async Task A_different_decision_Event_origin_cannot_qualify_the_original_sanction()
    {
        await using var database = await Database.CreateAsync();
        var origin = AuthorityScope.Event(database.Scope, Guid.CreateVersion7());
        await database.SeedAsync("expiry", subjectOrigin: origin);
        await using (var writer = database.Context())
        {
            writer.ModerationTrustDomainId = database.Instance.TrustDomainId;
            await writer.ModerationDecisions.ExecuteUpdateAsync(update =>
                update.SetProperty(row => row.SubjectOriginEventId, (Guid?)Guid.CreateVersion7()));
        }
        await using var context = database.Context();
        var machine = new MachinePrincipalAccessor(new HttpContextAccessor());
        machine.SetPrincipal((await database.Credentials(context)
            .AuthenticateAsync(database.TenantId, CancellationToken.None))!);

        var batch = await new ModerationReconciliationQuery(context, machine, database.Bindings(context, machine))
            .ReadDueAsync(database.TenantId, Now, 100, null, CancellationToken.None);

        await Assert.That(batch.Targets.Count).IsEqualTo(0);
        await Assert.That(batch.LastScannedSanctionId).IsNull();
        await Assert.That(context.ModerationTrustDomainId).IsNull();
        await Assert.That(context.ChangeTracker.Entries().Any()).IsFalse();
    }

    [Test]
    [Arguments("selection-unavailable")]
    [Arguments("publication-unavailable")]
    [Arguments("host-mismatch")]
    [Arguments("jurisdiction-mismatch")]
    [Arguments("revision-mismatch")]
    [Arguments("instance-mismatch")]
    [Arguments("rule-mismatch")]
    [Arguments("scope-unavailable")]
    [Arguments("subject-unavailable")]
    public async Task Unavailable_rich_selection_advances_the_scanned_cursor_without_making_a_target(string rejection)
    {
        await using var database = await Database.CreateAsync();
        var sanction = await database.SeedAsync("expiry");
        await using (var writer = database.Context())
        {
            writer.ModerationTrustDomainId = database.Instance.TrustDomainId;
            switch (rejection)
            {
                case "selection-unavailable":
                    writer.ModerationPolicySelections.Remove(await writer.ModerationPolicySelections.SingleAsync());
                    await writer.SaveChangesAsync();
                    break;
                case "publication-unavailable":
                    await writer.ModerationPolicyPublications.ExecuteUpdateAsync(update =>
                        update.SetProperty(row => row.JurisdictionTenantId, (Guid?)Guid.CreateVersion7()));
                    break;
                case "host-mismatch":
                    await writer.ModerationPolicySelections.ExecuteUpdateAsync(update =>
                        update.SetProperty(row => row.HostBindingId, Guid.CreateVersion7()));
                    break;
                case "jurisdiction-mismatch":
                    await writer.ModerationPolicySelections.ExecuteUpdateAsync(update =>
                        update.SetProperty(row => row.JurisdictionId, Guid.CreateVersion7()));
                    break;
                case "revision-mismatch":
                    await writer.ModerationPolicySelections.ExecuteUpdateAsync(update =>
                        update.SetProperty(row => row.Revision, 2));
                    break;
                case "instance-mismatch":
                    await writer.ModerationPolicyPublications.ExecuteUpdateAsync(update =>
                        update.SetProperty(row => row.InstanceId, Guid.CreateVersion7()));
                    break;
                case "rule-mismatch":
                    await writer.Set<ModerationPolicyPublicationProfile>().ExecuteUpdateAsync(update =>
                        update.SetProperty(row => row.RuleCode, "other.rule"));
                    break;
                case "scope-unavailable":
                    await writer.Set<ModerationPolicyPublicationScope>().ExecuteDeleteAsync();
                    break;
                case "subject-unavailable":
                    await writer.Set<ModerationPolicyPublicationSubject>().ExecuteDeleteAsync();
                    break;
            }
        }
        await using var context = database.Context();
        var machine = new MachinePrincipalAccessor(new HttpContextAccessor());
        machine.SetPrincipal((await database.Credentials(context).AuthenticateAsync(database.TenantId, CancellationToken.None))!);
        var query = new ModerationReconciliationQuery(context, machine, database.Bindings(context, machine));
        var batch = await query.ReadDueAsync(database.TenantId, Now, 1, null, CancellationToken.None);
        await Assert.That(batch.Targets.Count).IsEqualTo(0);
        await Assert.That(batch.LastScannedSanctionId).IsEqualTo(sanction.Id);
        var ended = await query.ReadDueAsync(database.TenantId, Now, 1, batch.LastScannedSanctionId, CancellationToken.None);
        await Assert.That(ended.LastScannedSanctionId).IsNull();
        await Assert.That(context.ModerationTrustDomainId).IsNull();
        await Assert.That(context.ChangeTracker.Entries().Any()).IsFalse();
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    public async Task A_draft_in_the_active_pointer_cannot_replace_an_unavailable_latest_declaration()
    {
        await using var database = await Database.CreateAsync();
        var sanction = await database.SeedAsync("expiry");
        var priorDraftId = database.Publications[database.Scope].Proposed.PublicationId;
        await database.PublishAsync(database.Scope);
        var latestId = database.Publications[database.Scope].Proposed.PublicationId;
        await using (var writer = database.Context())
        {
            writer.ModerationTrustDomainId = database.Instance.TrustDomainId;
            // Deliberately inconsistent selection, while retaining real writer-produced drafts.
            // No active evidence, conformance, or live permission is manufactured by this guard.
            await writer.ModerationPolicySelections.ExecuteUpdateAsync(update =>
                update.SetProperty(row => row.ActivePublicationId, (Guid?)priorDraftId));
            await writer.ModerationPolicyPublications.Where(row => row.Id == latestId).ExecuteUpdateAsync(update =>
                update.SetProperty(row => row.JurisdictionTenantId, (Guid?)Guid.CreateVersion7()));
        }
        await using var context = database.Context();
        var machine = new MachinePrincipalAccessor(new HttpContextAccessor());
        machine.SetPrincipal((await database.Credentials(context).AuthenticateAsync(database.TenantId, CancellationToken.None))!);
        var batch = await new ModerationReconciliationQuery(context, machine, database.Bindings(context, machine))
            .ReadDueAsync(database.TenantId, Now, 1, null, CancellationToken.None);
        await Assert.That(batch.Targets.Count).IsEqualTo(0);
        await Assert.That(batch.LastScannedSanctionId).IsEqualTo(sanction.Id);
    }

    [Test]
    public async Task Denied_leading_selection_cannot_starve_a_later_native_jurisdiction()
    {
        await using var database = await Database.CreateAsync();
        var skipped = await database.SeedAsync("expiry",
            sanctionId: Guid.Parse("019a7ed0-0000-7000-8000-000000000001"));
        var available = await database.SeedAsync("expiry", database.Instance,
            Guid.Parse("019a7ed0-0000-7000-8000-000000000002"));
        await using (var writer = database.Context())
        {
            writer.ModerationTrustDomainId = database.Instance.TrustDomainId;
            writer.ModerationPolicySelections.Remove(await writer.ModerationPolicySelections.SingleAsync(
                row => row.JurisdictionId == database.TenantId));
            await writer.SaveChangesAsync();
        }
        await using var context = database.Context();
        var machine = new MachinePrincipalAccessor(new HttpContextAccessor());
        machine.SetPrincipal((await database.Credentials(context).AuthenticateAsync(database.TenantId, CancellationToken.None))!);
        var query = new ModerationReconciliationQuery(context, machine, database.Bindings(context, machine));
        var first = await query.ReadDueAsync(database.TenantId, Now, 1, null, CancellationToken.None);
        await Assert.That(first.Targets.Count).IsEqualTo(0);
        await Assert.That(first.LastScannedSanctionId).IsEqualTo(skipped.Id);
        var next = await query.ReadDueAsync(database.TenantId, Now, 1, first.LastScannedSanctionId, CancellationToken.None);
        await Assert.That(next.Targets.Single().SanctionId).IsEqualTo(available.Id);
        await Assert.That(next.LastScannedSanctionId).IsEqualTo(available.Id);
    }

    [Test]
    [Arguments("wrong-secret")]
    [Arguments("expired")]
    [Arguments("inactive")]
    [Arguments("configured-only")]
    [Arguments("other-tenant")]
    [Arguments("disabled")]
    public async Task Background_authentication_requires_live_native_key_and_real_secret_possession(string rejection)
    {
        await using var database = await Database.CreateAsync();
        await using var context = database.Context();
        switch (rejection)
        {
            case "wrong-secret":
                database.Credential = ApiKeyHashing.FormatPersistedApiKey(database.KeyId, ApiKeyHashing.CreateSecret());
                break;
            case "configured-only":
                database.Credential = ApiKeyHashing.CreateSecret();
                break;
            case "disabled":
                database.Settings.Enabled = false;
                break;
            default:
                var key = await context.ExternalApiKeys.SingleAsync();
                if (rejection == "expired") key.ExpiresAt = Now.UtcDateTime;
                if (rejection == "inactive") key.ExternalApiKeyStatusId = (int)ExternalApiKeyStatusEnum.Revoked;
                if (rejection == "other-tenant") database.Settings.TenantId = Guid.CreateVersion7();
                await context.SaveChangesAsync();
                context.ChangeTracker.Clear();
                break;
        }
        var credential = await database.Credentials(context).AuthenticateAsync(database.TenantId, CancellationToken.None);
        await Assert.That(credential is null).IsTrue();
    }

    [Test]
    public async Task Native_pending_rotation_key_authenticates_without_borrowing_a_configured_credential()
    {
        await using var database = await Database.CreateAsync();
        await using var context = database.Context();
        var key = await context.ExternalApiKeys.SingleAsync();
        key.ExternalApiKeyStatusId = (int)ExternalApiKeyStatusEnum.PendingRotation;
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var credential = await database.Credentials(context).AuthenticateAsync(database.TenantId, CancellationToken.None);
        await Assert.That(credential!.CredentialSource).IsEqualTo(ApiKeyCredentialSource.Persisted);
        await Assert.That(credential.KeyId).IsEqualTo(database.KeyId);
    }

    [Test]
    [Arguments("binding")]
    [Arguments("service")]
    [Arguments("epoch")]
    [Arguments("tenant")]
    public async Task Human_owner_and_installed_coordinates_cannot_replace_current_maintenance_qualification(string rejection)
    {
        await using var database = await Database.CreateAsync();
        await database.SeedAsync("expiry");
        await using var context = database.Context();
        if (rejection == "binding")
            context.ModerationMachineSubjectBindings.Remove(await context.ModerationMachineSubjectBindings.SingleAsync());
        if (rejection == "service")
            (await context.ServicePrincipals.SingleAsync()).IsDeleted = true;
        if (rejection == "epoch")
            context.ModerationQualificationEpochs.Remove(await context.ModerationQualificationEpochs.SingleAsync());
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var machine = new MachinePrincipalAccessor(new HttpContextAccessor());
        machine.SetPrincipal((await database.Credentials(context).AuthenticateAsync(database.TenantId, CancellationToken.None))!);
        var rejected = false;
        try
        {
            await new ModerationReconciliationQuery(context, machine, database.Bindings(context, machine))
                .ReadDueAsync(rejection == "tenant" ? Guid.CreateVersion7() : database.TenantId, Now, 100, null, CancellationToken.None);
        }
        catch (InvalidOperationException)
        {
            rejected = true;
        }
        await Assert.That(rejected).IsTrue();
        await Assert.That(context.ModerationTrustDomainId is null).IsTrue();
    }

    [Test]
    public async Task Bounded_discovery_keeps_foreign_tenant_and_foreign_host_rows_out()
    {
        await using var database = await Database.CreateAsync();
        await database.SeedAsync("expiry");
        await database.SeedAsync("expiry");
        await database.SeedAsync("expiry", AuthorityScope.Tenant(database.Instance, Guid.CreateVersion7()));
        var foreignInstance = AuthorityScope.Instance(database.Instance.Parent!, Guid.CreateVersion7());
        await database.SeedAsync("expiry", AuthorityScope.Tenant(foreignInstance, database.TenantId));
        await using var context = database.Context();
        var machine = new MachinePrincipalAccessor(new HttpContextAccessor());
        machine.SetPrincipal((await database.Credentials(context).AuthenticateAsync(database.TenantId, CancellationToken.None))!);
        var query = new ModerationReconciliationQuery(context, machine, database.Bindings(context, machine));
        var all = (await query.ReadDueAsync(database.TenantId, Now, 100, null, CancellationToken.None)).Targets;
        var bounded = (await query.ReadDueAsync(database.TenantId, Now, 1, null, CancellationToken.None)).Targets;
        await Assert.That(all.Count).IsEqualTo(2);
        await Assert.That(bounded.Count).IsEqualTo(1);
        await Assert.That(all.All(row => row.Scope == database.Scope)).IsTrue();
        await Assert.That(context.ChangeTracker.Entries().Any()).IsFalse();
    }

    [Test]
    public async Task Recovery_cursor_advances_past_a_denied_leading_target_and_wraps_at_the_end()
    {
        await using var database = await Database.CreateAsync();
        var scheduled = await database.SeedAsync("scheduled",
            sanctionId: Guid.Parse("019a7ed0-0000-7000-8000-000000000001"));
        var expired = await database.SeedAsync("expiry",
            sanctionId: Guid.Parse("019a7ed0-0000-7000-8000-000000000002"));
        await using var context = database.Context();
        var machine = new MachinePrincipalAccessor(new HttpContextAccessor());
        machine.SetPrincipal((await database.Credentials(context)
            .AuthenticateAsync(database.TenantId, CancellationToken.None))!);
        var query = new ModerationReconciliationQuery(context, machine, database.Bindings(context, machine));
        var first = await query.ReadDueAsync(database.TenantId, Now, 1, null, CancellationToken.None);
        await Assert.That(first.Targets.Single().SanctionId).IsEqualTo(scheduled.Id);
        await Assert.That((await database.ExecuteAsync(first.Targets.Single().Operation())).Outcome)
            .IsEqualTo(ModerationOperationOutcome.Denied);
        var second = await query.ReadDueAsync(database.TenantId, Now, 1, first.LastScannedSanctionId, CancellationToken.None);
        await Assert.That(second.Targets.Single().SanctionId).IsEqualTo(expired.Id);
        await Assert.That((await database.ExecuteAsync(second.Targets.Single().Operation())).Outcome)
            .IsEqualTo(ModerationOperationOutcome.Unavailable);
        var ended = await query.ReadDueAsync(database.TenantId, Now, 1, second.LastScannedSanctionId, CancellationToken.None);
        await Assert.That(ended.LastScannedSanctionId).IsNull();
        var wrapped = await query.ReadDueAsync(database.TenantId, Now, 1, null, CancellationToken.None);
        await Assert.That(wrapped.Targets.Single().SanctionId).IsEqualTo(scheduled.Id);
    }

    [Test]
    public async Task Expiry_is_effective_but_missing_disclosure_cannot_commit_reconciliation()
    {
        await using var database = await Database.CreateAsync();
        var contribution = await database.SeedAsync("expiry");
        await Assert.That(contribution.IsEffectiveAt(Now)).IsFalse();
        var target = await database.DiscoverAsync();
        var operation = target.Operation();
        var applied = await database.ExecuteAsync(operation);
        var replay = await database.ExecuteAsync((target with { }).Operation());
        await Assert.That(applied.Outcome).IsEqualTo(ModerationOperationOutcome.Unavailable);
        await Assert.That(replay).IsEqualTo(applied);
        await using var reader = database.Context();
        reader.ModerationTrustDomainId = database.Instance.TrustDomainId;
        var stored = await reader.ModerationSanctions.SingleAsync();
        await Assert.That(stored.State).IsEqualTo(contribution.State);
        await Assert.That(stored.Revision).IsEqualTo(contribution.Revision);
        await Assert.That(stored.IsEffectiveAt(Now)).IsFalse();
        await Assert.That(await reader.ModerationOperationReceipts.CountAsync()).IsEqualTo(0);
        await Assert.That(await reader.ModerationEffectIntents.CountAsync()).IsEqualTo(0);
        await Assert.That(await reader.ModerationNoticeSources.AnyAsync()).IsFalse();
    }

    [Test]
    public async Task Scheduled_recovery_does_not_activate_without_native_finding_approval_and_conformance()
    {
        await using var database = await Database.CreateAsync();
        await database.SeedAsync("scheduled");
        var result = await database.ExecuteAsync((await database.DiscoverAsync()).Operation());
        await Assert.That(result.Outcome).IsEqualTo(ModerationOperationOutcome.Denied);
        await using var reader = database.Context();
        reader.ModerationTrustDomainId = database.Instance.TrustDomainId;
        await Assert.That((await reader.ModerationSanctions.SingleAsync()).State).IsEqualTo(ModerationSanctionState.Scheduled);
        await Assert.That(await reader.ModerationOperationReceipts.CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task Credential_revocation_after_discovery_prevents_recovery_in_a_fresh_scope()
    {
        await using var database = await Database.CreateAsync();
        await database.SeedAsync("expiry");
        var target = await database.DiscoverAsync();
        await using (var writer = database.Context())
        {
            var key = await writer.ExternalApiKeys.SingleAsync();
            key.ExternalApiKeyStatusId = (int)ExternalApiKeyStatusEnum.Revoked;
            await writer.SaveChangesAsync();
        }
        await using var context = database.Context();
        var credential = await database.Credentials(context).AuthenticateAsync(target.TenantId, CancellationToken.None);
        await Assert.That(credential is null).IsTrue();
        await Assert.That(context.ChangeTracker.Entries().Any()).IsFalse();
    }

    [Test]
    public async Task Denied_scheduled_target_cannot_contaminate_the_next_expiry_transaction()
    {
        await using var database = await Database.CreateAsync();
        var scheduled = await database.SeedAsync("scheduled");
        var expired = await database.SeedAsync("expiry");
        await using var discovery = database.Context();
        var machine = new MachinePrincipalAccessor(new HttpContextAccessor());
        machine.SetPrincipal((await database.Credentials(discovery).AuthenticateAsync(database.TenantId, CancellationToken.None))!);
        var targets = (await new ModerationReconciliationQuery(discovery, machine, database.Bindings(discovery, machine))
            .ReadDueAsync(database.TenantId, Now, 100, null, CancellationToken.None)).Targets;
        var denied = await database.ExecuteAsync(targets.Single(row => row.SanctionId == scheduled.Id).Operation());
        var applied = await database.ExecuteAsync(targets.Single(row => row.SanctionId == expired.Id).Operation());
        await Assert.That(denied.Outcome).IsEqualTo(ModerationOperationOutcome.Denied);
        await Assert.That(applied.Outcome).IsEqualTo(ModerationOperationOutcome.Unavailable);
        await using var reader = database.Context();
        reader.ModerationTrustDomainId = database.Instance.TrustDomainId;
        await Assert.That(await reader.ModerationOperationReceipts.AnyAsync()).IsFalse();
        await Assert.That(await reader.ModerationEffectIntents.AnyAsync()).IsFalse();
    }

    [Test]
    public async Task Binding_removed_after_real_authentication_is_denied_by_native_transaction_authority()
    {
        await using var database = await Database.CreateAsync();
        await database.SeedAsync("expiry");
        var target = await database.DiscoverAsync();
        var result = await database.ExecuteAsync(target.Operation(), async () =>
        {
            await using var writer = database.Context();
            writer.ModerationMachineSubjectBindings.Remove(await writer.ModerationMachineSubjectBindings.SingleAsync());
            await writer.SaveChangesAsync();
        });
        await Assert.That(result.Outcome).IsEqualTo(ModerationOperationOutcome.Denied);
        await using var reader = database.Context();
        reader.ModerationTrustDomainId = database.Instance.TrustDomainId;
        await Assert.That(await reader.ModerationOperationReceipts.CountAsync()).IsEqualTo(0);
        await Assert.That((await reader.ModerationSanctions.SingleAsync()).State).IsEqualTo(ModerationSanctionState.Active);
    }

    [Test]
    public async Task Concurrent_revocation_defeats_stale_recovery_and_does_not_resurrect_history()
    {
        await using var database = await Database.CreateAsync();
        await database.SeedAsync("expiry");
        var target = await database.DiscoverAsync();
        await using (var writer = database.Context())
        {
            writer.ModerationTrustDomainId = database.Instance.TrustDomainId;
            var sanction = await writer.ModerationSanctions.SingleAsync();
            sanction.Revoke(1, Now);
            await writer.SaveChangesAsync();
        }
        var result = await database.ExecuteAsync(target.Operation());
        await Assert.That(result.Outcome).IsEqualTo(ModerationOperationOutcome.Conflict);
        await using var reader = database.Context();
        reader.ModerationTrustDomainId = database.Instance.TrustDomainId;
        await Assert.That((await reader.ModerationSanctions.SingleAsync()).State).IsEqualTo(ModerationSanctionState.Revoked);
        await Assert.That(await reader.ModerationOperationReceipts.CountAsync()).IsEqualTo(0);
    }

    private sealed class Database(SqliteConnection connection) : IAsyncDisposable, ITenantContext
    {
        private readonly DbContextOptions<ExploreDbContext> _options =
            TestDbContextOptions.Create<ExploreDbContext>().UseSqlite(connection).UseSnakeCaseNamingConvention()
                .AddInterceptors(SqliteNamedLockTransactionInterceptor.Instance).Options;
        public Guid TenantId { get; } = Guid.CreateVersion7();
        public Guid OwnerId { get; } = Guid.CreateVersion7();
        public Guid MaintenanceId { get; } = Guid.CreateVersion7();
        public Guid HostId { get; } = Guid.CreateVersion7();
        public Guid EpochId { get; } = Guid.CreateVersion7();
        public Dictionary<AuthorityScope, ModerationPolicyPublicationRequest> Publications { get; } = [];
        public ModerationCapabilityRegistry Inventory { get; } =
            ModerationCapabilityRegistry.Parse(ModerationCapabilityRegistry.ReadInventory());
        public string KeyId { get; } = ApiKeyHashing.CreateKeyId();
        public AuthorityScope Instance { get; } = AuthorityScope.Instance(
            AuthorityScope.Solution(AuthorityScope.TrustDomain(Guid.CreateVersion7()), Guid.CreateVersion7()), Guid.CreateVersion7());
        public AuthorityScope Scope => AuthorityScope.Tenant(Instance, TenantId);
        public ModerationReconciliationOptions Settings { get; } = new() { Enabled = true };
        public string Credential { get; set; } = string.Empty;
        public ExploreDbContext Context() => new(_options) { TenantContext = this };

        public static async Task<Database> CreateAsync()
        {
            var connection = await SqliteTestDatabaseFactory.CreateOpenIsolatedConnectionAsync();
            await LocalIdentitySqliteTemplate.CopySeededApplicationToAsync(connection, CancellationToken.None);
            var database = new Database(connection);
            database.Settings.TenantId = database.TenantId;
            var secret = ApiKeyHashing.CreateSecret();
            database.Credential = ApiKeyHashing.FormatPersistedApiKey(database.KeyId, secret);
            await using var context = database.Context();
            var status = await context.TenantStatuses.SingleAsync(row => row.IsActiveState);
            var tenant = new Tenant
            {
                Id = database.TenantId, Slug = $"reconcile-{database.TenantId:N}", FullName = "Reconciliation",
                TenantStatusId = status.Id, TenantStatus = status
            };
            var service = new ServicePrincipal
            {
                Id = database.MaintenanceId, Code = $"reconcile-{database.MaintenanceId:N}", DisplayName = "Maintenance"
            };
            var host = ModerationHostBinding.Establish(database.HostId, database.Instance.TrustDomainId,
                database.Instance.Parent!.Id, database.Instance.Id, service.Id);
            context.AddRange(tenant, service, host, ModerationQualificationEpoch.Initialize(database.EpochId, host, tenant));
            var role = await context.Roles.SingleAsync(row =>
                row.RoleScopeId == (int)RoleScopeEnum.Platform && row.MasterCode == "platform.admin");
            var administrator = new User
            {
                Id = database.OwnerId,
                Pii = new UserPii { UserId = database.OwnerId, Email = $"{database.OwnerId:N}@example.invalid",
                    FirstName = "Recovery", LastName = "Administrator" }
            };
            context.Users.Add(administrator);
            context.PlatformUserRoles.Add(new PlatformUserRole
                { Id = Guid.CreateVersion7(), UserId = administrator.Id, User = administrator, RoleId = role.Id, Role = role });
            context.TenantUsers.Add(new TenantUser
            {
                Id = Guid.CreateVersion7(), TenantId = database.TenantId, Tenant = tenant,
                UserId = administrator.Id, User = administrator, StatusId = (int)TenantUserStatusEnum.Active
            });
            context.ExternalApiKeys.Add(new ExternalApiKey
            {
                Id = Guid.CreateVersion7(), KeyId = database.KeyId, Name = "Reconciliation",
                SecretHash = ApiKeyHashing.ComputeHash(secret), Scopes = "events:read",
                OwnerType = ExternalApiKeyOwnerType.InstanceAdmin, OwnerId = database.OwnerId,
                ExternalApiKeyStatusId = (int)ExternalApiKeyStatusEnum.Active, ExternalApiKeyStatus = null!,
                ExternalApiKeyCreditPeriodId = (int)ExternalApiKeyCreditPeriodEnum.None, ExternalApiKeyCreditPeriod = null!
            });
            context.ModerationMachineSubjectBindings.Add(ModerationMachineSubjectBinding.Establish(
                Guid.CreateVersion7(), host.Id, ModerationMachineCredentialSource.Persisted,
                database.KeyId, null, ExternalApiKeyOwnerType.InstanceAdmin, database.OwnerId, service.Id));
            await context.SaveChangesAsync();
            return database;
        }

        public ModerationReconciliationCredentialProvider Credentials(ExploreDbContext context) =>
            new(new SettingsMonitor(Settings), new ExternalApiKeyRepository(context), new SelectedSecret(this), new FixedClock());

        private static AdminContext Admin(ExploreDbContext context) =>
            new(new HttpContextAccessor(), new PlatformUserRoleRepository(context), new TenantUserRoleGrantRepository(context),
                new OrganizationMemberRepository(context), new GroupMemberRepository(context),
                new UserExternalLoginRepository(context), NullLogger<AdminContext>.Instance);

        public ModerationMachineSubjectBindingRepository Bindings(ExploreDbContext context, MachinePrincipalAccessor machine) =>
            new(context, Admin(context), machine, new ExternalApiKeyRepository(context), new UnconfiguredKeys(), new FixedClock());

        public async Task PublishAsync(AuthorityScope jurisdiction, bool retire = false)
        {
            await using var context = Context();
            var http = new HttpContextAccessor { HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, OwnerId.ToString())], "reconciliation-publication"))
            }};
            var admin = new AdminContext(http, new PlatformUserRoleRepository(context), new TenantUserRoleGrantRepository(context),
                new OrganizationMemberRepository(context), new GroupMemberRepository(context),
                new UserExternalLoginRepository(context), NullLogger<AdminContext>.Instance);
            var epoch = await context.ModerationQualificationEpochs.AsNoTracking().SingleAsync();
            ModerationPolicyPublicationRequest request;
            if (Publications.TryGetValue(jurisdiction, out var previous))
                request = previous with
                {
                    OperationId = Guid.CreateVersion7(), ExpectedQualificationRevision = epoch.Revision,
                    ExpectedPublicationId = previous.Proposed.PublicationId,
                    Proposed = previous.Proposed with
                    {
                        PublicationId = Guid.CreateVersion7(), ExpectedRevision = previous.Proposed.ExpectedRevision + 1,
                        SupportedProfiles = retire ? [] : previous.Proposed.SupportedProfiles
                    }
                };
            else
            {
                var reference = ModerationReferenceCatalogue.GetProfile(ModerationReferenceProfileId.CFG06);
                var response = reference.Responses[0];
                ModerationRuleReference Rule(string code) => ModerationRuleReference.Create(jurisdiction.Id, code, 1);
                var profile = new ModerationPolicyProfileDefinition(Guid.CreateVersion7(), Rule("recovery.rule"),
                    Rule("recovery.reason"), reference.Id, reference.Revision, response.Grade,
                    reference.ScopeKinds.Where(kind => kind >= jurisdiction.Kind).Order().ToImmutableArray(),
                    [ModerationSubjectKind.Human], reference.Effects.ToImmutableArray(), reference.Bundle,
                    reference.ProtectionPurpose, reference.Strategies.Order().ToImmutableArray(),
                    new(response.Timing.Kind, response.Timing.Duration), response.RequiredApprovals, 6,
                    ModerationReferenceRecognition.Never);
                var proposed = new ModerationPolicyProposedState(Guid.CreateVersion7(), 0, Guid.CreateVersion7(), jurisdiction,
                    ModerationReferenceCatalogue.Revision, Inventory.Revision, Inventory.Fingerprint,
                    ModerationPolicyRequestedState.Draft, [ModerationNativeMode.Embedded],
                    [new(Rule("recovery.class"), "Publication misuse.", reference.Ground, reference.Basis)],
                    [new(Rule("recovery.reason"), "Recovery witness.", Rule("recovery.class"))],
                    [profile], retire ? [] : [profile.Rule]);
                request = new(Guid.CreateVersion7(), HostId, EpochId, epoch.Revision,
                    ModerationPolicyPublicationProjection.WriterGeneration, null, proposed);
            }
            var result = await new ModerationPolicyPublicationRepository(
                context, admin, new MachinePrincipalAccessor(http), Inventory, null, new FixedClock())
                .PublishAsync(request);
            if (result.Outcome != ModerationPolicyPublicationOutcome.Known)
                throw new InvalidOperationException($"Native reconciliation draft publication failed: {result.Outcome}.");
            Publications[jurisdiction] = request;
        }

        public async Task<ModerationSanction> SeedAsync(string state, AuthorityScope? scope = null,
            Guid? sanctionId = null, AuthorityScope? subjectOrigin = null)
        {
            scope ??= Scope;
            var jurisdiction = scope == Instance ? Instance : Scope;
            if (!Publications.ContainsKey(jurisdiction)) await PublishAsync(jurisdiction);
            var origin = scope;
            while (origin.Kind != ModerationScopeKind.Instance) origin = origin.Parent!;
            var subject = SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), subjectOrigin ?? origin);
            var issuer = SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), origin);
            var reviewer = SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), origin);
            var secondReviewer = SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), origin);
            var created = Now.AddHours(-2);
            var starts = state is "scheduled" or "future" or "missed-interval" ? Now : created;
            if (state == "future") starts = Now.AddHours(1);
            if (state == "missed-interval") starts = Now.AddHours(-1);
            DateTimeOffset? ends = state is "expiry" or "expired" or "missed-interval" ? Now : Now.AddHours(2);
            DateTimeOffset? review = null;
            if (state == "review-overdue") { ends = null; review = Now.AddHours(-1); }
            var approvals = ends is null ? 2 : 1;
            var allegation = ModerationCase.Open(Guid.CreateVersion7(), subject, scope, Guid.CreateVersion7(), created);
            await using var context = Context();
            context.ModerationTrustDomainId = Instance.TrustDomainId;
            var rule = Publications[jurisdiction].Proposed.Profiles.Single().Rule;
            var decision = ModerationDecision.Propose(Guid.CreateVersion7(), allegation.Id, subject, issuer,
                origin, scope, rule, [ModerationCapability.Publish], starts, ends, review, approvals, created);
            decision.RecordResponse(1, created);
            decision.AcceptFinding(Guid.CreateVersion7(), 2, created);
            decision.Approve(reviewer, scope, 3, created);
            if (approvals == 2) decision.Approve(secondReviewer, scope, 4, created);
            var sanction = decision.Activate(sanctionId ?? Guid.CreateVersion7(), 3 + approvals, created);
            if (state == "revoked") sanction.Revoke(1, Now);
            if (state == "superseded") sanction.Supersede(Guid.CreateVersion7(), 1, Now);
            if (state == "expired") sanction.Reconcile(1, Now);
            foreach (var human in new[] { subject.Id, issuer.Id, reviewer.Id, secondReviewer.Id })
                context.Users.Add(new User
                {
                    Id = human, Pii = new UserPii { UserId = human, Email = $"{human:N}@example.invalid", FirstName = "Recovery", LastName = "Witness" }
                });
            context.AddRange(allegation, decision, sanction);
            await context.SaveChangesAsync();
            return sanction;
        }

        public async Task<Explore.Application.Contracts.Scheduling.ModerationReconciliationTarget> DiscoverAsync()
        {
            await using var context = Context();
            var machine = new MachinePrincipalAccessor(new HttpContextAccessor());
            machine.SetPrincipal((await Credentials(context).AuthenticateAsync(TenantId, CancellationToken.None))!);
            return (await new ModerationReconciliationQuery(context, machine, Bindings(context, machine))
                .ReadDueAsync(TenantId, Now, 100, null, CancellationToken.None)).Targets.Single();
        }

        public async Task<ModerationOperationResult> ExecuteAsync(ModerationOperation operation, Func<Task>? beforeExecution = null)
        {
            await using var context = Context();
            var machine = new MachinePrincipalAccessor(new HttpContextAccessor());
            machine.SetPrincipal((await Credentials(context).AuthenticateAsync(TenantId, CancellationToken.None))!);
            if (beforeExecution is not null) await beforeExecution();
            var clock = new FixedClock();
            await using var privacyFixture = await ModerationNoticePrivacyFixture.CreateAsync();
            var privacyAuthority = privacyFixture.Authority(context, clock);
            var participant = new ModerationAuthorityParticipant(context, Admin(context), machine, Bindings(context, machine),
                new ExternalApiKeyRepository(context),
                new ModerationPolicyPublicationRepository(context, Admin(context), machine, Inventory, null, clock),
                Inventory, new ModerationNativeActivationQualification(context, Inventory, null, null, clock), clock);
            var result = await new ModerationOperationService(new ModerationStore(context, participant, clock, privacyAuthority,
                    ModerationLedgerPersistenceTests.Capture(context, privacyAuthority, participant, clock)),
                participant, new ModerationEffectPlanner(), clock).ExecuteAsync(operation);
            await Assert.That(context.ModerationTrustDomainId is null).IsTrue();
            await Assert.That(context.ChangeTracker.Entries().Any()).IsFalse();
            machine.Clear();
            await Assert.That(machine.IsMachineCaller).IsFalse();
            return result;
        }

        public ValueTask DisposeAsync() => connection.DisposeAsync();
    }

    private sealed class SettingsMonitor(ModerationReconciliationOptions settings) : IOptionsMonitor<ModerationReconciliationOptions>
    {
        public ModerationReconciliationOptions CurrentValue => settings;
        public ModerationReconciliationOptions Get(string? name) => settings;
        public IDisposable? OnChange(Action<ModerationReconciliationOptions, string?> listener) => null;
    }

    private sealed class SelectedSecret(Database database) : IModerationReconciliationSecretProvider
    {
        public ValueTask<string?> ReadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<string?>(database.Credential);
        }
    }

    private sealed class UnconfiguredKeys : IConfiguredApiKeyResourceAuthorityReader
    {
        public ValueTask<IConfiguredApiKeyResourceAuthorityLease?> AcquireAuthenticatedAsync(
            string keyId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IConfiguredApiKeyResourceAuthorityLease?>(null);

        public ValueTask<IConfiguredApiKeyResourceAuthorityLease?> AcquireAsync(
            string keyId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IConfiguredApiKeyResourceAuthorityLease?>(null);

        public ValueTask<ApiKeyPrincipalContext?> ReadAsync(string keyId, DateTimeOffset evaluationUtc, CancellationToken cancellationToken) =>
            ValueTask.FromResult<ApiKeyPrincipalContext?>(null);
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
