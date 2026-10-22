using Explore.Application.Authentication;
using Explore.Application.Contracts.Identity;
using Explore.Application.Models;
using Explore.Application.Notifications;
using Explore.Domain.Enums;
using Explore.Infrastructure;
using Explore.Infrastructure.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Event.Api.IntegrationTests.Features;

/// <summary>
/// Exercises the real background/email entry points and their owned DI lifetimes.
/// Test stores detect construction on the wrong owner; they do not supply native identity authority.
/// </summary>
public sealed class LocalIdentityLifecycleOwnerSelectionTests
{
    [Test]
    public async Task WorkerSelectsBeforeConstructionAndDisposesCancelledPendingScan()
    {
        await using var composition = new Composition();
        composition.State.PauseSynchronization = true;
        using var cancellation = new CancellationTokenSource();
        using var worker = composition.Worker();
        Task execution = worker.RunOnceAsync(cancellation.Token);
        try
        {
            await ObserveBeforeCompletion(composition.Factory.Requested.Task, execution);
            await Assert.That(composition.State.AmbientStoreConstructed).IsFalse();
            await Assert.That(composition.State.Owners.Count).IsEqualTo(0);
            composition.Factory.Release.TrySetResult();
            await ObserveBeforeCompletion(composition.State.SynchronizationStarted.Task, execution);
            var owner = composition.State.Owners.Single();
            await Assert.That(owner.Disposed).IsFalse();
            cancellation.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => execution);
            await Assert.That(owner.Disposed).IsTrue();
            await Assert.That(owner.DeliveryRead).IsFalse();
        }
        finally
        {
            cancellation.Cancel();
            composition.Factory.Release.TrySetResult();
            composition.State.ReleaseSynchronization.TrySetResult();
            await ObserveSettlement(execution);
        }
    }

    [Test]
    public async Task WorkerPendingScanAndDeliveryUseSeparateSelectedLifetimes()
    {
        await using var composition = new Composition();
        composition.Factory.Release.TrySetResult();
        using var worker = composition.Worker();

        await worker.RunOnceAsync(CancellationToken.None);

        await Assert.That(composition.State.AmbientStoreConstructed).IsFalse();
        await Assert.That(composition.State.Owners.Count).IsEqualTo(2);
        var scan = composition.State.Owners[0];
        var delivery = composition.State.Owners[1];
        await Assert.That(ReferenceEquals(scan, delivery)).IsFalse();
        await Assert.That(scan.SynchronizationRead && !scan.DeliveryRead).IsTrue();
        await Assert.That(delivery.DeliveryRead && !delivery.SynchronizationRead).IsTrue();
        await Assert.That(scan.Disposed && delivery.Disposed).IsTrue();
        await Assert.That(delivery.OverlappedEarlierOwner).IsFalse();
    }

    [Test]
    public async Task DeliveryFailureReleasesItsSelectedOwnerAndAllowsTheNextPass()
    {
        await using var composition = new Composition();
        composition.Factory.Release.TrySetResult();
        composition.State.FailDeliveryRead = true;
        using var worker = composition.Worker();

        await Assert.ThrowsAsync<DeliveryReadFailure>(() => worker.RunOnceAsync(CancellationToken.None));
        var failedOwners = composition.State.Owners.ToArray();
        await Assert.That(failedOwners.Length).IsEqualTo(2);
        await Assert.That(failedOwners.All(owner => owner.Disposed)).IsTrue();
        composition.State.FailDeliveryRead = false;

        await worker.RunOnceAsync(CancellationToken.None);

        var successfulDelivery = composition.State.Owners.Last();
        await Assert.That(successfulDelivery.DeliveryRead && successfulDelivery.Disposed).IsTrue();
        await Assert.That(failedOwners.Contains(successfulDelivery)).IsFalse();
        await Assert.That(composition.State.AmbientStoreConstructed).IsFalse();
    }

    [Test]
    public async Task EmailProviderConstructsLazilyAndRecordsOnItsSelectedOwner()
    {
        await using var composition = new Composition();
        await using var requestScope = composition.Ambient.CreateAsyncScope();
        var provider = requestScope.ServiceProvider.GetRequiredService<IAccountAuthorityLifecycleEmailProvider>();
        await Assert.That(provider.Kind).IsEqualTo(AccountAuthorityKind.LocalIdentity);
        await Assert.That(composition.State.AmbientStoreConstructed).IsFalse();
        Task<AccountAuthorityLifecycleEmailResult> execution = provider.RequestAsync(
            AccountAuthorityLifecycleEmailAction.EmailVerification, composition.State.Request, composition.State.Authority);
        try
        {
            await ObserveBeforeCompletion(composition.Factory.Requested.Task, execution);
            await Assert.That(composition.State.Owners.Count).IsEqualTo(0);
            composition.Factory.Release.TrySetResult();
            var result = await execution.WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.That(result.Status).IsEqualTo(AccountAuthorityLifecycleEmailStatus.DelegationRecorded);
            await Assert.That(composition.State.Recorded).IsSameReferenceAs(composition.State.Pointer);
            var owner = composition.State.Owners.Single();
            await Assert.That(owner.RequestRead && owner.IntakeRecorded && owner.Disposed).IsTrue();
            await Assert.That(composition.State.AmbientStoreConstructed).IsFalse();
        }
        finally
        {
            composition.Factory.Release.TrySetResult();
            await ObserveSettlement(execution);
        }
    }

    [Test]
    public async Task EmailBindingMismatchCannotOpenAnIdentityOwner()
    {
        await using var composition = new Composition();
        await using var requestScope = composition.Ambient.CreateAsyncScope();
        var provider = requestScope.ServiceProvider.GetRequiredService<IAccountAuthorityLifecycleEmailProvider>();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var result = await provider.RequestAsync(AccountAuthorityLifecycleEmailAction.EmailVerification,
            composition.State.Request with { ExternalLoginId = Guid.CreateVersion7() }, composition.State.Authority,
            deadline.Token);

        await Assert.That(result.Status).IsEqualTo(AccountAuthorityLifecycleEmailStatus.AccountNotLinked);
        await Assert.That(composition.Factory.Requested.Task.IsCompleted).IsFalse();
        await Assert.That(composition.State.Owners.Count).IsEqualTo(0);
        await Assert.That(composition.State.Recorded).IsNull();
        await Assert.That(composition.State.AmbientStoreConstructed).IsFalse();
    }

    [Test]
    public async Task EmailIntakeFailureDisposesTheOwnerWithoutRecordingAnOperation()
    {
        await using var composition = new Composition();
        composition.Factory.Release.TrySetResult();
        composition.State.FailIntake = true;
        await using var requestScope = composition.Ambient.CreateAsyncScope();
        var provider = requestScope.ServiceProvider.GetRequiredService<IAccountAuthorityLifecycleEmailProvider>();

        await Assert.ThrowsAsync<IntakeFailure>(() => provider.RequestAsync(
            AccountAuthorityLifecycleEmailAction.EmailVerification, composition.State.Request, composition.State.Authority));

        var owner = composition.State.Owners.Single();
        await Assert.That(owner.RequestRead && owner.Disposed).IsTrue();
        await Assert.That(owner.IntakeRecorded).IsFalse();
        await Assert.That(composition.State.Recorded).IsNull();
    }

    private static async Task ObserveBeforeCompletion(Task signal, Task execution)
    {
        await Task.WhenAny(signal, execution).WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(signal.IsCompletedSuccessfully).IsTrue();
        await Assert.That(execution.IsCompleted).IsFalse();
    }

    private static async Task ObserveSettlement(Task execution)
    {
        try { await execution.WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (Exception) when (execution.IsCompleted) { }
    }

    private sealed class Composition : IAsyncDisposable
    {
        internal State State { get; } = new();
        internal ServiceProvider Ambient { get; }
        internal SelectedFactory Factory { get; }
        private readonly ServiceProvider selected;

        internal Composition()
        {
            selected = Services(isSelected: true).BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            Factory = new SelectedFactory(selected, State);
            var ambient = Services(isSelected: false);
            ambient.AddSingleton<ILocalIdentityExecutionScopeFactory>(Factory);
            Ambient = ambient.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        }

        internal LocalIdentityLifecycleDeliveryWorker Worker() => new(
            Ambient.GetRequiredService<IServiceScopeFactory>(), NullLogger<LocalIdentityLifecycleDeliveryWorker>.Instance);

        private ServiceCollection Services(bool isSelected)
        {
            var services = new ServiceCollection();
            services.AddScoped(_ => new Owner(State, isSelected));
            services.AddScoped<ILocalIdentityLifecycleStore, LifecycleProbe>();
            services.AddScoped<ILocalIdentityLifecycleDeliveryStore, DeliveryProbe>();
            services.AddScoped<IAccountAuthorityLifecycleEmailProvider, LocalIdentityLifecycleEmailService>();
            services.AddScoped(provider => new LocalIdentityLifecycleDeliveryProcessor(
                provider.GetRequiredService<ILocalIdentityLifecycleStore>(),
                provider.GetRequiredService<ILocalIdentityLifecycleDeliveryStore>(),
                // Empty delivery inventory makes SMTP/settings/secret dependencies unreachable.
                // An unexpected delivery must fail, not invoke a replacement/no-op transport.
                null!, null!, null!, null!,
                new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                { ["PublicBaseUrl"] = "https://lifecycle.example.test" }).Build(),
                null!, Options.Create(new EmailDispatchProcessorSettings())));
            return services;
        }

        public async ValueTask DisposeAsync()
        {
            await Ambient.DisposeAsync();
            await selected.DisposeAsync();
        }
    }

    private sealed class State
    {
        internal List<Owner> Owners { get; } = [];
        internal bool AmbientStoreConstructed { get; set; }
        internal bool PauseSynchronization { get; set; }
        internal bool FailDeliveryRead { get; set; }
        internal bool FailIntake { get; set; }
        internal TaskCompletionSource SynchronizationStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleaseSynchronization { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal LocalIdentityLifecycleRequest NativeRequest { get; } = new(Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), LocalIdentityLifecyclePurpose.EmailVerification, "lifecycle@example.test");
        internal LocalIdentityLifecyclePointer Pointer { get; }
        internal LocalIdentityLifecyclePointer? Recorded { get; set; }
        internal AccountAuthorityLifecycleEmailRequest Request => new(NativeRequest.LocalSubjectId, NativeRequest.ExternalLoginId);
        internal ResolvedAccountAuthority Authority => new(NativeRequest.ExternalLoginId, NativeRequest.LocalSubjectId,
            new ProviderAccountKey(AuthenticationProviderKind.Local, NativeRequest.LocalSubjectId.ToString("D")),
            AccountAuthorityKind.LocalIdentity);

        internal State() => Pointer = new(Guid.CreateVersion7(), NativeRequest.LocalSubjectId, NativeRequest.PersonalActorId,
            NativeRequest.ExternalLoginId, NativeRequest.Purpose, Guid.CreateVersion7());
    }

    private sealed class Owner(State state, bool selected) : IAsyncDisposable
    {
        internal State State => state;
        internal bool Disposed { get; private set; }
        internal bool SynchronizationRead { get; set; }
        internal bool DeliveryRead { get; set; }
        internal bool RequestRead { get; set; }
        internal bool IntakeRecorded { get; set; }
        internal bool OverlappedEarlierOwner { get; set; }

        internal void RequireSelected()
        {
            if (!selected)
            {
                state.AmbientStoreConstructed = true;
                throw new InvalidOperationException("lifecycle_store_constructed_on_ambient_owner");
            }
            if (Disposed) throw new ObjectDisposedException(nameof(Owner));
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class SelectedFactory(ServiceProvider selected, State state) : ILocalIdentityExecutionScopeFactory
    {
        internal TaskCompletionSource Requested { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<ILocalIdentityExecutionScope> CreateAsync(CancellationToken cancellationToken)
        {
            Requested.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            var scope = selected.CreateAsyncScope();
            state.Owners.Add(scope.ServiceProvider.GetRequiredService<Owner>());
            return new SelectedScope(scope);
        }
    }

    private sealed class SelectedScope(AsyncServiceScope scope) : ILocalIdentityExecutionScope
    {
        public IServiceProvider Services => scope.ServiceProvider;
        public ValueTask DisposeAsync() => scope.DisposeAsync();
    }

    private sealed class DeliveryProbe : ILocalIdentityLifecycleDeliveryStore
    {
        private readonly Owner owner;
        public DeliveryProbe(Owner owner) { owner.RequireSelected(); this.owner = owner; }

        public async Task<IReadOnlyList<LocalIdentityLifecyclePointer>> ReadPendingSynchronizationAsync(
            int maximumCount, CancellationToken cancellationToken)
        {
            owner.RequireSelected();
            owner.SynchronizationRead = true;
            owner.State.SynchronizationStarted.TrySetResult();
            if (owner.State.PauseSynchronization)
                await owner.State.ReleaseSynchronization.Task.WaitAsync(cancellationToken);
            return [];
        }

        public Task<IReadOnlyList<LocalIdentityLifecyclePointer>> ReadPendingAsync(int maximumCount, CancellationToken cancellationToken)
        {
            owner.RequireSelected();
            owner.DeliveryRead = true;
            owner.OverlappedEarlierOwner = owner.State.Owners.Any(other => other != owner && !other.Disposed);
            if (owner.State.FailDeliveryRead) throw new DeliveryReadFailure();
            return Task.FromResult<IReadOnlyList<LocalIdentityLifecyclePointer>>([]);
        }

        public Task<LocalIdentityLifecycleRequest?> ReadRequestAsync(Guid localSubjectId, Guid externalLoginId,
            LocalIdentityLifecyclePurpose purpose, string? proposedAddress, string? expectedSecurityStamp, CancellationToken cancellationToken)
        {
            owner.RequireSelected();
            var request = owner.State.NativeRequest;
            if (request.LocalSubjectId != localSubjectId || request.ExternalLoginId != externalLoginId || request.Purpose != purpose)
                throw new InvalidOperationException("unexpected_lifecycle_binding");
            owner.RequestRead = true;
            return Task.FromResult<LocalIdentityLifecycleRequest?>(request);
        }

        public Task<bool> TryReserveGlobalSmtpAsync(int limitPerMinute, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Guid?> TryAdmitAsync(LocalIdentityLifecyclePointer operation, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task CompleteAsync(LocalIdentityLifecyclePointer operation, Guid attemptId, SmtpDeliveryOutcome outcome,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class LifecycleProbe : ILocalIdentityLifecycleStore
    {
        private readonly Owner owner;
        public LifecycleProbe(Owner owner) { owner.RequireSelected(); this.owner = owner; }

        public Task<LocalIdentityLifecyclePointer?> BeginAsync(LocalIdentityLifecycleRequest request, CancellationToken cancellationToken)
        {
            owner.RequireSelected();
            if (!ReferenceEquals(request, owner.State.NativeRequest)) throw new InvalidOperationException("lifecycle_request_replaced");
            if (owner.State.FailIntake) throw new IntakeFailure();
            owner.IntakeRecorded = true;
            owner.State.Recorded = owner.State.Pointer;
            return Task.FromResult<LocalIdentityLifecyclePointer?>(owner.State.Recorded);
        }

        public Task<LocalIdentityLifecycleRequest?> FindRequestByIdentifierAsync(string identifier, LocalIdentityLifecyclePurpose purpose,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<LocalIdentityLifecycleOutcome> ChangePasswordAsync(LocalIdentityPasswordChangeRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> ExecuteSynchronizationAsync(LocalIdentityLifecycleConsumption authority,
            Func<LocalIdentityLifecycleSynchronization, CancellationToken, Task<bool>> synchronize, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> ExecuteSynchronizationAsync(LocalIdentityLifecyclePointer operation,
            Func<LocalIdentityLifecycleSynchronization, CancellationToken, Task<bool>> synchronize, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<LocalIdentityLifecycleTransport?> IssueTransportTokenAsync(LocalIdentityLifecyclePointer operation,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<LocalIdentityLifecycleResult> ConsumeAsync(LocalIdentityLifecycleConsumption request,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<LocalIdentityLifecycleSynchronization?> ReadSynchronizationAsync(LocalIdentityLifecyclePointer operation,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> MarkSynchronizedAsync(LocalIdentityLifecyclePointer operation, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class DeliveryReadFailure : Exception { }
    private sealed class IntakeFailure : Exception { }
}
