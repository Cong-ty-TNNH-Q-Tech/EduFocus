# 📝 Changelog

Tất cả các thay đổi đáng chú ý của dự án sẽ được ghi lại tại đây.

Định dạng dựa trên [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
và dự án tuân theo [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [2.0.1] - 2026-10-31

### Fixed (Installer — chặn nhầm Windows 10/11)

- **Nguyên nhân:** Windows Installer báo `VersionNT = 603` và `WindowsBuild = 9600` trên Windows 10/11 với gói MSI không có OS manifest, nên launch condition `VersionNT >= 1000` luôn sai. MSI 2.0.0 dừng ngay khi chạy với hộp thoại "EduFocus requires Windows 10 or later." trên cả Windows 10 và 11 (xác nhận bằng log MSI và probe MSI).
- **Sửa:** `RegistrySearch` đọc `CurrentBuildNumber` thật từ registry và launch condition kiểm tra `>= 10240`; thêm `Installed OR` để gỡ cài đặt không bị chặn. Điều kiện `VersionNT64` giữ nguyên.

### Fixed (Installer — thông báo quyền riêng tư)

- **Nội dung bị ẩn:** `ScrollableText` hiển thị hộp trắng trong msiexec trên Windows 10/11 nên toàn bộ nội dung thông báo quyền riêng tư không đọc được. Thay bằng control `Text` tự xuống dòng, tăng chiều cao dialog để đủ 7 đoạn.
- **Đoạn giữa bị thay bằng `...`:** msiexec áp dụng path ellipsis khi nội dung chứa dấu `\`; bỏ dấu `\` khỏi văn bản trong dialog. Đã xác minh bằng MSI dựng lại: thông báo hiển thị đủ, Next bị chặn khi chưa tick, tick xong mới sang Destination Folder.

### Breaking changes

- Không có. Bản vá chỉ sửa launch condition và hộp thoại cài đặt; schema dữ liệu, định dạng file và API giữ nguyên.

---

## [2.0.0] - 2026-10-30

> **Trạng thái:** Bản phát hành đầu tiên (MVP desktop) của EduFocus 2.0; các mục Phase 1–7 dưới đây đã được triển khai.

### Added (Docs & Product Definition)
- **Pivot sản phẩm:** Don't Be Lazy → **EduFocus**, ứng dụng giám sát học tập có trách nhiệm dành cho phụ huynh.
- Viết lại `docs/ba_document.md`: mục tiêu, actors, phạm vi, yêu cầu chức năng, business rules, NFR, privacy, acceptance.
- Viết lại `docs/use_cases.md`: UC01–UC14, permission matrix, luồng buổi học mẫu, yêu cầu dữ liệu.
- Viết lại `docs/system_architecture.md`: Clean Architecture, Ports & Adapters, ProtectionEngine, BlockPageServer, ActivityTracker, WebcamAttentionMonitor, Watchdog, ADR.
- Viết lại `docs/database_schema.md`: schema SQLite mới, index, retention, báo cáo, bảo mật dữ liệu.
- Viết lại `README.md` và `README.en.md` cho EduFocus.
- **Parent PIN:** 6–8 chữ số, PBKDF2-SHA256 600.000 iterations, recovery code, khóa chung 5 lần sai / 15 phút.
- **Protection ON/OFF + schedule:** bật/tắt thủ công hoặc theo khung giờ; phục hồi trạng thái sau crash; session crash `Interrupted`, phiên phục hồi `Trigger=Recovery`.
- **Chatbot blocklist:** domain exact + app, allowlist/blocklist tùy chỉnh, có thời hạn. URL/path là post-MVP.
- **Study session & activity tracking:** app/domain foreground (best-effort), nhận diện Google Meet/Teams/Zoom; `ActiveSeconds` và heartbeat.
- **Blocked attempts:** chỉ ghi khi quan sát được; hosts không log HTTPS nên số liệu là best-effort.
- **Attention estimate:** OpenCvSharp4 + ONNX Runtime; trạng thái Focused/Distracted/Absent/Drowsy/CameraUnavailable; coverage theo `MonitoringEligibleSeconds`; chỉ lưu event, không lưu frame/video.
- **Local-first privacy:** SQLite tại `%ProgramData%\EduFocus`, không cloud, không telemetry.
- **Retention:** 30 ngày activity sample, 90 ngày attention event/blocked attempt, 365 ngày session summary/audit.
- **Legal:** ghi nhận Luật 91/2025/QH15 và Nghị định 356/2025/NĐ-CP; cần DPIA trước phát hành.

### Changed
- Đổi định vị từ công cụ "kỷ luật bản thân" sang công cụ **hỗ trợ phụ huynh**, không dùng cơ chế guilt-trip.
- Trạng thái bảo vệ chuyển từ "phiên tập trung có đếm ngược" sang **Protection ON/OFF** do phụ huynh kiểm soát.
- Dữ liệu chuyển từ `%AppData%` sang `%ProgramData%\EduFocus` với ACL cho Administrators/SYSTEM.
- Database schema mới; không migrate dữ liệu từ 1.x.

### Changed (Phase 1 — Foundation)
- **Đổi tên codebase (issue #5):** solution, toàn bộ project, namespace, assembly và test project chuyển từ `DontBeLazy.*` sang `EduFocus.*`; file thực thi đổi thành `EduFocus.exe`.
- **Đường dẫn dữ liệu mới:** DB `%ProgramData%\EduFocus\edufocus.db` (chốt ở issue #9/#10), log `%LocalAppData%\EduFocus\logs` (chốt ở issue #12); không đọc hoặc migrate DB 1.x. Sau issue #7 app chưa mở DB; schema/database 2.0 được tạo ở issue #9/#10.
- **Cập nhật tích hợp:** auto-start registry, mutex single-instance, updater (repo `Cong-ty-TNNH-Q-Tech/EduFocus`), EF design-time DB, CI/release workflow và README/CONTRIBUTING.
- **Nâng nền tảng .NET 10 (issue #6):** toàn bộ project chuyển sang `net10.0`/`net10.0-windows`; EF Core 10.0.14; đồng bộ `Microsoft.Extensions.*` và `System.Management` 10.0.14; CI/release dùng .NET 10; pin `dotnet-ef` 10.0.14 qua tool manifest.
- **Cảnh báo build:** bỏ fallback `Assembly.Location` (IL3000), bổ sung annotation Windows cho Infrastructure (CA1416), dọn nullable trong test; giữ manifest DPI của WPF và suppress WFO0003 (analyzer chỉ dành cho WinForms). Build Release hiện 0 warning.
- **Theo dõi:** đã chạy đủ 4 migration trên DB dev bằng EF Core 10; trước khi migration DB thật cần backup theo `docs/database_schema.md` §17; kiểm tra DPI và auto-start trên bản publish single-file.

### Changed (Phase 1 — Legacy cleanup, issue #7)
- **Dọn code 1.x:** xóa toàn bộ entity/enum/value object, use-case port, repository, use case, EF Core DbContext + 4 migration, Gemini AI, `WindowsStrictEngine`, `LocalRedirectServer` và 5 view/view model của 1.x. Chi tiết ở [docs/legacy-cleanup.md](docs/legacy-cleanup.md).
- **Shell tối thiểu:** `App` không còn mở DB, migration/self-heal, seed settings/profile, MaterialDesign theme, `GlobalStrictMode`, auto-start registry hay tray icon; chỉ giữ single-instance, logging, update flow và shell hiển thị version.
- **Giữ hạ tầng 2.0:** attention domain/adapter + spike, `FileLogger`, `MonotonicClockAdapter`, `UpdaterService`/`UpdateViewModel`; `UseCases` và `SqliteDataAccess` tạm là project scaffold chờ issue #8/#9.
- **Rebrand build/installer:** MSI và GitHub Release dùng tên **EduFocus**; bỏ shortcut/description/icon 1.x và `app.ico`/`assets/logo.png`. Giữ `UpgradeCode` cũ để nâng cấp từ 1.x.
- **Không còn entry point 1.x:** hosts marker `DONTBELAZY`, chặn process và guilt-trip không còn được tạo bởi bất kỳ code nào trong build.

### Added (Phase 1 — Domain model, issue #8)
- **Domain model 2.0:** 9 entity (`AppSettings`, `ParentCredential`, `BlockRule`, `ScheduleWindow`, `StudySession`, `ActivitySample`, `AttentionEvent`, `BlockedAttempt`, `AuditLog`), enum và strongly-typed ID; factory/invariant cho protection state, PIN/lockout, rule matching, schedule và session lifecycle.
- **Rule engine domain:** `BlockRuleEvaluator` (allow thắng block) và `ProtectedDomains`; chặn được dịch vụ cụ thể như `gemini.google.com`/`copilot.microsoft.com` nhưng không chặn domain dùng chung/đăng nhập/lớp học.
- **Privacy helpers:** `DomainName`, `UrlSanitizer` (bỏ query/fragment/credentials), `TextSanitizer` (control character/truncate); domain không có field ảnh/video/landmark.
- **Tests:** 190 test trong `EduFocus.Domain.Tests` cho session lifecycle, block matching, schedule, protection state, credential/lockout và privacy; `EduFocus.Domain` không tham chiếu EF Core, WPF hay Windows API.
- **Docs:** [docs/domain-invariants.md](docs/domain-invariants.md), liên kết từ architecture §3.1.

### Added (Phase 1 — EF Core schema, issue #9)
- **Persistence 2.0:** `EduFocusDbContext` + 9 `IEntityTypeConfiguration` map đúng `docs/database_schema.md` — tên bảng/cột, `TEXT(n)`, check constraint, FK (cascade cho sample/event, `SET NULL` cho blocked attempt) và index, gồm unique partial index `UX_StudySessions_Active` chỉ cho phép một session `Active`.
- **Migration `InitialEduFocusSchema`:** tạo toàn bộ schema 9 bảng; `MonitoredSeconds`/`AttentionPercent`/`CoveragePercent` là stored generated columns do SQLite tính.
- **Converter:** UTC DateTime `TEXT(28)` (`yyyy-MM-ddTHH:mm:ss.fffZ`), DateOnly `TEXT(10)`, strongly-typed ID `TEXT(36)`, `DomainName`, theme/language lowercase; enum lưu tên.
- **Khởi tạo DB:** `SqliteDatabaseInitializer` backup trước migration (giữ 3 bản), giữ nguyên DB và log hướng dẫn khôi phục khi migration lỗi; DB mới set `auto_vacuum = INCREMENTAL` + `journal_mode = WAL`; `SqlitePragmaInterceptor` bật `foreign_keys` mỗi kết nối.
- **Config:** `EduFocusDatabaseOptions` mặc định `%ProgramData%\EduFocus\edufocus.db`, backup trong `backups`; design-time factory cho `dotnet ef`.
- **Tests:** project `EduFocus.SqliteDataAccess.Tests` với 19 test — schema/index/constraint, khởi tạo/backup/retention, roundtrip entity và ràng buộc một session `Active`.

### Added (Phase 1 — Data folder protection, issue #10)
- **ACL thư mục dữ liệu:** `DataDirectorySecurity` đặt `%ProgramData%\EduFocus` chỉ cho Administrators/SYSTEM full control, tắt kế thừa cũ; DB, WAL và `backups` kế thừa ACL. Standard user không đọc/ghi được; lỗi quyền ném `DataDirectorySecurityException` và không xóa dữ liệu.
- **Backup được kiểm chứng:** `SqliteBackupStore` tạo backup bằng SQLite backup API, verify `PRAGMA integrity_check` trước khi migration; tên file duy nhất nên không ghi đè; retention giữ tối đa 3 bản.
- **Phát hiện DB hỏng:** initializer chạy integrity check trước migration; DB hỏng giữ nguyên file và ném `SqliteDatabaseCorruptException` kèm hướng dẫn restore.
- **Restore an toàn:** `RestoreBackup` chỉ chấp nhận backup hợp lệ, snapshot DB hiện tại (best-effort), xóa `-wal`/`-shm` trước khi thay thế.
- **Tài liệu:** giới hạn shared Windows account và ACL được ghi trong [SECURITY.md](SECURITY.md), README vi/en và [docs/database_schema.md](docs/database_schema.md) §16–§17.
- **Tests:** thêm 6 test ACL/backup/restore/corrupt DB, tổng 25 test persistence.

### Changed (Phase 1 — CI, issue #11)
- **CI .NET 10:** workflow `EduFocus CI` trên Windows runner — restore, build/test Release, publish framework-dependent; cache NuGet theo csproj/tool manifest; timeout và concurrency để hủy run cũ.
- **Artifact:** upload `edufocus-test-results` (TRX, kể cả khi fail) và `edufocus-ci-publish`; không đưa DB/log/secret vào artifact.
- **Tài liệu:** README/README.en có badge CI và lệnh test toàn solution; CONTRIBUTING mô tả lệnh CI và nhắc maintainer bật required check **EduFocus — Build & Test** (branch protection hiện chưa bật).

### Added (Phase 1 — Structured logging, issue #12)
- **Log kỹ thuật có cấu trúc:** `FileLogger` ghi timestamp UTC, level, category và correlation id tại `%LocalAppData%\EduFocus\logs`; file `edufocus-YYYYMMDD.log`, tối đa 10 MB/file, giữ 14 ngày; lỗi ghi log không làm app crash.
- **Correlation id:** `ICorrelationContext` dùng `AsyncLocal`; app tạo scope khi khởi động, sẵn sàng cho use case/protection engine/session/block page.
- **Redaction:** `LogSanitizer` che PIN, password, recovery code, token/API key, `Authorization`/`Bearer` và URL đầy đủ (chỉ còn domain); message một dòng, tối đa 4.096 ký tự; exception message/stack cũng được redact.
- **Audit tách biệt:** port `IAuditLogRepository` + `SqliteAuditLogRepository` ghi `AuditLogs` kèm `CorrelationId`, không trộn với file log kỹ thuật.
- **Tests:** thêm 24 test (16 redaction domain, 7 log rotation/correlation, 1 audit persistence); tổng 239 test pass.
- **Tài liệu vận hành:** vị trí log, vòng đời file và cách thu thập log khi báo lỗi trong README vi/en, SECURITY và architecture §12.

### Added (Phase 1 — Parent authentication, issue #13)
- **PBKDF2 hasher:** `Pbkdf2PasswordHasher` dùng PBKDF2-SHA256 600.000 iterations, salt ngẫu nhiên 16 byte, hash 32 byte và `FixedTimeEquals`; từ chối hash/salt/iterations không hợp lệ.
- **Recovery code:** `RecoveryCodeGenerator` sinh mã 12 ký tự từ alphabet `A–Z`/`2–9` không nhập nhằng; `ParentRecoveryCode` chuẩn hóa chữ hoa và bỏ dấu gạch/space trước khi hash.
- **Use case:** `ParentAuthUseCase` xử lý setup, đăng nhập, đổi PIN và reset bằng recovery code; lockout dùng chung 5 lần/15 phút; reset xoay recovery code và trả mã mới một lần.
- **Audit:** ghi `PinSetup`, `ParentLogin`, `AuthenticationFailed` cho từng lần sai, `LockoutTriggered`, `PinChanged` và `RecoveryCodeUsed` kèm correlation id; không chứa PIN/recovery code.
- **Persistence:** `SqliteParentCredentialRepository` đọc/ghi singleton `ParentCredentials`; schema hiện có đủ cột nên không cần migration mới.
- **Tests:** project `EduFocus.UseCases.Tests` (19 test) + hasher/generator, recovery-code format và round-trip SQLite với PBKDF2 thật; tổng 289 test pass, build Release 0 warning.

### Added (Phase 1 — Parent Mode, issue #14)
- **Parent session:** `ParentSessionState` giữ phiên trong bộ nhớ, tự khóa sau 5 phút idle; `ParentModeService` mở phiên khi login thành công, khóa phiên sau khi đổi PIN và luôn khóa trước khi reset bằng recovery code. `IParentAuthorizer` là cổng cho các use case nhạy cảm ở các issue sau.
- **UI Parent Mode:** cửa sổ `ParentModeWindow` với đăng nhập PIN, hiển thị số phút lockout còn lại, đổi PIN, quên PIN bằng recovery code, hiển thị mã khôi phục mới đúng một lần; ô mật khẩu được xóa sau mỗi lần gửi và khi đóng cửa sổ.
- **Chống bypass phiên:** đóng cửa sổ hoặc idle quá 5 phút khóa phiên ngay; session không được khôi phục sau khi khởi động lại app; thao tác đổi PIN yêu cầu phiên hợp lệ.
- **Khởi tạo DB lazy:** `DatabaseBootstrapper` khởi tạo SQLite khi mở Parent Mode; lỗi dữ liệu hiển thị trong cửa sổ và có thể thử lại, không làm sập shell. Onboarding tạo PIN lần đầu thuộc issue #15.
- **Tests:** session hết hạn/khóa phiên, login/đổi PIN/reset kết thúc phiên, lockout đọc từ DB sau khi "restart" và hết hạn sau 15 phút; tổng 306 test pass, build Release 0 warning.

### Added (Phase 2 — Onboarding, issue #15)
- **Wizard lần chạy đầu:** `OnboardingWindow`/`OnboardingViewModel` gồm 5 bước — chào mừng → xác nhận quyền riêng tư và trách nhiệm phụ huynh → lưu tùy chọn an toàn → thiết lập PIN và nhận recovery code một lần → xác nhận đã lưu mã. Không thể bỏ qua bước bắt buộc; `OnboardingCompleted` chỉ được đặt khi privacy consent, PIN và xác nhận mã đều hoàn tất.
- **Consent lưu bền vững:** `AppSettings.PrivacyAcceptedAtUtc`/`GuardianAcknowledgedAtUtc` (migration `AddOnboardingConsent`, nullable, giữ mốc thời gian đầu tiên); `CompleteOnboarding` ném lỗi nghiệp vụ nếu chưa có consent nên UI không thể lách.
- **Use case:** `IOnboardingUseCase`/`OnboardingService` lưu từng bước ngay khi hoàn thành, nên tắt app giữa chừng vẫn giữ consent, tùy chọn và PIN; khi mở lại, wizard vào đúng bước còn thiếu. Credential đã có mà chưa hoàn tất thì xác minh PIN và phát hành recovery code mới (`RegenerateRecoveryCodeAsync`, dùng chung lockout, audit `RecoveryCodeRegenerated`) thay vì giả định mã cũ còn.
- **Mặc định theo BA:** wizard cho phép bật/tắt giám sát attention và “khởi động cùng Windows”, mặc định bật như tài liệu UC01 và giải thích hệ quả; chỉ lưu lựa chọn (đăng ký auto-start thực tế được triển khai ở issue #39, quyền camera thuộc issue #30).
- **Tiền kiểm tra camera:** `CameraPermissionProbe` đọc consent store webcam của Windows và hiển thị cảnh báo kèm nút “Kiểm tra lại” khi bị từ chối; không mở thiết bị, không lưu ảnh.
- **Trạng thái lỗi rõ ràng:** lỗi PIN yếu/không khớp, lockout (kèm số phút còn lại), từ chối camera và lỗi lưu cấu hình đều hiển thị bằng chữ “Lỗi:” (không chỉ dựa vào màu); ô PIN dùng `PasswordBox`, label trỏ đúng điều khiển và có `AutomationProperties.Name`.
- **Khởi động có cổng onboarding:** app chạy `ShutdownMode=OnExplicitShutdown`, hiển thị wizard modal trước `MainWindow`; đóng wizard giữa chừng thì app thoát và lần sau tiếp tục đúng bước.
- **Tests:** 9 test `OnboardingService` (mặc định an toàn, consent idempotent + audit, chặn PIN khi chưa consent, hủy giữa chừng, restart rồi hoàn tất, sai PIN, hoàn tất hai lần) và 3 test tái phát hành recovery code; 3 test repository mới; test upgrade migration giữ nguyên settings cũ; 2 test consent trong domain. Tổng **324 test pass**, build Release 0 warning.

### Added (Phase 2 — Protection toggle, issue #16)
- **State machine bảo vệ:** `ProtectionService` triển khai `IProtectionUseCase` — `EnableAsync`/`DisableAsync` yêu cầu phiên Parent Mode qua `IParentAuthorizer`; `GetStatusAsync` trả trạng thái đã lưu kèm phiên đang hoạt động. Bật áp engine trước khi ghi `ProtectionEnabled = true`; tắt gỡ engine trước khi ghi OFF, nên crash giữa chừng để lại tài nguyên có thể dọn thay vì trạng thái OFF mà chặn vẫn còn. Lỗi ghi DB sau khi áp engine sẽ rollback (gỡ engine, hủy phiên mới); lỗi gỡ engine giữ nguyên trạng thái ON.
- **Idempotency:** bật khi đã ON không tạo phiên thứ hai và không ghi audit lặp; tắt khi đã OFF không gọi engine. Bật thủ công xóa `ScheduleOverrideUntilUtc`; audit `ProtectionOn`/`ProtectionOff` actor Parent (không chứa PIN).
- **Engine port tạm thời:** `IProtectionEnginePort` (`ApplyAsync`/`RemoveAsync`/`ReconcileAsync`) + `DeferredProtectionEngine` trả cảnh báo rõ ràng rằng chặn web/app thực tế thuộc Phase 3 (issue #24). Trạng thái, phiên, audit và UI hoạt động đầy đủ nhưng chưa chặn domain/app.
- **Session repository:** `IStudySessionRepository` + `SqliteStudySessionRepository`; phiên `Active` duy nhất theo partial index, `GetActiveAsync` phục vụ state machine và khôi phục sau restart.
- **Khôi phục sau khởi động:** app reconcile trước khi mở cửa sổ chính — phiên `Active` mồ côi bị đóng tại heartbeat (`Interrupted`, audit `SessionInterrupted`); nếu `ProtectionEnabled = true`, engine được reconcile và tạo phiên `Trigger=Recovery` (audit `SessionRecovered`); nếu OFF, tài nguyên sót được dọn. Kết quả/cảnh báo được đưa vào UI thay vì im lặng.
- **UI và tray:** thẻ "Bảo vệ học tập" trong Parent Mode (trạng thái, cảnh báo engine, nút Bật/Tắt) và trên cửa sổ chính; `NotifyIcon` đổi icon/tooltip theo trạng thái, menu mở lại EduFocus. Tray không có mục thoát — luồng thoát cần PIN thuộc issue #18.
- **Tests:** 13 test `ProtectionService` (bắt buộc auth, bật/tắt đầy đủ, lặp lại idempotent, lỗi engine giữ trạng thái cũ, rollback khi lỗi lưu phiên, reconcile crash + cảnh báo) và 2 test `SqliteStudySessionRepository`; tổng **339 test pass**, build Release 0 warning.

### Added (Phase 2 — Protection schedule, issue #17)
- **Lịch bảo vệ (UC06):** `ScheduleClock` tính khung theo wall clock/local time, xử lý DST gap/ambiguous và chọn khung kết thúc sớm nhất; `IScheduleWindowRepository`/`SqliteScheduleWindowRepository` CRUD khung giờ; validate `DaysMask`, `StartMinutes`/`EndMinutes`, không qua nửa đêm và không chồng lấn.
- **Nguồn bảo vệ bền vững:** thêm `ProtectionSource` (`None`/`Manual`/`Schedule`) trên `AppSettings` + migration `AddProtectionSource` backfill `Manual` cho DB cũ đang bật. Nhờ đó scheduler chỉ tự tắt bảo vệ do lịch sở hữu, còn bảo vệ phụ huynh bật thủ công tồn tại ngoài khung giờ.
- **Use case:** `ScheduleService` yêu cầu Parent Mode cho CRUD, ghi audit `ScheduleChanged`, tự bật `ScheduleEnabled` khi thêm khung đầu tiên và serialize thao tác bằng semaphore. `EvaluateAsync` chạy mỗi 30 giây và lúc khởi động: tự bật trong khung, tự tắt khi hết khung, tôn trọng override và clamp override khi khung bị sửa; lệch wall clock > 2 phút so với monotonic ghi `TamperDetected` và bỏ qua auto-disable trong tick đó.
- **Override (BR-07):** tắt bảo vệ trong khung giờ tạo `ScheduleOverrideUntilUtc` đến hết khung; bật thủ công xóa override và chuyển nguồn sang `Manual`; tắt lịch cũng kết thúc bảo vệ do lịch sở hữu và xóa override.
- **UI:** thẻ "Lịch bảo vệ" trong Parent Mode — danh sách khung giờ, trạng thái khung đang hoạt động/override, bật/tắt lịch và editor 7 ngày + `HH:mm` (hỗ trợ `24:00`), nhãn, khung đang bật/tắt. `ScheduleSchedulerService` dùng `DispatcherTimer` 30 giây, đánh giá ngay trước khi mở cửa sổ chính; lỗi engine/đánh giá hiển thị cảnh báo thay vì làm sập app.
- **Tests:** thêm test domain `ScheduleClock`/`AppSettings`, 4 test repository `ScheduleWindows`, 17 test `ScheduleService` (override, clock jump, DST, biên 24:00, lỗi engine) và 5 test schedule trong `ProtectionService`; cập nhật test migration/schema. Tổng **379 test pass**, build Release 0 warning.

### Added (Phase 2 — Exit & Windows shutdown, issue #18)
- **Thoát bằng PIN (UC12):** `IAppLifecycleUseCase`/`AppLifecycleService` — `ExitWithPinAsync` xác thực PIN qua `ParentModeService` (dùng chung lockout), gọi `IProtectionUseCase.DisableForExitAsync` để gỡ chặn, kết thúc session `Completed`, ghi `LastCleanShutdownAtUtc`, audit `ProtectionOff` + `CleanExit` và luôn khóa Parent Mode sau mỗi lần thử. PIN bắt buộc kể cả khi bảo vệ OFF; gỡ chặn thất bại giữ app chạy với bảo vệ nguyên vẹn; lỗi ghi dấu thoát sạch chỉ cảnh báo vì reconcile lúc khởi động tự dọn.
- **Không tạo override khi thoát:** `DisableForExitAsync` tắt bảo vệ mà không tạo `ScheduleOverrideUntilUtc` (nếu không lần khởi động sau scheduler sẽ tưởng phụ huynh từ chối bảo vệ) và xóa override còn sót, kể cả khi bảo vệ đang OFF.
- **Windows shutdown/logoff:** `NotifySystemShutdownAsync` không gỡ chặn và không đổi `ProtectionEnabled`/`ProtectionSource`; chỉ đóng session đang chạy với `Interrupted` + `Note = SystemShutdown`, audit `SessionInterrupted`/`SystemShutdown`; mỗi bước best-effort, lỗi DB bị nuốt kèm log thay vì chặn OS.
- **UI:** đóng cửa sổ luôn ẩn xuống tray kèm balloon nhắc bảo vệ đang chạy; menu tray thêm "Thoát EduFocus..." mở `ExitWindow` — cảnh báo đỏ và checkbox xác nhận lần hai khi bảo vệ ON, hiển thị lockout/số lần thử còn lại, xóa ô PIN sau mỗi lần gửi, chặn đóng dialog khi đang xử lý. `AppExitState` chỉ cho phép thoát thật sau khi use case đồng ý; `App.OnSessionEnding` ghi nhận tối đa 2 giây và không bao giờ hủy shutdown.
- **Tests:** 11 test `AppLifecycleService` (sai PIN/lockout, thoát sạch, thoát khi OFF, xóa override, lỗi gỡ engine, lỗi ghi marker, shutdown có/không session, phục hồi sau shutdown, lỗi DB khi shutdown) và 2 test `ProtectionService` cho `DisableForExitAsync`; tổng **392 test pass**, build Release 0 warning.

### Added (Phase 3 — BlockRule matching, issue #19)
- **Snapshot matching:** `BlockRuleSet` là snapshot bất biến dựng từ danh sách rule đã lưu — bỏ rule `IsEnabled = false` và `NotEnforced` khi tạo, kiểm tra hết hạn theo `nowUtc` mỗi lần đánh giá, coi domain thiết yếu (`ProtectedDomains`) là `Allow` và so khớp `Exact` bằng `Ordinal` (không substring/suffix: `chatgpt.com.evil.test` không khớp `chatgpt.com`). Engine giữ snapshot và dựng lại sau khi rule thay đổi, nên vòng lặp matching không truy vấn DB.
- **Persistence:** `IBlockRuleRepository` + `SqliteBlockRuleRepository` (list/get/add/update/delete), đăng ký DI; unique key cho phép Allow và Block cùng domain tồn tại song song, rule URL/path được lưu với `EnforcementStatus = NotEnforced`.
- **Tests:** 7 test `BlockRuleSet` (exact-only, allow-wins, disabled/not-enforced/expired, protected domains, process name, snapshot cũ không tự bật rule mới) và 5 test repository (round-trip chuẩn hóa/hiệu lực, allow+block cùng domain, update/delete, URL `NotEnforced`, xóa id không tồn tại); tổng **404 test pass**, build Release 0 warning.

### Added (Phase 3 — Built-in rules, issue #20)
- **Catalog mặc định (UC05):** `BuiltInRuleCatalog` phiên bản 1 gồm 21 rule — 14 domain chatbot/AI assistant (ChatGPT, Claude, Gemini, Copilot, Perplexity, POE, Character.AI, DeepSeek, Grok, Meta AI, You.com, Pi, Hugging Face) và 7 allowlist học tập (`meet.google.com`, `teams.microsoft.com`, `classroom.google.com`, `accounts.google.com`, `login.microsoftonline.com`, `login.live.com`, `zoom.us`). Catalog từ chối version ≤ 0 và definition trùng `(RuleType, Value, Effect)`.
- **Seed idempotent:** `BlockRuleSeedService` chạy lúc khởi động, sau onboarding và trước khi reconcile bảo vệ. Rule được so khớp theo identity `(RuleType, MatchType, NormalizedValue, Effect)` nên chạy lại không tạo bản trùng; rule phụ huynh tạo thắng theo identity; rule built-in đã có giữ nguyên `IsEnabled` và hạn dùng, chỉ cập nhật `Category` khi catalog đổi. Khi có thay đổi, service ghi audit `BlockRuleChanged` (actor System) kèm metadata `seedVersion`/`added`/`updated`; lỗi seed được log và không chặn khởi động.
- **Quản lý blocklist trong Parent Mode:** `IBlockRuleUseCase`/`BlockRuleService` yêu cầu phiên Parent Mode để xem và bật/tắt rule; mỗi thay đổi được audit với actor Parent. Thẻ "Danh sách chặn" trong `ParentModeWindow` nhóm theo danh mục, hiển thị nguồn (Mặc định/Tùy chỉnh), hiệu lực, trạng thái, đánh dấu rule `NotEnforced` ("Chưa áp dụng trong MVP") và có nút Bật/Tắt kèm thông báo lỗi/hết phiên.
- **Không cần migration:** seed version là hằng số trong catalog; schema vẫn giữ 9 bảng.
- **Tests:** 5 test `BuiltInRuleCatalog` (tập rule mặc định, allowlist không bị chặn, không chứa domain bảo vệ, từ chối trùng/version sai), 5 test `BlockRuleSeedService` (fresh install + audit, idempotent, giữ rule bị tắt, rule phụ huynh thắng, version bump cập nhật metadata) và 6 test `BlockRuleService` (quyền Parent Mode, nhóm/thứ tự, toggle + audit, no-op, id không tồn tại); tổng **420 test pass**, build Release 0 warning.

### Added (Phase 3 — Hosts file sync, issue #21)
- **Adapter hosts file:** port `IHostsFileBlocker` + `HostsFileBlocker` ghi khối `### EDUFOCUS BLOCK START ###`/`### EDUFOCUS BLOCK END ###` trong `%SystemRoot%\System32\drivers\etc\hosts`; mỗi domain có đúng một bản ghi `127.0.0.1` và một bản ghi `::1`. Adapter chỉ nhận domain exact (chuẩn hóa lowercase/trailing dot; từ chối scheme, path, port, wildcard, IP, `localhost`) và **không tự thêm biến thể `www.`** — muốn chặn `www.` phải có rule riêng. Nội dung ngoài marker không bị sửa.
- **Ghi an toàn:** ghi qua temp file rồi replace, thử tối đa 3 lần; lỗi được phân loại `NotFound`/`AccessDenied`/`Locked`/`WriteFailed` bằng `HostsFileException`, nên tầng use case không coi lỗi ghi hosts là "đã tắt" và giữ nguyên trạng thái bảo vệ. Flush DNS best-effort qua `IpconfigDnsFlusher` (timeout 5 giây); lỗi flush chỉ ghi cảnh báo.
- **Backup, restore và self-heal:** trước lần ghi đầu, hosts được backup vào `%ProgramData%\EduFocus\hosts-backup` kèm `journal.json`. `RestoreAsync` khôi phục đúng byte gốc khi ngoài marker không bị sửa; nếu người dùng đã chỉnh sửa ngoài marker thì chỉ gỡ marker và giữ chỉnh sửa đó (`RestoredExact = false`). `SelfHealAsync` xử lý journal còn sót sau crash và marker mồ côi khi khởi động.
- **Không log nội dung:** logger chỉ nhận số domain đã ghi; không ghi danh sách domain, nội dung hosts hoặc đường dẫn đầy đủ vào log kỹ thuật.
- **Chưa nối vào engine:** DI đăng ký adapter nhưng `IProtectionEnginePort` vẫn là `DeferredProtectionEngine`; engine thật thuộc issue #24 để block page được bind trước khi hosts trỏ domain về loopback.
- **Tests:** 24 test `HostsFileBlockerTests` chạy trong sandbox (thư mục tạm, không cần elevation) — marker IPv4/IPv6, chuẩn hóa/sắp xếp, idempotent, backup, restore exact và khi có chỉnh sửa ngoài, self-heal marker mồ côi/journal sau crash, từ chối domain sai, hosts thiếu/khóa/chỉ đọc, lỗi flush không fatal và log không chứa domain; tổng **444 test pass**, build Release 0 warning.

### Added (Phase 3 — Block page HTTP loopback, issue #22)
- **Block page cục bộ:** port `IBlockPageServer` + `BlockPageServer` dùng `TcpListener` bind `127.0.0.1` (mặc định cổng 80; cổng 0 cho test) thay vì `HttpListener` nên không cần URL ACL. Chỉ phục vụ GET/HEAD; Host header phải khớp domain exact đang bị chặn, request còn lại nhận 404/405 chung và không echo input.
- **Trang chặn tĩnh:** HTML/CSS nhúng, domain được HTML-escape, hướng dẫn nhờ phụ huynh, không tải font/ảnh/script ngoài. Response đặt `Content-Type: text/html; charset=utf-8`, `Content-Security-Policy: default-src 'none'; style-src 'unsafe-inline'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'`, `Cache-Control: no-store`, `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY` và `Referrer-Policy: no-referrer`.
- **Cổng 80 bận:** `StartAsync` trả `BlockPageStartResult.Warning` (không ném lỗi) và vẫn giữ `IsRunning = false`; engine ở issue #24 sẽ giữ Protection ON và hiển thị cảnh báo trên UI.
- **Hit quan sát được:** mỗi request bị chặn phát `BlockPageHit` chỉ gồm domain chuẩn hóa + thời điểm UTC; không chứa path/query/User-Agent và không ghi log domain. Engine #24 debounce 30 giây rồi ghi `BlockedAttempt` với `ObservedVia = BlockPage`.
- **Giới hạn tài nguyên:** header tối đa 16 KB, timeout đọc header 5 giây, tối đa 16 kết nối đồng thời; stop idempotent và khởi động lại được.
- **Chưa nối vào engine:** DI đăng ký adapter; `IProtectionEnginePort` vẫn là `DeferredProtectionEngine` cho tới issue #24.
- **Tests:** 18 test `BlockPageServerTests` sandbox (cổng ephemeral, không cần elevation) — start/stop/restart, idempotent + cập nhật domain, cổng bận, trang chặn + security header, không tài nguyên ngoài, XSS qua Host/path/query, HEAD/POST, header quá lớn/thiếu Host, hit và log không chứa domain; tổng **462 test pass**, build Release 0 warning.

### Added (Phase 3 — Process watcher, issue #23)
- **Theo dõi process:** port `IProcessWatcher` + `ProcessWatcher` polling mỗi 2 giây qua seam `IProcessController`; `WindowsProcessController` dùng `Process.GetProcesses()` (chưa cần WMI eventing) và chỉ xét process có cửa sổ chính.
- **Quy tắc chặn:** tên process được chuẩn hóa (bỏ path/`.exe`, lowercase) và so khớp **exact**; allowlist luôn thắng blocklist. Watcher bỏ qua PID ≤ 4, chính EduFocus, process không có cửa sổ chính và danh sách process hệ thống quan trọng (`explorer`, `lsass`, `svchost`…).
- **Đóng mềm trước khi kill:** gửi `WM_CLOSE` qua `CloseMainWindow()` và chờ tối đa 2 giây; nếu chưa thoát thì `Kill(entireProcessTree: true)`. Kết quả `ClosedGracefully`/`Killed`/`Failed` kèm cảnh báo tiếng Việt về dữ liệu chưa lưu hoặc thiếu quyền; lỗi đóng/kill không làm dừng vòng theo dõi.
- **Debounce 30 giây:** theo tên process và dùng monotonic clock, nên app bị kill rồi tự khởi động lại không tạo vòng kill/log; `StopAsync` xóa debounce để lần bật sau xử lý ngay.
- **Không log chi tiết:** log chỉ ghi trạng thái watcher và loại lỗi; không ghi window title, path hay nội dung tài liệu.
- **Chưa nối vào engine:** DI đăng ký adapter; `IProtectionEnginePort` vẫn là `DeferredProtectionEngine` cho tới issue #24, nơi `ProcessBlockedObservation` được debounce/ghi `BlockedAttempt` với `ObservedVia = ProcessWatcher`.
- **Tests:** 18 test `ProcessWatcherTests` với `IProcessController`, monotonic clock và logger giả — đóng mềm, timeout → kill, close/kill lỗi, allowlist, protected/self/windowless, chuẩn hóa tên, debounce + advance clock, update rule, stop/restart, handler lỗi, scan lỗi một lần rồi phục hồi, tên/option sai; tổng **480 test pass**, build Release 0 warning.

### Added (Phase 3 — ProtectionEngine, issue #24)
- **Ghép ba adapter:** `ProtectionEngine` triển khai `IProtectionEnginePort` — bật theo thứ tự block page → hosts → process watcher để listener loopback sẵn sàng trước khi hosts trỏ domain về `127.0.0.1`; tắt thì gỡ rule watcher/page trước rồi mới dừng watcher, page và hosts, nên process không bị đóng sau khi bảo vệ đã tắt. Remove idempotent và chạy được cả khi Protection OFF; reconcile self-heal hosts trước khi áp hoặc gỡ.
- **Rollback và cảnh báo:** cổng 80 bận chỉ tạo cảnh báo, Protection vẫn ON và hosts/watcher vẫn chạy; lỗi adapter khác khi bật sẽ gỡ phần đã áp và ném `ProtectionEngineException`. Khi tắt, lỗi dừng watcher/page trả cảnh báo nhưng rule đã được gỡ; hosts restore thất bại ném lỗi để `ProtectionService` giữ Protection ON thay vì báo OFF giả.
- **Ghi blocked attempt:** một writer đọc `Channel` duy nhất ghi `BlockedAttempt` ngoài thread adapter, gắn `SessionId` đang hoạt động và tăng `BlockedAttemptCount`; domain ghi `ObservedVia = BlockPage`, app ghi `ProcessWatcher` (chỉ tên process, không window title/path). Debounce trung tâm 30 giây theo domain/tên app bằng monotonic clock; lỗi ghi DB chỉ được log và không làm dừng adapter.
- **DI:** thay `DeferredProtectionEngine` bằng `ProtectionEngine` (singleton); đăng ký `IBlockedAttemptRepository` + `SqliteBlockedAttemptRepository`.
- **Giới hạn:** thay đổi rule khi Protection ON chưa tự áp dụng lại engine — cần tắt/bật bảo vệ hoặc khởi động lại; live reload thuộc issue sau (UC05).
- **Tests:** 22 test `ProtectionEngineTests` với adapter/clock/logger giả — thứ tự apply/remove, lọc rule allow/expired/protected, warning cổng bận, idempotent, rollback hosts/page/watcher, reconcile + self-heal, ghi attempt domain/app kèm session count, debounce, bỏ qua observation khi OFF, lỗi ghi DB và dispose; 1 test `BlockedAttemptRepositoryTests` round-trip domain/app kèm `SessionId`; tổng **503 test pass**, build Release 0 warning.

### Added (Phase 4 — Session heartbeat, issue #25)
- **Heartbeat độc lập sampling:** `IStudySessionHeartbeatUseCase`/`StudySessionHeartbeatService` chạy `PeriodicTimer` 10 giây, tách khỏi activity sample; mỗi beat cộng `ActiveSeconds` theo **monotonic clock** và chặn tối đa 60 giây, nên sleep/tắt máy hoặc chỉnh đồng hồ không làm phồng thời gian; wall clock lùi được clamp về `LastHeartbeatAtUtc`.
- **Ghi có điều kiện:** `IStudySessionRepository.RecordHeartbeatAsync` và `IncrementBlockedAttemptCountAsync` dùng `ExecuteUpdateAsync` chỉ trên session `Active`, không lùi `LastHeartbeatAtUtc`/`UpdatedAtUtc`; heartbeat không hồi sinh session đã kết thúc và không đè lên `BlockedAttemptCount`. `ProtectionEngine` chuyển sang targeted increment thay vì full-entity update.
- **Vòng đời và concurrency:** `ProtectionService` serialize enable/disable/reconcile bằng semaphore; heartbeat khởi động trước khi lưu `ProtectionEnabled = true`, dừng trước khi kết thúc session và được khởi động lại nếu disable thất bại. Reconcile đóng session cũ tại `LastHeartbeatAtUtc` rồi tạo phiên `Recovery` từ thời điểm hiện tại, không cộng downtime.
- **Thoát và shutdown:** thoát sạch dừng heartbeat trước khi `Complete`; Windows shutdown dừng heartbeat trước khi `Interrupt`; `App.OnExit` dispose `ServiceProvider` bất đồng bộ để service ghi beat cuối và nhả timer.
- **DI:** đăng ký `StudySessionHeartbeatOptions` + singleton `IStudySessionHeartbeatUseCase`; thêm log category `Session`.
- **Tests:** 9 test heartbeat (cadence monotonic, clamp đồng hồ, biên 60 giây, lỗi DB retry, start/stop lặp), 10 test `ProtectionService` (heartbeat, concurrency, rollback, recovery), 2 test lifecycle shutdown và 6 test SQLite (heartbeat có điều kiện, chống hồi sinh, increment active, unique active, retention giữ summary); tổng **529 test pass**, build Release 0 warning.

### Added (Phase 4 — Activity sampling, issue #26)
- **Sampling tách khỏi heartbeat:** `IActivityUseCase`/`ActivityTrackingService` poll foreground mỗi giây nhưng chỉ ghi tối đa một `ActivitySample` mỗi 15 giây; foreground phải ổn định 2 giây (debounce) trước khi ghi. `DurationSeconds` lấy từ monotonic clock, chặn tối đa 60 giây/sample nên sleep/suspend không làm phồng thời gian; wall clock lùi được clamp để timestamp không đảo.
- **Idle:** idle ≥ 5 phút chỉ ghi một sample idle cho mỗi chuỗi nghỉ; các beat sau bị chặn cho đến khi người dùng hoạt động lại. `IdleSeconds` và category time được trừ khỏi nhau nên thời gian idle không bị tính là học/giải trí.
- **Phân loại:** `ActivityClassifier` (domain) trả `Study`/`Distraction`/`Neutral`/`Unknown` theo `BlockRuleSet`; allow thắng block. `OnlineClassDetector` nhận Meet/Teams/Zoom qua domain, process hoặc window title và đặt overlay `IsOnlineClass`/`OnlineClassSeconds`.
- **Riêng tư:** URL chỉ đọc best-effort qua `IBrowserUrlProviderPort` (UIA, chỉ browser đã biết, bỏ field password); URL được strip query/fragment/credentials, title strip control/truncate 512; không lưu clipboard/nội dung trang. Không lấy được URL thì `UrlSource = None`, sample vẫn ghi app/process.
- **Ghi DB bền:** counters `StudySeconds`/`DistractionSeconds`/`NeutralSeconds`/`IdleSeconds`/`OnlineClassSeconds` cập nhật bằng targeted `AddActivitySecondsAsync` chỉ trên session `Active` trước khi insert sample; session đã kết thúc thì dừng sampling, còn lỗi DB chỉ được log và thử lại ở beat sau.
- **Adapter Windows:** `WindowsForegroundActivityPort` dùng `GetForegroundWindow`/`GetWindowThreadProcessId`/`GetWindowText`/`GetLastInputInfo`, chịu được process biến mất hoặc access denied. `UiAutomationBrowserUrlProvider` (WPF) đọc address bar qua UI Automation.
- **Vòng đời:** `ProtectionService` start/stop sampling cùng heartbeat ở enable/disable/reconcile/rollback; activity start thất bại chỉ log, không chặn Protection ON. Windows shutdown và thoát sạch dừng sampling trước khi kết thúc session.
- **DI:** đăng ký `ActivityTrackingOptions`, `IActivitySampleRepository`/`SqliteActivitySampleRepository`, `IForegroundActivityPort`, `IBrowserUrlProviderPort` và singleton `IActivityUseCase`.
- **Tests:** 13 test `ActivityTrackingService` (foreground đổi nhanh, idle một sample, thiếu URL/quyền, process biến mất, DB retry, session kết thúc, suspend cap, wall clock lùi), 2 test SQLite sample round-trip + targeted counters, bổ sung assertion activity vào test ProtectionService/lifecycle; tổng **564 test pass**, build Release 0 warning.

### Added (Phase 4 — Online class detection, issue #27)
- **Nhận diện chặt hơn:** `OnlineClassDetector` vẫn nhận domain `meet.google.com`/`teams.microsoft.com`/`zoom.us` (kể cả subdomain) và process `ms-teams`/`teams`/`zoom` (chuẩn hóa path, `.exe`, dấu nháy, không phân biệt hoa thường). Meet qua window title chỉ khớp `Google Meet` hoặc `Meet` theo sau là dấu phân cách, nên `Meeting notes`, `Meet the team` hay `Microsoft Teams` không bị nhận nhầm; title đa ngôn ngữ như `Google Meet – Lớp Vật lý` vẫn khớp.
- **Không nhận nhầm chat:** Discord, Slack, Messenger, Zalo và process chứa `zoom` khác (ví dụ `zoominfo`) không bị coi là lớp online; title Teams đơn thuần không đủ, Teams chỉ nhận qua process/domain.
- **Overlay nhất quán:** sample idle không còn đánh dấu `IsOnlineClass`, nên cửa sổ Meet/Teams bị bỏ quên khi người dùng rời máy không tạo thời gian lớp online; `OnlineClassSeconds` vẫn là overlay trên `StudySeconds`, không cộng trùng.
- **MonitorOnlyDuringClasses:** thêm policy domain `MonitoringEligibility.IsEligible(monitoringEnabled, monitorOnlyDuringClasses, isOnlineClass)` để attention pipeline (#33) chỉ cộng `MonitoringEligibleSeconds` khi monitoring bật và, nếu chọn chỉ-trong-lớp, khi đang phát hiện lớp online; test chứng minh mẫu số coverage đổi từ 200 giây (mọi lúc) xuống 50 giây (chỉ trong lớp) trên cùng 40 giây monitored.
- **Tests:** 6 test domain detector theo nền tảng/âm bản, 3 test `MonitoringEligibility`, 4 test `ActivityTrackingService` (Teams, title Meet đa ngôn ngữ, chat, idle) và 1 test SQLite `IsOnlineClass` round-trip; tổng **594 test pass**, build Release 0 warning.

### Added (Phase 4 — Sleep, clock jump & power events, issue #28)
- **Port sự kiện nguồn:** `ISystemPowerEventPort` phát `Suspended`/`Resumed`; adapter WPF `WindowsSystemPowerEventPort` lắng nghe `SystemEvents.PowerModeChanged` (Suspend/Resume) và `SessionSwitch` (lock/disconnect → suspended, unlock/connect → resumed), hủy đăng ký khi dispose. Handler chạy trên thread sự kiện OS nên service chỉ ghi cờ/tick dưới lock.
- **Heartbeat:** `StudySessionHeartbeatService` cộng dồn phần hoạt động vào lúc nhận suspend rồi neo lại monotonic tick, nên resume chỉ tiếp tục từ mốc thức và thời gian sleep không vào `ActiveSeconds`. Suspend lặp lại (lock rồi disconnect) giữ mốc sớm nhất, còn nhiều chu kỳ suspend/resume trước một beat vẫn chỉ cộng các slice hoạt động. Beat định kỳ trong lúc lock/disconnect tiếp tục chờ resume mà không cộng thời gian; nếu timer bị pause (sleep) và resume event mất, gap vượt `MaxActiveDelta` bị bỏ rồi đếm lại. Dừng giữa lúc suspended chỉ ghi phần trước suspend. Forward wall clock không làm phồng thời lượng (monotonic), backward wall clock vẫn clamp `LastHeartbeatAtUtc`; chuyển DST không đổi `ActiveSeconds`.
- **Activity:** `ActivityTrackingService` nhận suspend/resume để reset cửa sổ sample — không tính gap vào `DurationSeconds`, không ghi sample cho khoảng sleep và khởi động lại cadence 15 giây sau resume. Gap vượt `MaxSampleGap` cũng bị bỏ tương tự; timestamp sample vẫn monotonic khi wall clock lùi.
- **Retry DB:** giữ `_pendingSeconds` khi ghi heartbeat thất bại, cộng vào beat kế tiếp nhưng vẫn clamp `MaxActiveDelta` để không vượt biên.
- **DI:** đăng ký singleton `ISystemPowerEventPort`/`WindowsSystemPowerEventPort`; activity và heartbeat nhận port này qua constructor.
- **Tests:** 7 test heartbeat (suspend/resume, dừng khi suspended, suspend lặp, nhiều chu kỳ trước beat, lock dài không resume, mất resume sau gap, DST), cập nhật test gap dài và clamp; 3 test activity (bỏ gap suspend, reset cửa sổ sau resume, forward clock), cập nhật test gap; tổng **603 test pass**, build Release 0 warning.

### Added (Phase 4 — Retention job, issue #29)
- **Retention theo AppSettings:** `IRetentionUseCase`/`RetentionService` đọc `ActivityRetentionDays`/`EventRetentionDays`/`SessionRetentionDays` và xóa `ActivitySamples` (mặc định 30 ngày), `AttentionEvents` + `BlockedAttempts` (90 ngày), `AuditLogs` (365 ngày cố định) và `StudySessions` (365 ngày) theo batch mặc định 500. Thiếu settings hoặc đọc settings lỗi thì bỏ qua toàn bộ pass, không xóa ngoài phạm vi.
- **An toàn dữ liệu đang dùng:** `SqliteRetentionRepository` dùng raw SQL subquery `LIMIT @batch` vì SQLite không hỗ trợ `DELETE ... LIMIT`; sample/event của session `Active` bị loại khỏi mọi batch, attention event còn mở không bị xóa, session `Active` hoặc chưa kết thúc không bị xóa. Chi tiết được xóa trước session để báo cáo dài hạn giữ đến hạn session; xóa session cascade sample/event còn sót, còn `BlockedAttempts` chuyển `SessionId` về NULL.
- **Lỗi cục bộ, không báo thành công giả:** mỗi bảng chạy độc lập; bảng lỗi giữ số đã xóa và vào danh sách `Failures`, các bảng khác vẫn tiếp tục; lỗi vacuum/audit cũng được ghi nhận. Audit `RetentionApplied` (actor `System`) ghi số bản ghi từng bảng và danh sách bảng lỗi, không chứa nội dung nhạy cảm.
- **Lịch chạy:** `RetentionSchedulerService` chạy một lần lúc khởi động và mỗi ngày khi local time ≥ 03:00; pass chạy trên thread pool nên batch không giữ UI thread. `PRAGMA incremental_vacuum` chạy sau mỗi pass trên DB `auto_vacuum = INCREMENTAL`.
- **Ghi chú:** issue ghi "RetentionCompleted"; domain đã có enum `AuditEventType.RetentionApplied` nên dùng lại tên này.
- **Tests:** 11 test `RetentionService` (cutoff theo settings, batch đến khi batch ngắn, lỗi giữa batch, thiếu/lỗi settings, vacuum/audit lỗi, audit count, chạy lặp tuần tự, validate options) và 8 test SQLite (sample session Active, event mở, blocked attempt/audit cutoff, batch session + cascade + SET NULL, session Active/recent, vacuum, validate cutoff/batch); tổng **622 test pass**, build Release 0 warning.

### Added (Phase 5 — Camera abstraction & attention port, issue #30)
- **Port camera không phụ thuộc OpenCV:** `ICameraPort` + DTO thuần (`CameraDeviceInfo`, `CameraFrame` BGR24 managed, `CameraOpenResult`, `CameraCaptureResult`, `CameraFailureKind`, `CameraSessionState`). Adapter copy pixel khỏi `Mat` sang buffer managed rồi giải phóng native buffer ngay trong `CaptureFrame`; không frame/ảnh/embedding nào được ghi disk hay gửi network.
- **Supervisor phiên camera:** `ICameraSessionUseCase`/`CameraSessionService` đọc `AppSettings.CameraDeviceId`, fallback camera mặc định khi thiết bị đã lưu không còn kết nối, mở/đóng tuần tự bằng semaphore. Open/list/capture chạy trên thread pool với timeout 5 giây (`CameraOptions`), nên driver treo không chặn caller; lỗi được phân biệt thành `PermissionDenied`/`Busy`/`NotFound`/`Disconnected`/`Timeout`/`Unknown`.
- **Retry có backoff:** lỗi mở → `Unavailable` + `LastFailure`, tự mở lại sau 30 giây, backoff nhân đôi tới trần 5 phút; capture trước hạn trả lỗi mà không mở lại. Lỗi capture lẻ không kết luận mất kết nối — chỉ `Disconnected` hoặc 3 lỗi liên tiếp mới đóng camera và chuyển `Unavailable`.
- **Adapter Windows:** `OpenCvCameraPort` (OpenCvSharp MSMF 640×480, flush frame đầu, chọn thiết bị theo PNP id) và `WindowsCameraDeviceCatalog` (WMI `Win32_PnPEntity`, best effort); thêm `System.Management` 10.0.14.
- **UI:** `CameraStatusViewModel` hiển thị trạng thái/thời điểm lỗi và hướng dẫn quyền trong cửa sổ chính, kèm mục "Camera" trong menu tray. Camera **chưa tự bật** — vòng đời do attention pipeline (#31–#33) điều khiển.
- **DI:** đăng ký `ICameraDeviceCatalog`/`WindowsCameraDeviceCatalog`, `CameraPortOptions`, `ICameraPort`/`OpenCvCameraPort`, `CameraOptions`, `ICameraSessionUseCase`/`CameraSessionService`, `CameraStatusViewModel`; WPF tham chiếu `EduFocus.Infrastructure.Attention`.
- **Tests:** 25 test `CameraSessionService` (chọn thiết bị lưu/mặc định, catalog WMI rỗng, settings lỗi, permission/busy, backoff 30→60→90 giây, disconnect, ngưỡng capture, timeout open/capture, stop/idempotent, event, refresh thiết bị, validate options); tổng **647 test pass**, build Release 0 warning.

### Added (Phase 5 — Attention pipeline & sampling, issue #31)
- **Tách capture khỏi inference:** `IAttentionFrameAnalyzer`/`OpenCvAttentionFrameAnalyzer` chỉ nhận `CameraFrame` BGR24 managed từ issue #30, không mở camera, không dispose frame của caller; analyzer copy pixel sang `Mat`, tính luminance và chạy YuNet/Haar. Kết quả `AttentionFrameResult` giữ `FaceObservation`, luminance, thời gian detect và lỗi có kiểu.
- **Sampling pipeline:** `IAttentionPipelineUseCase`/`AttentionPipelineService` chạy vòng 1 giây (validate 0–5 giây), `StartAsync` mở camera qua `ICameraSessionUseCase` và sample ngay; `StopAsync` hủy loop, chờ tick đang chạy rồi đóng camera. `SampleOnceAsync` cho test/chẩn đoán, dùng chung semaphore với loop nên không chồng capture.
- **Mẫu không mang ảnh:** `AttentionSample` chỉ chứa timestamp, `CameraFailureKind`, observation nullable, luminance, capture/detect ms và lỗi. Frame được dispose **trước** khi phát `SampleProduced`, nên subscriber không thể giữ pixel; không ghi disk/network. Camera lỗi → `Observation = null` + failure; inference lỗi → `CameraFailure = None` + `Error`, không bị hiểu nhầm là `Absent`.
- **Log chống spam:** lỗi camera chỉ log khi loại lỗi đổi, lỗi inference lặp được gộp; exception của subscriber được bắt và log, loop tiếp tục. Pipeline không dispose analyzer (DI/composition root sở hữu).
- **DI:** composition root đăng ký `AttentionAnalyzerOptions`, `AttentionPipelineOptions.Default`, `IAttentionFrameAnalyzer` qua `AttentionAnalyzerFactory` (YuNet, `models/` cạnh executable) và `IAttentionPipelineUseCase`; pipeline chưa được start ở startup, việc nối phiên học thuộc #33.
- **Spike harness:** `tools/AttentionPipelineSpike` chuyển sang `OpenCvCameraPort` + analyzer mới, giữ nguyên cách đo capture/detect; cập nhật README.
- **Tests:** 22 test `AttentionPipelineService` (frame dispose trước event, camera/inference failure, timestamp, loop nhiều tick, loop tiếp tục sau lỗi, start idempotent/stop trước start, camera unavailable, log một lần, subscriber lỗi, serialize sample, cancel khi mở camera, validate options); tổng **669 test pass**, build Release 0 warning.

### Added (Phase 5 — Attention state machine & events, issue #32)
- **State machine ổn định:** `AttentionStateWindow` làm mượt trong cửa sổ trượt 5–10 giây (mặc định 6 giây, tối thiểu 3 mẫu, majority ≥ 60%, confidence trung bình) và `AttentionStateMachine` giữ debounce: Focused ngay, Distracted > 20 giây, Absent > 30 giây, Drowsy > 2 giây, LowLight → CameraUnavailable sau 30 giây. `CameraUnavailable` bỏ qua cửa sổ và reset mẫu để ghi nhận tức thì; lỗi inference (`Observation = null`) không bị tính thành `Absent`.
- **AttentionEvent chỉ khi state đổi:** `AttentionMonitorService` nhận `AttentionSample` qua channel tuần tự, đóng event cũ tại mốc chuyển (clamp `EndedAtUtc ≥ StartedAtUtc`), mở event mới với state/confidence/note; `CameraUnavailable` có confidence `null`; gap > 5 giây bị bỏ khỏi thời gian cộng.
- **Cộng giây attention:** `IStudySessionRepository.AddAttentionSecondsAsync` cập nhật counter và `ConfidenceAvg` trọng số trong một câu raw `UPDATE` (vì `MonitoredSeconds` là stored generated column), `UpdatedAtUtc` không lùi, chỉ trên session `Active`; `CameraUnavailable` nằm ngoài mẫu số attention và confidence.
- **Drowsy có điều kiện:** `FaceObservation.EyesClosed` nullable; YuNet hiện không đo EAR nên production chưa phát `Drowsy`, classifier/state machine đã sẵn và được test bằng observation tổng hợp; `null` nghĩa là "không đoán".
- **Recovery sau crash:** `ProtectionService.ReconcileAsync` đóng mọi `AttentionEvent` còn mở tại `LastHeartbeatAtUtc`; lỗi đóng event chỉ cảnh báo, không chặn phục hồi phiên.
- **DI & stop:** composition root đăng ký `IAttentionEventRepository`/`SqliteAttentionEventRepository`, `AttentionThresholds`, `AttentionMonitoringOptions.Default`, `IAttentionMonitorUseCase`/`AttentionMonitorService`; monitor chưa start ở startup (thuộc #33).
- **Tests:** +40 test — sliding window, classifier/state machine/Drowsy, 16 hành vi monitor (event đầu tiên, không event trùng, noise, Drowsy, camera, inference lỗi, confidence trọng số, lỗi DB/event retry, session kết thúc, restart/stop, options), repository attention event và counter/weighted confidence; tổng **709 test pass**, build Release 0 warning.

### Added (Phase 5 — Coverage & session integration, issue #33)
- **Summary attention nhất quán:** `SessionAttentionSummary` tính `MonitoredSeconds` từ bốn counter, `AttentionPercent`/`CoveragePercent` (NULL khi mẫu số 0) và trả cảnh báo "Dữ liệu chưa đủ tin cậy" khi coverage < 50%; `CameraUnavailableSeconds` nằm ngoài mẫu số attention.
- **Coverage denominator:** `MonitoringEligibleSeconds` được cộng theo activity sample trong cùng `UPDATE` với category/idle/online-class (`AddActivitySecondsAsync`), dùng `MonitoringEligibility.IsEligible` với policy đọc lúc start; idle vẫn là thời gian kỳ vọng trừ khi `MonitorOnlyDuringClasses`, nên camera outage làm coverage giảm chứ không làm attention giảm.
- **Vòng đời attention theo phiên:** `ProtectionService` start monitor trước rồi pipeline khi bật bảo vệ/recovery, stop pipeline trước rồi monitor khi tắt/reconcile; session `MonitoringEnabled = false` không mở camera. `AppLifecycleService` stop pipeline/monitor trước activity/heartbeat khi Windows shutdown. Mọi lỗi camera/attention chỉ log, không chặn bảo vệ.
- **Tests:** +19 test — 6 domain summary (biên 0/50/100%, camera outage), 4 eligible (mọi lúc/chỉ lớp/tắt monitoring/idle), 7 vòng đời protection (start/stop/rollback/skip khi tắt monitoring), shutdown attention và tích hợp event → counter → summary; tổng **728 test pass**, build Release 0 warning.

### Added (Phase 5 — Camera conflict & calibration, issue #34)
- **Kiểm tra camera trước phiên:** `CameraCheckService` mở camera, đọc tối đa 5 khung hình trong bộ nhớ (cách nhau 250 ms), luôn đóng camera trong `finally`, không ghi ảnh/landmark và không cần quyền quản trị; từ chối chạy khi pipeline đang giám sát (`MonitoringActive`) để tránh tranh chấp thiết bị. `CameraHealthEvaluator` phân loại `Ready`/`TooDark`/`NoFace`/`NoImage`/`Unavailable` từ luminance trung bình, tỷ lệ khung đủ sáng và việc phát hiện khuôn mặt.
- **Hướng dẫn thống nhất:** `CameraGuidance` giữ một bộ câu tiếng Việt cho cả status bar và kết quả kiểm tra — cấp quyền Windows, đóng ứng dụng đang dùng camera, Multi-App Camera (Windows 11 22H2+ qua `WindowsCameraPlatformInfo`), thiếu webcam, ngắt kết nối và timeout.
- **Xử lý tranh chấp:** `OpenCvCameraPort` phân loại lỗi mở camera khi thiết bị vẫn hiện diện là `Busy`; `CameraSessionService` dừng retry với `NotFound` (mở lại tường minh khi phụ huynh bấm kiểm tra) và đặt cờ `StoredDeviceSubstituted` khi camera đã lưu biến mất để UI nhắc chọn lại. Camera bận/không quyền không tạo `Focused`/`Distracted` giả — chỉ ghi `CameraUnavailable` và làm giảm coverage.
- **Báo cáo & UI:** `SessionAttentionSummary.UnobservedSeconds` = eligible − monitored để báo cáo hiển thị thời gian không quan sát được; Parent Mode có thẻ Camera (trạng thái, hướng dẫn, kiểm tra, chọn camera lưu vào `AppSettings.CameraDeviceId`, dùng camera mặc định); onboarding chạy kiểm tra thật thay cho `CameraPermissionProbe` (đã xóa); cửa sổ chính hiện hướng dẫn khắc phục.
- **Tests:** +39 test — 20 domain health/guidance (đủ sáng/quá tối/không mặt/không ảnh/problem thắng reading, hướng dẫn theo từng lỗi), 8 mapper, 9 camera check (busy/permission/no-camera/quá tối/analyzer lỗi vẫn nhả camera/refuse khi đang giám sát/validate options), busy không tạo attention, fallback thiết bị và suppress retry `NotFound`; tổng **767 test pass**, build Release 0 warning.

### Added (Phase 6 — Dashboard hôm nay & Student Mode, issue #35)
- **Tổng hợp ngày từ StudySessions:** `DailyStudySummary` cộng counter của mọi session có `StartLocalDate` bằng ngày local hiện tại, tái dùng quy tắc `SessionAttentionSummary` (coverage < 50% cảnh báo, `CameraUnavailable` ngoài mẫu số, `UnobservedSeconds`) và trả `ActiveSessionStartedAtUtc`; `IStudySessionRepository.GetDailySummaryAsync` chạy một aggregate `GROUP BY` trên StudySessions, không quét ActivitySamples/AttentionEvents. `DashboardService.GetTodayAsync` lấy ngày theo `TimeProvider` local và trả empty summary khi chưa có phiên.
- **Student Mode:** cửa sổ chính có thẻ "Tổng quan hôm nay" chỉ hiển thị thời gian học và phiên đang hoạt động (kèm empty/loading/error), không lộ counter chặn, cấu hình hay PIN.
- **Parent dashboard:** thẻ tổng quan trong Parent Mode hiển thị thời gian học/hoạt động, % tập trung, coverage, thời lượng mất tập trung, camera không khả dụng, thời gian không quan sát được, blocked attempts best-effort, số phiên và cảnh báo "Dữ liệu chưa đủ tin cậy"; làm mới mỗi 15 giây khi cửa sổ mở, giữ số liệu cũ nếu query lỗi kèm nút thử lại.
- **Polling & lỗi DB:** `DashboardViewModel` chặn refresh chồng lấp bằng `SemaphoreSlim`, chỉ hiện loading lần đầu, log lỗi và không crash khi DB trống; MainWindow dừng polling khi ẩn xuống tray.
- **Tests:** +12 test — 5 domain summary (empty, nhiều session cộng dồn, coverage thấp, active session, camera outage), 4 DashboardService (local date UTC+7, empty, passthrough, lỗi), 3 repository aggregate (ngày trống/null, nhiều session + lọc ngày, coverage thấp); tổng **779 test pass**, build Release 0 warning.

### Added (Phase 6 — Báo cáo ngày/tuần & timeline, issue #36)
- **Report domain:** `ReportPeriod` giữ khoảng ngày local inclusive (ngày, tuần bắt đầu Thứ 2, tối đa 31 ngày chi tiết); `StudyReport` là projection bất biến gồm tổng counter, một `DailyStudySummary` cho mỗi ngày, session gần nhất, timeline theo giờ, top app/domain, attention event, confidence trung bình có trọng số và cờ `ActivityDetailsExpired`/`AttentionDetailsExpired`.
- **Truy vấn có giới hạn:** `SqliteStudySessionRepository.ListByLocalDateRangeAsync` lọc theo index `StartLocalDate`; `SqliteActivitySampleRepository.ListForReportAsync` chỉ trả projection `SessionId/SampledAtUtc/ActiveSeconds/Category/AppName/Domain` (không có `WindowTitle`, URL đầy đủ hay `ProcessName`), bỏ sample fully idle; `SqliteAttentionEventRepository.ListForReportAsync` trả event mới nhất theo session. Mỗi truy vấn nhận `maxCount` + đọc thêm một dòng để phát hiện truncation.
- **ReportService:** `IReportUseCase.GetAsync(ReportPeriod)` tổng hợp từ session summary; timeline chuyển `SampledAtUtc` sang giờ local tại thời điểm đọc bằng `TimeProvider.LocalTimeZone` nên giờ lặp khi fall-back được gộp và giờ khuyết khi spring-forward không xuất hiện; top domain ưu tiên domain đã làm sạch, sau đó app, cuối cùng "Không rõ"; cap 50 session, 100.000 sample, 100 event, 10 top; cảnh báo khi detail đã hết retention dựa trên session có counter nhưng không còn sample/event.
- **Parent Mode UI:** thẻ "Báo cáo học tập" với bộ chọn Hôm nay / Tuần này / 30 ngày gần nhất, làm mới theo yêu cầu, tổng hợp study/active/attention/coverage/confidence/monitored/distraction/lớp trực tuyến/blocked, danh sách ngày có phiên, phiên học (cap 20, kèm attention/coverage/confidence), timeline 24 khung giờ gần nhất, top domain/app và 10 attention event gần nhất; có loading/empty/error, cảnh báo coverage/retention và ghi chú privacy/best-effort. Báo cáo được nạp khi mở Parent Mode hoặc đăng nhập; Student Mode không đổi.
- **Tests:** +27 test — 6 `ReportPeriod` (ngày, tuần qua tháng/năm, biên 31/32 ngày), 4 `StudyReport`, 12 `ReportService` (cộng dồn, ngày local qua năm, DST fall-back/spring-forward, top domain/app, retention warning, cap session/event, lỗi repository, period thiếu), 5 repository report query (lọc khoảng ngày, idle/privacy/order, cap sample/event); tổng **806 test pass**, build Release 0 warning.

### Added (Phase 6 — Xuất báo cáo CSV, issue #37)
- **Một file CSV cho toàn bộ báo cáo:** `IReportExportUseCase`/`ReportExportService` nhận đúng `StudyReport` đang hiển thị nên tổng ngày/phiên khớp màn hình; `ReportCsvWriter` ghi một header 27 cột và các dòng `Tổng hợp`, `Ngày`, `Phiên học`, `Mẫu hoạt động`, `Sự kiện tập trung`. File UTF-8 có BOM để Excel đọc đúng tiếng Việt, CRLF, số theo invariant; tên `EduFocus-bao-cao-yyyyMMdd-HHmmss.csv`.
- **An toàn khi mở bằng Excel:** mọi ô bắt đầu bằng `=`, `+`, `-`, `@` (kể cả sau khoảng trắng) được thêm `'`; ô chứa dấu phẩy, dấu nháy hoặc xuống dòng được quote/escape. Không xuất `StudySession.Note`, `AttentionEvent.Note`, window title, URL đầy đủ, process name hay dữ liệu camera — chỉ app name và domain đã làm sạch. Sample/event được chuyển UTC → local và lọc đúng khoảng ngày đang chọn.
- **Lưu file & UI:** `LocalCsvExportStore` tạo `%LocalAppData%\EduFocus\exports`, không ghi đè (thêm `-2`, `-3`...), từ chối tên chứa path separator và dọn file dở khi ghi lỗi. Parent Mode có nút **Xuất CSV** (disable khi không có phiên/đang tải/đang xuất), hiển thị đường dẫn đã lưu và nhắc "File chứa dữ liệu học tập nhạy cảm; hãy bảo vệ file..." cùng cảnh báo retention/truncation.
- **Tests:** +16 test — 7 use case (tổng khớp snapshot, header/tên file tiếng Việt, comma/quote/newline, formula injection, lọc ngày local, không lộ note/URL/PIN, empty report không đọc detail, lỗi store propagate), 9 store (BOM + round-trip tiếng Việt, tạo thư mục, không ghi đè, chặn path traversal, tên rỗng); tổng **822 test pass**, build Release 0 warning.

### Added (Phase 6 — Settings quyền riêng tư & xóa dữ liệu, issue #38)
- **Settings do phụ huynh kiểm soát:** `PrivacySettingsService`/`IPrivacySettingsUseCase` trả snapshot gồm `AppSettings`, số row từng bảng (`PrivacyDataCounts`) và dung lượng file DB (`page_count * page_size`); save yêu cầu phiên phụ huynh đã xác thực, validate trong domain rồi ghi một audit `SettingsChanged` với metadata chỉ liệt kê nhóm thực sự đổi (`monitoring`, `thresholds`, `camera`, `retention`, `startWithWindows`, `updateCheck`, `appearance`). Save không đổi gì thì không update DB/audit; giá trị ngoài biên bị từ chối trước khi chạm DB.
- **Áp dụng ngay khi có thể:** monitoring + sample interval vào phiên đang chạy qua `IActivityUseCase.RefreshSettingsAsync`; ngưỡng attention đọc khi phiên kế tiếp bắt đầu; camera ở lần mở sau; retention ở lần dọn kế tiếp. Tắt monitoring chỉ dừng camera/attention, **không** tắt web/app blocking (`ProtectionEnabled`/`ProtectionSource` giữ nguyên). Theme/language được lưu nhưng runtime theming/localization chưa có nên UI ghi rõ.
- **Update check opt-in:** migration `AddUpdateCheckOptIn` thêm `AppSettings.UpdateCheckEnabled` mặc định `0`; bỏ lời gọi kiểm tra lúc khởi động. `UpdateViewModel` từ chối gọi mạng khi tắt và chỉ GET GitHub Releases API với `User-Agent: EduFocus/{version}` khi phụ huynh bật; UI nói rõ GitHub thấy IP, không gửi dữ liệu học tập/tên máy/nội dung màn hình. Không có telemetry hay upload nền.
- **Xóa toàn bộ dữ liệu:** `DataDeletionService`/`IDataDeletionUseCase` yêu cầu gõ đúng `XÓA` trước rồi mới kiểm tra PIN qua `ParentModeService` (dùng chung lockout 5 lần/15 phút). `SqlitePrivacyDataRepository` xóa theo batch 500 đúng thứ tự sample → event → blocked attempt → session không `Active`, không chọn dữ liệu của phiên đang chạy, chạy `VACUUM` best-effort và ghi audit `DataDeleted` kèm số bản ghi; bảng lỗi → `Incomplete` + danh sách bảng + UI báo lỗi, hủy giữa chừng/lockout không ghi audit thành công. Thao tác không thể hoàn tác, giữ settings/PIN/audit và dữ liệu phiên đang chạy; file CSV đã xuất nằm ngoài DB nên không bị xóa.
- **UI Parent Mode:** thẻ **Dữ liệu & quyền riêng tư** hiển thị dung lượng DB, số row, retention hiện tại, trạng thái camera; cho sửa monitoring/only-class, ngưỡng, sample interval, ba mốc retention, StartWithWindows, update check, theme/language; nút Lưu/Làm mới có busy state và thông báo lỗi. Khu vực xóa có giải thích phạm vi/không hoàn tác, ô PIN và ô xác nhận `XÓA`; ô PIN luôn được xóa sau mỗi lần gửi hoặc khi phiên khóa. Dữ liệu được nạp khi mở Parent Mode hoặc đăng nhập.
- **Tests:** +29 test — 10 `PrivacySettingsService` (yêu cầu auth, snapshot counts/size, save đủ trường + audit một lần, save không đổi không audit, tắt monitoring giữ bảo vệ, biên threshold/retention), 11 `DataDeletionService` (sai xác nhận trước PIN, sai PIN/lockout, batch giữ session `Active`, lỗi bảng → `Incomplete`, lỗi audit, vacuum lỗi, hủy giữa chừng), 5 `PrivacyDataRepository` (counts/size, batch xóa, bảo vệ session `Active`, batch ≤ 0), 1 domain `UpdateCheckEnabled` opt-in, 1 activity refresh setting và 1 attention đọc ngưỡng từ settings; tổng **851 test pass**, build Release 0 warning.

### Added (Phase 7 — Watchdog & auto-start, issue #39)
- **Watchdog thường trú:** app và watchdog dùng chung `EduFocus.exe` (`--watchdog`). App ghi heartbeat mỗi 5 giây (PID + process start time + timestamp); watchdog tick 5 giây, kiểm tra PID + start time để tránh PID reuse, coi heartbeat cũ hơn 30 giây là treo và chỉ kill sau grace 30 giây. Nhận diện resume từ sleep bằng monotonic clock để không kill sai; watchdog tự khởi tạo DB trước khi đọc settings nên sống sót qua upgrade schema.
- **Restart có kiểm soát:** chỉ restart khi `ProtectionEnabled = true` và không có clean exit; exit PIN/OFF là tín hiệu dừng tuyệt đối. Backoff 5 → 10 → 20 → 30 giây, budget bền vững 5 lần/10 phút, reset sau 10 phút ổn định; vượt budget ghi `WatchdogGaveUp` một lần và dừng thử hết cửa sổ. Audit `WatchdogRestart` chỉ chứa lý do (`Recovery`/`UnexpectedExit`/`Hang`), số lần thử và PID cũ/mới.
- **Auto-start & chống trùng instance:** `WindowsWatchdogAutoStart` đăng ký `EduFocusLogon` (ONLOGON) và `EduFocusWatchdog` (mỗi phút, HIGHEST) bằng `schtasks` khi app elevated; app giữ mutex single-instance, watchdog giữ mutex global (local ở Debug) nên hai task không tạo watchdog trùng. `WatchdogCoordinator` đồng bộ task theo onboarding/Protection/`StartWithWindows` (thay đổi trong Parent Mode được áp ngay); Protection OFF dừng watchdog, Protection OFF + `StartWithWindows` chỉ còn task logon để mở app một lần.
- **Vòng đời & installer:** `OnSessionEnding` ghi shutdown flag gắn với boot hiện tại + stop request để watchdog không restart trong lúc logoff, lần đăng nhập sau dùng `ReconcileAsync` sẵn có. CLI có `--watchdog-stop`/`--uninstall-watchdog`; MSI uninstall chạy `--uninstall-watchdog --wait-ms 5000` trước `RemoveFiles` (best effort) để dừng watchdog và xóa task.
- **Tests:** +74 test — 20 `WatchdogService` (onboarding/settings thiếu, login start, recovery/unexpected exit/hang, grace/resume, clean marker, app mutex, budget/backoff, give-up một lần, stable reset, lỗi audit), 7 `WatchdogCoordinator`, decorator protection, runtime files + JSON store, CLI parser, auto-start với fake `schtasks`, process controller và heartbeat publisher; tổng **925 test pass**, build Release 0 warning.

### Added (Phase 7 — WiX installer & nâng cấp an toàn, issue #40)
- **Payload & build MSI:** `installer/Build-Installer.ps1` harvest toàn bộ publish self-contained (bỏ `*.pdb`/`*.lib`), sinh `installer/obj/Payload.wxs` với component mỗi file và **GUID cố định** cho `EduFocus.exe`, rồi build MSI per-machine bằng WiX 4.0.6 + UI/Util extension. Script **chặn secret**: private key/chứng chỉ (`.pfx`, `.p12`, `.snk`, `.key`, `.pem`, `.cer`, `.crt`, `.jks`, `.keystore`, `.pvk`, `.env`), file `secrets.json`/`credentials.json`/`id_rsa`, marker `-----BEGIN ... PRIVATE KEY-----`, cùng `.db`/`.db-wal`/`.db-shm`/`.log`/`.bak`/`.tmp`; release workflow xóa PDB/LIB trước khi build và gọi script này.
- **Cài đặt & quyền:** MSI cài vào `%ProgramFiles%\EduFocus`, tạo shortcut Desktop/Start Menu dùng chung và registry `HKLM\Software\ThanhTrunggDEV\EduFocus` (InstallLocation/Version). Custom action deferred as SYSTEM `--prepare-data-directory` tạo `%ProgramData%\EduFocus` + ACL Administrators/SYSTEM trước khi app tạo DB; lỗi bước này làm cài đặt rollback. Launch condition chặn Windows < 10 hoặc 32-bit.
- **Nâng cấp an toàn:** `MajorUpgrade` cùng UpgradeCode (không tạo hai bản cài), `util:CloseApplication` đóng app/watchdog trước khi thay file; DB/settings nằm ngoài thư mục cài. Backup trước migration, giữ DB khi migration lỗi và `RestoreBackup` đã verify là đường rollback; thêm test `Failed_upgrade_rolls_back_and_the_backup_restores_the_previous_data`.
- **Gỡ cài đặt hỏi rõ dữ liệu:** dialog `RemoveDataDlg` (override `MaintenanceTypeDlg` RemoveButton) mặc định **giữ** database/settings/backup, checkbox `DELETEUSERDATA` cho phép xóa vĩnh viễn. CLI mở rộng `--uninstall-watchdog [--delete-user-data]`, `--prepare-data-directory`: gỡ scheduled task, dừng watchdog, xóa `runtime`, `SelfHealAsync` gỡ hosts marker + xóa `hosts-backup`, và chỉ xóa `%ProgramData%\EduFocus` sau kiểm tra đường dẫn + watchdog đã dừng. Silent uninstall giữ dữ liệu trừ khi đặt `DELETEUSERDATA=1`.
- **Tài liệu:** [docs/installer.md](docs/installer.md) — quyết định self-contained/per-machine, layout, ACL, quy trình cài/nâng cấp/gỡ, silent commands, checklist clean VM Windows 10/11, build/secret scan và độ lệch elevation (app vẫn cần admin trong MVP, chuyển sang Windows Service ở #46); cập nhật `SECURITY.md` và architecture §14.
- **Tests:** +5 test — 3 CLI parser (`--prepare-data-directory`, `--delete-user-data` chỉ có hiệu lực khi uninstall), 1 `EnsureSecure` tạo thư mục + ACL, 1 rollback migration/restore; tổng **930 test pass**, build Debug/Release 0 warning.

### Added (Phase 7 — Release CI, versioning & signing, issue #41)
- **Workflow theo tag + dry run:** `.github/workflows/release.yml` chạy khi push tag `v*.*.*`; `workflow_dispatch` nhận `version` và `dry_run` (mặc định `true`) để chạy đầy đủ build/test/ký/đóng gói trên branch thử nghiệm mà không tạo release/tag. SemVer được validate; tag là nguồn sự thật; tag có hậu tố prerelease tạo GitHub prerelease.
- **Versioning & kiểm tra:** publish nhận full SemVer (`Version`, `FileVersion` dạng số, tắt hậu tố source revision trong ProductVersion); workflow kiểm tra `ProductVersion`/`FileVersion` của `EduFocus.exe` và `ProductVersion` của MSI qua Windows Installer COM trước khi phát hành.
- **Ký số tùy chọn:** `installer/Sign-Artifacts.ps1` nhận PFX base64 + password từ GitHub Secrets, import vào `CurrentUser\My`, ký exe/MSI bằng `signtool /fd SHA256 /td SHA256 /tr` (mật khẩu không nằm trên command line), verify `/pa` và fail nếu chữ ký không hợp lệ; luôn xóa PFX tạm + certificate đã import trong `finally`. `REQUIRE_CODE_SIGNING=true` biến việc thiếu chứng chỉ thành lỗi; nếu chưa cấu hình, workflow cảnh báo và summary ghi `Code signed: false`.
- **Checksum & release notes:** `installer/Get-ReleaseNotes.ps1` lấy section CHANGELOG khớp version (kể cả heading Unreleased có tên version), bắt buộc `### Breaking changes`, ghép yêu cầu hệ thống/hướng dẫn xác minh; `SHA256SUMS.txt` (ASCII/LF) cho MSI, ZIP và install guide được tạo rồi đọc lại để verify. Release đính kèm MSI, portable ZIP, install guide và checksum.
- **Quyền tối thiểu & chống lộ secret:** job build dùng `contents: read` với checkout `persist-credentials: false`; chỉ job công bố có `contents: write`; secret chỉ truyền vào step ký; workflow quét workspace và fail nếu còn `.pfx/.p12/.snk/.pem/.key` hoặc dữ liệu runtime `.db/.log/.bak/.tmp`.
- **Tài liệu & kiểm thử:** thêm [docs/release-process.md](docs/release-process.md) (versioning, kích hoạt, ký số, checksum, dry run, checklist máy sạch); cập nhật `docs/installer.md`, CONTRIBUTING, README/README.en, architecture §14 và SECURITY. Kiểm thử cục bộ: parse YAML + 18 run block PowerShell, `Get-ReleaseNotes` thành công cho 2.0.0 và fail đúng khi thiếu section/`Breaking changes`, `Sign-Artifacts` no-cert/required/self-signed sign + cleanup, verify `ProductVersion` MSI; không đổi mã C#, **930 test pass**, build Release 0 warning.

### Added (Phase 7 — Hardening bảo mật & checklist E2E, issue #42)
- **Redaction log mở rộng:** `LogSanitizer` che PIN/recovery code cả khi không có dấu `=`/`:` (ví dụ `PIN 482915`, `recovery code is ABCD-EFGH-JKLM`) mà không đổi văn bản vô hại; `FileLogger` có test cho secret dạng khoảng trắng, exception lồng nhau và URL có query/fragment.
- **Dọn hosts backup:** sau restore hoặc self-heal thành công, `HostsFileBlocker` xóa cả `journal.json` lẫn file `.bak`; chỉ xóa khi đường dẫn nằm trong thư mục backup của EduFocus, ngược lại giữ nguyên và ghi warning.
- **Bằng chứng quyền riêng tư:** test quét raw byte DB, WAL và backup sau setup/đổi/reset PIN xác nhận không có PIN/recovery code plaintext; test ACL xác nhận file trong thư mục dữ liệu chỉ thừa hưởng Administrators/SYSTEM; test schema chặn cột `BLOB` hoặc tên cột ảnh/frame/landmark/embedding/pixel.
- **Block page:** bổ sung theory cho Host exact — chấp nhận khác hoa thường, dấu chấm cuối và port; từ chối subdomain, suffix giả, credentials và chuỗi escape.
- **Checklist E2E:** thêm [docs/security-e2e-checklist.md](docs/security-e2e-checklist.md) gồm ma trận bằng chứng tự động, 19 bước kiểm thử thủ công trên VM sạch Windows 10 22H2/Windows 11, findings register (không có P0/P1; P2 window title + thông báo/DPIA theo dõi ở #44, P3 hosts backup đã sửa), sign-off và lệnh thu thập bằng chứng; liên kết từ `SECURITY.md`, `docs/installer.md`, `docs/release-process.md` và architecture §14.
- **Tests:** +21 test — 6 `LogSanitizer`, 1 `FileLogger`, 1 hosts backup cleanup/path safety, 10 block page Host variants, 1 ACL file kế thừa, 1 DB/WAL/backup secret scan, 1 schema không có cột ảnh/BLOB; tổng **951 test pass**, build Release 0 warning.

### Added (Phase 7 — Đo hiệu năng, soak 8 giờ & release checklist, issue #43)
- **Ngân sách hiệu năng tự động:** `PerformanceBudgetTests` trên SQLite thật đo khởi tạo DB (mới/tái sử dụng), bộ dữ liệu 30 ngày (90 phiên, 21.600 sample, 360 event, 270 blocked attempt, 150 audit), báo cáo tháng qua `ReportService` thật và retention mặc định; assert DB 30 ngày < 16 MB, báo cáo < 2 giây, retention < 10 giây, DB một năm mô phỏng < 50 MB. Số đo tham chiếu: khởi tạo mới 993 ms / tái sử dụng 39 ms, DB 30 ngày 8,14 MB, báo cáo median 171 ms, retention 147 ms, DB một năm 9,57 MB.
- **Block page & listener:** `BlockPagePerformanceTests` đo 20 request loopback (median 0,27 ms, p95 0,43 ms, max 0,62 ms — ngân sách BA §9.3 là p95 < 100 ms) và 20 chu kỳ start/stop liên tiếp rồi phục vụ lại, xác nhận không rò socket/listener.
- **Soak script:** `scripts/Measure-Soak.ps1` (PowerShell 5.1) lấy mẫu CPU/RAM/handle/thread theo chu kỳ cho chế độ Idle/Protection/Attention, tính peak/p95, độ dốc bộ nhớ và handle mỗi giờ, phát hiện restart, ghi `soak-samples.csv` + `soak-summary.json` + `soak-summary.md` và trả exit code 1 khi vượt ngưỡng; hỗ trợ `-ProcessId`, `-AllowRestarts`, `-Max*` để tái sử dụng trên máy khác.
- **Báo cáo hiệu năng:** [docs/performance-report.md](docs/performance-report.md) ghi cấu hình máy đo (i5-9300H, 15,9 GB, Samsung PM981a NVMe, Windows 10 22H2), ngân sách BA §9.3/schema §15, số đo thực tế, quy trình soak 8 giờ và sáu kiểm tra thủ công PERF-M-01..06 còn chờ VM sạch.
- **Release checklist:** [docs/release-checklist.md](docs/release-checklist.md) — bản ghi bắt buộc cho từng bản phát hành: thông tin version/commit/workflow, cổng CI/artifact/ký số, bảo mật/quyền riêng tư, hiệu năng/soak, cài/nâng cấp/gỡ trên máy sạch, migration/backup, tài liệu, bảng ký duyệt và nơi lưu artifact (kèm TRX, số đo, bằng chứng đã khử dữ liệu nhạy cảm).
- **Tài liệu liên kết:** cập nhật [docs/release-process.md](docs/release-process.md) §6/§8 (cổng hiệu năng + soak + checklist ký duyệt), [docs/installer.md](docs/installer.md) §7 và architecture §13/§14.
- **Tests:** +5 test (`PerformanceBudgetTests` 3, `BlockPagePerformanceTests` 2); tổng **956 test pass**, build Release 0 warning.

### Added (Phase 7 — Hồ sơ minh bạch & tuân thủ dữ liệu, issue #44)
- **Thông báo quyền riêng tư:** thêm [docs/privacy-notice.md](docs/privacy-notice.md) (tiếng Việt; dữ liệu, mục đích, lưu trữ, chia sẻ, quyền của chủ thể dữ liệu, xóa, sự cố, liên hệ) và `PrivacyNoticeWindow` + `PrivacyNoticeRenderer` trong WPF đọc Markdown nhúng; mở từ bước Privacy/Preferences của onboarding và thẻ **Dữ liệu & quyền riêng tư** trong Parent Mode trước khi bật monitoring.
- **DPIA & bản đồ dữ liệu:** [docs/dpia.md](docs/dpia.md) theo **Luật Bảo vệ dữ liệu cá nhân 91/2025/QH15** và **Nghị định 356/2025/NĐ-CP**, gồm bản đồ dữ liệu, đánh giá rủi ro, người chịu trách nhiệm, retention, quy trình xóa và ứng cứu sự cố.
- **Consent trong installer:** `PrivacyConsentDlg` bắt buộc tick trước khi copy file, Back/Cancel hoạt động, Back từ InstallDir quay lại bước consent; MSI chuyển codepage sang UTF-8 (`65001`) để hiển thị tiếng Việt; payload kèm `docs\privacy-notice.md`, `THIRD-PARTY-NOTICES.md` và `LICENSE`.
- **Third-party licenses:** [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) liệt kê runtime/test/model kèm phiên bản, giấy phép và văn bản đầy đủ; xác nhận không có GPL/AGPL/SSPL/non-commercial. FluentAssertions pin **7.2.0** vì từ 8.x dùng Xceed Community License chỉ cho mục đích phi thương mại.
- **Tài liệu người dùng:** README/README.en thêm mục **Hồ sơ minh bạch**, quy trình xóa dữ liệu và liên kết third-party; cập nhật `SECURITY.md`, `docs/installer.md`, `docs/release-checklist.md`, `docs/ba_document.md` §10.2 và architecture §8.5/§14.
- **Kiểm thử:** renderer xác nhận render đủ 38 block/10.765 ký tự (heading, camera, liên hệ, bold); MSI build + `wix msi validate` thành công, kiểm tra qua COM xác nhận dialog consent, thứ tự ControlEvent và file `docs\privacy-notice.md` trong File table; **956 test pass**, build Release 0 warning.

### Added (Phase 3 — Browser extension chặn URL/path, issue #45)
- **Chặn URL/path chính xác:** `BlockRule` hỗ trợ `RuleType.Url` với `MatchType.Exact/Suffix/Contains`; URL được làm sạch (bỏ credentials/query/fragment, chỉ http/https, tối đa 2.048 ký tự) trước khi so khớp; allow rule vẫn thắng block và domain bảo vệ của lớp học/đăng nhập không bị chặn. Built-in catalog thêm rule YouTube Shorts và TikTok.
- **Browser extension MV3 (Chrome/Edge):** `browser-extension/` dùng `chrome.webNavigation.onBeforeNavigate` + `onHistoryStateUpdated`, chặn theo snapshot rule nhận qua native messaging, chuyển tab tới `blocked.html` an toàn (không đọc/hiển thị URL bị chặn); fail-open khi EduFocus không chạy; kết nối lại mỗi phút; popup hiển thị trạng thái bảo vệ.
- **Native messaging host:** `EduFocus.NativeHost` nối stdin/stdout của Chrome với named pipe `EduFocus.BrowserBridge.v1` (length-prefixed, giới hạn 16 KB vào/1 MB ra), chỉ chấp nhận origin `chrome-extension://lcanbfnejggilcaeinmahlcfimecmjmp/`; host đăng ký manifest + registry Chrome/Edge cho cả hai registry view qua `--register/--unregister --scope machine|user`.
- **IPC trong app:** `NamedPipeBrowserExtensionBridge` phát snapshot rule khi bảo vệ bật/cập nhật, nhận báo cáo URL đã khớp rule và ghi `BlockedAttempt`; pipe chỉ tồn tại trong phiên bảo vệ và URL được xác thực lại theo rule trước khi ghi.
- **Installer & phát hành:** MSI đóng gói `native-host\EduFocus.NativeHost.exe`, tự đăng ký native messaging khi cài (deferred custom action, best-effort) và gỡ đăng ký trước khi xóa file; `scripts/pack-browser-extension.ps1` tạo ZIP runtime (loại tests/native-host) và ký CRX tùy chọn; release workflow publish/ký native host, đóng gói extension và phát hành thêm ZIP + hướng dẫn.
- **Tài liệu:** [docs/browser-extension.md](docs/browser-extension.md) (kiến trúc, quyền, cài/gỡ, đóng gói, kiểm thử chấp nhận, xử lý sự cố) và cập nhật installer, privacy notice, DPIA, release process/checklist, README.
- **Tests:** +25 test Node (matcher/URL sanitization/manifest/DOM an toàn) và +10 test native host; toàn bộ **995 test .NET pass**, build Release 0 warning.

### Added (Phase 7 — Windows Service sở hữu protection, issue #46)
- **Windows Service `EduFocusProtection`:** `EduFocus.Service.exe` (LocalSystem, auto-start) sở hữu state machine bảo vệ, `ProtectionEngine` (hosts, block page :80, process watcher), heartbeat + vòng đời session, schedule evaluator và retention; UI đóng vẫn bảo vệ, SCM restart sau crash (3 lần, cách 60 giây, reset sau 1 ngày).
- **IPC có xác thực:** named pipe `EduFocus.ServiceControl.v1` một lệnh mỗi kết nối, frame JSON length-prefix, request tối đa 16 KB/response 64 KB, ACL chỉ SYSTEM + Administrators, xác thực ảnh tiến trình hai chiều, timeout phía client/server; lệnh cha (`enable`/`disable`/`disable-for-exit`) cần Parent Mode qua `ServiceRequestAuthorizer`, lệnh hệ thống (`reconcile`, `enable/disable-for-schedule`) không cần; PIN không đi qua pipe.
- **UI service mode:** `ServiceControlClient` + `ServiceMonitoredProtectionUseCase`/`ServiceMonitoringCoordinator` + poller trạng thái 10 giây; app đọc marker `HKLM\SOFTWARE\EduFocus\ServiceMode` để bỏ seed/reconcile/schedule/retention/watchdog cục bộ, dọn watchdog cũ và không fallback về engine cục bộ khi service không phản hồi.
- **Dừng an toàn:** service stop giữ nguyên trạng thái bảo vệ + hosts, dừng heartbeat, đánh dấu session `Interrupted` với ghi chú `ServiceStopped`, ghi audit `ProtectionServiceStopped`/`SessionInterrupted`; lần khởi động sau reconcile lại. Log service tại `%ProgramData%\EduFocus\logs` với cùng redaction/rotation.
- **Installer & phát hành:** MSI thêm `ServiceInstall`/`ServiceControl` (start khi cài, stop khi nâng cấp/gỡ, remove khi uninstall), `util:ServiceConfig` restart, marker `ServiceMode=1`, custom action ACL dữ liệu và đăng ký native host; portable ZIP chứa service exe nhưng chạy standalone vì không có marker/service row; release workflow publish, kiểm tra version và ký thêm `EduFocus.Service.exe`.
- **Tài liệu:** architecture §5.2/§10–§14/§16 (mô hình service, IPC, vòng đời, giới hạn), `SECURITY.md` (mục dịch vụ/IPC, installer, release, known limitations), `docs/installer.md`, `docs/release-process.md`, README/README.en.
- **Tests:** +82 test .NET (31 service control framing/ACL/xác thực ảnh/timeout/handler, 28 service host/runtime, 23 monitoring service-mode); toàn bộ **1077 test .NET pass**, build Release 0 warning.

### Added (Phase 7 — Mã hóa SQLite bằng SQLCipher, issue #47)
- **Mã hóa at-rest:** chuyển sang SQLCipher 4.5.2 (`SQLitePCLRaw.bundle_e_sqlcipher` 2.1.11, `Microsoft.EntityFrameworkCore.Sqlite.Core` 10.0.14, native `e_sqlcipher.dll`); DB, WAL và backup không đọc được nếu không có khóa; phát hiện plaintext/mã hóa qua 16 byte header.
- **Khóa DPAPI:** `DatabaseKeyStore` sinh khóa 32 byte bằng CSPRNG, lưu `%ProgramData%\EduFocus\edufocus.key` (magic `EDUFKEY1`, DPAPI `LocalMachine`, entropy `EduFocus.DatabaseKey.v1`); khóa không phải PIN, không ghi log, gắn thiết bị; thiếu/hỏng khóa thì fail-closed, không tạo khóa mới và không ghi đè DB.
- **PRAGMA key cho mọi kết nối:** `SqlitePragmaInterceptor` áp raw key `x'<64 hex>'` đúng một lần cho mỗi native handle (kể cả handle pool, qua `ConditionalWeakTable`) rồi `foreign_keys`; raw key tránh PBKDF2 ~525 ms mỗi kết nối.
- **Nâng cấp plaintext:** initializer phát hiện DB plaintext (bản trước #47), tạo backup, `sqlcipher_export` sang DB mã hóa, verify rồi thay file; lỗi giữ nguyên DB và backup.
- **Backup/restore mã hóa:** `SqliteBackupStore` dùng `ATTACH` + `sqlcipher_export` (backup API không hỗ trợ DB mã hóa), backup plaintext legacy được mã hóa lại; restore từ chối backup không giải mã được bằng key hiện tại và tạo snapshot trước khi ghi đè.
- **UI/service & xóa dữ liệu:** key store/options dùng chung cho UI và service; `--delete-user-data` xóa cả `edufocus.key`; gỡ cài đặt giữ dữ liệu thì giữ key.
- **Tài liệu:** schema §15–§18 (thiết kế, mất khóa, backup), SECURITY, DPIA/R15, privacy notice, installer, release/security checklist (SEC-E2E-27/28), architecture §3.5.1/ADR-18, BA §9.1, README/README.en, THIRD-PARTY (SQLCipher BSD-3-Clause).
- **Hiệu năng:** init 175 ms (DB mới) / 78 ms (mở lại), DB 30 ngày 8,25 MB (plaintext 8,14 MB), báo cáo tháng median 274 ms, retention 545 ms, DB một năm mô phỏng 9,71 MB — trong ngân sách BA §9.3/schema §15.
- **Tests:** +12 test (`EncryptionTests`: header/không đọc được, DPAPI/key file, fail-closed, nâng cấp plaintext, backup/restore/reject legacy, mất khóa, log); toàn bộ **1089 test .NET pass**, build Release 0 warning.

### Added (Desktop app — tắt Secure DNS Chrome/Edge khi bảo vệ ON)
- **Chặn DoH không cần extension:** `IBrowserDnsPolicyPort` + `BrowserDnsPolicy` đặt `DnsOverHttpsMode=off` dưới policy máy Chrome (`HKLM\SOFTWARE\Policies\Google\Chrome`) và Edge (`HKLM\SOFTWARE\Policies\Microsoft\Edge`) cho cả Registry64/Registry32; DoH là đường browser bỏ qua hosts nên policy trả hosts về làm lớp chặn hiệu lực, đúng yêu cầu chỉ dùng desktop app.
- **Backup/khôi phục chính xác:** giá trị policy cũ (hoặc việc chưa từng có value) được ghi vào `HKLM\SOFTWARE\EduFocus\BrowserDnsPolicy\<Chrome|Edge>-64|32` **trước khi** sửa bất kỳ policy nào — cần thiết vì một số bản Windows chia sẻ cùng key giữa hai registry view. Apply idempotent (không ghi đè backup bằng `off` của EduFocus); restore khôi phục đúng kiểu/giá trị hoặc xóa value, không động vào policy kiểu lạ do quản trị viên đặt.
- **Vòng đời bảo vệ:** engine áp policy trước hosts; rollback khi hosts lỗi; tắt bảo vệ và gỡ cài đặt khôi phục policy; lỗi policy chỉ là cảnh báo (protection vẫn ON) và được trả về UI; browser đang chạy cần khởi động lại mới đọc policy.
- **Tài liệu:** architecture §4.1/§6.3/§6.4, security E2E thêm SEC-E2E-29 + bằng chứng tự động, installer §5 (bước khôi phục khi gỡ), SECURITY/README/README.en (giới hạn DoH còn lại cho Firefox/VPN/proxy).
- **Tests:** +12 test (`BrowserDnsPolicyTests` 7, `ProtectionEngineTests` 5); toàn bộ **1101 test .NET pass**, build Release 0 warning.

### Changed (UI/UX — điều hướng sidebar bên trái)
- **Sidebar dùng chung:** thêm `SidebarNavList`/`SidebarNavItem` và `ContentOnlyTabControl` trong `Themes/DesignSystem.xaml` (mục đang chọn có thanh nhấn, icon + nhãn, hover/focus rõ ràng, hỗ trợ bàn phím); nội dung chính chuyển trang theo `SelectedIndex`, không còn tab strip ngang.
- **Student Mode:** `MainWindow` đổi sang sidebar trái (Tổng quan, Mẹo học tập, Cài đặt & riêng tư) kèm thẻ trạng thái camera và lối vào Parent Mode; giữ nguyên binding dashboard/protection và mọi thông báo.
- **Parent Mode:** sidebar trái thay cho tab ngang (Tổng quan, Báo cáo, Bảo vệ, Lịch, Chặn, Cài đặt); lịch bảo vệ và danh sách chặn tách thành hai trang riêng (bên trái là trạng thái + danh sách khung giờ, bên phải là form soạn khung giờ); binding, PIN, tự khóa phiên và xác nhận nguy hiểm không đổi.
- **Trang chặn:** giữ thiết kế thẻ tên miền/dark mode hiện có khi chuyển sang phục vụ cả HTTP và HTTPS.

### Added (Trang chặn HTTPS bằng CA cục bộ)
- **Root CA cục bộ:** `LocalCertificateAuthority` sinh root ECDSA P-256 một lần, lưu khóa riêng trong `%ProgramData%\EduFocus\certificates\block-root-ca.edufocus` (magic `EDUCA1` + DPAPI `LocalMachine`, ACL Administrators/SYSTEM, hạn 10 năm); chỉ **public root** được cài vào `LocalMachine\Root` khi bảo vệ ON và bị gỡ khi OFF/rollback/gỡ cài đặt.
- **Chứng chỉ theo domain:** leaf ECDSA P-256 `CA:FALSE`, EKU serverAuth, SAN đúng domain bị chặn, hạn 14 ngày, cache tối đa 512; chỉ cấp cho domain đang bị chặn và chọn theo SNI; khóa riêng giữ trong user key store của tiến trình (SChannel không hỗ trợ ephemeral key) và được xóa khi dispose.
- **Listener 443:** `BlockPageServer` phục vụ TLS 1.2/1.3, ALPN HTTP/1.1, trả cùng trang chặn với HTTP; vẫn kiểm tra Host header chính xác, giữ CSP/security headers, không log domain/path/query; listener 443 chỉ mở khi CA sẵn sàng, lỗi CA/port chỉ là cảnh báo và không tắt bảo vệ.
- **Vòng đời engine:** thứ tự apply DNS policy → CA trust → block page (80/443) → hosts → watcher → bridge; rollback và remove đảo ngược, gỡ trust CA sau khi listener đã dừng; gỡ cài đặt (kể cả giữ dữ liệu) gỡ root khỏi trust store và xóa thư mục `certificates`.
- **Tài liệu:** SECURITY (trust anchor, rủi ro còn lại, Firefox), architecture §4/§5/§6 + ADR-19, DPIA (data map/R16), privacy notice, installer §3–§7, security E2E thêm SEC-E2E-30…33; Firefox vẫn cảnh báo chứng chỉ (F-42-06, P3 chấp nhận).
- **Tests:** `CertificateAuthorityTests` 11, `BlockPageServerHttpsTests` 5, `ProtectionEngineTests` +6 cho vòng đời CA/rollback/warning.

### Fixed (Camera — luôn báo không khả dụng vì MSMF chậm)
- **Nguyên nhân:** `CameraSessionService` bọc `_camera.Open()` trong timeout 5 giây, nhưng backend MSMF mất 6,4-8,2 giây để mở và thương lượng 640×480 trên webcam UVC của máy đo (ADR-17 đã ghi nhận), cộng thêm ~0,2-0,5 giây liệt kê WMI; mọi lần mở đều vượt hạn nên bị báo `Timeout` dù camera vẫn mở được trong tác vụ nền.
- **Sửa:** `CameraPortOptions.CaptureApi` mặc định chuyển từ `msmf` sang `dshow`; đo lại toàn bộ chuỗi WMI + mở + đặt 640×480 + đọc khung bằng DirectShow là ~1,4 giây (MSMF ~7,3 giây), nằm gọn trong timeout 5 giây. MSMF vẫn chọn được qua options; cập nhật ADR-17 và system architecture.

### Changed (UI/UX — thiết kế lại giao diện theo độ tuổi)
- **Design system dùng chung:** thêm `Themes/DesignSystem.xaml` (bảng màu, typography, nút, input, checkbox, progress, tab, card) và chuyển toàn bộ cửa sổ sang style/token thay vì màu hardcode; không thêm thư viện UI bên ngoài.
- **Student Mode:** `MainWindow` mới với hero trạng thái bảo vệ, thẻ thống kê hôm nay, gợi ý học tập và cập nhật; bỏ các khối "Phase 1"/thông tin dành cho nhà phát triển.
- **Onboarding:** bố cục hai cột có thanh tiến trình 5 bước, ngôn ngữ thân thiện với học sinh nhưng vẫn giữ đủ nội dung consent/quyền riêng tư và toàn bộ kiểm soát trợ năng.
- **Parent Mode:** chuyển từ một trang cuộn dài thành 5 thẻ (Tổng quan, Báo cáo, Bảo vệ, Lịch & chặn, Cài đặt); giữ nguyên mọi binding, thao tác PIN/khôi phục và hành vi tự khóa phiên.
- **Exit/Privacy:** đồng bộ giao diện dialog với design system, giữ nguyên xác nhận PIN, cảnh báo tắt bảo vệ và nội dung thông báo quyền riêng tư.
- **Trang chặn website:** thiết kế lại loopback block page (thẻ tên miền, gợi ý quay lại việc học, dark mode) mà vẫn giữ nguyên CSP/security headers, câu thông báo được test và mã hóa HTML tên miền.

### Breaking changes
- **Không migrate dữ liệu 1.x:** 2.0.0 dùng schema mới tại `%ProgramData%\EduFocus\edufocus.db`; DB của Don't Be Lazy (1.x) không được đọc hoặc nâng cấp tự động. Người dùng chuyển từ 1.x bắt đầu lại onboarding và dữ liệu 2.0.
- **Mô hình bảo vệ và dữ liệu mới:** không còn phiên tập trung 1.x; quyền điều khiển thuộc Parent Mode + PIN, dữ liệu nằm trong `%ProgramData%` với ACL Administrators/SYSTEM.
- **Yêu cầu hệ thống:** Windows 10 22H2+/Windows 11 x64; bản MVP yêu cầu quyền Administrator khi cài và chạy (xem [docs/installer.md](docs/installer.md) §8).

### Removed (khỏi phạm vi EduFocus)
- Quản lý task, profile học tập, quote động lực và Gemini AI.
- Cơ chế "friction" bắt gõ câu xác nhận bỏ cuộc.
- Màn hình Dashboard/Profiles/FocusSession/Analytics của 1.x (sẽ thay bằng view mới).

---

> **Legacy — Don't Be Lazy (1.x):** Các phiên bản dưới đây thuộc dự án tiền nhiệm. Lịch sử được giữ nguyên để tham khảo.

---

## [1.0.7] - 2026-05-12

### Added / Feature
- **Ultimate Entertainment Blocklist**: Càn quét và khóa chặt mọi ngõ ngách giải trí phổ biến nhất Việt Nam vào danh sách cấm mặc định (Default Blacklist):
  - **Phim & Video**: Chặn các trang xem phim lậu (`phimmoi.net`, `motphim.net`, `tvhay.org`) và nền tảng bản quyền (`vieon.vn`, `fptplay.vn`, `galaxyplay.vn`, `vtvgiaitri.vn`).
  - **Truyện chữ & Manga**: Đưa hơn 30 tên miền đọc truyện nổi tiếng vào "sổ bìa đen" (`wattpad`, `truyenyy`, `wikidich`, `enovel`, `webtruyen`, `truyencv`, `tangthuvien`...). Đặc biệt quét sạch các biến thể tên miền chứa từ khóa "truyen" phổ biến.
  - **Mạng xã hội & Diễn đàn**: Khóa các cộng đồng đông dân nhất (`voz.vn`, `tinhte.vn`, `webtretho`, `otofun`) và các trang tin tức giải trí (`kenh14`, `znews`, `vnexpress`, `24h`).
  - **Game & Ứng dụng**: Bổ sung chặn các phần mềm giả lập (`ldplayer`, `nox`, `bluestacks`), các game hot (`FO4`, `Genshin`, `Honkai Star Rail`, `Wuthering Waves`) và app giải trí (`CapCut`, `iTunes`).

---

## [1.0.6] - 2026-05-12

### Added / Feature
- **Auto-Start & Auto-Resume**: Đảm bảo kỷ luật tuyệt đối! Trong quá trình đếm ngược (Focus Session), hệ thống sẽ tự động đăng ký khóa khởi động cùng Windows (Registry `Run`). Nếu máy tính bị tắt đột ngột (hoặc người dùng cố tình Shutdown) và bật lại, ứng dụng sẽ tự động khởi chạy và lập tức **khôi phục (Resume) phiên tập trung dang dở** mà không hiện hộp thoại hỏi ý kiến. Người dùng không thể lách luật bằng cách tắt máy tính. Khóa tự khởi động sẽ được dọn dẹp khi phiên hoàn thành hoặc bị huỷ bỏ đàng hoàng.
- **Popular Games Blocklist**: Cập nhật danh sách chặn ứng dụng mặc định, bổ sung thêm hàng loạt Launcher và Game phổ biến nhất hiện nay để ngăn chặn phân tâm (Steam, Riot Client, Epic Games, Battle.net, LoL, Valorant, CS2, Dota 2, Roblox, Minecraft, GTA V, Genshin Impact...).

---

## [1.0.5] - 2026-05-12

### Added / Feature
- **Smart Web Redirection (Issue 22)**: Khởi tạo `LocalRedirectServer` (Mini HTTP Server sử dụng raw `TcpListener` trên port 80). Khi người dùng cố gắng truy cập các trang web HTTP nằm trong danh sách đen, hệ thống sẽ điều hướng họ sang một màn hình HTML cảnh báo ("MÀY ĐANG LƯỜI BIẾNG!") thay vì hiển thị giao diện báo lỗi kết nối khô khan. (Lưu ý: Đối với mạng HTTPS, trình duyệt sẽ chủ động văng lỗi chứng chỉ để bảo mật, trang web vẫn bị chặn nhưng không hiển thị giao diện HTML này).
- **Minimize to Tray (Issue 18)**: Thiết kế lại hành vi đóng cửa sổ ứng dụng (Alt+F4 hoặc nút X). Nếu có một Focus Session đang chạy, ứng dụng sẽ thu nhỏ xuống thanh System Tray (Khay hệ thống) thay vì thoát hẳn, ngăn ngừa việc vô tình (hoặc cố ý) kết thúc ứng dụng để trốn tránh kỷ luật.

### Fixed & Changed
- **Global Strict Mode Bug Fix (Issue 25)**: Khắc phục lỗi chế độ Strict Mode Toàn Cục không tự động kích hoạt. Cập nhật vòng đời để khóa toàn bộ trang web/ứng dụng ngay khi App vừa khởi động và ngay khi người dùng vừa lưu Setting thay đổi `GlobalStrictMode` (chỉ áp dụng khi không có Session nào đang chạy). Đảm bảo chặn mọi lúc mọi nơi kể cả khi không đếm ngược.
- **Give-up Friction (Issue 26)**: Chuyển đổi toàn bộ cấu hình các câu nói "chấp nhận bỏ cuộc" từ Database sang đọc tĩnh từ file ẩn (`Assets/give_up_quotes.txt`). Xóa bỏ hoàn toàn giao diện tự quản lý thêm/sửa các câu nói này trong Settings để tăng tối đa "sự bất tiện" và tước đoạt quyền tự kiểm soát của người dùng, làm tăng rào cản tâm lý khi muốn abandon session.

---

## [1.0.4] - 2026-04-17

### Added / Feature
- **Light Mode UI Refactoring**: Thay thế toàn bộ mã màu HEX cứng trong giao diện (Settings, Dashboard, Focus, Profiles, MainWindow, App) bằng hệ thống `DynamicResource` của MaterialDesign. Đảm bảo ứng dụng hiển thị xuất sắc và đồng nhất trên cả 2 nền tối và sáng, tự động theo theme người dùng chọn.
- **Motivation Quote UI**: Triển khai hiển thị câu chữ truyền động lực (Motivation Quote) bên dưới đồng hồ đếm ngược. UI tự động gọi AI lấy câu quote (theo database hoặc fallback) dựa trên các cột mốc chiến lược: `PreFocus` (bắt đầu ngẫu nhiên), `MidFocus` (qua 50% thời gian), và `PostFocus` (gần hoàn thành, vượt 90%). Khi khôi phục phiên sẽ hiển thị quote giữ lửa cố định.

### Fixed & Changed
- **MSI Installer (Wix v4)**: Khắc phục và cấu hình bổ sung nhúng file `app.ico` vào bộ cài Windows Installer. Trừ việc hiển thị logo trong app, các lối tắt (shortcuts) do thư mục bộ cài tạo ra ngoài màn hình Desktop, trong Start Menu và trong mục Add/Remove Programs (Control Panel) giờ đây sẽ tự động hiển thị logo đầy đủ của ứng dụng.

---

## [1.0.3] - 2026-04-17

### Added / Feature
- **AI Priority Auto-Sort**: Cập nhật tính năng `AiSuggestPriority` để hệ thống tự động lưu `SortOrder` vào Database dựa vào JSON phân tích của AI thay vì chỉ in chuỗi tĩnh ra cho người dùng đọc. Thay đổi luồng hiển thị ưu tiên.
- **Session UI Binding**: Tự động bind giá trị `ExpectedMinutes` và `Profile` của công việc vào giao diện (ngoài Dialog và vào màn đếm ngược `25:00` gốc) ngay khi người dùng pick item, tiết kiệm thao tác thiết lập lại cho người dùng.

---

## [1.0.2] - 2026-04-16

### Added / Feature
- **File Logging System (Decorator Pattern)**: Triển khai hệ thống ghi log ra file theo ngày tại `%AppData%\DontBeLazy\logs\app-YYYY-MM-DD.log`. Sử dụng Decorator Design Pattern để bọc `IFocusSessionUseCase` và `IStrictEnginePort` — ghi lại mọi sự kiện quan trọng (bắt đầu phiên, hoàn thành, chặn web, lỗi hệ thống) mà không chạm vào logic lõi. Log tự động xoay vòng, giữ tối đa 7 ngày gần nhất.
- **Settings → Mở file log**: Thêm nút "Mở file log" trong mục Quản lý Dữ liệu ở màn hình Cài đặt.
- **AI Automation Pipeline**: Tự động trích xuất JSON từ kết quả của Gemini (cho AI Profile Generation và AI Task Breakdown), tự động khởi tạo dữ liệu trong DB thay vì chỉ trả về văn bản tĩnh.
- **Data Management (Export CSV)**: Chức năng xuất dữ liệu lịch sử tập trung (Session History) ra file CSV ở thẻ Analytics.
- **Data Management (Clear History)**: Bổ sung bộ lọc ngày để dọn dẹp và xoá lịch sử cũ ở màn hình Settings, giúp giải phóng lưu lượng lưu trữ.

### Fixed & Changed
- **State Loss / Session Bug (UI)**: Chuyển đổi toàn bộ vòng đời của tập ViewModels (FocusSession, Dashboard, Settings, etc.) sang `AddSingleton` thay vì `AddTransient` để tránh lỗi mất hiển thị thời gian tập trung (Session Data) khi chuyển đổi qua lại giữa các tab UI (Navigation).
- **Silent Fail / Strict Engine**: Cập nhật cơ chế chặn Web (`hosts` modifier). Ở bản Release build, nếu Antivirus (ví dụ Windows Defender Tamper Protection) chặn app ghi file vì app chưa được ký (unsigned), hệ thống sẽ chủ động hiển thị MessageBox cảnh báo ngay lập tức thay vì lỗi ngầm (Silent Fail).  
- **Crash Recovery**: Vá các lỗi crash tĩnh (silent crash) và quản lý ngoại lệ chưa xử lý qua `Dispatcher.UnhandledException` bằng Logger.
- **UI / SettingsView**: Sửa lỗi thẻ tag XAML bị sai lệch làm rối `DialogHost`, khắc phục lỗi không thể compile WPF.
- **ViewModel Syntax & Null Checks**: Xử lý bug namespace `IFocusSessionUseCase`, thuộc tính `PerTaskStrictMode` không tồn tại, và chặn `NullReferenceException` cho Quote Author tại `DtoMapper`. Khép lại toàn bộ vòng lặp review kiến trúc mã nguồn.

---

## [1.0.1] - 2026-04-16

### Fixed
- **Startup Crash**: Sửa lỗi crash vòng lặp khởi động (startup crash loop) trên các phiên bản cài đặt MSI và bản portable sau khi người dùng upgrade lên version `1.0.0`.

---

## [1.0.0] - 2026-04-16

### Added / Feature
- **AI Settings**: Bổ sung giao diện thiết lập trực tiếp Gemini API Key và chọn Model trong trang Cài đặt. Hỗ trợ cấu hình tự động cho các phiên bản Model mới nhất (Gemini 2.5 Flash, 2.5 Pro, 3.1 Pro Preview...).
- **Settings UI**: Thêm thông tin tác giả (ThanhTrunggDEV) vào giao diện Cài đặt.
- **Strict Mode Friction (UC04)**: Tích hợp hộp thoại cảnh báo bắt buộc gõ "Tôi chấp nhận giảm mức độ kỷ luật" trước khi cho phép tắt Global Strict Mode.
- **Ultimate Strict Mode**: Bổ sung `taskmgr` vào danh sách ứng dụng bị tắt bắt buộc (blacklist) trong quá trình đếm ngược, cùng với cơ chế chặn đóng bằng Alt+F4 / Window Close.
- **Profile Edit (UC02)**: Cho phép edit / đổi tên Profile, xử lý làm sạch URL tự động.
- **Fallback Polling Loop**: Kích hoạt bộ theo dõi dự phòng giám sát ứng dụng vòng lặp trong `WindowsStrictEngine`.

### Fixed & Changed
- **Database Architecture**: Chuyển đổi cơ chế khởi tạo Database từ `EnsureCreated()` sang `Database.Migrate()` lúc khởi động (App Startup) để hỗ trợ Auto Migration. Tự động apply pending migrations vào SQLite, đảm bảo nâng cấp schema mượt mà không loss data (Ví dụ: Thêm cột cho Settings).
- **WindowsStrictEngine**: Bổ sung khối lệnh `try-catch/finally` vào quá trình xóa cờ chặn (ClearRestrictions) để chắc chắn việc đồng bộ khóa / DB không ghim file `hosts`. Giải quyết crash hệ thống qua container `ActiveSessionState`.
- **Profiles UI**: Khắc phục lỗi `DialogHost` bị ẩn do Binding Converter, sửa lỗi chồng lấn các nút (overlapping icons) và lệch hộp thoại Danh sách Profile. Tự động chuyển mục chọn Profile ngay khi tạo Profile mới.
- **Analytics UI**: Sửa lỗi tràn chữ ở thẻ *Streak hiện tại* trên các chuỗi thông báo dài.
- **Session Validation**: Bắt buộc người dùng phải chọn cả `Task` và `Profile` trước khi hiển thị khung nhập cam kết ý định khởi tạo phiên tập trung.
- **Update Dialog**: Tích hợp Markdown (thông qua thư viện `MdXaml`) để render giao diện danh sách các chức năng mới (Release Notes) trở nên có format, chuyên nghiệp và có màu sắc hơn thay vì chuỗi thô.



---

## [0.3.0-beta] - 2026-04-16

### Fixed
- **Auto Update**: Sửa lỗi tính năng tự động cập nhật không nhận các bản phát hành có hậu tố (ví dụ: `v0.x-beta`). Đã chuyển sang dùng endpoint `/releases` (hỗ trợ hiển thị mọi tag kể cả pre-releases) và chuẩn hoá version chuỗi trước khi so sánh.
- **Icon Crash**: Sửa lỗi ứng dụng không khởi động được (throw `XamlParseException` / `Cannot locate resource 'app.ico'`). Ứng dụng hiện sử dụng đường dẫn vật lý cục bộ của `app.ico` để nạp programmatically vào runtime để chống lỗi nạp từ Pack URI.
- **dotnet run (Debug)**: Cho phép build và debug cục bộ `dotnet run` từ bất kì terminal nào mà không cần Admin privileges. Lệnh khởi chạy của build Release dưới file `DontBeLazy.exe` (hay `.msi`) sẽ vẫn yêu cầu quyền Admin thông qua UAC prompt (bảo vệ chức năng chỉnh sửa hệ thống).

---

## [0.2.0-beta] - 2026-04-16

### Added
- **Auto-elevate Administrator** (`app.manifest`): app tự động hiện UAC prompt khi khởi chạy — không cần người dùng phải right-click "Run as administrator"
  - `requestedExecutionLevel level="requireAdministrator"` — bắt buộc vì app dùng DNS flush và kill process trong Strict Mode
  - `PerMonitorV2` DPI awareness — giao diện sắc nét trên màn hình HiDPI
- **App Icon**: logo được nhúng vào exe dưới dạng `.ico` (256×256), hiển thị trong taskbar, Alt+Tab, title bar và File Explorer
- **Branch Protection** cho nhánh `master`: không cho phép push trực tiếp, mọi thay đổi phải qua Pull Request và phải pass CI
- **CI Workflow** (`.github/workflows/ci.yml`): tự động build + test cho mọi PR vào `master`
- **App Logo**: thiết kế logo cho app (power/motivation concept — fist breaking chains), thêm vào README

### Fixed
- File `.db`, `.db-shm`, `.db-wal` bị commit lên remote — đã xóa khỏi tracking và thêm rule vào `.gitignore`

### Changed
- README viết lại hoàn toàn: thêm badge CI/CD, hướng dẫn cài đặt (MSI vs portable), Tech Stack table, hướng dẫn GEMINI_API_KEY
- CHANGELOG sắp xếp lại theo version thực tế

---

## [0.1.0-beta] - 2026-04-16

Bản phát hành beta đầu tiên. Toàn bộ tính năng cốt lõi đã hoạt động trên Windows.

### Added

#### Core Architecture
- **Clean Architecture** gồm 5 layer: `Domain`, `Ports`, `UseCases`, `Infrastructure`, `WPF`
- **Domain Layer**: entities `FocusTask`, `Profile`, `ProfileEntry`, `SessionHistory`, `SystemSettings`, `DailyAnalytics`; enums `TaskStatus`, `CompletionStatus`, `ProfileEntryType`
- **Ports Layer**: interfaces phân tách theo ISP/SRP — `IFocusTaskUseCase`, `IFocusSessionUseCase`, `IProfileUseCase`, `ISystemSettingsUseCase`, `IQuoteUseCase`, `IAnalyticsUseCase`; outbound repositories & service ports
- **UseCases Layer**: business logic đầy đủ — strict mode, time-travel protection, session state singleton, Unit of Work pattern
- **Infrastructure Layer**: Gemini AI client, Win32 DNS flusher, whitelist process sweeper
- **SQLite Data Access**: EF Core 9 + migrations; repositories cho tất cả entities
- **DTOs Layer**: tách biệt hoàn toàn UI khỏi Domain entities

#### UI / WPF
- Material Design Dark theme (màu tím `#7C4DFF`), 5 views: Dashboard, FocusSession, Profiles, Analytics, Settings
- MVVM đầy đủ với CommunityToolkit.Mvvm; converters cho status/color/label
- Thay toàn bộ emoji bằng Material Design `PackIcon`

#### Task Management (UC01)
- Tạo task một lần hoặc lặp lại (Daily / Weekly / Custom interval)
- Dialog Thêm và Chỉnh sửa với đầy đủ trường bao gồm Recurring
- Nút toggle **Undo Done** (Done ↔ Pending), icon tự đổi theo trạng thái
- Nút **Retry Abandoned** — reset task về Pending bằng một click
- Guard: nút Edit/Delete bị disable khi task đang `Active`

#### Profile Management (UC02)
- Tạo, xoá, đổi tên Whitelist Profile
- Quản lý ProfileEntry (Website / App) cho từng profile

#### Focus Session (UC03)
- Start → Pause/Resume → Complete flow
- **Strict Mode**: không thể tắt trong phiên làm việc
- **Friction gate bỏ cuộc**: phải gõ đúng *"Tôi là kẻ lười biếng và tôi chấp nhận bỏ cuộc"* trước khi abandon

#### AI Features (UC04/UC05/UC06 — Gemini Flash)
- `AiSuggestPriority`: gợi ý thứ tự ưu tiên danh sách task
- `AiBreakdownTask`: phân tách task thành các bước nhỏ có thể thực hiện
- `AiGenerateProfileSuggestion`: gợi ý cấu hình whitelist profile từ mô tả ý định
- **Guilt-trip Quotes**: Gemini tạo câu quote cá nhân hoá (Tiếng Việt / English) hiện trước khi cho phép bỏ cuộc

#### CI/CD & Distribution
- **GitHub Actions** (`.github/workflows/release.yml`): auto build + release khi push tag `v*.*.*`
  - `dotnet publish` win-x64 self-contained single file
  - Build **MSI installer** (WiX v4) — install vào Program Files, Desktop & Start Menu shortcuts, MajorUpgrade
  - Build **portable zip** — chạy thẳng không cần cài
  - Release notes tự động trích từ CHANGELOG; tag có `-` → đánh dấu pre-release
- **Auto-updater** (`UpdaterService`): check GitHub API lúc khởi động, download MSI/ZIP, cài & relaunch
- **Settings — Mục CẬP NHẬT**: version hiện tại, nút kiểm tra, progress bar, release notes, nút cài đặt

### Fixed
- Nút "Huỷ" và "Tạo/Thêm" trong dialog không phản hồi → đồng bộ qua `DialogClosing` event handlers
- DB path relative gây crash Settings (`InvalidOperationException`) → dùng `AppContext.BaseDirectory`
- Icon `UpdateOutline`, `DownloadOutline` không tồn tại trong MaterialDesign 5.3.1 → thay bằng `CloudSync`, `Download`
- File `.db` bị commit lên remote → xoá khỏi tracking, thêm rule vào `.gitignore`

### Known Issues (Beta)
- Strict Mode chưa chặn được các process cố tình thoát qua Task Manager trên Windows 11 ARM
- macOS chưa được hỗ trợ

---

<!-- Template:

## [x.y.z] - YYYY-MM-DD

### Added
- ...

### Changed
- ...

### Fixed
- ...

### Removed
- ...

-->
