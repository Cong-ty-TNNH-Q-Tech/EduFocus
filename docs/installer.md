# EduFocus — Installer và nâng cấp an toàn

Tài liệu này mô tả gói cài đặt MSI (WiX v4), cách nâng cấp giữ dữ liệu, cách gỡ cài đặt
hỏi rõ về dữ liệu người dùng, và checklist kiểm thử trên máy Windows sạch.

Tham chiếu: [system_architecture.md](./system_architecture.md) §14,
[database_schema.md](./database_schema.md) §16–§18, [release-process.md](./release-process.md),
[SECURITY.md](../SECURITY.md).

---

## 1. Quyết định phát hành

| Hạng mục | Quyết định |
|---|---|
| Định dạng | MSI per-machine (WiX Toolset 4.0.6, UI + Util extension) |
| Kiến trúc | `win-x64`, self-contained single-file |
| .NET runtime | Đóng gói sẵn trong `EduFocus.exe`; máy đích không cần cài .NET |
| Windows | Windows 10/11 64-bit (`VersionNT64` và `CurrentBuildNumber` từ registry ≥ 10240) |
| Nâng cấp | `MajorUpgrade` cùng `UpgradeCode`; bản cũ bị gỡ trước khi cài bản mới |
| Thư mục cài | `%ProgramFiles%\EduFocus` |
| Thư mục dữ liệu | `%ProgramData%\EduFocus` (ACL Administrators/SYSTEM) |
| Bảo vệ nền | Windows Service `EduFocusProtection` (`EduFocus.Service.exe`, LocalSystem, auto-start, restart khi crash) |
| Quyền chạy app | **Administrator** trong bản này (xem §8) |

Windows 8/8.1, MSIX/Microsoft Store và cloud auto-update nằm ngoài phạm vi.

> Windows Installer trả về `VersionNT = 603` và `WindowsBuild = 9600` trên Windows 10/11
> vì gói MSI không có OS manifest. Vì vậy launch condition đọc `CurrentBuildNumber`
> thật từ registry (`>= 10240`) thay vì `VersionNT`; `Installed OR` bảo đảm việc gỡ
> cài đặt không bị chặn.

---

## 2. Thành phần gói cài

`installer/Build-Installer.ps1` lấy toàn bộ file từ thư mục publish (trừ `*.pdb`, `*.lib`,
`*.wixpdb`), sinh `installer/obj/Payload.wxs` rồi gọi `wix build`:

```powershell
dotnet publish src/EduFocus.WPF/EduFocus.WPF.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true -p:Version=2.0.0 `
  -o publish/

dotnet publish src/EduFocus.Service/EduFocus.Service.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true -p:Version=2.0.0 `
  -o publish/

dotnet publish src/EduFocus.NativeHost/EduFocus.NativeHost.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true -p:Version=2.0.0 `
  -o publish/native-host/

