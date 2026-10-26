# Kiến trúc Hệ thống - EduFocus

> **Trạng thái:** Thiết kế đích / đang chuyển đổi từ **Don't Be Lazy**.
> **Phiên bản:** 2.0 — 2026-10-26
>
> Project, namespace và assembly trong mã nguồn đã được đổi sang `EduFocus.*`. Tài liệu này mô tả kiến trúc **đích** của EduFocus.

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

**Value Object:** giữ pattern `StronglyTypedId` (`SessionId`, `RuleId`, `CredentialId`…).

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
IParentAuthUseCase
IProtectionUseCase
IStudySessionUseCase
IActivityUseCase
IAttentionUseCase
IBlockRuleUseCase
IScheduleUseCase
IReportUseCase
IDataRetentionUseCase
```

**Outbound**
```text
IProtectionEnginePort
IBlockPageHostPort
IForegroundActivityPort
IBrowserUrlProviderPort
IWebcamAttentionPort
IMonotonicClockPort
IParentCredentialRepository
IAppSettingsRepository
IBlockRuleRepository
IScheduleRepository
IStudySessionRepository
IActivitySampleRepository
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
| `ParentAuthUseCase` | Setup PIN, login, lockout, đổi PIN, recovery |
| `ProtectionUseCase` | Bật/tắt bảo vệ, áp dụng/gỡ rule, phục hồi trạng thái |
| `StudySessionUseCase` | Bắt đầu/kết thúc session, tổng hợp thời gian |
| `ActivityUseCase` | Ghi sample, phân loại app/URL, phát hiện lớp online |
| `AttentionUseCase` | Nhận trạng thái từ camera, tạo event, tính score |
| `BlockRuleUseCase` | CRUD rule, validate, allowlist override |
| `ScheduleUseCase` | Đánh giá khung giờ, auto ON/OFF |
| `ReportUseCase` | Query dashboard, timeline, export |
| `DataRetentionUseCase` | Xóa dữ liệu quá hạn, xóa theo yêu cầu |

**Decorator:** giữ pattern logging decorator cho các use case nhạy cảm (Protection, ParentAuth).

### 3.4. Infrastructure (`EduFocus.Infrastructure`)

Triển khai outbound ports, tương tác Windows.

| Thành phần | Vai trò |
|---|---|
| `ProtectionEngine` | Refactor từ `WindowsStrictEngine`: hosts, block page server, process watcher, rule matching |
| `BlockPageServer` | Local HTTP server trả trang chặn; refactor từ `LocalRedirectServer` |
| `ChatbotBlocklist` | Danh sách domain chatbot mặc định, có version |
| `ForegroundActivityTracker` | Win32 foreground window + process + idle time |
| `BrowserUrlProvider` | UI Automation (MVP); browser extension native messaging (post-MVP) |
| `WebcamAttentionMonitor` | OpenCV + ONNX; face/head/iris/EAR; không lưu frame |
| `CameraAvailabilityProbe` | Kiểm tra camera bận/không có quyền |
| `ParentAuthService` | PBKDF2, constant-time compare, recovery code, shared lockout |
| `ParentAuthorizer` | Kiểm tra Parent Mode cho use case nhạy cảm; chặn exit khi chưa PIN |
| `WatchdogService` | Scheduled task (MVP, poll 30–60 giây) / Windows Service (post-MVP) |
| `FileLogger` | Log xoay vòng theo ngày, redact dữ liệu nhạy cảm |
| `MonotonicClockAdapter` | Giữ từ dự án cũ |

### 3.5. Persistence (`EduFocus.Infrastructure.Persistence`)

- `AppDbContext` ánh xạ domain 2.0 sang SQLite; timestamp được chuyển sang ISO-8601 UTC.
- Migration đầu `InitialEduFocusSchema` tạo các bảng, index và foreign key theo `docs/database_schema.md`.
- Partial unique index bảo đảm tối đa một session `Active`; foreign keys bật trên mọi kết nối.
- `DatabaseInitializer` dùng `%ProgramData%\EduFocus\edufocus.db`, bật WAL và incremental auto-vacuum, backup trước migration và chỉ giữ 3 backup gần nhất.
- Migration lỗi được log; DB hiện có được giữ nguyên để phục hồi thủ công từ backup. DB không có EF migration history sẽ bị từ chối; không mở/migrate DB ở đường dẫn `DontBeLazy` 1.x.

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
  → AttentionUseCase.RecordAsync(state, confidence)
  → aggregate every 10s
  → AttentionEvent (khi state đổi)
  → frame disposed (không ghi disk)
