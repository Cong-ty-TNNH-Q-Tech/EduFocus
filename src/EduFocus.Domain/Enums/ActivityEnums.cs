namespace EduFocus.Domain.Enums;

public enum ActivityCategory
{
    Study,
    Distraction,
    Neutral,
    Unknown
}

public enum ActivityUrlSource
{
    UIA,
    Extension,
    None
}

public enum BlockTargetType
{
    Domain,
    App,
    Url
}

public enum BlockAttemptSource
{
    BlockPage,
    ProcessWatcher,
    Extension
}

public enum AuditActor
{
    Parent,
    System,
    Student
}
