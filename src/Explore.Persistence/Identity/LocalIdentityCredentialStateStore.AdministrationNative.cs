using System.Data.Common;
using System.Text.Json;
using Explore.Application.Contracts.Identity;
using Explore.Application.Features.Authentication.Local.Requests.Commands;
using Explore.Application.Features.Authentication.Local.Requests.Queries;
using Explore.Persistence.Security;
using Explore.Persistence.Database.ProviderPrimitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Native = Explore.Persistence.Security.PostgresLocalAdministrationContract;

namespace Explore.Persistence.Identity;

internal sealed partial class LocalIdentityCredentialStateStore
{
    private NativeAdministrationAttempt? _nativeAdministration;
    private NativeIssuedImage? _nativeIssued;

    private sealed class NativeIssuedImage(ILocalAdministrationInvocation invocation, object request,
        JsonElement data, LocalCredentialResetReceipt? selfReset)
    {
        internal ILocalAdministrationInvocation Invocation { get; } = invocation;
        internal object Request { get; } = request;
        internal JsonElement Data { get; set; } = data;
        internal LocalCredentialResetReceipt? SelfReset { get; } = selfReset;
        internal bool Committed { get; set; }
    }

    /// <summary>
    /// An operation-bound DATA capability. It retains the original owner and compiler
    /// expectation; an apply/read result is evidence, never a replacement expectation.
    /// </summary>
    private sealed class NativeAdministrationAttempt(
        LocalIdentityCredentialStateStore store, HumanAdministrationAttempt human, object originalRequest,
        Native.Request request, PostgresNativeQueueEnrollment enrollment)
    {
        private readonly RelationalConnectionIdentity connection = RelationalConnectionIdentity.Capture(store.ApplicationOwner);
        private readonly DbTransaction transaction = human.Transaction.GetDbTransaction();
        private readonly DbContextId lease = store.ApplicationOwner.ContextId;
        private JsonElement before;
        private JsonElement expected;
        private JsonElement disclosed;
        private bool acquired;
        private bool compiled;
        private bool applied;
        private bool completed;

        internal Native.Request Request { get; } = request;
        internal object OriginalRequest { get; } = originalRequest;
        internal JsonElement Before => before.GetProperty("Data");
        internal JsonElement Expected => compiled ? expected
            : throw new InvalidOperationException("The Local compiler has not produced an expectation.");
        internal string Outcome { get; private set; } = string.Empty;

        internal void RequireOwner()
        {
            var context = store.ApplicationOwner;
            context.ValidateTenantLease();
            if (!ReferenceEquals(store._administrationAttempt, human)
                || !ReferenceEquals(store._nativeAdministration, this)
                || !ReferenceEquals(context, store.IdentityOwner) || context.ContextId != lease
                || context.TenantFilterTenantId != Request.TenantId || context.IsTenantFilterBypassed
                || !context.HasHumanCommitContextLease || !context.HumanCommitAttemptOwnsTransaction
                || !connection.Matches(context)
                || !ReferenceEquals(context.Database.CurrentTransaction?.GetDbTransaction(), transaction)
                || !connection.Owns(transaction) || completed)
                throw new UnauthorizedAccessException("Local DATA custody left its original physical owner.");
        }

        internal async Task ReadAsync(CancellationToken token)
        {
            RequireOwner();
            if (before.ValueKind != JsonValueKind.Undefined)
                throw new InvalidOperationException("The original Local preimage is read once.");
            before = Parse(await Native.ReadAsync(store.ApplicationOwner, enrollment, Request, token));
        }

        internal async Task AcquireAsync(CancellationToken token)
        {
            RequireOwner();
            if (acquired) throw new InvalidOperationException("Local DATA custody cannot be reacquired.");
            var admitted = Parse(await Native.AcquireAsync(store.ApplicationOwner, enrollment, Request,
                before.GetRawText(), token));
            RequireSame(before, admitted);
            acquired = true;
        }

        internal async Task CompileAsync(Native.Accepted accepted, CancellationToken token)
        {
            RequireOwner();
            if (!acquired || compiled) throw new InvalidOperationException("Local DATA compilation requires one acquired stage.");
            var plan = Parse(await Native.CompileAsync(store.ApplicationOwner, enrollment, Request, accepted, token));
            expected = plan.GetProperty("Data").Clone();
            Outcome = plan.GetProperty("Outcome").GetString()
                ?? throw new InvalidOperationException("The Local compiler outcome is absent.");
            compiled = true;
        }

        internal async Task ApplyAsync(CancellationToken token)
        {
            RequireOwner();
            if (!compiled || applied) throw new InvalidOperationException("A Local compiled group can execute only once.");
            var result = Parse(await Native.ApplyPlanAsync(store.ApplicationOwner, enrollment, Request, token));
            if (result.GetProperty("Outcome").GetString() != Outcome)
                throw new UnauthorizedAccessException("The Local applied outcome differs from its compiler.");
            disclosed = result.GetProperty("Image").Clone();
            RequireSame(expected, disclosed.GetProperty("Data"));
            applied = true;
        }

        internal async Task ReadOnlyAsync(CancellationToken token)
        {
            await CompileAsync(new(), token);
            await ApplyAsync(token);
        }

        internal async Task<JsonElement> ReadCurrentAsync(CancellationToken token)
        {
            RequireOwner();
            return Parse(await Native.ReadAsync(store.ApplicationOwner, enrollment, Request, token))
                .GetProperty("Data").Clone();
        }

        internal async Task CompleteAsync(CancellationToken token)
        {
            RequireOwner();
            if (!applied) throw new InvalidOperationException("The complete Local group must execute before settlement.");
            var observed = Parse(await Native.CompleteAsync(store.ApplicationOwner, enrollment, Request,
                disclosed.GetRawText(), token));
            RequireSame(expected, observed.GetProperty("Data"));
            RequireSame(disclosed, observed);
            completed = true;
        }

        internal void RequireCompleted()
        {
            if (!completed)
                throw new UnauthorizedAccessException("Local native completion has not acknowledged its exact image.");
        }

        internal void RequireCreatedUser(LocalIdentityUser candidate)
        {
            var image = Row(Expected, nameof(LocalIdentityUser), candidate.Id)
                ?? throw new UnauthorizedAccessException("The compiled Local user is absent.");
            // The contract checked the explicit inventory against this model. Do not
            // introduce another inventory which silently accepts future mapped fields.
            foreach (var property in store.ApplicationOwner.Model.FindEntityType(typeof(LocalIdentityUser))!.GetProperties())
            {
                object? actual = property.PropertyInfo!.GetValue(candidate);
                object? required = image.GetProperty(property.Name).Deserialize(property.ClrType, SerializerOptions);
                if (!Equals(actual, required))
                    throw new NotSupportedException("Configured Identity behavior is outside the closed Local creation contract.");
            }
        }

        internal static JsonElement Parse(string json) => JsonSerializer.Deserialize<JsonElement>(json, SerializerOptions);
        internal static void RequireSame(JsonElement expectedImage, JsonElement actualImage)
        {
            if (!JsonElement.DeepEquals(expectedImage, actualImage))
                throw new UnauthorizedAccessException("The Local image differs from its private expected image.");
        }
    }

