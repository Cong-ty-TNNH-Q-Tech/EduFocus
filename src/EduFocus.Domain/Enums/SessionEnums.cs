namespace EduFocus.Domain.Enums;

public enum StudySessionStatus
{
    Active,
    Completed,
    Aborted,
    Interrupted
}

public enum SessionTrigger
{
    Manual,
    Schedule,
    Recovery
}

public enum ActivityCategory
{
    Study,
    Distraction,
    Neutral,
    Unknown
}

public enum UrlSource
{
    UIA,
    Extension,
    None
}
