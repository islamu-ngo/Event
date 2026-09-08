namespace Event.Setup.Core.Tests;

using System.Reflection;

public sealed class SetupCompositionInvariantTests
{
    [Test]
    public async Task KeyShapeMatrixIsClosedAndCollisionAware() =>
        await AssertMatrix(
            CompositionMatrix.KeyShape,
            "duplicate-exact",
            "duplicate-case",
            "duplicate-normalization",
            "non-scalar-key",
            "null-key");

    [Test]
    public async Task YamlGrammarMatrixRejectsEveryForbiddenFeature() =>
        await AssertMatrix(
            CompositionMatrix.YamlGrammar,
            "alias",
            "anchor",
            "tag",
            "merge-key",
            "directive",
            "unsupported-node");

    [Test]
    public async Task ScalarParityMatrixPinsCoreOwnedConversion() =>
        await AssertMatrix(
            CompositionMatrix.ScalarParity,
            "quoted-string",
            "unquoted-string",
            "boolean-exact",
            "integer-invariant",
            "null-exact",
            "locale-number");

    [Test]
    public async Task DocumentShapeMatrixRejectsAmbiguousStreams() =>
        await AssertMatrix(
            CompositionMatrix.DocumentShape,
            "empty-stream",
            "empty-document",
            "scalar-root",
            "sequence-root",
            "multiple-documents",
            "trailing-document-content");

    [Test]
    public async Task ParserCeilingsPinExactBoundariesAndCheckedArithmetic()
    {
        CompositionLimit[] limits = SetupCompositionTestContract.Limits;

        await Assert.That(limits.Select(limit => limit.Name).Distinct(StringComparer.Ordinal)
            .Count()).IsEqualTo(limits.Length);
        await Assert.That(limits.All(limit => limit.Value > 0)).IsTrue();
        foreach (CompositionLimit limit in limits)
        {
            int accepted = limit.Value;
            int rejected = checked(limit.Value + 1);
            await Assert.That(accepted).IsEqualTo(limit.Value);
            await Assert.That(rejected).IsGreaterThan(limit.Value);
        }

        await AssertMatrix(
            CompositionMatrix.ParserCeilings,
            "exact-boundary",
            "boundary-plus-one",
            "checked-overflow");
    }

    [Test]
    public async Task PathSafetyMatrixRejectsEscapeAndNormalizationAmbiguity() =>
        await AssertMatrix(
            CompositionMatrix.PathSafety,
            "rooted-path",
            "absolute-path",
            "parent-traversal",
            "escaped-root",
            "overlong-path",
            "reserved-name",
            "normalization-collision");

    [Test]
    public async Task FilesystemIdentityMatrixRequiresProvableSafeEntries() =>
        await AssertMatrix(
            CompositionMatrix.FilesystemIdentity,
            "symbolic-link",
            "reparse-point",
            "junction",
            "hard-link",
            "special-file",
            "cycle",
            "unsupported-semantics");

    [Test]
    public async Task DirectoryMutationMatrixPinsBothSnapshotBarriers() =>
        await AssertMatrix(
            CompositionMatrix.DirectoryMutation,
            "added-after-discovery",
            "removed-after-discovery",
            "renamed-after-discovery",
            "replaced-after-open",
            "resized-after-open",
            "retargeted-before-commit");

    [Test]
    public async Task ConflictAndOrderingMatrixIsDeterministicAndOrdinal() =>
        await AssertMatrix(
            CompositionMatrix.ConflictOrdering,
            "duplicate-fragment",
            "conflicting-fragment",
            "ordinal-order",
            "locale-independent-order");

    [Test]
    public async Task CancellationMatrixCoversEveryPipelineStage() =>
        await AssertMatrix(
            CompositionMatrix.Cancellation,
            "before-discovery",
            "during-read",
            "during-parser",
            "during-normalization",
            "during-validation",
            "during-serialization",
            "at-publication-commit");

    [Test]
    public async Task SmugglingMatrixRejectsEveryForeignAuthority() =>
        await AssertMatrix(
            CompositionMatrix.Smuggling,
            "secret-material",
            "provider-coordinate",
            "application-data",
            "publication-evidence",
            "acceptance-evidence",
            "tenant-user-authority");

    [Test]
    public async Task CanonicalParityMatrixPinsEveryCoreAuthority() =>
        await AssertMatrix(
            CompositionMatrix.CanonicalParity,
            "json",
            "yaml",
            "directory",
            "byte-identity",
            "digest-identity",
            "coverage-identity",
            "legal-identity",
            "diagnostic-identity");

    [Test]
    public async Task FailureAndProfileMatricesAreClosedAndValueFree()
    {
        await AssertMatrix(
            CompositionMatrix.ValueFreeFailure,
            "closed-code",
            "no-source-path",
            "no-key-value",
            "no-exception-text",
            "no-provider-coordinate",
            "no-tenant-user-id");
        await AssertMatrix(
            CompositionMatrix.ProfileAdmission,
            "unknown-profile",
            "disabled-profile",
            "evidence-mismatch",
            "target-incompatible",
            "no-clamp",
            "no-fallback");

        PropertyInfo[] vectorProperties = typeof(CompositionVector).GetProperties(
            BindingFlags.Public | BindingFlags.Instance);
        await Assert.That(vectorProperties.Select(property => property.Name))
            .IsEquivalentTo(["Case", "ExpectedCode", "Matrix"]);
    }

    [Test]
    public async Task Phase8WorstBreakUsesExactBarrierAndProducesNothing()
    {
        CompositionWorstBreak breaker = SetupCompositionTestContract.WorstBreak;

        await Assert.That(breaker.Barrier).IsEqualTo("publication-commit");
        await Assert.That(breaker.DirectoryMutation).IsEqualTo("retargeted-open-entry");
        await Assert.That(breaker.ParserAttack).IsEqualTo("alias-bomb-at-final-ceiling");
        await Assert.That(breaker.ExpectedCode).IsEqualTo("source-changed");
        await Assert.That(breaker.ExpectedArtifacts).IsEqualTo(0);
        await Assert.That(breaker.UsesSleep).IsFalse();
        await Assert.That(breaker.UsesPolling).IsFalse();
        await Assert.That(breaker.UsesMockFilesystem).IsFalse();
    }

    [Test]
    public async Task ApprovedPublicCompositionCompilerOwnerExists()
    {
        Type compiler = SetupCompositionTestContract.RequireCompiler();

        await Assert.That(compiler.IsPublic).IsTrue();
    }

    private static async Task AssertMatrix(
        CompositionMatrix matrix,
        params string[] requiredCases)
    {
        CompositionVector[] vectors = SetupCompositionTestContract.For(matrix);
        HashSet<string> cases = vectors.Select(vector => vector.Case)
            .ToHashSet(StringComparer.Ordinal);

        await Assert.That(vectors).IsNotEmpty();
        await Assert.That(vectors.All(vector =>
            vector.Matrix == matrix
            && vector.Case.Length > 0
            && vector.ExpectedCode.Length > 0)).IsTrue();
        await Assert.That(cases.Count).IsEqualTo(vectors.Length);
        await Assert.That(requiredCases.All(cases.Contains)).IsTrue();
    }
}
