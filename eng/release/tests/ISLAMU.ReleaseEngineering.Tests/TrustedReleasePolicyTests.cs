using System.Security.Cryptography;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using ISLAMU.ReleaseEngineering;
using YamlDotNet.Serialization;

namespace ISLAMU.ReleaseEngineering.Tests;

[NotInParallel("RuntimePromotionTrustRoot")]
public sealed class TrustedReleasePolicyTests
{
    [Test]
    public async Task PublicVerificationContractDoesNotExposePromotionTrustRootOrReplayState()
    {
        string[] publicInputs = typeof(PromotionAuthorityInput).GetProperties()
            .Select(property => property.Name)
            .ToArray();
        string[] trustRootParameters = typeof(TrustedBundlePolicy).Assembly.GetTypes()
            .SelectMany(type => type.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance)
                .Cast<System.Reflection.MethodBase>()
                .Concat(type.GetConstructors(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)))
            .Where(member => member.IsPublic && member.DeclaringType?.IsVisible == true)
            .SelectMany(member => member.GetParameters().Select(parameter => $"{member.DeclaringType?.Name}.{member.Name}:{parameter.Name}"))
            .Where(parameter => parameter.Contains("trustedPromotionRoot", StringComparison.OrdinalIgnoreCase) ||
                parameter.Contains("allowedSigners", StringComparison.OrdinalIgnoreCase) ||
                parameter.Contains("trustRoot", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        await Assert.That(publicInputs).DoesNotContain("AllowedSignersPath");
        await Assert.That(publicInputs).DoesNotContain("ConsumedReceiptIds");
        await Assert.That(trustRootParameters).IsEmpty();
    }

    [Test]
    public async Task TrustedBundleBoundaryConstantsAreExplicitAndPromotionReceiptIsRequired()
    {
        using var bundle = TrustedBundleFixture.Create();

        await Assert.That(TrustedBundlePolicy.MaximumManifestBytes).IsGreaterThan(0);
        await Assert.That(TrustedBundlePolicy.MaximumBundleFiles).IsGreaterThan(0);
        await Assert.That(TrustedBundlePolicy.MaximumFileBytes).IsGreaterThan(0);
        await Assert.That(TrustedBundlePolicy.MaximumTotalBytes).IsGreaterThan(TrustedBundlePolicy.MaximumFileBytes);
        TrustedBundleVerificationRequest request = bundle.Request();
        await Assert.That(bundle.Verify(request with { PromotionAuthority = request.PromotionAuthority with { ReceiptPath = string.Empty } }).Diagnostic).IsEqualTo("trusted_bundle_promotion_receipt_missing");
    }

    [Test]
    public async Task PreviouslyPromotedBundleWithExactCanonicalManifestIsAccepted()
    {
        using var bundle = TrustedBundleFixture.Create();

        TrustedBundleResult result = bundle.Verify();
        TrustedBundleResult exactReceiptReuse = bundle.Verify();

        await Assert.That(result.IsValid).IsTrue();
        await Assert.That(result.Diagnostic).IsNull();
        await Assert.That(result.ManifestDigest).IsEqualTo(bundle.ManifestDigest);
        await Assert.That(exactReceiptReuse.IsValid).IsTrue();
        await Assert.That(exactReceiptReuse.ManifestDigest).IsEqualTo(result.ManifestDigest);
    }

    [Test]
    public async Task SignedPromotionReceiptIsBoundToExactBundleAndCannotPromoteDifferentBundle()
    {
        using var bundle = TrustedBundleFixture.Create();
        using var different = TrustedBundleFixture.Create();
        File.WriteAllText(Path.Combine(different.Root, "config", "cliff.toml"), "# different packaged config\n");
        different.RewriteManifestFromFiles(resignReceipt: true);
        TrustedBundleVerificationRequest request = different.Request() with { PromotionAuthority = bundle.PromotionAuthority };

        TrustedBundleResult sameBundle = bundle.Verify();
        TrustedBundleResult reusedForDifferentBundle = different.Verify(request, bundle.TrustRootPath);

        await Assert.That(sameBundle.IsValid).IsTrue();
        await Assert.That(reusedForDifferentBundle.Diagnostic).IsEqualTo("trusted_bundle_promotion_mismatch");
    }

    [Test]
    public async Task CandidateOrSelfPromotedBundleIsRejectedBeforePayloadAuthority()
    {
        using var bundle = TrustedBundleFixture.Create();
        TrustedBundleVerificationRequest request = bundle.Request();

        TrustedBundleResult missing = bundle.Verify(request with { PromotionAuthority = request.PromotionAuthority with { ReceiptPath = string.Empty } });
        TrustedBundleResult sameRoot = bundle.Verify(request with { CandidateCheckoutRoot = bundle.Root });
        TrustedBundleResult selfCreated = bundle.Verify(request with { PromotionAuthority = request.PromotionAuthority with { ReceiptPath = Path.Combine(bundle.CandidateRoot, "self-created-receipt.json") } });
        TrustedBundleResult wrongSigner = bundle.Verify(request with { PromotionAuthority = request.PromotionAuthority with { Principal = "candidate-promoter" } });
        File.Copy(bundle.TrustRootPath, Path.Combine(bundle.CandidateRoot, "allowed-signers"));
        TrustedBundleResult candidateSiblingRoot = bundle.VerifyWithPackagedDefault(request);

        await Assert.That(missing.Diagnostic).IsEqualTo("trusted_bundle_promotion_receipt_missing");
        await Assert.That(sameRoot.Diagnostic).IsEqualTo("trusted_bundle_candidate_overlap");
        await Assert.That(selfCreated.Diagnostic).IsEqualTo("trusted_bundle_promotion_authority_overlap");
        await Assert.That(wrongSigner.Diagnostic).IsEqualTo("trusted_bundle_promotion_signature_invalid");
        await Assert.That(candidateSiblingRoot.Diagnostic).IsEqualTo("trusted_bundle_promotion_authority_not_configured");
    }

    [Test]
    public async Task PackagedDefaultAuthorityIgnoresStaleRuntimeSignerBytes()
    {
        using var bundle = TrustedBundleFixture.Create();

        TrustedBundleResult result = bundle.VerifyWithPackagedDefault(bundle.Request());

        await Assert.That(result.Diagnostic).IsEqualTo("trusted_bundle_promotion_authority_not_configured");
    }

    [Test]
    public async Task FixedRuntimeAuthorityPathReturnsNotConfiguredForCommentOnlyRootAndSignatureInvalidForWrongSigner()
    {
        using var bundle = TrustedBundleFixture.Create();
        using var wrongAuthority = TrustedBundleFixture.Create();

        TrustedBundleResult commentOnly = bundle.VerifyWithPackagedDefault(bundle.Request());
        TrustedBundleResult wrongSigner = bundle.Verify(trustedPromotionRootPath: wrongAuthority.TrustRootPath);

        await Assert.That(commentOnly.Diagnostic).IsEqualTo("trusted_bundle_promotion_authority_not_configured");
        await Assert.That(wrongSigner.Diagnostic).IsEqualTo("trusted_bundle_promotion_signature_invalid");
    }

    [Test]
    public async Task RuntimeSignerScopesSerializeParallelFixtureAuthoritiesAndRestoreTheObserver()
    {
        using var first = TrustedBundleFixture.Create();
        using var second = TrustedBundleFixture.Create();

        TrustedBundleResult[] results = await Task.WhenAll(Task.Run(() => first.Verify()), Task.Run(() => second.Verify()));

        await Assert.That(results[0].IsValid).IsTrue();
        await Assert.That(results[1].IsValid).IsTrue();
        await Assert.That(first.VerifyWithPackagedDefault(first.Request()).Diagnostic).IsEqualTo("trusted_bundle_promotion_authority_not_configured");
    }

    [Test]
    public async Task RuntimeSignerScopeRestoresAfterCancellationLikeInterruption()
    {
        using var bundle = TrustedBundleFixture.Create();
        RuntimePromotionTrustRootScope? trustRoot = null;

        try
        {
            using RuntimePromotionTrustRootScope scope = RuntimePromotionTrustRootScope.Use(bundle.TrustRootPath);
            trustRoot = scope;
            throw new OperationCanceledException("fixture_cancellation");
        }
        catch (OperationCanceledException)
        {
        }

        await Assert.That(trustRoot!.RestoredSha256).IsEqualTo(trustRoot.OriginalSha256);
        await Assert.That(bundle.VerifyWithPackagedDefault(bundle.Request()).Diagnostic).IsEqualTo("trusted_bundle_promotion_authority_not_configured");
    }

    [Test]
    public async Task RuntimeSignerScopeFailsClosedForMalformedAndMissingSignerInputsThenRestores()
    {
        using var bundle = TrustedBundleFixture.Create();
        RuntimePromotionTrustRootScope? trustRoot = null;
        TrustedBundleResult malformed;
        TrustedBundleResult missing;

        using (RuntimePromotionTrustRootScope scope = RuntimePromotionTrustRootScope.Use(bundle.TrustRootPath))
        {
            trustRoot = scope;
            File.WriteAllText(scope.RuntimeTrustRootPath, "malformed signer");
            malformed = TrustedBundlePolicy.Verify(bundle.Request());
            File.Delete(scope.RuntimeTrustRootPath);
            missing = TrustedBundlePolicy.Verify(bundle.Request());
        }

        await Assert.That(malformed.Diagnostic).IsEqualTo("trusted_bundle_promotion_signature_invalid");
        await Assert.That(missing.Diagnostic).IsEqualTo("trusted_bundle_promotion_authority_not_configured");
        await Assert.That(trustRoot!.RestoredSha256).IsEqualTo(trustRoot.OriginalSha256);
        await Assert.That(bundle.VerifyWithPackagedDefault(bundle.Request()).Diagnostic).IsEqualTo("trusted_bundle_promotion_authority_not_configured");
    }

    [Test]
    public async Task PromotionReceiptRejectsTamperingWrongRootAndBundleMismatch()
    {
        using var tampered = TrustedBundleFixture.Create();
        File.AppendAllText(tampered.ReceiptPath, "tampered");
        await Assert.That(tampered.Verify().Diagnostic).IsEqualTo("trusted_bundle_promotion_signature_invalid");

        using var wrongRoot = TrustedBundleFixture.Create();
        using var otherAuthority = TrustedBundleFixture.Create();
        await Assert.That(wrongRoot.Verify(trustedPromotionRootPath: otherAuthority.TrustRootPath).Diagnostic).IsEqualTo("trusted_bundle_promotion_signature_invalid");

        using var mismatch = TrustedBundleFixture.Create();
        mismatch.RewriteManifestWithAdditionalFile("policy/new.yaml", SHA256.HashData("new"u8.ToArray()), createFile: true, resignReceipt: false);
        await Assert.That(mismatch.Verify().Diagnostic).IsEqualTo("trusted_bundle_promotion_mismatch");
    }

    [Test]
    public async Task BundleRejectsDriftMissingExtraTraversalSymlinkAndHashMismatch()
    {
        using var drift = TrustedBundleFixture.Create();
        File.WriteAllText(Path.Combine(drift.Root, "policy", "release-policy.yaml"), "drift");
        await Assert.That(drift.Verify().Diagnostic).IsEqualTo("trusted_bundle_hash_mismatch");

        using var missing = TrustedBundleFixture.Create();
        File.Delete(Path.Combine(missing.Root, "policy", "release-policy.yaml"));
        await Assert.That(missing.Verify().Diagnostic).IsEqualTo("trusted_bundle_file_set_mismatch");

        using var extra = TrustedBundleFixture.Create();
        File.WriteAllText(Path.Combine(extra.Root, "extra.txt"), "extra");
        await Assert.That(extra.Verify().Diagnostic).IsEqualTo("trusted_bundle_file_set_mismatch");

        using var missingRequired = TrustedBundleFixture.Create();
        missingRequired.RewriteManifestWithoutFile("toolchain.lock.json");
        await Assert.That(missingRequired.Verify().Diagnostic).IsEqualTo("trusted_bundle_required_file_missing");

        using var traversal = TrustedBundleFixture.Create("../escape.txt");
        await Assert.That(traversal.Verify().Diagnostic).IsEqualTo("trusted_bundle_unsafe_path");

        using var symlink = TrustedBundleFixture.Create();
        string link = Path.Combine(symlink.Root, "policy", "linked.yaml");
        File.CreateSymbolicLink(link, Path.Combine(symlink.Root, "policy", "release-policy.yaml"));
        symlink.RewriteManifestWithAdditionalFile("policy/linked.yaml", SHA256.HashData("release-policy"u8.ToArray()), createFile: false, resignReceipt: true);
        await Assert.That(symlink.Verify().Diagnostic).IsEqualTo("trusted_bundle_symlink_ambiguous");
    }

    [Test]
    public async Task BundleRejectsCandidateSymlinkAliasToBundleRootAndHardlinkedRequiredFiles()
    {
        using var symlinkAlias = TrustedBundleFixture.Create();
        string alias = symlinkAlias.ReplaceCandidateWithSymlinkToBundleRoot();
        await Assert.That(symlinkAlias.Verify(symlinkAlias.Request() with { CandidateCheckoutRoot = alias }).Diagnostic).IsEqualTo("trusted_bundle_candidate_overlap");

        using var hardlink = TrustedBundleFixture.Create();
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        hardlink.HardlinkRequiredFileOutsideBundle("toolchain.lock.json");
        await Assert.That(hardlink.Verify().Diagnostic).IsEqualTo("trusted_bundle_link_count_unsafe");
    }

    [Test]
    public async Task BundleRejectsNoncanonicalManifestAndVersionPolicyConfigOrTrustMismatch()
    {
        using var bundle = TrustedBundleFixture.Create();

        await Assert.That(bundle.Verify(bundle.Request() with { ExpectedBundleVersion = "2.0.0" }).Diagnostic).IsEqualTo("trusted_bundle_version_mismatch");
        await Assert.That(bundle.Verify(bundle.Request() with { ExpectedPolicyVersion = "policy-v2" }).Diagnostic).IsEqualTo("trusted_bundle_policy_mismatch");
        await Assert.That(bundle.Verify(bundle.Request() with { ExpectedConfigVersion = "config-v2" }).Diagnostic).IsEqualTo("trusted_bundle_config_mismatch");
        await Assert.That(bundle.Verify(bundle.Request() with { ExpectedTrustVersion = "trust-v2" }).Diagnostic).IsEqualTo("trusted_bundle_trust_mismatch");

        bundle.RewriteManifestAsNoncanonicalJson();
        bundle.ResignReceipt();
        await Assert.That(bundle.Verify().Diagnostic).IsEqualTo("trusted_bundle_manifest_not_canonical");
    }

    [Test]
    public async Task BundleRejectsBoundaryPlusOneForEveryResourceCeiling()
    {
        using var manifest = TrustedBundleFixture.Create();
        manifest.SetManifestLength(TrustedBundlePolicy.MaximumManifestBytes + 1L);
        await Assert.That(manifest.Verify().Diagnostic).IsEqualTo("trusted_bundle_manifest_too_large");

        using var files = TrustedBundleFixture.Create();
        files.ReplaceFiles(TrustedBundlePolicy.MaximumBundleFiles + 1, 0);
        await Assert.That(files.Verify().Diagnostic).IsEqualTo("trusted_bundle_file_limit_exceeded");

        using var oneFile = TrustedBundleFixture.Create();
        oneFile.ReplaceFiles(1, TrustedBundlePolicy.MaximumFileBytes + 1);
        await Assert.That(oneFile.Verify().Diagnostic).IsEqualTo("trusted_bundle_file_too_large");

        using var total = TrustedBundleFixture.Create();
        total.ReplaceFiles(5, TrustedBundlePolicy.MaximumFileBytes);
        await Assert.That(total.Verify().Diagnostic).IsEqualTo("trusted_bundle_total_size_exceeded");

        using var longPath = TrustedBundleFixture.Create(new string('a', TrustedBundlePolicy.MaximumPathUtf8Bytes + 1));
        await Assert.That(longPath.Verify().Diagnostic).IsEqualTo("trusted_bundle_unsafe_path");

        using var deepPath = TrustedBundleFixture.Create(string.Join('/', Enumerable.Repeat("d", TrustedBundlePolicy.MaximumPathDepth + 1)));
        await Assert.That(deepPath.Verify().Diagnostic).IsEqualTo("trusted_bundle_unsafe_path");

        using var entries = TrustedBundleFixture.Create();
        entries.CreateExtraDirectories(TrustedBundlePolicy.MaximumEnumeratedEntries + 1);
        await Assert.That(entries.Verify().Diagnostic).IsEqualTo("trusted_bundle_entry_limit_exceeded");

        using var receipt = TrustedBundleFixture.Create();
        receipt.SetReceiptLength(TrustedBundlePolicy.MaximumPromotionReceiptBytes + 1L);
        await Assert.That(receipt.Verify().Diagnostic).IsEqualTo("trusted_bundle_promotion_receipt_too_large");

        using var signature = TrustedBundleFixture.Create();
        signature.SetSignatureLength(TrustedBundlePolicy.MaximumPromotionSignatureBytes + 1L);
        await Assert.That(signature.Verify().Diagnostic).IsEqualTo("trusted_bundle_promotion_signature_too_large");

        using var trustRoot = TrustedBundleFixture.Create();
        trustRoot.SetTrustRootLength(TrustedBundlePolicy.MaximumPromotionTrustRootBytes + 1L);
        await Assert.That(trustRoot.Verify().Diagnostic).IsEqualTo("trusted_bundle_promotion_trust_root_too_large");
    }

    [Test]
    public async Task BundleRejectsExactNormalizedDuplicatesAndPortableCaseCollisions()
    {
        using var duplicate = TrustedBundleFixture.Create();
        duplicate.SetManifestPaths("payload/file.yaml", "payload/file.yaml");
        await Assert.That(duplicate.Verify().Diagnostic).IsEqualTo("trusted_bundle_duplicate_path");

        using var caseCollision = TrustedBundleFixture.Create();
        caseCollision.SetManifestPaths("policy/A.yaml", "policy/a.yaml");
        await Assert.That(caseCollision.Verify().Diagnostic).IsEqualTo("trusted_bundle_path_case_collision");

        using var normalizationCollision = TrustedBundleFixture.Create();
        normalizationCollision.SetManifestPaths("policy/Café.yaml", "policy/Cafe\u0301.yaml");
        await Assert.That(normalizationCollision.Verify().Diagnostic).IsEqualTo("trusted_bundle_duplicate_path");
    }

    [Test]
    public async Task SignerContractRequiresUniqueAuthorizedCurrentEd25519ReleaseSigner()
    {
        var signer = new TrustedSshSigner("release-operator", "release", "SHA256:synthetic-release-key", "ssh-ed25519", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), null);
        var request = new SshTagAuthorizationRequest(true, true, "release-operator", "release", "SHA256:synthetic-release-key", "ssh-ed25519", new DateOnly(2026, 8, 14), new string('a', 40), new string('a', 40), null);

        await Assert.That(SshSignerPolicy.Authorize([signer], request).IsValid).IsTrue();
        await Assert.That(SshSignerPolicy.Authorize([signer], request with { IsAnnotatedTag = false }).Diagnostic).IsEqualTo("release_tag_not_annotated");
        await Assert.That(SshSignerPolicy.Authorize([signer], request with { CryptographicSignatureVerified = false }).Diagnostic).IsEqualTo("release_tag_signature_invalid");
        await Assert.That(SshSignerPolicy.Authorize([signer], request with { Principal = "candidate" }).Diagnostic).IsEqualTo("release_signer_unauthorized");
        await Assert.That(SshSignerPolicy.Authorize([signer], request with { RequiredRole = "tooling-promotion" }).Diagnostic).IsEqualTo("release_signer_role_forbidden");
        await Assert.That(SshSignerPolicy.Authorize([signer], request with { Algorithm = "ssh-rsa" }).Diagnostic).IsEqualTo("release_signer_algorithm_forbidden");
        await Assert.That(SshSignerPolicy.Authorize([signer], request with { VerificationDate = new DateOnly(2027, 1, 1) }).Diagnostic).IsEqualTo("release_signer_not_current");
        await Assert.That(SshSignerPolicy.Authorize([signer with { RevokedOn = new DateOnly(2026, 8, 1) }], request).Diagnostic).IsEqualTo("release_signer_revoked");
        await Assert.That(SshSignerPolicy.Authorize([signer, signer], request).Diagnostic).IsEqualTo("release_signer_policy_not_unique");
        await Assert.That(SshSignerPolicy.Authorize([signer with { Algorithm = "ssh-rsa" }], request).Diagnostic).IsEqualTo("release_signer_policy_invalid");
    }

