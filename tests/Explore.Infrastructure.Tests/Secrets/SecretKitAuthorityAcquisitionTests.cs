using Explore.ServiceDefaults.Secrets;
using Microsoft.Extensions.Configuration;
using SecretKit.Core;
using SecretKit.Infisical;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Text.Json;

namespace Explore.Infrastructure.Tests.Secrets;

public sealed class SecretKitAuthorityAcquisitionTests
{
    [Test]
    [Arguments("APPHOST:CLIENT_SECRET")]
    [Arguments("DATA_PROTECTION:WRAPPING_KEY")]
    [Arguments("EVENT_DP:PIN")]
    public async Task HierarchicalProtectedDestinationsCannotBeExported(string destination)
    {
        Action admit = () => SecretAcquisitionPolicy.Admit(
            new(SecretAuthorityKind.Infisical, "Testing", SecretConsumerRole.Api),
            [new("/database", "password", destination)]);
        await Assert.That(admit).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task RetirementReservesPreparationBeforeCallerEnumeration()
    {
        using var supplied = (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["DATABASE_PASSWORD"] = Guid.NewGuid().ToString("N") }).Build();
        await using var owner = SecretAuthorityAcquisition.OpenLocal(
            new(SecretAuthorityKind.Environment, "Testing", SecretConsumerRole.Api), supplied);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        IEnumerable<SecretAcquisitionEntry> BlockedEntries()
        {
            entered.TrySetResult();
            release.Task.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            yield return new("/database", "DATABASE_PASSWORD", "Database:Password");
        }
        Task<SecretOutcome<SecretAuthorityLease>> pending =
            Task.Run(async () => await owner.PrepareGroupAsync(BlockedEntries()));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task retirement = owner.DisposeAsync().AsTask();
        bool completedBeforeRelease = retirement.IsCompleted;
        release.TrySetResult();
        bool canceled = false;
        bool disposedDuringUse = false;
        try
        {
            var result = await pending;
            if (result.IsSuccess)
                result.Value.Dispose();
        }
        catch (OperationCanceledException)
        {
            canceled = true;
        }
        catch (ObjectDisposedException)
        {
            disposedDuringUse = true;
        }
        await retirement.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(completedBeforeRelease).IsFalse();
        await Assert.That(disposedDuringUse).IsFalse();
        await Assert.That(canceled).IsTrue();
    }

