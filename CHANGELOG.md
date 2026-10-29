# 📝 Changelog

Tất cả các thay đổi đáng chú ý của dự án sẽ được ghi lại tại đây.

Định dạng dựa trên [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
và dự án tuân theo [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [Unreleased] — EduFocus 2.0.0 (2026-10-26)

> **Trạng thái:** Định hướng sản phẩm và tài liệu đã hoàn tất. Mã nguồn sẽ được chuyển đổi theo lộ trình Phase 1–7.
> Các mục dưới đây **chưa phải tính năng đã phát hành**.

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
- **Mặc định theo BA:** wizard cho phép bật/tắt giám sát attention và “khởi động cùng Windows”, mặc định bật như tài liệu UC01 và giải thích hệ quả; chỉ lưu lựa chọn (đăng ký auto-start thực tế thuộc issue #39, quyền camera thuộc issue #30).
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

### Removed (khỏi phạm vi EduFocus)
- Quản lý task, profile học tập, quote động lực và Gemini AI.
- Cơ chế "friction" bắt gõ câu xác nhận bỏ cuộc.
- Màn hình Dashboard/Profiles/FocusSession/Analytics của 1.x (sẽ thay bằng view mới).

### Planned
- Phase 0: Spike camera, blocking, tamper resistance.
- Phase 1: Schema mới, CI/installer, nâng .NET 10.
- Phase 2–7: PIN & Parent Mode → blocking engine → session/activity → attention → reports → hardening/release MVP.

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