    [Test]
    public async Task ReplacedOrRecreatedTagObjectIsRejectedEvenWhenCommitIsUnchanged()
    {
        var signer = new TrustedSshSigner("release-operator", "release", "SHA256:synthetic-release-key", "ssh-ed25519", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), null);
        var request = new SshTagAuthorizationRequest(true, true, signer.Principal, signer.Role, signer.KeyFingerprint, signer.Algorithm, new DateOnly(2026, 8, 14), new string('a', 40), new string('b', 40), new string('c', 40));

        await Assert.That(SshSignerPolicy.Authorize([signer], request).Diagnostic).IsEqualTo("release_tag_object_replaced");
        await Assert.That(SshSignerPolicy.Authorize([signer], request with { ExpectedTagObjectId = request.ObservedTagObjectId }).Diagnostic).IsEqualTo("release_tag_object_recreated");
    }

    [Test]
    public async Task EmbargoInputNeverProjectsRestrictedFieldsUntilApprovedDisclosure()
    {
        var restricted = new RestrictedSecurityInput("private vulnerability details", "secret-token", "maintainer@example.org", "/restricted/provider/path", "provider-run-123", "CVE-2099-0001", "Security fixes are available");

        PublicSecurityDispositionResult withheld = EmbargoPolicy.CreatePublicDisposition(restricted, disclosureAuthorized: false);
        PublicSecurityDispositionResult disclosed = EmbargoPolicy.CreatePublicDisposition(restricted, disclosureAuthorized: true);
        string serialized = JsonSerializer.Serialize(disclosed.Disposition);

        await Assert.That(withheld.Diagnostic).IsEqualTo("embargo_disclosure_not_authorized");
        await Assert.That(disclosed.IsValid).IsTrue();
        await Assert.That(serialized).Contains("CVE-2099-0001");
        await Assert.That(serialized).Contains("Security fixes are available");
        await Assert.That(serialized).DoesNotContain(restricted.RestrictedDetails);
        await Assert.That(serialized).DoesNotContain(restricted.Secret);
        await Assert.That(serialized).DoesNotContain(restricted.Identity);
        await Assert.That(serialized).DoesNotContain(restricted.StoragePath);
        await Assert.That(serialized).DoesNotContain(restricted.ProviderMetadata);
    }

    [Test]
    public async Task ApprovedSecurityPublicFieldsCannotExactlyAliasRestrictedInputs()
    {
        var restricted = new RestrictedSecurityInput("Private   Detail", "CVE-2099-9999", "Maintainer", " /restricted/path ", "Provider Metadata", "CVE-2099-0001", "Security fixes are available");

        string[] aliases =
        [
            restricted.RestrictedDetails,
            restricted.Secret,
            restricted.Identity,
            restricted.StoragePath,
            restricted.ProviderMetadata,
            "private detail",
            "/restricted/path",
        ];

        foreach (string alias in aliases)
        {
            await Assert.That(EmbargoPolicy.CreatePublicDisposition(restricted with { ApprovedPublicDisposition = alias }, disclosureAuthorized: true).Diagnostic).IsEqualTo("embargo_public_disposition_invalid");
        }

        await Assert.That(EmbargoPolicy.CreatePublicDisposition(restricted with { ApprovedPublicReference = restricted.Secret }, disclosureAuthorized: true).Diagnostic).IsEqualTo("embargo_public_disposition_invalid");
        await Assert.That(EmbargoPolicy.CreatePublicDisposition(restricted, disclosureAuthorized: false).Disposition).IsNull();
        await Assert.That(EmbargoPolicy.CreatePublicDisposition(restricted, disclosureAuthorized: true).IsValid).IsTrue();
    }

    [Test]
    public async Task BundledSignerFilesAreInactiveSyntheticUniqueAndEd25519Only()
    {
        string trustRoot = Path.Combine(RepositoryRoot.Find(), "eng", "release", "trust");
        string policyText = File.ReadAllText(Path.Combine(trustRoot, "release-signing-policy.yaml"));
        Dictionary<object, object> policy = new DeserializerBuilder().Build().Deserialize<Dictionary<object, object>>(policyText);
        string[] signerLines = File.ReadAllLines(Path.Combine(trustRoot, "allowed-signers"))
            .Where(line => line.Length != 0 && !line.StartsWith('#'))
            .ToArray();

        await Assert.That(policy["status"].ToString()).IsEqualTo("inactive-fixture-only");
        await Assert.That(policyText).Contains("reviewed production principals and public keys are absent");
        await Assert.That(signerLines).IsEmpty();
    }

    private sealed class TrustedBundleFixture : IDisposable
    {
        private readonly string parent;
        private readonly string authorityRoot;
        private readonly string privateKeyPath;
        private readonly string signaturePath;
        private readonly string allowedSignersPath;
        private string manifestJson;

        private TrustedBundleFixture(string parent, string root, string candidateRoot, string authorityRoot, string manifestJson)
        {
            this.parent = parent;
            Root = root;
            CandidateRoot = candidateRoot;
            this.authorityRoot = authorityRoot;
            privateKeyPath = Path.Combine(authorityRoot, "promotion-key");
            ReceiptPath = Path.Combine(authorityRoot, "promotion-receipt.v1.json");
            signaturePath = ReceiptPath + ".sig";
            allowedSignersPath = Path.Combine(authorityRoot, "allowed-promoters");
            this.manifestJson = manifestJson;
            WriteManifest();
            CreatePromotionAuthority();
        }

        public string Root { get; }
        public string CandidateRoot { get; }
        public string ReceiptPath { get; }
        public string TrustRootPath => allowedSignersPath;
        public string ReceiptId => "promotion-fixture-0001";
        public string ManifestDigest => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(Root, "trusted-bundle.manifest.json")))).ToLowerInvariant();

        public static TrustedBundleFixture Create(string path = "policy/release-policy.yaml")
        {
            string parent = Path.Combine(Path.GetTempPath(), $"islamu-trust-{Guid.NewGuid():N}");
            string root = Path.Combine(parent, "promoted");
            string candidate = Path.Combine(parent, "candidate");
            string authority = Path.Combine(parent, "operator-authority");
            Directory.CreateDirectory(Path.Combine(root, "policy"));
            Directory.CreateDirectory(Path.Combine(root, "bin"));
            Directory.CreateDirectory(Path.Combine(root, "config"));
            Directory.CreateDirectory(Path.Combine(root, "trust"));
            Directory.CreateDirectory(candidate);
            Directory.CreateDirectory(authority);
            WriteBundleFile(root, "bin/ISLAMU.ReleaseEngineering.dll", "release-engine-binary");
            WriteBundleFile(root, "config/cliff.toml", "# packaged renderer config\n");
            WriteBundleFile(root, "policy/context-version.txt", "context-v1\n");
            WriteBundleFile(root, "policy/release-policy.yaml", "release-policy");
            WriteBundleFile(root, "policy/schema-version.txt", "schema-v1\n");
            WriteBundleFile(root, "toolchain.lock.json", "{}\n");
            WriteBundleFile(root, "trust/allowed-signers", "# production signers absent\n");
            WriteBundleFile(root, "trust/release-signing-policy.yaml", "status: inactive-fixture-only\n");

            List<object> files = BuildManifestFiles(root);
            if (path != "policy/release-policy.yaml")
            {
                files = [new { path, sha256 = Convert.ToHexString(SHA256.HashData(Array.Empty<byte>())).ToLowerInvariant() }];
            }

            string policyHash = HashFile(Path.Combine(root, "policy", "release-policy.yaml"));
            string configHash = HashFile(Path.Combine(root, "config", "cliff.toml"));
            string trustHash = HashFile(Path.Combine(root, "trust", "release-signing-policy.yaml"));
            string json = JsonSerializer.Serialize(new
            {
                schemaVersion = "trusted-bundle.v1",
                bundleId = "islamu-release-engineering",
                bundleVersion = "1.0.0",
                policyVersion = "policy-v1",
                configVersion = "config-v1",
                trustVersion = "trust-v1",
                policyDigest = policyHash,
                configDigest = configHash,
                trustDigest = trustHash,
                files,
            });
            return new TrustedBundleFixture(parent, root, candidate, authority, json);
        }

        public TrustedBundleVerificationRequest Request() => new(
            Root,
            CandidateRoot,
            new PromotionAuthorityInput(ReceiptPath, signaturePath, "fixture-tooling-promoter"),
            "islamu-release-engineering",
            "1.0.0",
            "policy-v1",
            "config-v1",
            "trust-v1")
        { ExpectedManifestDigest = ManifestDigest };

        public TrustedBundleResult Verify(TrustedBundleVerificationRequest? request = null, string? trustedPromotionRootPath = null)
        {
            using RuntimePromotionTrustRootScope trustRoot = RuntimePromotionTrustRootScope.Use(trustedPromotionRootPath ?? allowedSignersPath);
            return TrustedBundlePolicy.Verify(request ?? Request());
        }

        public TrustedBundleResult VerifyWithPackagedDefault(TrustedBundleVerificationRequest request)
        {
            using RuntimePromotionTrustRootScope trustRoot = RuntimePromotionTrustRootScope.UsePackagedDefault();
            return TrustedBundlePolicy.Verify(request);
        }

        public PromotionAuthorityInput PromotionAuthority => Request().PromotionAuthority;

        public PromotionAuthorityInput WithPromotionManifestDigest(string digest)
        {
            ResignReceipt(manifestDigest: digest);
            return PromotionAuthority;
        }

        public void RewriteManifestWithoutFile(string path)
        {
            using JsonDocument document = JsonDocument.Parse(manifestJson);
            JsonElement root = document.RootElement;
            RewriteManifest(root.GetProperty("files").EnumerateArray()
                .Where(item => item.GetProperty("path").GetString() != path)
                .Select(item => (object)new { path = item.GetProperty("path").GetString(), sha256 = item.GetProperty("sha256").GetString() })
                .ToList());
            ResignReceipt();
        }

        public void RewriteManifestFromFiles(bool resignReceipt)
        {
            RewriteManifest(BuildManifestFiles(Root));
            if (resignReceipt) ResignReceipt();
        }

        public string ReplaceCandidateWithSymlinkToBundleRoot()
        {
            Directory.Delete(CandidateRoot, recursive: true);
            Directory.CreateSymbolicLink(CandidateRoot, Root);
            return CandidateRoot;
        }

        public void HardlinkRequiredFileOutsideBundle(string path)
        {
            string source = Path.Combine(Root, path.Replace('/', Path.DirectorySeparatorChar));
            string alias = Path.Combine(authorityRoot, "outside-hardlink");
            Run("/usr/bin/ln", source, alias);
        }

        public void RewriteManifestWithAdditionalFile(string path, byte[] hash, bool createFile, bool resignReceipt)
        {
            using JsonDocument document = JsonDocument.Parse(manifestJson);
            JsonElement root = document.RootElement;
            if (createFile)
            {
                string fullPath = Path.Combine(Root, path.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                File.WriteAllText(fullPath, "new");
            }

            List<object> files = root.GetProperty("files").EnumerateArray()
                .Select(item => (object)new { path = item.GetProperty("path").GetString(), sha256 = item.GetProperty("sha256").GetString() })
                .ToList();
            files.Add(new { path, sha256 = Convert.ToHexString(hash).ToLowerInvariant() });
            RewriteManifest(files);
            if (resignReceipt) ResignReceipt();
        }

        public void RewriteManifestAsNoncanonicalJson()
        {
            File.WriteAllText(Path.Combine(Root, "trusted-bundle.manifest.json"), manifestJson);
        }

        public void SetManifestLength(long length) => SetLength(Path.Combine(Root, "trusted-bundle.manifest.json"), length);
        public void SetReceiptLength(long length) => SetLength(ReceiptPath, length);
        public void SetSignatureLength(long length) => SetLength(signaturePath, length);
        public void SetTrustRootLength(long length) => SetLength(allowedSignersPath, length);

        public void ReplaceFiles(int count, long length)
        {
            foreach (string file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)) File.Delete(file);
            var files = new List<object>(count);
            string emptyHash = Convert.ToHexString(SHA256.HashData(Array.Empty<byte>())).ToLowerInvariant();
            string[] required =
            [
                "bin/ISLAMU.ReleaseEngineering.dll",
                "config/cliff.toml",
                "policy/context-version.txt",
                "policy/release-policy.yaml",
                "policy/schema-version.txt",
                "toolchain.lock.json",
                "trust/allowed-signers",
                "trust/release-signing-policy.yaml",
            ];
            int actualCount = Math.Max(count, required.Length);
            for (int index = 0; index < actualCount; index++)
            {
                string path = index < required.Length ? required[index] : $"payload/file-{index:D3}.bin";
                string fullPath = Path.Combine(Root, path.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                using (FileStream stream = File.Create(fullPath)) stream.SetLength(length);
                files.Add(new { path, sha256 = emptyHash });
            }

            RewriteManifest(files);
            ResignReceipt();
        }

        public void CreateExtraDirectories(int count)
        {
            for (int index = 0; index < count; index++) Directory.CreateDirectory(Path.Combine(Root, $"extra-{index:D4}"));
        }

        public void SetManifestPaths(params string[] paths)
        {
            string hash = Convert.ToHexString(SHA256.HashData(Array.Empty<byte>())).ToLowerInvariant();
            foreach (string path in paths.Distinct(StringComparer.Ordinal))
            {
                string fullPath = Path.Combine(Root, path.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                File.WriteAllBytes(fullPath, []);
            }

            RewriteManifest(paths.Select(path => (object)new { path, sha256 = hash }).ToList());
            ResignReceipt();
        }

        public void ResignReceipt(string? manifestDigest = null)
        {
            using JsonDocument document = JsonDocument.Parse(CanonicalArtifactPolicy.CanonicalizeJson(manifestJson).Bytes!);
            JsonElement root = document.RootElement;
            string receiptJson = JsonSerializer.Serialize(new
            {
                schemaVersion = "trusted-bundle-promotion.v1",
                receiptId = ReceiptId,
                bundleManifestSha256 = manifestDigest ?? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(Root, "trusted-bundle.manifest.json")))).ToLowerInvariant(),
                bundleId = root.GetProperty("bundleId").GetString(),
                bundleVersion = root.GetProperty("bundleVersion").GetString(),
                policyVersion = root.GetProperty("policyVersion").GetString(),
                configVersion = root.GetProperty("configVersion").GetString(),
                trustVersion = root.GetProperty("trustVersion").GetString(),
                policyDigest = root.GetProperty("policyDigest").GetString(),
                configDigest = root.GetProperty("configDigest").GetString(),
                trustDigest = root.GetProperty("trustDigest").GetString(),
                promotionPrincipal = "fixture-tooling-promoter",
            });
            File.WriteAllBytes(ReceiptPath, CanonicalArtifactPolicy.CanonicalizeJson(receiptJson).Bytes!);
            if (File.Exists(signaturePath)) File.Delete(signaturePath);
            Run("/usr/bin/ssh-keygen", "-Y", "sign", "-f", privateKeyPath, "-n", "islamu-release-promotion", ReceiptPath);
        }

        private void CreatePromotionAuthority()
        {
            Run("/usr/bin/ssh-keygen", "-q", "-t", "ed25519", "-N", string.Empty, "-C", "synthetic-promotion-fixture", "-f", privateKeyPath);
            string publicKey = string.Join(' ', File.ReadAllText(privateKeyPath + ".pub").Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2));
            File.WriteAllText(allowedSignersPath, $"fixture-tooling-promoter namespaces=\"islamu-release-promotion\" {publicKey}\n");
            ResignReceipt();
        }

        private void RewriteManifest(IReadOnlyList<object> files)
        {
            using JsonDocument document = JsonDocument.Parse(manifestJson);
            JsonElement root = document.RootElement;
            JsonElement fileArray = JsonSerializer.SerializeToElement(files);
            string Digest(string path) => fileArray.EnumerateArray().SingleOrDefault(item => item.GetProperty("path").GetString() == path).ValueKind == JsonValueKind.Undefined
                ? root.GetProperty(path switch { "policy/release-policy.yaml" => "policyDigest", "config/cliff.toml" => "configDigest", _ => "trustDigest" }).GetString()!
                : fileArray.EnumerateArray().Single(item => item.GetProperty("path").GetString() == path).GetProperty("sha256").GetString()!;
            manifestJson = JsonSerializer.Serialize(new
            {
                schemaVersion = root.GetProperty("schemaVersion").GetString(),
                bundleId = root.GetProperty("bundleId").GetString(),
                bundleVersion = root.GetProperty("bundleVersion").GetString(),
                policyVersion = root.GetProperty("policyVersion").GetString(),
                configVersion = root.GetProperty("configVersion").GetString(),
                trustVersion = root.GetProperty("trustVersion").GetString(),
                policyDigest = Digest("policy/release-policy.yaml"),
                configDigest = Digest("config/cliff.toml"),
                trustDigest = Digest("trust/release-signing-policy.yaml"),
                files,
            });
            WriteManifest();
        }

        private void WriteManifest()
        {
            CanonicalArtifactResult canonical = CanonicalArtifactPolicy.CanonicalizeJson(manifestJson);
            File.WriteAllBytes(Path.Combine(Root, "trusted-bundle.manifest.json"), canonical.Bytes!);
        }

        private static void Run(string executable, params string[] arguments)
        {
            var startInfo = new ProcessStartInfo(executable) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (string argument in arguments) startInfo.ArgumentList.Add(argument);
            using Process process = Process.Start(startInfo)!;
            if (!process.WaitForExit(TimeSpan.FromSeconds(10)))
            {
                process.Kill(entireProcessTree: true);
                throw new TimeoutException("synthetic_ssh_fixture_timeout");
            }

            if (process.ExitCode != 0) throw new InvalidOperationException("synthetic_ssh_fixture_failed");
        }

        private static void SetLength(string path, long length)
        {
            using FileStream stream = File.OpenWrite(path);
            stream.SetLength(length);
        }

        private static void WriteBundleFile(string root, string path, string contents)
        {
            string fullPath = Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, contents);
        }

        private static List<object> BuildManifestFiles(string root) => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => Path.GetFileName(path) != "trusted-bundle.manifest.json")
            .Select(path => new
            {
                path = Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/'),
                sha256 = HashFile(path),
            })
            .OrderBy(item => item.path, StringComparer.Ordinal)
            .Select(item => (object)item)
            .ToList();

        private static string HashFile(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

        public void Dispose() => Directory.Delete(parent, recursive: true);
    }
}
