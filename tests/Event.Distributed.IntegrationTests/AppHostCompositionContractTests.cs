using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting;
using Aspire.Hosting.Testing;
using Explore.AppHost;
using Explore.Secrets.Database;
using Microsoft.Extensions.Configuration;

namespace Event.Distributed.IntegrationTests;

[Category("Harness")]
[Category("Distributed")]
public sealed class AppHostCompositionContractTests
{
    [Test]
    public async Task TopologyDefaultsAndCaseVariantsSelectExactlyOneWebComposition()
    {
        foreach (string? topology in new[] { null, "", "  ", " sPlIt ", " sTaNdAlOnE " })
            await InspectAsync(async builder =>
            {
                bool standalone = topology?.Trim().Equals("Standalone", StringComparison.OrdinalIgnoreCase) == true;
                await Assert.That(HasExpectedApplicationProjects(builder, standalone)).IsTrue()
                    .Because("The complete project model must contain exactly the selected web roles and migration worker.");
                builder.AddProject<Projects.Explore_API>("unexpected-web-api", options => options.ExcludeLaunchProfile = true);
                await Assert.That(HasExpectedApplicationProjects(builder, standalone)).IsFalse()
                    .Because("An additional real API project under another name must violate topology exclusivity.");
            }, topology: topology);
    }

    [Test]
    public async Task MalformedTopologiesFailBeforeTheInspectionCallback()
    {
        foreach (string topology in new[] { "Combined", "Split/Standalone", "Standalone-extra" })
        {
            bool inspected = false;
            var failure = await Assert.That(() => InspectAsync(_ =>
            {
                inspected = true;
                return Task.CompletedTask;
            }, topology: topology)).Throws<InvalidOperationException>();
            await Assert.That(failure!.Message).IsEqualTo("Hosting:Topology must be Split or Standalone.");
            await Assert.That(inspected).IsFalse();
        }
    }

    [Test]
    public async Task StandaloneForwardsWebInputsAndWaitsForMigrationCompletion()
    {
        await InspectAsync(async builder =>
        {
            var standalone = Project(builder, "event-standalone");
            var environment = await EnvironmentAsync(builder, standalone);
            foreach (string key in new[] { "HttpsRedirection__Enabled", "INSTANCE_ADMIN_PUBLIC_ORIGIN",
                "Cerbos__PolicyPackagePath", "Storage__Local__RootPath", "Storage__Local__CreateRootIfMissing",
                "StorageReconciliation__Enabled", "StorageReconciliation__DryRun",
                "PrivacyErasure__Authority__Topology", "Bff__AdminHosts__0", "Cerbos__GrpcEndpoint",
                "Keycloak__Authority" })
                await Assert.That(environment.EnvironmentVariables.ContainsKey(key)).IsTrue();
            await Assert.That(environment.EnvironmentVariables["HttpsRedirection__Enabled"] as string).IsEqualTo("false");
            string clientId = builder.Configuration["KEYCLOAK_BLAZOR_CLIENT_ID"] ?? "islamu-event-blazor";
            foreach (string key in new[] { "Keycloak__ClientId", "KEYCLOAK_CLIENT_ID" })
                await Assert.That(environment.EnvironmentVariables.TryGetValue(key, out object? identity)
                    && identity is string selected && string.Equals(selected, clientId, StringComparison.Ordinal)).IsTrue()
                    .Because("Both BFF client identity projections must match the selected input exactly.");
            await Assert.That(standalone.Annotations.OfType<WaitAnnotation>()
                .Any(wait => ReferenceEquals(wait.Resource, Resource(builder, "keycloak"))
                    && wait.WaitType == WaitType.WaitUntilHealthy)).IsTrue();
            await Assert.That(standalone.Annotations.OfType<WaitAnnotation>()
                .Any(wait => ReferenceEquals(wait.Resource, Project(builder, "event-migrationservice"))
                    && wait.WaitType == WaitType.WaitForCompletion && wait.ExitCode == 0)).IsTrue();
        }, topology: "Standalone");
    }

    [Test]
    public async Task MigrationWorkerExistsWithLocalAndExternalDatabaseInputs()
    {
        foreach (string mode in new[] { "local-lite", "local-core" })
            await InspectAsync(async builder =>
            {
                var worker = Project(builder, "event-migrationservice");
                var environment = await EnvironmentAsync(builder, worker);
                await Assert.That(environment.EnvironmentVariables["Database__Provider"] as string).IsEqualTo("PostgreSql");
                await Assert.That(builder.Resources.OfType<PostgresServerResource>().Any()).IsEqualTo(mode == "local-core");
                await Assert.That(environment.EnvironmentVariables.ContainsKey("Database__Migrator__Username")).IsTrue();
            }, runMode: mode);
    }

