using Explore.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence;

public partial class ExploreDbContext
{
    // A capture operation is a narrowly scoped maintenance authority for one erasure subject.
    // Parent model composition calls this helper; no ordinary tenant query receives these rows.
    internal Guid? ModerationRecognitionSubjectId { get; set; }

    private void ApplyModerationRecognitionQueryFilters(ModelBuilder builder)
    {
        builder.Entity<ModerationRecognitionPending>().HasQueryFilter("ModerationRecognition",
            row => ModerationRecognitionSubjectId != null && row.SubjectId == ModerationRecognitionSubjectId);
        builder.Entity<ModerationRecognitionCaptureItem>().HasQueryFilter("ModerationRecognition",
            row => ModerationRecognitionSubjectId != null && row.SubjectId == ModerationRecognitionSubjectId);
    }
}
