namespace EduFocus.Domain.Enums;

public enum AuditActor
{
    Parent,
    System,
    Student
}

public enum AuditEventType
{
    ParentLogin,
    ParentLogout,
    ProtectionOn,
    ProtectionOff,
    PinSetup,
    PinChanged,
    RecoveryCodeUsed,
    LockoutTriggered,
    DataDeleted,
    TamperDetected,
    SessionInterrupted,
    SessionRecovered,
    WatchdogRestart,
    SystemShutdown,
    CleanExit,
    ScheduleChanged,
    BlockRuleChanged,
    SettingsChanged,
    ClockJumpDetected,
    RetentionApplied,
    AuthenticationFailed
}
