# 🔒 Security Policy — EduFocus

## Phiên bản được hỗ trợ

EduFocus đang ở giai đoạn **Pre-release / đang chuyển đổi**. Bảng dưới đây sẽ được cập nhật khi có bản phát hành chính thức.

| Version | Supported |
|---|---|
| Pre-release / `master` | 🔄 Đang phát triển |
| 2.x | 🔜 Chưa phát hành |
| 1.x (Don't Be Lazy) | ❌ Không còn hỗ trợ |

---

## Mô hình bảo mật

EduFocus là ứng dụng desktop **local-first** dành cho phụ huynh giám sát hoạt động học tập trên máy tính của con. Ứng dụng can thiệp vào hệ thống ở mức trung bình:

- Ghi file `hosts` để chặn domain (yêu cầu quyền Administrator).
- Chạy local HTTP block page bind loopback `127.0.0.1:80`, chỉ phục vụ GET/HEAD và HTML tĩnh; port 80 có thể bị ứng dụng khác chiếm.
- Block page chỉ trả trang khi Host header khớp domain exact đang bị chặn; input không được echo, output escape, CSP chặn tài nguyên ngoài.
- Theo dõi foreground window/process.
- Có thể kill process nằm trong blocklist.
- Truy cập webcam để ước lượng trạng thái tập trung (nếu bật).
- Từ issue #46, bản cài đặt chạy protection trong **Windows Service** `EduFocusProtection`
  (`EduFocus.Service.exe`, LocalSystem, auto-start) do MSI cài. Service sở hữu engine (hosts, block
  page, process watcher), heartbeat, session, lịch và retention; UI chỉ mở camera/activity trong user
  session và điều khiển qua named pipe. Service dừng → SCM restart 3 lần cách nhau 60 giây (reset sau
  1 ngày); UI đóng không làm dừng protection.
- Bản chạy từ source/Debug không có marker service (`HKLM\SOFTWARE\EduFocus\ServiceMode`) dùng
  watchdog thường trú (`EduFocus.exe --watchdog`, heartbeat 5 giây, stale 30 giây) được hai scheduled
  task (đăng nhập + mỗi phút) giám sát; app thường được khởi động lại trong ≤ 30 giây, còn watchdog bị
  kill được khôi phục trong tối đa khoảng 60 giây.

### Dịch vụ bảo vệ và IPC

- Service chạy **LocalSystem** vì các thao tác cần thiết đều đòi đặc quyền: ghi file `hosts`, bind
  cổng 80 loopback, kill process của user khác và ghi DB có ACL Administrators/SYSTEM. Đây là tài
  khoản tối thiểu đáp ứng đủ các thao tác đó; service chạy headless trong Session 0, không mở
  webcam/foreground và không có UI.
- IPC là named pipe `\\.\pipe\EduFocus.ServiceControl.v1`, ACL chỉ **SYSTEM + Administrators** (không
  cho standard user), mỗi kết nối một request, payload JSON có length-prefix 4 byte LE, request tối đa
  16 KB và response tối đa 64 KB. Mọi trường được validate; lỗi trả mã lỗi chung, không trả đường dẫn
  file hay stack trace.
- Xác thực hai chiều bằng ảnh tiến trình: server chỉ nhận client có image đúng
  `<install>\EduFocus.exe`; client chỉ gửi lệnh khi PID server có image đúng
  `<install>\EduFocus.Service.exe`. Kẻ tạo pipe giả hoặc kết nối từ tiến trình khác bị từ chối và ghi
  log `Security`; không có fallback sang engine cục bộ.
- Lệnh phụ huynh (`enable`, `disable`, `disable-for-exit`) chỉ được service chấp nhận trong scope
  `ServiceRequestAuthorizer` mở bởi handler; UI kiểm tra `IParentAuthorizer` **trước khi gửi**.
  **PIN không bao giờ đi qua pipe.** Lệnh hệ thống (`reconcile`, `enable-for-schedule`,
  `disable-for-schedule`) do service tự chạy, không cần Parent Mode.
- Stop service là **fail-closed**: không gỡ hosts section, không đổi `ProtectionEnabled`, dừng
  heartbeat và đóng session đang chạy với `Note = ServiceStopped` tại beat cuối; lần start kế tiếp
  reconcile engine và tạo phiên `Recovery`. Service ghi audit `ProtectionServiceStarted` /
  `ProtectionServiceStopped` và `SessionInterrupted`, không chứa secret.
- Log service nằm ở `%PROGRAMDATA%\EduFocus\logs` (ACL Administrators/SYSTEM), cùng format/redaction/
  rotation với log UI; log UI vẫn ở `%LOCALAPPDATA%\EduFocus\logs`.
- MSI dùng `ServiceInstall`/`ServiceControl`/`util:ServiceConfig` để cài, start, stop trước khi gỡ và
  restart khi crash; marker `ServiceMode=1` bảo UI không mở engine thứ hai. Gỡ cài đặt dừng service
  trước khi chạy cleanup hosts/dữ liệu.

### Hồ sơ minh bạch

- [docs/privacy-notice.md](docs/privacy-notice.md) — thông báo quyền riêng tư đang áp dụng: dữ liệu thu thập, mục đích, lưu trữ, chia sẻ, quyền của chủ thể dữ liệu, quy trình xóa và xử lý sự cố.
- [docs/dpia.md](docs/dpia.md) — đánh giá tác động xử lý dữ liệu cá nhân (DPIA), bản đồ dữ liệu, người chịu trách nhiệm và quy trình ứng cứu sự cố.
- [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) — danh mục giấy phép bên thứ ba kèm văn bản giấy phép đầy đủ.

### Cam kết dữ liệu

- **Không gửi dữ liệu lên cloud/server.** Không telemetry mặc định. Update check mặc định tắt (`AppSettings.UpdateCheckEnabled = 0`), chỉ chạy khi phụ huynh bật trong Parent Mode và không tự kiểm tra lúc khởi động. Request chỉ gồm `User-Agent: EduFocus/{version}` tới GitHub Releases API — không có dữ liệu học tập, tên máy, danh sách process hay nội dung màn hình — nhưng GitHub vẫn thấy IP của máy, và UI nói rõ điều này.
- **Không lưu ảnh, video, âm thanh hoặc frame webcam.**
- **Không lưu nội dung trang, tin nhắn, clipboard hay lịch sử duyệt web đầy đủ.**
- **Không nhận diện danh tính/khuôn mặt.**
- Dữ liệu nằm trong `%ProgramData%\EduFocus`, được bảo vệ bằng ACL cho Administrators/SYSTEM. Ứng dụng áp ACL khi khởi tạo DB; file DB, WAL và backup kế thừa ACL này. Khởi tạo dữ liệu cần quyền Administrator.
- Backup trước migration được kiểm tra `PRAGMA integrity_check`; giữ tối đa 3 bản. Khi DB hỏng, ứng dụng giữ nguyên file và yêu cầu khôi phục từ backup, không tự xóa dữ liệu.
- Log kỹ thuật của UI nằm trong `%LocalAppData%\EduFocus\logs`; log của service nằm trong `%ProgramData%\EduFocus\logs` (ACL Administrators/SYSTEM). Cả hai tối đa 10 MB/file và giữ 14 ngày. PIN, recovery code, token và URL đầy đủ được redact trước khi ghi — kể cả khi secret không có dấu `=`/`:` (ví dụ `PIN 482915`, `recovery code is ...`); exception lồng nhau cũng được redact. Test quét raw byte DB, WAL và backup xác nhận không có PIN/recovery code dạng plaintext. Audit event quan trọng nằm trong `AuditLogs` của DB, không ghi vào file log.
- PIN/recovery code chỉ lưu dưới dạng hash PBKDF2-SHA256 600.000 iterations, salt 16 byte, hash 32 byte; so sánh constant-time; dùng chung lockout 5 lần/15 phút. Recovery code 12 ký tự chỉ hiển thị một lần và được xoay sau khi dùng để reset PIN. Mỗi lần sai PIN/recovery code được ghi audit `AuthenticationFailed`; lần thứ 5 ghi thêm `LockoutTriggered`.
- Parent Mode session chỉ nằm trong bộ nhớ, tự khóa sau 5 phút idle hoặc khi đóng cửa sổ Parent Mode, và không được khôi phục sau khi app khởi động lại; đổi PIN thành công buộc đăng nhập lại.
- Onboarding lần chạy đầu là cổng bắt buộc: app chỉ mở cửa sổ chính sau khi phụ huynh xác nhận quyền riêng tư/trách nhiệm, thiết lập PIN và xác nhận đã lưu recovery code. Timestamp consent (`PrivacyAcceptedAtUtc`, `GuardianAcknowledgedAtUtc`) lưu trong `AppSettings` và không bị ghi đè khi lặp lại. Không có đường bỏ qua bước nào từ UI; domain từ chối hoàn tất onboarding khi thiếu consent.
- Thông báo quyền riêng tư đầy đủ hiển thị được từ bước Privacy và Preferences của onboarding, và từ thẻ **Dữ liệu & quyền riêng tư** trong Parent Mode (trước khi bật monitoring), qua `PrivacyNoticeWindow` render Markdown nhúng (`EduFocus.Resources.privacy-notice.md`); bản cài kèm tại `docs\privacy-notice.md` trong thư mục cài. MSI hiển thị bước **Thông báo quyền riêng tư** bắt buộc tick đồng ý trước khi copy file; silent install bỏ qua UI nhưng onboarding vẫn ghi consent.
- Onboarding chỉ **lưu** tùy chọn `StartWithWindows` và `MonitoringEnabled`; đăng ký auto-start thật được thực hiện từ issue #39 qua `WatchdogCoordinator` và hai scheduled task. Lớp camera đã có ở issue #30 (`ICameraPort`/`CameraSessionService`) và pipeline sampling ở issue #31 (`AttentionPipelineService`); từ issue #33, `ProtectionService` chỉ start monitor + pipeline khi bật bảo vệ/recovery và session có `MonitoringEnabled = true`, rồi stop pipeline trước monitor khi tắt, reconcile hoặc Windows shutdown. Frame chỉ tồn tại trong buffer managed ngay sau `CaptureFrame`, được dispose **trước khi** `AttentionSample` được phát cho subscriber và không ghi disk/network. `AttentionSample` chỉ chứa timestamp, lỗi, luminance, thời gian capture/inference và các trường số của khuôn mặt; không chứa pixel, ảnh hay landmark thô. Tiền kiểm tra camera chỉ đọc consent store của Windows, không mở thiết bị. `AttentionEvent` chỉ lưu state, thời điểm, confidence, app/domain (khi có) và note đã sanitize, không có frame/landmark. Classifier `Drowsy` chỉ chạy khi model mắt cung cấp tín hiệu nhắm mắt; YuNet hiện không cung cấp nên production không suy đoán `Drowsy`.
- Recovery code lần đầu và mã tái phát hành khi tiếp tục onboarding đều chỉ hiển thị một lần; mã cũ bị vô hiệu hóa. Nút sao chép đưa mã vào clipboard của phiên Windows hiện tại — clipboard do người dùng kiểm soát và nằm ngoài phạm vi bảo vệ của EduFocus, nên hướng dẫn lưu mã ở nơi an toàn.
- Không có PIN, recovery code hay nội dung clipboard nào được ghi vào DB, audit hay file log; ô mật khẩu được xóa sau mỗi lần gửi và khi đóng wizard.
- Bật/tắt bảo vệ thủ công bắt buộc phiên Parent Mode còn hiệu lực (`IParentAuthorizer`); không có API UI nào tắt bảo vệ mà không qua PIN. `ProtectionEnabled` trong `AppSettings` là nguồn sự thật: engine được áp trước khi ghi ON và gỡ trước khi ghi OFF, kèm rollback khi ghi DB thất bại. Mỗi lần đổi trạng thái ghi audit `ProtectionOn`/`ProtectionOff`; khôi phục sau restart ghi `SessionInterrupted`/`SessionRecovered`. Audit chỉ chứa hành động và thời điểm, không chứa PIN/recovery code.
- Khi app khởi động, phiên `Active` còn sót được đóng tại heartbeat và một phiên `Recovery` được tạo nếu bảo vệ đang ON; trạng thái tray/UI đọc từ DB sau bước reconcile nên không hiển thị ON khi DB đang OFF.
- Lịch bảo vệ tự bật/tắt với actor `System`, không cần PIN. Scheduler chỉ tự tắt bảo vệ có `ProtectionSource = Schedule`; bảo vệ phụ huynh bật thủ công (`Manual`) tồn tại ngoài khung giờ. Tắt thủ công trong khung giờ tạo override đến hết khung; bật thủ công xóa override. Mọi thay đổi lịch ghi audit `ScheduleChanged`, không chứa secret.
- Khi wall clock lệch hơn 2 phút so với monotonic clock, app ghi audit `TamperDetected` và bỏ qua auto-disable trong tick đó; auto-enable vẫn được phép.
- Đóng cửa sổ không thoát app mà ẩn xuống tray. Thoát app chỉ thực hiện từ menu tray và **luôn** yêu cầu PIN, kể cả khi bảo vệ OFF; khi bảo vệ ON, dialog hiển thị cảnh báo và bắt buộc xác nhận lần hai. `AppLifecycleService` xác thực PIN qua `ParentModeService` (dùng chung lockout 5 lần/15 phút), luôn khóa phiên Parent Mode sau mỗi lần thử, gỡ chặn, kết thúc session `Completed`, ghi `LastCleanShutdownAtUtc` và audit `ProtectionOff` + `CleanExit` (không chứa PIN). Nếu gỡ chặn thất bại, app **không thoát** và bảo vệ giữ nguyên. Thoát bằng PIN không tạo `ScheduleOverrideUntilUtc` và xóa override còn sót.
- Windows shutdown/logoff không bị app chặn: `App.OnSessionEnding` chỉ ghi nhận best-effort trong tối đa 2 giây (session `Interrupted` + `Note = SystemShutdown`, audit `SessionInterrupted`/`SystemShutdown`) rồi để Windows tiếp tục. App không gỡ chặn, không đổi `ProtectionEnabled`/`ProtectionSource` và **không** đặt `LastCleanShutdownAtUtc`, nên lần đăng nhập sau reconcile tự bật lại bảo vệ với session `Recovery`. Lỗi DB trong lúc shutdown bị nuốt kèm log thay vì chặn OS.
- Watchdog (chỉ standalone, không có marker service) chạy cùng `EduFocus.exe` với tham số `--watchdog` và chỉ restart khi `ProtectionEnabled = true`, không có clean exit, và chưa vượt budget 5 lần/10 phút; backoff 5–30 giây, reset sau 10 phút ổn định, vượt budget ghi `WatchdogGaveUp` một lần. Audit `WatchdogRestart` chỉ chứa lý do (`Recovery`/`UnexpectedExit`/`Hang`), số lần thử và PID cũ/mới — không có window title, URL, tên tài liệu hay dữ liệu người dùng. Heartbeat/state/stop flag nằm trong `%ProgramData%\EduFocus\runtime` và kế thừa ACL Administrators/SYSTEM; app ghi shutdown flag + stop request trước khi dừng để watchdog không restart trong lúc logoff. MSI uninstall chạy `--uninstall-watchdog` trước khi xóa file để dọn scheduled task/runtime còn sót; service mode không tạo task mới và UI xóa task cũ khi khởi động.
- Service mode (issue #46) thay watchdog bằng Windows Service: engine/heartbeat/session/lịch/retention chạy trong `EduFocusProtection` (LocalSystem, auto-start), UI đóng không dừng protection, service crash được SCM restart (3 lần, delay 60 giây, reset 1 ngày). IPC và giới hạn quyền được mô tả ở mục **Dịch vụ bảo vệ và IPC**; service không nhận PIN, không trả secret/đường dẫn nội bộ trong lỗi.
- **Installer:** payload MSI được harvest từ thư mục publish self-contained **gồm `EduFocus.exe`, `EduFocus.Service.exe` và native host**; script build **từ chối** nếu thiếu service exe, private key/chứng chỉ (`.pfx`, `.p12`, `.snk`, `.key`, `.pem`...), file secret (`secrets.json`, `id_rsa`...) và marker `-----BEGIN ... PRIVATE KEY-----`, đồng thời chặn `.db`/`.log`/backup trong publish. Bước **Thông báo quyền riêng tư** bắt buộc tick đồng ý trước khi copy file; payload kèm `docs\privacy-notice.md`, `THIRD-PARTY-NOTICES.md` và `LICENSE`. MSI dùng codepage UTF-8 (`65001`) để hiển thị tiếng Việt đúng. MSI cài per-machine, cài service `EduFocusProtection` (LocalSystem, auto-start, restart khi crash), ghi marker `HKLM\SOFTWARE\EduFocus\ServiceMode=1`, tạo `%ProgramData%\EduFocus` + ACL Administrators/SYSTEM bằng custom action deferred as SYSTEM (`--prepare-data-directory`) trước khi service/app tạo DB; nếu bước này lỗi, cài đặt rollback. Nâng cấp dùng `MajorUpgrade` cùng UpgradeCode, dừng service và đóng app trước khi thay file; dữ liệu nằm ngoài thư mục cài nên không bị xóa. Gỡ cài đặt dừng service qua `ServiceControl`, chạy `--uninstall-watchdog` (dọn scheduled task/watchdog cũ, xóa runtime, `SelfHealAsync` gỡ hosts marker + xóa `hosts-backup`) và **chỉ xóa** `%ProgramData%\EduFocus` khi người dùng chọn trong hộp thoại hoặc silent uninstall đặt `DELETEUSERDATA=1`; mặc định giữ dữ liệu. Chi tiết: [docs/installer.md](docs/installer.md).
- **Phát hành:** GitHub Actions build từ tag, chạy test, publish self-contained `EduFocus.exe`, `EduFocus.Service.exe` và native host, kiểm tra version từng exe, đính kèm `SHA256SUMS.txt` và hướng dẫn cài. Khi repo có chứng chỉ code signing (`CODESIGN_PFX_BASE64`/`CODESIGN_PFX_PASSWORD`, bật `REQUIRE_CODE_SIGNING`), workflow ký **cả ba exe** và MSI bằng `signtool`, xác minh `/pa` và fail nếu chữ ký không hợp lệ. Job build chỉ có `contents: read`; chỉ job công bố mới có `contents: write`. Secret chỉ truyền vào step ký, không ghi log/artifact; workflow quét workspace để chặn chứng chỉ, secret và dữ liệu runtime. Chi tiết: [docs/release-process.md](docs/release-process.md).
- Adapter hosts file (`HostsFileBlocker`) chỉ sửa khối giữa marker `### EDUFOCUS BLOCK START/END ###`, ghi cả `127.0.0.1` và `::1` cho domain exact và không tự thêm `www.`. Trước lần ghi đầu, hosts được backup vào `%ProgramData%\EduFocus\hosts-backup` kèm `journal.json`; restore trả lại đúng byte gốc nếu ngoài marker không bị sửa, ngược lại chỉ gỡ marker để không đè chỉnh sửa của người dùng. Sau restore/self-heal thành công, journal và file `.bak` đều được xóa; adapter chỉ xóa backup nằm trong thư mục backup của EduFocus. Lỗi quyền/khóa file ném `HostsFileException` để bảo vệ không bị coi là đã tắt; log chỉ ghi số domain, không ghi nội dung hosts.
- Block page chỉ ghi nhận domain + thời điểm qua `BlockPageHit` (engine debounce rồi ghi `BlockedAttempt`); không ghi path/query/User-Agent hay nội dung trang. Log kỹ thuật chỉ ghi cổng/trạng thái listener, không ghi domain; cổng 80 bận trả warning và không làm tắt bảo vệ.
- Process watcher chỉ xử lý process có cửa sổ chính, khớp chính xác tên đã chuẩn hóa và không nằm trong allowlist; PID ≤ 4, chính EduFocus và process hệ thống quan trọng bị bỏ qua. Trước khi kill, watcher gửi `WM_CLOSE` và chờ tối đa 2 giây; quá hạn mới kill process tree. `ProcessBlockedObservation` chỉ gồm tên process, PID, kết quả và cảnh báo; không ghi window title, path hay nội dung tài liệu. Log không ghi danh sách process đang chạy.
- Retention mặc định: sample 30 ngày, event/blocked attempt 90 ngày, session summary/audit 365 ngày.
- Báo cáo CSV do phụ huynh chủ động xuất từ Parent Mode được ghi vào `%LocalAppData%\EduFocus\exports` (ngoài ACL của `%ProgramData%`), dùng UTF-8 có BOM và không ghi đè file cũ. File chỉ chứa tổng hợp session, app name + domain đã làm sạch, attention state/confidence và thời lượng; không có PIN/recovery code/token, window title, URL đầy đủ, note hay dữ liệu camera. Ô bắt đầu bằng `=`, `+`, `-`, `@` được escape trước khi mở bằng Excel (chống formula injection), và UI nhắc phụ huynh bảo vệ file vì đây là dữ liệu học tập nhạy cảm; ứng dụng không tự gửi file đi đâu.
- Cấu hình quyền riêng tư (monitoring, `MonitorOnlyDuringClasses`, ngưỡng distracted/absent, sample interval, ba mốc retention, camera, StartWithWindows, update check, theme/language) chỉ sửa được trong Parent Mode đã xác thực qua `IParentAuthorizer`. Mỗi lần lưu ghi audit `SettingsChanged` chỉ chứa tên nhóm thay đổi (không chứa giá trị cấu hình hay secret); lưu không đổi gì thì không ghi audit. Giá trị ngoài biên bị domain từ chối. Tắt monitoring chỉ dừng camera/attention — web/app blocking vẫn theo `ProtectionEnabled` và không thể tắt nếu không qua PIN.
- Xóa toàn bộ dữ liệu yêu cầu PIN đúng **và** gõ đúng chữ `XÓA`; sai xác nhận bị từ chối trước khi kiểm tra PIN nên không tiêu tốn lượt thử. Thao tác không thể hoàn tác: xóa sample → event → blocked attempt → session không `Active` theo batch 500, giữ nguyên settings, PIN, audit và dữ liệu phiên đang chạy, chạy `VACUUM` best-effort rồi ghi audit `DataDeleted`. Nếu một bảng lỗi, các bảng khác vẫn tiếp tục, audit ghi `Incomplete` kèm tên bảng và UI báo lỗi thay vì báo thành công giả; hủy giữa chừng hoặc bị lockout không ghi audit thành công. File CSV đã xuất nằm ngoài DB nên không bị xóa.

### Giới hạn đã biết (không phải lỗ hổng)

Các điểm sau **không được coi là lỗ hổng bảo mật** vì đã được ghi rõ trong thiết kế:

- Người dùng có quyền **Administrator/SYSTEM** có thể tắt, gỡ hoặc bypass EduFocus.
- File `hosts` có thể bị bypass bằng DoH/DoT, VPN, proxy hoặc sửa registry.
- File `hosts` có thể bị antivirus hoặc ứng dụng khác khóa: adapter thử lại 3 lần rồi báo `Locked`/`AccessDenied` và giữ nguyên trạng thái bảo vệ; phụ huynh cần đóng ứng dụng đang giữ file rồi bật lại bảo vệ.
- Hosts không tạo log truy cập HTTPS; `BlockedAttempt` là best-effort, không phải tổng tuyệt đối.
- Trang chặn chỉ hiển thị với HTTP; HTTPS thường báo lỗi kết nối/SSL.
- Block page chỉ bind IPv4 `127.0.0.1`; hosts có thêm bản ghi `::1`, nên kết nối IPv6 thuần có thể báo lỗi kết nối thay vì thấy trang chặn, nhưng domain vẫn bị chặn.
- Windows shutdown/logoff vẫn là đường bypass được chấp nhận: app không chặn OS và không gỡ chặn trong lúc shutdown, nhưng bảo vệ được phục hồi ở lần đăng nhập kế tiếp (standalone: watchdog khởi động lại app trong ≤ 30 giây sau kill; service mode: service auto-start lại cùng Windows và reconcile engine).
- Đổi giờ hệ thống có thể làm lịch bảo vệ lệch: app phát hiện nhảy giờ, ghi audit và không tự tắt bảo vệ ở tick phát hiện, nhưng không khôi phục được giờ đúng; các tick sau vẫn đánh giá theo wall clock mới.
- Watchdog (standalone) không tự phục hồi nếu scheduled task bị disable/xóa hoặc máy chưa đăng nhập lại. Nếu cả app và watchdog bị kill, task health khôi phục watchdog trong tối đa khoảng 60 giây; nếu watchdog không khởi động được (thiếu quyền, file bị khóa) thì app không được restart cho tới lần đăng nhập sau hoặc khi phụ huynh mở lại.
- Watchdog (standalone) giới hạn restart (mặc định 5 lần/10 phút) để tránh vòng lặp vô hạn; sau ngưỡng đó app không tự phục hồi cho tới lần đăng nhập sau hoặc khi phụ huynh mở lại.
- Service mode phụ thuộc SCM: admin/SYSTEM có thể `sc stop`, `sc config` (đổi recovery/start), `sc delete` hoặc gỡ MSI; nếu service crash 3 lần trong cùng ngày, SCM ngừng restart cho tới khi reset period (1 ngày) hoặc service được start lại. Trong thời gian service dừng, hosts section cũ vẫn còn nhưng block page/process watcher/heartbeat không chạy, và UI không thể điều khiển bảo vệ.
- Nếu dịch vụ bảo vệ không phản hồi, UI hiển thị lỗi và dừng camera/activity; UI **không** fallback sang engine cục bộ (tránh hai protection owner), nên trong sự cố service người dùng không thể tắt/bật bảo vệ từ UI cho tới khi service trở lại.
- UI vẫn chạy elevated trong bản này vì đọc/ghi DB có ACL Administrators/SYSTEM cho onboarding, báo cáo, settings và xuất CSV; service mode không xóa UAC. Chuyển các thao tác dữ liệu qua service để UI chạy standard user là việc tiếp theo.
- Nếu DB chưa từng được service tạo (ví dụ service bị disable ngay sau khi cài trước lần chạy đầu), UI service mode không tự tạo DB và onboarding có thể báo lỗi; cần start service một lần.
- Phát hiện treo dựa trên heartbeat; nếu file heartbeat/state bị khóa, hỏng hoặc ổ đĩa đầy, watchdog có thể phát hiện sai hoặc trễ.
- Uninstall PIN chỉ là best-effort; admin Windows vẫn gỡ được.
- Nếu maintainer chưa cấu hình chứng chỉ code signing, bản phát hành sẽ unsigned và chỉ có SHA-256; hãy kiểm tra checksum và chỉ cài từ release chính thức. Đặt `REQUIRE_CODE_SIGNING=true` để release fail thay vì phát hành unsigned.
- Chặn app có thể đóng/kill process và làm mất dữ liệu chưa lưu; EduFocus ưu tiên `WM_CLOSE` trước khi kill.
- PIN không bảo vệ file DB nếu phụ huynh và học sinh dùng chung tài khoản Windows; ACL thư mục dữ liệu cũng không tách được quyền trong trường hợp này.
- Webcam attention chỉ là ước lượng, không phải eye tracker y khoa.
- Ứng dụng không chống gian lận thi cử và không nên dùng cho mục đích đó.
- Không phát hiện được mọi chatbot mới hoặc chatbot nhúng trong ứng dụng khác.
- Process watcher khớp chính xác tên process (basename, không phân biệt hoa thường); app đổi tên binary hoặc chạy portable có thể không bị phát hiện. Process không có cửa sổ chính bị bỏ qua, nên app chạy nền không bị đóng.
- URL/path, wildcard subdomain và AI sidebar chưa được enforce trong MVP.
- Từ issue #24, `ProtectionEngine` đã nối adapter hosts file (issue #21), block page loopback (issue #22) và process watcher (issue #23); bật bảo vệ chặn domain/app thật. Cổng 80 bận chỉ tạo cảnh báo (Protection vẫn ON); hosts restore thất bại khi tắt ném lỗi để Protection giữ ON thay vì báo OFF giả.
- Engine debounce 30 giây cho cùng domain/tên app và ghi `BlockedAttempt` bất đồng bộ qua writer channel; số attempt có thể ít hơn thực tế và bản cuối có thể mất nếu app thoát đột ngột.
- Thay đổi rule khi Protection ON chưa tự áp dụng lại engine; cần tắt/bật hoặc khởi động lại.
- File CSV đã xuất nằm ngoài DB nên retention không xóa; phụ huynh tự quản lý, di chuyển hoặc xóa file trong `%LocalAppData%\EduFocus\exports`.
- **App vẫn yêu cầu Administrator sau khi cài** (Release manifest `requireAdministrator`) vì đọc/ghi DB có ACL Administrators/SYSTEM cho onboarding, báo cáo và settings. Từ issue #46, enforcement đã nằm trong service LocalSystem nên app không còn tự ghi hosts/kill process/quản lý task; service mode không xóa UAC. Hướng tiếp theo là chuyển truy cập dữ liệu qua service để UI chạy standard user. UAC xuất hiện mỗi lần mở app, không phải lỗi cài đặt.
- Không có service nào chống được **Windows shutdown, safe mode, hoặc can thiệp của admin/SYSTEM**; tuyên bố "không thể tắt" là sai và không được dùng trong marketing.
- Nếu người dùng chọn xóa dữ liệu khi gỡ cài đặt, thao tác là vĩnh viễn và không có recycle bin; UI cảnh báo trước khi xác nhận. Silent uninstall chỉ xóa khi đặt rõ `DELETEUSERDATA=1`.

Nếu bạn phát hiện cách vượt qua các giới hạn trên **trong điều kiện người dùng standard user**, hãy báo cáo — đó có thể là lỗ hổng thực sự.

### Kiểm thử bảo mật trước phát hành

Checklist E2E bảo mật/quyền riêng tư nằm ở [docs/security-e2e-checklist.md](docs/security-e2e-checklist.md):
ma trận bằng chứng tự động (XSS block page, CSV injection, redaction log, ACL, hosts rollback,
camera không lưu frame, DB/WAL/backup không có secret) và checklist thủ công trên VM sạch
Windows 10 22H2/Windows 11. Checklist này là **cổng bắt buộc** trước khi phát hành; kết quả được ghi
vào release checklist (#43).

---

## 🐛 Báo cáo lỗ hổng bảo mật

**KHÔNG mở public Issue cho lỗ hổng bảo mật.**

Vui lòng báo cáo qua **GitHub Private Security Advisory**:

1. Vào tab **Security** của repo.
2. Chọn **Report a vulnerability**.
3. Điền thông tin theo mẫu.

### Nội dung cần có

- Mô tả lỗ hổng và tác động tiềm tàng.
- Các bước tái hiện (steps to reproduce).
- Phiên bản/commit bị ảnh hưởng.
- Môi trường (Windows version, quyền tài khoản, cấu hình).
- Proof of concept tối thiểu, nếu có.
- Đề xuất khắc phục, nếu có.

### Thời gian phản hồi

- Xác nhận đã nhận: trong **72 giờ**.
- Đánh giá ban đầu: trong **7 ngày**.
- Vá hoặc kế hoạch vá: tùy mức độ nghiêm trọng, thường trong **30 ngày**.

### Phạm vi ưu tiên

| Mức | Ví dụ |
|---|---|
| Critical | Bypass PIN không cần quyền admin; lộ dữ liệu trẻ; RCE |
| High | Xóa/sửa dữ liệu trái phép; nâng quyền; lộ hash PIN |
| Medium | Bypass blocking với standard user; crash có thể lợi dụng |
| Low | Lỗi hardening, thông tin lộ không nhạy cảm |

### Safe harbor

Chúng tôi không theo đuổi pháp lý với nhà nghiên cứu bảo mật thiện chí, miễn là:

- Không truy cập, sửa hoặc xóa dữ liệu của người khác.
- Không gây gián đoạn dịch vụ.
- Báo cáo riêng tư và cho thời gian khắc phục hợp lý.
- Không công bố chi tiết trước khi có bản vá.

---

## 🔐 Khuyến nghị cho phụ huynh

- Dùng **tài khoản Windows riêng** cho học sinh (standard user).
- Đặt PIN mạnh, không chia sẻ với trẻ.
- Lưu recovery code ở nơi an toàn.
- Bật **Multi-App Camera** trên Windows 11 nếu muốn theo dõi trong Meet/Teams.
- Thông báo công khai với trẻ về việc sử dụng ứng dụng.
- Kiểm tra báo cáo định kỳ; không dùng để kết luận thay cho trao đổi trực tiếp.

---

## ⚖️ Tuân thủ dữ liệu cá nhân

- Ứng dụng xử lý dữ liệu có thể liên quan đến trẻ em và dữ liệu sinh trắc học (ước lượng từ webcam). Đây là **dữ liệu cá nhân nhạy cảm** theo pháp luật Việt Nam.
- Hồ sơ đã hoàn thiện để luật sư/DPO rà soát:
  - [docs/privacy-notice.md](docs/privacy-notice.md) — thông báo quyền riêng tư tiếng Việt (dữ liệu, mục đích, lưu trữ, chia sẻ, quyền, xóa, sự cố); hiển thị trong onboarding/Parent Mode và cài kèm MSI.
  - [docs/dpia.md](docs/dpia.md) — DPIA theo **Luật Bảo vệ dữ liệu cá nhân 91/2025/QH15** và **Nghị định 356/2025/NĐ-CP** (thay thế Nghị định 13/2023/NĐ-CP từ 01/01/2026), gồm bản đồ dữ liệu, đánh giá rủi ro, người chịu trách nhiệm và quy trình xử lý sự cố.
  - [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) — danh mục giấy phép bên thứ ba đầy đủ, không có GPL/AGPL/non-commercial.
- Đồng ý được thu hai lớp: MSI yêu cầu tick trước khi cài; onboarding bắt buộc xác nhận trước khi mở app và trước khi bật monitoring, timestamp lưu trong `AppSettings`.
- Còn lại trước khi phát hành thương mại: luật sư/DPO ký duyệt notice + DPIA, nộp hồ sơ theo Nghị định 356/2025/NĐ-CP nếu thuộc diện (trong 60 ngày), và rà soát lại danh mục giấy phép mỗi khi thêm dependency.
- Tài liệu này không phải tư vấn pháp lý.

---

## 🙏 Cảm ơn

Chúng tôi đánh giá cao mọi đóng góp giúp EduFocus an toàn hơn cho trẻ em và gia đình.
