namespace Event.Setup.Core.Tests;

using System.Reflection;

internal static class SetupCompositionTestContract
{
    internal const string CompilerTypeName =
        "ISLAMU.Event.Setup.Core.Composition.SetupCompositionCompiler";

    internal static readonly CompositionLimit[] Limits =
    [
        new("aggregate-source-bytes", 4_194_304),
        new("yaml-documents", 1),
        new("parser-events", 131_072),
        new("normalized-nodes", 65_536),
        new("nesting-depth", 32),
        new("mapping-entries", 4_096),
        new("sequence-entries", 4_096),
        new("scalar-characters", 65_536),
        new("aggregate-scalar-characters", 1_048_576),
        new("directories", 256),
        new("files", 1_024),
        new("entries-per-directory", 256),
        new("relative-path-characters", 512),
        new("path-depth", 16),
        new("per-file-bytes", 524_288),
        new("aggregate-directory-bytes", 4_194_304),
        new("aggregate-directory-nodes", 65_536)
    ];

    internal static readonly CompositionVector[] Vectors =
    [
        new(CompositionMatrix.KeyShape, "duplicate-exact", "duplicate-key"),
        new(CompositionMatrix.KeyShape, "duplicate-case", "key-collision"),
        new(CompositionMatrix.KeyShape, "duplicate-normalization", "key-collision"),
        new(CompositionMatrix.KeyShape, "non-scalar-key", "invalid-key"),
        new(CompositionMatrix.KeyShape, "null-key", "invalid-key"),

        new(CompositionMatrix.YamlGrammar, "alias", "unsupported-yaml-grammar"),
        new(CompositionMatrix.YamlGrammar, "anchor", "unsupported-yaml-grammar"),
        new(CompositionMatrix.YamlGrammar, "tag", "unsupported-yaml-grammar"),
        new(CompositionMatrix.YamlGrammar, "merge-key", "unsupported-yaml-grammar"),
        new(CompositionMatrix.YamlGrammar, "directive", "unsupported-yaml-grammar"),
        new(CompositionMatrix.YamlGrammar, "unsupported-node", "unsupported-yaml-grammar"),

        new(CompositionMatrix.ScalarParity, "quoted-string", "accepted"),
        new(CompositionMatrix.ScalarParity, "unquoted-string", "accepted"),
        new(CompositionMatrix.ScalarParity, "boolean-exact", "accepted"),
        new(CompositionMatrix.ScalarParity, "integer-invariant", "accepted"),
        new(CompositionMatrix.ScalarParity, "null-exact", "accepted"),
        new(CompositionMatrix.ScalarParity, "locale-number", "invalid-scalar"),

        new(CompositionMatrix.DocumentShape, "empty-stream", "invalid-document"),
        new(CompositionMatrix.DocumentShape, "empty-document", "invalid-document"),
        new(CompositionMatrix.DocumentShape, "scalar-root", "invalid-document"),
        new(CompositionMatrix.DocumentShape, "sequence-root", "invalid-document"),
        new(CompositionMatrix.DocumentShape, "multiple-documents", "invalid-document"),
        new(CompositionMatrix.DocumentShape, "trailing-document-content", "invalid-document"),

        new(CompositionMatrix.ParserCeilings, "exact-boundary", "accepted"),
        new(CompositionMatrix.ParserCeilings, "boundary-plus-one", "limit-exceeded"),
        new(CompositionMatrix.ParserCeilings, "checked-overflow", "limit-exceeded"),

        new(CompositionMatrix.PathSafety, "rooted-path", "unsafe-path"),
        new(CompositionMatrix.PathSafety, "absolute-path", "unsafe-path"),
        new(CompositionMatrix.PathSafety, "parent-traversal", "unsafe-path"),
        new(CompositionMatrix.PathSafety, "escaped-root", "unsafe-path"),
        new(CompositionMatrix.PathSafety, "overlong-path", "limit-exceeded"),
        new(CompositionMatrix.PathSafety, "reserved-name", "unsafe-path"),
        new(CompositionMatrix.PathSafety, "normalization-collision", "path-collision"),

        new(CompositionMatrix.FilesystemIdentity, "symbolic-link", "unsafe-entry"),
        new(CompositionMatrix.FilesystemIdentity, "reparse-point", "unsafe-entry"),
        new(CompositionMatrix.FilesystemIdentity, "junction", "unsafe-entry"),
        new(CompositionMatrix.FilesystemIdentity, "hard-link", "unsafe-entry"),
        new(CompositionMatrix.FilesystemIdentity, "special-file", "unsafe-entry"),
        new(CompositionMatrix.FilesystemIdentity, "cycle", "unsafe-entry"),
        new(CompositionMatrix.FilesystemIdentity, "unsupported-semantics", "unsupported-filesystem"),

        new(CompositionMatrix.DirectoryMutation, "added-after-discovery", "source-changed"),
        new(CompositionMatrix.DirectoryMutation, "removed-after-discovery", "source-changed"),
        new(CompositionMatrix.DirectoryMutation, "renamed-after-discovery", "source-changed"),
        new(CompositionMatrix.DirectoryMutation, "replaced-after-open", "source-changed"),
        new(CompositionMatrix.DirectoryMutation, "resized-after-open", "source-changed"),
        new(CompositionMatrix.DirectoryMutation, "retargeted-before-commit", "source-changed"),

        new(CompositionMatrix.ConflictOrdering, "duplicate-fragment", "source-conflict"),
        new(CompositionMatrix.ConflictOrdering, "conflicting-fragment", "source-conflict"),
        new(CompositionMatrix.ConflictOrdering, "ordinal-order", "accepted"),
        new(CompositionMatrix.ConflictOrdering, "locale-independent-order", "accepted"),

        new(CompositionMatrix.Cancellation, "before-discovery", "cancelled"),
        new(CompositionMatrix.Cancellation, "during-read", "cancelled"),
        new(CompositionMatrix.Cancellation, "during-parser", "cancelled"),
        new(CompositionMatrix.Cancellation, "during-normalization", "cancelled"),
        new(CompositionMatrix.Cancellation, "during-validation", "cancelled"),
        new(CompositionMatrix.Cancellation, "during-serialization", "cancelled"),
        new(CompositionMatrix.Cancellation, "at-publication-commit", "cancelled"),

        new(CompositionMatrix.Smuggling, "secret-material", "forbidden-authority"),
        new(CompositionMatrix.Smuggling, "provider-coordinate", "forbidden-authority"),
        new(CompositionMatrix.Smuggling, "application-data", "forbidden-authority"),
        new(CompositionMatrix.Smuggling, "publication-evidence", "forbidden-authority"),
        new(CompositionMatrix.Smuggling, "acceptance-evidence", "forbidden-authority"),
        new(CompositionMatrix.Smuggling, "tenant-user-authority", "forbidden-authority"),

        new(CompositionMatrix.CanonicalParity, "json", "accepted"),
        new(CompositionMatrix.CanonicalParity, "yaml", "accepted"),
        new(CompositionMatrix.CanonicalParity, "directory", "accepted"),
        new(CompositionMatrix.CanonicalParity, "byte-identity", "accepted"),
        new(CompositionMatrix.CanonicalParity, "digest-identity", "accepted"),
        new(CompositionMatrix.CanonicalParity, "coverage-identity", "accepted"),
        new(CompositionMatrix.CanonicalParity, "legal-identity", "accepted"),
        new(CompositionMatrix.CanonicalParity, "diagnostic-identity", "accepted"),

        new(CompositionMatrix.ValueFreeFailure, "closed-code", "accepted"),
        new(CompositionMatrix.ValueFreeFailure, "no-source-path", "accepted"),
        new(CompositionMatrix.ValueFreeFailure, "no-key-value", "accepted"),
        new(CompositionMatrix.ValueFreeFailure, "no-exception-text", "accepted"),
        new(CompositionMatrix.ValueFreeFailure, "no-provider-coordinate", "accepted"),
        new(CompositionMatrix.ValueFreeFailure, "no-tenant-user-id", "accepted"),

        new(CompositionMatrix.ProfileAdmission, "unknown-profile", "profile-disabled"),
        new(CompositionMatrix.ProfileAdmission, "disabled-profile", "profile-disabled"),
        new(CompositionMatrix.ProfileAdmission, "evidence-mismatch", "profile-disabled"),
        new(CompositionMatrix.ProfileAdmission, "target-incompatible", "profile-disabled"),
        new(CompositionMatrix.ProfileAdmission, "no-clamp", "profile-disabled"),
        new(CompositionMatrix.ProfileAdmission, "no-fallback", "profile-disabled")
    ];

