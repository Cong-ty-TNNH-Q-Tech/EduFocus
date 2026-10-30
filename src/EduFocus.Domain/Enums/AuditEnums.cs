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
    RecoveryCodeRegenerated,
    PrivacyConsentRecorded,
    OnboardingCompleted,
    LockoutTriggered,
    DataDeleted,
    TamperDetected,
    SessionInterrupted,
    SessionRecovered,
    WatchdogRestart,
    WatchdogGaveUp,
    SystemShutdown,
    CleanExit,
    ScheduleChanged,
    BlockRuleChanged,
    SettingsChanged,
    ClockJumpDetected,
    RetentionApplied,
    AuthenticationFailed,
    ProtectionServiceStarted,
    ProtectionServiceStopped
}
