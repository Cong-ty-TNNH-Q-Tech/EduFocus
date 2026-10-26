namespace EduFocus.Domain.Enums;

public enum StudySessionStatus
{
    Active,
    Completed,
    Aborted,
    Interrupted
}

public enum StudySessionTrigger
{
    Manual,
    Schedule,
    Recovery
}