    [Test]
    public async Task PrivacyMigrationStageIsForwardedToItsConsumersWithoutADefault()
    {
        foreach (string topology in new[] { "Split", "Standalone" })
            foreach (string? stage in new[] { null, "", "  ", "Expand", "  Contract  " })
                await InspectAsync(async builder =>
                {
                    var worker = Project(builder, "event-migrationservice");
                    var backend = Project(builder, topology == "Split" ? "explore-api" : "event-standalone");
                    if (topology == "Split") _ = Project(builder, "explore-blazor");
                    foreach (var project in builder.Resources.OfType<ProjectResource>())
                    {
                        var environment = await EnvironmentAsync(builder, project);
                        bool expected = !string.IsNullOrWhiteSpace(stage)
                            && (ReferenceEquals(project, worker) || ReferenceEquals(project, backend));
                        bool projected = environment.EnvironmentVariables.TryGetValue(
                            "Database__Migrations__EventLocationPrivacyStage", out object? value);
                        await Assert.That(projected).IsEqualTo(expected);
                        if (expected)
                            await Assert.That(value is string selectedStage
                                && string.Equals(selectedStage, stage, StringComparison.Ordinal)).IsTrue()
                                .Because("The selected migration stage must be forwarded verbatim to its actual consumer.");
                    }
                }, topology: topology, stage: stage);
    }

    [Test]
    public async Task FullLocalOspreyUsesItsKafkaBootstrapDependency()
    {
        foreach (string webhook in new[] { "Local", "Svix" })
            await InspectAsync(async builder =>
        {
            var osprey = Resource(builder, "osprey");
            var environment = await EnvironmentAsync(builder, osprey);
            await Assert.That(environment.EnvironmentVariables["OSPREY_KAFKA_BOOTSTRAP_SERVERS"] as string)
                .IsEqualTo("osprey-kafka:29092");
            _ = Resource(builder, "osprey-kafka");
            await Assert.That(osprey.Annotations.OfType<WaitAnnotation>()
                .Any(wait => ReferenceEquals(wait.Resource, Resource(builder, "osprey-kafka-bootstrap"))
                    && wait.WaitType == WaitType.WaitForCompletion && wait.ExitCode == 0)).IsTrue();
            await Assert.That(builder.Resources.Any(resource => resource.Name == "svix")).IsEqualTo(webhook == "Svix");
        }, runMode: "local-full", webhook: webhook);
    }

