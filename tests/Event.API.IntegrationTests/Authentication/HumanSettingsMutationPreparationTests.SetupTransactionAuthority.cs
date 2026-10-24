using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;
using Explore.Application.Exceptions;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Features.InstanceOnboarding.Services;
using Explore.Application.Models.Common;
using Explore.Domain.Constants;
using Explore.Persistence;
using Explore.Persistence.Security;
using Explore.Persistence.Services;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Event.Api.IntegrationTests.Authentication;

public sealed partial class HumanSettingsMutationPreparationTests
{
    [Test]
    [Arguments("unchanged")]
    [Arguments("secret-lock")]
    [Arguments("request-secret")]
    [Arguments("commit-fault")]
    public async Task ProviderSetupCommit_RetainsPrebeginCoordinationAndSettlement(string change)
    {
        var observer = new NativeProviderObserver();
        var controls = new SetupTransactionControls(observer, change == "commit-fault");
        await using var native = await LocalAdmissionWebApplicationFactory.CreateAsync(
            persistenceInterceptor: controls, incompleteSetup: true);
        await using var host = native.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["Authorization:Provider"] = "" }));
            builder.ConfigureTestServices(services =>
            {
                RegisterNativeProvider(services, observer);
                var original = services.Last(item => item.ServiceType == typeof(IAuthorizationProviderModeCacheInvalidator));
                services.RemoveAll<IAuthorizationProviderModeCacheInvalidator>();
                services.AddScoped<IAuthorizationProviderModeCacheInvalidator>(provider => new ProviderAuthorizationActivationObserver(
                    (IAuthorizationProviderModeCacheInvalidator)original.ImplementationFactory!(provider), observer));
            });
        });
        await using var request = await ProviderRequestAsync(host.Services, null, native.SetupSecret,
            "/api/instance/settings/authz-provider");
        var setup = request.Services.GetRequiredService<ISetupSecretProvider>();
        await Assert.That(setup.ValidateSecret(native.SetupSecret)).IsTrue();
        var service = request.Services.GetRequiredService<IAuthorizationProviderConfigurationService>();
        var before = await service.ReadConfigurationAsync();
        await using var observation = await service.ObserveConfigurationAsync(new UpdateAuthorizationProviderConfigurationDuringSetupCommand
        {
            Patch = new()
            {
                Configuration = OptionalUpdate<Explore.Application.DTOs.Instance.AuthorizationProviderConfigurationWriteDto>
                    .Set(new() { Provider = "local" })
            }
        }, default);
        await Assert.That(observation.Source is IProviderConfigurationSetupSource).IsTrue();
        string[] keys = [GovernanceSettingKeys.Security.AuthorizationProvider, GovernanceSettingKeys.Cerbos.GrpcEndpoint];
        await Assert.That(observation.Effects.Select(row => row.Key)).IsEquivalentTo(keys);
        await Assert.That(observation.Effects.Single(row => row.Key == keys[0]).Value).IsEqualTo("\"local\"");
        await Assert.That(observation.Effects.Single(row => row.Key == keys[1]).Value).IsEqualTo("\"\"");
        await Assert.That(HumanSecuritySourceGate.TryRequireCurrent(observer.Context!)).IsFalse();
        await Assert.That(EventDiscoveryEnrollmentGate.TryRequireCurrent(observer.Context!)).IsFalse();
        controls.Original = observation;
        controls.OriginalConnection = observer.Context!.Database.GetDbConnection();
        observer.AfterWrite = async (database, actual, token) =>
        {
            await Assert.That(actual).IsSameReferenceAs(observation);
            await Assert.That(database).IsSameReferenceAs(observer.Context);
            controls.WrittenTransaction = database.Database.CurrentTransaction?.GetDbTransaction()
                ?? throw new InvalidOperationException("The setup writer lost its original transaction.");
            await Assert.That(controls.WrittenTransaction.IsolationLevel).IsEqualTo(IsolationLevel.Serializable);
            await RequireSetupImageAsync(database, observation, token);
            controls.CompleteImageObserved = true;
            controls.Written.TrySetResult();
            if (change is "secret-lock" or "request-secret")
                await controls.Resume.Task.WaitAsync(TimeSpan.FromSeconds(30), token);
        };
        observer.ObserveCommitted = () =>
        {
            using var independent = native.CreateDatabase();
            return observer.Context!.Database.CurrentTransaction is null
                && independent.SystemSettings.Count(row => keys.Contains(row.SettingKey) && row.UpdatedBy == null
                    && (row.SettingKey == keys[0] && row.Value == "\"local\""
                        || row.SettingKey == keys[1] && row.Value == "\"\"")) == 2;
        };
        controls.Armed = true;
        try
        {
            Task operation = service.ApplyConfigurationAsync(observation, default);
            if (change is "secret-lock" or "request-secret")
            {
                try
                {
                    var reached = await Task.WhenAny(controls.Written.Task, operation).WaitAsync(TimeSpan.FromSeconds(30));
                    if (!ReferenceEquals(reached, controls.Written.Task))
                    {
                        await operation;
                        throw new InvalidOperationException("Setup returned before its complete native writer image.");
                    }
                    if (change == "secret-lock")
                    {
                        setup.Lock();
                        await Assert.That(setup.ValidateSecret(native.SetupSecret)).IsFalse();
                    }
                    else
                    {
                        string replacement = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
                        request.Context.Request.Headers["X-Setup-Secret"] = replacement;
                        await Assert.That(setup.ValidateSecret(replacement)).IsFalse();
                        await Assert.That(request.Context.Request.Headers["X-Setup-Secret"].ToString() == native.SetupSecret).IsFalse();
                    }
                }
                finally { controls.Resume.TrySetResult(); }
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => operation);
            }
            else if (change == "commit-fault")
            {
                var failure = await Assert.ThrowsAsync<HumanMutationOutcomeUnknownException>(() => operation);
                await Assert.That(failure!.InnerException is SetupCommitFaultException).IsTrue();
            }
            else
                await operation;

            await Assert.That(controls.PrebeginObserved).IsTrue();
            await Assert.That(observer.Applied).IsTrue();
            await Assert.That(controls.CompleteImageObserved).IsTrue();
            await Assert.That(controls.WrittenTransaction).IsNotNull();
            await Assert.That(controls.CommitObserved).IsEqualTo(change is "unchanged" or "commit-fault");
            await Assert.That(controls.CommitFaultObserved).IsEqualTo(change == "commit-fault");
            await Assert.That(observer.Context!.Database.CurrentTransaction).IsNull();
            await Assert.That(HumanSecuritySourceGate.TryRequireCurrent(observer.Context)).IsFalse();
            await Assert.That(EventDiscoveryEnrollmentGate.TryRequireCurrent(observer.Context)).IsFalse();
            await Assert.That(observer.ActivatedAfterCommit).IsEqualTo(change == "unchanged" ? true : (bool?)null);
            await using var final = native.CreateDatabase();
            foreach (var effect in observation.Effects)
            {
                var row = await final.SystemSettings.AsNoTracking().SingleOrDefaultAsync(row => row.SettingKey == effect.Key);
                await Assert.That(row is null ? null : ProviderConfigurationRow.From(row))
                    .IsEqualTo(change == "unchanged" ? effect : observation.Before.System[effect.Key]);
            }
            var after = await service.ReadConfigurationAsync();
            await Assert.That(after.Provider).IsEqualTo(change == "unchanged" ? "local" : before.Provider);
            await Assert.That(after.AuthorizationProviderConfigured).IsEqualTo(change == "unchanged" || before.AuthorizationProviderConfigured);
            if (change != "unchanged") await Assert.That(after.CerbosGrpcEndpoint).IsEqualTo(before.CerbosGrpcEndpoint);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ApplyConfigurationAsync(observation, default));
        }
        finally
        {
            controls.Resume.TrySetResult();
            controls.Armed = false;
        }
    }

    private static async Task RequireSetupImageAsync(ExploreDbContext database, ProviderConfigurationMutation original,
        CancellationToken token)
    {
        foreach (var effect in original.Effects)
        {
            var row = await database.SystemSettings.AsNoTracking().SingleAsync(row => row.SettingKey == effect.Key, token);
            await Assert.That(ProviderConfigurationRow.From(row)).IsEqualTo(effect);
            await Assert.That(row.UpdatedBy).IsNull();
        }
    }

    private sealed class SetupTransactionControls(NativeProviderObserver observer, bool failCommit) : DbTransactionInterceptor
    {
        internal ProviderConfigurationMutation Original { get; set; } = null!;
        internal DbConnection OriginalConnection { get; set; } = null!;
        internal bool Armed { get; set; }
        internal bool PrebeginObserved { get; private set; }
        internal bool CompleteImageObserved { get; set; }
        internal bool CommitObserved { get; private set; }
        internal bool CommitFaultObserved { get; private set; }
        internal DbTransaction? WrittenTransaction { get; set; }
        internal TaskCompletionSource Written { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Resume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(DbConnection connection,
            TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result,
            CancellationToken cancellationToken = default)
        {
            if (!Armed || !ReferenceEquals(eventData.Context, observer.Context)) return result;
            var database = observer.Context!;
            ((EfCoreUnitOfWork)Original.OriginalOwner).RequireTrustedSetupSettingsOwner(database);
            await Assert.That(database.Database.CurrentTransaction).IsNull();
            await Assert.That(database.TenantFilterTenantId).IsNull();
            await Assert.That(connection).IsSameReferenceAs(OriginalConnection);
            HumanSecuritySourceGate.RequireCurrent(database);
            EventDiscoveryEnrollmentGate.RequireCurrent(database);
            PrebeginObserved = true;
            return result;
        }

        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (!Armed || !ReferenceEquals(eventData.Context, observer.Context)) return result;
            var database = observer.Context!;
            ((EfCoreUnitOfWork)Original.OriginalOwner).RequireTrustedSetupSettingsOwner(database);
            await Assert.That(transaction).IsSameReferenceAs(WrittenTransaction);
            await Assert.That(database.Database.CurrentTransaction?.GetDbTransaction()).IsSameReferenceAs(transaction);
            HumanSecuritySourceGate.RequireCurrent(database);
            EventDiscoveryEnrollmentGate.RequireCurrent(database);
            await RequireSetupImageAsync(database, Original, cancellationToken);
            CommitObserved = true;
            if (failCommit)
            {
                CommitFaultObserved = true;
                throw new SetupCommitFaultException();
            }
            return result;
        }
    }

    private sealed class SetupCommitFaultException : Exception;
}
