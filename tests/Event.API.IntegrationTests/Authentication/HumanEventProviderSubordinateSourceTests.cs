using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Contracts.Services.Registration;
using Explore.Application.DTOs.RegistrationProviders;
using Explore.Application.Features.RegistrationProviders.Commands;
using Explore.Application.Services.Registration;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Identity;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

/// <summary>Real Local authentication, selected owner and physical source leases; no substitute authority.</summary>
[NotInParallel]
public sealed class HumanEventProviderSubordinateSourceTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ManualImportPairsRetainTheirOriginalBindingAndRealProtectedReceipt(bool replaceBinding)
    {
        var token = LocalIdentityLifecycleHttpFixture.CancellationToken;
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        var seed = await SeedAsync(fixture);
        await using (var setup = fixture.Native.CreateDatabase())
        {
            setup.Add(RegistrationProviderCapability.Create(seed.Binding, "source", "hosted", "1", "1", "source",
                RegistrationProviderCapabilityCodes.Manual));
            await setup.SaveChangesAsync(token);
        }
        await using var host = fixture.Host.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddHumanEventProviderSources(IdentityDatabaseTopology.Colocated)));
        await using var request = await Request.OpenAsync(host.Services, await fixture.SignInAsync(), seed.EventId);
        var command = new QueueManualRegistrationProviderImportCommand(PlatformDefaults.DefaultTenantId, seed.EventId,
            seed.Binding.Id, Guid.CreateVersion7().ToString("D"), "source-provenance");
        var source = request.Services.GetRequiredService<IHumanEventProviderSourceFactory>()
            .Create(HumanEventProviderSourceTarget.From(command, seed.EventId));
        var observed = await source.ObserveAsync(token);
        string response = Guid.CreateVersion7().ToString("N");
        string decision = $"{seed.Binding.Id:N}:{response}";
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            providerCode = "source",
            bindingId = seed.Binding.Id,
            formId = "source",
            responseId = response,
            attemptId = Guid.CreateVersion7(),
            timestamp = seed.Now,
            mappedValues = new Dictionary<string, string>(),
            contractVersion = "1",
            idempotencyKey = $"source:{response}"
        });
        string hash = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(payload));
        string receipt = request.Services.GetRequiredService<IRegistrationProviderCallbackReceiptProtector>().Protect(
            new(PlatformDefaults.DefaultTenantId, seed.Connection.Id, seed.Binding.Id, "source",
                seed.Connection.TupleKey, hash, response, new DateTimeOffset(seed.Now), Guid.CreateVersion7().ToString("N")));
        string headers = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["X-Registration-Callback-Provider"] = "source",
            ["X-Registration-Verification-Receipt"] = receipt
        });
        var message = IncomingWebhookMessage.CreateVerified(PlatformDefaults.DefaultTenantId, "registration-provider",
            decision, decision, "registration.provider_manual_import", payload, hash, "application/json", "utf-8", headers,
            seed.Now, seed.Now, seed.Now.AddDays(7), "registration-provider-manual-import-v1",
            seed.Now.AddDays(30), seed.Now.AddDays(30), seed.Now.AddDays(7), seed.Now.AddDays(30));
        var effect = IncomingWebhookEffectOutbox.CreatePending(PlatformDefaults.DefaultTenantId, message.Id,
            "registration-provider", decision, "registration.provider_submission", hash, seed.Now);
        async Task Write(CancellationToken ct)
        {
            request.Context.AddRange(message, effect);
            await request.Context.SaveChangesAsync(ct);
            if (replaceBinding)
                await request.Context.IncomingWebhookMessages.Where(row => row.Id == message.Id)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.ProviderMessageId, $"{seed.Sibling.Id:N}:{response}"), ct);
        }
        if (replaceBinding)
            await Assert.ThrowsAsync<InvalidOperationException>(() => request.InOwnerAsync(source, observed, Write));
        else
            await request.InOwnerAsync(source, observed, Write);
        await using var final = fixture.Native.CreateDatabase();
        await Assert.That(await final.IncomingWebhookMessages.AnyAsync(row => row.Id == message.Id, token)).IsEqualTo(!replaceBinding);
        await Assert.That(await final.IncomingWebhookEffectOutboxes.AnyAsync(row => row.Id == effect.Id, token)).IsEqualTo(!replaceBinding);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ImportedVersionKeepsItsExactSchemaSourceAndGeneratedArtifacts(bool replaceSource)
    {
        var token = LocalIdentityLifecycleHttpFixture.CancellationToken;
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        var seed = await SeedAsync(fixture);
        await using var host = fixture.Host.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddHumanEventProviderSources(IdentityDatabaseTopology.Colocated)));
        await using var request = await Request.OpenAsync(host.Services, await fixture.SignInAsync(), seed.EventId);
        var command = new ImportExternalRegistrationProviderFormVersionCommand(PlatformDefaults.DefaultTenantId,
            seed.EventId, seed.Connection.Id, new() { FormId = seed.Form.Id, ProviderSurveyId = "source", ProviderSurveyRevisionId = "2" });
        var source = request.Services.GetRequiredService<IHumanEventProviderSourceFactory>()
            .Create(HumanEventProviderSourceTarget.From(command, seed.EventId));
        var observed = await source.ObserveAsync(token);
        const string snapshot = """{"schema":"islamu.registration-provider-schema-snapshot.v1","fields":[{"key":"name","label":"Name","type":"ShortText","isRequired":false,"options":[]}]}""";
        const string mapping = """{"schema":"islamu.external-import-mapping-revision.v1","importerPolicyVersion":"1","providerCode":"source","providerDeploymentCode":"hosted","apiVersion":"1","conformanceEvidenceRevision":"source","providerSurveyId":"source","providerSurveyRevisionId":"2","fields":[{"providerKey":"name","providerType":"ShortText","isRequired":false,"mappingKey":"external/name","options":[]}]}""";
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(snapshot));
        var schema = RegistrationProviderSchemaRevision.Create(PlatformDefaults.DefaultTenantId, seed.Connection.Id,
            RegistrationProviderSchemaAuthorityEnum.ProviderDiscovered, RegistrationEvidenceHash.Create(Convert.ToBase64String(hash)),
            "source", "2", snapshot, Convert.ToHexStringLower(hash), RegistrationProviderDriftClassEnum.NoDrift, seed.Now);
        var version = RegistrationFormVersion.CreateExternalImported(seed.Form, 2, "en", seed.Connection.Id, schema.Id,
            "source", "2", Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(mapping))), seed.Now);
        var section = RegistrationFormSection.Create(Guid.CreateVersion7(), version, 1, "Imported questions", seed.Now);
        version.AddSection(section);
        var field = RegistrationFormField.Create(Guid.CreateVersion7(), section, 1, "external", "name", "Name",
            RegistrationFieldTypeEnum.ShortText, 1, RegistrationOrganizerVisibilityEnum.AuthorizedOrganizers, false, true, seed.Now);
        version.AddField(section, field);
        ActivatorUtilities.CreateInstance<FormSchemaArtifactPublicationService>(request.Services).Publish(version, seed.Now);
        async Task Write(CancellationToken ct)
        {
            request.Context.AddRange(schema, version);
            await request.Context.SaveChangesAsync(ct);
            if (replaceSource)
                await request.Context.RegistrationFormVersions.Where(row => row.Id == version.Id)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.ExternalProviderSurveyId, "wrong-source"), ct);
        }
        if (replaceSource)
            await Assert.ThrowsAsync<InvalidOperationException>(() => request.InOwnerAsync(source, observed, Write));
        else
            await request.InOwnerAsync(source, observed, Write);
        await using var final = fixture.Native.CreateDatabase();
        await Assert.That(await final.RegistrationFormVersions.AnyAsync(row => row.Id == version.Id, token)).IsEqualTo(!replaceSource);
        await Assert.That(await final.RegistrationProviderSchemaRevisions.AnyAsync(row => row.Id == schema.Id, token)).IsEqualTo(!replaceSource);
    }

    [Test]
    [Arguments("current")]
    [Arguments("binding")]
    [Arguments("profile")]
    [Arguments("capability")]
    [Arguments("schema")]
    [Arguments("version")]
    [Arguments("missing-binding")]
    public async Task RemoteCheckpointAndOriginalOwnerRejectCommittedSubordinateDrift(string change)
    {
        var token = LocalIdentityLifecycleHttpFixture.CancellationToken;
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        var seed = await SeedAsync(fixture);
        await using var host = fixture.Host.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddHumanEventProviderSources(IdentityDatabaseTopology.Colocated)));
        await using var request = await Request.OpenAsync(host.Services, await fixture.SignInAsync(), seed.EventId);
        var command = new DeleteRegistrationProviderBindingCommand(PlatformDefaults.DefaultTenantId, seed.EventId,
            change == "missing-binding" ? seed.Absent.Id : seed.Binding.Id);
        var source = request.Services.GetRequiredService<IHumanEventProviderSourceFactory>()
            .Create(HumanEventProviderSourceTarget.From(command, seed.EventId));
        var observed = await source.ObserveAsync(token);
        await Assert.That(observed.Resolved.EventId).IsEqualTo(seed.EventId);
        await Assert.That(observed.Resolved.BindingId).IsEqualTo(command.BindingId);
        if (change != "current")
        {
            await using var writer = fixture.Native.CreateDatabase();
            switch (change)
            {
                case "binding":
                    await writer.RegistrationProviderBindings.Where(row => row.Id == seed.Binding.Id)
                        .ExecuteUpdateAsync(update => update
                            .SetProperty(row => row.RegistrationFormId, seed.Absent.RegistrationFormId)
                            .SetProperty(row => row.RegistrationFormVersionId, seed.Absent.RegistrationFormVersionId), token);
                    break;
                case "profile":
                    await writer.RegistrationProviderConnections.Where(row => row.Id == seed.Connection.Id)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.AdapterPolicyVersion, "different"), token);
                    break;
                case "capability":
                    writer.Add(RegistrationProviderCapability.Create(seed.Binding, "source", "hosted", "1", "1", "source",
                        RegistrationProviderCapabilityCodes.Manual));
                    await writer.SaveChangesAsync(token);
                    break;
                case "schema":
                    writer.Add(RegistrationProviderSchemaRevision.Create(PlatformDefaults.DefaultTenantId, seed.Connection.Id,
                        RegistrationProviderSchemaAuthorityEnum.ProviderDiscovered, RegistrationEvidenceHash.Create(Convert.ToBase64String(new byte[32])),
                        "source", "2", "{\"fields\":[]}", new string('0', 64), RegistrationProviderDriftClassEnum.NoDrift, seed.Now));
                    await writer.SaveChangesAsync(token);
                    break;
                case "version":
                    writer.Add(RegistrationFormVersion.Create(seed.Form, 2, "en", null, null, seed.Now));
                    await writer.SaveChangesAsync(token);
                    break;
                case "missing-binding":
                    writer.Add(seed.Absent);
                    await writer.SaveChangesAsync(token);
                    break;
            }
            await Assert.ThrowsAsync<InvalidOperationException>(() => source.ValidateObservationAsync(observed, token));
            await Assert.ThrowsAsync<InvalidOperationException>(() => request.InOwnerAsync(source, observed, _ => Task.CompletedTask));
        }
        else
        {
            await source.ValidateObservationAsync(observed, token);
            await request.InOwnerAsync(source, observed, _ => Task.CompletedTask);
        }
        await Assert.That(request.Context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    [Arguments("delete")]
    [Arguments("mapping")]
    [Arguments("create-binding")]
    [Arguments("update-binding")]
    [Arguments("provision-evidence")]
    [Arguments("wrong-binding")]
    [Arguments("wrong-form")]
    [Arguments("wrong-profile")]
    [Arguments("wrong-mapping")]
    public async Task FinalPhysicalReadAdmitsOnlyTheOriginalCommandsOwnTransition(string change)
    {
        var token = LocalIdentityLifecycleHttpFixture.CancellationToken;
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        var seed = await SeedAsync(fixture);
        await using var host = fixture.Host.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddHumanEventProviderSources(IdentityDatabaseTopology.Colocated)));
        await using var request = await Request.OpenAsync(host.Services, await fixture.SignInAsync(), seed.EventId);
        var mapping = new ReplaceEventDraftRegistrationProviderMappingsCommand(PlatformDefaults.DefaultTenantId, seed.EventId, seed.Binding.Id,
            new ReplaceRegistrationProviderMappingsRequestDto
            {
                FieldMappings = [new() { PlatformFieldKey = "external.name", ProviderFieldKey = "name", IsRequired = true }]
            });
        var bindingRequest = new RegistrationProviderBindingRequestDto
        {
            ConnectionId = change == "update-binding" ? seed.DestinationConnection.Id : seed.Connection.Id,
            FormId = change == "create-binding" ? seed.Absent.RegistrationFormId : seed.Form.Id,
            FormVersionId = change == "create-binding" ? seed.Absent.RegistrationFormVersionId : seed.Version.Id,
            PresentationModeId = (int)RegistrationProviderPresentationModeEnum.Manual,
            CollectionModeId = (int)RegistrationProviderCollectionModeEnum.ProviderHosted,
            CompletionModeId = (int)RegistrationProviderCompletionModeEnum.Manual,
            TrustLevelId = (int)RegistrationProviderTrustLevelEnum.FullSync,
            ProviderSurveyId = "source",
            ProviderSurveyRevisionId = "1"
        };
        var target = change is "mapping" or "wrong-mapping"
            ? HumanEventProviderSourceTarget.From(mapping, seed.EventId)
            : change == "create-binding" ? HumanEventProviderSourceTarget.From(
                new CreateRegistrationProviderBindingCommand(PlatformDefaults.DefaultTenantId, seed.EventId, bindingRequest), seed.EventId)
            : change == "update-binding" ? HumanEventProviderSourceTarget.From(
                new UpdateRegistrationProviderBindingCommand(PlatformDefaults.DefaultTenantId, seed.EventId, seed.Binding.Id, bindingRequest), seed.EventId)
            : change == "provision-evidence" ? HumanEventProviderSourceTarget.From(
                new PublishEventRegistrationProviderBindingCommand(PlatformDefaults.DefaultTenantId, seed.EventId, seed.Binding.Id), seed.EventId)
            : HumanEventProviderSourceTarget.From(new DeleteRegistrationProviderBindingCommand(
                PlatformDefaults.DefaultTenantId, seed.EventId, seed.Binding.Id), seed.EventId);
        var source = request.Services.GetRequiredService<IHumanEventProviderSourceFactory>().Create(target);
        var observed = await source.ObserveAsync(token);
        async Task Mutate(CancellationToken ct)
        {
            if (change == "create-binding")
            {
                request.Context.Add(seed.Absent);
                await request.Context.SaveChangesAsync(ct);
                return;
            }
            if (change == "wrong-form")
                await request.Context.RegistrationForms.Where(row => row.Id == seed.OtherForm.Id)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.Name, "wrong form"), ct);
            else if (change == "wrong-profile")
                await request.Context.RegistrationProviderConnections.Where(row => row.Id == seed.Connection.Id)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.ConformanceEvidenceRevision, "wrong profile"), ct);
            else
            {
                var repository = request.Services.GetRequiredService<IRegistrationProviderRepository>();
                var binding = (await repository.GetBindingAsync(PlatformDefaults.DefaultTenantId,
                    change == "wrong-binding" ? seed.Sibling.Id : seed.Binding.Id, ct))!;
                if (change is "mapping" or "wrong-mapping")
                    binding.ReplaceDraftMappings([RegistrationProviderFieldMapping.Create(binding, "external.name",
                        change == "mapping" ? "name" : "other", true)], []);
                else if (change == "update-binding")
                    binding.UpdateDraft(seed.DestinationConnection.Id, seed.Form.Id, seed.Version.Id,
                        RegistrationProviderPresentationModeEnum.Manual, RegistrationProviderCollectionModeEnum.ProviderHosted,
                        RegistrationProviderCompletionModeEnum.Manual, RegistrationProviderTrustLevelEnum.FullSync, null);
                else if (change == "provision-evidence")
                    binding.SetDraftProvisionedSubscription("acknowledged-subscription");
                else binding.Remove(seed.Now);
                await repository.SaveChangesAsync(ct);
            }
        }
        if (change is "delete" or "mapping" or "create-binding" or "update-binding" or "provision-evidence")
            await request.InOwnerAsync(source, observed, Mutate);
        else
            await Assert.ThrowsAsync<InvalidOperationException>(() => request.InOwnerAsync(source, observed, Mutate));
        await Assert.That(request.Context.Database.CurrentTransaction).IsNull();
        await using var final = fixture.Native.CreateDatabase();
        await Assert.That(await final.RegistrationProviderBindings.IgnoreQueryFilters([QueryFilterNames.SoftDelete])
            .Where(row => row.Id == seed.Binding.Id).Select(row => row.IsDeleted).SingleAsync(token))
            .IsEqualTo(change == "delete");
        await Assert.That(await final.RegistrationProviderBindings.IgnoreQueryFilters([QueryFilterNames.SoftDelete])
            .Where(row => row.Id == seed.Sibling.Id).Select(row => row.IsDeleted).SingleAsync(token)).IsFalse();
    }

    [Test]
    public async Task TrackedChangesAreNotCurrentFactsAndEndedLeasesCannotBeReused()
    {
        var token = LocalIdentityLifecycleHttpFixture.CancellationToken;
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        var seed = await SeedAsync(fixture);
        await using var host = fixture.Host.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddHumanEventProviderSources(IdentityDatabaseTopology.Colocated)));
        await using var request = await Request.OpenAsync(host.Services, await fixture.SignInAsync(), seed.EventId);
        var target = HumanEventProviderSourceTarget.From(new PollRegistrationProviderReconciliationCommand(
            PlatformDefaults.DefaultTenantId, seed.EventId, seed.Binding.Id, seed.Now), seed.EventId);
        var source = request.Services.GetRequiredService<IHumanEventProviderSourceFactory>().Create(target);
        var observed = await source.ObserveAsync(token);
        var tracked = (await request.Services.GetRequiredService<IRegistrationProviderRepository>()
            .GetBindingAsync(PlatformDefaults.DefaultTenantId, seed.Binding.Id, token))!;
        tracked.SetDraftProvisionedSurvey("tracked-only", "2");
        await source.ValidateObservationAsync(observed, token);
        await Assert.That(observed.Of(HumanEventProviderSource.Binding).Single(row => row.Id == seed.Binding.Id)["ProviderSurveyId"])
            .IsEqualTo("source");
        await request.Context.Entry(tracked).ReloadAsync(token);
        var lease = await request.InOwnerAsync(source, observed, _ => Task.CompletedTask);
        await Assert.ThrowsAsync<InvalidOperationException>(() => lease.ValidateAsync(token));
        await Assert.ThrowsAsync<InvalidOperationException>(() => source.ObserveAsync(token));

        var subject = request.Services.GetRequiredService<IHumanCommitScopePreparation>().Subject!;
        await using var foreignIdentity = fixture.Native.CreateDatabase();
        await Assert.ThrowsAsync<NotSupportedException>(() => Task.FromResult(new HumanEventProviderSourceFence(
            request.Context, subject, target, request.Services.GetRequiredService<IRegistrationProviderRegistry>(),
            selectedIdentityContext: foreignIdentity)));
    }

    private sealed class Request : IAsyncDisposable
    {
        private readonly AsyncServiceScope _scope;
        private readonly IHttpContextAccessor _accessor;
        private readonly Guid _eventId;
        private Request(IServiceProvider root, Guid eventId)
        {
            _scope = root.CreateAsyncScope();
            _accessor = Services.GetRequiredService<IHttpContextAccessor>();
            _eventId = eventId;
        }
        internal IServiceProvider Services => _scope.ServiceProvider;
        internal ExploreDbContext Context => Services.GetRequiredService<ExploreDbContext>();
        internal static async Task<Request> OpenAsync(IServiceProvider root, string credential, Guid eventId)
        {
            var request = new Request(root, eventId);
            request.Services.GetRequiredService<ITenantContextAccessor>().BindExecutionTenant(PlatformDefaults.DefaultTenantId);
            var http = new DefaultHttpContext { RequestServices = request.Services };
            http.Request.Headers.Authorization = "Bearer " + credential;
            request._accessor.HttpContext = http;
            var authenticated = await http.AuthenticateAsync();
            await Assert.That(authenticated.Succeeded).IsTrue();
            http.User = authenticated.Principal!;
            await request.Services.GetRequiredService<IHumanCommitScopePreparation>()
                .PrepareAsync(LocalIdentityLifecycleHttpFixture.CancellationToken);
            return request;
        }
        internal async Task<IHumanEventProviderSourceLease> InOwnerAsync(IHumanEventProviderSourceFence source,
            HumanEventProviderSourceFacts observed, Func<CancellationToken, Task> mutate)
        {
            var token = LocalIdentityLifecycleHttpFixture.CancellationToken;
            var principal = Services.GetRequiredService<IHumanPrincipalSourceFence>();
            var route = Services.GetRequiredService<IHumanAuthorizationRouteSource>();
            var assignments = Services.GetRequiredService<IHumanEventAssignmentSourceFactory>()
                .Create(PlatformDefaults.DefaultTenantId, [_eventId]);
            var support = await Services.GetRequiredService<IHumanSupportAccessSourceFactory>().CreateAsync(token);
            return await Services.GetRequiredService<ISerializableAttemptOwner>().ExecuteAsync(async ct =>
            {
                var s = await Services.GetRequiredService<IPolicySourceSetFence>().AcquireAsync(ct);
                var p = await principal.AcquireAsync(s, ct);
                var r = await route.AcquireAsync(s, ct);
                var a = await assignments.AcquireAsync(s, ct);
                var h = await support.AcquireAsync(s, p, a, ct);
                var held = await source.AcquireAsync(observed, s, p, r, a, h, ct);
                Services.GetRequiredService<IHumanActionCommitFinalizer>().Register(
                    async finalToken => { await held.ValidateAsync(finalToken); },
                    now =>
                    {
                        var lifetime = Services.GetRequiredService<Explore.Persistence.Database.ExploreDbContextExecutionLease>().HumanLifetime!;
                        if (!lifetime.IsValidAt(now)) throw new UnauthorizedAccessException("The original credential expired.");
                    });
                await mutate(ct);
                return held;
            }, token);
        }
        public async ValueTask DisposeAsync()
        {
            _accessor.HttpContext = null;
            await _scope.DisposeAsync();
        }
    }

    private sealed record Seed(Guid EventId, RegistrationProviderConnection Connection, RegistrationProviderConnection DestinationConnection, RegistrationForm Form,
        RegistrationFormVersion Version, RegistrationForm OtherForm, RegistrationFormVersion OtherVersion,
        RegistrationProviderBinding Binding, RegistrationProviderBinding Sibling, RegistrationProviderBinding Absent, DateTime Now);

    private static async Task<Seed> SeedAsync(LocalIdentityLifecycleHttpFixture fixture)
    {
        var token = LocalIdentityLifecycleHttpFixture.CancellationToken;
        await using var context = fixture.Native.CreateDatabase();
        DateTime now = fixture.Clock.GetUtcNow().UtcDateTime;
        Guid actor = await context.Actors.Where(row => row.UserId == fixture.Binding.LocalSubjectId).Select(row => row.Id).SingleAsync(token);
        var parent = new Explore.Domain.Event(EventStatusEnum.Draft)
        {
            Id = Guid.CreateVersion7(),
            TenantId = PlatformDefaults.DefaultTenantId,
            Tenant = null!,
            Title = "Subordinate source",
            ActorId = actor,
            Actor = null!,
            EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
            VisibilityTypeId = (int)VisibilityTypeEnum.Public,
            VisibilityType = null!,
            EventFormatId = (int)EventFormatEnum.Local,
            EventFormat = null!,
            EventStatus = null!,
            SessionCount = 1,
            FirstSessionStartUtc = fixture.Clock.GetUtcNow().AddDays(1),
            LastSessionEndUtc = fixture.Clock.GetUtcNow().AddDays(1).AddHours(1),
            CreatedAt = now
        };
        var form = RegistrationForm.Create(parent.TenantId, parent.Id, "source", "form", "Source form", now);
        var version = RegistrationFormVersion.Create(form, 1, "en", null, null, now);
        form.AddVersion(version);
        var otherForm = RegistrationForm.Create(parent.TenantId, parent.Id, "source", "other", "Sibling form", now);
        var otherVersion = RegistrationFormVersion.Create(otherForm, 1, "en", null, null, now);
        otherForm.AddVersion(otherVersion);
        var connection = RegistrationProviderConnection.Create(parent.TenantId, "Source connection",
            RegistrationProviderKindEnum.ExternalForm, RegistrationProviderDeploymentKindEnum.HostedSaas,
            "source", "hosted", "1", "1", "source", "https://provider.example", "https://provider.example", "source", null, null, now);
        var destination = RegistrationProviderConnection.Create(parent.TenantId, "Destination connection",
            RegistrationProviderKindEnum.ExternalForm, RegistrationProviderDeploymentKindEnum.HostedSaas,
            "source", "hosted", "1", "1", "source", "https://provider.example", "https://provider.example", "source", null, null, now);
        RegistrationProviderBinding Binding(RegistrationForm selectedForm, RegistrationFormVersion selectedVersion)
        {
            var binding = RegistrationProviderBinding.Create(parent.TenantId, connection.Id, selectedForm.Id, selectedVersion.Id,
                RegistrationProviderPresentationModeEnum.Manual, RegistrationProviderCollectionModeEnum.ProviderHosted,
                RegistrationProviderCompletionModeEnum.Manual, RegistrationProviderTrustLevelEnum.FullSync, null, now);
            binding.SetDraftProvisionedSurvey("source", "1");
            return binding;
        }
        var binding = Binding(form, version);
        var sibling = Binding(otherForm, otherVersion);
        var absentForm = RegistrationForm.Create(parent.TenantId, parent.Id, "source", "absent", "Absent binding form", now);
        var absentVersion = RegistrationFormVersion.Create(absentForm, 1, "en", null, null, now);
        absentForm.AddVersion(absentVersion);
        context.AddRange(parent, connection, destination, form, otherForm, absentForm, binding, sibling);
        await context.SaveChangesAsync(token);
        return new(parent.Id, connection, destination, form, version, otherForm, otherVersion, binding, sibling, Binding(absentForm, absentVersion), now);
    }
}