./installer/Build-Installer.ps1 -PublishDir publish -Version 2.0.0
```

MSI chứa:

- `EduFocus.exe` (self-contained, component GUID cố định để nâng cấp thay thế file, không tạo bản cài thứ hai).
- `EduFocus.Service.exe` (self-contained, component GUID cố định) — Windows Service
  `EduFocusProtection` chạy LocalSystem, auto-start, restart khi crash (`ServiceInstall`,
  `ServiceControl Start="install" Stop="both" Remove="uninstall"`, `util:ServiceConfig` 3 lần/60 giây).
  `Build-Installer.ps1` từ chối build nếu thiếu file này hoặc có nhiều hơn một bản ở gốc publish.
- Registry marker `HKLM\Software\EduFocus\ServiceMode = 1` — UI đọc để chạy service mode và không mở
  engine cục bộ; component có GUID cố định để giữ qua nâng cấp.
- `native-host\EduFocus.NativeHost.exe` (self-contained; component GUID cố định) — cầu nối native
  messaging cho extension trình duyệt. `Build-Installer.ps1` từ chối build nếu thiếu file này vì
  `Product.wxs` tham chiếu nó cho custom action đăng ký/gỡ đăng ký.
- Shortcut **Desktop** và **Start Menu** dùng chung cho mọi người dùng (`CommonDesktopFolder`, `CommonProgramsFolder`).
- Registry `HKLM\Software\ThanhTrunggDEV\EduFocus` với `InstallLocation` và `Version` (keypath của component shortcut/registry).
- `docs\privacy-notice.md`, `THIRD-PARTY-NOTICES.md` và `LICENSE` (bản sao chỉ đọc từ repo) trong thư mục cài.
- Thư viện native SQLCipher (`e_sqlcipher.dll` từ `SQLitePCLRaw.lib.e_sqlcipher`) đi kèm trong bundle
  self-contained của `EduFocus.exe`/`EduFocus.Service.exe`; máy đích không cần cài SQLCipher riêng.
- Không có database, log, file cấu hình, `edufocus.key` hay secret. Extension trình duyệt **không** nằm trong MSI;
  nó được phát hành dưới dạng
  `EduFocus-<version>-browser-extension.zip` (xem [browser-extension.md](./browser-extension.md)).

`Package/@Codepage` là `65001` (UTF-8) để toàn bộ text tiếng Việt trong UI cài đặt hiển thị đúng;
codepage 1252/1258 không biểu diễn hoặc không round-trip được chữ Việt precomposed.

**Chống secret trong payload:** script từ chối build nếu publish chứa:

- phần mở rộng private key/secret: `.pfx`, `.p12`, `.snk`, `.key`, `.pem`, `.cer`, `.crt`,
  `.jks`, `.keystore`, `.pvk`, `.env`;
- file tên `secrets.json`, `credentials.json`, `id_rsa`, `id_ed25519`;
- file text (`.config`, `.json`, `.xml`, `.txt`, `.yml`, `.yaml`, `.ps1`, `.cmd`, `.bat`,
  `.ini`, `.properties`) chứa marker `-----BEGIN ... PRIVATE KEY-----`;
- dữ liệu runtime `.db`, `.db-wal`, `.db-shm`, `.log`, `.bak`, `.tmp`.

---

## 3. Cài đặt lần đầu (fresh install)

Trình tự MSI thực hiện:

1. `WelcomeDlg` → `PrivacyConsentDlg`: tóm tắt thông báo quyền riêng tư, checkbox **Tôi đã đọc và
   đồng ý** (property `PRIVACY_CONSENT`). Nút Next chỉ mở `InstallDirDlg` khi đã tick; Back quay
   lại Welcome, Cancel hủy cài đặt. Bản đầy đủ nằm ở [privacy-notice.md](./privacy-notice.md).
   Silent install (`/qn`) bỏ qua UI; consent vẫn được ghi trong onboarding ở lần chạy đầu.
   > Thông báo dùng control `Text` tự xuống dòng thay cho `ScrollableText` (control này hiển thị
   > hộp trắng trên Windows 10/11). Không dùng dấu `\` trong nội dung: msiexec áp dụng path
   > ellipsis và thay đoạn giữa bằng `...`.
2. `util:CloseApplication` đóng `EduFocus.exe` (app và watchdog dùng chung file) nếu đang chạy.
3. Copy file vào `%ProgramFiles%\EduFocus`.
4. Chạy deferred custom action `--prepare-data-directory` **as SYSTEM**:
   tạo `%ProgramData%\EduFocus` và áp ACL chỉ Administrators + SYSTEM (kế thừa cho file con).
   Nếu bước này lỗi, cài đặt thất bại và rollback — app sẽ không có thư mục dữ liệu hở quyền.
5. Cài service `EduFocusProtection` từ component `ServiceExe` và **start ngay**
   (`ServiceControl Start="install"`). Service khởi tạo `edufocus.key` (DPAPI) và DB SQLCipher nếu
   chưa có, migrate DB, seed rule, reconcile bảo vệ và mở control pipe; screen SCM nhận trạng thái
   START PENDING → RUNNING. Nếu service không start được, MSI trả lỗi và rollback. Khi bảo vệ được
   bật, service tạo CA cục bộ trong `%ProgramData%\EduFocus\certificates` và cài public root vào
   `LocalMachine\Root` trước khi mở listener HTTPS 443; thư mục này không có trong MSI và chỉ xuất
   hiện sau lần bật bảo vệ đầu tiên.
6. Chạy deferred custom action `--register --scope machine --manifest-dir [INSTALLFOLDER]native-host`
   **as SYSTEM**: ghi `com.edufocus.native_host.json` cạnh host và tạo registry
   `HKLM\Software\Google\Chrome\NativeMessagingHosts\com.edufocus.native_host` +
   `HKLM\Software\Microsoft\Edge\NativeMessagingHosts\com.edufocus.native_host` (cả view 64-bit và
   `WOW6432Node`). Đăng ký là best-effort: lỗi được ghi vào MSI log nhưng không chặn cài đặt vì
   phần chặn hosts/app vẫn hoạt động; có thể đăng ký lại thủ công bằng
   `scripts/register-browser-extension.ps1`.
7. Tạo shortcut, registry entry và marker `ServiceMode`.

Extension vẫn phải được cài một lần trong trình duyệt (Chrome Web Store, giải nén ZIP và
**Load unpacked**, hoặc enterprise policy). Quy trình và kiểm tra quyền nằm ở
[browser-extension.md](./browser-extension.md).

Sau khi cài, người dùng mở EduFocus từ shortcut. Lần chạy đầu:

- Onboarding (consent quyền riêng tư, PIN, recovery code) chạy trước dashboard; nút
  **Xem thông báo quyền riêng tư đầy đủ** mở bản render từ Markdown nhúng, và bản cài kèm nằm ở
  `%ProgramFiles%\EduFocus\docs\privacy-notice.md`.
- Database được service tạo/migrate với incremental auto-vacuum + WAL (service chạy trước lần mở app).
- UI đọc marker `ServiceMode`, chạy ở service mode, **không** đăng ký scheduled task mới và dọn
  `EduFocusLogon`/`EduFocusWatchdog` còn sót từ bản cũ. Bật/tắt bảo vệ trong UI gửi lệnh qua pipe;
  service giữ engine, heartbeat, lịch và retention.
- Nếu DB chưa tồn tại vì service chưa từng chạy (ví dụ bị disable ngay sau cài), khởi động service
  một lần để tạo DB trước khi dùng UI.

> Chế độ standalone (chạy từ source/Debug, không có marker) vẫn dùng watchdog như trước: khi
> phụ huynh bật bảo vệ, `WatchdogCoordinator` đăng ký hai scheduled task `EduFocusLogon` và
> `EduFocusWatchdog` (HIGHEST). Nếu môi trường chặn tạo scheduled task, app vẫn chạy bình thường;
> watchdog/auto-start chỉ hoạt động khi chính sách cho phép. Đây là giới hạn môi trường, không phải
> lỗi installer.

---

## 4. Nâng cấp

- Cùng `UpgradeCode` + `MajorUpgrade Schedule="afterInstallInitialize"`: bản cũ được gỡ
  (giữ dữ liệu) trước khi bản mới được cài; không thể tồn tại hai bản cài.
- `util:CloseApplication` đóng app/watchdog trước khi thay file; `ServiceControl Stop="both"`
  dừng service cũ trước `RemoveFiles` rồi start service mới (`Start="install"`). Session đang chạy
  bị ngắt khi service dừng sẽ được ghi `ServiceStopped` + `Interrupted` và phục hồi ở lần start kế
  tiếp (`Recovery`).
- Dữ liệu trong `%ProgramData%\EduFocus` không bị MSI chạm tới; marker `ServiceMode` giữ nguyên qua
  nâng cấp nhờ component GUID cố định. Thư mục `certificates` và root tin cậy cũng được giữ nguyên;
  service tiếp tục dùng CA hiện có nên không phải cài lại chứng chỉ khi nâng cấp.
- **Mã hóa dữ liệu:** service mở DB bằng khóa 32 byte lưu qua DPAPI trong
  `%ProgramData%\EduFocus\edufocus.key`. Nếu DB hiện tại còn plaintext (bản trước issue #47),
  initializer tạo backup, export sang SQLCipher, verify rồi thay file; lỗi thì giữ DB cũ + backup.
  Thiếu khóa hoặc khóa hỏng/không giải mã được → fail-closed, không tạo khóa mới và không ghi đè DB
  (xem [database_schema.md](./database_schema.md) §18).
- Lần start đầu sau nâng cấp, service chạy `SqliteDatabaseInitializer`:
  1. mở DB bằng khóa, kiểm tra `PRAGMA integrity_check` — hỏng, sai khóa hoặc không giải mã được thì
     giữ nguyên file và báo khôi phục backup;
  2. tạo backup đã verify trong `%ProgramData%\EduFocus\backups` (giữ 3 bản mới nhất; backup của DB
     mã hóa cũng được mã hóa);
  3. chạy migration trong transaction; lỗi thì giữ DB cũ, ghi log và ném
     `SqliteMigrationException` kèm đường dẫn backup;
  4. bật WAL và tiếp tục khởi động.
- Nếu migration lỗi, service báo START PENDING → STOPPED với `ServiceSpecificExitCode = 1`; SCM
  restart theo cấu hình và mỗi lần thử lại đều giữ DB cũ + backup. Admin có thể chạy
  `EduFocus.Service.exe --console` để xem lỗi trên console mà không cần gỡ cài đặt.
- **Rollback:** dùng `RestoreBackup` (đã kiểm tra integrity, tạo snapshot trước khi ghi đè)
  với file backup trước migration. Test `Failed_upgrade_rolls_back_and_the_backup_restores_the_previous_data`
  xác nhận dữ liệu và `__EFMigrationsHistory` được giữ nguyên sau khi migration thất bại và
  sau khi restore.

Không migrate dữ liệu từ bản 1.x (Don't Be Lazy); DB 1.x nằm ở đường dẫn khác và không bị đọc.

---

## 5. Gỡ cài đặt

### Qua UI (Apps & Features → EduFocus → Uninstall)

Sau bước xác nhận gỡ, hộp thoại **Remove EduFocus** hỏi rõ:

- Mặc định: **giữ** database, settings, backup và `edufocus.key` trong `%ProgramData%\EduFocus`
  để cài lại là dùng tiếp. Key giữ cùng DB — thiếu key thì DB không đọc được (khóa gắn thiết bị).
- Tùy chọn: **xóa toàn bộ dữ liệu EduFocus** (không thể hoàn tác). Nếu chọn, MSI chạy
  `--uninstall-watchdog --delete-user-data` và xóa `%ProgramData%\EduFocus` (gồm DB, backup,
  `edufocus.key` và log service).

Trước `RemoveFiles`, MSI dừng service qua `ServiceControl Stop="both" Wait="yes"` và gỡ service
(`Remove="uninstall"`), sau đó chạy `--unregister --scope machine` (deferred, SYSTEM, best effort) để
xóa registry native messaging và manifest `native-host\com.edufocus.native_host.json`, rồi chạy
`--uninstall-watchdog` (deferred, SYSTEM, best effort):

1. gỡ scheduled task `EduFocusLogon`/`EduFocusWatchdog` còn sót từ bản standalone;
2. yêu cầu watchdog cũ dừng và chờ tối đa 5 giây (kill app đang chạy do `CloseApplication` đã xử lý);
3. xóa thư mục runtime `%ProgramData%\EduFocus\runtime`;
4. `IHostsFileBlocker.SelfHealAsync()` gỡ khối `### EDUFOCUS BLOCK ... ###` khỏi hosts
   (khôi phục đúng byte gốc nếu có journal) và xóa `hosts-backup`;
