# EduFocus — Installer và nâng cấp an toàn

Tài liệu này mô tả gói cài đặt MSI (WiX v4), cách nâng cấp giữ dữ liệu, cách gỡ cài đặt
hỏi rõ về dữ liệu người dùng, và checklist kiểm thử trên máy Windows sạch.

Tham chiếu: [system_architecture.md](./system_architecture.md) §14,
[database_schema.md](./database_schema.md) §16–§17, [SECURITY.md](../SECURITY.md).

---

## 1. Quyết định phát hành

| Hạng mục | Quyết định |
|---|---|
| Định dạng | MSI per-machine (WiX Toolset 4.0.6, UI + Util extension) |
| Kiến trúc | `win-x64`, self-contained single-file |
| .NET runtime | Đóng gói sẵn trong `EduFocus.exe`; máy đích không cần cài .NET |
| Windows | Windows 10/11 64-bit (`VersionNT >= 1000` và `VersionNT64`) |
| Nâng cấp | `MajorUpgrade` cùng `UpgradeCode`; bản cũ bị gỡ trước khi cài bản mới |
| Thư mục cài | `%ProgramFiles%\EduFocus` |
| Thư mục dữ liệu | `%ProgramData%\EduFocus` (ACL Administrators/SYSTEM) |
| Quyền chạy app | **Administrator** trong MVP (xem §8) |

Windows 8/8.1, MSIX/Microsoft Store và cloud auto-update nằm ngoài phạm vi.

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

./installer/Build-Installer.ps1 -PublishDir publish -Version 2.0.0
```

MSI chứa:

- `EduFocus.exe` (self-contained, component GUID cố định để nâng cấp thay thế file, không tạo bản cài thứ hai).
- Shortcut **Desktop** và **Start Menu** dùng chung cho mọi người dùng (`CommonDesktopFolder`, `CommonProgramsFolder`).
- Registry `HKLM\Software\ThanhTrunggDEV\EduFocus` với `InstallLocation` và `Version` (keypath của component shortcut/registry).
- Không có database, log, file cấu hình hay secret.

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

1. `util:CloseApplication` đóng `EduFocus.exe` (app và watchdog dùng chung file) nếu đang chạy.
2. Copy file vào `%ProgramFiles%\EduFocus`.
3. Chạy deferred custom action `--prepare-data-directory` **as SYSTEM**:
   tạo `%ProgramData%\EduFocus` và áp ACL chỉ Administrators + SYSTEM (kế thừa cho file con).
   Nếu bước này lỗi, cài đặt thất bại và rollback — app sẽ không có thư mục dữ liệu hở quyền.
4. Tạo shortcut và registry entry.

Sau khi cài, người dùng mở EduFocus từ shortcut. Lần chạy đầu:

- Onboarding (consent, PIN, recovery code) chạy trước dashboard.
- Database được tạo với incremental auto-vacuum + WAL.
- Khi phụ huynh bật bảo vệ, `WatchdogCoordinator` đăng ký hai scheduled task
  `EduFocusLogon` và `EduFocusWatchdog` (HIGHEST). Installer **không** đăng ký task:
  việc đăng ký do app elevated thực hiện theo trạng thái bảo vệ/tùy chọn StartWithWindows,
  nên không có task mồ côi khi cài nhưng chưa onboarding.

> Nếu môi trường chặn tạo scheduled task, app vẫn cài và chạy bình thường; watchdog/auto-start
> chỉ hoạt động khi chính sách cho phép. Đây là giới hạn môi trường, không phải lỗi installer.

---

## 4. Nâng cấp

- Cùng `UpgradeCode` + `MajorUpgrade Schedule="afterInstallInitialize"`: bản cũ được gỡ
  (giữ dữ liệu) trước khi bản mới được cài; không thể tồn tại hai bản cài.
- `util:CloseApplication` đóng app/watchdog trước khi thay file, tránh file bị khóa.
  Session đang chạy bị ngắt sẽ được phục hồi ở lần mở kế tiếp (`SessionInterrupted`/`Recovery`).
- Dữ liệu trong `%ProgramData%\EduFocus` không bị MSI chạm tới.
- Lần chạy đầu sau nâng cấp, `SqliteDatabaseInitializer`:
  1. kiểm tra `PRAGMA integrity_check` — hỏng thì giữ nguyên file và báo khôi phục backup;
  2. tạo backup đã verify trong `%ProgramData%\EduFocus\backups` (giữ 3 bản mới nhất);
  3. chạy migration trong transaction; lỗi thì giữ DB cũ, ghi log và ném
     `SqliteMigrationException` kèm đường dẫn backup;
  4. bật WAL và tiếp tục khởi động.
- **Rollback:** dùng `RestoreBackup` (đã kiểm tra integrity, tạo snapshot trước khi ghi đè)
  với file backup trước migration. Test `Failed_upgrade_rolls_back_and_the_backup_restores_the_previous_data`
  xác nhận dữ liệu và `__EFMigrationsHistory` được giữ nguyên sau khi migration thất bại và
  sau khi restore.

Không migrate dữ liệu từ bản 1.x (Don't Be Lazy); DB 1.x nằm ở đường dẫn khác và không bị đọc.

---

## 5. Gỡ cài đặt

### Qua UI (Apps & Features → EduFocus → Uninstall)

Sau bước xác nhận gỡ, hộp thoại **Remove EduFocus** hỏi rõ:

- Mặc định: **giữ** database, settings và backup trong `%ProgramData%\EduFocus`
  để cài lại là dùng tiếp.
- Tùy chọn: **xóa toàn bộ dữ liệu EduFocus** (không thể hoàn tác). Nếu chọn, MSI chạy
  `--uninstall-watchdog --delete-user-data` và xóa `%ProgramData%\EduFocus`.

Trước `RemoveFiles`, MSI chạy `--uninstall-watchdog` (deferred, SYSTEM, best effort):

1. gỡ scheduled task `EduFocusLogon`/`EduFocusWatchdog`;
2. yêu cầu watchdog dừng và chờ tối đa 5 giây (kill app đang chạy do `CloseApplication` đã xử lý);
3. xóa thư mục runtime `%ProgramData%\EduFocus\runtime`;
4. `IHostsFileBlocker.SelfHealAsync()` gỡ khối `### EDUFOCUS BLOCK ... ###` khỏi hosts
   (khôi phục đúng byte gốc nếu có journal) và xóa `hosts-backup`;
