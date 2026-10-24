using Explore.Application.Features.Moderation.Policy;
using Explore.Application.Services.Moderation;
using ISLAMU.Wire.Contracts.ConfigurationPortability;

namespace Explore.Application.Features.ConfigurationManifest.Moderation;

/// <summary>
/// Import eligibility is not activation authority. Unsupported destination effects/modes fail
/// closed even when an inactive draft can explain the proposed meaning.
/// </summary>
public sealed record ModerationConfigurationValidationResult(
    ModerationPolicyCompilationResult Compilation, bool RecipesMatch)
{
    public bool CanImport => RecipesMatch && Compilation.Outcome == ModerationPolicyCompilationOutcome.Draft
        && Compilation.Draft is not null
        && !Compilation.Diagnostics.Any(item => item.Severity is ModerationPolicyDiagnosticSeverity.Error
            or ModerationPolicyDiagnosticSeverity.ActivationBlocker);
}

/// <summary>
/// Manually instantiated pure validation: portable shape, explicit destination conversion, complete
/// native policy compiler, and exact recipe parity. No write, grant, receipt or activation is issued.
/// </summary>
public sealed class ModerationConfigurationDocumentValidator
{
    public ModerationConfigurationValidationResult ValidateOnly(
        ModerationConfigurationDocument document, ModerationConfigurationDestinationMapping mapping,
        ModerationCapabilityRegistry registry)
    {
        var proposed = ModerationConfigurationDocumentProjection.ToProposedState(document, mapping, registry);
        var compilation = ModerationPolicyProposedStateCompiler.Compile(proposed, mapping.Context, registry);
        var recipesMatch = compilation.Draft is { } draft && document.Profiles.All(portable =>
        {
            var native = draft.Profiles.Single(item => item.Definition.Rule.Code == portable.Rule.Code
                && item.Definition.Rule.Revision == portable.Rule.Revision);
            return portable.Recipes.SequenceEqual(ModerationConfigurationDocumentProjection.Recipes(native));
        });
        return new(compilation, recipesMatch);
    }

    internal static void ValidateShape(ModerationConfigurationDocument document)
    {
        if (document is null || document.SchemaVersion != ModerationConfigurationDocument.CurrentSchemaVersion
            || document.CatalogueRevision < 1
            || document.Classes.Count == 0 || document.Reasons.Count == 0 || document.Profiles.Count == 0
            || document.Classes.Any(item => item is null || !Reference(item.Reference) || !Text(item.Meaning))
            || document.Reasons.Any(item => item is null || !Reference(item.Reference)
                || !Reference(item.Class) || !Text(item.Meaning))
            || document.Profiles.Any(item => item is null || !Reference(item.Rule) || !Reference(item.Reason)
                || item.Timing is null || !Bounded(item.ScopeKinds) || !Bounded(item.SubjectKinds)
                || !Bounded(item.Effects) || !Bounded(item.Strategies) || !Bounded(item.Recipes)
                || item.Effects.Any(effect => effect is null) || item.Recipes.Any(recipe => recipe is null))
            || document.SupportedProfiles.Any(item => !Reference(item)))
            throw new ConfigurationPortabilityContractException(ConfigurationPortabilityDiagnosticCodes.ContractInvalid, "$");
    }

    private static bool Bounded<T>(System.Collections.Immutable.ImmutableArray<T> values) =>
        !values.IsDefault && values.Length <= ModerationConfigurationDocument.MaximumDefinitions;

    private static bool Text(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 4000;

    // Portable shape only. Native semantic code validation runs with the actual destination owner.
    private static bool Reference(ModerationConfigurationReference reference) =>
        reference is not null && Text(reference.Code) && reference.Revision > 0;
}
