using System.Data;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text.Json;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Configuration;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Features.Authentication.Local.Models;
using Explore.Application.Features.Users.Handlers.Queries;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Infrastructure.Authentication;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Identity;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Explore.Secrets.Database;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using NSubstitute;

namespace Event.Persistence.IntegrationTests;

[NotInParallel]
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
public sealed class HumanPrincipalNativeSourceFenceTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    [Arguments("none")]
    [Arguments("stamp")]
    [Arguments("email")]
    [Arguments("password")]
    [Arguments("metadata")]
    [Arguments("stage")]
    [Arguments("binding")]
    [Arguments("binding-id")]
    [Arguments("actor")]
    [Arguments("account-deleted")]
    [Arguments("unverified-off")]
    [Arguments("delivery-required")]
    [Arguments("malformed-policy")]
    [Arguments("verified-malformed-policy")]
    public async Task GenuineLocalSessionValidatesOnRuntimeWithoutPrivateIdentityGrants(string mutation)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        CancellationToken token = timeout.Token;
        byte[] key = RandomNumberGenerator.GetBytes(64);
        var secrets = Substitute.For<ISecretResolver>();
        secrets.ResolveAsync(SecretDefinitionRegistry.Keys.Authentication.LocalJwtKey, null, Arg.Any<CancellationToken>())
            .Returns(SecretResolutionResult.Resolved(new ResolvedSecret(
                SecretDefinitionRegistry.Keys.Authentication.LocalJwtKey, Convert.ToBase64String(key),
                SecretSourceType.EnvironmentVariable, SecretScope.Instance, null, DateTimeOffset.UtcNow)));
        var signer = new LocalJwtTokenGenerator(secrets, Options.Create(new LocalIdentityOptions()), TimeProvider.System);
        var jwt = new JwtSecurityTokenHandler { MapInboundClaims = false };
        TokenValidationParameters Validation(string audience) => new()
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(key),
            ValidateIssuer = true,
            ValidIssuer = LocalIdentityOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = audience,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ClockSkew = TimeSpan.Zero,
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256]
        };
        ServiceProvider Services(PrimaryDatabaseRole role)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<ExploreDbContext>(options =>
            {
                PrimaryDatabaseProviderComposition.ConfigureApplication(options, fixture.GetNativeQueueOptions(role));
                if (role == PrimaryDatabaseRole.Runtime)
                    options.AddInterceptors(PostgresTenantSessionInterceptor.RuntimeInstance);
            });
            services.AddIdentityCore<LocalIdentityUser>().AddRoles<LocalIdentityRole>()
                .AddEntityFrameworkStores<ExploreDbContext>();
            return services.BuildIsolatedServiceProvider(validateScopes: true);
        }
        LocalIdentityAuthService Authentication(ExploreDbContext context, UserManager<LocalIdentityUser> manager) => new(
            manager, signer, SystemSettingRepository.CreateReadOnly(context,
                new RelationalSettingMutationLock(context, new EfCoreUnitOfWork(context))),
            new LocalIdentityCredentialStateStore(context, context, manager, TimeProvider.System),
            fixture.NativeQueueEnrollment);

        await using var setup = Services(PrimaryDatabaseRole.Migrator);
        await using var setupScope = setup.CreateAsyncScope();
        var migration = setupScope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var manager = setupScope.ServiceProvider.GetRequiredService<UserManager<LocalIdentityUser>>();
        var store = new LocalIdentityCredentialStateStore(migration, migration, manager, TimeProvider.System);
        var initiator = new User
        {
            Id = Guid.CreateVersion7(),
            CreatedAt = DateTime.UtcNow,
            Pii = new UserPii { Email = string.Empty, FirstName = "Session", LastName = "Setup" }
        };
        var tenant = new Tenant
        {
            Id = Guid.CreateVersion7(),
            FullName = "Session validation",
            Slug = $"session-{Guid.CreateVersion7():N}",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!
        };
        migration.AddRange(initiator, tenant);
        await migration.SaveChangesAsync(token);
        string email = $"session-{Guid.CreateVersion7():N}@example.test";
        var created = await store.CreatePendingAsync(new LocalCredentialCreateRequest(
            Guid.CreateVersion7(), initiator.Id, email, "Session", "Owner"), token);
        await Assert.That(created.Outcome).IsEqualTo(LocalCredentialCreateOutcome.Created);
        var receipt = created.Receipt!;
        var user = new User
        {
            Id = receipt.LocalSubjectId,
            EmailVerified = true,
            CreatedAt = DateTime.UtcNow,
            Pii = new UserPii { Email = email, FirstName = "Session", LastName = "Owner" }
        };
        migration.Actors.Add(new Actor
        {
            Id = receipt.PersonalActorId,
            UserId = user.Id,
            User = user,
            ActorTypeId = (int)ActorTypeEnum.User,
            ActorType = null!,
            Pii = new ActorPii { DisplayName = "Session Owner" },
            CreatedAt = DateTime.UtcNow
        });
        migration.UserExternalLogins.Add(new UserExternalLogin
        {
            Id = receipt.ExternalLoginId,
            UserId = user.Id,
            User = user,
            AuthenticationProviderId = (int)AuthenticationProviderKind.Local,
            AuthenticationProvider = null!,
            ProviderKey = user.Id.ToString("D"),
            CreatedAt = DateTime.UtcNow
        });
        await migration.SaveChangesAsync(token);
        var pending = (await store.ReadProvisioningAsync(receipt.OperationId, token))!;
        await Assert.That(await store.ActivateChangeRequiredAsync(
            new LocalCredentialActivationRequest(receipt.OperationId, pending.OperationConcurrencyStamp), token))
            .IsEqualTo(LocalCredentialActivationOutcome.Activated);
        var authentication = Authentication(migration, manager);
        var first = await authentication.AuthenticateAsync(new LocalAuthRequestDto(email, created.TemporaryPassword!), token);
        await Assert.That(first.Outcome).IsEqualTo(LocalAuthOutcome.ReplacementRequired);
        var challengePrincipal = jwt.ValidateToken(first.ReplacementChallenge!.Token,
            Validation(LocalCredentialChallengeToken.Audience), out var challengeToken);
        var challenge = (JwtSecurityToken)challengeToken;
        var replacement = new LocalCredentialReplacementAuthority(
            new LocalCredentialReplacementSubject(receipt.LocalSubjectId,
                Guid.Parse(challengePrincipal.FindFirst(LocalCredentialChallengeToken.OperationIdClaim)!.Value),
                challengePrincipal.FindFirst(LocalCredentialChallengeToken.SecurityStampClaim)!.Value),
            new DateTimeOffset(challenge.ValidFrom), new DateTimeOffset(challenge.ValidTo));
        string password = $"Aa1!{Convert.ToHexString(RandomNumberGenerator.GetBytes(24))}";
        await Assert.That(await store.ReplaceAsync(new LocalCredentialReplacementRequest(replacement, password), token))
            .IsEqualTo(LocalCredentialReplacementOutcome.Replaced);
        migration.ChangeTracker.Clear();
        var identity = (await manager.FindByIdAsync(user.Id.ToString("D")))!;
        bool emailConfirmed = mutation is not ("unverified-off" or "delivery-required" or "malformed-policy");
        identity.EmailConfirmed = emailConfirmed;
        await Assert.That((await manager.UpdateAsync(identity)).Succeeded).IsTrue();
        bool controlsPolicy = !emailConfirmed || mutation == "verified-malformed-policy";
        var originalPolicy = controlsPolicy ? await migration.SystemSettings.AsNoTracking()
            .SingleOrDefaultAsync(row => row.SettingKey == GovernanceSettingKeys.Email.DeliveryEnabled, token) : null;
        Guid policyId = originalPolicy?.Id ?? Guid.CreateVersion7();
        try
        {
            if (controlsPolicy)
            {
                if (originalPolicy is null)
                {
                    migration.SystemSettings.Add(new SystemSetting
                    {
                        Id = policyId,
                        SettingKey = GovernanceSettingKeys.Email.DeliveryEnabled,
                        Value = "false",
                        ValueType = SettingValueType.Boolean,
                        CreatedAt = DateTime.UtcNow
                    });
                    await migration.SaveChangesAsync(token);
                }
                else
                    await migration.SystemSettings.Where(row => row.Id == policyId)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.Value, "false"), token);
            }
            var login = await authentication.AuthenticateAsync(new LocalAuthRequestDto(email, password), token);
            await Assert.That(login.Outcome).IsEqualTo(LocalAuthOutcome.Authenticated);
            var principal = jwt.ValidateToken(login.Token!, Validation(LocalIdentityOptions.Audience), out _);
            var authority = new LocalSessionAuthority(
                Guid.Parse(principal.FindFirst("sub")!.Value),
                principal.FindFirst(LocalSessionToken.SecurityStampClaim)!.Value,
                bool.Parse(principal.FindFirst("email_verified")!.Value));
            // These are persisted revocations after genuine issuance, never replacement authentication.
            switch (mutation)
            {
                case "none": break;
                case "unverified-off": break;
                case "delivery-required":
                case "malformed-policy":
                case "verified-malformed-policy":
                    await migration.SystemSettings.Where(row => row.Id == policyId)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.Value,
                            mutation == "delivery-required" ? "true" : "not-a-boolean"), token);
                    break;
                case "stamp":
                    await migration.LocalIdentityUsers.Where(row => row.Id == user.Id)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.SecurityStamp, Guid.CreateVersion7().ToString("N")), token);
                    break;
                case "email":
                    await migration.LocalIdentityUsers.Where(row => row.Id == user.Id)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.EmailConfirmed, false), token);
                    break;
                case "password":
                    await migration.LocalIdentityUsers.Where(row => row.Id == user.Id)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.PasswordHash, "\u2003\t"), token);
                    break;
                case "metadata":
                    await migration.Set<IdentityUserToken<Guid>>().Where(row => row.UserId == user.Id)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.Value, JsonSerializer.Serialize(
                            new LocalCredentialStateMetadata(1, LocalCredentialState.Ready, receipt.OperationId, initiator.Id))), token);
                    break;
                case "stage":
                    await migration.Set<LocalIdentityCredentialOperation>().Where(row => row.Id == receipt.OperationId)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.Stage, LocalCredentialOperationStage.ChangeRequired), token);
                    break;
                case "binding":
                case "binding-id":
                    await migration.UserExternalLogins.Where(row => row.Id == receipt.ExternalLoginId).ExecuteDeleteAsync(token);
                    if (mutation == "binding-id")
                    {
                        migration.UserExternalLogins.Add(new UserExternalLogin
                        {
                            Id = Guid.CreateVersion7(),
                            UserId = user.Id,
                            User = null!,
                            AuthenticationProviderId = (int)AuthenticationProviderKind.Local,
                            AuthenticationProvider = null!,
                            ProviderKey = user.Id.ToString("D"),
                            CreatedAt = DateTime.UtcNow
                        });
                        await migration.SaveChangesAsync(token);
                    }
                    break;
                case "actor":
                    await migration.Actors.Where(row => row.Id == receipt.PersonalActorId)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsSuspended, true), token);
                    break;
                case "account-deleted":
                    await migration.Users.Where(row => row.Id == user.Id)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsDeleted, true), token);
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(mutation));
            }
            await using var runtime = Services(PrimaryDatabaseRole.Runtime);
            await using var runtimeScope = runtime.CreateAsyncScope();
            var context = runtimeScope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            var runtimeAuthentication = Authentication(context,
                runtimeScope.ServiceProvider.GetRequiredService<UserManager<LocalIdentityUser>>());
            // Runtime with no tenant is the authentication boundary for instance routes.
            LocalSessionValidationOutcome expected = mutation switch
            {
                "none" or "unverified-off" or "verified-malformed-policy" => LocalSessionValidationOutcome.Valid,
                "malformed-policy" => LocalSessionValidationOutcome.Unavailable,
                _ => LocalSessionValidationOutcome.Invalid
            };
            if (expected != LocalSessionValidationOutcome.Unavailable)
            {
                var states = new LocalIdentityCredentialStateStore(context, context,
                    runtimeScope.ServiceProvider.GetRequiredService<UserManager<LocalIdentityUser>>(), TimeProvider.System);
                await Assert.That(await states.ValidateReadySessionAsync(authority,
                    fixture.NativeQueueEnrollment, token)).IsEqualTo(expected == LocalSessionValidationOutcome.Valid);
            }
            await Assert.That(await runtimeAuthentication.ValidateSessionAsync(authority, token)).IsEqualTo(expected);
            await Assert.That(context.ChangeTracker.Entries<LocalIdentityUser>().Any()).IsFalse();
            await Assert.That(context.Database.CurrentTransaction).IsNull();
            var denied = await Assert.ThrowsAsync<PostgresException>(async () =>
                await context.Set<IdentityUserToken<Guid>>().AsNoTracking().AnyAsync(row => row.UserId == user.Id, token));
            await Assert.That(denied!.SqlState).IsEqualTo("42501");
            await using var tenantScope = runtime.CreateAsyncScope();
            var tenantContext = tenantScope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            tenantContext.TenantContext = new TenantIdentity(tenant.Id);
            var tenantAuthentication = Authentication(tenantContext,
                tenantScope.ServiceProvider.GetRequiredService<UserManager<LocalIdentityUser>>());
            await Assert.That(await tenantAuthentication.ValidateSessionAsync(authority, token)).IsEqualTo(expected);
        }
        finally
        {
            if (controlsPolicy)
            {
                if (originalPolicy is null)
                    await migration.SystemSettings.Where(row => row.Id == policyId).ExecuteDeleteAsync(CancellationToken.None);
                else
                    await migration.SystemSettings.Where(row => row.Id == policyId)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.Value, originalPolicy.Value), CancellationToken.None);
            }
        }
    }

    [Test]
    public async Task RuntimeReadsGlobalCandidatesAndHoldsRealRowsWithoutGlobalConnection()
    {
        var rows = await SeedAsync();
        await using var context = Open(rows.Tenant);
        var source = new HumanPrincipalSourceFence(context, await SubjectAsync(rows.User),
            PolicySourceSetPurpose.RuntimeTenant, enrollment: fixture.NativeQueueEnrollment);
        var prepared = await source.ObserveAsync();
        await Assert.That(prepared.TenantMemberships.Any(row => row.TenantId == rows.OtherTenant)).IsTrue();
        await Assert.That(prepared.Organizations.Any(row => row.TenantId == rows.OtherTenant)).IsTrue();
        await Assert.That(prepared.Groups.Any(row => row.TenantId == rows.OtherTenant)).IsTrue();
        await Assert.That(prepared.PlatformGrants.Length).IsEqualTo(0);
        var sets = new PolicySourceSetFence(context, PolicySourceSetPurpose.RuntimeTenant, fixture.NativeQueueEnrollment);
        await using var owner = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var lease = await source.AcquireAsync(await sets.AcquireAsync());
        await using var contender = fixture.CreateDbContext();
        List<(Type Type, object Id)> contributors = [(typeof(User), rows.User), (typeof(TenantUser), rows.Member)];
        contributors.AddRange(lease.Facts.TenantGrants.Select(row => (typeof(TenantUserRoleGrant), (object)row.Id)));
        contributors.AddRange(lease.Facts.Tenants.Select(row => (typeof(Tenant), (object)row.Id)));
        contributors.AddRange(lease.Facts.Tenants.Select(row => (typeof(TenantStatus), (object)row.TenantStatusId)).Distinct());
        contributors.AddRange(lease.Facts.Roles.Select(row => (typeof(Role), (object)row.Id)));
        contributors.AddRange(lease.Facts.Permissions.Select(row => (typeof(Permission), (object)row.PermissionId)).Distinct());
        contributors.AddRange(lease.Facts.Organizations.Select(row => (typeof(OrganizationMember), (object)row.Id)));
        contributors.AddRange(lease.Facts.Organizations.Select(row => (typeof(OrganizationTenant), (object)row.PlacementId)));
        contributors.AddRange(lease.Facts.Organizations.Select(row => (typeof(Organization), (object)row.OwnerId)));
        contributors.AddRange(lease.Facts.Groups.Select(row => (typeof(GroupMember), (object)row.Id)));
        contributors.AddRange(lease.Facts.Groups.Select(row => (typeof(GroupTenant), (object)row.PlacementId)));
        contributors.AddRange(lease.Facts.Groups.Select(row => (typeof(Group), (object)row.OwnerId)));
        foreach (var (type, id) in contributors)
        {
            await using var attempt = await contender.Database.BeginTransactionAsync();
            var blocked = await Assert.ThrowsAsync<PostgresException>(async () =>
                await contender.Database.ExecuteSqlRawAsync(
                    $"SELECT id FROM {Table(contender, type)} WHERE id = {{0}} FOR UPDATE NOWAIT", id));
            await Assert.That(blocked!.SqlState).IsEqualTo("55P03");
            await attempt.RollbackAsync();
        }
        await lease.ValidateAsync();
        await owner.RollbackAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => lease.ValidateAsync());
        // The port's fixed result does not grant ambient cross-tenant projection.
        var denied = await Assert.ThrowsAsync<PostgresException>(() => context.TenantUsers.AsNoTracking()
            .AnyAsync(row => row.TenantId == rows.OtherTenant));
        await Assert.That(denied!.SqlState).IsEqualTo("42501");
    }

    [Test]
    public async Task MembershipInsertedAfterOriginalSnapshotForcesFreshAttempt()
    {
        var rows = await SeedAsync();
        await using var context = Open(rows.Tenant);
        var source = new HumanPrincipalSourceFence(context, await SubjectAsync(rows.User),
            PolicySourceSetPurpose.RuntimeTenant, enrollment: fixture.NativeQueueEnrollment);
        var sets = new PolicySourceSetFence(context, PolicySourceSetPurpose.RuntimeTenant, fixture.NativeQueueEnrollment);
        await using var owner = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var setLease = await sets.AcquireAsync();
        await using (var writer = fixture.CreateDbContext())
        {
            writer.PlatformUserRoles.Add(new PlatformUserRole
            {
                Id = Guid.CreateVersion7(),
                UserId = rows.User,
                User = null!,
                RoleId = (int)RoleEnum.Admin,
                Role = null!,
                GrantedAt = DateTime.UtcNow
            });
            await writer.SaveChangesAsync();
        }
        var stale = await Assert.ThrowsAsync<PostgresException>(() => source.AcquireAsync(setLease));
        await Assert.That(stale!.SqlState).IsEqualTo("40001");
        await owner.RollbackAsync();
        await using var fresh = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var held = await source.AcquireAsync(await sets.AcquireAsync());
        await Assert.That(held.Facts.PlatformGrants.Length).IsEqualTo(1);
        await fresh.RollbackAsync();
    }

    [Test]
    public async Task NewFkContributorCannotCommitBehindHeldPrincipal()
    {
        var rows = await SeedAsync();
        await using var context = Open(rows.Tenant);
        var source = new HumanPrincipalSourceFence(context, await SubjectAsync(rows.User),
            PolicySourceSetPurpose.RuntimeTenant, enrollment: fixture.NativeQueueEnrollment);
        var sets = new PolicySourceSetFence(context, PolicySourceSetPurpose.RuntimeTenant, fixture.NativeQueueEnrollment);
        await using var owner = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var held = await source.AcquireAsync(await sets.AcquireAsync());
        await using var writer = fixture.CreateDbContext();
        await using var attempt = await writer.Database.BeginTransactionAsync();
        await writer.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '100ms'");
        var blocked = await Assert.ThrowsAsync<PostgresException>(async () =>
            await writer.Database.ExecuteSqlRawAsync(
                $$"""
                INSERT INTO {{Table(writer, typeof(PlatformUserRole))}} (id,user_id,role_id,granted_at)
                VALUES ({0},{1},{2},CURRENT_TIMESTAMP)
                """, Guid.CreateVersion7(), rows.User, (int)RoleEnum.Admin));
        await Assert.That(blocked!.SqlState).IsEqualTo("55P03");
        await attempt.RollbackAsync();
        await held.ValidateAsync();
        await owner.RollbackAsync();
    }

    [Test]
    [Arguments("public-execute")]
    [Arguments("owner-login")]
    [Arguments("disabled-trigger")]
    [Arguments("extra-select")]
    [Arguments("rls")]
    public async Task RestrictedReaderRejectsSourceDefinedAdmissionDrift(string drift)
    {
        var rows = await SeedAsync();
        await using var migration = fixture.CreateDbContext();
        string routine = PostgresHumanPrincipalSourceContract.ObserveFunctionSql(migration)
            + "(uuid,integer,text,integer,uuid,text)";
        string table = Table(migration, typeof(User));
        string change = drift switch
        {
            "public-execute" => $"GRANT EXECUTE ON FUNCTION {routine} TO PUBLIC",
            "owner-login" => $"ALTER ROLE {PostgresHumanPrincipalSourceContract.OwnerRole} LOGIN",
            "disabled-trigger" => $"ALTER TABLE {table} DISABLE TRIGGER human_principal_source_write",
            "extra-select" => $"GRANT SELECT ON {Table(migration, typeof(RegistrationOrder))} TO {PostgresHumanPrincipalSourceContract.OwnerRole}",
            "rls" => $"ALTER TABLE {Table(migration, typeof(TenantUser))} NO FORCE ROW LEVEL SECURITY",
            _ => throw new ArgumentOutOfRangeException(nameof(drift))
        };
        string restore = drift switch
        {
            "public-execute" => $"REVOKE EXECUTE ON FUNCTION {routine} FROM PUBLIC",
            "owner-login" => $"ALTER ROLE {PostgresHumanPrincipalSourceContract.OwnerRole} NOLOGIN",
            "disabled-trigger" => $"ALTER TABLE {table} ENABLE ALWAYS TRIGGER human_principal_source_write",
            "extra-select" => $"REVOKE SELECT ON {Table(migration, typeof(RegistrationOrder))} FROM {PostgresHumanPrincipalSourceContract.OwnerRole}",
            "rls" => $"ALTER TABLE {Table(migration, typeof(TenantUser))} FORCE ROW LEVEL SECURITY",
            _ => throw new ArgumentOutOfRangeException(nameof(drift))
        };
        await migration.Database.ExecuteSqlRawAsync(change);
        try
        {
            await using var context = Open(rows.Tenant);
            var source = new HumanPrincipalSourceFence(context, await SubjectAsync(rows.User),
                PolicySourceSetPurpose.RuntimeTenant, enrollment: fixture.NativeQueueEnrollment);
            await Assert.ThrowsAsync<PostgresException>(() => source.ObserveAsync());
        }
        finally { await migration.Database.ExecuteSqlRawAsync(restore); }
    }

    [Test]
    public async Task ReStampedForeignRoutineBodyCannotBecomeAcceptedSource()
    {
        var rows = await SeedAsync();
        await using var migration = fixture.CreateDbContext();
        string name = PostgresHumanPrincipalSourceContract.ObserveFunctionSql(migration);
        string signature = name + "(uuid,integer,text,integer,uuid,text)";
        string original = await migration.Database.SqlQueryRaw<string>(
            "SELECT pg_catalog.pg_get_functiondef(pg_catalog.to_regprocedure({0})) AS \"Value\"", signature).SingleAsync();
        await migration.Database.ExecuteSqlRawAsync($"""
            CREATE OR REPLACE FUNCTION {name}(p_actor uuid,p_provider integer,p_account text,p_purpose integer,p_tenant uuid,p_stamp text)
            RETURNS jsonb LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog
            AS 'BEGIN RETURN pg_catalog.jsonb_build_object(); END';
            """);
        await migration.Database.ExecuteSqlRawAsync($"""
            DO $restamp$ BEGIN
                EXECUTE (SELECT pg_catalog.format('COMMENT ON FUNCTION %s IS %L', {Text(signature)},
                    'human-principal-source:v1:' || pg_catalog.encode(pg_catalog.sha256(pg_catalog.convert_to(prosrc,'UTF8')),'hex'))
                    FROM pg_catalog.pg_proc WHERE oid = pg_catalog.to_regprocedure({Text(signature)}));
            END $restamp$;
            """);
        try
        {
            await using var context = Open(rows.Tenant);
            var source = new HumanPrincipalSourceFence(context, await SubjectAsync(rows.User),
                PolicySourceSetPurpose.RuntimeTenant, enrollment: fixture.NativeQueueEnrollment);
            await Assert.ThrowsAsync<PostgresException>(() => source.ObserveAsync());
        }
        finally
        {
            await migration.Database.ExecuteSqlRawAsync(original);
            await migration.Database.ExecuteSqlRawAsync($"""
                DO $restamp$ BEGIN
                    EXECUTE (SELECT pg_catalog.format('COMMENT ON FUNCTION %s IS %L', {Text(signature)},
                        'human-principal-source:v1:' || pg_catalog.encode(pg_catalog.sha256(pg_catalog.convert_to(prosrc,'UTF8')),'hex'))
                        FROM pg_catalog.pg_proc WHERE oid = pg_catalog.to_regprocedure({Text(signature)}));
                END $restamp$;
                """);
        }
    }

    [Test]
    public async Task RemovedContributorForeignKeyFailsAdmissionWithoutLearningNewCatalog()
    {
        var rows = await SeedAsync();
        await using var migration = fixture.CreateDbContext();
        var entity = migration.Model.FindEntityType(typeof(PlatformUserRole))!;
        string constraint = entity.GetForeignKeys().Single(key => key.PrincipalEntityType.ClrType == typeof(User)).GetConstraintName()!;
        string table = Table(migration, typeof(PlatformUserRole));
        string definition = await migration.Database.SqlQueryRaw<string>(
            "SELECT pg_catalog.pg_get_constraintdef(oid) AS \"Value\" FROM pg_catalog.pg_constraint "
            + "WHERE conrelid = pg_catalog.to_regclass({0}) AND conname = {1}", table, constraint).SingleAsync();
        string identifier = migration.GetService<ISqlGenerationHelper>().DelimitIdentifier(constraint);
        await migration.Database.ExecuteSqlRawAsync($"ALTER TABLE {table} DROP CONSTRAINT {identifier}");
        try
        {
            await using var context = Open(rows.Tenant);
            var source = new HumanPrincipalSourceFence(context, await SubjectAsync(rows.User),
                PolicySourceSetPurpose.RuntimeTenant, enrollment: fixture.NativeQueueEnrollment);
            await Assert.ThrowsAsync<PostgresException>(() => source.ObserveAsync());
        }
        finally { await migration.Database.ExecuteSqlRawAsync($"ALTER TABLE {table} ADD CONSTRAINT {identifier} {definition}"); }
    }

    [Test]
    public async Task DeclaredGlobalPurposeCannotExecuteOnSelectedRuntimeSession()
    {
        var rows = await SeedAsync();
        await using var context = Open(rows.Tenant);
        var error = await Assert.ThrowsAsync<PostgresException>(async () =>
            await context.Database.ExecuteSqlRawAsync(
                $$"""
                SELECT {{PostgresHumanPrincipalSourceContract.ObserveFunctionSql(context)}}
                ({0},NULL::integer,NULL::text,2,{1},NULL::text)
                """, rows.User, rows.Tenant));
        await Assert.That(error!.SqlState).IsEqualTo("42501");
    }

    private async Task<(Guid User, Guid Tenant, Guid OtherTenant, Guid Member)> SeedAsync()
    {
        await using var context = fixture.CreateDbContext();
        await PostgresPolicySourceSetContract.ApplyAsync(context, fixture.NativeQueueEnrollment);
        await PostgresHumanPrincipalSourceContract.ApplyAsync(context, fixture.NativeQueueEnrollment);
        var user = new User
        {
            Id = Guid.CreateVersion7(),
            CreatedAt = DateTime.UtcNow,
            Pii = new UserPii { Email = string.Empty, FirstName = "Principal", LastName = "Source" }
        };
        Tenant NewTenant() => new()
        {
            Id = Guid.CreateVersion7(),
            FullName = "Principal source",
            Slug = $"principal-{Guid.CreateVersion7():N}",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!
        };
        var tenant = NewTenant();
        var other = NewTenant();
        var member = new TenantUser
        {
            Id = Guid.CreateVersion7(),
            TenantId = other.Id,
            Tenant = other,
            UserId = user.Id,
            User = user,
            StatusId = (int)TenantUserStatusEnum.Suspended,
            IsDeleted = true,
            CreatedAt = DateTime.UtcNow,
            JoinedAt = DateTime.UtcNow
        };
        var grant = new TenantUserRoleGrant
        {
            Id = Guid.CreateVersion7(),
            TenantId = other.Id,
            Tenant = other,
            TenantUserId = member.Id,
            TenantUser = member,
            RoleId = (int)RoleEnum.TenantAdmin,
            Role = null!,
            RoleScopeId = (int)RoleScopeEnum.Tenant,
            CreatedAt = DateTime.UtcNow,
            GrantedAt = DateTime.UtcNow,
            RevokedAt = DateTime.UtcNow
        };
        var organization = new Organization
        {
            Id = Guid.CreateVersion7(),
            Pii = new OrganizationPii { FullName = "Other tenant organization" },
            CreatedAt = DateTime.UtcNow,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        var organizationPlacement = new OrganizationTenant
        {
            Id = Guid.CreateVersion7(),
            TenantId = other.Id,
            Tenant = other,
            OrganizationId = organization.Id,
            Organization = organization,
            ApprovalStatusId = (int)ApprovalStatusEnum.Pending,
            ApprovalStatus = null!,
            CreatedAt = DateTime.UtcNow,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        var organizationMember = new OrganizationMember
        {
            Id = Guid.CreateVersion7(),
            TenantId = other.Id,
            Tenant = other,
            UserId = user.Id,
            User = user,
            OrganizationTenantId = organizationPlacement.Id,
            OrganizationTenant = organizationPlacement,
            RoleId = (int)RoleEnum.OrgAdmin,
            Role = null!,
            CreatedAt = DateTime.UtcNow
        };
        var group = new Group
        {
            Id = Guid.CreateVersion7(),
            FullName = "Other tenant group",
            CreatedAt = DateTime.UtcNow,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        var groupPlacement = new GroupTenant
        {
            Id = Guid.CreateVersion7(),
            TenantId = other.Id,
            Tenant = other,
            GroupId = group.Id,
            Group = group,
            ApprovalStatusId = (int)ApprovalStatusEnum.Pending,
            ApprovalStatus = null!,
            CreatedAt = DateTime.UtcNow,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        var groupMember = new GroupMember
        {
            Id = Guid.CreateVersion7(),
            TenantId = other.Id,
            Tenant = other,
            UserId = user.Id,
            User = user,
            GroupTenantId = groupPlacement.Id,
            GroupTenant = groupPlacement,
            RoleId = (int)RoleEnum.GroupAdmin,
            Role = null!,
            CreatedAt = DateTime.UtcNow
        };
        context.AddRange(user, tenant, other, member, grant, organizationMember, groupMember);
        await context.SaveChangesAsync();
        return (user.Id, tenant.Id, other.Id, member.Id);
    }

    private async Task<HumanCommitSubject> SubjectAsync(Guid id) =>
        await HumanCommitSubject.ResolveAsync(
            new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id.ToString())], "fixture")),
            new ResolveCurrentUserIdByIdentityRequestHandler(new ExternalLoginAuthenticationRepository(
                new GlobalExploreDbContextFactory(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global)), fixture.NativeQueueEnrollment)),
            CancellationToken.None) ?? throw new InvalidOperationException("A resolved subject is required.");

    private ExploreDbContext Open(Guid tenant)
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options, fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Runtime));
        options.AddInterceptors(PostgresTenantSessionInterceptor.Instance);
        return new ExploreDbContext(options.Options) { TenantContext = new TenantIdentity(tenant) };
    }

    private static string Table(ExploreDbContext context, Type type)
    {
        var entity = context.Model.FindEntityType(type)!;
        return context.GetService<ISqlGenerationHelper>().DelimitIdentifier(entity.GetTableName()!, entity.GetSchema());
    }

    private sealed record TenantIdentity(Guid TenantId) : ITenantContext;
    private static string Text(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
}