5. nếu được yêu cầu, xóa thư mục dữ liệu sau khi kiểm tra đường dẫn đúng là
   `%ProgramData%\EduFocus` và watchdog đã dừng.

Block page listener chạy trong tiến trình app/watchdog; dừng hai tiến trình là listener
đóng theo, không để lại service hay port chiếm dụng.

### Silent uninstall

```bat
msiexec /x "EduFocus-2.0.0-win-x64.msi" /qn
msiexec /x "EduFocus-2.0.0-win-x64.msi" /qn DELETEUSERDATA=1
```

`DELETEUSERDATA` mặc định `0`; chỉ đặt `1` khi thực sự muốn xóa vĩnh viễn dữ liệu.
Nếu watchdog không dừng kịp, CLI giữ dữ liệu và trả mã lỗi (MSI vẫn hoàn tất gỡ cài đặt).

Sau khi gỡ, còn lại tối đa:

- thư mục cài đặt `%ProgramFiles%\EduFocus` (MSI xóa khi rỗng);
- shortcut/registry (MSI xóa);
- `%ProgramData%\EduFocus` **chỉ khi** người dùng chọn giữ dữ liệu;
- log chẩn đoán `%LocalAppData%\EduFocus\logs` của từng tài khoản Windows (không chứa secret).

---

## 6. Quyền và vị trí dữ liệu

| Đường dẫn | Nội dung | ACL |
|---|---|---|
| `%ProgramFiles%\EduFocus` | binary | mặc định Program Files (admin ghi, user đọc/chạy) |
| `%ProgramData%\EduFocus` | `edufocus.db`, `backups/`, `runtime/`, `hosts-backup/` | Administrators + SYSTEM full control, protected, kế thừa |
| `%LocalAppData%\EduFocus\logs` | log kỹ thuật (redacted) | theo tài khoản người dùng |
| `%LocalAppData%\EduFocus\exports` | CSV phụ huynh tự xuất | theo tài khoản người dùng |