    private Native.Request NativeRequest(ILocalAdministrationInvocation invocation, object original,
        LocalAdministrationStage stage, LocalCredentialProvisioningSnapshot? snapshot)
    {
        Guid tenant = applicationDbContext.TenantFilterTenantId
            ?? throw new UnauthorizedAccessException("Local native administration requires the selected Runtime tenant.");
        return original switch
        {
            CreateLocalIdentityCommand create => Creation(create),
            ResetLocalCredentialCommand reset => Reset(reset),
            ReconcileLocalCredentialOperationCommand reconcile => new("Reconcile", stage, invocation.PrincipalId,
                tenant, reconcile.OperationId, ExpectedOperationConcurrencyStamp: snapshot?.OperationConcurrencyStamp),
            GetLocalCredentialOperationQuery status => new("Status", stage, invocation.PrincipalId, tenant, status.OperationId),
            ListLocalIdentitiesQuery list => new("List", stage, invocation.PrincipalId, tenant,
                PageNumber: list.PageNumber, PageSize: list.PageSize),
            _ => throw new UnauthorizedAccessException("Unknown original Local request.")
        };

        Native.Request Creation(CreateLocalIdentityCommand command)
        {
            var normalized = new LocalCredentialCreateRequest(command.OperationId, invocation.PrincipalId,
                command.Email, command.FirstName, command.LastName);
            return new("Create", stage, invocation.PrincipalId, tenant, command.OperationId,
                ExpectedOperationConcurrencyStamp: snapshot?.OperationConcurrencyStamp,
                Email: normalized.Email, FirstName: normalized.FirstName, LastName: normalized.LastName,
                NormalizedUserName: userManager.NormalizeName(normalized.Username),
                NormalizedEmail: userManager.NormalizeEmail(normalized.Email));
        }

        Native.Request Reset(ResetLocalCredentialCommand command)
        {
            var normalized = new LocalCredentialResetRequest(command.OperationId, invocation.PrincipalId,
                command.LocalSubjectId, command.ExpectedCurrentOperationId,
                command.ExpectedCurrentOperationConcurrencyStamp, command.Reason);
            return new("Reset", stage, invocation.PrincipalId, tenant, normalized.OperationId,
                normalized.LocalSubjectId, normalized.ExpectedCurrentOperationId,
                normalized.ExpectedCurrentOperationConcurrencyStamp, Reason: normalized.Reason);
        }
    }

