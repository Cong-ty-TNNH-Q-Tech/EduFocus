namespace EduFocus.Domain.ValueObjects;

public readonly record struct AppSettingsId(Guid Value)
{
    public static AppSettingsId New() => new(Guid.NewGuid());
    public static AppSettingsId From(Guid value) => new(value);
    public static readonly AppSettingsId Empty = new(Guid.Empty);
    public bool IsEmpty => Value == Guid.Empty;
    public override string ToString() => Value.ToString();
}

public readonly record struct ParentCredentialId(Guid Value)
{
    public static ParentCredentialId New() => new(Guid.NewGuid());
    public static ParentCredentialId From(Guid value) => new(value);
    public static readonly ParentCredentialId Empty = new(Guid.Empty);
    public bool IsEmpty => Value == Guid.Empty;
    public override string ToString() => Value.ToString();
}

public readonly record struct BlockRuleId(Guid Value)
{
    public static BlockRuleId New() => new(Guid.NewGuid());
    public static BlockRuleId From(Guid value) => new(value);
    public static readonly BlockRuleId Empty = new(Guid.Empty);
    public bool IsEmpty => Value == Guid.Empty;
    public override string ToString() => Value.ToString();
}

public readonly record struct ScheduleWindowId(Guid Value)
{
    public static ScheduleWindowId New() => new(Guid.NewGuid());
    public static ScheduleWindowId From(Guid value) => new(value);
    public static readonly ScheduleWindowId Empty = new(Guid.Empty);
    public bool IsEmpty => Value == Guid.Empty;
    public override string ToString() => Value.ToString();
}

public readonly record struct StudySessionId(Guid Value)
{
    public static StudySessionId New() => new(Guid.NewGuid());
    public static StudySessionId From(Guid value) => new(value);
    public static readonly StudySessionId Empty = new(Guid.Empty);
    public bool IsEmpty => Value == Guid.Empty;
    public override string ToString() => Value.ToString();
}

public readonly record struct ActivitySampleId(Guid Value)
{
    public static ActivitySampleId New() => new(Guid.NewGuid());
    public static ActivitySampleId From(Guid value) => new(value);
    public static readonly ActivitySampleId Empty = new(Guid.Empty);
    public bool IsEmpty => Value == Guid.Empty;
    public override string ToString() => Value.ToString();
}

public readonly record struct AttentionEventId(Guid Value)
{
    public static AttentionEventId New() => new(Guid.NewGuid());
    public static AttentionEventId From(Guid value) => new(value);
    public static readonly AttentionEventId Empty = new(Guid.Empty);
    public bool IsEmpty => Value == Guid.Empty;
    public override string ToString() => Value.ToString();
}

public readonly record struct BlockedAttemptId(Guid Value)
{
    public static BlockedAttemptId New() => new(Guid.NewGuid());
    public static BlockedAttemptId From(Guid value) => new(value);
    public static readonly BlockedAttemptId Empty = new(Guid.Empty);
    public bool IsEmpty => Value == Guid.Empty;
    public override string ToString() => Value.ToString();
}

public readonly record struct AuditLogId(Guid Value)
{
    public static AuditLogId New() => new(Guid.NewGuid());
    public static AuditLogId From(Guid value) => new(value);
    public static readonly AuditLogId Empty = new(Guid.Empty);
    public bool IsEmpty => Value == Guid.Empty;
    public override string ToString() => Value.ToString();
}