```

### 4.4. Schedule

```text
Timer 30s
  → ScheduleUseCase.Evaluate(localNow)
  → nếu vào window và OFF → ProtectionUseCase.EnableAsync(System)
  → nếu hết window và ON do schedule → ProtectionUseCase.DisableAsync(System)
  → Manual override được ghi audit và hết hiệu lực khi window kết thúc
```

Schedule dùng **wall clock/local time** để xác định khung giờ. Monotonic clock chỉ dùng đo elapsed trong một process và phát hiện nhảy giờ; không dùng monotonic xuyên reboot. Khi phát hiện nhảy giờ, ghi audit `TamperDetected` và không tự tắt protection chỉ vì thay đổi giờ.

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

Watchdog MVP kiểm tra mỗi 30–60 giây và chỉ restart khi `ProtectionEnabled = true` và lần thoát trước không phải clean exit. Exit hợp lệ bằng PIN đặt `ProtectionEnabled = false`, ghi `LastCleanShutdownAtUtc` và không tự bật lại. Windows shutdown giữ nguyên `ProtectionEnabled` và không ghi clean exit để lần đăng nhập sau phục hồi bảo vệ.

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

### 6.2. Website blocking

- Ghi block vào `%SystemRoot%\System32\drivers\etc\hosts` giữa marker riêng:
  ```text
  ### EDUFOCUS BLOCK START ###
  127.0.0.1 chatgpt.com
  127.0.0.1 www.chatgpt.com
  ...
  ### EDUFOCUS BLOCK END ###
  ```
- Chỉ chặn domain exact; không dùng wildcard/suffix. Thêm biến thể `www.` khi phù hợp, trừ khi có rule Allow cho chính biến thể đó.
- Không sửa các dòng khác của hosts; chỉ xóa block trong marker EduFocus.
- Flush DNS sau khi thay đổi.
- Self-heal: xóa block mồ côi của EduFocus khi khởi động.
- `BlockPageServer` bind `127.0.0.1:80`, chỉ phục vụ HTTP, trả HTML tĩnh; kiểm tra Host header thuộc domain bị chặn, escape output, không tải tài nguyên ngoài. Nếu port 80 bị chiếm, protection vẫn ON nhưng block page không khả dụng.
- Retry khi file bị antivirus khóa; báo lỗi rõ nếu thất bại.

### 6.3. App blocking (MVP)

- Process watcher dùng WMI + polling fallback.
- Chỉ xử lý process nằm trong danh sách Block; Allowlist app được ưu tiên.
- Gửi `WM_CLOSE` tới cửa sổ chính trước, chờ tối đa 2 giây; nếu process vẫn chạy thì kill process tree.
- Không kill process hệ thống hoặc process của EduFocus.
- Ghi `BlockedAttempt` với `ObservedVia = ProcessWatcher`; debounce 30 giây theo process.
- Kill process có thể làm mất dữ liệu chưa lưu; UI phải thông báo và giới hạn này phải được ghi trong known limitations.

### 6.4. Hạn chế đã biết

- DoH/DoT, VPN, proxy có thể bypass hosts.
- Hosts không tạo log truy cập HTTPS; không thể đếm đầy đủ blocked attempts.
- Trang chặn chỉ hiển thị với HTTP; HTTPS thường báo lỗi kết nối/SSL.
- Browser extension (post-MVP) giải quyết URL/path và logging chính xác hơn.
- Process watcher dựa trên tên/đường dẫn process; app đổi tên, chạy portable hoặc khởi động/thoát quá nhanh có thể không bị phát hiện.
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

- Google Meet: `meet.google.com` hoặc window title chứa `Meet`.
- Microsoft Teams: process `ms-teams`/`Teams` hoặc URL `teams.microsoft.com`.
- Zoom: process `Zoom`.
- Lưu `IsOnlineClass` để báo cáo riêng.

### 7.4. Sampling

- Mỗi 10–30 giây.
- Heartbeat session mỗi 10 giây, độc lập với sample.
- Không ghi khi idle > 5 phút (chỉ ghi một sample idle); `IdleSeconds` lưu riêng.
- Debounce chuyển foreground 2 giây.
- Sample cũ bị retention job xóa; báo cáo dài hạn đọc session summary.

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

### 8.2. State machine

| State | Điều kiện |
|---|---|
| `Focused` | Có mặt, head pose trong ngưỡng, mắt mở, gaze on-screen |
| `Distracted` | Head/gaze lệch > ngưỡng trong > 20 giây |
| `Absent` | Không thấy mặt > 30 giây |
| `Drowsy` | Mắt nhắm lặp lại > 2 giây |
| `CameraUnavailable` | Không mở được camera / camera bận / không có quyền |

### 8.3. Scoring

```text
AttentionPercent =
    FocusedSeconds / (FocusedSeconds + DistractedSeconds + AbsentSeconds + DrowsySeconds)
