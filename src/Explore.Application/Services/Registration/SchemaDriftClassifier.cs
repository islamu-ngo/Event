using Explore.Application.Contracts.Services.Registration;

namespace Explore.Application.Services.Registration;

public sealed class SchemaDriftClassifier
{
    public RegistrationProviderSchemaDriftClass Classify(
        RegistrationProviderSchemaSnapshot previous,
        RegistrationProviderSchemaSnapshot current)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);

        if (previous.Fields.Select(field => field.Key).Distinct(StringComparer.Ordinal).Count() != previous.Fields.Count ||
            current.Fields.Select(field => field.Key).Distinct(StringComparer.Ordinal).Count() != current.Fields.Count)
        {
            return RegistrationProviderSchemaDriftClass.UnsupportedChange;
        }

        Dictionary<string, RegistrationProviderSchemaFieldSnapshot> oldFields = previous.Fields.ToDictionary(field => field.Key, StringComparer.Ordinal);
        Dictionary<string, RegistrationProviderSchemaFieldSnapshot> newFields = current.Fields.ToDictionary(field => field.Key, StringComparer.Ordinal);

        bool labelOnly = false;
        bool additiveOptional = false;
        foreach ((string key, RegistrationProviderSchemaFieldSnapshot oldField) in oldFields)
        {
            if (!newFields.TryGetValue(key, out RegistrationProviderSchemaFieldSnapshot? newField))
            {
                return oldField.IsRequired
                    ? RegistrationProviderSchemaDriftClass.RequiredFieldRemoved
                    : RegistrationProviderSchemaDriftClass.MappingRequired;
            }

            if (!StringComparer.Ordinal.Equals(oldField.Type, newField.Type))
            {
                return RegistrationProviderSchemaDriftClass.TypeChanged;
            }

            if (!oldField.IsRequired && newField.IsRequired)
            {
                return RegistrationProviderSchemaDriftClass.MappingRequired;
            }

            if (oldField.IsRequired && !newField.IsRequired)
            {
                additiveOptional = true;
            }

            if (oldField.Options.Select(option => option.Key).Order(StringComparer.Ordinal)
                .SequenceEqual(newField.Options.Select(option => option.Key).Order(StringComparer.Ordinal)) is false)
            {
                return RegistrationProviderSchemaDriftClass.OptionSetChanged;
            }

            if (!StringComparer.Ordinal.Equals(oldField.Label, newField.Label) ||
                oldField.Options.Any(oldOption => !StringComparer.Ordinal.Equals(
                    oldOption.Label,
                    newField.Options.Single(option => option.Key == oldOption.Key).Label)))
            {
                labelOnly = true;
            }
        }

        foreach (RegistrationProviderSchemaFieldSnapshot added in newFields.Values.Where(field => !oldFields.ContainsKey(field.Key)))
        {
            if (added.IsRequired)
            {
                return RegistrationProviderSchemaDriftClass.MappingRequired;
            }

            additiveOptional = true;
        }

        if (additiveOptional)
        {
            return RegistrationProviderSchemaDriftClass.AdditiveOptionalChange;
        }

        return labelOnly ? RegistrationProviderSchemaDriftClass.LabelOnlyChange : RegistrationProviderSchemaDriftClass.NoDrift;
    }

    public static bool BlocksPublication(RegistrationProviderSchemaDriftClass driftClass) => driftClass is
        RegistrationProviderSchemaDriftClass.MappingRequired or
        RegistrationProviderSchemaDriftClass.RequiredFieldRemoved or
        RegistrationProviderSchemaDriftClass.TypeChanged or
        RegistrationProviderSchemaDriftClass.OptionSetChanged or
        RegistrationProviderSchemaDriftClass.UnsupportedChange;
}
