using System.Text.Json;
using EduFocus.Domain.Attention;
using EduFocus.Domain.Enums;
using EduFocus.Domain.ValueObjects;

namespace EduFocus.Domain.Entities;

public sealed class AppSettings
{
    public static readonly Guid SingletonId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private Guid _id = SingletonId;
    public Guid Id => _id;
    public ProtectionState Protection { get; private set; }
    public bool MonitoringEnabled { get; private set; }
    public bool ScheduleEnabled { get; private set; }
    public bool MonitorOnlyDuringClasses { get; private set; }
    public string? CameraDeviceId { get; private set; }
    public int DistractionThresholdSeconds { get; private set; } = 20;
    public int AbsenceThresholdSeconds { get; private set; } = 30;
    public int SampleIntervalSeconds { get; private set; } = 15;
    public int ActivityRetentionDays { get; private set; } = 30;
    public int EventRetentionDays { get; private set; } = 90;
    public int SessionRetentionDays { get; private set; } = 365;
    public bool StartWithWindows { get; private set; }
    public ThemePreference Theme { get; private set; } = ThemePreference.Dark;
    public LanguagePreference Language { get; private set; } = LanguagePreference.Vietnamese;
    public bool OnboardingCompleted { get; private set; }
    public DateTimeOffset? ScheduleOverrideUntilUtc { get; private set; }
    public DateTimeOffset? LastCleanShutdownAtUtc { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    private AppSettings() { }
    private AppSettings(DateTimeOffset now) { CreatedAtUtc = UpdatedAtUtc = Utc.Require(now); }
    public static AppSettings Create(DateTimeOffset now) => new(now);
    public void SetProtection(ProtectionState state, DateTimeOffset now) { EnumGuard.Defined(state); Protection = state; Touch(now); }
    public void Configure(bool monitoringEnabled, bool scheduleEnabled, bool monitorOnlyDuringClasses,
        string? cameraDeviceId, int distractionThresholdSeconds, int absenceThresholdSeconds, int sampleIntervalSeconds,
        int activityRetentionDays, int eventRetentionDays, int sessionRetentionDays, bool startWithWindows,
        ThemePreference theme, LanguagePreference language, bool onboardingCompleted, DateTimeOffset now)
    {
        EnumGuard.Defined(theme); EnumGuard.Defined(language);
        if (distractionThresholdSeconds <= 0 || absenceThresholdSeconds <= 0 || sampleIntervalSeconds <= 0 || activityRetentionDays <= 0 || eventRetentionDays <= 0 || sessionRetentionDays <= 0) throw new ArgumentOutOfRangeException(nameof(sampleIntervalSeconds), "Threshold and retention values must be positive.");
        CameraDeviceId = TextLimit.Optional(cameraDeviceId, 256); MonitoringEnabled = monitoringEnabled; ScheduleEnabled = scheduleEnabled; MonitorOnlyDuringClasses = monitorOnlyDuringClasses;
        DistractionThresholdSeconds = distractionThresholdSeconds; AbsenceThresholdSeconds = absenceThresholdSeconds; SampleIntervalSeconds = sampleIntervalSeconds;
        ActivityRetentionDays = activityRetentionDays; EventRetentionDays = eventRetentionDays; SessionRetentionDays = sessionRetentionDays; StartWithWindows = startWithWindows;
        Theme = theme; Language = language; OnboardingCompleted = onboardingCompleted; Touch(now);
    }
    public void CompleteOnboarding(DateTimeOffset now) { OnboardingCompleted = true; Touch(now); }
    public void SetScheduleOverride(DateTimeOffset? untilUtc, DateTimeOffset now) { if (untilUtc is { } v) Utc.Require(v); ScheduleOverrideUntilUtc = untilUtc; Touch(now); }
    public void RecordCleanShutdown(DateTimeOffset now) { LastCleanShutdownAtUtc = Utc.Require(now); Protection = ProtectionState.Off; Touch(now); }
    private void Touch(DateTimeOffset now) => UpdatedAtUtc = Utc.Require(now);
}

public sealed class ParentCredentials
{
    public static readonly Guid SingletonId = Guid.Parse("00000000-0000-0000-0000-000000000002");
    public Guid Id { get; private set; }
    public CredentialHash Pin { get; private set; }
    public CredentialHash RecoveryCode { get; private set; }
    public int FailedAttempts { get; private set; }
    public DateTimeOffset? LockoutUntilUtc { get; private set; }
    public DateTimeOffset PinUpdatedAtUtc { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    private ParentCredentials() { Pin = null!; RecoveryCode = null!; }
    private ParentCredentials(Guid id, CredentialHash pin, CredentialHash recovery, DateTimeOffset now)
    { Id = id; Pin = pin; RecoveryCode = recovery; CreatedAtUtc = PinUpdatedAtUtc = Utc.Require(now); }
    public static ParentCredentials Create(CredentialHash pin, CredentialHash recovery, DateTimeOffset now) { ArgumentNullException.ThrowIfNull(pin); ArgumentNullException.ThrowIfNull(recovery); return new(SingletonId, pin, recovery, now); }
    public bool IsLockedOut(DateTimeOffset now) => LockoutUntilUtc is { } until && Utc.Require(now) < until;
    public void RecordFailedAttempt(DateTimeOffset now)
    {
        now = Utc.Require(now); if (IsLockedOut(now)) throw new InvalidOperationException("Credential entry is locked out.");
        if (LockoutUntilUtc is not null) { FailedAttempts = 0; LockoutUntilUtc = null; }
        FailedAttempts = checked(FailedAttempts + 1); if (FailedAttempts >= 5) { FailedAttempts = 5; LockoutUntilUtc = now.AddMinutes(15); }
    }
    public void RecordSuccessfulAuthentication(DateTimeOffset now) { Utc.Require(now); FailedAttempts = 0; LockoutUntilUtc = null; }
    public void ChangePin(CredentialHash newPin, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(newPin); if (Pin.SameHash(newPin)) throw new ArgumentException("A changed PIN must use a new hash and salt.", nameof(newPin));
        Pin = newPin; PinUpdatedAtUtc = Utc.Require(now); FailedAttempts = 0; LockoutUntilUtc = null;
    }
    public void ChangeRecoveryCode(CredentialHash newCode) { ArgumentNullException.ThrowIfNull(newCode); if (RecoveryCode.SameHash(newCode)) throw new ArgumentException("A changed recovery code must use a new hash and salt.", nameof(newCode)); RecoveryCode = newCode; FailedAttempts = 0; LockoutUntilUtc = null; }
}

public sealed class BlockRule
{
    public Guid Id { get; private set; }
    public BlockRuleType RuleType { get; private set; }
    public BlockMatchType MatchType { get; private set; }
    public string Value { get; private set; }
    public string NormalizedValue { get; private set; }
    public BlockRuleCategory Category { get; private set; }
    public BlockRuleEffect Effect { get; private set; }
    public bool IsEnabled { get; private set; }
    public bool IsBuiltIn { get; private set; }
    public RuleEnforcementStatus EnforcementStatus { get; private set; }
    public DateTimeOffset? ExpiresAtUtc { get; private set; }
    public string? Note { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    private BlockRule() { Value = NormalizedValue = string.Empty; }
    private BlockRule(Guid id, BlockRuleType type, BlockMatchType match, string value, string normalized, BlockRuleCategory category, BlockRuleEffect effect, bool enabled, bool builtIn, RuleEnforcementStatus enforcement, DateTimeOffset? expires, string? note, DateTimeOffset now)
    { Id=id; RuleType=type; MatchType=match; Value=value; NormalizedValue=normalized; Category=category; Effect=effect; IsEnabled=enabled; IsBuiltIn=builtIn; EnforcementStatus=enforcement; ExpiresAtUtc=expires; Note=note; CreatedAtUtc=UpdatedAtUtc=Utc.Require(now); }
    public static BlockRule Create(BlockRuleType type, BlockMatchType match, string value, BlockRuleCategory category, BlockRuleEffect effect, DateTimeOffset now, bool enabled=true, bool builtIn=false, DateTimeOffset? expiresAtUtc=null, string? note=null)
    {
        EnumGuard.Defined(type); EnumGuard.Defined(match); EnumGuard.Defined(category); EnumGuard.Defined(effect);
        value = TextLimit.Required(value,512,nameof(value)); var normalized = Normalize(type,value);
        var enforceable = type is BlockRuleType.Domain or BlockRuleType.App && match == BlockMatchType.Exact;
        return new(Guid.NewGuid(),type,match,value,normalized,category,effect,enabled,builtIn,enforceable?RuleEnforcementStatus.Enforced:RuleEnforcementStatus.NotEnforced,expiresAtUtc is { } e ? Utc.Require(e) : null,TextLimit.Optional(note,512),now);
    }
    public void SetEnabled(bool enabled, DateTimeOffset now) { IsEnabled=enabled; UpdatedAtUtc=Utc.Require(now); }
    public void SetExpiry(DateTimeOffset? expiry, DateTimeOffset now) { ExpiresAtUtc=expiry is { } e?Utc.Require(e):null; UpdatedAtUtc=Utc.Require(now); }
    public bool IsApplicable(DateTimeOffset now) => IsEnabled && EnforcementStatus == RuleEnforcementStatus.Enforced && (ExpiresAtUtc is null || ExpiresAtUtc > Utc.Require(now));
    private static string Normalize(BlockRuleType type,string value)
    {
        var input=value.Trim();
        if(type == BlockRuleType.Domain)
        {
            if(Uri.TryCreate(input.Contains("://",StringComparison.Ordinal)?input:"https://"+input,UriKind.Absolute,out var uri)) input=uri.Host;
            input=input.TrimEnd('.').ToLowerInvariant();
            if(input.Length==0 || input.Any(char.IsWhiteSpace)) throw new ArgumentException("Domain rule must contain a valid host.",nameof(value));
            return input;
        }
        if(type == BlockRuleType.App) return input.ToLowerInvariant();
        return input.ToLowerInvariant();
    }
    public static BlockDecision Evaluate(IEnumerable<BlockRule> rules, BlockRuleType type, string candidate, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(rules); EnumGuard.Defined(type); var normalized=Normalize(type,candidate);
        var matches=rules.Where(r=>r.RuleType==type && r.MatchType==BlockMatchType.Exact && r.IsApplicable(now) && r.NormalizedValue==normalized).ToArray();
        if(matches.Any(r=>r.Effect==BlockRuleEffect.Allow)) return BlockDecision.Allow;
        return matches.Any(r=>r.Effect==BlockRuleEffect.Block)?BlockDecision.Block:BlockDecision.None;
    }
    public static void EnsureUnique(IEnumerable<BlockRule> rules)
    { var duplicate=rules.GroupBy(r=>(r.RuleType,r.MatchType,r.NormalizedValue,r.Effect)).FirstOrDefault(g=>g.Count()>1); if(duplicate is not null) throw new InvalidOperationException("Duplicate block rule for the same type, match, value and effect."); }
}

public sealed class ScheduleWindow
{
    public Guid Id { get; private set; }
    public int DaysMask { get; private set; }
    public int StartMinutes { get; private set; }
    public int EndMinutes { get; private set; }
    public string? Label { get; private set; }
    public bool IsEnabled { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    private ScheduleWindow() { }
    private ScheduleWindow(int days,int start,int end,string? label,bool enabled,DateTimeOffset now) { Id=Guid.NewGuid(); DaysMask=days; StartMinutes=start; EndMinutes=end; Label=label; IsEnabled=enabled; CreatedAtUtc=UpdatedAtUtc=Utc.Require(now); }
    public static ScheduleWindow Create(int daysMask,int startMinutes,int endMinutes,DateTimeOffset now,string? label=null,bool enabled=true)
    { if(daysMask is <1 or >127) throw new ArgumentOutOfRangeException(nameof(daysMask)); if(startMinutes is <0 or >1439) throw new ArgumentOutOfRangeException(nameof(startMinutes)); if(endMinutes is <1 or >1440 || endMinutes<=startMinutes) throw new ArgumentOutOfRangeException(nameof(endMinutes)); return new(daysMask,startMinutes,endMinutes,TextLimit.Optional(label,100),enabled,now); }
    public void Update(int daysMask,int startMinutes,int endMinutes,string? label,bool enabled,DateTimeOffset now)
    {
        if(daysMask is <1 or >127) throw new ArgumentOutOfRangeException(nameof(daysMask));
        if(startMinutes is <0 or >1439) throw new ArgumentOutOfRangeException(nameof(startMinutes));
        if(endMinutes is <1 or >1440 || endMinutes<=startMinutes) throw new ArgumentOutOfRangeException(nameof(endMinutes));
        DaysMask=daysMask; StartMinutes=startMinutes; EndMinutes=endMinutes;
        Label=TextLimit.Optional(label,100); IsEnabled=enabled; UpdatedAtUtc=Utc.Require(now);
    }
    public static void EnsureNoOverlap(IEnumerable<ScheduleWindow> windows)
    {
        var active=windows.Where(w=>w.IsEnabled).ToArray();
        for(var i=0;i<active.Length;i++) for(var j=i+1;j<active.Length;j++)
            if((active[i].DaysMask & active[j].DaysMask)!=0 && active[i].StartMinutes<active[j].EndMinutes && active[j].StartMinutes<active[i].EndMinutes)
                throw new InvalidOperationException("Enabled schedule windows overlap on at least one day.");
    }

    public bool AppliesAt(DateTime localTime)
    {
        var dayBit = 1 << (((int)localTime.DayOfWeek + 6) % 7);
        var minute = localTime.Hour * 60 + localTime.Minute;
        return IsEnabled && (DaysMask & dayBit) != 0 && minute >= StartMinutes && minute < EndMinutes;
    }
}

public sealed class StudySession
{
    public Guid Id { get; private set; }
    public DateTimeOffset StartedAtUtc { get; private set; }
    public DateTimeOffset? EndedAtUtc { get; private set; }
    public DateTimeOffset LastHeartbeatAtUtc { get; private set; }
    public DateOnly StartLocalDate { get; private set; }
    public StudySessionStatus Status { get; private set; } = StudySessionStatus.Active;
    public StudySessionTrigger Trigger { get; private set; }
    public bool MonitoringEnabled { get; private set; }
    public long ActiveSeconds { get; private set; }
    public long FocusedSeconds { get; private set; }
    public long DistractedSeconds { get; private set; }
    public long AbsentSeconds { get; private set; }
    public long DrowsySeconds { get; private set; }
    public long CameraUnavailableSeconds { get; private set; }
    public long IdleSeconds { get; private set; }
    public long StudySeconds { get; private set; }
    public long DistractionSeconds { get; private set; }
    public long NeutralSeconds { get; private set; }
    public long OnlineClassSeconds { get; private set; }
    public long MonitoredSeconds { get; private set; }
    public long MonitoringEligibleSeconds { get; private set; }
    public double? AttentionPercent { get; private set; }
    public double? CoveragePercent { get; private set; }
    public double? ConfidenceAvg { get; private set; }
    public int BlockedAttemptCount { get; private set; }
    public string? Note { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    private long ConfidenceWeight; private double ConfidenceWeighted;
    private StudySession() { }
    private StudySession(StudySessionTrigger trigger,bool monitoring,DateOnly localDate,DateTimeOffset now)
    { Id=Guid.NewGuid(); Trigger=trigger; MonitoringEnabled=monitoring; StartLocalDate=localDate; StartedAtUtc=LastHeartbeatAtUtc=CreatedAtUtc=UpdatedAtUtc=Utc.Require(now); }
    public static StudySession Start(IEnumerable<StudySession>? existing,StudySessionTrigger trigger,bool monitoring,DateOnly localDate,DateTimeOffset now)
    { EnumGuard.Defined(trigger); if(existing?.Any(s=>s.Status==StudySessionStatus.Active)==true) throw new InvalidOperationException("Only one study session may be active."); return new(trigger,monitoring,localDate,now); }
    public void Heartbeat(DateTimeOffset now) { EnsureActive(); now=Utc.Require(now); if(now<LastHeartbeatAtUtc) throw new ArgumentOutOfRangeException(nameof(now)); LastHeartbeatAtUtc=UpdatedAtUtc=now; }
    public void AddActiveSeconds(long seconds,DateTimeOffset now) { EnsureActive(); NonNegative(seconds,nameof(seconds)); ActiveSeconds=checked(ActiveSeconds+seconds); Heartbeat(now); }
    public void AddMonitoringEligibleSeconds(long seconds,DateTimeOffset now) { EnsureActive(); NonNegative(seconds,nameof(seconds)); MonitoringEligibleSeconds=checked(MonitoringEligibleSeconds+seconds); RefreshSummary(); Heartbeat(now); }
    public void AddAttention(AttentionState state,long seconds,double? confidence,DateTimeOffset now)
    {
        EnsureActive(); NonNegative(seconds,nameof(seconds)); if(confidence is <0 or >1) throw new ArgumentOutOfRangeException(nameof(confidence));
        switch(state){case AttentionState.Focused: FocusedSeconds=checked(FocusedSeconds+seconds); break; case AttentionState.Distracted: DistractedSeconds=checked(DistractedSeconds+seconds); break; case AttentionState.Absent: AbsentSeconds=checked(AbsentSeconds+seconds); break; case AttentionState.Drowsy: DrowsySeconds=checked(DrowsySeconds+seconds); break; case AttentionState.CameraUnavailable: CameraUnavailableSeconds=checked(CameraUnavailableSeconds+seconds); break; default: throw new ArgumentOutOfRangeException(nameof(state));}
        if(state is not AttentionState.CameraUnavailable) MonitoredSeconds=checked(MonitoredSeconds+seconds);
        if(confidence is { } c && seconds>0){ConfidenceWeight=checked(ConfidenceWeight+seconds);ConfidenceWeighted+=c*seconds;} RefreshSummary(); Heartbeat(now);
    }
    public void AddActivity(ActivityCategory category,long seconds,bool onlineClass,DateTimeOffset now)
    { EnsureActive(); NonNegative(seconds,nameof(seconds)); switch(category){case ActivityCategory.Study:StudySeconds=checked(StudySeconds+seconds);break;case ActivityCategory.Distraction:DistractionSeconds=checked(DistractionSeconds+seconds);break;case ActivityCategory.Neutral:case ActivityCategory.Unknown:NeutralSeconds=checked(NeutralSeconds+seconds);break;default:throw new ArgumentOutOfRangeException(nameof(category));} if(onlineClass)OnlineClassSeconds=checked(OnlineClassSeconds+seconds); Heartbeat(now); }
    public void AddIdleSeconds(long seconds,DateTimeOffset now){EnsureActive();NonNegative(seconds,nameof(seconds));IdleSeconds=checked(IdleSeconds+seconds);Heartbeat(now);}
    public void RecordBlockedAttempt(DateTimeOffset now){EnsureActive();BlockedAttemptCount=checked(BlockedAttemptCount+1);Heartbeat(now);}
    public void SetNote(string? note,DateTimeOffset now){Note=TextLimit.Optional(note,512);UpdatedAtUtc=Utc.Require(now);}
    public void Complete(DateTimeOffset now)=>End(StudySessionStatus.Completed,now,true);
    public void Abort(DateTimeOffset now)=>End(StudySessionStatus.Aborted,now,true);
    public void InterruptAtLastHeartbeat(){End(StudySessionStatus.Interrupted,LastHeartbeatAtUtc,false);}
    private void End(StudySessionStatus status,DateTimeOffset now,bool heartbeat)
    { EnsureActive(); now=Utc.Require(now); if(now<LastHeartbeatAtUtc) throw new ArgumentOutOfRangeException(nameof(now)); Status=status; EndedAtUtc=now; if(heartbeat)LastHeartbeatAtUtc=now; UpdatedAtUtc=now; }
    private void EnsureActive(){if(Status!=StudySessionStatus.Active)throw new InvalidOperationException("Session has already ended.");}
    private void RefreshSummary(){AttentionPercent=MonitoredSeconds==0?null:(double)FocusedSeconds/MonitoredSeconds*100;CoveragePercent=MonitoringEligibleSeconds==0?null:(double)MonitoredSeconds/MonitoringEligibleSeconds*100;ConfidenceAvg=ConfidenceWeight==0?null:ConfidenceWeighted/ConfidenceWeight;}
    private static void NonNegative(long value,string name){if(value<0)throw new ArgumentOutOfRangeException(name);}
}

public sealed class ActivitySample
{
    public Guid Id { get; private set; } = Guid.NewGuid(); public Guid SessionId { get; private set; } public DateTimeOffset SampledAtUtc { get; private set; } public long DurationSeconds { get; private set; }
    public string? AppName { get; private set; } public string? ProcessName { get; private set; } public string? WindowTitle { get; private set; } public string? Url { get; private set; } public string? Domain { get; private set; }
    public ActivityUrlSource UrlSource { get; private set; } public ActivityCategory Category { get; private set; } public bool IsForeground { get; private set; } public bool IsOnlineClass { get; private set; } public long IdleSeconds { get; private set; } public DateTimeOffset CreatedAtUtc { get; private set; }
    private ActivitySample() { }
    private ActivitySample(Guid sid,DateTimeOffset time,long duration,string? app,string? process,string? title,string? url,string? domain,ActivityUrlSource source,ActivityCategory category,bool foreground,bool online,long idle,DateTimeOffset created)
    { if(sid==Guid.Empty)throw new ArgumentException("Session ID cannot be empty.",nameof(sid)); if(duration<0||idle<0)throw new ArgumentOutOfRangeException(nameof(duration)); SessionId=sid;SampledAtUtc=Utc.Require(time);DurationSeconds=duration;AppName=TextLimit.Optional(app,128);ProcessName=TextLimit.Optional(process,128);WindowTitle=TextLimit.Optional(title,512);Url=SanitizeUrl(url);Domain=TextLimit.Optional(domain,256)?.Trim().TrimEnd('.').ToLowerInvariant();UrlSource=source;Category=category;IsForeground=foreground;IsOnlineClass=online;IdleSeconds=idle;CreatedAtUtc=Utc.Require(created); }
    public static ActivitySample Create(Guid sessionId,DateTimeOffset sampledAtUtc,long durationSeconds,DateTimeOffset createdAtUtc,ActivityCategory category=ActivityCategory.Unknown,ActivityUrlSource urlSource=ActivityUrlSource.None,string? appName=null,string? processName=null,string? windowTitle=null,string? url=null,string? domain=null,bool isForeground=true,bool isOnlineClass=false,long idleSeconds=0)
    { EnumGuard.Defined(category);EnumGuard.Defined(urlSource);return new(sessionId,sampledAtUtc,durationSeconds,appName,processName,windowTitle,url,domain,urlSource,category,isForeground,isOnlineClass,idleSeconds,createdAtUtc); }
    private static string? SanitizeUrl(string? value){if(string.IsNullOrWhiteSpace(value))return null;if(!Uri.TryCreate(value,UriKind.Absolute,out var uri)||!(uri.Scheme is "http" or "https"))throw new ArgumentException("URL must be absolute HTTP(S).",nameof(value));var builder=new UriBuilder(uri){Query="",Fragment=""};return TextLimit.Optional(builder.Uri.ToString(),2048);}
}

public sealed class AttentionEvent
{
    public Guid Id { get; private set; }=Guid.NewGuid(); public Guid SessionId { get; private set; } public AttentionState State { get; private set; } public DateTimeOffset StartedAtUtc { get; private set; } public DateTimeOffset? EndedAtUtc { get; private set; } public long DurationSeconds { get; private set; } public double? Confidence { get; private set; } public string? AppName { get; private set; } public string? Domain { get; private set; } public string? Note { get; private set; } public DateTimeOffset CreatedAtUtc { get; private set; }
    private AttentionEvent() { }
    private AttentionEvent(Guid sid,AttentionState state,DateTimeOffset start,long duration,double? confidence,string? app,string? domain,string? note,DateTimeOffset created)
    { if(sid==Guid.Empty)throw new ArgumentException("Session ID cannot be empty.",nameof(sid)); if(duration<0)throw new ArgumentOutOfRangeException(nameof(duration)); if(confidence is <0 or >1)throw new ArgumentOutOfRangeException(nameof(confidence)); EnumGuard.Defined(state);SessionId=sid;State=state;StartedAtUtc=Utc.Require(start);DurationSeconds=duration;Confidence=confidence;AppName=TextLimit.Optional(app,128);Domain=TextLimit.Optional(domain,256);Note=TextLimit.Optional(note,256);CreatedAtUtc=Utc.Require(created); }
    public static AttentionEvent Start(Guid sid,AttentionState state,DateTimeOffset startedAtUtc,DateTimeOffset createdAtUtc,double? confidence=null,string? appName=null,string? domain=null,string? note=null)=>new(sid,state,startedAtUtc,0,confidence,appName,domain,note,createdAtUtc);
    public void Close(DateTimeOffset endedAtUtc){if(EndedAtUtc is not null)throw new InvalidOperationException("Attention event is already closed.");var end=Utc.Require(endedAtUtc);if(end<StartedAtUtc)throw new ArgumentOutOfRangeException(nameof(endedAtUtc));DurationSeconds=(long)(end-StartedAtUtc).TotalSeconds;EndedAtUtc=end;}
}

public sealed class BlockedAttempt
{
    public Guid Id { get; private set; }=Guid.NewGuid(); public Guid? SessionId { get; private set; } public DateTimeOffset OccurredAtUtc { get; private set; } public BlockTargetType TargetType { get; private set; } public string TargetValue { get; private set; } public string? Domain { get; private set; } public string? Url { get; private set; } public string? ProcessName { get; private set; } public string? WindowTitle { get; private set; } public BlockAttemptSource ObservedVia { get; private set; } public DateTimeOffset CreatedAtUtc { get; private set; }
    private BlockedAttempt() { TargetValue = string.Empty; }
    private BlockedAttempt(Guid? sid,DateTimeOffset occurred,BlockTargetType type,string target,string? domain,string? url,string? process,string? title,BlockAttemptSource source,DateTimeOffset created)
    { if(sid==Guid.Empty)throw new ArgumentException("Session ID cannot be empty.",nameof(sid));EnumGuard.Defined(type);EnumGuard.Defined(source);SessionId=sid;OccurredAtUtc=Utc.Require(occurred);TargetType=type;TargetValue=TextLimit.Required(target,512,nameof(target));Domain=TextLimit.Optional(domain,256)?.Trim().TrimEnd('.').ToLowerInvariant();Url=ActivitySample.Create(Guid.NewGuid(),occurred,0,created,url:url).Url;ProcessName=TextLimit.Optional(process,128);WindowTitle=TextLimit.Optional(title,512);ObservedVia=source;CreatedAtUtc=Utc.Require(created); }
    public static BlockedAttempt Create(Guid? sessionId,DateTimeOffset occurredAtUtc,BlockTargetType targetType,string targetValue,BlockAttemptSource observedVia,DateTimeOffset createdAtUtc,string? domain=null,string? url=null,string? processName=null,string? windowTitle=null)=>new(sessionId,occurredAtUtc,targetType,targetValue,domain,url,processName,windowTitle,observedVia,createdAtUtc);
}

public sealed class AuditLog
{
    public Guid Id { get; private set; }=Guid.NewGuid(); public DateTimeOffset OccurredAtUtc { get; private set; } public AuditActor Actor { get; private set; } public string EventType { get; private set; } public string Description { get; private set; } public string? MetadataJson { get; private set; } public Guid? CorrelationId { get; private set; } public DateTimeOffset CreatedAtUtc { get; private set; }
    private AuditLog() { EventType = Description = string.Empty; }
    private AuditLog(DateTimeOffset occurred,AuditActor actor,string eventType,string description,string? metadata,Guid? correlation,DateTimeOffset created)
    {EnumGuard.Defined(actor);OccurredAtUtc=Utc.Require(occurred);Actor=actor;EventType=TextLimit.Required(eventType,64,nameof(eventType));Description=TextLimit.Required(description,512,nameof(description));if(metadata is not null){if(metadata.Length>2048)throw new ArgumentOutOfRangeException(nameof(metadata));using var doc=JsonDocument.Parse(metadata);if(ContainsSecret(doc.RootElement))throw new ArgumentException("Audit metadata must not contain secret fields.",nameof(metadata));}MetadataJson=metadata;if(correlation==Guid.Empty)throw new ArgumentException("Correlation ID cannot be empty.",nameof(correlation));CorrelationId=correlation;CreatedAtUtc=Utc.Require(created);}
    public static AuditLog Create(DateTimeOffset occurredAtUtc,AuditActor actor,string eventType,string description,DateTimeOffset createdAtUtc,string? metadataJson=null,Guid? correlationId=null)=>new(occurredAtUtc,actor,eventType,description,metadataJson,correlationId,createdAtUtc);
    private static bool ContainsSecret(JsonElement e){if(e.ValueKind==JsonValueKind.Object){foreach(var p in e.EnumerateObject()){var n=p.Name.ToLowerInvariant();if(n.Contains("pin")||n.Contains("password")||n.Contains("recovery")||n.Contains("token")||n.Contains("secret")||ContainsSecret(p.Value))return true;}}else if(e.ValueKind==JsonValueKind.Array){foreach(var item in e.EnumerateArray())if(ContainsSecret(item))return true;}return false;}
}

internal static class EnumGuard { public static void Defined<T>(T value) where T:struct,Enum { if(!Enum.IsDefined(value))throw new ArgumentOutOfRangeException(nameof(value)); } }
internal static class TextLimit { public static string Required(string? value,int max,string name){if(string.IsNullOrWhiteSpace(value))throw new ArgumentException("Value is required.",name);var v=value.Trim();if(v.Length>max)throw new ArgumentOutOfRangeException(name);return v;} public static string? Optional(string? value,int max){if(string.IsNullOrWhiteSpace(value))return null;var v=value.Trim();return v.Length<=max?v:v[..max];} }