5. `IBrowserDnsPolicyPort.RestoreAsync()` khôi phục policy Secure DNS trước đó của Chrome/Edge (hoặc
   xóa value nếu trước đó chưa có) và xóa backup `HKLM\SOFTWARE\EduFocus\BrowserDnsPolicy`; lỗi được
   log và trả mã lỗi nhưng không chặn gỡ cài đặt;
6. `ILocalCertificateAuthority.RemoveTrustAsync()` gỡ mọi root "EduFocus Local Blocking CA" khỏi
   `LocalMachine\Root`, rồi xóa thư mục `%ProgramData%\EduFocus\certificates` (khóa CA + file CA).
   Bước này chạy **kể cả khi giữ dữ liệu**; lỗi được log và trả mã lỗi nhưng không chặn gỡ cài đặt
   (khi đó phải xóa tay trong Certificate Manager);
7. nếu được yêu cầu, xóa thư mục dữ liệu (kể cả `edufocus.key`) sau khi kiểm tra đường dẫn đúng là
   `%ProgramData%\EduFocus` và watchdog/service đã dừng.

Block page listener và process watcher chạy trong service; SCM stop là listener đóng theo, không để
lại port 80/443 hay process mồ côi. MSI xóa component `ServiceModeRegistry` nên lần cài sau (nếu dùng
bản standalone) không còn marker. Gỡ service cần quyền admin, giống gỡ MSI.