Installer và app đều áp ACL qua `DataDirectorySecurity`; file tạo sau (DB, WAL, backup)
kế thừa ACL thư mục. Standard user không đọc/ghi được dữ liệu học tập.

---

## 7. Kiểm thử trên máy sạch (acceptance)

Chưa thể chạy tự động trong CI (cần VM Windows). Checklist bắt buộc trước phát hành:

**Windows 10 22H2 và Windows 11 (64-bit), mỗi bản trên VM sạch:**

1. Cài bằng tài khoản Administrator → thành công; shortcut Desktop + Start Menu tồn tại;
   `%ProgramData%\EduFocus` tồn tại với ACL chỉ Administrators/SYSTEM
   (`icacls "%ProgramData%\EduFocus"`).
2. Mở app bằng **standard user** → xác nhận UAC xuất hiện (hành vi dự kiến) và app chạy sau khi
   nâng quyền; hoàn tất onboarding, bật bảo vệ → hosts có marker, scheduled task được tạo
   (nếu chính sách cho phép), block page hoạt động.
3. Cài đè bản trước đó (nâng cấp) → chỉ còn một entry trong Apps & Features; mở app → DB/settings
   còn nguyên, session cũ được phục hồi, migration chạy với backup trước đó.
4. Gỡ cài đặt, **giữ dữ liệu** → không còn hosts marker, không còn scheduled task
   `EduFocus*`, không còn `runtime`/`hosts-backup`; `%ProgramData%\EduFocus\edufocus.db` còn.
5. Gỡ cài đặt, **xóa dữ liệu** → `%ProgramData%\EduFocus` không còn.
6. Giả lập migration lỗi (ví dụ chạy bản mới trên DB có cột xung đột) → app báo lỗi an toàn,
   DB cũ và backup còn; restore backup thành công.
7. Kiểm tra gói: `Get-ChildItem` payload không có `.pfx/.pem/.snk/.key`, không có `.db/.log`;
   không có chứng chỉ/private key trong MSI.

Kết quả từng lần chạy ghi vào release checklist (issue #43).

---

## 8. Độ lệch elevation đã biết

Acceptance của issue #40 ghi "app không cần admin để chạy sau khi cài". MVP **chưa đáp ứng**
điều này: `app.manifest` (Release) yêu cầu `requireAdministrator` vì app phải ghi hosts file,
kill process bị chặn và quản lý scheduled task.

- Debug manifest dùng `asInvoker` để phát triển.
- Hướng giải quyết dài hạn là **Windows Service agent độc lập** (post-MVP issue #46):
  service chạy SYSTEM làm phần đặc quyền, UI chạy standard user.
- Trong MVP, installer vẫn cài/shortcut bình thường; UAC xuất hiện mỗi lần mở app.
  Đây là độ lệch được ghi nhận, không phải lỗi của gói cài.

---

## 9. Build cục bộ và CI

- CI release (`.github/workflows/release.yml`) publish self-contained, xóa `*.pdb/*.lib`,
  cài WiX 4.0.6 + `WixToolset.UI.wixext` + `WixToolset.Util.wixext`, rồi gọi
  `installer/Build-Installer.ps1` với version lấy từ tag.
- Build thủ công cần WiX 4.0.6 và hai extension (global hoặc local):

```powershell
wix extension add --global WixToolset.UI.wixext/4.0.6
wix extension add --global WixToolset.Util.wixext/4.0.6
./installer/Build-Installer.ps1 -PublishDir publish -Version 2.0.0
```

- File sinh ra: `installer/obj/Payload.wxs` (không commit) và
  `artifacts/EduFocus-<version>-win-x64.msi` (không commit).
- Lỗi WiX thường gặp: thiếu extension Util (custom action `CloseApplication`), version không
  phải dạng số (MSI không nhận `-beta`), hoặc publish thiếu `EduFocus.exe`.