    private void RequireIssuedImage(NativeAdministrationAttempt native)
    {
        if (_nativeIssued is not { } issued) return;
        if (!ReferenceEquals(issued.Invocation, _administrationAttempt!.Invocation)
            || !ReferenceEquals(issued.Request, native.OriginalRequest) || !issued.Committed)
            throw new UnauthorizedAccessException("The issued Local credential has no acknowledged original producer.");
        NativeAdministrationAttempt.RequireSame(issued.Data, native.Before);
    }

    private async Task<LocalCredentialResetReceipt?> ValidateNativeSelfResetAsync(
        ILocalAdministrationInvocation invocation, CancellationToken token)
    {
        if (_nativeIssued is not { SelfReset: { } receipt } issued) return null;
        var attempt = _administrationAttempt
            ?? throw new UnauthorizedAccessException("The self-reset has no original owner.");
        var native = _nativeAdministration
            ?? throw new UnauthorizedAccessException("The self-reset has no private DATA capability.");
        native.RequireOwner();
        if (!ReferenceEquals(issued.Invocation, invocation) || !ReferenceEquals(attempt.Invocation, invocation)
            || !ReferenceEquals(issued.Request, native.OriginalRequest)
            || issued.Committed && attempt.Stage != LocalAdministrationStage.Disclose
            || !issued.Committed && attempt.Stage != LocalAdministrationStage.Reset)
            throw new UnauthorizedAccessException("Self-reset custody cannot move to a different lifecycle.");
        // Disclose reads before source Acquire/Compare, so it does not depend on a
        // successful comparison of the original, intentionally superseded stamp.
        var current = issued.Committed ? native.Before : await native.ReadCurrentAsync(token);
        NativeAdministrationAttempt.RequireSame(issued.Data, current);
        return receipt;
    }

    private static IEnumerable<JsonElement> Rows(JsonElement data, string type) =>
        data.GetProperty(type).EnumerateArray();

    private static JsonElement? Row(JsonElement data, string type, Guid id, string key = "Id") =>
        Rows(data, type).Where(row => row.GetProperty(key).GetGuid() == id)
            .Select(row => (JsonElement?)row).SingleOrDefault();

    private static string? Text(JsonElement row, string name) => row.GetProperty(name).GetString();

    private DateTime NativeInstant()
    {
        DateTime instant = timeProvider.GetUtcNow().UtcDateTime;
        return new DateTime(instant.Ticks - instant.Ticks % 10, DateTimeKind.Utc);
    }
}