### Silent uninstall

```bat
msiexec /x "EduFocus-2.0.0-win-x64.msi" /qn
msiexec /x "EduFocus-2.0.0-win-x64.msi" /qn DELETEUSERDATA=1
```

`DELETEUSERDATA` mặc định `0`; chỉ đặt `1` khi thực sự muốn xóa vĩnh viễn dữ liệu.
Nếu watchdog cũ không dừng kịp, CLI giữ dữ liệu và trả mã lỗi (MSI vẫn hoàn tất gỡ cài đặt).
Service được SCM dừng trước `RemoveFiles`; nếu stop timeout, MSI vẫn tiếp tục gỡ và service row bị xóa.

Sau khi gỡ, còn lại tối đa:

- thư mục cài đặt `%ProgramFiles%\EduFocus` (MSI xóa khi rỗng);
- shortcut/registry/service row (MSI xóa);
- `%ProgramData%\EduFocus` **chỉ khi** người dùng chọn giữ dữ liệu (gồm DB mã hóa, `edufocus.key`,
  backup và log service); thư mục `certificates` **luôn** bị xóa cùng root đã gỡ trust;
- log chẩn đoán `%LocalAppData%\EduFocus\logs` của từng tài khoản Windows (UI, không chứa secret).

---

## 6. Quyền và vị trí dữ liệu