    [Test]
    public async Task CerbosProjectsItsAllocatedGrpcEndpointAndPostgresStore()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        await OwnedAppHostFixture.InspectCompositionAsync("Split", "local-default", "EmbeddedSqlite", null, "Local", async builder =>
        {
            var cerbos = Resource(builder, "cerbos");
            var grpc = cerbos.Annotations.OfType<EndpointAnnotation>().Single(endpoint => endpoint.Name == "grpc");
            grpc.AllocatedEndpoint = new AllocatedEndpoint(grpc, "127.0.0.1", 13593, "3593");
            var api = await EnvironmentAsync(builder, Project(builder, "explore-api"));
            await Assert.That(await ResolveAsync(api.EnvironmentVariables["CERBOS_GRPC_ENDPOINT"]) == "http://127.0.0.1:13593").IsTrue();
            await Assert.That(await ResolveAsync(api.EnvironmentVariables["Cerbos__GrpcEndpoint"]) == "http://127.0.0.1:13593").IsTrue();
            var environment = await EnvironmentAsync(builder, cerbos);
            var database = builder.Resources.OfType<ContainerResource>().Single(resource => resource.Name == "cerbos-db");
            var databaseEnvironment = await EnvironmentAsync(builder, database);
            await Assert.That(await PostgresStoreTargetsCerbosAsync(environment.EnvironmentVariables["CERBOS_PG_URL"],
                database, databaseEnvironment.EnvironmentVariables["POSTGRES_DB"] as string, deadline.Token)).IsTrue()
                .Because("Cerbos must target its selected PostgreSQL store.");
            await Assert.That(cerbos.Annotations.OfType<WaitAnnotation>()
                .Any(wait => ReferenceEquals(wait.Resource, database)
                    && wait.WaitType == WaitType.WaitUntilHealthy)).IsTrue();
        }, deadline.Token);
    }

    [Test]
    public async Task CompositionPreservesTheStableNamedHttpsEndpoints()
    {
        foreach (string topology in new[] { "Split", "Standalone" })
            await InspectAsync(async builder =>
            {
                foreach (var project in builder.Resources.OfType<ProjectResource>()
                    .Where(resource => resource.Name != "event-migrationservice"))
                {
                    var endpoint = project.Annotations.OfType<EndpointAnnotation>().Single(value => value.Name == "https");
                    int expected = project.Name == "explore-api" ? 7039 : project.Name == "explore-blazor" ? 7177 : 7180;
                    await Assert.That(endpoint.Port).IsEqualTo(expected);
                    await Assert.That(endpoint.UriScheme).IsEqualTo("https");
                }
            }, topology: topology);
    }

    [Test]
    public async Task FormbricksExistsOnlyInFullLocalAndSharesTheSelectedEscapedDatabasePassword()
    {
        foreach (string mode in new[] { "local-lite", "local-core", "local-default", "local-full" })
            await InspectAsync(async builder =>
            {
                string[] names = builder.Resources.OfType<ContainerResource>()
                    .Where(resource => resource.Name.StartsWith("formbricks", StringComparison.Ordinal))
                    .Select(resource => resource.Name).ToArray();
                await Assert.That(names).IsEquivalentTo(mode == "local-full"
                    ? new[] { "formbricks-postgres", "formbricks-redis", "formbricks-migrate",
                        "formbricks-hub-migrate", "formbricks-hub", "formbricks-cube", "formbricks" } : []);
                if (mode != "local-full") return;
                var postgres = await EnvironmentAsync(builder, Resource(builder, "formbricks-postgres"));
                var cube = await EnvironmentAsync(builder, Resource(builder, "formbricks-cube"));
                object password = postgres.EnvironmentVariables["POSTGRES_PASSWORD"];
                await Assert.That(password is ParameterResource { Secret: true }
                    && ReferenceEquals(password, cube.EnvironmentVariables["CUBEJS_DB_PASS"])).IsTrue();
                string? selected = builder.Configuration["TestingEnvironment:FORMBRICKS_DATABASE_PASSWORD"];
                await Assert.That(await ResolveAsync(password) == selected && !string.IsNullOrEmpty(selected)).IsTrue();
                foreach (string name in new[] { "formbricks", "formbricks-migrate", "formbricks-hub", "formbricks-hub-migrate" })
                {
                    var environment = await EnvironmentAsync(builder, Resource(builder, name));
                    string? url = await ResolveAsync(environment.EnvironmentVariables["DATABASE_URL"]);
                    await Assert.That(PasswordRoundTrips(url, selected)).IsTrue();
                }
            }, runMode: mode);
    }

    [Test]
    public async Task FormbricksRejectsEachMissingSelectedSecretInsteadOfFallingBack()
    {
        await InspectAsync(async builder =>
        {
            foreach (var pair in new[]
            {
                ("FORMBRICKS_NEXTAUTH_SECRET", "formbricks-nextauth-secret"),
                ("FORMBRICKS_ENCRYPTION_KEY", "formbricks-encryption-key"),
                ("FORMBRICKS_CRON_SECRET", "formbricks-cron-secret"),
                ("FORMBRICKS_HUB_API_KEY", "formbricks-hub-api-key"),
                ("FORMBRICKS_CUBEJS_API_SECRET", "formbricks-cube-api-secret"),
                ("FORMBRICKS_DATABASE_PASSWORD", "formbricks-database-password")
            })
            {
                builder.Configuration[pair.Item1] = null;
                var parameter = builder.Resources.OfType<ParameterResource>().Single(resource => resource.Name == pair.Item2);
                var failure = await Assert.That(async () => await ResolveAsync(parameter)).Throws<InvalidOperationException>();
                await Assert.That(failure!.Message).IsEqualTo("agent_browser_selected_authority_missing:" + pair.Item1);
            }
        }, runMode: "local-full");
    }

    [Test]
    public async Task ErasureTopologySelectsManagedResourcesAndKeepsTheBffOutsideDatabaseAuthority()
    {
        foreach (string topology in new[] { "Split", "Standalone" })
          foreach (string mode in new[] { "local-lite", "local-core", "local-default", "local-full" })
            foreach (string erasure in new[] { "EmbeddedSqlite", "ExternalDatabase" })
                await InspectAsync(async builder =>
                {
                    var worker = Project(builder, "event-migrationservice");
                    var backend = Project(builder, topology == "Split" ? "explore-api" : "event-standalone");
                    if (topology == "Split") _ = Project(builder, "explore-blazor");
                    await Assert.That(HasExpectedApplicationProjects(builder, topology == "Standalone")).IsTrue();
                    bool localAuthority = mode != "local-lite" && erasure == "ExternalDatabase";
                    var database = builder.Resources.OfType<PostgresDatabaseResource>()
                        .SingleOrDefault(resource => resource.Name == "privacy-erasure-authority");
                    var server = builder.Resources.OfType<PostgresServerResource>()
                        .SingleOrDefault(resource => resource.Name == "privacy-erasure-postgres");
                    await Assert.That(database is not null).IsEqualTo(localAuthority);
                    await Assert.That(server is not null).IsEqualTo(localAuthority);
                    foreach (var resource in builder.Resources.OfType<IResourceWithEnvironment>())
                        await Assert.That(HasNoBroadNamedErasureConnection(await EnvironmentAsync(builder, resource))).IsTrue()
                            .Because("Every environment-capable resource must exclude active broad named erasure connections.");
                    foreach (var project in builder.Resources.OfType<ProjectResource>())
                    {
                        var environment = await EnvironmentAsync(builder, project);
                        bool bff = project.Name is "explore-blazor" or "explore-blazor-peer";
                        await Assert.That(environment.EnvironmentVariables.Any(pair => pair.Key.StartsWith(
                            "PrivacyErasureAuthorityDatabase__", StringComparison.Ordinal) && pair.Value is not (null or "")))
                            .IsEqualTo(!bff && erasure == "ExternalDatabase");
                        await Assert.That(environment.EnvironmentVariables.Any(pair =>
                            pair.Key == "PrivacyErasureAuthorityEmbedded__Path" && pair.Value is not (null or "")))
                            .IsEqualTo(!bff && erasure == "EmbeddedSqlite");
                        if (bff)
                        {
                            await Assert.That(BffHasNoDeclaredDatabaseAuthority(environment)).IsTrue()
                                .Because("A BFF-only resource must have no active primary or erasure database authority.");
                            await Assert.That(BffHasNoErasureDependency(project, environment, database, server)).IsTrue()
                                .Because("A BFF-only resource must not directly reference or wait for the erasure database or its server.");
                            builder.CreateResourceBuilder(project)
                                .WithEnvironment("PrivacyErasureAuthorityDatabase__Runtime__Password", string.Empty)
                                .WithEnvironment("PrivacyErasureAuthorityEmbedded__Path", string.Empty)
                                .WithEnvironment("ConnectionStrings__PrivacyErasureAuthority", string.Empty);
                            environment = await EnvironmentAsync(builder, project);
                            await Assert.That(BffHasNoDeclaredDatabaseAuthority(environment)).IsTrue()
                                .Because("Explicit empty authority denial placeholders grant no database authority.");
                            await Assert.That(HasNoBroadNamedErasureConnection(environment)).IsTrue()
                                .Because("An empty named-connection denial placeholder grants no erasure authority.");
                        }
                        else if (ReferenceEquals(project, backend))
                        {
                            await Assert.That(BffHasNoDeclaredDatabaseAuthority(environment)).IsFalse()
                                .Because("The API role receives its selected primary database authority.");
                            foreach (var purpose in new[]
                            {
                                PrimaryDatabaseRole.EmailPolicyInstanceMutation,
                                PrimaryDatabaseRole.EmailPolicyTenantMutation
                            })
                            {
                                string prefix = $"Database__{purpose}__";
                                bool projected = HasActiveEnvironmentEntry(environment, prefix + "Username")
                                    && HasActiveEnvironmentEntry(environment, prefix + "Password");
                                await Assert.That(projected).IsTrue()
                                    .Because("API and Combined require both selected email-policy database authorities.");
                                bool selectedBinding = false, declaredEmptyDenied = false;
                                CancellationToken token = TestContext.Current!.Execution.CancellationToken;
                                try
                                {
                                    string? username = environment.EnvironmentVariables[prefix + "Username"] switch
                                    {
                                        string text => text,
                                        IValueProvider provider => await provider.GetValueAsync(token),
                                        _ => null
                                    };
                                    string? password = environment.EnvironmentVariables[prefix + "Password"] switch
                                    {
                                        string text => text,
                                        IValueProvider provider => await provider.GetValueAsync(token),
                                        _ => null
                                    };
                                    var shared = PrimaryDatabaseConfiguration.BindRuntime(builder.Configuration);
                                    var selected = PrimaryDatabaseConfiguration.Bind(builder.Configuration, purpose);
                                    string alias = purpose == PrimaryDatabaseRole.EmailPolicyInstanceMutation
                                        ? "DATABASE_EMAIL_POLICY_INSTANCE_MUTATION_" : "DATABASE_EMAIL_POLICY_TENANT_MUTATION_";
                                    // Use owned nonsecret coordinates; model endpoints have not been allocated.
                                    var inputs = new Dictionary<string, string?>
                                    {
                                        ["Database:Provider"] = shared.Provider.ToString(),
                                        ["Database:Host"] = shared.Host, ["Database:Database"] = shared.Database,
                                        ["Database:Schema"] = shared.Schema, ["Database:TlsMode"] = shared.TlsMode.ToString(),
                                        ["Database:TrustServerCertificate"] = shared.TrustServerCertificate.ToString(),
                                        [$"Database:{purpose}:Username"] = username,
                                        [$"Database:{purpose}:Password"] = password,
                                        [alias + "USERNAME"] = shared.Username,
                                        [alias + "PASSWORD"] = shared.Password
                                    };
                                    using var projectedConfiguration = new ConfigurationManager();
                                    projectedConfiguration.AddInMemoryCollection(inputs);
                                    var bound = PrimaryDatabaseConfiguration.Bind(projectedConfiguration, purpose);
                                    selectedBinding = bound.Role == purpose && bound.Username == selected.Username
                                        && bound.Password == selected.Password && password != shared.Password;
                                    inputs[$"Database:{purpose}:Password"] = string.Empty;
                                    inputs[alias + "PASSWORD"] = selected.Password;
                                    try
                                    {
                                        using var denied = new ConfigurationManager();
                                        denied.AddInMemoryCollection(inputs);
                                        _ = PrimaryDatabaseConfiguration.Bind(denied, purpose);
                                    }
                                    catch (Microsoft.Extensions.Options.OptionsValidationException failure)
                                    {
                                        declaredEmptyDenied = failure.Failures.Contains("PostgreSql requires Password.", StringComparer.Ordinal);
                                    }
                                }
                                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                                catch (Exception) { selectedBinding = false; }
                                await Assert.That(selectedBinding && declaredEmptyDenied).IsTrue()
                                    .Because("Native email-policy role binding must preserve selected authority and declared-empty denial.");
                            }
                        }
                        if (!ReferenceEquals(project, backend))
                            foreach (string purpose in new[] { "EmailPolicyInstanceMutation", "EmailPolicyTenantMutation" })
                                await Assert.That(!HasActiveEnvironmentEntry(environment, $"Database__{purpose}__Password")
                                    && !HasActiveEnvironmentEntry(environment, purpose == "EmailPolicyInstanceMutation"
                                        ? "DATABASE_EMAIL_POLICY_INSTANCE_MUTATION_PASSWORD"
                                        : "DATABASE_EMAIL_POLICY_TENANT_MUTATION_PASSWORD")).IsTrue()
                                    .Because("BFF-only and migration resources must not receive email-policy workflow passwords.");
                        if (!bff && erasure == "EmbeddedSqlite")
                        {
                            string root = builder.Configuration["ISLAMU_TEST_ROOT"]!;
                            await Assert.That(environment.EnvironmentVariables["PrivacyErasureAuthorityEmbedded__Path"] is string path
                                && Path.GetFullPath(path) == Path.Combine(root, "privacy-erasure-authority-data",
                                    "aspire-local", "privacy_erasure_authority.db")).IsTrue();
                            await Assert.That(environment.EnvironmentVariables["PrivacyErasureAuthorityEmbedded__WriterReplicaCount"] as string)
                                .IsEqualTo("1");
                            await Assert.That(project.Annotations.OfType<ReplicaAnnotation>().Single().Replicas).IsEqualTo(1);
                        }
                        if (!bff && erasure == "ExternalDatabase")
                        {
                            string role = ReferenceEquals(project, worker) ? "Migrator" : "Runtime";
                            string otherRole = role == "Migrator" ? "Runtime" : "Migrator";
                            foreach (string family in new[] { "PrivacyErasureAuthorityDatabase", "Database__Erasure" })
                            {
                                await Assert.That(HasActiveEnvironmentEntry(environment, $"{family}__{role}__Username")
                                    && HasActiveEnvironmentEntry(environment, $"{family}__{role}__Password")).IsTrue()
                                    .Because("Each erasure consumer must receive active credentials for its own purpose.");
                                await Assert.That(environment.EnvironmentVariables.Any(pair =>
                                    NormalizeEnvironmentKey(pair.Key).StartsWith(
                                        NormalizeEnvironmentKey($"{family}__{otherRole}__"), StringComparison.Ordinal)
                                    && pair.Value is not (null or ""))).IsFalse()
                                    .Because("An erasure consumer must not receive the other purpose's credentials.");
                            }
                        }
                    }
                    if (mode == "local-core" && erasure == "EmbeddedSqlite")
                    {
                        string runId = builder.Configuration["ISLAMU_TEST_RUN_ID"]!;
                        foreach (string? volume in new[] { null, $"islamu-test-{runId}-privacy-erasure-authority-data" })
                        {
                            var container = new ContainerResource(volume is null ? "erasure-default-deployment" : "erasure-owned-deployment");
                            EmbeddedPrivacyErasureDeployment.Configure(volume)(builder.CreateResourceBuilder(container));
                            var deployed = await EnvironmentAsync(builder, container);
                            await Assert.That(deployed.EnvironmentVariables["PrivacyErasureAuthorityEmbedded__Path"] as string)
                                .IsEqualTo("/app/data/privacy_erasure_authority.db");
                            var mount = container.Annotations.OfType<ContainerMountAnnotation>().Single();
                            await Assert.That(mount.Type == ContainerMountType.Volume && !mount.IsReadOnly).IsTrue();
                            await Assert.That(mount.Target).IsEqualTo("/app/data");
                            await Assert.That(mount.Source).IsEqualTo(volume ?? "islamu-event-privacy-erasure-authority-data");
                        }
                    }
                }, topology: topology, runMode: mode, erasure: erasure);

        foreach (string topology in new[] { "Split", "Standalone" })
            foreach (string? alias in new[] { null, "PrivacyErasureAuthority", "PrivacyErasureAuthorityMigrator" })
                await InspectAsync(async builder =>
                {
                    _ = Project(builder, "event-migrationservice");
                    _ = Project(builder, topology == "Split" ? "explore-api" : "event-standalone");
                    if (topology == "Split") _ = Project(builder, "explore-blazor");
                    var database = builder.Resources.OfType<PostgresDatabaseResource>()
                        .Single(resource => resource.Name == "privacy-erasure-authority");
                    string key = "ConnectionStrings__" + (alias ?? database.Name);
                    foreach (var project in builder.Resources.OfType<ProjectResource>())
                    {
                        var baseline = await EnvironmentAsync(builder, project);
                        await Assert.That(HasNoBroadNamedErasureConnection(baseline)).IsTrue()
                            .Because("Each named-reference counterexample must start from its own clean consumer.");
                        var consumer = builder.CreateResourceBuilder(project);
                        if (alias is null) consumer.WithReference(builder.CreateResourceBuilder(database));
                        else consumer.WithReference(builder.CreateResourceBuilder(database), connectionName: alias);
                        var contaminated = await EnvironmentAsync(builder, project);
                        await Assert.That(contaminated.EnvironmentVariables.TryGetValue(key, out object? connection)
                            && connection is IValueProvider).IsTrue()
                            .Because("The native reference must project this consumer's unresolved named connection.");
                        await Assert.That(HasNoBroadNamedErasureConnection(contaminated)).IsFalse()
                            .Because("A broad named erasure connection must be rejected on every consumer role.");
                        if (project.Name is "explore-blazor" or "explore-blazor-peer")
                            await Assert.That(BffHasNoDeclaredDatabaseAuthority(contaminated)).IsFalse()
                                .Because("The BFF boundary must reject its native named erasure connection.");
                    }
                }, topology: topology, runMode: "local-core", erasure: "ExternalDatabase");

        foreach (bool waitOnly in new[] { false, true })
            await InspectAsync(async builder =>
            {
                var bff = Project(builder, "explore-blazor");
                var database = builder.Resources.OfType<PostgresDatabaseResource>()
                    .Single(resource => resource.Name == "privacy-erasure-authority");
                var server = builder.Resources.OfType<PostgresServerResource>()
                    .Single(resource => resource.Name == "privacy-erasure-postgres");
                var baseline = await EnvironmentAsync(builder, bff);
                await Assert.That(BffHasNoErasureDependency(bff, baseline, database, server)).IsTrue()
                    .Because("Each provenance counterexample must start without a direct erasure dependency.");
                var consumer = builder.CreateResourceBuilder(bff);
                if (waitOnly) consumer.WaitFor(builder.CreateResourceBuilder(database));
                else consumer.WithReference(builder.CreateResourceBuilder(database), connectionName: "unrelated-authority-alias");
                var contaminated = await EnvironmentAsync(builder, bff);
                await Assert.That(HasNoBroadNamedErasureConnection(contaminated)).IsTrue()
                    .Because("A differently named reference or wait must be checked by actual resource provenance.");
                if (!waitOnly)
                    await Assert.That(contaminated.EnvironmentVariables.TryGetValue(
                        "ConnectionStrings__unrelated-authority-alias", out object? connection)
                        && connection is IValueProvider).IsTrue();
                await Assert.That(BffHasNoErasureDependency(bff, contaminated, database, server)).IsFalse()
                    .Because("A direct erasure reference under any alias, or a direct wait, violates the BFF boundary.");
            }, runMode: "local-core", erasure: "ExternalDatabase");
    }

    [Test]
    public async Task InspectionCompletesBeforeFixtureBootstrapAndCleansItsOwnedRoot()
    {
        string? root = null;
        await InspectAsync(async builder =>
        {
            root = builder.Configuration["ISLAMU_TEST_ROOT"];
            await Assert.That(File.Exists(Path.Combine(root!, "database-logins.sql"))).IsFalse();
            await Assert.That(File.Exists(Path.Combine(root!, "password-blocklist.json"))).IsFalse();
        });
        await Assert.That(root is not null && !Directory.Exists(root)).IsTrue();
    }

    [Test]
    public async Task PublicCreationAndStartupRejectTheInspectionProfile()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await Assert.That(async () =>
        {
            await using var unexpectedOwner = await OwnedAppHostFixture.CreateAsync("Split", deadline.Token,
                profile: OwnedAppHostProfile.ClosedCompositionModel);
        }).Throws<InvalidOperationException>();
        await using var owner = new OwnedAppHostFixture("Split", OwnedAppHostProfile.ClosedCompositionModel);
        await Assert.That(() => owner.StartAsync(deadline.Token)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task NoRunDisposesAnAccidentallyBuiltApplicationAndPreservesCleanupFailure()
    {
        foreach (bool failCleanup in new[] { false, true })
        {
            await using var application = new DisposalBoundary(failCleanup);
            var isolation = new TestingResourceIsolation("unused", "unused", OwnedAppHostProfile.ClosedCompositionModel);
            Exception? failure = null;
            try { await isolation.DenyInspectionRunAsync(application); }
            catch (Exception exception) { failure = exception; }
            await Assert.That(application.Disposed).IsTrue();
            await Assert.That(failCleanup
                ? failure is AggregateException aggregate && aggregate.InnerExceptions.Count == 2
                    && aggregate.InnerExceptions[0].Message == "test_resource_inspection_cannot_run"
                    && ReferenceEquals(aggregate.InnerExceptions[1], application.Failure)
                : failure is InvalidOperationException { Message: "test_resource_inspection_cannot_run" }).IsTrue();
        }
    }

    [Test]
    public async Task CancelledInspectionPropagatesItsTokenAndCleansItsOwnedRoot()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        string? root = null;
        var failure = await Assert.That(() => OwnedAppHostFixture.InspectCompositionAsync("Split", "local-default",
            "EmbeddedSqlite", null, "Local", builder =>
            {
                root = builder.Configuration["ISLAMU_TEST_ROOT"];
                cancellation.Cancel();
                return Task.CompletedTask;
            }, cancellation.Token)).Throws<OperationCanceledException>();
        await Assert.That(failure!.CancellationToken == cancellation.Token).IsTrue();
        await Assert.That(root is not null && !Directory.Exists(root)).IsTrue();
    }

    [Test]
    public async Task UnexpectedInspectionAndCleanupFailuresRemainVisible()
    {
        var primary = new InvalidOperationException("assurance_inspection_primary");
        string? root = null;
        var failure = await Assert.That(() => InspectAsync(builder =>
        {
            root = builder.Configuration["ISLAMU_TEST_ROOT"];
            throw primary;
        })).Throws<InvalidOperationException>();
        await Assert.That(ReferenceEquals(primary, failure)).IsTrue();
        await Assert.That(root is not null && !Directory.Exists(root)).IsTrue();
        await using var builderCleanup = new DisposalBoundary(true);
        await using (var retainedOwner = new OwnedAppHostFixture("Split"))
        {
            var aggregate = await Assert.That(() => OwnedAppHostFixture.DisposeFailedCreationAsync(primary,
                builderCleanup, retainedOwner)).Throws<AggregateException>();
            await Assert.That(builderCleanup.Disposed && Directory.Exists(retainedOwner.Root)).IsTrue();
            await Assert.That(aggregate!.InnerExceptions.Count == 2
                && ReferenceEquals(aggregate.InnerExceptions[0], primary)
                && ReferenceEquals(aggregate.InnerExceptions[1], builderCleanup.Failure)).IsTrue();
        }
        await using var settledBuilder = new DisposalBoundary(false);
        await using var ownerCleanup = new DisposalBoundary(true);
        var ownerFailure = await Assert.That(() => OwnedAppHostFixture.DisposeFailedCreationAsync(primary,
            settledBuilder, ownerCleanup)).Throws<AggregateException>();
        await Assert.That(settledBuilder.Disposed && ownerCleanup.Disposed).IsTrue();
        await Assert.That(ownerFailure!.InnerExceptions.Count == 2
            && ReferenceEquals(ownerFailure.InnerExceptions[0], primary)
            && ReferenceEquals(ownerFailure.InnerExceptions[1], ownerCleanup.Failure)).IsTrue();
        string? runId = null;
        root = null;
        try
        {
            var completionCleanup = await Assert.That(() => InspectAsync(builder =>
            {
                root = builder.Configuration["ISLAMU_TEST_ROOT"];
                runId = builder.Configuration["ISLAMU_TEST_RUN_ID"];
                File.Delete(Path.Combine(root!, TestingResourceIsolation.MarkerName));
                return Task.CompletedTask;
            })).Throws<AggregateException>();
            await Assert.That(completionCleanup!.InnerExceptions.Any(exception => exception
                is InvalidOperationException { Message: "test_resource_marker_invalid" })).IsTrue();
            await Assert.That(root is not null && Directory.Exists(root)).IsTrue();
        }
        finally
        {
            if (root is not null && runId is not null && Directory.Exists(root))
            {
                File.WriteAllText(Path.Combine(root, TestingResourceIsolation.MarkerName), runId);
                new TestingResourceIsolation(runId, root).VerifyOwnership();
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static async Task InspectAsync(Func<IDistributedApplicationTestingBuilder, Task> inspect,
        string? topology = "Split", string runMode = "local-default", string erasure = "EmbeddedSqlite",
        string? stage = null, string webhook = "Local")
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        await OwnedAppHostFixture.InspectCompositionAsync(topology, runMode, erasure, stage, webhook, inspect, deadline.Token);
    }

    private static ProjectResource Project(IDistributedApplicationTestingBuilder builder, string name) =>
        builder.Resources.OfType<ProjectResource>().Single(resource => resource.Name == name);

    private static IResourceWithEnvironment Resource(IDistributedApplicationTestingBuilder builder, string name) =>
        builder.Resources.OfType<IResourceWithEnvironment>().Single(resource => resource.Name == name);

    private static async Task<EnvironmentCallbackContext> EnvironmentAsync(IDistributedApplicationTestingBuilder builder,
        IResourceWithEnvironment resource)
    {
        var context = new EnvironmentCallbackContext(builder.ExecutionContext, resource);
        foreach (var callback in resource.Annotations.OfType<EnvironmentCallbackAnnotation>())
            await callback.Callback(context);
        return context;
    }

    private static async Task<string?> ResolveAsync(object value) => value switch
    {
        string text => text,
        IValueProvider provider => await provider.GetValueAsync(),
        _ => throw new InvalidOperationException("assurance_projection_value_invalid")
    };

    private static bool PasswordRoundTrips(string? url, string? password)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "postgresql"
            || uri.Host != "formbricks-postgres" || uri.Port != 5432 || uri.AbsolutePath != "/formbricks") return false;
        return Uri.UnescapeDataString(uri.UserInfo) == "postgres:" + password;
    }

    private static bool HasExpectedApplicationProjects(IDistributedApplicationTestingBuilder builder, bool standalone)
    {
        string[] expected = standalone
            ? ["event-migrationservice", "event-standalone"]
            : ["event-migrationservice", "explore-api", "explore-blazor"];
        return builder.Resources.OfType<ProjectResource>().Select(resource => resource.Name)
            .Order(StringComparer.Ordinal).SequenceEqual(expected, StringComparer.Ordinal);
    }

    private static string NormalizeEnvironmentKey(string key) =>
        key.Replace("__", ":", StringComparison.Ordinal).ToUpperInvariant();

    private static bool HasActiveEnvironmentEntry(EnvironmentCallbackContext context, string key) =>
        context.EnvironmentVariables.TryGetValue(key, out object? value) && value is not (null or "");

    private static bool HasNoBroadNamedErasureConnection(EnvironmentCallbackContext context) =>
        !context.EnvironmentVariables.Any(pair => pair.Value is not (null or "")
            && (NormalizeEnvironmentKey(pair.Key).StartsWith("CONNECTIONSTRINGS:PRIVACYERASUREAUTHORITY", StringComparison.Ordinal)
                || NormalizeEnvironmentKey(pair.Key).StartsWith("CONNECTIONSTRINGS:PRIVACY-ERASURE-AUTHORITY", StringComparison.Ordinal)));

    private static bool BffHasNoErasureDependency(ProjectResource project, EnvironmentCallbackContext environment,
        PostgresDatabaseResource? database, PostgresServerResource? server)
    {
        if (database is null && server is null) return true;
        bool IsAuthority(object value) => value is not null
            && (ReferenceEquals(value, database) || ReferenceEquals(value, server));
        if (project.Annotations.OfType<WaitAnnotation>().Any(wait => IsAuthority(wait.Resource))
            || project.Annotations.OfType<ResourceRelationshipAnnotation>().Any(relationship => IsAuthority(relationship.Resource)))
            return false;

        var pending = new Stack<object>(environment.EnvironmentVariables.Values);
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        while (pending.TryPop(out object? value))
        {
            if (value is null || !visited.Add(value)) continue;
            if (IsAuthority(value)) return false;
            if (value is IResource) continue;
            if (value is ConnectionStringReference connection && IsAuthority(connection.Resource)) return false;
            if (value is IValueWithReferences references)
                foreach (object reference in references.References)
                    if (reference is not null) pending.Push(reference);
        }
        return true;
    }

    private static bool BffHasNoDeclaredDatabaseAuthority(EnvironmentCallbackContext context) =>
        !context.EnvironmentVariables.Any(pair =>
        {
            string name = NormalizeEnvironmentKey(pair.Key);
            bool database = name.StartsWith("DATABASE:", StringComparison.Ordinal)
                || name.StartsWith("DATABASE_", StringComparison.Ordinal)
                || name.StartsWith("DATABASEERASURE:", StringComparison.Ordinal)
                || name.StartsWith("PRIVACYERASUREAUTHORITYDATABASE:", StringComparison.Ordinal)
                || name.StartsWith("ERASURE_DATABASE_", StringComparison.Ordinal)
                || name.StartsWith("PRIVACY_ERASURE_AUTHORITY_", StringComparison.Ordinal)
                || name.StartsWith("CONNECTIONSTRINGS:PRIVACYERASUREAUTHORITY", StringComparison.Ordinal)
                || name is "CONNECTIONSTRINGS:ISLAMU-EVENT-DB" or "CONNECTIONSTRINGS:PRIVACY-ERASURE-AUTHORITY";
            return database && pair.Value is not (null or "");
        });

    private static async Task<bool> PostgresStoreTargetsCerbosAsync(object value, ContainerResource database,
        string? databaseName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            string? candidate = value switch
            {
                string text => text,
                IValueProvider provider => await provider.GetValueAsync(cancellationToken),
                _ => null
            };
            return !string.IsNullOrWhiteSpace(databaseName)
                && Uri.TryCreate(candidate, UriKind.Absolute, out var uri) && uri.Scheme == "postgres"
                && string.Equals(uri.Host, database.Name, StringComparison.OrdinalIgnoreCase) && uri.Port == 5432
                && Uri.UnescapeDataString(uri.AbsolutePath) == "/" + databaseName
                && uri.Query.Contains("search_path=cerbos", StringComparison.Ordinal);
        }
        catch (OperationCanceledException exception) when (exception.CancellationToken == cancellationToken
            && cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return false; }
    }

    private sealed class DisposalBoundary(bool fail) : IAsyncDisposable
    {
        internal bool Disposed { get; private set; }
        internal Exception Failure { get; } = new InvalidOperationException("assurance_cleanup_failure");
        public ValueTask DisposeAsync()
        {
            if (Disposed) return ValueTask.CompletedTask;
            Disposed = true;
            return fail ? ValueTask.FromException(Failure) : ValueTask.CompletedTask;
        }
    }
}
