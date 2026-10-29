using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;

namespace Explore.AppHost;

public sealed record TestingResourceIsolation(string RunId, string Root,
    OwnedAppHostProfile Profile = OwnedAppHostProfile.Standard)
{
    public const string MarkerName = ".islamu-test-owner";

    public static void ConfigureOwnedInputs(ConfigurationManager configuration, string[] arguments)
    {
        using var explicitInputs = new ConfigurationManager();
        explicitInputs.AddCommandLine(arguments);
        string? runId = explicitInputs["ISLAMU_TEST_RUN_ID"];
        var authority = configuration.Sources.OfType<MemoryConfigurationSource>()
            .SingleOrDefault(source => source.InitialData?.Any(pair =>
                pair.Key == "TestingEnvironment:ISLAMU_TEST_RUN_ID" && pair.Value == runId) == true)
            ?? throw new InvalidOperationException("test_resource_authority_missing");
        var ownedInputs = authority.InitialData!.ToArray();
        string[] technicalKeys = ["AppHost:Directory", "AppHost:Path", "AppHost:Sha256",
            "DcpPublisher:CliPath", "DcpPublisher:RandomizePorts"];
        var technicalInputs = technicalKeys.Select(key => new KeyValuePair<string, string?>(key, configuration[key])).ToArray();
        configuration.Sources.Clear();
        configuration.AddInMemoryCollection(technicalInputs)
            .AddInMemoryCollection(ownedInputs).AddCommandLine(arguments);
    }

    public static TestingResourceIsolation? Admit(IConfiguration configuration)
    {
        string? runId = configuration["ISLAMU_TEST_RUN_ID"];
        string? root = configuration["ISLAMU_TEST_ROOT"];
        if (runId is null && root is null)
        {
            if (configuration["ISLAMU_TEST_PROFILE"] is not (null or ""))
                throw new InvalidOperationException("test_resource_owner_invalid");
            return null;
        }
        if (!Guid.TryParseExact(runId, "N", out _) || root is null
            || !Path.IsPathFullyQualified(root) || Path.GetFullPath(root) != root
            || Path.GetFileName(root) != runId)
            throw new InvalidOperationException("test_resource_owner_invalid");
        OwnedAppHostProfile profile = ValidateProfile(configuration);
        string marker = Path.Combine(root, MarkerName);
        var owner = new TestingResourceIsolation(runId, root, profile);
        owner.VerifyOwnership();
        if (Directory.EnumerateFileSystemEntries(root).Any(path => path != marker))
            throw new InvalidOperationException("test_resource_root_not_empty");
        return owner;
    }

    public void VerifyOwnership()
    {
        EnsureNoLinks(Root);
        string marker = Path.Combine(Root, MarkerName);
        if (!Directory.Exists(Root) || !File.Exists(marker)
            || new FileInfo(marker).LinkTarget is not null || File.ReadAllText(marker) != RunId)
            throw new InvalidOperationException("test_resource_marker_invalid");
    }

    public static OwnedAppHostProfile ValidateProfile(IConfiguration configuration)
    {
        OwnedAppHostProfile profile = configuration["ISLAMU_TEST_PROFILE"] switch
        {
            null or "" or "Standard" => OwnedAppHostProfile.Standard,
            "ExternalIdentitySharedBff" => OwnedAppHostProfile.ExternalIdentitySharedBff,
            "ClosedCompositionModel" => OwnedAppHostProfile.ClosedCompositionModel,
            _ => throw new InvalidOperationException("test_resource_profile_invalid")
        };
        if (configuration["DOTNET_ENVIRONMENT"] != "Assurance"
            || configuration["ASPNETCORE_ENVIRONMENT"] is not (null or "Assurance")
            || (profile == OwnedAppHostProfile.ClosedCompositionModel
                ? configuration["ISLAMU_ASPIRE_MODE"] is not ("local-lite" or "local-core" or "local-default" or "local-full")
                : configuration["ISLAMU_ASPIRE_MODE"] != "local-default"
                    || configuration["Hosting:Topology"] is not ("Split" or "Standalone"))
            || configuration["SECRET_PROVIDER"] != "Environment"
            || configuration["SecretProvider:Provider"] is not (null or "Environment")
            || configuration["AGENT_BROWSER_SEED_ENABLED"] is not (null or "false")
            || configuration["CONFIGURATION_MANIFEST_MODE"] is not (null or "Off")
            || (profile == OwnedAppHostProfile.ClosedCompositionModel
                ? configuration["PrivacyErasure:Authority:Topology"] is not ("EmbeddedSqlite" or "ExternalDatabase")
                    || configuration["ERASURE_DATABASE_TOPOLOGY"] != configuration["PrivacyErasure:Authority:Topology"]
                : configuration["PrivacyErasure:Authority:Topology"] is not (null or "EmbeddedSqlite")
                    || configuration["ERASURE_DATABASE_TOPOLOGY"] is not (null or "EmbeddedSqlite"))
            || profile != OwnedAppHostProfile.ExternalIdentitySharedBff
                && configuration["IDENTITY_DATABASE_TOPOLOGY"] is not (null or "CoLocated")
            || (profile == OwnedAppHostProfile.ClosedCompositionModel
                ? configuration["WEBHOOKS_PROVIDER"] is not ("Local" or "Svix")
                : configuration["WEBHOOKS_PROVIDER"] is not (null or "Local")))
            throw new InvalidOperationException("test_resource_profile_invalid");
        if (profile == OwnedAppHostProfile.ExternalIdentitySharedBff)
            ValidateExternalIdentity(configuration);
        return profile;
    }

    private static void ValidateExternalIdentity(IConfiguration configuration)
    {
        string? root = configuration["ISLAMU_TEST_ROOT"];
        if (configuration["Hosting:Topology"] != "Split" || root is null || !Path.IsPathFullyQualified(root))
            throw new InvalidOperationException("test_resource_profile_invalid");
        string path = Path.Combine(root, "identity.sqlite");
        if (configuration["IDENTITY_DATABASE_TOPOLOGY"] != "External"
            || configuration["IDENTITY_DATABASE_PROVIDER"] != "Sqlite"
            || configuration["IDENTITY_DATABASE_NAME"] != path)
            throw new InvalidOperationException("test_resource_profile_invalid");
        var expected = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["IDENTITY_DATABASE_TOPOLOGY"] = "External",
            ["IdentityDatabase:Topology"] = "External",
            ["IDENTITY_DATABASE_PROVIDER"] = "Sqlite",
            ["IdentityDatabase:Provider"] = "Sqlite",
            ["IDENTITY_DATABASE_NAME"] = path,
            ["IdentityDatabase:Name"] = path
        };
        foreach (var pair in configuration.AsEnumerable())
        {
            string key = pair.Key.Replace("__", ":", StringComparison.Ordinal);
            if (pair.Value is null || !(key.StartsWith("IdentityDatabase:", StringComparison.OrdinalIgnoreCase)
                || key.StartsWith("IDENTITY_DATABASE_", StringComparison.OrdinalIgnoreCase)))
                continue;
            if (expected.TryGetValue(key, out string? value)
                ? pair.Value != value : pair.Value.Length != 0)
                throw new InvalidOperationException("test_resource_profile_invalid");
        }
        EnsureNoLinks(root);
        if (new FileInfo(path).LinkTarget is not null || Directory.Exists(path))
            throw new InvalidOperationException("test_resource_symbolic_path");
    }

    public bool TryCaptureConfiguredContainerModel(IDistributedApplicationBuilder builder, out byte[] snapshot)
    {
        snapshot = [];
        try
        {
            if (!Guid.TryParseExact(RunId, "N", out Guid owner) || owner.ToString("N") != RunId)
                return false;
            var containers = builder.Resources.OfType<ContainerResource>()
                .OrderBy(resource => resource.Name, StringComparer.Ordinal).ToArray();
            if (containers.Length > 64) return false;
            var names = new HashSet<string>(StringComparer.Ordinal);
            var rows = new List<object>();
            int budget = 256;
            foreach (var container in containers)
            {
                if (!ModelText(container.Name, 128, ref budget)
                    || container.Name.Any(character => !char.IsAsciiLetterOrDigit(character)
                        && character is not ('-' or '_'))
                    || !names.Add(container.Name)
                    || container.Annotations.OfType<DockerfileBuildAnnotation>().Any()
                    || container.Annotations.OfType<ContainerImageAnnotation>().Count() != 1
                    || !container.TryGetContainerImageName(false, out string? image)
                    || !ModelText(image, 2048, ref budget) || !ModelImageReference(image))
                    return false;
                var policies = container.Annotations.OfType<ContainerImagePullPolicyAnnotation>().ToArray();
                if (policies.Length != 1 || policies[0].ImagePullPolicy != ImagePullPolicy.Never) return false;
                var mounts = container.Annotations.OfType<ContainerMountAnnotation>()
                    .OrderBy(mount => mount.Target, StringComparer.Ordinal).ToArray();
                if (mounts.Length > 32) return false;
                var targets = new HashSet<string>(StringComparer.Ordinal);
                var mountRows = new List<object>();
                foreach (var mount in mounts)
                {
                    string? source = mount.Source;
                    string target = mount.Target;
                    var type = mount.Type;
                    bool readOnly = mount.IsReadOnly;
                    if (!ModelText(source, 4096, ref budget)
                        || !ModelText(target, 4096, ref budget)
                        || !Path.IsPathFullyQualified(target) || Path.GetFullPath(target) != target
                        || target.Length > 1 && target.EndsWith(Path.DirectorySeparatorChar)
                        || !targets.Add(target))
                        return false;
                    string admittedSource = source!;
                    string kind;
                    if (type == ContainerMountType.Volume)
                    {
                        if (!admittedSource.StartsWith(VolumePrefix, StringComparison.Ordinal)
                            || admittedSource.Any(character => !char.IsAsciiLetterOrDigit(character)
                                && character is not ('-' or '_' or '.')))
                            return false;
                        kind = "Volume";
                    }
                    else if (type == ContainerMountType.BindMount)
                    {
                        if (!readOnly || !Path.IsPathFullyQualified(admittedSource)
                            || Path.GetFullPath(admittedSource) != admittedSource)
                            return false;
                        kind = "BindMount";
                    }
                    else return false;
                    mountRows.Add(new { Type = kind, Source = admittedSource, Target = target, ReadOnly = readOnly });
                }
                rows.Add(new { container.Name, Image = image, PullPolicy = "Never", Mounts = mountRows });
            }
            var projects = builder.Resources.OfType<ProjectResource>()
                .OrderBy(resource => resource.Name, StringComparer.Ordinal).ToArray();
            if (projects.Length > 64) return false;
            var projectRows = new List<object>();
            foreach (var project in projects)
            {
                if (!ModelText(project.Name, 128, ref budget)
                    || project.Name.Any(character => !char.IsAsciiLetterOrDigit(character)
                        && character is not ('-' or '_')) || !names.Add(project.Name))
                    return false;
                string? projectPath = project.GetProjectMetadata().ProjectPath;
                if (!ModelText(projectPath, 4096, ref budget) || string.IsNullOrWhiteSpace(projectPath)
                    || !Path.IsPathFullyQualified(projectPath) || Path.GetFullPath(projectPath) != projectPath
                    || !File.Exists(projectPath))
                    return false;
                EnsureNoLinks(projectPath);
                projectRows.Add(new { project.Name, ProjectPath = projectPath });
            }
            byte[] bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
            {
                Schema = 1,
                RunId,
                Containers = rows,
                Projects = projectRows
            });
            if (bytes.Length > 262144) return false;
            snapshot = bytes;
            return true;
        }
        catch (Exception failure) when (failure is ArgumentException or InvalidOperationException
            or NotSupportedException or System.Text.Json.JsonException)
        {
            return false;
        }
    }

    public async Task PublishConfiguredContainerModelAsync(IDistributedApplicationBuilder builder,
        CancellationToken cancellationToken)
    {
        if (Profile == OwnedAppHostProfile.ClosedCompositionModel) return;
        var failures = new List<Exception>();
        FileStream? pending = null;
        Microsoft.Win32.SafeHandles.SafeFileHandle? duplicate = null;
        try
        {
            if (!OperatingSystem.IsLinux()
                || System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture
                    != System.Runtime.InteropServices.Architecture.X64
                || !TryCaptureConfiguredContainerModel(builder, out byte[] bytes)
                || !Path.IsPathFullyQualified(Root) || Path.GetFullPath(Root) != Root
                || Path.GetFileName(Root) != RunId)
                throw new InvalidOperationException("test_resource_final_model_invalid");
            EnsureNoLinks(Root);
            string marker = Path.Combine(Root, MarkerName);
            string currentUser = (await ModelPathMetadataAsync("/proc/" + Environment.ProcessId, true, cancellationToken))[2];
            string[] rootIdentity = await ModelPathMetadataAsync(Root, false, cancellationToken);
            string[] markerIdentity = await ModelPathMetadataAsync(marker, false, cancellationToken);
            if (rootIdentity[0] != "directory" || rootIdentity[1] != "700" || rootIdentity[2] != currentUser
                || markerIdentity[0] != "regular file" || markerIdentity[1] != "600"
                || markerIdentity[2] != currentUser || markerIdentity[3] != "1" || markerIdentity[6] != "32")
                throw new InvalidDataException();
            using (var input = new FileStream(marker, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                byte[] value = new byte[32];
                await input.ReadExactlyAsync(value, cancellationToken);
                if (input.ReadByte() != -1 || System.Text.Encoding.UTF8.GetString(value) != RunId)
                    throw new InvalidDataException();
            }
            string temporary = Path.Combine(Root, ".container-model-" + Guid.NewGuid().ToString("N"));
            string published = Path.Combine(Root, "container-model.json");
            pending = new FileStream(temporary, new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.ReadWrite,
                Share = FileShare.Read,
                UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite
            });
            await pending.WriteAsync(bytes, cancellationToken);
            await pending.FlushAsync(cancellationToken);
            const int duplicateCloseOnExec = 1030;
            int descriptorNumber = DuplicateModelDescriptor(pending.SafeFileHandle, duplicateCloseOnExec, 3);
            if (descriptorNumber < 0) throw new IOException("test_resource_descriptor_unavailable");
            duplicate = new Microsoft.Win32.SafeHandles.SafeFileHandle(new IntPtr(descriptorNumber), ownsHandle: true);
            string descriptor = $"/proc/{Environment.ProcessId}/fd/{descriptorNumber}";
            string[] written = await ModelPathMetadataAsync(descriptor, true, cancellationToken);
            pending.Position = 0;
            byte[] readback = new byte[bytes.Length];
            await pending.ReadExactlyAsync(readback, cancellationToken);
            if (pending.ReadByte() != -1 || !readback.AsSpan().SequenceEqual(bytes)) throw new InvalidDataException();
            EnsureNoLinks(Root);
            if (!(await ModelPathMetadataAsync(Root, false, cancellationToken)).Take(6).SequenceEqual(rootIdentity.Take(6))
                || !(await ModelPathMetadataAsync(marker, false, cancellationToken)).SequenceEqual(markerIdentity)
                || !TryCaptureConfiguredContainerModel(builder, out byte[] current) || !current.AsSpan().SequenceEqual(bytes))
                throw new InvalidDataException();
            File.Move(temporary, published, overwrite: false);
            string[] after = await ModelPathMetadataAsync(published, false, cancellationToken);
            if (!after.SequenceEqual(written) || after[0] != "regular file" || after[1] != "600"
                || after[2] != currentUser || after[3] != "1"
                || after[6] != bytes.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)
                || !(await ModelPathMetadataAsync(descriptor, true, cancellationToken)).SequenceEqual(after))
                throw new InvalidDataException();
            EnsureNoLinks(Root);
            if (!(await ModelPathMetadataAsync(Root, false, cancellationToken)).Take(6).SequenceEqual(rootIdentity.Take(6))
                || !(await ModelPathMetadataAsync(marker, false, cancellationToken)).SequenceEqual(markerIdentity)
                || !TryCaptureConfiguredContainerModel(builder, out current) || !current.AsSpan().SequenceEqual(bytes))
                throw new InvalidDataException();
        }
        catch (Exception failure) { failures.Add(failure); }
        finally
        {
            try { duplicate?.Dispose(); }
            catch (Exception failure) { failures.Add(failure); }
            try { if (pending is not null) await pending.DisposeAsync(); }
            catch (Exception failure) { failures.Add(failure); }
        }
        if (failures.Count != 0) throw new PrivateModelPublicationException(failures);
    }

    [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "fcntl", SetLastError = true)]
    [System.Runtime.InteropServices.DefaultDllImportSearchPaths(System.Runtime.InteropServices.DllImportSearchPath.SafeDirectories)]
    private static extern int DuplicateModelDescriptor(Microsoft.Win32.SafeHandles.SafeFileHandle source,
        int command, int minimum);

    private static bool ModelText(string? value, int maximum, ref int budget)
    {
        if (string.IsNullOrEmpty(value) || value.Length > maximum || value.Any(char.IsControl)) return false;
        _ = new System.Text.UTF8Encoding(false, true).GetByteCount(value);
        budget = checked(budget + value.Length * 6 + 128);
        return budget <= 262144;
    }

    private static bool ModelImageReference(string value)
    {
        if (value.Any(character => !char.IsAsciiLetterOrDigit(character)
            && character is not ('/' or '.' or '_' or ':' or '@' or '-'))
            || value.Split('/').Any(segment => segment is "" or "." or "..")) return false;
        int digest = value.IndexOf('@', StringComparison.Ordinal);
        return digest < 0 || digest > 0 && value[(digest + 1)..].StartsWith("sha256:", StringComparison.Ordinal)
            && value.Length - digest == 72 && value[(digest + 8)..].All(character =>
                char.IsAsciiDigit(character) || character is >= 'a' and <= 'f');
    }

    private sealed class PrivateModelPublicationException : InvalidOperationException
    {
        private Exception[] RetainedFailures { get; }

        public PrivateModelPublicationException(IEnumerable<Exception> failures)
            : base("test_resource_final_model_invalid")
        {
            RetainedFailures = failures.ToArray();
            if (RetainedFailures.Length == 0) throw new ArgumentException("model_failure_missing");
        }
    }

    private static async Task<string[]> ModelPathMetadataAsync(string path, bool follow,
        CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        using var process = new System.Diagnostics.Process();
        process.StartInfo = new System.Diagnostics.ProcessStartInfo("/usr/bin/stat")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        process.StartInfo.Environment["LC_ALL"] = "C";
        if (follow) process.StartInfo.ArgumentList.Add("--dereference");
        foreach (string argument in new[] { "--format=%F|%a|%u|%h|%d|%i|%s", "--", path })
            process.StartInfo.ArgumentList.Add(argument);
        if (!process.Start()) throw new InvalidDataException();
        Task<string> output = ReadModelMetadataAsync(process.StandardOutput, deadline.Token);
        Task<string> error = ReadModelMetadataAsync(process.StandardError, deadline.Token);
        var failures = new List<Exception>();
        string? value = null;
        try
        {
            await process.WaitForExitAsync(deadline.Token);
            value = await output.WaitAsync(deadline.Token);
            string problem = await error.WaitAsync(deadline.Token);
            if (process.ExitCode != 0 || problem.Length != 0) throw new InvalidDataException();
        }
        catch (Exception failure) { failures.Add(failure); }
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (Exception failure) { failures.Add(failure); }
        try { await process.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(1), CancellationToken.None); }
        catch (Exception failure) { failures.Add(failure); }
        try { await Task.WhenAll(output, error).WaitAsync(TimeSpan.FromSeconds(1), CancellationToken.None); }
        catch (Exception failure) { failures.Add(failure); }
        bool outputCompleted = output.IsCompleted;
        bool errorCompleted = error.IsCompleted;
        if (outputCompleted)
        {
            try { await output; }
            catch (Exception failure)
            {
                if (!failures.Any(existing => ReferenceEquals(existing, failure))) failures.Add(failure);
            }
        }
        if (errorCompleted)
        {
            try { await error; }
            catch (Exception failure)
            {
                if (!failures.Any(existing => ReferenceEquals(existing, failure))) failures.Add(failure);
            }
        }
        if (failures.Count != 0) throw new AggregateException(failures);
        string[] fields = value!.TrimEnd('\n').Split('|');
        if (fields.Length != 7 || fields.Skip(1).Any(field => field.Length is < 1 or > 20
            || !field.All(char.IsAsciiDigit))) throw new InvalidDataException();
        return fields;
    }

    private static async Task<string> ReadModelMetadataAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        char[] buffer = new char[513];
        int length = 0;
        while (length < buffer.Length)
        {
            int read = await reader.ReadAsync(buffer.AsMemory(length), cancellationToken);
            if (read == 0) return new string(buffer, 0, length);
            length += read;
        }
        throw new InvalidDataException();
    }

    public string VolumePrefix => $"islamu-test-{RunId}-";

    public FileStream Claim() => new(Path.Combine(Root, ".apphost-claim"), FileMode.CreateNew,
        FileAccess.Write, FileShare.None);

    public void Apply(IDistributedApplicationBuilder builder)
    {
        if (Profile == OwnedAppHostProfile.ClosedCompositionModel)
            return;
        foreach (var resource in builder.Resources)
        {
            foreach (var endpoint in resource.Annotations.OfType<EndpointAnnotation>())
                endpoint.Port = null;
            if (resource is ContainerResource container)
            {
                foreach (var policy in container.Annotations.OfType<ContainerImagePullPolicyAnnotation>().ToArray())
                    container.Annotations.Remove(policy);
                builder.CreateResourceBuilder(container).WithImagePullPolicy(ImagePullPolicy.Never);
                builder.CreateResourceBuilder(container).WithContainerRuntimeArgs("--pids-limit", "512",
                    "--label", $"islamu.assurance.owner={RunId}",
                    "--label", $"islamu.assurance.resource={container.Name}");
                foreach (var mount in container.Annotations.OfType<ContainerMountAnnotation>().ToArray())
                {
                    if (mount.Type == ContainerMountType.Volume)
                    {
                        container.Annotations.Remove(mount);
                        builder.CreateResourceBuilder(container).WithVolume(
                            VolumePrefix + mount.Source, mount.Target, mount.IsReadOnly);
                    }
                    else if (!mount.IsReadOnly)
                        throw new InvalidOperationException("test_resource_foreign_writable_mount");
                }
            }
            if (resource is ProjectResource project)
            {
                var projected = builder.CreateResourceBuilder(project)
                    .WithEnvironment("ISLAMU_TEST_RUN_ID", RunId)
                    .WithEnvironment("ISLAMU_TEST_ROOT", Root)
                    .WithEnvironment("DOTNET_ENVIRONMENT", "Assurance")
                    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Assurance");
                if (project.Name != "event-migrationservice")
                {
                    projected.WithHttpEndpoint(name: "operations")
                        .WithEnvironment("OperationalEndpoints__Address", "127.0.0.1")
                        .WithEnvironment("OperationalEndpoints__Port",
                            projected.GetEndpoint("operations").Property(EndpointProperty.TargetPort))
                        .WithHttpHealthCheck("/health", endpointName: "operations");
                }
            }
        }
    }

    public async ValueTask DenyInspectionRunAsync(IAsyncDisposable application)
    {
        if (Profile != OwnedAppHostProfile.ClosedCompositionModel)
            return;
        var failure = new InvalidOperationException("test_resource_inspection_cannot_run");
        try { await application.DisposeAsync(); }
        catch (Exception cleanupFailure) { throw new AggregateException(failure, cleanupFailure); }
        throw failure;
    }

    private static void EnsureNoLinks(string path)
    {
        for (DirectoryInfo? directory = new(path); directory is not null; directory = directory.Parent)
            if (directory.LinkTarget is not null)
                throw new InvalidOperationException("test_resource_symbolic_path");
    }
}
