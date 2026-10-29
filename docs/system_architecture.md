# Kiến trúc Hệ thống - EduFocus

> **Trạng thái:** Thiết kế đích / đang chuyển đổi từ **Don't Be Lazy**.
> **Phiên bản:** 2.0 — 2026-10-26
>
> Mã nguồn đã dùng tên project `EduFocus.*` từ Phase 1 (issue #5). Tài liệu này mô tả kiến trúc **đích** của EduFocus.

---

## 1. Mục tiêu kiến trúc

EduFocus kế thừa nền **Clean Architecture + Ports & Adapters** của dự án cũ vì đây là lựa chọn phù hợp với một ứng dụng desktop có nhiều thành phần hệ điều hành:

- Tách biệt business logic khỏi WPF, Win32 API, SQLite và camera.
- Dễ unit test mà không cần chạy chặn web hay mở webcam thật.
- Có thể thay thế hạ tầng (ví dụ: hosts → browser extension → firewall) mà không đổi UseCase.
- Cho phép chạy MVP dưới dạng một app elevated, sau đó tách Windows Service mà không viết lại logic.

### Nguyên tắc

1. **Local-first:** không cloud, không telemetry mặc định.
2. **Least data:** chỉ lưu dữ liệu tổng hợp và sự kiện; không lưu ảnh/video.
3. **Safe failure:** nếu không chặn được hoặc không mở được camera, báo đúng trạng thái; không giả vờ thành công.
4. **Auditability:** mọi thay đổi bảo vệ, PIN, blocklist, xóa dữ liệu đều có audit log.
5. **Tamper-aware, not tamper-proof:** biết rõ giới hạn trước người dùng có quyền admin.

---

## 2. Sơ đồ tổng quan

```text
┌──────────────────────────────────────────────────────────────────────┐
│                         EduFocus.App (WPF)                           │
│  Parent Dashboard │ Student Status │ Blocklist │ Reports │ Settings  │
│  MVVM (CommunityToolkit) + MaterialDesign                            │
└──────────────────────────────────┬───────────────────────────────────┘
                                   │ Inbound Ports
┌──────────────────────────────────▼───────────────────────────────────┐
│                         EduFocus.UseCases                            │
│  ParentAuth │ Protection │ StudySession │ Attention │ Reports         │
└───────┬──────────────────────────┬──────────────────────────┬────────┘
        │ Outbound Ports           │                          │
┌───────▼──────────────┐  ┌────────▼─────────────┐  ┌─────────▼────────┐
│ Protection Engine    │  │ Activity Tracker     │  │ Attention Monitor│
│ hosts + redirect     │  │ foreground + URL     │  │ camera + ONNX    │
│ process watcher      │  │ Meet/Teams detect    │  │ face/gaze/EAR    │
└───────┬──────────────┘  └────────┬─────────────┘  └─────────┬────────┘
        │                          │                          │
        └──────────────┬───────────┴──────────────┬───────────┘
                       │                          │
              ┌────────▼────────┐        ┌────────▼────────┐
              │ SQLite (EF Core)│        │ Windows APIs    │
              │ ProgramData     │        │ Win32 / WMI     │
              └─────────────────┘        └─────────────────┘

             (Post-MVP) EduFocus.Agent (Windows Service)
             - watchdog, policy enforcement, ACL, auto-start
             - giao tiếp với app qua named pipe
```

---

## 3. Các tầng

### 3.1. Domain (`EduFocus.Domain`)

Không phụ thuộc project ngoài. Chứa entity, enum, value object và business rules thuần túy.

> Domain model 2.0 đã được triển khai ở issue #8. Chi tiết invariant và ranh giới trách nhiệm:
> [domain-invariants.md](./domain-invariants.md).

**Entity chính**

| Entity | Trách nhiệm |
|---|---|
| `ParentCredential` | PIN hash/salt, recovery code hash, lockout |
| `AppSettings` | Trạng thái protection, monitoring, schedule, retention, camera, theme |
| `BlockRule` | Rule chặn/cho phép domain/app (MVP); URL là post-MVP |
| `ScheduleWindow` | Khung giờ bảo vệ theo ngày; không hỗ trợ qua nửa đêm trong MVP |
| `StudySession` | Vòng đời phiên học, `ActiveSeconds`, category totals, heartbeat, attention score |
| `ActivitySample` | Mẫu app/URL tại một thời điểm |
| `AttentionEvent` | Sự kiện focused/distracted/absent/drowsy/camera-unavailable |
| `BlockedAttempt` | Lần truy cập bị chặn |
| `AuditLog` | Thao tác nhạy cảm |

**Value Object:** `StronglyTypedId` cho từng entity (`StudySessionId`, `BlockRuleId`, `ParentCredentialId`…) và `DomainName`; helper privacy `UrlSanitizer`, `TextSanitizer`.

**Business rules tiêu biểu**
- PIN phải 6–8 chữ số, không yếu.
- Allowlist thắng blocklist.
- Không chặn domain thiết yếu.
- Attention score chỉ tính trên thời gian camera khả dụng; coverage tính trên `MonitoringEligibleSeconds`.
- Session không cộng thời gian khi máy sleep hoặc app tắt; heartbeat xác định điểm kết thúc khi crash.
- BlockRule domain exact ở MVP; không so khớp chuỗi thô.

### 3.2. Ports (`EduFocus.Ports`)

Chỉ chứa interface và DTO.

**Inbound**
```text
IOnboardingUseCase
IParentAuthUseCase
IParentModeUseCase
IProtectionUseCase
IAppLifecycleUseCase
IStudySessionUseCase
IStudySessionHeartbeatUseCase
IActivityUseCase
ICameraSessionUseCase
IAttentionMonitorUseCase
IBlockRuleUseCase
IScheduleUseCase
IReportUseCase
IRetentionUseCase
```

**Outbound**
```text
IProtectionEnginePort
IBlockPageHostPort
IForegroundActivityPort
IBrowserUrlProviderPort
ICameraPort
IAttentionFrameAnalyzer
IMonotonicClockPort
IPasswordHasher
IRecoveryCodeGenerator
IParentCredentialRepository
IAppSettingsRepository
IBlockRuleRepository
IScheduleWindowRepository
IStudySessionRepository
IActivitySampleRepository
IRetentionRepository
ISystemPowerEventPort
IAttentionEventRepository
IBlockedAttemptRepository
IAuditLogRepository
IUnitOfWork
IAppLogger
IParentAuthorizer
IProcessWatchdogPort
```

### 3.3. UseCases (`EduFocus.UseCases`)

Điều phối domain + outbound ports. Không biết WPF, SQLite hay Win32.

| UseCase | Chức năng |
|---|---|
| `ParentAuthUseCase` | Setup PIN, login, lockout, đổi PIN, recovery, tạo lại recovery code sau khi xác thực PIN |
| `ParentModeService` | Parent session: login mở phiên, đổi PIN/reset kết thúc phiên |
| `OnboardingService` | First-run: consent, tuỳ chọn an toàn, PIN, resume và hoàn tất UC01 |
| `ProtectionService` | Bật/tắt bảo vệ, đồng bộ phiên học và engine, phục hồi trạng thái sau restart |
| `AppLifecycleService` | Thoát bằng PIN (clean exit) và ghi nhận Windows shutdown/logoff (UC12) |
| `StudySessionUseCase` | Bắt đầu/kết thúc session, tổng hợp thời gian |
| `ActivityUseCase` | Ghi sample, phân loại app/URL, phát hiện lớp online |
| `CameraSessionService` | Chọn/mở/đóng camera, báo lỗi quyền/bận/mất kết nối, retry backoff (issue #30) |
| `AttentionPipelineService` | Sampling camera 1 FPS qua camera supervisor, inference trên frame managed, dispose frame trước khi phát `AttentionSample` (issue #31) |
| `AttentionMonitorService` | Nhận `AttentionSample`, làm mượt 5–10 giây, chạy state machine, mở/đóng `AttentionEvent` và cộng giây attention (issue #32); `ProtectionService` start/stop monitor + pipeline theo phiên học (issue #33) |
| `BlockRuleUseCase` | CRUD rule, validate, allowlist override |
| `ScheduleService` | CRUD khung giờ, đánh giá 30 giây, auto ON/OFF và override |
| `ReportUseCase` | Query dashboard, báo cáo ngày/tuần, timeline (issue #35/#36) |
| `ReportExportService` | Xuất đúng snapshot báo cáo ra một CSV, lọc sample/event theo ngày local, chống formula injection (issue #37) |
| `RetentionService` | Xóa dữ liệu quá hạn theo AppSettings, batch và audit (issue #29); xóa theo yêu cầu/audit viewer thuộc UC11 UI sau |

`ParentSessionState` (singleton, chỉ trong bộ nhớ) triển khai `IParentAuthorizer` để các use case
nhạy cảm kiểm tra phiên trước khi thực thi.

**Decorator:** giữ pattern logging decorator cho các use case nhạy cảm (Protection, ParentAuth).

**Hiện trạng issue #16:** `ProtectionService` chỉ phụ thuộc `IProtectionEnginePort`, nên không biết hosts,
process hay block page. Thứ tự bền vững là engine → session → `ProtectionEnabled` khi bật và
engine → kết thúc session → `ProtectionEnabled = false` khi tắt; lỗi ghi DB kích hoạt rollback tương ứng
và lỗi gỡ engine giữ nguyên trạng thái ON. Từ issue #24, `ProtectionEngine` là adapter thật ghép block
page, hosts và process watcher; "bật bảo vệ" chặn domain/app thật và UI hiển thị cảnh báo của engine
(ví dụ cổng 80 bận) thay vì cảnh báo "engine chưa có".

**Hiện trạng issue #17:** `ProtectionSource` lưu bền vững nguồn bật bảo vệ (`None`/`Manual`/`Schedule`).
`ProtectionService.EnableForScheduleAsync`/`DisableForScheduleAsync` dùng actor `System` và chỉ tắt được
bảo vệ do lịch sở hữu; `DisableAsync` của phụ huynh trong khung giờ đang hoạt động ghi
`ScheduleOverrideUntilUtc` đến hết khung. `ScheduleService` serialize mọi thao tác bằng semaphore, kiểm tra
parent auth cho CRUD, chống chồng lấn qua `ScheduleValidator`, ghi audit `ScheduleChanged`, và chạy
`EvaluateAsync` mỗi 30 giây lẫn lúc khởi động. Khi phát hiện lệch wall clock > 2 phút so với monotonic,
service ghi `TamperDetected` và bỏ qua auto-disable trong tick đó. Lỗi engine được chuyển thành cảnh báo
thay vì làm hỏng vòng đánh giá.

**Hiện trạng issue #18:** `IAppLifecycleUseCase`/`AppLifecycleService` tách hai đường thoát. Thoát bằng
PIN luôn xác thực qua `IParentModeUseCase` (kể cả khi bảo vệ OFF), sau đó gọi
`IProtectionUseCase.DisableForExitAsync` — biến thể không tạo `ScheduleOverrideUntilUtc` — để gỡ engine,
kết thúc phiên `Completed`, đặt `ProtectionEnabled = false` và ghi audit `ProtectionOff`/`CleanExit`.
Sau đó service ghi `LastCleanShutdownAtUtc`; lỗi ghi dấu này chỉ tạo cảnh báo vì startup reconcile tự dọn
theo trạng thái đã lưu. Windows shutdown/logoff chỉ đóng phiên đang chạy với `Interrupted` +
`Note = SystemShutdown`, ghi audit `SessionInterrupted`/`SystemShutdown`, giữ nguyên
`ProtectionEnabled` và `LastCleanShutdownAtUtc`; mọi lỗi DB được log và nuốt để không chặn hệ điều hành.
`ParentSessionState` luôn bị khóa sau khi luồng thoát kết thúc, kể cả khi PIN sai.

**Hiện trạng issue #25:** `IStudySessionHeartbeatUseCase`/`StudySessionHeartbeatService` chạy beat 10 giây
bằng `PeriodicTimer`, cộng `ActiveSeconds` theo monotonic clock (tối đa 60 giây/beat), clamp wall clock
lùi và dừng trước khi session kết thúc. `ProtectionService` serialize enable/disable/reconcile bằng
semaphore, khởi động heartbeat trước khi lưu `ProtectionEnabled = true`, dừng heartbeat trước khi
`Complete`/`Interrupt` và rollback khi bật thất bại. Reconcile đóng session cũ tại `LastHeartbeatAtUtc`
rồi tạo phiên `Recovery` tại thời điểm hiện tại, nên thời gian app tắt không bị cộng. Thoát sạch và
Windows shutdown đều dừng heartbeat trước khi kết thúc phiên; `App.OnExit` dispose `ServiceProvider`
bất đồng bộ để ghi beat cuối và nhả timer. Repository ghi heartbeat/increment bằng targeted update có
điều kiện nên heartbeat không hồi sinh session đã kết thúc hoặc đè lên bộ đếm blocked attempt.

**Hiện trạng issue #26:** `IActivityUseCase`/`ActivityTrackingService` poll foreground mỗi giây, chỉ ghi
sample khi context ổn định đủ 2 giây và cách sample trước 15 giây; idle ≥ 5 phút ghi đúng một sample cho
mỗi chuỗi idle. Thời lượng lấy từ monotonic clock, cap 60 giây/sample; wall clock lùi bị clamp. Mỗi sample
được phân loại bằng `BlockRuleSet` snapshot (load một lần lúc `StartAsync`), `OnlineClassDetector` phủ
overlay `IsOnlineClass`; URL được sanitize qua `UrlSanitizer` trước khi suy domain. Bộ đếm session được ghi
bằng `AddActivitySecondsAsync` targeted (chỉ session `Active`) **trước** khi insert sample, nên lỗi DB
không đếm đôi; lỗi insert chỉ log và thử lại ở beat sau. Activity là observability: lỗi start/stop không
làm hỏng Protection. Adapter Windows: `WindowsForegroundActivityPort` (Win32 + `GetLastInputInfo`) và
`UiAutomationBrowserUrlProvider` (UIA address bar, allowlist trình duyệt).

**Hiện trạng issue #27:** `OnlineClassDetector` nhận Meet/Teams/Zoom theo domain, process đã chuẩn hóa
(path, `.exe`, dấu nháy) và title Meet có kiểm tra ranh giới; title chat chung và process chat như Discord,
Slack, Messenger, Zalo không bị nhận nhầm. Sample idle luôn có `IsOnlineClass = false`, còn
`OnlineClassSeconds` vẫn chỉ là overlay của category time nên không cộng trùng. Policy domain
`MonitoringEligibility.IsEligible(monitoringEnabled, monitorOnlyDuringClasses, isOnlineClass)` chuẩn bị cho
attention pipeline (#33): khi `MonitorOnlyDuringClasses` bật, chỉ thời gian phát hiện lớp online mới vào
`MonitoringEligibleSeconds`, nên coverage không bị chia cho thời gian ngoài lớp.

**Hiện trạng issue #28:** `ISystemPowerEventPort` phát `Suspended`/`Resumed`; adapter WPF nghe
`SystemEvents.PowerModeChanged` và `SessionSwitch` (lock/disconnect/unlock/connect). Heartbeat và activity
subscribe trong `StartAsync` và unsubscribe trong `StopCore`, nên handler không sống qua stop. Heartbeat
cộng dồn phần hoạt động vào lúc suspend rồi neo lại monotonic tick, nên resume chỉ cộng tiếp từ mốc thức;
suspend lặp lại giữ mốc sớm nhất và nhiều chu kỳ trước một beat chỉ cộng các slice active. Beat định kỳ
trong lúc lock/disconnect chờ resume mà không cộng thời gian; nếu timer bị pause (sleep) và resume event mất,
gap vượt `MaxActiveDelta` bị bỏ rồi đếm lại. Activity reset `_lastSampleTick` khi resume, bỏ gap vượt
`MaxSampleGap` và khởi động lại cửa sổ 15 giây; cả hai dùng monotonic clock nên forward wall clock không
làm phồng thời lượng, còn backward wall clock vẫn clamp timestamp. Khi ghi heartbeat lỗi, `_pendingSeconds`
được cộng vào beat sau và clamp `MaxActiveDelta`.

**Hiện trạng issue #29:** `IRetentionUseCase`/`RetentionService` đọc retention từ `AppSettings`, tính cutoff
UTC cho từng bảng rồi xóa `ActivitySamples` → `AttentionEvents` → `BlockedAttempts` → `AuditLogs` →
`StudySessions` bằng batch 500 (lặp đến khi batch ngắn). `SqliteRetentionRepository` dùng raw SQL subquery
`LIMIT @batch` vì SQLite không hỗ trợ `DELETE ... LIMIT`; sample/event thuộc session `Active` và attention
event còn mở bị loại khỏi batch, session chỉ bị xóa khi đã kết thúc và quá hạn. Mỗi bảng lỗi độc lập, giữ số
đã xóa và ghi vào `Failures`; lỗi vacuum/audit cũng được báo thay vì giả thành công. Audit `RetentionApplied`
(actor `System`) chứa số bản ghi từng bảng và danh sách bảng lỗi, không có nội dung nhạy cảm. Thiếu hoặc lỗi
đọc settings thì bỏ qua toàn bộ pass. Sau khi xóa, service chạy `PRAGMA incremental_vacuum`.
`RetentionSchedulerService` chạy một pass lúc khởi động và mỗi ngày khi local time ≥ 03:00; pass chạy trên
thread pool, chống chồng bằng cờ `_running`. Xóa dữ liệu thủ công theo yêu cầu phụ huynh (UC11) vẫn thuộc
phần UI sau; xuất CSV đã có ở issue #37.

**Hiện trạng issue #30:** `ICameraSessionUseCase`/`CameraSessionService` sở hữu vòng đời một camera: đọc
`AppSettings.CameraDeviceId`, liệt kê thiết bị qua `ICameraPort.ListDevices`, fallback camera mặc định khi
thiết bị đã lưu không còn, mở/đóng tuần tự bằng semaphore. Open/list/capture chạy trên thread pool với
timeout 5 giây (`CameraOptions`), nên driver treo không chặn UI. Lỗi mở → `Unavailable` + `LastFailure`,
tự mở lại sau 30 giây và backoff nhân đôi tới 5 phút; capture trước hạn trả lỗi mà không mở lại. Capture
lỗi liên tiếp 3 lần hoặc `Disconnected` mới đóng camera và chuyển `Unavailable`; lỗi lẻ không kết luận mất
kết nối. `CameraOptions` validate timeout/backoff/ngưỡng. Adapter `OpenCvCameraPort` copy `Mat` sang
`CameraFrame` BGR24 managed và giải phóng native buffer trước khi trả về; `WindowsCameraDeviceCatalog` liệt
kê PNP camera qua WMI (best effort). `CameraStatusViewModel` hiển thị trạng thái và hướng dẫn quyền trong
cửa sổ chính/tray. Camera **chưa tự bật**; #33 nối vòng đời phiên học: `ProtectionService` start monitor rồi
pipeline khi bật bảo vệ/phục hồi và stop pipeline rồi monitor khi tắt/reconcile, còn `AttentionMonitorService`
(#32) đã ghi `AttentionEvent`/`CameraUnavailable` từ sample.

**Hiện trạng issue #31:** `IAttentionPipelineUseCase`/`AttentionPipelineService` chạy vòng sampling mặc định
1 giây (validate 0–5 giây), `StartAsync` mở camera qua `ICameraSessionUseCase` và tick đầu tiên chạy ngay.
Mỗi tick lấy `CameraFrame`, gọi `IAttentionFrameAnalyzer.Analyze`, dispose frame **trước** khi phát
`AttentionSample`, nên subscriber không thể giữ ảnh. Camera lỗi/không quyền → sample có `CameraFailure` và
`Observation = null`; lỗi inference → sample `Error` nhưng `CameraFailure = None`, không bị hiểu nhầm là
`Absent`. Lỗi camera chỉ log khi loại lỗi đổi, lỗi inference lặp được gộp; exception của subscriber không làm
dừng loop. `OpenCvAttentionFrameAnalyzer` không còn sở hữu camera: nó nhận `CameraFrame` BGR24, copy sang
`Mat`, tính luminance và chạy detector. Phân loại trạng thái và ghi `AttentionEvent` đã có ở #32; cộng
coverage và start pipeline theo phiên học đã nối ở #33.

### 3.4. Infrastructure (`EduFocus.Infrastructure`)

Triển khai outbound ports, tương tác Windows.

| Thành phần | Vai trò |
|---|---|
| `ProtectionEngine` | Refactor từ `WindowsStrictEngine`: hosts, block page server, process watcher, rule matching |
| `BlockPageServer` | Local HTTP server trả trang chặn; refactor từ `LocalRedirectServer` |
| `ChatbotBlocklist` | Danh sách domain chatbot mặc định, có version |
| `WindowsForegroundActivityPort` | Win32 foreground window + process + idle time (`GetLastInputInfo`) |
| `UiAutomationBrowserUrlProvider` | UI Automation đọc address bar (MVP); browser extension native messaging (post-MVP) |
| `WindowsSystemPowerEventPort` | `SystemEvents.PowerModeChanged` + `SessionSwitch` → `Suspended`/`Resumed` |
| `WebcamAttentionMonitor` | Tên cũ trong tài liệu; Phase 5 tách thành `OpenCvCameraPort` + `OpenCvAttentionFrameAnalyzer` |
| `OpenCvAttentionFrameAnalyzer` | `IAttentionFrameAnalyzer` trên `CameraFrame` managed: YuNet/Haar, luminance, không mở camera, không lưu frame (issue #31) |
| `OpenCvCameraPort` | `ICameraPort` trên OpenCvSharp MSMF; chọn thiết bị theo PNP id, copy frame sang buffer managed, giải phóng `Mat` ngay |
| `WindowsCameraDeviceCatalog` | Liệt kê camera qua WMI `Win32_PnPEntity` (best effort) |
| `Pbkdf2PasswordHasher` | PBKDF2-SHA256 600.000 iterations, salt 16 byte, hash 32 byte, constant-time compare |
| `RecoveryCodeGenerator` | Sinh recovery code 12 ký tự từ alphabet không nhập nhằng bằng CSPRNG |
| `WatchdogService` | Scheduled task (MVP, poll 60 giây) / Windows Service (post-MVP) |
| `FileLogger` | Log xoay vòng theo ngày, redact dữ liệu nhạy cảm |
| `MonotonicClockAdapter` | Giữ từ dự án cũ |

### 3.5. Data Access (`EduFocus.SqliteDataAccess`)

- `EduFocusDbContext` với 9 DbSet mới; Fluent API cấu hình index, unique constraint, converter và check constraint.
- Migration `InitialEduFocusSchema` + `AddOnboardingConsent` (hai mốc consent nullable trên `AppSettings`) + `AddProtectionSource` (cột `ProtectionSource`, backfill `Manual`); **không migrate** dữ liệu từ `DontBeLazy` 1.x.
- DB path: `%ProgramData%\EduFocus\edufocus.db` (mặc định trong `EduFocusDatabaseOptions`).
- `SqliteDatabaseInitializer`: tạo/khóa thư mục dữ liệu, kiểm tra integrity DB hiện có, backup trước migration (verified, giữ 3 bản), giữ DB nếu lỗi và ghi log hướng dẫn khôi phục.
- `SqliteBackupStore`: tạo/verify/rotate backup và `Restore` an toàn (snapshot trước khi thay thế).
- `DataDirectorySecurity`: ACL Administrators/SYSTEM, kế thừa cho file DB và backup.
- `SqliteParentCredentialRepository`: đọc/ghi singleton `ParentCredentials` (hash + lockout state), không lưu plaintext.
- `SqliteAppSettingsRepository`: đọc/ghi singleton `AppSettings`, gồm consent onboarding và tuỳ chọn an toàn.
- `SqliteStudySessionRepository`: đọc phiên `Active` hiện tại, thêm/cập nhật phiên; unique partial index đảm bảo tối đa một phiên active. Heartbeat và `BlockedAttemptCount` được ghi bằng `ExecuteUpdateAsync` có điều kiện (chỉ session `Active`, không lùi timestamp).
- `SqliteScheduleWindowRepository`: CRUD `ScheduleWindows`, list theo `StartMinutes`/`EndMinutes`/`DaysMask` cho scheduler và UI.
- `SqliteAuditLogRepository`: ghi audit event kèm `CorrelationId`, tách khỏi file log kỹ thuật.
- `SqliteRetentionRepository`: xóa theo batch bằng raw SQL subquery `LIMIT` (SQLite không hỗ trợ `DELETE ... LIMIT`), loại session `Active`/event còn mở, và chạy `PRAGMA incremental_vacuum`.
- `SqlitePragmaInterceptor`: `PRAGMA foreign_keys = ON` mỗi kết nối; WAL và `auto_vacuum` do initializer thiết lập.
- Các repository còn lại sẽ được thêm ở các issue sau.

### 3.6. UI (`EduFocus.WPF`)

MVVM, DI root. View/ViewModel mới:

```text
Views/
├── OnboardingView
├── ParentLoginView
├── ParentDashboardView
├── StudentStatusView
├── LiveMonitorView
├── BlocklistView
├── ScheduleView
├── ReportsView
├── PrivacyView
└── SettingsView
```

**Quy tắc**
- ViewModel không gọi `AppDbContext`, không gọi Win32 trực tiếp.
- ViewModel chỉ gọi Inbound Ports.
- Parent Mode được quản lý bởi `ParentSessionState` singleton.
- Không hiển thị chi tiết nhạy cảm ở Student Mode.

Triển khai (issue #14): `ParentModeWindow` + `ParentModeViewModel` cung cấp đăng nhập PIN,
trạng thái phiên, đổi PIN, reset bằng recovery code và hiển thị mã khôi phục mới đúng một lần.
`DatabaseBootstrapper` khởi tạo DB lazily; lỗi dữ liệu hiển thị trong cửa sổ và có thể thử lại.

Triển khai (issue #15): `OnboardingWindow` + `OnboardingViewModel` là wizard 5 bước (giới thiệu,
quyền riêng tư, tuỳ chọn an toàn, PIN, mã khôi phục) và là cửa sổ đầu tiên khi app khởi động.
App chỉ mở dashboard sau khi onboarding hoàn tất; đóng wizard trước khi xong sẽ thoát app. Wizard
kiểm tra quyền camera qua Windows consent store (chỉ cảnh báo) và hỗ trợ copy mã khôi phục một lần.
Các bước đã lưu cho phép resume sau restart; nếu PIN đã tạo nhưng chưa hoàn tất, phụ huynh nhập PIN
để tạo lại mã khôi phục mới.

Triển khai (issue #16): `ProtectionStatusViewModel` là nguồn hiển thị dùng chung cho `MainWindow`,
`ParentModeWindow` và tray. Parent Mode có nút Bật/Tắt bảo vệ (gọi `IProtectionUseCase`, hiển thị cảnh báo
engine); cửa sổ chính chỉ hiển thị trạng thái và cảnh báo. Trước khi mở cửa sổ chính, `App` gọi
`IProtectionUseCase.ReconcileAsync()` để đồng bộ DB, phiên học, engine và tray; lỗi reconcile được log và
hiển thị trên UI. `TrayIconService` dùng `NotifyIcon` (WinForms) với icon/tooltip theo trạng thái; luồng
thoát bằng PIN được bổ sung ở issue #18.

Triển khai (issue #17): thẻ "Lịch bảo vệ" trong màn hình quản lý của `ParentModeWindow` hiển thị trạng thái
lịch, khung giờ đang hoạt động và override; phụ huynh thêm/sửa/xóa khung giờ (7 ngày, `HH:mm`, hỗ trợ
`24:00`, nhãn) và bật/tắt lịch. `ScheduleViewModel` là singleton dùng chung, chỉ gọi `IScheduleUseCase` và
không giữ secret. `ScheduleSchedulerService` dùng `DispatcherTimer` 30 giây, chạy `EvaluateAsync` một lần
trước khi mở cửa sổ chính rồi khởi động timer; lỗi được log và hiển thị cảnh báo, không làm sập app.
`ScheduleViewModel.ApplyEvaluationAsync` chỉ thay danh sách khi dữ liệu đổi nên không ghi đè editor khi
tick 30 giây.

Triển khai (issue #18): `MainWindow.OnClosing` luôn hủy đóng và ẩn xuống tray trừ khi `AppExitState`
đã được duyệt bởi luồng thoát; tray balloon nhắc "ứng dụng vẫn đang bảo vệ" khi bảo vệ ON. Menu tray có
"Thoát EduFocus..." mở `ExitWindow`/`ExitViewModel` — nhập PIN và, khi bảo vệ ON, tick xác nhận tắt bảo vệ
lần hai. Thành công đặt `AppExitState.ApproveExit()` rồi `Application.Shutdown()`; lỗi hiển thị tại chỗ và
app tiếp tục chạy. `App.OnSessionEnding` chạy `NotifySystemShutdownAsync()` trên background với chờ tối đa
2 giây và không bao giờ gán `e.Cancel`, nên Windows shutdown không bị app chặn.

---

## 4. Luồng nghiệp vụ chính

### 4.1. Bật Protection

```text
Parent bấm ON
  → ParentAuth (PIN nếu chưa login)
  → ProtectionUseCase.EnableAsync()
       → validate block rules + schedule
       → StudySessionUseCase.StartAsync()  (heartbeat bắt đầu)
       → ProtectionEngine.ApplyAsync(rules exact domain/app)
            → write hosts block
            → start BlockPageServer nếu port 80 khả dụng
            → start process watcher
       → ActivityTracker.StartAsync()
       → AttentionMonitor.StartAsync() (nếu enabled + eligible)
       → save ProtectionEnabled = true
       → audit log
  → UI cập nhật ON + tray icon
```

**Rollback:** nếu bước hosts thất bại, gỡ phần đã áp dụng và giữ OFF. Nếu block page server thất bại nhưng hosts thành công, vẫn giữ protection ON và cảnh báo.

> **Hiện trạng Phase 2 (issue #16):** `EnableAsync` đã thực hiện đúng thứ tự engine → session → lưu
> `ProtectionEnabled`, rollback engine/phiên khi ghi DB lỗi và bỏ qua việc tạo phiên thứ hai. Rule
> validation, ActivityTracker và attention thuộc các issue #26/#28 trở đi. Engine thật
> `ProtectionEngine` đã được nối ở issue #24 — apply/remove/reconcile điều phối block page, hosts và
> watcher; cảnh báo của adapter được trả về UI thay vì tuyên bố đã chặn mọi thứ.
>
> **Heartbeat (issue #25):** `ProtectionService` giữ thứ tự engine → session → heartbeat → lưu
> `ProtectionEnabled`; heartbeat dừng trước khi session kết thúc và được khởi động lại nếu disable lỗi.
> Enable/disable/reconcile serialize bằng semaphore nên không tạo hai session hoặc hai heartbeat.

### 4.2. Học sinh truy cập chatbot

```text
Student mở chatgpt.com
  → hosts trỏ 127.0.0.1
  → HTTP: BlockPageServer trả trang chặn (nếu đang chạy)
  → ProtectionEngine ghi BlockedAttempt nếu quan sát được (debounce 30s)
  → Report/Dashboard cập nhật
```

Hosts không tạo log cho HTTPS. Với HTTPS, browser thường báo lỗi kết nối/SSL; domain vẫn bị chặn nhưng `BlockedAttempt` có thể không được ghi. Dashboard phải ghi rõ số liệu blocked là best-effort, không phải tổng tuyệt đối.

### 4.3. Attention sampling

```text
Timer 1–2s
  → WebcamAttentionMonitor.CaptureFrame()
  → Face detection (ONNX)
  → Head pose + iris + EAR
  → State classification (Focused/Distracted/Absent/Drowsy/CameraUnavailable)
  → AttentionMonitorService.ProcessAsync(sample)
  → AttentionEvent (khi state đổi)
  → aggregate every 10s
  → frame disposed (không ghi disk)
```

> **Triển khai issue #31:** `AttentionPipelineService` là nửa đầu của luồng trên: timer 1 giây → camera
> supervisor → `IAttentionFrameAnalyzer` → `AttentionSample` → frame disposed. **Issue #32** nối nửa sau:
> classifier + cửa sổ trượt 5–10 giây + state machine → `AttentionEvent` khi state đổi → cộng giây attention.
> **Issue #33** nối vòng đời phiên học: monitor subscribe pipeline trước, pipeline start sau; stop pipeline
> trước rồi monitor drain/đóng event. Coverage và cảnh báo độ tin cậy tính từ counter session, không cần
> aggregate riêng mỗi 10 giây.

### 4.4. Schedule

```text
DispatcherTimer 30s (+ một lần trước khi mở cửa sổ chính)
  → IScheduleUseCase.EvaluateAsync()
  → đọc AppSettings + ScheduleWindows, tính localNow theo TimeProvider.LocalTimeZone
  → clamp/xóa ScheduleOverrideUntilUtc theo khung đang hoạt động
  → trong khung & OFF & không override → ProtectionService.EnableForScheduleAsync() (actor System)
  → ngoài khung & ON & ProtectionSource=Schedule → ProtectionService.DisableForScheduleAsync() (actor System)
  → wall-clock lệch > 2 phút so với monotonic → audit TamperDetected, bỏ qua auto-disable trong tick
```

Phụ huynh tắt bảo vệ trong khung giờ tạo override đến hết khung; bật thủ công xóa override và chuyển
`ProtectionSource` sang `Manual`, nên lịch không tự tắt bảo vệ thủ công khi hết khung. Khi phụ huynh sửa
khung đang hoạt động, override được clamp lại theo giờ kết thúc mới. CRUD khung giờ yêu cầu Parent Mode,
chống chồng lấn và ghi audit `ScheduleChanged`.

Schedule dùng **wall clock/local time** để xác định khung giờ. Monotonic clock chỉ dùng đo elapsed trong một process và phát hiện nhảy giờ; không dùng monotonic xuyên reboot. Khi phát hiện nhảy giờ, ghi audit `TamperDetected` và không tự tắt protection chỉ vì thay đổi giờ.

### 4.5. Thoát ứng dụng & Windows shutdown

```text
Đóng cửa sổ (X / Alt+F4)
  → MainWindow.OnClosing: chưa có exit được duyệt → e.Cancel = true, ẩn xuống tray
  → tray balloon "EduFocus vẫn đang chạy" (nhắc PIN nếu bảo vệ ON)

Tray → "Thoát EduFocus..."
  → ExitWindow (modal): nhập PIN, bắt buộc tick xác nhận nếu bảo vệ ON
  → IAppLifecycleUseCase.ExitWithPinAsync(pin)
       → IParentModeUseCase.LoginAsync(pin)  (PIN luôn bắt buộc)
       → ProtectionService.DisableForExitAsync()  (không tạo override)
            → heartbeat.StopAsync()  (flush beat cuối)
            → ProtectionEngine.RemoveAsync()
            → session Completed (nếu có)
            → ProtectionEnabled = false, ScheduleOverrideUntilUtc = null
            → audit ProtectionOff
       → AppSettings.RecordCleanShutdown()
       → audit CleanExit
       → Parent Mode luôn bị khóa
  → AppExitState.ApproveExit() → Application.Shutdown()

Windows shutdown / logoff
  → Application.OnSessionEnding (không bao giờ e.Cancel)
  → best-effort NotifySystemShutdownAsync() trong tối đa 2 giây
       → heartbeat.StopAsync()
       → session Note = "SystemShutdown" + Interrupted
       → audit SessionInterrupted, SystemShutdown
       → giữ ProtectionEnabled, không đặt LastCleanShutdownAtUtc
  → lần đăng nhập sau: ReconcileAsync interrupts orphan (nếu kịp lỡ) rồi phục hồi engine + phiên Recovery
```

Nếu gỡ engine thất bại, `DisableForExitAsync` giữ nguyên trạng thái ON và `ExitWithPinAsync` trả
`CanExit = false`; app tiếp tục chạy để tránh thoát khỏi trạng thái chặn dở dang. Lỗi ghi
`LastCleanShutdownAtUtc` không chặn thoát: phiên và bảo vệ đã về trạng thái sạch, lần khởi động sau chỉ
reconcile theo `ProtectionEnabled = false`. Trong `SessionEnding`, app không gỡ hosts hay đổi settings vì
Windows có thể kết thúc tiến trình trước khi hoàn tất và shutdown có thể bị hủy bởi ứng dụng khác; giữ
`ProtectionEnabled` để reconcile phục hồi, đúng đường bypass đã ghi trong UC12/SECURITY.

---

## 5. Mô hình tiến trình

### 5.1. MVP — một app elevated

```text
EduFocus.App (WPF, elevated)
├── UI
├── ProtectionEngine
├── ActivityTracker
├── AttentionMonitor
├── SQLite access
└── Tray icon / auto-start
```

**Ưu điểm:** đơn giản, nhanh.
**Nhược điểm:** học sinh có admin có thể kill; watchdog chỉ ở mức scheduled task.

Watchdog MVP kiểm tra mỗi 60 giây và chỉ restart khi `ProtectionEnabled = true` và lần thoát trước không phải clean exit. Exit hợp lệ bằng PIN đặt `ProtectionEnabled = false`, ghi `LastCleanShutdownAtUtc` và không tự bật lại. Windows shutdown giữ nguyên `ProtectionEnabled` và không ghi clean exit để lần đăng nhập sau phục hồi bảo vệ.

Spike #3 xác nhận mô hình task-only: mô phỏng chu kỳ 60 giây restart target trong một chu kỳ, trong khi watchdog process riêng bị kill thì không có lớp ngoài phục hồi. Phase 7 cần lưu restart budget bền vững và ghi audit `WatchdogRestart`/`WatchdogGaveUp`; chi tiết ở `docs/spikes/watchdog-evaluation.md`.

### 5.2. Production — Service + Session Agent

```text
EduFocus.Agent (Windows Service, SYSTEM)
├── policy enforcement
├── watchdog + auto-restart
├── hosts/firewall rules
├── process ACL
└── named pipe server

EduFocus.App (WPF, user session)
├── parent UI
├── named pipe client
└── student status

EduFocus.Monitor (user session, hidden)
├── webcam attention
├── foreground activity
└── gửi event qua named pipe/DB
```

**Lý do tách:** Windows Service chạy Session 0 **không truy cập được webcam** và không thấy foreground window của user. Vì vậy enforcement ở service, monitoring ở user session.

**Không thuộc MVP** nhưng kiến trúc Ports cho phép thêm sau mà không đổi UseCase.

---

## 6. Protection Engine

### 6.1. Rule matching

```text
MVP:  RuleType: Domain | App
      MatchType: Exact
      Effect: Block | Allow

Post-MVP (extension): RuleType: Url; MatchType: Exact | Suffix | Contains
```

Thứ tự:
1. Rule hết hạn bị bỏ qua.
2. Allow thắng Block.
3. Exact domain so khớp sau khi lowercase, bỏ scheme/path/trailing dot; không so khớp chuỗi thô.
4. Rule URL/path bị bỏ qua trong MVP và được đánh dấu `NotEnforced`.
5. Domain thiết yếu có allowlist built-in.

`BlockRules` cho phép tồn tại đồng thời Allow và Block cùng domain; unique key gồm `Effect`.

> **Triển khai (issue #19):** `BlockRuleEvaluator` là hàm thuần cho domain/app; `BlockRuleSet` là
> snapshot bất biến dựng từ `IBlockRuleRepository.ListAsync()`. Snapshot bỏ rule `IsEnabled = false`
> và `NotEnforced` khi tạo, kiểm tra `ExpiresAtUtc` theo `nowUtc` mỗi lần gọi, trả `Allow` cho domain
> trong `ProtectedDomains` và so khớp `Exact` bằng `Ordinal` — không substring, không suffix.
> `ProtectionEngine` (issue #24) nạp snapshot mỗi lần bật/reconcile nên vòng lặp matching không truy
> vấn DB; thay đổi rule khi đang ON chưa tự áp dụng lại (cần tắt/bật hoặc khởi động lại).
>
> **Seed mặc định (issue #20):** `BuiltInRuleCatalog` phiên bản 1 cung cấp 21 rule (14 chatbot/AI,
> 7 allowlist học tập). `BlockRuleSeedService` chạy sau onboarding và trước reconcile: seed theo
> identity `(RuleType, MatchType, NormalizedValue, Effect)` nên idempotent, không ghi đè rule phụ
> huynh, giữ trạng thái bật/tắt và hạn dùng của rule mặc định, chỉ cập nhật `Category` khi catalog
> đổi; lần seed có thay đổi ghi audit `BlockRuleChanged` (actor System) với metadata `seedVersion`.
> Thẻ "Danh sách chặn" trong Parent Mode dùng `IBlockRuleUseCase`, nhóm theo category và cho bật/tắt
> rule; thao tác yêu cầu phiên Parent Mode và được audit với actor Parent.

### 6.2. Website blocking

- Ghi block vào `%SystemRoot%\System32\drivers\etc\hosts` giữa marker riêng:
  ```text
  ### EDUFOCUS BLOCK START ###
  127.0.0.1 chatgpt.com
  ::1 chatgpt.com
  ...
  ### EDUFOCUS BLOCK END ###
  ```
- Chỉ chặn domain exact; không dùng wildcard/suffix và không tự thêm biến thể `www.` — muốn chặn `www.` phải có rule riêng.
- Không sửa các dòng khác của hosts; chỉ xóa block trong marker EduFocus.
- Flush DNS sau khi thay đổi.
- Self-heal: xóa block mồ côi của EduFocus khi khởi động.
- `BlockPageServer` bind `127.0.0.1:80` bằng TCP listener loopback (không cần URL ACL), chỉ phục vụ GET/HEAD, trả HTML tĩnh; kiểm tra Host header thuộc domain bị chặn, escape output, không tải tài nguyên ngoài. Nếu port 80 bị chiếm, protection vẫn ON nhưng block page không khả dụng.
- Retry khi file bị antivirus khóa; báo lỗi rõ nếu thất bại.

> **Triển khai hosts file (issue #21):** `IHostsFileBlocker`/`HostsFileBlocker` sở hữu khối marker,
> chỉ nhận domain exact (chuẩn hóa lowercase/trailing dot; từ chối scheme/path/port/wildcard/IP) và
> ghi cặp `127.0.0.1` + `::1`. Ghi bằng temp file + replace, thử tối đa 3 lần; lỗi được phân loại
> `NotFound`/`AccessDenied`/`Locked`/`WriteFailed` qua `HostsFileException` để use case giữ nguyên
> trạng thái thay vì coi là OFF. Trước lần ghi đầu, bản gốc được backup vào
> `%ProgramData%\EduFocus\hosts-backup` kèm `journal.json`; restore trả lại đúng byte gốc nếu ngoài
> marker chưa bị sửa, ngược lại chỉ gỡ marker để giữ chỉnh sửa của người dùng. `SelfHealAsync` xử lý
> journal sau crash và marker mồ côi. `IpconfigDnsFlusher` flush DNS best-effort (timeout 5 giây);
> log chỉ ghi số domain, không ghi nội dung hosts. Việc nối hosts + block page + process watcher do
> `ProtectionEngine` (issue #24) đảm nhiệm.
>
> **Triển khai block page (issue #22):** `IBlockPageServer`/`BlockPageServer` dùng `TcpListener`
> loopback thay vì `HttpListener` (không cần URL ACL), mặc định cổng 80 và nhận cổng 0 cho test.
> Chỉ GET/HEAD; Host header phải là domain exact trong danh sách, input không được echo. Trang chặn
> escape domain, không tài nguyên ngoài, kèm `Content-Security-Policy: default-src 'none';
> style-src 'unsafe-inline'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'`,
> `Cache-Control: no-store`, `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY` và
> `Referrer-Policy: no-referrer`. Cổng bận/thiếu quyền trả `BlockPageStartResult.Warning` thay vì
> ném lỗi, để engine giữ Protection ON và UI cảnh báo. Mỗi request bị chặn phát `BlockPageHit`
> (domain + UTC); `ProtectionEngine` (issue #24) debounce 30 giây rồi ghi `BlockedAttempt` với
> `ObservedVia = BlockPage`. Adapter không tự ghi DB hoặc log domain.

### 6.3. App blocking (MVP)

- Process watcher dùng WMI + polling fallback.
- Chỉ xử lý process nằm trong danh sách Block; Allowlist app được ưu tiên.
- Gửi `WM_CLOSE` tới cửa sổ chính trước, chờ tối đa 2 giây; nếu process vẫn chạy thì kill process tree.
- Không kill process hệ thống hoặc process của EduFocus.
- Ghi `BlockedAttempt` với `ObservedVia = ProcessWatcher`; debounce 30 giây theo process.
- Kill process có thể làm mất dữ liệu chưa lưu; UI phải thông báo và giới hạn này phải được ghi trong known limitations.

> **Triển khai watcher (issue #23):** MVP dùng polling `Process.GetProcesses()` mỗi 2 giây qua seam
> `IProcessController` (chưa cần WMI eventing); chỉ process có `MainWindowHandle` khác 0 mới được xử
> lý. Tên process được chuẩn hóa (bỏ path/`.exe`, lowercase) và so khớp **exact**; allowlist thắng
> blocklist. Watcher bỏ qua PID ≤ 4, chính EduFocus và danh sách process hệ thống quan trọng
> (`explorer`, `lsass`, `svchost`…). `WM_CLOSE` qua `CloseMainWindow()` + chờ tối đa 2 giây; hết hạn
> thì `Kill(entireProcessTree: true)`. Kết quả `ClosedGracefully`/`Killed`/`Failed` được phát qua
> `ProcessBlockedObservation`; watcher tự debounce 30 giây theo tên process bằng monotonic clock và
> `ProtectionEngine` (issue #24) debounce trung tâm trước khi ghi `BlockedAttempt`. Lỗi đóng/kill hoặc
> handler không làm dừng vòng polling; log không ghi window title, path hay nội dung tài liệu.
>
> **Triển khai engine (issue #24):** `ProtectionEngine` là adapter duy nhất của
> `IProtectionEnginePort`. Bật theo thứ tự block page → hosts → watcher (listener sẵn sàng trước khi
> hosts trỏ domain về loopback); cổng 80 bận chỉ là cảnh báo, lỗi adapter khác rollback phần đã áp và
> ném `ProtectionEngineException`. Tắt gỡ rule watcher/page → dừng watcher/page → restore hosts; lỗi
> dừng trả cảnh báo nhưng lỗi restore hosts ném lỗi để Protection giữ ON. Reconcile self-heal hosts rồi
> apply/remove; remove idempotent kể cả khi Protection OFF. Block page/watcher chỉ phát observation;
> engine debounce 30 giây theo domain/tên app bằng monotonic clock và ghi `BlockedAttempt` qua một
> writer channel duy nhất, gắn `SessionId` đang hoạt động và tăng `BlockedAttemptCount`; lỗi ghi DB chỉ
> log và không làm dừng adapter. Không lưu window title/path.

### 6.4. Hạn chế đã biết

- DoH/DoT, VPN, proxy có thể bypass hosts.
- Hosts không tạo log truy cập HTTPS; không thể đếm đầy đủ blocked attempts.
- Trang chặn chỉ hiển thị với HTTP; HTTPS thường báo lỗi kết nối/SSL.
- UI Protection phải hiển thị cảnh báo thường trực: chặn web chỉ áp dụng HTTP/domain exact; HTTPS, DoH/VPN/proxy có thể bypass và blocked attempts HTTPS không được ghi nhận đầy đủ.
- Browser extension (post-MVP) giải quyết URL/path và logging chính xác hơn.
- Process watcher dựa trên tên/đường dẫn process; app đổi tên, chạy portable hoặc khởi động/thoát quá nhanh có thể không bị phát hiện.
- Engine debounce 30 giây cho cùng domain/tên app nên nhiều lần thử liên tiếp chỉ ghi một `BlockedAttempt`; writer ghi bất đồng bộ nên attempt cuối có thể mất nếu app thoát đột ngột.
- Thay đổi rule khi Protection ON chưa tự áp dụng lại engine; cần tắt/bật hoặc khởi động lại.
- Học sinh có admin có thể sửa hosts/tắt app; không chống được.
- Không dùng kernel driver/WinDivert trong MVP để tránh antivirus và rủi ro ổn định.

---

## 7. Activity Tracker

### 7.1. Foreground window

- `GetForegroundWindow`
- `GetWindowThreadProcessId`
- `GetWindowText`
- `GetLastInputInfo` cho idle time
- `Process.GetProcessById` cho process name/path

### 7.2. URL detection

| Phương án | Ưu điểm | Nhược điểm |
|---|---|---|
| UI Automation đọc address bar | Không cần extension | Phụ thuộc browser, có thể fail |
| Browser extension + native messaging | Chính xác, path-level | Cần cài extension, policy |
| Không lấy URL | Ổn định | Chỉ biết app/window title |

MVP dùng UI Automation + fallback window title. URL là best-effort; nếu không lấy được, `UrlSource = None`. Post-MVP thêm extension để lấy path chính xác.

Không lưu query string/fragment trong `ActivitySamples`; chỉ lưu domain và (nếu cần) path đã chuẩn hóa. Window title có thể chứa nội dung nhạy cảm nên được strip/truncate trước khi lưu.

### 7.3. Nhận diện lớp online

- Google Meet: domain `meet.google.com` (kể cả subdomain); window title chứa `Google Meet` hoặc bắt đầu bằng
  `Meet` theo sau là dấu phân cách (`-`, `–`, `—`, `|`, `:`). Title như `Meeting notes`/`Meet the team`
  không khớp.
- Microsoft Teams: process `ms-teams` (Teams mới) hoặc `teams` (Teams classic) sau khi chuẩn hóa path/`.exe`;
  domain `teams.microsoft.com`. Title `Microsoft Teams` một mình không đủ vì chat cũng dùng title đó.
- Zoom: process `zoom` sau chuẩn hóa; domain `zoom.us` (kể cả subdomain như `us02web.zoom.us`).
- Không nhận nhầm ứng dụng chat: Discord, Slack, Messenger, Zalo và process không khớp exact (ví dụ
  `zoominfo`) đều trả `false`.
- Sample idle không đánh dấu `IsOnlineClass`, vì cửa sổ lớp bị bỏ quên không phải thời gian học.
- Lưu `IsOnlineClass` để báo cáo riêng; `OnlineClassSeconds` là overlay, không cộng trùng category.
- `MonitoringEligibility.IsEligible` quyết định `MonitoringEligibleSeconds` có tăng hay không: monitoring
  phải bật và, khi `MonitorOnlyDuringClasses`, phải đang trong lớp online.

### 7.4. Sampling

- Mỗi 10–30 giây.
- Heartbeat session mỗi 10 giây, độc lập với sample: `StudySessionHeartbeatService` dùng `PeriodicTimer`,
  cộng `ActiveSeconds` theo monotonic clock (tối đa 60 giây/beat), clamp wall clock lùi; lỗi DB được log và
  thử lại ở beat sau; heartbeat dừng trước khi session kết thúc.
- Không ghi khi idle > 5 phút (chỉ ghi một sample idle); `IdleSeconds` lưu riêng.
- Debounce chuyển foreground 2 giây.
- Sample cũ bị retention job xóa; báo cáo dài hạn đọc session summary.

> **Triển khai issue #26:** poll 1 giây, sample 15 giây, foreground debounce 2 giây; idle ≥ 5 phút ghi một
> sample/chuỗi. `ActivityTrackingService` đọc `IForegroundActivityPort` + `IBrowserUrlProviderPort`, phân
> loại bằng `ActivityClassifier`/`BlockRuleSet` snapshot, phủ `IsOnlineClass`, rồi ghi bộ đếm session
> targeted trước khi insert `ActivitySample` (lỗi insert chỉ log và thử lại ở sample sau). `StopAsync`
> không flush sample cuối; vòng lặp dừng khi session kết thúc.

> **Triển khai issue #28:** `ISystemPowerEventPort` (adapter WPF `WindowsSystemPowerEventPort`) báo
> suspend/resume. Heartbeat chỉ cộng phần trước sleep khi resume; activity bỏ gap và reset cửa sổ sample.
> Forward wall clock không đổi `DurationSeconds`/`ActiveSeconds` vì cả hai tính bằng monotonic clock; wall
> clock lùi vẫn clamp timestamp. Gap không có sự kiện và vượt ngưỡng bị bỏ thay vì cộng dồn.

> **Triển khai issue #33:** `ActivityTrackingService` đọc policy monitoring (`MonitoringEnabled`,
> `MonitorOnlyDuringClasses`) một lần khi start và cộng `MonitoringEligibleSeconds` cùng transaction với
> category/idle/online-class qua `AddActivitySecondsAsync`. Idle vẫn là thời gian monitoring được kỳ vọng
> (trừ khi chỉ-monitor-trong-lớp); `IdleSeconds`/`OnlineClassSeconds` vẫn là overlay nên không cộng trùng.
> Lỗi đọc settings giữ coverage là unknown thay vì chặn sampling.

---

## 8. Attention Monitor

### 8.1. Pipeline

```text
Camera (640×480, 1–2 FPS)
  → OpenCvSharp VideoCapture
  → ONNX Runtime:
       - Face Detection (BlazeFace/MediaPipe)
       - Face Mesh landmarks
       - Iris landmarks
  → Head pose (solvePnP)
  → Gaze ratio (iris vs eye corners)
  → Eye Aspect Ratio (EAR)
  → State classifier (sliding window 5–10s)
  → AttentionEvent + aggregate
```

> Phase 0 (ADR-17): dùng OpenCV MSMF + YuNet ONNX để phát hiện mặt và suy ra yaw/pitch proxy; EAR/iris/gaze và `Drowsy` để Phase 5. Adapter nằm ở `EduFocus.Infrastructure.Attention`; Domain/Ports không phụ thuộc OpenCV/ONNX.
>
> Issue #31 tách capture khỏi inference: `OpenCvCameraPort` (issue #30) giữ camera và trả `CameraFrame`
> BGR24 managed; `OpenCvAttentionFrameAnalyzer` chỉ nhận frame, chạy detector và tính luminance, không mở
> thiết bị. `AttentionPipelineService` điều phối hai phần ở nhịp 1 FPS.

### 8.2. State machine

| State | Điều kiện |
|---|---|
| `Focused` | Có mặt, head pose trong ngưỡng, mắt mở, gaze on-screen |
| `Distracted` | Head/gaze lệch > ngưỡng trong > 20 giây |
| `Absent` | Không thấy mặt > 30 giây |
| `Drowsy` | Mắt nhắm lặp lại > 2 giây |
| `CameraUnavailable` | Không mở được camera / camera bận / không có quyền |

> **Triển khai issue #32:** `AttentionClassifier` phân loại từng mẫu (camera, no-face, low-light, yaw/pitch,
> eyes-closed); `AttentionStateWindow` làm mượt bằng majority trong cửa sổ 6 giây (3 mẫu tối thiểu, ≥ 60%)
> và `AttentionStateMachine` giữ debounce 20/30/2 giây. `CameraUnavailable` bỏ qua cửa sổ, reset mẫu và mở
> event ngay; lỗi inference (`Observation = null`) không được tính là `Absent`. Event chỉ mở khi state đổi
> và event cũ đóng tại mốc chuyển. YuNet hiện không đo EAR nên `EyesClosed = null` và production chưa phát
> `Drowsy`; classifier/state machine đã sẵn cho model mắt sau này.

### 8.3. Scoring

```text
AttentionPercent =
    FocusedSeconds / (FocusedSeconds + DistractedSeconds + AbsentSeconds + DrowsySeconds)
```

- `MonitoredSeconds` = tổng bốn trạng thái trên (camera khả dụng).
- Không tính `CameraUnavailable` vào mẫu số attention.
- `MonitoringEligibleSeconds` = thời gian monitoring được kỳ vọng (toàn session hoặc chỉ lúc lớp online nếu `MonitorOnlyDuringClasses`).
  - Quyết định bởi `MonitoringEligibility.IsEligible`: monitoring phải bật; nếu `MonitorOnlyDuringClasses` thì chỉ cộng khi `IsOnlineClass` (issue #27).
- Báo cáo luôn hiển thị:
  - `Coverage = MonitoredSeconds / MonitoringEligibleSeconds`; null nếu eligible = 0.
  - `UnobservedSeconds = MonitoringEligibleSeconds − MonitoredSeconds`: thời gian camera không quan sát được, gồm cả `CameraUnavailable` (issue #34).
  - `ConfidenceAvg` trung bình có trọng số theo thời lượng event, dựa trên face detection score, ánh sáng, số khuôn mặt.
- Nếu coverage < 50%, dashboard hiển thị cảnh báo "Dữ liệu chưa đủ tin cậy".
- Khi crash, mọi `AttentionEvent` đang mở được đóng tại `LastHeartbeatAtUtc`; khi session/monitoring dừng bình thường, đóng tại thời điểm dừng.

> **Triển khai issue #32:** `SqliteStudySessionRepository.AddAttentionSecondsAsync` cộng counter và tính
> `ConfidenceAvg` trọng số ngay trong một câu `UPDATE` (SQLite đánh giá mọi biểu thức SET trên dòng cũ), vì
> `MonitoredSeconds`/`AttentionPercent` là stored generated column không thể ghi qua EF. `CameraUnavailable`
> truyền confidence `null` và không vào mẫu số. Thời gian chỉ cộng khi state đã xác định; gap >
> `MaxSampleGap` (5 giây) bị bỏ, không cộng dồn. Reconcile sau crash đóng event mở tại `LastHeartbeatAtUtc`;
> stop bình thường đóng tại mẫu cuối.
>
> **Triển khai issue #33:** `SessionAttentionSummary` là view tự nhất quán cho báo cáo: `MonitoredSeconds`
> luôn là tổng bốn counter, `AttentionPercent`/`CoveragePercent` null khi mẫu số 0, `CoveragePercent < 50`
> trả `Warning = "Dữ liệu chưa đủ tin cậy"`. `MonitoringEligibleSeconds` do `ActivityTrackingService` cộng
> theo sample foreground (kể cả idle nếu không giới hạn lớp), nên camera outage làm coverage giảm chứ không
> làm attention giảm. `ProtectionService` start monitor rồi pipeline khi bật/phục hồi bảo vệ và stop
> pipeline rồi monitor khi tắt, reconcile hoặc Windows shutdown; mọi lỗi camera/attention chỉ log và không
> tắt bảo vệ.
>
> **Triển khai issue #35:** `DashboardService.GetTodayAsync` lấy ngày local từ `TimeProvider` rồi gọi
> `IStudySessionRepository.GetDailySummaryAsync`, một aggregate `GROUP BY` trên `StudySessions` (không đọc
> `ActivitySamples`/`AttentionEvents`). `DailyStudySummary` là projection bất biến dùng lại
> `SessionAttentionSummary` nên coverage/attention/cảnh báo giống từng phiên; `ActiveSessionStartedAtUtc`
> được lấy từ session `Active` duy nhất. `DashboardViewModel` polling 15 giây, bỏ qua tick chồng lấp bằng
> `SemaphoreSlim`, giữ số liệu cũ khi query lỗi và chỉ hiện loading lần đầu; Student Mode chỉ bind thời gian
> học/phiên đang hoạt động, Parent Mode bind toàn bộ counter nên PIN/cấu hình không xuất hiện ở cửa sổ chính.
> MainWindow dừng polling khi ẩn xuống tray; Parent Mode tái dùng timer 1 giây và đọc dữ liệu mỗi 15 tick.
>
> **Triển khai issue #36:** `ReportService.GetAsync(ReportPeriod)` đọc session theo khoảng `StartLocalDate`
> (index sẵn có), rồi sample/event theo danh sách session id với `maxCount + 1` để phát hiện truncation; không
> quét toàn bộ sample của năm. `ReportPeriod` giới hạn 31 ngày chi tiết và tuần bắt đầu Thứ 2.
> `StudyReport` giữ projection tổng hợp + `DailyStudySummary` mỗi ngày + timeline + top + event; session được
> nhóm theo `StartLocalDate` đã lưu nên DST không làm session nhảy ngày, còn timeline chuyển `SampledAtUtc`
> bằng `TimeProvider.LocalTimeZone` tại thời điểm đọc. `SqliteActivitySampleRepository.ListForReportAsync`
> chỉ trả `AppName`/`Domain` đã làm sạch (không `WindowTitle`, URL đầy đủ, `ProcessName`) và bỏ sample fully
> idle. Session có counter nhưng không còn sample/event bị đánh dấu detail hết retention thay vì báo lỗi.
> `ReportViewModel` trong Parent Mode đọc theo yêu cầu (mở Parent Mode, đổi kỳ, nút Làm mới) và cap hiển thị
> 20 session/24 khung giờ/10 event; Student Mode không bị ảnh hưởng.
>
> **Triển khai issue #37:** `ReportExportService` nhận chính `StudyReport` mà Parent Mode đang hiển thị nên
> dòng `Tổng hợp`/`Ngày`/`Phiên học` khớp số liệu màn hình; sample hoạt động được đọc lại qua projection
> privacy-safe của #36 (không window title/URL/process), event lấy từ snapshot, cả hai chuyển UTC → local bằng
> `TimeProvider.LocalTimeZone` rồi lọc theo `ReportPeriod` nên không lọt dữ liệu ngoài khoảng đã chọn.
> `ReportCsvWriter` phát một header 27 cột + các dòng theo `Loại bản ghi`, quote ô có dấu phẩy/nháy/xuống dòng
> và thêm `'` trước ô bắt đầu bằng `=`, `+`, `-`, `@` để chặn formula injection. `LocalCsvExportStore` ghi
> UTF-8 **có BOM** (Excel nhận đúng tiếng Việt), chỉ nhận tên file phẳng, không ghi đè và lưu tại
> `%LocalAppData%\EduFocus\exports`. `ReportViewModel.ExportCommand` chỉ bật khi có phiên và không tải; kết quả
> hiện đường dẫn file kèm nhắc bảo vệ file nhạy cảm. Không có bảng mới — export chỉ đọc dữ liệu hiện có.

### 8.4. Camera contention

- Windows 11 build mới hỗ trợ **Multi-App Camera**.
- App kiểm tra và hướng dẫn phụ huynh bật trong Settings → Bluetooth & devices → Cameras.
- Nếu không hỗ trợ:
  - Ghi `CameraUnavailable` trong thời gian Meet/Teams dùng camera.
  - Không kết luận mất tập trung.
  - Báo cáo ghi rõ thời gian không quan sát được.
- Post-MVP: companion phone camera hoặc virtual camera.

> **Triển khai issue #30:** `CameraSessionService` coi camera bận/không quyền/mất kết nối là `Unavailable`
> và tự mở lại sau 30 giây (backoff nhân đôi tới 5 phút); chỉ `Disconnected` hoặc 3 lỗi capture liên tiếp
> mới đóng thiết bị. `CameraStatusViewModel` hiển thị trạng thái và hướng dẫn bật quyền Camera trong cửa
> sổ chính/tray. Việc ghi event `CameraUnavailable` đã có ở #32; #33 cộng `MonitoringEligibleSeconds` độc
> lập với sample attention nên thời gian camera bận làm giảm coverage và hiện cảnh báo dữ liệu chưa đủ tin cậy.

> **Triển khai issue #31:** camera bận/không quyền vẫn tạo sample mỗi giây với `CameraFailure` và
> `Observation = null`; pipeline không tự suy đoán `Absent` khi không có frame hay khi inference lỗi. #32
> chuyển sample lỗi thành event `CameraUnavailable` ngay và bỏ qua khi inference lỗi; #33 tính coverage từ
> mẫu số độc lập nên outage không bị tính là mất tập trung.

> **Triển khai issue #34:** `CameraHealthEvaluator` + `CameraGuidance` (Domain) đánh giá ảnh kiểm tra
> (quá tối/không thấy mặt/không có ảnh/không khả dụng) và giữ một bộ câu hướng dẫn dùng chung cho status bar
> lẫn kết quả kiểm tra. `CameraCheckService` chạy preflight tối đa 5 khung hình trong bộ nhớ, luôn đóng
> camera trong `finally` và từ chối khi pipeline đang chạy (`MonitoringActive`) để không tranh thiết bị;
> kiểm tra không cần quyền quản trị. Khi mở camera thất bại nhưng thiết bị vẫn hiện diện,
> `OpenCvCameraPort` phân loại `Busy` (Meet/Teams/Zoom đang giữ camera); Windows 11 22H2+ được phát hiện
> qua `WindowsCameraPlatformInfo` để hướng dẫn bật Multi-App Camera. Thiết bị đã lưu không còn kết nối →
> `StoredDeviceSubstituted`, UI nhắc chọn lại; `NotFound` dừng retry cho phiên (mở lại tường minh khi phụ
> huynh bấm kiểm tra), `PermissionDenied` vẫn retry có backoff để tự phục hồi sau khi cấp quyền. Parent Mode
> có thẻ Camera (trạng thái, hướng dẫn, kiểm tra, chọn camera/mặc định); onboarding chạy cùng preflight
> trước khi lưu tùy chọn. `SessionAttentionSummary.UnobservedSeconds` bổ sung thời gian eligible nhưng
> không quan sát được để báo cáo (#35–#38) hiển thị cạnh coverage. Camera bận/không quyền chỉ tạo
> `CameraUnavailable` và giảm coverage, không tạo `Focused`/`Distracted` giả.

### 8.5. Privacy

- Không ghi frame, không gửi network.
- Không lưu face embedding/template.
- Frame được `Dispose()` ngay sau xử lý.
- Không nhận diện danh tính.
- Có chỉ báo camera đang hoạt động (tray + UI).
- `AttentionSample` chỉ chứa timestamp, trạng thái lỗi, luminance, thời gian capture/inference và các
  trường số của khuôn mặt (confidence, yaw/pitch, số mắt); không chứa pixel, ảnh hay landmark thô. Frame
  được dispose trước khi sample được phát cho subscriber (issue #31).

---

## 9. Parent Authentication & State

### 9.1. Credential

- PIN 6–8 chữ số.
- PBKDF2-SHA256, 600.000 iterations, salt 16 byte.
- Recovery code 12 ký tự, hash tương tự PIN; chỉ hiển thị một lần, không thể xem lại.
- Constant-time compare.
- Lockout 5 lần / 15 phút, dùng chung cho PIN và recovery code.
- Không lưu plaintext; không log PIN/recovery code.

Triển khai (issue #13): `ParentAuthUseCase` điều phối `IParentCredentialRepository`,
`IPasswordHasher`, `IRecoveryCodeGenerator`, `IAuditLogRepository` và `ICorrelationContext`.
`Pbkdf2PasswordHasher` dùng `Rfc2898DeriveBytes.Pbkdf2` + `CryptographicOperations.FixedTimeEquals`;
`RecoveryCodeGenerator` dùng `RandomNumberGenerator.GetInt32`. Recovery code được chuẩn hóa
(uppercase, bỏ dấu gạch/space) trước khi hash; setup trả mã một lần, reset xoay mã mới.
Mỗi lần sai PIN/recovery code ghi audit `AuthenticationFailed`; lần thứ 5 ghi thêm
`LockoutTriggered`. Không có migration mới vì schema `ParentCredentials` đã đủ cột.

### 9.2. Parent session

- `ParentSessionState` singleton:
  - `IsAuthenticated`
  - `LastActivityAt`
  - `LockAsync()` sau 5 phút idle.
- Mọi use case nhạy cảm kiểm tra `IParentAuthorizer` trước khi thực thi.
- Thoát app luôn yêu cầu PIN, kể cả Protection OFF; Windows shutdown/logoff không chặn.

Triển khai (issue #14): `ParentModeService` bọc `IParentAuthUseCase` + `ParentSessionState`;
login thành công mở phiên, đổi PIN thành công khóa phiên, reset bằng recovery code luôn khóa phiên.
`ParentModeWindow` hiển thị số phút lockout còn lại và tự quay về màn hình đăng nhập khi phiên
hết hạn; đóng cửa sổ khóa phiên ngay. Session chỉ nằm trong bộ nhớ nên khởi động lại app phải
đăng nhập lại.

### 9.3. Protection state machine

```text
OFF ──Enable (Parent/System)──> ON
ON  ──Disable (Parent/System)──> OFF (Completed)
ON  ──Crash/Restart───────────> ON (recover; old session Interrupted)
ON  ──Schedule end───────────> OFF (Completed)
ON  ──Parent override────────> OFF (until current window end)
```

Trạng thái lưu trong `AppSettings.ProtectionEnabled`; session active lưu trong `StudySessions`. Khi phục hồi trong thời gian bảo vệ, tạo session mới với `Trigger = Recovery`, không cộng thời gian app tắt. Heartbeat 10 giây xác định điểm kết thúc khi crash; beat cuối được flush trước khi session `Complete`/`Interrupt`.

### 9.4. First-run onboarding & consent

- `OnboardingService` điều phối UC01 và lưu từng bước: `PrivacyAcceptedAtUtc` +
  `GuardianAcknowledgedAtUtc`, tuỳ chọn `MonitoringEnabled`/`StartWithWindows`, `ParentCredential`,
  cuối cùng là `OnboardingCompleted`.
- Không thể tạo PIN trước khi consent được lưu; không thể hoàn tất nếu thiếu PIN hoặc consent.
  `AppSettings.CompleteOnboarding` cũng từ chối khi thiếu consent.
- Wizard chỉ hiển thị mã khôi phục một lần và yêu cầu phụ huynh xác nhận đã lưu. Nếu app đóng sau
  khi tạo PIN, lần mở sau yêu cầu PIN để xoay mã mới (`RecoveryCodeRegenerated`), vì mã cũ chỉ tồn tại
  dưới dạng hash.
- PIN sai khi resume dùng chung lockout 5 lần/15 phút; audit ghi `PrivacyConsentRecorded` và
  `OnboardingCompleted`, không chứa PIN/recovery code.

---

## 10. Persistence & Privacy

- DB: `%ProgramData%\EduFocus\edufocus.db`.
- Logs: `%ProgramData%\EduFocus\logs`.
- ACL: Administrators/SYSTEM full; học sinh standard user không đọc/ghi. Phụ huynh thao tác qua app elevated, không cấp quyền file trực tiếp.
- `DataDirectorySecurity` áp ACL khi initializer chạy; quyền kế thừa cho DB, WAL và `backups`; lỗi ACL ném `DataDirectorySecurityException` và không xóa dữ liệu.
- Backup trước migration bằng `SqliteBackupStore`: verify `PRAGMA integrity_check`, tên file duy nhất, giữ tối đa 3 bản.
- DB hỏng integrity check: giữ nguyên file, ném `SqliteDatabaseCorruptException`; `RestoreBackup` thay thế bằng backup đã verify và snapshot DB hiện tại khi có thể.
- Timestamp lưu **UTC**; hiển thị local. Query theo ngày dùng local boundary chuyển sang UTC.
- Session summary giữ lâu hơn sample chi tiết (365 ngày vs 30 ngày).
- Retention job chạy lúc khởi động và mỗi ngày khi local time ≥ 03:00; xóa theo batch 500 để tránh khóa DB lâu, bỏ qua session `Active`/event còn mở và chạy `PRAGMA incremental_vacuum` sau mỗi pass.
- Backup trước migration schema; giữ 3 bản gần nhất. Không migrate dữ liệu từ 1.x.
- Không lưu ảnh/video/audio.
- Không network call chứa dữ liệu học tập. Update check nếu có phải opt-in, chỉ gửi version/OS và có thể tắt; server vẫn thấy IP và điều này phải được thông báo.

---

## 11. Tamper resistance

| Lớp | MVP | Production |
|---|---|---|
| Ẩn xuống tray khi đóng | ✅ | ✅ |
| Thoát cần PIN | ✅ (kể cả OFF) | ✅ |
| Auto-start | Scheduled Task | Service |
| Watchdog | Scheduled Task restart khi Protection ON, poll 60 giây; không restart sau exit sạch | Windows Service |
| Chặn kill process | Hạn chế | Process ACL + SYSTEM |
| Chống sửa hosts | Self-heal | Service + ACL |
| Chống uninstall | Best-effort PIN prompt | Service + policy |
| Phát hiện đổi giờ | Ghi audit | Ghi audit + policy |
| Chống admin | ❌ (giới hạn đã biết) | Một phần |

**Tuyên bố đúng:** EduFocus chống tắt **ở mức hợp lý**, không chống được admin/SYSTEM, không chặn Windows shutdown, và scheduled-task watchdog có độ trễ tới khoảng 60 giây. Không quảng cáo "không thể tắt".

---

## 12. Logging & Observability

### Technical log

- `FileLogger` triển khai `IAppLogger`; không gửi log ra network.
- Mỗi dòng có dạng:
  `2026-10-29T14:08:16.123Z [INFO ] [Database] [cid=6f1c...] message`
  → timestamp **UTC**, level, category, correlation id.
- Level: `INFO`, `WARN`, `ERROR`. Category chuẩn: `App`, `Database`, `Security`, `Protection`, `Session`, `UI`;
  module mới thêm hằng số trong `LogCategories`.
- Vị trí: `%LOCALAPPDATA%\EduFocus\logs\edufocus-YYYYMMDD.log` (theo user, không cần admin).
- Xoay vòng theo ngày và dung lượng: tối đa **10 MB/file** (`edufocus-YYYYMMDD-001.log`, ...),
  giữ **14 ngày**; file cũ bị dọn khi khởi động hoặc sang ngày mới.
- Correlation: `ICorrelationContext` dùng `AsyncLocal`. App tạo id khi khởi động; mỗi thao tác
  phụ huynh, phiên học, protection engine và block page request mở scope riêng. Log ngoài scope
  ghi `cid=-`.
- Redaction tập trung ở `LogSanitizer` trước khi ghi: PIN, password, recovery code, token/API key,
  `Authorization`/`Bearer`, URL đầy đủ (chỉ còn domain đã chuẩn hóa) và control character;
  message tối đa 4.096 ký tự, exception message/stack cũng được redact.
- Không log toàn bộ window title, nội dung trang, query string, frame/camera hay dữ liệu sinh trắc học.
  Activity/attention code chỉ ghi domain đã chuẩn hóa và app/process đã `TextSanitizer`.
- Ghi log là best-effort: lỗi file/ổ đĩa bị nuốt, không làm app crash và không tạo dữ liệu ngoài máy.

### Audit log

- `AuditLogs` trong SQLite tách khỏi file log kỹ thuật; ghi qua `IAuditLogRepository`
  (`SqliteAuditLogRepository`).
- Chỉ thao tác phụ huynh/hệ thống quan trọng (PIN, protection, xóa dữ liệu, tamper...) mới ghi audit,
  kèm `CorrelationId` để đối chiếu với file log.
- Audit không chứa PIN, recovery code, token hoặc nội dung camera.
- Retention audit 365 ngày theo [database_schema.md](./database_schema.md) §14.

### Thu thập log khi báo lỗi

1. Mở `%LOCALAPPDATA%\EduFocus\logs` (thường là `C:\Users\<user>\AppData\Local\EduFocus\logs`).
2. Gửi các file `edufocus-*.log` quanh thời điểm lỗi (nén lại).
3. Kèm thời điểm UTC, phiên bản EduFocus, phiên bản Windows và các bước tái hiện.
4. Không đính kèm DB, ảnh/video webcam hoặc dữ liệu học sinh; dù log đã redact, vẫn kiểm tra trước khi chia sẻ.


---

## 13. Dependency Injection

`App.xaml.cs` (hoặc composition root mới) đăng ký:

```text
AddSqliteDataAccess()
AddUseCases()
AddInfrastructureServices()
AddViewModels()
AddViews()
```

- Singleton: `ParentSessionState`, `ProtectionEngine`, `WebcamAttentionMonitor`, `WatchdogService`, logger.
- Scoped/transient: repositories, use cases (trừ stateful).
- `IProtectionEnginePort` dự kiến được bọc bởi `LoggingProtectionEngineDecorator` (chưa triển khai).

> **Hiện trạng sau issue #16:** composition root gọi `AddInfrastructureServices()` (logger,
> monotonic clock), đăng ký SQLite repositories
> (`IParentCredentialRepository`, `IAppSettingsRepository`, `IStudySessionRepository`,
> `IAuditLogRepository`), `ParentAuthUseCase`, `ParentSessionState`, `ParentModeService`,
> `OnboardingService`, `ProtectionService`, `DatabaseBootstrapper`, `ProtectionStatusViewModel`,
> `MainWindowViewModel`, `TrayIconService` và các cửa sổ/ViewModel. Onboarding được mở trước dashboard
> ở mỗi lần khởi động; sau onboarding, `App` reconcile protection trước khi hiện cửa sổ chính.
> `IProtectionEnginePort` do `ProtectionEngine` triển khai từ issue #24; decorator logging chưa có.

> **Bổ sung sau issue #17:** composition root đăng ký thêm `IScheduleWindowRepository`,
> `IScheduleUseCase`/`ScheduleService`, `ScheduleViewModel` (singleton) và `ScheduleSchedulerService`.
> `App` chạy `ScheduleSchedulerService.EvaluateNowAsync()` sau reconcile protection và trước khi hiện
> cửa sổ chính, sau đó gọi `Start()` để timer 30 giây hoạt động; `ServiceProvider.Dispose()` dừng timer.

> **Bổ sung sau issue #18:** composition root đăng ký `IAppLifecycleUseCase`/`AppLifecycleService`,
> `AppExitState` (singleton), `ExitViewModel`/`ExitWindow` (transient) và `Func<ExitWindow>` cho tray.
> `MainWindow` nhận `TrayIconService` + `AppExitState` để chặn đóng cửa sổ; `App.OnSessionEnding` gọi
> lifecycle với timeout 2 giây. Việc thoát sạch đi qua `Application.Shutdown()` sau khi được duyệt, nên
> `ShutdownMode=OnMainWindowClose` không thể vô tình đóng app khi người dùng bấm X.

> **Bổ sung sau issue #24:** `AddInfrastructureServices()` đăng ký singleton `ProtectionEngine` cho
> `IProtectionEnginePort` kèm options debounce 30 giây; `DeferredProtectionEngine` đã bị xóa. Composition
> root đăng ký thêm `IBlockedAttemptRepository`/`SqliteBlockedAttemptRepository`. `ProtectionEngine`
> resolve `IHostsFileBlocker`, `IBlockPageServer`, `IProcessWatcher`, repository rule/session/attempt,
> monotonic clock, `TimeProvider` và logger qua DI.

> **Bổ sung sau issue #25:** composition root đăng ký `StudySessionHeartbeatOptions` và singleton
> `IStudySessionHeartbeatUseCase`/`StudySessionHeartbeatService`. `ProtectionService` và
> `AppLifecycleService` nhận heartbeat qua DI; `App.OnExit` dispose `ServiceProvider` bằng `DisposeAsync`
> để service ghi beat cuối và dừng timer. `ProtectionEngine` dùng targeted increment cho
> `BlockedAttemptCount` thay vì cập nhật toàn bộ entity session.

> **Bổ sung sau issue #26:** composition root đăng ký singleton `ActivityTrackingOptions`,
> `IActivitySampleRepository`/`SqliteActivitySampleRepository`, `IActivityUseCase`/`ActivityTrackingService`
> và `IBrowserUrlProviderPort`/`UiAutomationBrowserUrlProvider`; `AddInfrastructureServices()` đăng ký
> `IForegroundActivityPort`/`WindowsForegroundActivityPort`. `ProtectionService` và `AppLifecycleService`
> nhận `IActivityUseCase` qua DI; enable start activity sau heartbeat, disable/exit/shutdown stop trước khi
> kết thúc phiên, và lỗi start/stop activity chỉ được log để không chặn Protection.

> **Bổ sung sau issue #28:** composition root đăng ký singleton `ISystemPowerEventPort`/
> `WindowsSystemPowerEventPort`; `ActivityTrackingService` và `StudySessionHeartbeatService` nhận port này
> qua constructor, subscribe khi start và unsubscribe khi stop. Adapter giữ `SystemEvents` subscription
> trong suốt vòng đời singleton và dispose cùng `ServiceProvider`.

> **Bổ sung sau issue #29:** composition root đăng ký `IRetentionRepository`/`SqliteRetentionRepository`,
> `RetentionOptions` (batch 500, audit 365 ngày), singleton `IRetentionUseCase`/`RetentionService` và
> `RetentionSchedulerService`. `App` gọi `RetentionSchedulerService.Start()` sau khi hiện cửa sổ chính và
> scheduler đã chạy; service chạy pass đầu tiên trên thread pool, sau đó kiểm tra mỗi phút và chạy khi
> local time ≥ 03:00 (mỗi ngày một lần). `ServiceProvider.Dispose()` dừng `DispatcherTimer`.

> **Bổ sung sau issue #30:** composition root đăng ký `ICameraDeviceCatalog`/`WindowsCameraDeviceCatalog`,
> `CameraPortOptions` (MSMF 640×480), `ICameraPort`/`OpenCvCameraPort`, `CameraOptions` (timeout 5 giây,
> retry 30 giây → 5 phút, ngưỡng 3 lỗi capture), `ICameraSessionUseCase`/`CameraSessionService` và
> `CameraStatusViewModel`; WPF tham chiếu project `EduFocus.Infrastructure.Attention`. `ServiceProvider.DisposeAsync()`
> đóng camera qua `OpenCvCameraPort.Dispose()`. Camera chỉ được start/stop theo vòng đời phiên học ở #33.

> **Bổ sung sau issue #31:** composition root đăng ký `AttentionAnalyzerOptions`,
> `AttentionPipelineOptions.Default` (1 giây), `IAttentionFrameAnalyzer` qua `AttentionAnalyzerFactory` với
> backend YuNet và thư mục `models/` cạnh executable, cùng singleton `IAttentionPipelineUseCase`/
> `AttentionPipelineService`. Pipeline **không được resolve/start** ở startup: model ONNX chỉ được tải khi
> `ProtectionService` start camera theo phiên học (#33); thiếu model hoặc lỗi camera chỉ log và không chặn
> bảo vệ. `ServiceProvider.DisposeAsync()` dừng sampling và đóng camera nếu pipeline đã start.

> **Bổ sung sau issue #32:** composition root đăng ký `IAttentionEventRepository`/
> `SqliteAttentionEventRepository`, `AttentionThresholds`, `AttentionMonitoringOptions.Default` (cửa sổ 6
> giây, 3 mẫu tối thiểu, majority 0.6, gap tối đa 5 giây) và singleton `IAttentionMonitorUseCase`/
> `AttentionMonitorService`. Monitor **chưa được start** ở startup; #33 start/stop theo phiên học.
> `ServiceProvider.DisposeAsync()` đóng event đang mở nếu monitor đã start.

> **Bổ sung sau issue #33:** `ProtectionService` và `AppLifecycleService` nhận thêm
> `IAttentionPipelineUseCase` + `IAttentionMonitorUseCase`. Khi bật bảo vệ hoặc phục hồi sau crash, monitor
> start trước (subscribe sample) rồi pipeline; khi tắt, reconcile hoặc Windows shutdown, pipeline stop trước
> rồi monitor drain và đóng event. Session có `MonitoringEnabled = false` không mở camera. `ActivityTrackingService`
> nhận `IAppSettingsRepository` để cộng `MonitoringEligibleSeconds` theo policy tại thời điểm start.

---

## 14. Cấu trúc thư mục đích

```text
EduFocus/
├── EduFocus.slnx
├── src/
│   ├── EduFocus.Domain/
│   │   ├── Entities/
│   │   ├── Enums/
│   │   ├── ValueObjects/
│   │   └── EduFocus.Domain.csproj
│   ├── EduFocus.Ports/
│   │   ├── Inbound/
│   │   ├── Outbound/
│   │   ├── DTOs/
│   │   └── EduFocus.Ports.csproj
│   ├── EduFocus.UseCases/
│   │   ├── Auth/
│   │   ├── Protection/
│   │   ├── Sessions/
│   │   ├── Retention/
│   │   ├── Attention/
│   │   ├── Reports/
│   │   └── EduFocus.UseCases.csproj
│   ├── EduFocus.Infrastructure/
│   │   ├── Protection/
│   │   ├── Activity/
│   │   ├── Attention/
│   │   ├── Auth/
│   │   ├── Watchdog/
│   │   └── EduFocus.Infrastructure.csproj
│   ├── EduFocus.Infrastructure.Attention/
│   │   ├── Camera/          (OpenCvCameraPort, WindowsCameraDeviceCatalog, OpenCvCameraSource)
│   │   └── ... (adapter OpenCvSharp/ONNX từ spike attention)
│   ├── EduFocus.SqliteDataAccess/
│   │   ├── AppDbContext.cs
│   │   ├── Repositories/
│   │   ├── Migrations/
│   │   └── EduFocus.SqliteDataAccess.csproj
│   ├── EduFocus.WPF/
│   │   ├── Views/
│   │   ├── ViewModels/
│   │   ├── Services/
│   │   ├── App.xaml
│   │   └── EduFocus.WPF.csproj
│   └── EduFocus.Domain.Tests/
├── installer/
│   └── Product.wxs
├── docs/
│   ├── ba_document.md
│   ├── use_cases.md
│   ├── system_architecture.md
│   ├── database_schema.md
│   ├── domain-invariants.md
│   └── legacy-cleanup.md
└── .github/workflows/
```

> Phase 1 (issue #5) đã đổi đồng bộ tên file, namespace, solution và CI sang `EduFocus.*`.
>
> Issue #7 đã xóa toàn bộ code 1.x; issue #8 đã thêm domain model 2.0; issue #9 đã thêm
> `EduFocus.SqliteDataAccess` (DbContext, configuration, converter, migration
> `InitialEduFocusSchema`, initializer + backup); issue #10 thêm ACL thư mục dữ liệu và
> quy trình verify/restore backup; issue #11 cập nhật CI; issue #12 thêm structured logging,
> correlation id và audit repository; issue #13 thêm `ParentAuthUseCase`, PBKDF2 hasher,
> recovery-code generator và credential repository; issue #14 thêm `ParentSessionState`,
> `ParentModeService` và cửa sổ Parent Mode trong WPF (login, lockout, đổi/reset PIN);
> issue #15 thêm consent/repository AppSettings, migration `AddOnboardingConsent`, use case
> onboarding có resume và wizard first-run chặn dashboard đến khi hoàn tất; issue #16 thêm
> `IProtectionEnginePort`, state machine `ProtectionService`, session repository, reconcile và tray;
> issue #17 thêm lịch bảo vệ: `ScheduleService`, `ProtectionSource` + migration `AddProtectionSource`,
> repository `ScheduleWindows`, scheduler 30 giây và thẻ lịch trong Parent Mode.
> Inventory 1.x ở [legacy-cleanup.md](./legacy-cleanup.md), invariant domain ở
> [domain-invariants.md](./domain-invariants.md).

---

## 15. Migration từ codebase hiện tại

| Hiện tại | Đích |
|---|---|
| `WindowsStrictEngine.cs` | `ProtectionEngine.cs` — rule động, không hard-code whitelist |
| `LocalRedirectServer.cs` | `BlockPageServer.cs` — trang chặn mới, không guilt-trip |
| `_distractingDomains` | `ChatbotBlocklist.cs` + `BlockRules` trong DB |
| `SystemSettings.cs` | `AppSettings.cs` + `ParentCredential.cs` |
| `FocusSessionUseCase.cs` | `StudySessionUseCase.cs` + `ProtectionUseCase.cs` |
| `FocusTaskUseCase.cs`, `ProfileUseCase.cs` | Xóa |
| `QuoteUseCase.cs`, `GeminiAiServices.cs` | Xóa |
| `ActiveSessionState.cs` | `ProtectionSessionState.cs` |
| `DashboardView`, `ProfilesView`, `FocusSessionView`, `AnalyticsView` | Thay bằng view mới |
| `AppDbContext` + migrations | Schema mới, DB mới `edufocus.db` |
| `installer/Product.wxs` | Đổi tên/sản phẩm, thêm scheduled task/service |
| CI release | Đổi artifact, app name, product name |

**Không migrate dữ liệu 1.x** vì schema và mục đích sản phẩm khác. Nếu cần giữ blocklist tùy chỉnh, cung cấp tool import riêng (post-MVP).

> **Đã thực thi ở issue #7:** các dòng "Xóa"/"Thay thế" trong bảng đã được áp dụng cho code 1.x;
> `WindowsStrictEngine`, `LocalRedirectServer`, `GeminiAiServices`, entity/usecase/repository/
> migration và view 1.x không còn trong build. Installer và GitHub Release đã đổi tên EduFocus.
> Xem [legacy-cleanup.md](./legacy-cleanup.md).

---

## 16. Testing Strategy

### Unit tests
- Domain: PIN validation, rule matching, attention score, session totals, schedule.
- Privacy: `LogSanitizer` che PIN/token/URL, giữ exception đọc được; `FileLogger` format UTC/category/correlation, rotation và retention.
- UseCases: protection enable/disable, permission checks, lockout, retention.
- Parent auth: setup/đổi PIN, xác thực, lockout dùng chung PIN + recovery code, xoay recovery code; fake hasher/repository/audit.
- Fake ports; không chạm Windows API.

### Integration tests
- SQLite repositories + migrations.
- Rule persistence, session aggregation.
- Retention deletion.
- Audit log ghi kèm `CorrelationId`.
- Parent credential round-trip với PBKDF2 thật; PIN/recovery hoạt động sau khi tạo lại repository (mô phỏng restart).
- Backup verify/rotation/restore, corrupt DB, data-directory ACL.

### CI
- `.github/workflows/ci.yml` — Windows runner, .NET 10: restore, build/test Release, publish framework-dependent.
- Cache NuGet theo csproj/tool manifest; upload TRX và bản publish làm artifact; không cache/upload DB hay secret.
- Job **EduFocus — Build & Test** là required check khi bật branch protection cho `master`.

### Manual/system tests
- Chặn web trên Chrome/Edge/Firefox; xác nhận HTTP block page và HTTPS báo lỗi.
- BlockedAttempt best-effort: không kỳ vọng log HTTPS.
- Restore hosts sạch sau crash; chỉ xóa marker EduFocus.
- Auto-start + crash recovery; session cũ `Interrupted`, session mới `Trigger = Recovery`.
- Camera bận (Meet/Teams) + Multi-App Camera.
- Port 80 bị chiếm.
- Không có quyền admin.
- Sleep/resume qua schedule.
- Parent PIN/recovery shared lockout.
- Dữ liệu không rời máy (network monitor).
- Không có file ảnh/video trong thư mục dữ liệu.

### Performance tests
- Idle CPU/RAM.
- CPU khi camera 1–2 FPS.
- DB size sau 30 ngày.
- Startup time.

### Acceptance
- Xem checklist trong [BA Document](./ba_document.md#9-yêu-cầu-phi-chức-năng-nfr) và [Use Cases](./use_cases.md).

---

## 17. Architecture Decision Records (ADR)

| ADR | Quyết định | Lý do |
|---|---|---|
| ADR-01 | Giữ Clean Architecture | Code testable, dễ thay hạ tầng |
| ADR-02 | Local-first, không cloud | Quyền riêng tư trẻ em, đơn giản vận hành |
| ADR-03 | Webcam attention estimate, không lưu frame | Cân bằng tính năng và quyền riêng tư |
| ADR-04 | Hosts + block page HTTP cho MVP, domain exact | Tái sử dụng code, hoạt động mọi browser; chấp nhận không log được HTTPS |
| ADR-05 | Browser extension sau MVP | URL/path chính xác, logging và giảm bypass |
| ADR-06 | Wall clock cho schedule; monotonic chỉ đo elapsed/detect clock jump | Schedule cần local time; monotonic không tồn tại xuyên reboot |
| ADR-07 | Một app elevated cho MVP, service sau | Giảm độ phức tạp ban đầu |
| ADR-08 | DB ở ProgramData + ACL Admin/SYSTEM | Học sinh standard user không đọc/ghi; phụ huynh qua app |
| ADR-09 | UTC trong DB, local khi hiển thị | Tránh lỗi timezone/DST |
| ADR-10 | PBKDF2-SHA256 600k | Tiêu chuẩn hiện đại, dễ triển khai .NET |
| ADR-11 | CameraUnavailable là trạng thái hạng nhất | Không đánh giá sai khi camera bận |
| ADR-12 | Không dùng kernel driver/WinDivert | Tránh antivirus, BSOD, signing |
| ADR-13 | BlockedAttempt là best-effort | Hosts không cung cấp log HTTPS; không được quảng cáo số liệu tuyệt đối |
| ADR-14 | URL/path rules chỉ enforce khi có extension | Hosts không thể match path; tránh UI hứa hẹn sai |
| ADR-15 | Không có secret phụ trợ | Chỉ lưu hash PIN/recovery; giảm bề mặt tấn công và phức tạp |
| ADR-16 | Thoát app luôn cần PIN | Nhất quán bảo vệ; Windows shutdown vẫn được phép |
| ADR-17 | (Đề xuất) OpenCV MSMF + ONNX Runtime + YuNet cho attention Phase 0; Haar chỉ baseline; Drowsy/gaze ở Phase 5 | Face 99,2%/30 phút, CPU 0,23% máy, RAM 184 MB; Haar bỏ sót nhiều; giữ local-first |

---

## 18. Rủi ro kiến trúc

| Rủi ro | Ảnh hưởng | Giảm thiểu |
|---|---|---|
| Camera không chia sẻ được | Mất dữ liệu attention khi Meet/Teams | Multi-App Camera; fallback; companion app |
| Hosts bị bypass | Chặn không hiệu quả | Extension/firewall/DNS policy |
| Hosts không log được HTTPS | Số blocked attempts thấp/sai | Ghi rõ best-effort; extension post-MVP; không dùng làm KPI |
| SQLite bị học sinh đọc | Lộ báo cáo | ACL; mã hóa DB post-MVP nếu cần |
| App bị kill | Mất protection | Watchdog; service; ghi rõ độ trễ |
| Quá nhiều sample | DB phình | Aggregate + retention |
| ONNX model nặng | CPU/RAM cao | Model nhẹ, 1–2 FPS, thread limit |
| License model ONNX/MediaPipe | Không được phân phối | Rà soát license/attribution trước release |
| Quyền admin | Bypass toàn bộ | Giới hạn đã biết; tài khoản Windows riêng |

---

## 19. Tài liệu liên quan

- [BA Document](./ba_document.md)
- [Use Cases](./use_cases.md)
- [Database Schema](./database_schema.md)
- [Domain Invariants](./domain-invariants.md)
- [Security Policy](../SECURITY.md)
- [Contributing](../CONTRIBUTING.md)
