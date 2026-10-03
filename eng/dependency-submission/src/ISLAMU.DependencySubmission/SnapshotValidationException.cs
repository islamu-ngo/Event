using System.Collections.Frozen;

namespace ISLAMU.DependencySubmission;

public enum SnapshotValidationFailure
{
    Context, DuplicatePath, LockVersion, FrameworkGroup, PackageKind, ResolvedVersion,
    DuplicatePackage, LockDocument, EmptyRepository, RepositoryPath, DuplicateProperty
}

public sealed class SnapshotValidationException(SnapshotValidationFailure failure) : IOException("Snapshot validation failed.")
{
    public SnapshotValidationFailure Failure { get; } = failure;

    public static FrozenDictionary<SnapshotValidationFailure, string> Codes { get; } =
        new Dictionary<SnapshotValidationFailure, string>
        {
            [SnapshotValidationFailure.Context] = "invalid_context",
            [SnapshotValidationFailure.DuplicatePath] = "duplicate_path",
            [SnapshotValidationFailure.LockVersion] = "unsupported_lock_version",
            [SnapshotValidationFailure.FrameworkGroup] = "invalid_framework_group",
            [SnapshotValidationFailure.PackageKind] = "invalid_package_kind",
            [SnapshotValidationFailure.ResolvedVersion] = "missing_resolved_version",
            [SnapshotValidationFailure.DuplicatePackage] = "duplicate_package",
            [SnapshotValidationFailure.LockDocument] = "invalid_lock_document",
            [SnapshotValidationFailure.EmptyRepository] = "empty_repository",
            [SnapshotValidationFailure.RepositoryPath] = "invalid_repository_path",
            [SnapshotValidationFailure.DuplicateProperty] = "duplicate_property"
        }.ToFrozenDictionary();
}
