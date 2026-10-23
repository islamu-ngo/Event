using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.Authentication.Local.Models;
using Explore.Application.Features.Authentication.Local.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Infrastructure.Authentication;
using Explore.Persistence.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Event.API.IntegrationTests.Features;

[NotInParallel]
public sealed class LocalIdentityPublicFailureHttpTests
{
    private const string LoginPath = "/api/auth/local/login";
    private const string RecoveryPath = "/api/auth/local/password-recoveries";
    private static CancellationToken CancellationToken => TestContext.Current!.Execution.CancellationToken;

    [Test]
    public async Task InterleavedMissingWrongAndLockedCredentialsSharePublicEnvelopeButKeepNativeLockout()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        var missing = new LocalAuthRequestDto($"missing-{Guid.CreateVersion7():N}@example.test", fixture.Login.Password);
        var wrong = fixture.Login with { Password = LocalIdentityLifecycleHttpFixture.NewPassword() };
        var expected = await LoginFailureAsync(fixture, missing);
        await using var scope = fixture.Host.Services.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<LocalIdentityUser>>();
        int threshold = manager.Options.Lockout.MaxFailedAccessAttempts;
        DateTimeOffset? lockedUntil = null;
        for (int attempt = 1; attempt <= threshold; attempt++)
        {
            await Assert.That(await LoginFailureAsync(fixture, wrong)).IsEqualTo(expected);
            await Assert.That(await LoginFailureAsync(fixture, missing)).IsEqualTo(expected);
            await using DbContext read = fixture.Native.CreateIdentityDatabase();
            var current = await read.Set<LocalIdentityUser>().AsNoTracking()
                .SingleAsync(row => row.Id == fixture.Binding.LocalSubjectId, CancellationToken);
            await Assert.That(current.AccessFailedCount).IsEqualTo(attempt == threshold ? 0 : attempt);
            await Assert.That(current.LockoutEnd.HasValue).IsEqualTo(attempt == threshold);
            if (attempt == threshold) lockedUntil = current.LockoutEnd;
        }
        await Assert.That(await LoginFailureAsync(fixture, fixture.Login)).IsEqualTo(expected);
        await Assert.That(await LoginFailureAsync(fixture, wrong)).IsEqualTo(expected);
        await using DbContext lockedRead = fixture.Native.CreateIdentityDatabase();
        var locked = await lockedRead.Set<LocalIdentityUser>().AsNoTracking()
            .SingleAsync(row => row.Id == fixture.Binding.LocalSubjectId, CancellationToken);
        await Assert.That(locked.AccessFailedCount).IsEqualTo(0);
        await Assert.That(locked.LockoutEnd).IsEqualTo(lockedUntil);
    }

    [Test]
    public async Task CorrectUnlockedPasswordResetsNativeFailureCountAndIssuesAuthorityWithoutSmtpHandoff()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await LoginFailureAsync(fixture, fixture.Login with { Password = LocalIdentityLifecycleHttpFixture.NewPassword() });
        await fixture.SignInAsync();
        await using DbContext read = fixture.Native.CreateIdentityDatabase();
        var user = await read.Set<LocalIdentityUser>().AsNoTracking()
            .SingleAsync(row => row.Id == fixture.Binding.LocalSubjectId, CancellationToken);
        await Assert.That(user.AccessFailedCount).IsEqualTo(0);
        await Assert.That(user.LockoutEnd).IsNull();
        await Assert.That(fixture.Smtp.Handoffs.Count).IsEqualTo(0);
    }

    [Test]
    public async Task TargetOnlyPrecommitStoreFailureMatchesMissingIntakeAndDoesNotCreateDurableWork()
    {
        var failure = new IntakeSaveFailure(() => new TransientStoreFailure());
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(interceptor: failure);
        var before = await fixture.ReadIdentityAsync();
        failure.Armed = true;
        using (HttpResponseMessage target = await RequestAsync(fixture, fixture.Login.Identifier))
            await AssertAcceptedAsync(target);
        using (HttpResponseMessage missing = await RequestAsync(fixture, $"missing-{Guid.CreateVersion7():N}@example.test"))
            await AssertAcceptedAsync(missing);
        await Assert.That(failure.Triggered).IsTrue();
        await Assert.That(await fixture.ReadIdentityAsync()).IsEqualTo(before);
        await using DbContext read = fixture.Native.CreateIdentityDatabase();
        await Assert.That(await read.Set<LocalIdentityLifecycleOperation>().CountAsync(CancellationToken)).IsEqualTo(0);
        await using (var scope = fixture.Host.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<LocalIdentityLifecycleDeliveryProcessor>().DrainAsync(CancellationToken);
        await Assert.That(fixture.Smtp.Handoffs.Count).IsEqualTo(0);
        await fixture.SignInAsync();
    }

    [Test]
    public async Task ClassifiedTargetBindingReadFailureSharesMissingIntakeAndPreservesExistingRecovery()
    {
        var failure = new BindingReadFailure();
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(interceptor: failure);
        using (HttpResponseMessage initial = await RequestAsync(fixture, fixture.Login.Identifier))
            await AssertAcceptedAsync(initial);
        await using DbContext identity = fixture.Native.CreateIdentityDatabase();
        var original = await identity.Set<LocalIdentityLifecycleOperation>().AsNoTracking().SingleAsync(CancellationToken);
        await using (var read = fixture.Native.CreateDatabase())
            failure.Table = read.Model.FindEntityType(typeof(UserExternalLogin))!.GetTableName();
        using (HttpResponseMessage target = await RequestAsync(fixture, fixture.Login.Identifier))
            await AssertAcceptedAsync(target);
        using (HttpResponseMessage missing = await RequestAsync(fixture, $"missing-{Guid.CreateVersion7():N}@example.test"))
            await AssertAcceptedAsync(missing);
        await Assert.That(failure.Triggered).IsTrue();
        failure.Table = null;
        await fixture.SignInAsync();
        LocalIdentityLifecycleTransport handoff = await fixture.DrainOneAsync();
        await Assert.That(handoff.Operation.OperationId).IsEqualTo(original.Id);
        await Assert.That(handoff.ExpiresAtUtc.UtcDateTime).IsEqualTo(original.ExpiresAt);
        await Assert.That(await identity.Set<LocalIdentityLifecycleOperation>().CountAsync(CancellationToken)).IsEqualTo(1);
    }

    [Test]
    public async Task MissingSmtpConfigurationPreservesLocalSignInAndCommittedPendingRecovery()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await using (var configure = fixture.Native.CreateDatabase())
        {
            var host = await configure.SystemSettings.SingleAsync(row => row.SettingKey == GovernanceSettingKeys.Email.SmtpHost, CancellationToken);
            host.Value = "\"\"";
            await configure.SaveChangesAsync(CancellationToken);
        }
        using (HttpResponseMessage accepted = await RequestAsync(fixture, fixture.Login.Identifier))
            await AssertAcceptedAsync(accepted);
        await fixture.SignInAsync();
        await using (var scope = fixture.Host.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<LocalIdentityLifecycleDeliveryProcessor>().DrainAsync(CancellationToken);
        await Assert.That(fixture.Smtp.Handoffs.Count).IsEqualTo(0);
        await using DbContext read = fixture.Native.CreateIdentityDatabase();
        var pending = await read.Set<LocalIdentityLifecycleOperation>().AsNoTracking().SingleAsync(CancellationToken);
        await Assert.That(pending.DeliveryState).IsEqualTo(LocalIdentityLifecycleDeliveryState.Pending);
        await fixture.SignInAsync();
    }

    [Test]
    public async Task NativeIntakeCancellationPropagatesAndRollsBackWithoutPublicSuccess()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
        var failure = new IntakeSaveFailure(() =>
        {
            cancellation.Cancel();
            return new OperationCanceledException(cancellation.Token);
        });
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(interceptor: failure);
        failure.Armed = true;
        await using (var scope = fixture.Host.Services.CreateAsyncScope())
        {
            var handler = scope.ServiceProvider.GetRequiredService<
                ICommandHandler<RequestLocalPasswordRecoveryCommand, BaseCommandResponse<Guid>>>();
            await Assert.That(async () => await handler.ExecuteAsync(
                new(new LocalPasswordRecoveryRequestDto { Identifier = fixture.Login.Identifier }), cancellation.Token))
                .Throws<OperationCanceledException>();
        }
        await Assert.That(failure.Triggered).IsTrue();
        await using DbContext read = fixture.Native.CreateIdentityDatabase();
        await Assert.That(await read.Set<LocalIdentityLifecycleOperation>().CountAsync(CancellationToken)).IsEqualTo(0);
    }

    [Test]
    public async Task GenuineIntakeProgrammingFailureIsNotTranslatedIntoSuccess()
    {
        var failure = new IntakeSaveFailure(() => new InvalidOperationException("Controlled invariant failure."));
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(interceptor: failure);
        failure.Armed = true;
        await using (var scope = fixture.Host.Services.CreateAsyncScope())
        {
            var handler = scope.ServiceProvider.GetRequiredService<
                ICommandHandler<RequestLocalPasswordRecoveryCommand, BaseCommandResponse<Guid>>>();
            await Assert.That(async () => await handler.ExecuteAsync(
                new(new LocalPasswordRecoveryRequestDto { Identifier = fixture.Login.Identifier }), CancellationToken))
                .Throws<InvalidOperationException>();
        }
        await Assert.That(failure.Triggered).IsTrue();
        await using DbContext read = fixture.Native.CreateIdentityDatabase();
        await Assert.That(await read.Set<LocalIdentityLifecycleOperation>().CountAsync(CancellationToken)).IsEqualTo(0);
    }

    [Test]
    public async Task CommitAcknowledgementFailureRetainsOriginalOperationAndWorkerHandoff()
    {
        var failure = new CommitAcknowledgementFailure();
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(interceptor: failure);
        failure.Armed = true;
        await using (var scope = fixture.Host.Services.CreateAsyncScope())
        {
            var handler = scope.ServiceProvider.GetRequiredService<
                ICommandHandler<RequestLocalPasswordRecoveryCommand, BaseCommandResponse<Guid>>>();
            await Assert.That(async () => await handler.ExecuteAsync(
                new(new LocalPasswordRecoveryRequestDto { Identifier = fixture.Login.Identifier }), CancellationToken))
                .Throws<TransientStoreFailure>();
        }
        await Assert.That(failure.Triggered).IsTrue();
        await using DbContext read = fixture.Native.CreateIdentityDatabase();
        var original = await read.Set<LocalIdentityLifecycleOperation>().AsNoTracking().SingleAsync(CancellationToken);
        await Assert.That(original.DeliveryState).IsEqualTo(LocalIdentityLifecycleDeliveryState.Pending);
        using (HttpResponseMessage repeated = await RequestAsync(fixture, fixture.Login.Identifier))
            await AssertAcceptedAsync(repeated);
        LocalIdentityLifecycleTransport handoff = await fixture.DrainOneAsync();
        await Assert.That(handoff.Operation.OperationId).IsEqualTo(original.Id);
        await Assert.That(handoff.ExpiresAtUtc.UtcDateTime).IsEqualTo(original.ExpiresAt);
        await Assert.That(await read.Set<LocalIdentityLifecycleOperation>().CountAsync(CancellationToken)).IsEqualTo(1);
        await Assert.That(fixture.Smtp.Handoffs.Count).IsEqualTo(1);
    }

    [Test]
    public async Task CancellationAfterNativeCommitPropagatesButLeavesWorkerOwnedRecovery()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
        var failure = new CommitAcknowledgementFailure(() =>
        {
            cancellation.Cancel();
            return new OperationCanceledException(cancellation.Token);
        });
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(interceptor: failure);
        failure.Armed = true;
        await using (var scope = fixture.Host.Services.CreateAsyncScope())
        {
            var handler = scope.ServiceProvider.GetRequiredService<
                ICommandHandler<RequestLocalPasswordRecoveryCommand, BaseCommandResponse<Guid>>>();
            await Assert.That(async () => await handler.ExecuteAsync(
                new(new LocalPasswordRecoveryRequestDto { Identifier = fixture.Login.Identifier }), cancellation.Token))
                .Throws<OperationCanceledException>();
        }
        await Assert.That(failure.Triggered).IsTrue();
        LocalIdentityLifecycleTransport handoff = await fixture.DrainOneAsync();
        using (HttpResponseMessage repeated = await RequestAsync(fixture, fixture.Login.Identifier))
            await AssertAcceptedAsync(repeated);
        await using var scopeAfter = fixture.Host.Services.CreateAsyncScope();
        await scopeAfter.ServiceProvider.GetRequiredService<LocalIdentityLifecycleDeliveryProcessor>().DrainAsync(CancellationToken);
        await Assert.That(fixture.Smtp.Handoffs.Count).IsEqualTo(1);
        await Assert.That(fixture.Smtp.Handoffs.Single().Operation).IsEqualTo(handoff.Operation);
    }

    private static Task<HttpResponseMessage> RequestAsync(LocalIdentityLifecycleHttpFixture fixture, string identifier) =>
        fixture.Client.PostAsJsonAsync(RecoveryPath, new LocalPasswordRecoveryRequestDto { Identifier = identifier }, CancellationToken);

    private static async Task AssertAcceptedAsync(HttpResponseMessage response)
    {
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Accepted);
        await Assert.That(await response.Content.ReadAsStringAsync(CancellationToken)).IsEqualTo(string.Empty);
        await Assert.That(response.Headers.CacheControl?.Private).IsTrue();
        await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
        await Assert.That(response.Headers.Contains("Set-Cookie")).IsFalse();
    }

    private static async Task<PublicFailure> LoginFailureAsync(LocalIdentityLifecycleHttpFixture fixture, LocalAuthRequestDto request)
    {
        using HttpResponseMessage response = await fixture.Client.PostAsJsonAsync(LoginPath, request, CancellationToken);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("application/problem+json");
        await Assert.That(response.Headers.Contains("Set-Cookie")).IsFalse();
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        JsonElement problem = body.RootElement;
        await Assert.That(problem.GetProperty("code").GetString()).IsEqualTo("invalid_credentials");
        string[] fields = problem.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray();
        await Assert.That(fields.Any(field => field is "token" or "replacementChallenge" or "userId" or "roles" or "expiresAt")).IsFalse();
        return new(problem.GetProperty("status").GetInt32(), problem.GetProperty("type").GetString(),
            problem.GetProperty("title").GetString(), problem.GetProperty("detail").GetString(),
            problem.GetProperty("code").GetString(), problem.GetProperty("instance").GetString(), string.Join(",", fields));
    }

    private sealed record PublicFailure(int Status, string? Type, string? Title, string? Detail, string? Code, string? Instance, string Fields);

    private sealed class IntakeSaveFailure(Func<Exception> failure) : SaveChangesInterceptor
    {
        internal bool Armed { get; set; }
        internal bool Triggered { get; private set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Armed && eventData.Context!.ChangeTracker.Entries<LocalIdentityLifecycleOperation>().Any(entry => entry.State == EntityState.Added))
            {
                Triggered = true;
                throw failure();
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class CommitAcknowledgementFailure(Func<Exception>? failure = null) : DbTransactionInterceptor
    {
        internal bool Armed { get; set; }
        internal bool Triggered { get; private set; }
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (Armed && eventData.Context!.ChangeTracker.Entries<LocalIdentityLifecycleOperation>().Any())
            {
                Armed = false;
                Triggered = true;
                throw failure?.Invoke() ?? new TransientStoreFailure();
            }
            return Task.CompletedTask;
        }
    }

    private sealed class BindingReadFailure : DbCommandInterceptor
    {
        internal string? Table { get; set; }
        internal bool Triggered { get; private set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (Table is not null && command.CommandText.Contains(Table, StringComparison.Ordinal))
            {
                Triggered = true;
                throw new TransientStoreFailure();
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class TransientStoreFailure() : DbException("Controlled transient store failure.")
    {
        public override bool IsTransient => true;
    }
}
