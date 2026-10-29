using EduFocus.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace EduFocus.SqliteDataAccess.Converters;

public sealed class AppSettingsIdConverter : ValueConverter<AppSettingsId, Guid>
{
    public AppSettingsIdConverter() : base(id => id.Value, value => AppSettingsId.From(value)) { }
}

public sealed class ParentCredentialIdConverter : ValueConverter<ParentCredentialId, Guid>
{
    public ParentCredentialIdConverter() : base(id => id.Value, value => ParentCredentialId.From(value)) { }
}

public sealed class BlockRuleIdConverter : ValueConverter<BlockRuleId, Guid>
{
    public BlockRuleIdConverter() : base(id => id.Value, value => BlockRuleId.From(value)) { }
}

public sealed class ScheduleWindowIdConverter : ValueConverter<ScheduleWindowId, Guid>
{
    public ScheduleWindowIdConverter() : base(id => id.Value, value => ScheduleWindowId.From(value)) { }
}

public sealed class StudySessionIdConverter : ValueConverter<StudySessionId, Guid>
{
    public StudySessionIdConverter() : base(id => id.Value, value => StudySessionId.From(value)) { }
}

public sealed class ActivitySampleIdConverter : ValueConverter<ActivitySampleId, Guid>
{
    public ActivitySampleIdConverter() : base(id => id.Value, value => ActivitySampleId.From(value)) { }
}

public sealed class AttentionEventIdConverter : ValueConverter<AttentionEventId, Guid>
{
    public AttentionEventIdConverter() : base(id => id.Value, value => AttentionEventId.From(value)) { }
}

public sealed class BlockedAttemptIdConverter : ValueConverter<BlockedAttemptId, Guid>
{
    public BlockedAttemptIdConverter() : base(id => id.Value, value => BlockedAttemptId.From(value)) { }
}

public sealed class AuditLogIdConverter : ValueConverter<AuditLogId, Guid>
{
    public AuditLogIdConverter() : base(id => id.Value, value => AuditLogId.From(value)) { }
}