| Đường dẫn | Nội dung | ACL |
|---|---|---|
| `%ProgramFiles%\EduFocus` | `EduFocus.exe`, `EduFocus.Service.exe`, `native-host\EduFocus.NativeHost.exe`, manifest native messaging | mặc định Program Files (admin ghi, user đọc/chạy) |
| `%ProgramData%\EduFocus` | `edufocus.db` (SQLCipher), `edufocus.key`, `backups/` (mã hóa), `runtime/`, `hosts-backup/`, `logs/` (service), `certificates/` (CA cục bộ cho block page HTTPS) | Administrators + SYSTEM full control, protected, kế thừa |
| `%LocalAppData%\EduFocus\logs` | log kỹ thuật của UI (redacted) | theo tài khoản người dùng |
| `%LocalAppData%\EduFocus\exports` | CSV phụ huynh tự xuất | theo tài khoản người dùng |

Service chạy SYSTEM nên không có `%LocalAppData%`; `FileLogger` của service dùng
`%ProgramData%\EduFocus\logs` với cùng redaction, rotation 10 MB và retention 14 ngày.

Installer và app đều áp ACL qua `DataDirectorySecurity`; file tạo sau (DB, WAL, backup, key)
kế thừa ACL thư mục. Standard user không đọc/ghi được dữ liệu học tập. Thư mục `certificates` do
`LocalCertificateAuthority` tạo muộn hơn (lần bật bảo vệ đầu tiên) và tự áp ACL
Administrators/SYSTEM; MSI không đóng gói khóa CA hay chứng chỉ nào, và root tin cậy chỉ được cài
trong thời gian service chạy bảo vệ.