```

- `MonitoredSeconds` = tổng bốn trạng thái trên (camera khả dụng).
- Không tính `CameraUnavailable` vào mẫu số attention.
- `MonitoringEligibleSeconds` = thời gian monitoring được kỳ vọng (toàn session hoặc chỉ lúc lớp online nếu `MonitorOnlyDuringClasses`).
- Báo cáo luôn hiển thị:
  - `Coverage = MonitoredSeconds / MonitoringEligibleSeconds`; null nếu eligible = 0.
  - `ConfidenceAvg` trung bình có trọng số theo thời lượng event, dựa trên face detection score, ánh sáng, số khuôn mặt.
- Nếu coverage < 50%, dashboard hiển thị cảnh báo "Dữ liệu chưa đủ tin cậy".
- Khi crash, mọi `AttentionEvent` đang mở được đóng tại `LastHeartbeatAtUtc`; khi session/monitoring dừng bình thường, đóng tại thời điểm dừng.

### 8.4. Camera contention

- Windows 11 build mới hỗ trợ **Multi-App Camera**.
- App kiểm tra và hướng dẫn phụ huynh bật trong Settings → Bluetooth & devices → Cameras.
- Nếu không hỗ trợ:
  - Ghi `CameraUnavailable` trong thời gian Meet/Teams dùng camera.
  - Không kết luận mất tập trung.
  - Báo cáo ghi rõ thời gian không quan sát được.
- Post-MVP: companion phone camera hoặc virtual camera.

### 8.5. Privacy

- Không ghi frame, không gửi network.
- Không lưu face embedding/template.
- Frame được `Dispose()` ngay sau xử lý.
- Không nhận diện danh tính.
- Có chỉ báo camera đang hoạt động (tray + UI).

---

## 9. Parent Authentication & State

### 9.1. Credential

- PIN 6–8 chữ số.
- PBKDF2-SHA256, 600.000 iterations, salt 16 byte.
- Recovery code 12 ký tự, hash tương tự PIN; chỉ hiển thị một lần, không thể xem lại.
- Constant-time compare.
- Lockout 5 lần / 15 phút, dùng chung cho PIN và recovery code.
- Không lưu plaintext; không log PIN/recovery code.

### 9.2. Parent session

- `ParentSessionState` singleton:
  - `IsAuthenticated`
  - `LastActivityAt`
  - `LockAsync()` sau 5 phút idle.
- Mọi use case nhạy cảm kiểm tra `IParentAuthorizer` trước khi thực thi.
- Thoát app luôn yêu cầu PIN, kể cả Protection OFF; Windows shutdown/logoff không chặn.

### 9.3. Protection state machine

```text
OFF ──Enable (Parent/System)──> ON
ON  ──Disable (Parent/System)──> OFF (Completed)
ON  ──Crash/Restart───────────> ON (recover; old session Interrupted)
ON  ──Schedule end───────────> OFF (Completed)
ON  ──Parent override────────> OFF (until current window end)
```

Trạng thái lưu trong `AppSettings.ProtectionEnabled`; session active lưu trong `StudySessions`. Khi phục hồi trong thời gian bảo vệ, tạo session mới với `Trigger = Recovery`, không cộng thời gian app tắt.

---

## 10. Persistence & Privacy

- DB: `%ProgramData%\EduFocus\edufocus.db`.
- Logs: `%ProgramData%\EduFocus\logs`.
- ACL: Administrators/SYSTEM full; học sinh standard user không đọc/ghi. Phụ huynh thao tác qua app elevated, không cấp quyền file trực tiếp.
- Timestamp lưu **UTC**; hiển thị local. Query theo ngày dùng local boundary chuyển sang UTC.
- Session summary giữ lâu hơn sample chi tiết (365 ngày vs 30 ngày).
- Retention job chạy mỗi ngày; xóa theo batch để tránh khóa DB lâu.
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
| Watchdog | Scheduled Task restart khi Protection ON, poll 30–60 giây; không restart sau exit sạch | Windows Service |
| Chặn kill process | Hạn chế | Process ACL + SYSTEM |
| Chống sửa hosts | Self-heal | Service + ACL |
| Chống uninstall | Best-effort PIN prompt | Service + policy |
| Phát hiện đổi giờ | Ghi audit | Ghi audit + policy |
| Chống admin | ❌ (giới hạn đã biết) | Một phần |

**Tuyên bố đúng:** EduFocus chống tắt **ở mức hợp lý**, không chống được admin/SYSTEM, không chặn Windows shutdown, và scheduled-task watchdog có độ trễ 30–60 giây. Không quảng cáo "không thể tắt".

---

## 12. Logging & Observability

- `FileLogger` ghi theo ngày: `app-YYYY-MM-DD.log`.
- Mức log: Info, Warning, Error.
- Redact:
  - Không log PIN/recovery code.
  - Không log URL đầy đủ/query string; chỉ domain đã chuẩn hóa.
  - Không log window title đầy đủ nếu có khả năng chứa nội dung nhạy cảm.
  - Không log frame/camera data.
- Correlation ID cho mỗi phiên và thao tác.
- Audit log riêng trong DB cho thao tác phụ huynh.
- Không gửi log ra ngoài.

---

## 13. Dependency Injection

`App.xaml.cs` (hoặc composition root mới) đăng ký:

```text
AddEduFocusPersistence(databasePath)
AddUseCases()
AddInfrastructureServices()
AddViewModels()
AddViews()
```

- Singleton: `ParentSessionState`, `ProtectionEngine`, `WebcamAttentionMonitor`, `WatchdogService`, logger.
- Scoped/transient: repositories, use cases (trừ stateful).
- `IProtectionEnginePort` được bọc bởi `LoggingProtectionEngineDecorator`.

---

## 14. Cấu trúc hiện có trong repository

```text
EduFocus/
├── EduFocus.slnx
├── src/
│   ├── EduFocus.Domain/Attention/
│   ├── EduFocus.Domain.Tests/Attention/
│   ├── EduFocus.Ports/DTOs/AttentionDtos.cs
│   ├── EduFocus.Ports/Outbound/Services/
│   ├── EduFocus.Infrastructure/Logging/
│   ├── EduFocus.Infrastructure/Services/MonotonicClockAdapter.cs
│   ├── EduFocus.Infrastructure.Attention/
│   └── EduFocus.WPF/ (App.xaml, MainWindow.xaml, manifests)
├── tools/
│   ├── AttentionPipelineSpike/
│   ├── BlockingPipelineSpike/
│   └── WatchdogSpike/
├── installer/Product.wxs
└── .github/workflows/
```

Các project trong solution chứa domain 2.0, persistence SQLite/migration, mô hình attention và dịch vụ nền dùng chung, cùng cửa sổ EduFocus 2.0 tối giản. Các spike trong `tools/` chạy độc lập, không được đăng ký hoặc khởi chạy bởi ứng dụng. Persistence đã sẵn sàng nhưng chưa được gọi từ WPF startup; việc khởi tạo UI/data flow thuộc các issue tiếp theo.

## 15. Dọn codebase 1.x

| Thành phần cũ | Trạng thái sau cleanup |
|---|---|
| Task, profile, Pomodoro/session, analytics, settings và quote entities/use cases | Đã xóa khỏi solution; thay thế theo các phase tính năng 2.0, chưa được kích hoạt. |
| Gemini AI, guilt-trip, friction gate và quote asset | Đã xóa khỏi product code và output. |
| WindowsStrictEngine, LocalRedirectServer, tray, updater và auto-start cũ | Đã xóa khỏi WPF/runtime registrations; blocking/watchdog hiện chỉ có spike thủ công trong `tools/`. |
| DbContext, EF migrations và repositories dựa trên model 1.x | Đã xóa. Persistence 2.0 tạo DB riêng, không import dữ liệu 1.x; DB có schema không nhận diện sẽ không được migrate. |
| Icon, UI và tên Don't Be Lazy | Asset icon và các màn hình cũ đã xóa; app/MSI dùng tên EduFocus. Lịch sử Git và tài liệu ghi nhận 1.x được giữ lại. |
| Attention classifier/camera spike | Giữ lại riêng như nền tảng spike; WPF hiện không khởi tạo camera hoặc thu thập dữ liệu. |

Không migrate dữ liệu 1.x vì mục đích và schema sản phẩm khác. Blocklist tùy chỉnh chỉ được nhập nếu có công cụ riêng được thiết kế và kiểm thử sau này.

---

## 16. Testing Strategy

> Test tự động hiện bao gồm unit test domain/attention và integration test SQLite cho migration, UTC mapping, active-session unique index, backup, PRAGMA và từ chối đường dẫn DB 1.x.


### Unit tests
- Domain: PIN validation, rule matching, attention score, session totals, schedule.
- UseCases: protection enable/disable, permission checks, lockout, retention.
- Fake ports; không chạm Windows API.

### Integration tests
- SQLite migration, UTC conversion, foreign keys and unique active-session index.
- Backup-before-migration and retained DB on failure.
- Rule persistence, session aggregation and retention deletion (future issues).

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
- [Security Policy](../SECURITY.md)
- [Contributing](../CONTRIBUTING.md)