    internal static readonly CompositionWorstBreak WorstBreak = new(
        Barrier: "publication-commit",
        DirectoryMutation: "retargeted-open-entry",
        ParserAttack: "alias-bomb-at-final-ceiling",
        ExpectedCode: "source-changed",
        ExpectedArtifacts: 0,
        UsesSleep: false,
        UsesPolling: false,
        UsesMockFilesystem: false);

    internal static CompositionVector[] For(CompositionMatrix matrix) =>
        Vectors.Where(vector => vector.Matrix == matrix).ToArray();

    internal static Type RequireCompiler()
    {
        Assembly assembly = Assembly.Load("Event.Setup.Core");
        return assembly.GetType(CompilerTypeName, throwOnError: false)
            ?? throw new InvalidOperationException(
                $"missing-approved-owner:{CompilerTypeName}");
    }
}

internal enum CompositionMatrix
{
    KeyShape,
    YamlGrammar,
    ScalarParity,
    DocumentShape,
    ParserCeilings,
    PathSafety,
    FilesystemIdentity,
    DirectoryMutation,
    ConflictOrdering,
    Cancellation,
    Smuggling,
    CanonicalParity,
    ValueFreeFailure,
    ProfileAdmission
}

internal sealed record CompositionLimit(string Name, int Value);

internal sealed record CompositionVector(
    CompositionMatrix Matrix,
    string Case,
    string ExpectedCode);

internal sealed record CompositionWorstBreak(
    string Barrier,
    string DirectoryMutation,
    string ParserAttack,
    string ExpectedCode,
    int ExpectedArtifacts,
    bool UsesSleep,
    bool UsesPolling,
    bool UsesMockFilesystem);