`edufocus.key` do service (SYSTEM) tạo lần đầu và không nằm trong MSI/backup; khóa được bảo vệ bằng
DPAPI `LocalMachine` nên chỉ giải mã được trên đúng máy đã tạo. Thiếu key, key hỏng hoặc chép DB
sang máy khác đều dẫn tới fail-closed: service không tạo key mới và không ghi đè DB; xem quy trình
hỗ trợ trong [database_schema.md](./database_schema.md) §18.

---

## 7. Kiểm thử trên máy sạch (acceptance)

Chưa thể chạy tự động trong CI (cần VM Windows). Checklist bắt buộc trước phát hành:

> Checklist cài đặt dưới đây tập trung vào gói MSI/vòng đời cài đặt. Phần kiểm thử bảo mật và
> quyền riêng tư (XSS, redaction, ACL file sinh ra, hosts rollback, camera, gỡ listener/watchdog)
> nằm ở [security-e2e-checklist.md](./security-e2e-checklist.md); chạy cả hai và ghi kết quả vào
> [release-checklist.md](./release-checklist.md).

**Windows 10 22H2 và Windows 11 (64-bit), mỗi bản trên VM sạch:**

1. Cài bằng tài khoản Administrator → dialog **Thông báo quyền riêng tư** hiện trước khi copy file,
   Next không tiến được khi chưa tick, Back/Cancel hoạt động, text tiếng Việt không lỗi font;
   hoàn tất cài đặt, shortcut Desktop + Start Menu tồn tại; thư mục cài có
   `docs\privacy-notice.md`, `THIRD-PARTY-NOTICES.md`, `LICENSE`;
   `%ProgramData%\EduFocus` tồn tại với ACL chỉ Administrators/SYSTEM
   (`icacls "%ProgramData%\EduFocus"`).
2. Kiểm tra service: `Get-Service EduFocusProtection` = Running; `EduFocus.Service.exe --health`
   trả `running`; registry `HKLM\SOFTWARE\EduFocus\ServiceMode = 1`; `sc qc EduFocusProtection`
   thấy auto-start và recovery 60 giây; `%ProgramData%\EduFocus\edufocus.key` tồn tại với header
   `EDUFKEY1` và phần khóa 32 byte đã mã hóa DPAPI.
3. Mở app bằng **standard user** → xác nhận UAC xuất hiện (hành vi dự kiến) và app chạy sau khi
   nâng quyền; hoàn tất onboarding, bật bảo vệ → hosts có marker, block page hoạt động. Đóng UI
   hoàn toàn → hosts marker vẫn còn, block page vẫn trả trang, service vẫn Running; không có
   scheduled task `EduFocus*` mới.
4. Kill tiến trình service (`taskkill /f /pid` của `EduFocus.Service.exe` với quyền admin) → SCM
   restart trong khoảng 60 giây; sau restart hosts marker còn, session đang chạy `Interrupted` với
   note `ServiceStopped`, audit `ProtectionServiceStarted`/`ProtectionServiceStopped`.
5. Reboot máy → service tự start, protection ON được reconcile, phiên `Recovery` được tạo nếu
   trước đó bảo vệ ON; UI mở lại thấy trạng thái khớp service.
