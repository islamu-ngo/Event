using System.Collections.Immutable;
using System.Text;
using System.Text.Json.Nodes;
using Explore.Application.Features.ConfigurationManifest.Moderation;
using Explore.Application.Features.Moderation.Policy;
using Explore.Application.Services.Moderation;
using ISLAMU.Moderation.Domain;
using ISLAMU.Moderation.Domain.Policies;
using ISLAMU.Wire.Contracts.ConfigurationPortability;

namespace Event.Application.UnitTests.Moderation;

public sealed class ModerationConfigurationDocumentTests
{
    private static readonly ModerationCapabilityRegistry Registry =
        ModerationCapabilityRegistry.Parse(ModerationCapabilityRegistry.ReadInventory());
    private static readonly AuthorityScope Source = AuthorityScope.TrustDomain(Id(10));
    private static readonly AuthorityScope Destination = AuthorityScope.TrustDomain(Id(11));

    [Test]
    [Arguments("unchanged", 0, false)]
    [Arguments("local-only", 0, false)]
    [Arguments("incoming-only", 1, false)]
    [Arguments("same-change", 0, false)]
    [Arguments("conflict", 1, true)]
    public async Task PresetDiffPreservesLocalEditsAndReportsOnlyIndependentIncomingChanges(
        string scenario, int changes, bool conflict)
    {
        var baseline = Document();
        var localMeaning = scenario is "local-only" or "conflict" ? "Local meaning."
            : scenario == "same-change" ? "Incoming meaning." : baseline.Classes[0].Meaning;
        var incomingMeaning = scenario is "incoming-only" or "same-change" or "conflict"
            ? "Incoming meaning." : baseline.Classes[0].Meaning;
        ModerationConfigurationDocument ChangeMeaning(string meaning) =>
            new(baseline.SchemaVersion, baseline.CatalogueRevision, baseline.Modes,
                [baseline.Classes[0] with { Meaning = meaning }], baseline.Reasons,
                baseline.Profiles, baseline.SupportedProfiles);
        var local = ChangeMeaning(localMeaning);
        var incoming = ChangeMeaning(incomingMeaning);
        var original = ModerationConfigurationDocumentProjection.Serialize(local);

        var result = ModerationConfigurationDocumentDiff.Compare(baseline, local, incoming);

        await Assert.That(result.Changes.Length).IsEqualTo(changes);
        await Assert.That(result.HasConflicts).IsEqualTo(conflict);
        if (changes != 0)
        {
            await Assert.That(result.Changes[0].Section).IsEqualTo("classes");
            await Assert.That(result.Changes[0].Reference).IsEqualTo(baseline.Classes[0].Reference);
            await Assert.That(result.Changes[0].Conflict).IsEqualTo(conflict);
        }
        await Assert.That(ModerationConfigurationDocumentProjection.Serialize(local).SequenceEqual(original)).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task PresetRemovalConflictsOnlyWithAnIndependentLocalDefinitionEdit(bool localEdit)
    {
        var source = Document();
        var other = source.Profiles[0] with { Rule = new("profile.second", 1) };
        ModerationConfigurationDocument Profiles(params ModerationConfigurationProfile[] profiles) =>
            new(source.SchemaVersion, source.CatalogueRevision, source.Modes, source.Classes,
                source.Reasons, profiles, source.SupportedProfiles);
        var baseline = Profiles(source.Profiles[0], other);
        var local = Profiles(source.Profiles[0], localEdit ? other with { ContestationCalendarMonths = 12 } : other);
        var incoming = Profiles(source.Profiles[0]);

        var result = ModerationConfigurationDocumentDiff.Compare(baseline, local, incoming);

        await Assert.That(result.Changes.Length).IsEqualTo(1);
        await Assert.That(result.Changes[0].Reference).IsEqualTo(other.Rule);
        await Assert.That(result.Changes[0].Kind).IsEqualTo(ModerationConfigurationChangeKind.Removed);
        await Assert.That(result.HasConflicts).IsEqualTo(localEdit);
    }

    [Test]
    public async Task PresetMergePreservesLocalDefinitionsAndAppliesIndependentIncomingChanges()
    {
        var baseline = Document();
        var local = new ModerationConfigurationDocument(baseline.SchemaVersion, baseline.CatalogueRevision,
            baseline.Modes, [baseline.Classes[0] with { Meaning = "Local classification." }],
            baseline.Reasons, [baseline.Profiles[0] with { ContestationCalendarMonths = 12 }],
            baseline.SupportedProfiles);
        var incoming = new ModerationConfigurationDocument(baseline.SchemaVersion, baseline.CatalogueRevision,
            baseline.Modes, baseline.Classes, [baseline.Reasons[0] with { Meaning = "Incoming reason." }],
            baseline.Profiles, baseline.SupportedProfiles);
        var original = ModerationConfigurationDocumentProjection.Serialize(local);

        var merged = ModerationConfigurationDocumentDiff.Merge(baseline, local, incoming);

        await Assert.That(merged.Diff.HasConflicts).IsFalse();
        await Assert.That(merged.Document).IsNotNull();
        await Assert.That(merged.Document!.Classes[0].Meaning).IsEqualTo("Local classification.");
        await Assert.That(merged.Document.Reasons[0].Meaning).IsEqualTo("Incoming reason.");
        await Assert.That(merged.Document.Profiles[0].ContestationCalendarMonths).IsEqualTo(12);
        await Assert.That(merged.Document.Profiles[0].Recipes.SequenceEqual(local.Profiles[0].Recipes)).IsTrue();
        await Assert.That(ModerationConfigurationDocumentProjection.Serialize(local).SequenceEqual(original)).IsTrue();
        ModerationConfigurationDocumentProjection.Parse(
            ModerationConfigurationDocumentProjection.Serialize(merged.Document));
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task PresetMergeKeepsLocalAdditionsAndOnlyRemovesUneditedDefinitions(bool editedRemoval)
    {
        var source = Document();
        var removed = source.Profiles[0] with { Rule = new("profile.removed", 1) };
        var added = source.Profiles[0] with { Rule = new("profile.local", 1) };
        var baseline = new ModerationConfigurationDocument(source.SchemaVersion, source.CatalogueRevision,
            source.Modes, source.Classes, source.Reasons, [source.Profiles[0], removed], source.SupportedProfiles);
        var local = new ModerationConfigurationDocument(source.SchemaVersion, source.CatalogueRevision,
            source.Modes, source.Classes, source.Reasons,
            [source.Profiles[0], editedRemoval ? removed with { ContestationCalendarMonths = 12 } : removed, added],
            source.SupportedProfiles);

        var merged = ModerationConfigurationDocumentDiff.Merge(baseline, local, source);

        await Assert.That(merged.Diff.HasConflicts).IsEqualTo(editedRemoval);
        if (editedRemoval)
            await Assert.That(merged.Document).IsNull();
        else
        {
            await Assert.That(merged.Document).IsNotNull();
            await Assert.That(merged.Document!.Profiles.Count).IsEqualTo(2);
            await Assert.That(merged.Document.Profiles.Any(profile => profile.Rule == added.Rule)).IsTrue();
            await Assert.That(merged.Document.Profiles.Any(profile => profile.Rule == removed.Rule)).IsFalse();
        }
    }

    [Test]
    public async Task PresetMergeCannotPartiallyApplyWhenAnyCompleteRecipeConflicts()
    {
        var baseline = Document();
        var local = ReplaceProfile(baseline, baseline.Profiles[0] with { ContestationCalendarMonths = 12 });
        var incoming = new ModerationConfigurationDocument(baseline.SchemaVersion, baseline.CatalogueRevision,
            baseline.Modes, [baseline.Classes[0] with { Meaning = "Independent incoming classification." }],
            baseline.Reasons,
            [baseline.Profiles[0] with
            {
                Recipes = baseline.Profiles[0].Recipes.SetItem(0, baseline.Profiles[0].Recipes[0] with
                {
                    PrivateDisclosure = ModerationPrivateDisclosureMode.FenceSelectedVenueAndAllDerivatives
                })
            }], baseline.SupportedProfiles);

        var merged = ModerationConfigurationDocumentDiff.Merge(baseline, local, incoming);

        await Assert.That(merged.Document).IsNull();
        await Assert.That(merged.Diff.HasConflicts).IsTrue();
        await Assert.That(merged.Diff.Changes.Length).IsEqualTo(2);
        await Assert.That(local.Classes[0].Meaning).IsEqualTo(baseline.Classes[0].Meaning);
        await Assert.That(local.Profiles[0].Recipes.SequenceEqual(baseline.Profiles[0].Recipes)).IsTrue();
    }

    [Test]
    public async Task PresetCatalogueOrderDoesNotManufactureChanges()
    {
        var source = Document();
        var other = source.Profiles[0] with { Rule = new("profile.second", 1) };
        var baseline = new ModerationConfigurationDocument(source.SchemaVersion, source.CatalogueRevision,
            source.Modes, source.Classes, source.Reasons, [source.Profiles[0], other], source.SupportedProfiles);
        var incoming = new ModerationConfigurationDocument(source.SchemaVersion, source.CatalogueRevision,
            source.Modes, source.Classes, source.Reasons, [other, source.Profiles[0]], source.SupportedProfiles);

        var result = ModerationConfigurationDocumentDiff.Compare(baseline, baseline, incoming);

        await Assert.That(result.Changes.IsEmpty).IsTrue();
        await Assert.That(result.HasConflicts).IsFalse();
    }

    [Test]
    public async Task PresetProfileSetOrderCannotConflictWithAnIndependentLocalEdit()
    {
        foreach (var id in Enum.GetValues<ModerationReferenceProfileId>())
        {
            var source = Document(id);
            var reference = ModerationReferenceCatalogue.GetProfile(id);
            var profile = source.Profiles[0] with
            {
                ScopeKinds = reference.ScopeKinds.Order().ToImmutableArray(),
                SubjectKinds = reference.SubjectKinds.Order().ToImmutableArray()
            };
            var baseline = ReplaceProfile(source, profile);
            var local = ReplaceProfile(source, profile with { ContestationCalendarMonths = 12 });
            var incoming = ReplaceProfile(source, profile with
            {
                ScopeKinds = profile.ScopeKinds.Reverse().ToImmutableArray(),
                SubjectKinds = profile.SubjectKinds.Reverse().ToImmutableArray(),
                Effects = profile.Effects.Reverse().ToImmutableArray(),
                Strategies = profile.Strategies.Reverse().ToImmutableArray()
            });

            var result = ModerationConfigurationDocumentDiff.Compare(baseline, local, incoming);

            await Assert.That(result.Changes.IsEmpty).IsTrue();
            await Assert.That(result.HasConflicts).IsFalse();
        }
    }

    [Test]
    public async Task DuplicateSemanticDefinitionsCannotProduceAnUnambiguousPresetDiff()
    {
        var source = Document();
        var duplicate = new ModerationConfigurationDocument(source.SchemaVersion, source.CatalogueRevision,
            source.Modes, source.Classes, source.Reasons, [source.Profiles[0], source.Profiles[0]],
            source.SupportedProfiles);

        await Assert.That(() => ModerationConfigurationDocumentDiff.Compare(source, source, duplicate))
            .Throws<ConfigurationPortabilityContractException>();
    }

    [Test]
    public async Task PresetDiffCannotHideRecipeChangesBehindAnUnchangedProfileReference()
    {
        var baseline = Document();
        var profile = baseline.Profiles[0];
        var local = ReplaceProfile(baseline, profile with { ContestationCalendarMonths = 12 });
        var incoming = ReplaceProfile(baseline, profile with
        {
            Recipes = profile.Recipes.SetItem(0, profile.Recipes[0] with
            {
                PrivateDisclosure = ModerationPrivateDisclosureMode.FenceSelectedVenueAndAllDerivatives
            })
        });

        var result = ModerationConfigurationDocumentDiff.Compare(baseline, local, incoming);

        await Assert.That(result.HasConflicts).IsTrue();
        await Assert.That(result.Changes.Length).IsEqualTo(1);
        await Assert.That(result.Changes[0].Section).IsEqualTo("profiles");
        await Assert.That(result.Changes[0].Reference).IsEqualTo(profile.Rule);
    }

    [Test]
    public async Task RoundTripPreservesCompleteDefinitionsAndRecipesWithoutSourceAuthority()
    {
        var document = Document();
        var parsed = ModerationConfigurationDocumentProjection.Parse(
            ModerationConfigurationDocumentProjection.Serialize(document));
        var state = ModerationConfigurationDocumentProjection.ToProposedState(parsed, Mapping(parsed), Registry);
        await Assert.That(state.Jurisdiction).IsEqualTo(Destination);
        await Assert.That(state.Profiles[0].Id).IsEqualTo(Id(200));
        await Assert.That(state.Profiles[0].Rule.OwnerId).IsEqualTo(Destination.Id);
        await Assert.That(state.Classes[0].Reference.OwnerId).IsEqualTo(Destination.Id);
        await Assert.That(state.Reasons[0].Class.OwnerId).IsEqualTo(Destination.Id);
        await Assert.That(state.Profiles[0].Effects.Length).IsEqualTo(document.Profiles[0].Effects.Length);
        await Assert.That(parsed.Profiles[0].Recipes.Length).IsEqualTo(document.Profiles[0].Recipes.Length);
        await Assert.That(state.RequestedState).IsEqualTo(ModerationPolicyRequestedState.Draft);
        var json = Encoding.UTF8.GetString(ModerationConfigurationDocumentProjection.Serialize(document));
        await Assert.That(json.Contains(Source.Id.ToString(), StringComparison.Ordinal)).IsFalse();
        await Assert.That(json.Contains(Id(100).ToString(), StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task DocumentSnapshotsMutableCatalogueInputs()
    {
        var original = Document();
        var classes = original.Classes.ToList();
        var supported = original.SupportedProfiles.ToList();
        var document = new ModerationConfigurationDocument(original.SchemaVersion, original.CatalogueRevision,
            original.Modes, classes, original.Reasons, original.Profiles, supported);
        classes.Clear();
        supported.Clear();
        await Assert.That(document.Classes.Count).IsEqualTo(1);
        await Assert.That(document.SupportedProfiles.Count).IsEqualTo(1);
    }

    [Test]
    public async Task MappingSnapshotsDestinationIdentities()
    {
        var document = Document();
        var ids = new Dictionary<ModerationConfigurationReference, Guid> { [document.Profiles[0].Rule] = Id(200) };
        var mapping = new ModerationConfigurationDestinationMapping(Id(30), 0, Id(31), Context(), ids);
        ids[document.Profiles[0].Rule] = Id(201);
        await Assert.That(ModerationConfigurationDocumentProjection.ToProposedState(document, mapping, Registry)
            .Profiles[0].Id).IsEqualTo(Id(200));
    }

    [Test]
    [Arguments("cases")]
    [Arguments("people")]
    [Arguments("bans")]
    [Arguments("dateOfBirth")]
    [Arguments("grants")]
    [Arguments("keys")]
    [Arguments("endpoint")]
    [Arguments("approval")]
    [Arguments("publicationId")]
    [Arguments("unknown")]
    public async Task RuntimeAuthorityAndUnknownMembersAreRejected(string member)
    {
        var json = JsonNode.Parse(ModerationConfigurationDocumentProjection.Serialize(Document()))!;
        json[member] = true;
        await Assert.That(() => Parse(json)).Throws<ConfigurationPortabilityContractException>();
    }

    [Test]
    public async Task NestedNativeOwnerCannotBeImported()
    {
        var json = JsonNode.Parse(ModerationConfigurationDocumentProjection.Serialize(Document()))!;
        json["profiles"]![0]!["rule"]!["ownerId"] = Destination.Id.ToString();
        await Assert.That(() => Parse(json)).Throws<ConfigurationPortabilityContractException>();
    }

    [Test]
    public async Task NumericEnumAndDuplicateMembersAreRejected()
    {
        var json = JsonNode.Parse(ModerationConfigurationDocumentProjection.Serialize(Document()))!;
        json["profiles"]![0]!["grade"] = 0;
        await Assert.That(() => Parse(json)).Throws<ConfigurationPortabilityContractException>();
        json["profiles"]![0]!["grade"] = "0";
        await Assert.That(() => Parse(json)).Throws<ConfigurationPortabilityContractException>();
        await Assert.That(() => ModerationConfigurationDocumentProjection.Parse(
            Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"schemaVersion\":1}")))
            .Throws<ConfigurationPortabilityContractException>();
    }

    [Test]
    public async Task OversizeAndExcessiveDepthAreRejectedBeforeConversion()
    {
        await Assert.That(() => ModerationConfigurationDocumentProjection.Parse(
            new byte[ConfigurationPortabilityContentLimits.MaximumArtifactUtf8Bytes + 1]))
            .Throws<ConfigurationPortabilityContractException>();
        var deep = new string('[', 40) + "0" + new string(']', 40);
        await Assert.That(() => ModerationConfigurationDocumentProjection.Parse(Encoding.UTF8.GetBytes(deep)))
            .Throws<ConfigurationPortabilityContractException>();
    }

    [Test]
    public async Task OverBudgetCatalogueCannotBeConstructed()
    {
        var document = Document();
        await Assert.That(() => new ModerationConfigurationDocument(1, document.CatalogueRevision,
            document.Modes, Enumerable.Repeat(document.Classes[0], 257).ToArray(),
            document.Reasons, document.Profiles, document.SupportedProfiles))
            .Throws<ConfigurationPortabilityContractException>();
    }

    [Test]
    public async Task MissingAndExtraneousMappingsCannotConvert()
    {
        var document = Document();
        var missing = new ModerationConfigurationDestinationMapping(Id(30), 0, Id(31), Context(),
            new Dictionary<ModerationConfigurationReference, Guid>());
        await Assert.That(() => ModerationConfigurationDocumentProjection.ToProposedState(document, missing, Registry))
            .Throws<ConfigurationPortabilityContractException>();
        var extra = new ModerationConfigurationDestinationMapping(Id(30), 0, Id(31), Context(),
            new Dictionary<ModerationConfigurationReference, Guid>
            {
                [document.Profiles[0].Rule] = Id(200), [new("not.imported", 1)] = Id(201)
            });
        await Assert.That(() => ModerationConfigurationDocumentProjection.ToProposedState(document, extra, Registry))
            .Throws<ConfigurationPortabilityContractException>();
    }

    [Test]
    public async Task StaleDestinationRevisionFailsCompleteCompilerValidation()
    {
        var document = Document();
        var mapping = new ModerationConfigurationDestinationMapping(Id(30), 1, Id(31), Context(),
            new Dictionary<ModerationConfigurationReference, Guid> { [document.Profiles[0].Rule] = Id(200) });
        var result = new ModerationConfigurationDocumentValidator().ValidateOnly(document, mapping, Registry);
        await Assert.That(result.CanImport).IsFalse();
        await Assert.That(result.Compilation.Diagnostics.Any(issue => issue.Kind == ModerationPolicyDiagnosticKind.StaleBinding))
            .IsTrue();
    }

    [Test]
    public async Task ChangedRecipeCannotClaimEquivalentMeaning()
    {
        var document = Document();
        var profile = document.Profiles[0];
        var recipes = profile.Recipes.SetItem(0, profile.Recipes[0] with
        {
            PrivateDisclosure = ModerationPrivateDisclosureMode.FenceSelectedVenueAndAllDerivatives
        });
        var changed = ReplaceProfile(document, profile with { Recipes = recipes });
        var result = new ModerationConfigurationDocumentValidator().ValidateOnly(changed, Mapping(changed), Registry);
        await Assert.That(result.CanImport).IsFalse();
        await Assert.That(result.RecipesMatch).IsFalse();
    }

    [Test]
    public async Task IncompleteEffectsFailNativePolicyValidation()
    {
        var document = Document();
        var changed = ReplaceProfile(document, document.Profiles[0] with { Effects = [] });
        var result = new ModerationConfigurationDocumentValidator().ValidateOnly(changed, Mapping(changed), Registry);
        await Assert.That(result.CanImport).IsFalse();
        await Assert.That(result.Compilation.Outcome).IsEqualTo(ModerationPolicyCompilationOutcome.Invalid);
    }

    [Test]
    public async Task ValidateOnlyProducesDraftWithoutNativeAuthorityOrMutation()
    {
        var document = Document();
        var before = ModerationConfigurationDocumentProjection.Serialize(document);
        var validator = new ModerationConfigurationDocumentValidator();
        var first = validator.ValidateOnly(document, Mapping(document), Registry);
        var second = validator.ValidateOnly(document, Mapping(document), Registry);
        await Assert.That(first.Compilation.Outcome).IsEqualTo(ModerationPolicyCompilationOutcome.Draft);
        await Assert.That(first.Compilation.Draft!.Proposed.RequestedState).IsEqualTo(ModerationPolicyRequestedState.Draft);
        await Assert.That(second.Compilation.Diagnostics.SequenceEqual(first.Compilation.Diagnostics)).IsTrue();
        await Assert.That(ModerationConfigurationDocumentProjection.Serialize(document).SequenceEqual(before)).IsTrue();
    }

    [Test]
    public async Task InactiveDefinitionImportIsUsableWithoutClaimingConsumerReadiness()
    {
        var source = Document();
        var document = new ModerationConfigurationDocument(source.SchemaVersion, source.CatalogueRevision,
            source.Modes, source.Classes, source.Reasons, source.Profiles, []);
        var result = new ModerationConfigurationDocumentValidator().ValidateOnly(document, Mapping(document), Registry);
        await Assert.That(result.CanImport).IsTrue();
        await Assert.That(result.Compilation.Draft!.Proposed.SupportedProfiles.IsEmpty).IsTrue();
        await Assert.That(result.Compilation.Draft.Profiles.Length).IsEqualTo(1);
        await Assert.That(result.Compilation.Draft.Consumers.IsEmpty).IsTrue();
        await Assert.That(result.RecipesMatch).IsTrue();
    }

    [Test]
    public async Task UnsupportedSelectedSupportCannotBeReportedAsImportable()
    {
        var source = JsonNode.Parse(ModerationCapabilityRegistry.ReadInventory())!;
        // Leave the actual inventory intact except for withdrawing support from the selected mode.
        var mode = source["modes"]!.AsArray().Single(item => item!["mode"]!.GetValue<string>()
            == nameof(ModerationNativeMode.Embedded));
        mode!["supportState"] = nameof(ModerationSourceSupport.Unimplemented);
        var registry = ModerationCapabilityRegistry.Parse(source.ToJsonString());
        var document = Document();
        var result = new ModerationConfigurationDocumentValidator().ValidateOnly(document, Mapping(document), registry);
        await Assert.That(result.CanImport).IsFalse();
        await Assert.That(result.Compilation.Diagnostics.Any(item => item.Kind == ModerationPolicyDiagnosticKind.UnsupportedMode))
            .IsTrue();
        await Assert.That(result.Compilation.Outcome).IsEqualTo(ModerationPolicyCompilationOutcome.Draft);
    }

    [Test]
    public async Task MissingRecipeAndInvalidSemanticCodeCannotConvertSuccessfully()
    {
        var source = Document();
        var document = ReplaceProfile(source, source.Profiles[0] with { Recipes = [] });
        var result = new ModerationConfigurationDocumentValidator().ValidateOnly(document, Mapping(document), Registry);
        await Assert.That(result.CanImport).IsFalse();
        await Assert.That(result.RecipesMatch).IsFalse();
        document = ReplaceProfile(source, source.Profiles[0] with { Rule = new("not/a/semantic/code", 1) });
        await Assert.That(() => ModerationConfigurationDocumentProjection.ToProposedState(document, Mapping(document), Registry))
            .Throws<ConfigurationPortabilityContractException>();
    }

    [Test]
    public async Task FutureSchemaAndNestedAuthorityInRecipeAreRejected()
    {
        var json = JsonNode.Parse(ModerationConfigurationDocumentProjection.Serialize(Document()))!;
        json["schemaVersion"] = 2;
        await Assert.That(() => Parse(json)).Throws<ConfigurationPortabilityContractException>();
        json["schemaVersion"] = 1;
        json["profiles"]![0]!["recipes"]![0]!["approved"] = true;
        await Assert.That(() => Parse(json)).Throws<ConfigurationPortabilityContractException>();
    }

    [Test]
    public async Task EveryNonChatProfileRetainsAllRecipesAndProtectivePurposeThroughJson()
    {
        foreach (var id in Enum.GetValues<ModerationReferenceProfileId>())
        {
            var source = Document(id);
            var document = ModerationConfigurationDocumentProjection.Parse(
                ModerationConfigurationDocumentProjection.Serialize(source));
            var inactive = new ModerationConfigurationDocument(document.SchemaVersion, document.CatalogueRevision,
                document.Modes, document.Classes, document.Reasons, document.Profiles, []);
            var result = new ModerationConfigurationDocumentValidator().ValidateOnly(inactive, Mapping(inactive), Registry);
            await Assert.That(result.CanImport).IsTrue();
            await Assert.That(document.Profiles[0].Recipes.Length)
                .IsEqualTo(1 + ModerationReferenceCatalogue.GetProfile(id).Strategies.Count);
            await Assert.That(document.Profiles[0].ProtectionPurpose)
                .IsEqualTo(ModerationReferenceCatalogue.GetProfile(id).ProtectionPurpose);
        }
    }

    [Test]
    public async Task ReusedAndNonVersionSevenDestinationProfileIdentitiesAreRejected()
    {
        var source = Document();
        var second = source.Profiles[0] with { Rule = new("profile.second", 1) };
        var document = new ModerationConfigurationDocument(source.SchemaVersion, source.CatalogueRevision,
            source.Modes, source.Classes, source.Reasons, new[] { source.Profiles[0], second }, []);
        var reused = new ModerationConfigurationDestinationMapping(Id(30), 0, Id(31), Context(),
            new Dictionary<ModerationConfigurationReference, Guid>
            {
                [source.Profiles[0].Rule] = Id(200), [second.Rule] = Id(200)
            });
        await Assert.That(() => ModerationConfigurationDocumentProjection.ToProposedState(document, reused, Registry))
            .Throws<ConfigurationPortabilityContractException>();
        var invalid = new ModerationConfigurationDestinationMapping(Id(30), 0, Id(31), Context(),
            new Dictionary<ModerationConfigurationReference, Guid>
            {
                [source.Profiles[0].Rule] = Guid.Empty
            });
        await Assert.That(() => ModerationConfigurationDocumentProjection.ToProposedState(source, invalid, Registry))
            .Throws<ConfigurationPortabilityContractException>();
    }

    private static ModerationConfigurationDocument Document(ModerationReferenceProfileId id = ModerationReferenceProfileId.CFG06)
    {
        var reference = ModerationReferenceCatalogue.GetProfile(id);
        var response = reference.Responses[0];
        ModerationRuleReference Ref(string code) => ModerationRuleReference.Create(Source.Id, code, 1);
        var profile = new ModerationPolicyProfileDefinition(Id(100), Ref("profile.pause"), Ref("reason.pause"),
            reference.Id, reference.Revision, response.Grade, [reference.ScopeKinds.Max()],
            [reference.SubjectKinds.Order().First()], reference.Effects.ToImmutableArray(), reference.Bundle,
            reference.ProtectionPurpose, reference.Strategies.Order().ToImmutableArray(),
            new(response.Timing.Kind, response.Timing.Duration), response.RequiredApprovals, 6, reference.Recognition);
        var proposed = new ModerationPolicyProposedState(Id(1), 0, Id(2), Source,
            ModerationReferenceCatalogue.Revision, Registry.Revision, Registry.Fingerprint,
            ModerationPolicyRequestedState.Draft, [ModerationNativeMode.Embedded],
            [new(Ref("class.pause"), "Publication abuse", reference.Ground, reference.Basis)],
            [new(Ref("reason.pause"), "Confirmed publication abuse", Ref("class.pause"))],
            [profile], [profile.Rule]);
        var compiled = ModerationPolicyProposedStateCompiler.Compile(proposed, Context() with
        {
            Bounds = Context().Bounds with { Jurisdiction = Source }
        }, Registry);
        return ModerationConfigurationDocumentProjection.FromCompiledDraft(compiled.Draft!);
    }

    private static ModerationPolicyCompilationContext Context() => new(Id(20), Id(21), 1, "generation-1",
        new(Destination, Enum.GetValues<ModerationScopeKind>().ToImmutableArray(),
            Enum.GetValues<ModerationSubjectKind>().ToImmutableArray(),
            ModerationReferenceCatalogue.Profiles.Values.SelectMany(item => item.Effects).Distinct().ToImmutableArray(),
            Enum.GetValues<ModerationReferenceTimingKind>().ToImmutableArray(),
            TimeSpan.FromDays(365), TimeSpan.FromDays(90), 0, 2, 6), null);

    private static ModerationConfigurationDestinationMapping Mapping(ModerationConfigurationDocument document) =>
        new(Id(30), 0, Id(31), Context(),
            new Dictionary<ModerationConfigurationReference, Guid> { [document.Profiles[0].Rule] = Id(200) });

    private static ModerationConfigurationDocument ReplaceProfile(
        ModerationConfigurationDocument document, ModerationConfigurationProfile profile) =>
        new(document.SchemaVersion, document.CatalogueRevision, document.Modes, document.Classes,
            document.Reasons, new[] { profile }, document.SupportedProfiles);

    private static ModerationConfigurationDocument Parse(JsonNode json) =>
        ModerationConfigurationDocumentProjection.Parse(Encoding.UTF8.GetBytes(json.ToJsonString()));

    private static Guid Id(int value) => Guid.Parse($"019a0000-0000-7000-8000-{value:x12}");
}
