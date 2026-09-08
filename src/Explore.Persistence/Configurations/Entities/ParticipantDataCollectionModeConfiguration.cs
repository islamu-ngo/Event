using Explore.Domain;

namespace Explore.Persistence.Configurations.Entities;

public sealed class ParticipantDataCollectionModeConfiguration : LookupConfiguration<ParticipantDataCollectionMode>
{
    protected override string TableName => "participant_data_collection_modes";
}
