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