    [Test]
    public async Task DuplicateConfigurationDestinationsRejectBeforeReading()
    {
        Action admit = () => SecretAcquisitionPolicy.Admit(
            new(SecretAuthorityKind.Infisical, "Testing", SecretConsumerRole.Api),
            [new("/database", "first", "Database:Password"),
             new("/database", "second", "database:password")]);
        await Assert.That(admit).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ContradictoryReturnedCoordinatesCannotBecomePublishedOrAbsent()
    {
        await using var authority = await SecretKitHttpAuthority.StartAsync();
        authority.Values["password"] = Guid.NewGuid().ToString("N");
        authority.ReturnedProject = Guid.NewGuid().ToString("N");
        using var bootstrap = SecretMaterial.FromText(Guid.NewGuid().ToString("N"));
        SecretAcquisitionEntry[] requested = [new("/database", "password", "Database:Password")];
        var opened = await SecretAuthorityAcquisition.OpenInfisicalAsync(
            new(SecretAuthorityKind.Infisical, "Testing", SecretConsumerRole.Api),
            requested, Options(authority), bootstrap, new SecretAuthorityLoginAttempt());
        await Assert.That(opened.IsSuccess).IsTrue();
        await using var owner = opened.Value;
        var rejected = await owner.PrepareGroupAsync(requested);
        if (rejected.IsSuccess)
            rejected.Value.Dispose();
        await Assert.That(rejected.IsSuccess).IsFalse();
        await Assert.That(rejected.Status == SecretStatus.Absent).IsFalse();
    }

    [Test]
    public async Task FiniteCeilingExpiresWithoutDispatchOrImplicitAuthentication()
    {
        await using var authority = await SecretKitHttpAuthority.StartAsync();
        authority.Values["password"] = Guid.NewGuid().ToString("N");
        using var bootstrap = SecretMaterial.FromText(Guid.NewGuid().ToString("N"));
        var clock = new AuthorityClock();
        SecretAcquisitionEntry[] requested = [new("/database", "password", "Database:Password")];
        var opened = await SecretAuthorityAcquisition.OpenInfisicalAsync(
            new(SecretAuthorityKind.Infisical, "Testing", SecretConsumerRole.Api),
            requested, Options(authority), bootstrap, new SecretAuthorityLoginAttempt(), timeProvider: clock);
        await Assert.That(opened.IsSuccess).IsTrue();
        await using var owner = opened.Value;
        clock.Advance(TimeSpan.FromSeconds(3601));
        var rejected = await owner.PrepareGroupAsync(requested);
        if (rejected.IsSuccess)
            rejected.Value.Dispose();
        await Assert.That(rejected.IsSuccess).IsFalse();
        await Assert.That(rejected.Status == SecretStatus.Absent).IsFalse();
        await Assert.That(authority.Reads).IsEqualTo(0);
        await Assert.That(authority.Renewals).IsEqualTo(0);
        await Assert.That(authority.Logins).IsEqualTo(1);
    }

    [Test]
    public async Task ExplicitPeriodicPolicyAdmitsZeroMaximumWithoutInventingInfinity()
    {
        await using var authority = await SecretKitHttpAuthority.StartAsync();
        authority.MaximumTtl = 0;
        authority.Values["password"] = Guid.NewGuid().ToString("N");
        using var bootstrap = SecretMaterial.FromText(Guid.NewGuid().ToString("N"));
        SecretAcquisitionEntry[] requested = [new("/database", "password", "Database:Password")];
        var options = Options(authority) with
        {
            Lifetime = InfisicalTokenLifetime.Periodic,
            Period = TimeSpan.FromSeconds(600)
        };
        var opened = await SecretAuthorityAcquisition.OpenInfisicalAsync(
            new(SecretAuthorityKind.Infisical, "Testing", SecretConsumerRole.Api),
            requested, options, bootstrap, new SecretAuthorityLoginAttempt());
        await Assert.That(opened.IsSuccess).IsTrue();
        await using var owner = opened.Value;
        var result = await owner.PrepareGroupAsync(requested);
        await Assert.That(result.IsSuccess).IsTrue();
        using var lease = result.Value;
        await Assert.That(lease.Configuration["Database:Password"]).IsEqualTo(authority.Values["password"]);
    }

    [Test]
    public async Task CancelingOneGroupCannotCancelAnotherAdmittedGroup()
    {
        await using var authority = await SecretKitHttpAuthority.StartAsync();
        authority.Values["slow"] = Guid.NewGuid().ToString("N");
        string independent = Guid.NewGuid().ToString("N");
        authority.Values["fast"] = independent;
        authority.BlockedKey = "slow";
        using var bootstrap = SecretMaterial.FromText(Guid.NewGuid().ToString("N"));
        SecretAcquisitionEntry[] requested = [new("/database", "slow", "Database:Password")];
        var opened = await SecretAuthorityAcquisition.OpenInfisicalAsync(
            new(SecretAuthorityKind.Infisical, "Testing", SecretConsumerRole.Api),
            requested, Options(authority), bootstrap, new SecretAuthorityLoginAttempt());
        await Assert.That(opened.IsSuccess).IsTrue();
        await using var owner = opened.Value;
        using var cancellation = new CancellationTokenSource();
        Task<SecretOutcome<SecretAuthorityLease>> pending =
            owner.PrepareGroupAsync(requested, cancellation.Token).AsTask();
        try
        {
            await authority.ReadEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var other = await owner.PrepareGroupAsync([new("/database", "fast", "Database:Username")]);
            await Assert.That(other.IsSuccess).IsTrue();
            using var lease = other.Value;
            await cancellation.CancelAsync();
            Func<Task> observe = async () =>
            {
                var result = await pending;
                if (result.IsSuccess)
                    result.Value.Dispose();
            };
            await Assert.That(observe).Throws<OperationCanceledException>();
            await Assert.That(lease.Configuration["Database:Username"]).IsEqualTo(independent);
        }
        finally
        {
            authority.ReadReleased.TrySetResult();
        }
    }

    [Test]
    public async Task RetirementDrainsPendingReadWithoutPublishingItsCandidate()
    {
        await using var authority = await SecretKitHttpAuthority.StartAsync();
        authority.Values["slow"] = Guid.NewGuid().ToString("N");
        authority.BlockedKey = "slow";
        using var bootstrap = SecretMaterial.FromText(Guid.NewGuid().ToString("N"));
        SecretAcquisitionEntry[] requested = [new("/database", "slow", "Database:Password")];
        var opened = await SecretAuthorityAcquisition.OpenInfisicalAsync(
            new(SecretAuthorityKind.Infisical, "Testing", SecretConsumerRole.Api),
            requested, Options(authority), bootstrap, new SecretAuthorityLoginAttempt());
        await Assert.That(opened.IsSuccess).IsTrue();
        await using var owner = opened.Value;
        Task<SecretOutcome<SecretAuthorityLease>> pending = owner.PrepareGroupAsync(requested).AsTask();
        try
        {
            await authority.ReadEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await owner.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            bool rejected;
            try
            {
                var result = await pending;
                rejected = !result.IsSuccess;
                if (result.IsSuccess)
                    result.Value.Dispose();
            }
            catch (OperationCanceledException)
            {
                rejected = true;
            }
            await Assert.That(rejected).IsTrue();
            Func<Task> reopen = async () => _ = await owner.PrepareGroupAsync(requested);
            await Assert.That(reopen).Throws<ObjectDisposedException>();
        }
        finally
        {
            authority.ReadReleased.TrySetResult();
        }
    }

    [Test]
    [NotInParallel]
    public async Task NativeTransportAndFormattingDoNotExportMaterialCoordinatesOrProviderProse()
    {
        using var console = new CapturedConsole();
        Console.WriteLine("harmless_stdout_control");
        Console.Error.WriteLine("harmless_stderr_control");
        var activityTags = new ConcurrentBag<string>();
        var metricTags = new ConcurrentBag<string>();
        using var activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "System.Net.Http",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                foreach (var tag in activity.TagObjects)
                    activityTags.Add($"{tag.Key}:{tag.Value}");
            }
        };
        ActivitySource.AddActivityListener(activityListener);
        using var meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == "System.Net.Http")
                    listener.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<int>((_, _, tags, _) =>
        {
            foreach (var tag in tags)
                metricTags.Add($"{tag.Key}:{tag.Value}");
        });
        meterListener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            foreach (var tag in tags)
                metricTags.Add($"{tag.Key}:{tag.Value}");
        });
        meterListener.SetMeasurementEventCallback<double>((_, _, tags, _) =>
        {
            foreach (var tag in tags)
                metricTags.Add($"{tag.Key}:{tag.Value}");
        });
        meterListener.Start();
        using var controlSource = new ActivitySource("System.Net.Http");
        using (var control = controlSource.StartActivity("control"))
            control?.SetTag("control", "harmless_control");
        using var controlMeter = new Meter("System.Net.Http");
        controlMeter.CreateCounter<int>("secret_test_control")
            .Add(1, new KeyValuePair<string, object?>("control", "harmless_control"));
        await using var authority = await SecretKitHttpAuthority.StartAsync();
        string material = Guid.NewGuid().ToString("N");
        string coordinate = Guid.NewGuid().ToString("N");
        string prose = Guid.NewGuid().ToString("N");
        authority.Values[coordinate] = material;
        var entry = new SecretAcquisitionEntry("/database", coordinate, "Database:Password");
        var options = Options(authority) with { ProjectId = coordinate, ClientId = coordinate };
        using var bootstrap = SecretMaterial.FromText(Guid.NewGuid().ToString("N"));
        var attempt = new SecretAuthorityLoginAttempt();
        var opened = await SecretAuthorityAcquisition.OpenInfisicalAsync(
            new(SecretAuthorityKind.Infisical, "Testing", SecretConsumerRole.Api),
            [entry], options, bootstrap, attempt);
        await Assert.That(opened.IsSuccess).IsTrue();
        await using var owner = opened.Value;
        var result = await owner.PrepareGroupAsync([entry]);
        await Assert.That(result.IsSuccess).IsTrue();
        using var lease = result.Value;
        authority.ReadStatusCode = 400;
        authority.ProviderMessage = prose;
        var rejected = await owner.PrepareGroupAsync([entry]);
        if (rejected.IsSuccess)
            rejected.Value.Dispose();
        await Assert.That(rejected.IsSuccess).IsFalse();
        await owner.DisposeAsync();
        meterListener.RecordObservableInstruments();
        console.Dispose();
        string activities = string.Join('\n', activityTags);
        string metrics = string.Join('\n', metricTags);
        string rendered = JsonSerializer.Serialize(new { control = "harmless_control", options, entry, attempt })
            + owner + options.ToString() + entry + rejected;
        await Assert.That(activities.Contains("harmless_control", StringComparison.Ordinal)).IsTrue();
        await Assert.That(metrics.Contains("harmless_control", StringComparison.Ordinal)).IsTrue();
        await Assert.That(rendered.Contains("harmless_control", StringComparison.Ordinal)).IsTrue();
        await Assert.That(console.StandardOutput.Contains("harmless_stdout_control", StringComparison.Ordinal)).IsTrue();
        await Assert.That(console.StandardError.Contains("harmless_stderr_control", StringComparison.Ordinal)).IsTrue();
        string captured = activities + metrics + rendered + console.StandardOutput + console.StandardError;
        foreach (string canary in new[] { material, coordinate, prose, authority.Origin.AbsoluteUri })
            await Assert.That(captured.Contains(canary, StringComparison.Ordinal)).IsFalse();
    }

    private sealed class CapturedConsole : IDisposable
    {
        private readonly TextWriter _previousOutput = Console.Out;
        private readonly TextWriter _previousError = Console.Error;
        private readonly StringWriter _output = new(CultureInfo.InvariantCulture);
        private readonly StringWriter _error = new(CultureInfo.InvariantCulture);
        private int _disposed;

        internal CapturedConsole()
        {
            Console.SetOut(_output);
            Console.SetError(_error);
        }

        internal string StandardOutput => _output.ToString();
        internal string StandardError => _error.ToString();

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;
            Console.SetOut(_previousOutput);
            Console.SetError(_previousError);
            _output.Dispose();
            _error.Dispose();
        }
    }

    private sealed class AuthorityClock : TimeProvider
    {
        private long _ticks = new DateTimeOffset(2026, 10, 25, 0, 0, 0, TimeSpan.Zero).UtcTicks;
        public override DateTimeOffset GetUtcNow() => new(Volatile.Read(ref _ticks), TimeSpan.Zero);
        internal void Advance(TimeSpan elapsed) => Interlocked.Add(ref _ticks, elapsed.Ticks);
    }

    [Test]
    public async Task NativeLoginHandoffKeepsIndependentTokenAuthority()
    {
        await using var authority = await SecretKitHttpAuthority.StartAsync();
        using var bootstrap = SecretMaterial.FromText(Guid.NewGuid().ToString("N"));
        using var transport = new SocketsHttpHandler
        {
            AllowAutoRedirect = false, UseCookies = false, ActivityHeadersPropagator = null
        };
        var network = new InfisicalNetworkOptions { AllowLoopbackDevelopmentHttp = true };
        using var authentication = new InfisicalUniversalAuthentication(authority.Origin, transport, network);
        var loginResult = await authentication.LoginAsync(Guid.NewGuid().ToString("N"),
            bootstrap, authentication.CreateReceipt());
        await Assert.That(loginResult.IsSuccess).IsTrue();
        using InfisicalUniversalLogin login = loginResult.Value;
        var evidence = login.BindLifetimeEvidence(new InfisicalTokenLifetimeEvidence
        {
            OriginalIssuedAt = login.RequestStartedAt,
            ExpiresAt = login.RequestStartedAt + TimeSpan.FromSeconds(login.ExpiresIn),
            Lifetime = InfisicalTokenLifetime.Finite,
            ReportedMaximumTtl = TimeSpan.FromSeconds(login.AccessTokenMaxTTL)
        });
        await using var session = new InfisicalPeriodicTokenSession(login, evidence,
            TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5), transport, network);
        var initial = await session.GetTokenAsync(CancellationToken.None);
        if (!initial.IsSuccess)
            throw new InvalidOperationException($"native_initial_{initial.Status}_{session.State}");
        initial.Value.Dispose();
        login.Dispose();
        var afterLogin = await session.GetTokenAsync(CancellationToken.None);
        if (!afterLogin.IsSuccess)
            throw new InvalidOperationException($"native_login_disposal_{afterLogin.Status}_{session.State}");
        afterLogin.Value.Dispose();
        authentication.Dispose();
        var afterAuthentication = await session.GetTokenAsync(CancellationToken.None);
        if (!afterAuthentication.IsSuccess)
            throw new InvalidOperationException($"native_auth_disposal_{afterAuthentication.Status}_{session.State}");
        afterAuthentication.Value.Dispose();
    }

    [Test]
    public async Task ForeignConsumerIsRejectedBeforeAuthentication()
    {
        await using var authority = await SecretKitHttpAuthority.StartAsync();
        using var bootstrap = SecretMaterial.FromText(Guid.NewGuid().ToString("N"));
        Func<Task> open = async () =>
        {
            var result = await SecretAuthorityAcquisition.OpenInfisicalAsync(
                new(SecretAuthorityKind.Infisical, "Testing", SecretConsumerRole.Api),
                [new("/runtime/bff/oidc/keycloak", "client_secret", "Oidc:Secret")],
                Options(authority), bootstrap, new SecretAuthorityLoginAttempt());
            if (result.IsSuccess)
                await result.Value.DisposeAsync();
        };
        await Assert.That(open).Throws<InvalidOperationException>();
        await Assert.That(authority.Logins).IsEqualTo(0);
    }

    [Test]
    public async Task OneSelectedLoginServesIndependentRequiredGroups()
    {
        await using var authority = await SecretKitHttpAuthority.StartAsync();
        string password = Guid.NewGuid().ToString("N");
        authority.Values["password"] = password;
        using var bootstrap = SecretMaterial.FromText(Guid.NewGuid().ToString("N"));
        SecretAcquisitionEntry[] requested = [new("/database", "password", "Database:Password")];
        var opened = await SecretAuthorityAcquisition.OpenInfisicalAsync(
            new(SecretAuthorityKind.Infisical, "Testing", SecretConsumerRole.Api),
            requested, Options(authority), bootstrap, new SecretAuthorityLoginAttempt());
        await Assert.That(opened.IsSuccess).IsTrue();
        await using var owner = opened.Value;
        var required = await owner.PrepareGroupAsync(requested);
        await Assert.That(required.Status).IsEqualTo(SecretStatus.Success);
        using var lease = required.Value;
        var optional = await owner.PrepareGroupAsync([new("/smtp", "missing", "Mail:Password")]);
        if (optional.IsSuccess)
            optional.Value.Dispose();
        await Assert.That(optional.IsSuccess).IsFalse();
        await Assert.That(lease.Configuration["Database:Password"]).IsEqualTo(password);
        await Assert.That(authority.Logins).IsEqualTo(1);
        using SecretRead copy = lease.Read("Database:Password");
        lease.Dispose();
        await Assert.That(copy.Material.Reveal()).IsEqualTo(password);
    }

    [Test]
    public async Task ZeroMaximumTtlDoesNotProvePeriodicAuthority()
    {
        await using var authority = await SecretKitHttpAuthority.StartAsync();
        authority.MaximumTtl = 0;
        using var bootstrap = SecretMaterial.FromText(Guid.NewGuid().ToString("N"));
        var result = await SecretAuthorityAcquisition.OpenInfisicalAsync(
            new(SecretAuthorityKind.Infisical, "Testing", SecretConsumerRole.Api),
            [new("/database", "password", "Database:Password")], Options(authority), bootstrap,
            new SecretAuthorityLoginAttempt());
        if (result.IsSuccess)
            await result.Value.DisposeAsync();
        await Assert.That(result.Status).IsEqualTo(SecretStatus.Invalid);
        await Assert.That(authority.Logins).IsEqualTo(1);
    }

    [Test]
    public async Task ExplicitLocalAuthorityUsesOnlyItsAdmittedValues()
    {
        string material = Guid.NewGuid().ToString("N");
        IConfiguration supplied = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["DATABASE_PASSWORD"] = material }).Build();
        await using var owner = SecretAuthorityAcquisition.OpenLocal(
            new(SecretAuthorityKind.Environment, "Testing", SecretConsumerRole.Api), supplied);
        var result = await owner.PrepareGroupAsync(
            [new("/database", "DATABASE_PASSWORD", "Database:Password")]);
        await Assert.That(result.IsSuccess).IsTrue();
        using var lease = result.Value;
        await Assert.That(lease.Configuration["Database:Password"]).IsEqualTo(material);
    }

    private static InfisicalAuthorityOptions Options(SecretKitHttpAuthority authority) =>
        new(authority.Origin, "project", "testing", Guid.NewGuid().ToString("N"),
            InfisicalTokenLifetime.Finite, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5));

    [Test]
    [Arguments(null)]
    [Arguments("unsupported")]
    public async Task MissingOrUnsupportedSelectorRejectsBeforeAcquisition(string? selector)
    {
        IConfiguration bootstrap = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["SECRET_PROVIDER"] = selector }).Build();
        Action select = () => SecretAuthoritySelection.Select(bootstrap, "Production", SecretConsumerRole.Api);
        await Assert.That(select).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ContradictorySelectorsRejectInsteadOfChoosingPrecedence()
    {
        IConfiguration bootstrap = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["SECRET_PROVIDER"] = "Environment",
                ["SecretProvider:Provider"] = "Infisical"
            }).Build();
        Action select = () => SecretAuthoritySelection.Select(bootstrap, "Production", SecretConsumerRole.Api);
        await Assert.That(select).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ProductionRejectsUserSecretsBeforeOpeningTheStore()
    {
        IConfiguration bootstrap = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["SECRET_PROVIDER"] = "UserSecrets" }).Build();
        Action select = () => SecretAuthoritySelection.Select(bootstrap, "Production", SecretConsumerRole.Api);
        await Assert.That(select).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ApiCannotAdmitBffCredentials()
    {
        var selection = new SecretAuthoritySelection(
            SecretAuthorityKind.Infisical, "Testing", SecretConsumerRole.Api);
        Action admit = () => SecretAcquisitionPolicy.Admit(selection,
            [new("/runtime/bff/oidc/keycloak", "client_secret", "Oidc:ClientSecret")]);
        await Assert.That(admit).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ProviderCannotPublishAnAuthoritySelector()
    {
        var selection = new SecretAuthoritySelection(
            SecretAuthorityKind.Infisical, "Testing", SecretConsumerRole.Api);
        Action admit = () => SecretAcquisitionPolicy.Admit(selection,
            [new("/database", "SECRET_PROVIDER", "SECRET_PROVIDER")]);
        await Assert.That(admit).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task MissingRequiredMemberPublishesNoGeneration()
    {
        var source = new SecretKitProviderTransport();
        source.Values["present"] = Guid.NewGuid().ToString("N");
        var result = await SecretAuthorityAcquisition.PrepareAsync(
            new(SecretAuthorityKind.Infisical, "Testing", SecretConsumerRole.Api),
            source, "project", "testing",
            [new("/database", "present", "Database:Username"),
             new("/database", "missing", "Database:Password")]);
        if (result.IsSuccess)
            result.Value.Dispose();
        await Assert.That(result.Status).IsEqualTo(SecretStatus.Absent);
    }

    [Test]
    public async Task CompleteRequiredGroupExportsOnlyMappedMaterial()
    {
        var source = new SecretKitProviderTransport();
        string material = Guid.NewGuid().ToString("N");
        source.Values["password"] = material;
        var result = await SecretAuthorityAcquisition.PrepareAsync(
            new(SecretAuthorityKind.Infisical, "Testing", SecretConsumerRole.Api),
            source, "project", "testing",
            [new("/database", "password", "Database:Password")]);
        await Assert.That(result.IsSuccess).IsTrue();
        using var lease = result.Value;
        await Assert.That(lease.Configuration["Database:Password"]).IsEqualTo(material);
        await Assert.That(lease.Configuration["SECRET_PROVIDER"]).IsNull();
    }

    [Test]
    public async Task CanceledPreparationNeverPublishes()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var source = new SecretKitProviderTransport();
        Func<Task> prepare = async () =>
        {
            var result = await SecretAuthorityAcquisition.PrepareAsync(
                new(SecretAuthorityKind.Infisical, "Testing", SecretConsumerRole.Api),
                source, "project", "testing",
                [new("/database", "password", "Database:Password")],
                cancellationToken: cancellation.Token);
            if (result.IsSuccess)
                result.Value.Dispose();
        };
        await Assert.That(prepare).Throws<OperationCanceledException>();
    }
}
