using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Requests.Commands;
using Explore.Infrastructure.Services.Moderation;
using ISLAMU.Moderation.Measurement;

// There is intentionally no sign/import/replay command and no report, executable,
// filter, provider, result or "passed" argument. These are an owned execution's facts.
if (args.Length != 8 || args[0] != "--tenant" || args[2] != "--challenge"
    || args[4] != "--issuer-enrollment" || args[6] != "--output"
    || !Identifier(args[1], out var tenant) || !Identifier(args[3], out var challenge)
    || !Identifier(args[5], out var issuer))
{
    Console.WriteLine("moderation-measurement: usage --tenant <uuid7> --challenge <uuid7> --issuer-enrollment <uuid7> --output <existing-absolute-directory>");
    return 64;
}

using var cancellation = new CancellationTokenSource();
ConsoleCancelEventHandler cancelled = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
Console.CancelKeyPress += cancelled;
using var termination = OperatingSystem.IsLinux() ? PosixSignalRegistration.Create(PosixSignal.SIGTERM,
    context => { context.Cancel = true; cancellation.Cancel(); }) : null;
try
{
    var token = cancellation.Token;
    Require.That(OperatingSystem.IsLinux(), "host-platform-unsupported");
    // Locate the repository from the installed engineering binary, never the caller's CWD.
    var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
    Require.That(File.Exists(Path.Combine(root, "eng/moderation/MeasurementLauncher.csproj"))
        && Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar)
            == Path.Combine(root, "eng/moderation/bin/Release/net10.0"), "launcher-location-invalid");
    await using var configuration = await MeasurementConfiguration.LoadAsync(token);
    var before = await NativeAuthority.ReadAsync(configuration.Primary, tenant, challenge, issuer, token);
    var (catalogue, catalogueDigest) = await PinnedCatalogue.LoadAsync(root, before.Issuer.CatalogueDigest, token);
    Require.That(catalogue.ConsumerId == before.Challenge.ConsumerId, "catalogue-consumer-mismatch");
    var artifacts = await Artifacts.MeasureAsync(root, before.Challenge.ConsumerId, before.Binding.Mode, token);
    Require.That(artifacts.RunnerDigest == before.Issuer.RunnerDigest
        && artifacts.TestBundleDigest == before.Issuer.TestBundleDigest
        && artifacts.ProductArtifactDigest == catalogue.ProductArtifactDigest
        && artifacts.RuntimeGeneration == before.Challenge.RuntimeGeneration
        && artifacts.InventoryFingerprint == before.Challenge.InventoryFingerprint, "artifact-approval-mismatch");
    var providerVersion = await NativeAuthority.ProviderVersionAsync(configuration.Test, tenant, token);
    Require.That(catalogue.ConfigurationDigest == configuration.PublicDigest
        && catalogue.ProviderCode == configuration.Test.Provider.ToString()
        && catalogue.ProviderVersion == providerVersion, "configuration-approval-mismatch");
    var runId = Guid.CreateVersion7();
    var directory = OwnedExecution.CreateDirectory(args[7], runId);
    var evidence = ImmutableArray.CreateBuilder<BundleEvidence>();
    var began = DateTimeOffset.UtcNow;
    Require.That(began >= before.Challenge.PreparedAt && began >= before.Issuer.ApprovedAt
        && began < before.ValidUntil, "source-expired");
    foreach (var job in catalogue.Jobs)
    {
        // One launch only. A failed case, report, process or attempt ends this run.
        var child = await OwnedExecution.RunAsync(root, directory, runId, job, configuration, before.ValidUntil, token);
        evidence.Add(await EvidenceReconciliation.ReadAsync(directory, runId, job, child,
            configuration.Test.Provider.ToString(), providerVersion, configuration.TestPublicDigest, token));
    }
    var completed = DateTimeOffset.UtcNow;
    Require.That(completed >= began, "clock-regressed");
    var afterArtifacts = await Artifacts.MeasureAsync(root, before.Challenge.ConsumerId, before.Binding.Mode, token);
    Require.That(afterArtifacts == artifacts, "artifacts-changed");
    _ = await PinnedCatalogue.LoadAsync(root, catalogueDigest, token);
    await using var currentConfiguration = await MeasurementConfiguration.LoadAsync(token);
    Require.That(currentConfiguration.PublicDigest == configuration.PublicDigest
        && await NativeAuthority.ProviderVersionAsync(currentConfiguration.Test, tenant, token) == providerVersion,
        "configuration-changed");
    var after = await NativeAuthority.ReadAsync(currentConfiguration.Primary, tenant, challenge, issuer, token);
    Require.That(after.Fingerprint == before.Fingerprint, "native-source-changed");
    var caseIds = evidence.SelectMany(item => item.Cases).Select(item => item.CaseId)
        .Order(StringComparer.Ordinal).ToImmutableArray();
    Require.That(caseIds.Length == catalogue.Jobs.Sum(job => job.Cases.Length)
        && caseIds.Distinct(StringComparer.Ordinal).Count() == caseIds.Length, "case-set-mismatch");

    // This retained, domain-separated manifest binds exact stdout/stderr/exit, all three
    // native evidence formats, native run identities, source provenance and approved bytes.
    var manifest = JsonSerializer.SerializeToUtf8Bytes(new
    {
        format = "moderation-owned-execution-v1", runId, startedAt = began, completedAt = completed,
        sourceFingerprint = after.Fingerprint, artifacts, catalogueDigest,
        configurationDigest = configuration.PublicDigest,
        providerCode = catalogue.ProviderCode, providerVersion,
        executedCount = caseIds.Length, attemptCount = 1, bundleCount = evidence.Count,
        evidence = evidence.ToImmutable()
    });
    await OwnedExecution.WritePrivateAsync(Path.Combine(directory, "evidence.json"), manifest, token);
    // Signing starts only after children have exited and their pipes have drained.
    // No standalone method accepts external reports, booleans or a preconstructed receipt.
    using var signer = ECDsa.Create();
    signer.ImportFromPem(currentConfiguration.SigningKey());
    var publicKey = signer.ExportSubjectPublicKeyInfo();
    Require.That(signer.KeySize == 256 && signer.ExportParameters(false).Curve.Oid.Value == "1.2.840.10045.3.1.7"
        && Convert.ToBase64String(publicKey) == after.Issuer.PublicKeySpkiBase64, "signer-enrollment-mismatch");
    token.ThrowIfCancellationRequested();
    var issued = DateTimeOffset.UtcNow;
    var until = new[] { after.ValidUntil, began.AddMinutes(30) }.Min();
    Require.That(issued >= completed && issued < until, "source-expired");
    var receipt = new ModerationMeasurementReceipt
    {
        Issuer = after.Issuer.Issuer, ReceiptId = Guid.CreateVersion7(), ChallengeId = challenge,
        ChallengeNonce = after.Challenge.Nonce, RunId = runId,
        HostBindingId = after.Challenge.HostBindingId, TrustDomainId = after.Challenge.TrustDomainId,
        TenantId = tenant, Binding = after.Binding, ConsumerId = after.Challenge.ConsumerId,
        ConsumerContractDigest = after.Challenge.ConsumerContractDigest.ToLowerInvariant(),
        InstallationId = after.Challenge.InstallationId, InstallationRevision = after.Challenge.InstallationRevision,
        InstallationWriterGeneration = after.Challenge.InstallationWriterGeneration,
        RuntimeGeneration = after.Challenge.RuntimeGeneration,
        InventoryFingerprint = after.Challenge.InventoryFingerprint.ToLowerInvariant(),
        RunnerDigest = artifacts.RunnerDigest, CatalogueDigest = catalogueDigest,
        TestBundleDigest = artifacts.TestBundleDigest, ProductArtifactDigest = artifacts.ProductArtifactDigest,
        ConfigurationDigest = configuration.PublicDigest, ProviderCode = catalogue.ProviderCode,
        PrimaryConfigurationDigest = configuration.PrimaryPublicDigest,
        TestConfigurationDigest = configuration.TestPublicDigest,
        ProviderVersion = providerVersion, CaseIds = caseIds, ExecutedCount = caseIds.Length, AttemptCount = 1,
        ResultArtifactDigest = Artifacts.Hash(manifest), StartedAt = began, CompletedAt = completed,
        IssuedAt = issued, ValidUntil = until
    };
    var header = Base64(JsonSerializer.SerializeToUtf8Bytes(new
        { alg = "ES256", typ = ModerationMeasurementReceiptVerifier.RequiredType, kid = after.Issuer.KeyId.ToString("D") }));
    var unsigned = header + "." + Base64(JsonSerializer.SerializeToUtf8Bytes(receipt));
    var signed = unsigned + "." + Base64(signer.SignData(Encoding.ASCII.GetBytes(unsigned), HashAlgorithmName.SHA256,
        DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
    var verified = new ModerationMeasurementReceiptVerifier().Verify(signed, new ModerationMeasurementTrustKey
    {
        Issuer = after.Issuer.Issuer, KeyId = after.Issuer.KeyId, SubjectPublicKeyInfo = publicKey.ToImmutableArray(),
        HostBindingId = after.Issuer.HostBindingId, TrustDomainId = after.Issuer.TrustDomainId, TenantId = tenant,
        ApprovedRunnerDigest = after.Issuer.RunnerDigest, ApprovedCatalogueDigest = catalogueDigest,
        ApprovedTestBundleDigest = after.Issuer.TestBundleDigest, ValidUntil = after.Issuer.ValidUntil
    }, after.Binding, DateTimeOffset.UtcNow);
    Require.That(verified.Outcome == ModerationMeasurementVerificationOutcome.Known, "signed-contract-rejected");
    var request = new AcceptModerationConsumerMeasurementCommand
    {
        OperationId = Guid.CreateVersion7(), Target = after.Tenant, ExpectedQualificationRevision = after.QualificationRevision,
        ReceiptId = receipt.ReceiptId, ChallengeId = challenge, IssuerEnrollmentId = issuer,
        Binding = after.Binding, SignedToken = signed
    };
    // Only a fully written request is published. Native authenticated acceptance remains
    // mandatory; an assertion and a successful launcher exit confer no qualification.
    var pending = Path.Combine(directory, "acceptance.json.partial");
    await OwnedExecution.WritePrivateAsync(pending, JsonSerializer.SerializeToUtf8Bytes(request), token);
    token.ThrowIfCancellationRequested();
    Require.That(DateTimeOffset.UtcNow < until, "source-expired");
    File.Move(pending, Path.Combine(directory, "acceptance.json"), overwrite: false);
    Console.WriteLine($"moderation-measurement: assertion-issued run={runId:D} acceptance-required");
    return 0;
}
catch (MeasurementFailure error)
{
    Console.WriteLine("moderation-measurement: " + error.Category);
    return error.Category == "cancelled" ? 130 : 65;
}
catch (OperationCanceledException)
{
    Console.WriteLine("moderation-measurement: cancelled");
    return 130;
}
catch (JsonException)
{
    Console.WriteLine("moderation-measurement: evidence-contract-invalid");
    return 65;
}
catch (Exception)
{
    // Provider/cryptography/framework exceptions can contain credentials, output or paths.
    Console.WriteLine("moderation-measurement: unavailable");
    return 70;
}
finally
{
    Console.CancelKeyPress -= cancelled;
}

static bool Identifier(string text, out Guid value) => Guid.TryParseExact(text, "D", out value)
    && value.ToString("D") == text && value.Version == 7 && value.Variant is >= 8 and <= 11;

static string Base64(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