6. Cài đè bản trước đó (nâng cấp) → chỉ còn một entry trong Apps & Features; service dừng/start
   đúng, DB/settings còn nguyên, session cũ được phục hồi, migration chạy với backup trước đó.
7. Gỡ cài đặt, **giữ dữ liệu** → service row biến mất, không còn hosts marker, không còn scheduled
   task `EduFocus*`, không còn `runtime`/`hosts-backup`; `%ProgramData%\EduFocus\edufocus.db` và
   `edufocus.key` còn.
8. Gỡ cài đặt, **xóa dữ liệu** → `%ProgramData%\EduFocus` không còn (DB, backup, `edufocus.key`,
   log service).
9. Giả lập migration lỗi (ví dụ chạy bản mới trên DB có cột xung đột) → service báo lỗi an toàn,
   DB cũ và backup còn; restore backup thành công; `--console` hiển thị lỗi.
10. Kiểm tra gói: payload có đúng một `EduFocus.Service.exe`; thư viện native SQLCipher nằm trong
    bundle self-contained (`e_sqlcipher.dll`); không có `.pfx/.pem/.snk/.key` (gồm `edufocus.key`),
    không có `.db/.log`; không có chứng chỉ/private key trong MSI.
11. Với bản tải từ GitHub Release: đối chiếu SHA-256 với `SHA256SUMS.txt` và chạy
    `Get-AuthenticodeSignature` trên MSI (khi bản phát hành được ký) trước khi cài.
12. Cài silent (`msiexec /i "EduFocus-2.0.0-win-x64.msi" /qn`) → thành công, không UI; service
    Running; mở app lần đầu vẫn bắt buộc onboarding consent trước khi vào dashboard.
13. Cài browser extension, bật bảo vệ → popup **Đang bảo vệ**; rule URL/path chặn đúng; blocked
    attempt ghi URL đã làm sạch; tắt EduFocus → trình duyệt không bị chặn (fail-open). Chi tiết
    từng bước ở [browser-extension.md](./browser-extension.md) §7.
14. Gỡ cài đặt → registry `NativeMessagingHosts` của Chrome/Edge và
    `native-host\com.edufocus.native_host.json` bị xóa; không còn native host mồ côi.
15. Kiểm tra mã hóa at-rest: 16 byte đầu của `edufocus.db` **không** phải `SQLite format 3`;
    mở DB bằng công cụ SQLite thường (không có khóa) thất bại; backup trong `backups/` cũng không
    đọc được nếu không có key; log service không chứa key. Chi tiết lệnh ở
    [security-e2e-checklist.md](./security-e2e-checklist.md) SEC-E2E-27.
16. Kiểm tra khóa: đổi tên `edufocus.key` rồi start lại service → service fail-closed, **không** tạo
    key mới và DB giữ nguyên; trả key lại → service Running và dữ liệu còn nguyên. Chép
    `edufocus.db` sang VM khác → không mở được (khóa gắn thiết bị); restore backup trên chính máy
    có key hoạt động (SEC-E2E-28).
17. CA cục bộ và HTTPS block page: bật bảo vệ → `%ProgramData%\EduFocus\certificates\block-root-ca.edufocus`
    tồn tại với header `EDUCA1` và ACL Administrators/SYSTEM; Certificate Manager (Local Computer →
    Trusted Root) có "EduFocus Local Blocking CA"; mở `https://chatgpt.com` trong Chrome/Edge bị chặn
    → hiển thị trang chặn, **không** có cảnh báo chứng chỉ; `BlockedAttempt` ghi domain (không path).
    Tắt bảo vệ → root biến mất, listener 443 đóng.
18. Port 443 đang bị ứng dụng khác chiếm → bật bảo vệ: protection vẫn ON, UI có cảnh báo HTTPS không
    khả dụng, HTTP block page vẫn hoạt động; đóng ứng dụng chiếm cổng rồi tắt/bật lại → HTTPS hoạt động.
19. Firefox (nếu có) → trang chặn HTTPS hiện cảnh báo chứng chỉ nhưng domain vẫn bị chặn; ghi nhận là
    giới hạn đã biết, không phải lỗi cài đặt.
20. Gỡ cài đặt **giữ dữ liệu** → root "EduFocus Local Blocking CA" không còn trong Trusted Root,
    thư mục `certificates` bị xóa, `edufocus.db`/`edufocus.key` còn nguyên.

Kết quả từng lần chạy ghi vào [release-checklist.md](./release-checklist.md) §2.4. Quy trình tag,
ký số, checksum và dry run nằm ở [release-process.md](./release-process.md); ngưỡng hiệu năng và
soak 8 giờ nằm ở [performance-report.md](./performance-report.md).

---

## 8. Elevation và service mode

Từ issue #46, phần đặc quyền (hosts, block page, process watcher, heartbeat, lịch) đã chuyển vào
Windows Service `EduFocusProtection` chạy LocalSystem; UI không còn tự ghi hosts/kill process/quản lý
task. Tuy vậy `app.manifest` (Release) **vẫn** yêu cầu `requireAdministrator` trong bản này vì UI
đọc/ghi trực tiếp DB có ACL Administrators/SYSTEM cho onboarding, báo cáo, settings và xuất CSV.

- Debug/standalone manifest dùng `asInvoker`; không có marker service thì app dùng watchdog như cũ.
- Hướng tiếp theo: chuyển truy cập dữ liệu qua service (hoặc IPC dữ liệu có phân quyền) để UI chạy
  standard user; khi đó UAC mới có thể bỏ.
- Trong bản này, installer cài/shortcut bình thường; UAC xuất hiện mỗi lần mở app. Đây là độ lệch
  được ghi nhận, không phải lỗi của gói cài hay service.

---

## 9. Build cục bộ và CI

- CI release (`.github/workflows/release.yml`) chạy test trước khi publish self-contained,
  publish **`EduFocus.exe` + `EduFocus.Service.exe` vào `publish/`**, publish thêm native host vào
  `publish/native-host`, kiểm tra ProductVersion/FileVersion của cả ba exe khớp tag, xóa
  `*.pdb/*.lib`, ký cả ba exe cùng MSI nếu có chứng chỉ trong GitHub Secrets, cài WiX 4.0.6 +
  `WixToolset.UI.wixext` + `WixToolset.Util.wixext`, gọi `installer/Build-Installer.ps1`
  với version lấy từ tag, đóng gói browser extension, tạo `SHA256SUMS.txt` và release notes từ
  CHANGELOG.
- CI thường (`.github/workflows/ci.yml`) chạy `node --test browser-extension/tests`, smoke-test
  `scripts/pack-browser-extension.ps1`, và publish framework-dependent cả WPF, service và native
  host để phát hiện lỗi biên dịch sớm.
- Workflow hỗ trợ `workflow_dispatch` với `dry_run = true` để chạy đầy đủ trên branch thử
  nghiệm mà không tạo release. Chi tiết signing, secret và checklist:
  [release-process.md](./release-process.md).
- Build thủ công cần WiX 4.0.6 và hai extension (global hoặc local):

```powershell
wix extension add --global WixToolset.UI.wixext/4.0.6
wix extension add --global WixToolset.Util.wixext/4.0.6
./installer/Build-Installer.ps1 -PublishDir publish -Version 2.0.0
```

- File sinh ra: `installer/obj/Payload.wxs` (không commit) và
  `artifacts/EduFocus-<version>-win-x64.msi` (không commit).
- Lỗi WiX thường gặp: thiếu extension Util (custom action `CloseApplication`/`ServiceConfig`),
  version không phải dạng số (MSI không nhận `-beta`), publish thiếu `EduFocus.exe` hoặc
  `EduFocus.Service.exe` (script từ chối build nếu thiếu, hoặc có nhiều hơn một service exe ở gốc
  publish).
