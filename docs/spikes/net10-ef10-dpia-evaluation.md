# Đánh giá .NET 10 / EF Core 10 và DPIA sơ bộ

- **Issue:** [#4 - Spike: Xác nhận .NET 10/EF Core 10 và DPIA sơ bộ](https://github.com/Cong-ty-TNNH-Q-Tech/EduFocus/issues/4)
- **Ngày đo:** 2026-10-29
- **Trạng thái:** Spike hoàn tất; các thay đổi bắt buộc đã được áp dụng ở issue #6 (2026-10-29)
- **Phạm vi:** kiểm tra .NET 10 SDK, WPF `net10.0-windows`, EF Core 10, WiX và các package hiện dùng; xác định điểm vỡ, thứ tự migration, rollback; lập data map và checklist DPIA sơ bộ theo Luật BVDLCN 91/2025/QH15 và Nghị định 356/2025/NĐ-CP.

> **Ghi chú lịch sử:** spike được đo trước issue #5, nên lệnh, đường dẫn và tên artifact trong tài liệu giữ tên cũ `DontBeLazy.*`. Sau đổi tên, thay bằng `EduFocus.*` (ví dụ `EduFocus.slnx`, `src/EduFocus.WPF`, `edufocus.db`).

## 1. Kết luận

1. **Nâng cấp .NET 10 khả thi.** Toàn bộ solution build thành công trên .NET 10, 57/57 unit test đạt, publish framework-dependent và self-contained single-file đều thành công. EF Core 10 áp dụng đủ 4 migration hiện có; WiX 4.0.6 tạo được MSI.
2. **Điểm vỡ bắt buộc phải xử lý là phiên bản package, không phải API.** EF Core 10.0.14 yêu cầu `Microsoft.Extensions.DependencyInjection >= 10.0.14`, trong khi solution đang tham chiếu 10.0.6; nếu chỉ nâng EF Core sẽ gặp `NU1605`. Phải đồng bộ toàn bộ `Microsoft.Extensions.*` và `System.Management` lên 10.0.14.
3. **.NET 9 hết hỗ trợ ngày 10/11/2026** (10 November 2026), chỉ còn khoảng 12 ngày tại thời điểm đo. .NET 10 là bản LTS, hết hỗ trợ 14/11/2028. Việc nâng cấp là ưu tiên P0, không chỉ là cải tiến.
4. **Windows 10 22H2 không nằm trong danh sách OS được .NET 10 hỗ trợ chính thức.** Ma trận của Microsoft chỉ gồm Windows 11 23H2/24H2/25H2/26H1 và Windows 10 21H2/1809/1607 bản Enterprise/IoT. Máy đo là Windows 10 19045 (22H2), tức môi trường best-effort; sản phẩm nên lấy Windows 11 23H2+ làm nền tảng chính và ghi rõ giới hạn.
5. **DPIA sơ bộ xác định dữ liệu trẻ em và dữ liệu từ camera/attention là rủi ro cao.** Việc không lưu frame/landmark/embedding làm giảm rủi ro nhưng không loại bỏ nghĩa vụ đánh giá tác động, thông báo và đồng thuận. Cần hoàn thiện DPIA, privacy notice, luồng đồng thuận của cha mẹ/người giám hộ và của trẻ từ đủ 7 tuổi trước khi phát hành.
6. **Code hiện tại có các khoảng trống dữ liệu phải xử lý ở Phase 1/2:** Gemini API nhận nội dung task và API key nằm trong query string; kiểm tra cập nhật GitHub chạy tự động; DB/log nằm trong profile người dùng thay vì `%ProgramData%\EduFocus`; gỡ cài đặt không xóa DB/log. Đây là các mục phải đóng trước khi coi DPIA là hoàn chỉnh.
7. **Không có thay đổi production trong spike này.** Mọi thử nghiệm chạy trên bản sao tạm ngoài repo; repo chỉ thêm tài liệu đánh giá.

## 2. Môi trường và cách đo

### 2.1. Môi trường

| Hạng mục | Giá trị |
|---|---|
| OS | Windows 10.0.19045 (22H2, x64) - **ngoài ma trận OS chính thức của .NET 10** |
| SDK dùng để đo | .NET SDK 10.0.401; máy còn cài 10.0.202 |
| Runtime | .NET 10.0.12 (Desktop + NETCore); .NET 9.0.15 vẫn còn |
| EF Core tool | `dotnet-ef` 10.0.14 (công cụ local); máy còn global 9.0.14 |
| WiX | 4.0.6+73c89738, extension `WixToolset.UI.wixext/4.0.6` |
| Thời gian chạy | 2026-10-29 20:03-20:18 +07:00 |
| Work root | `%TEMP%\edufocus-net10-spike\20261029-2015` (bản sao ngoài repo) |
| Quyền | Không elevated; không cài MSI, không đăng ký task/service |

Bản sao tạm được tạo từ working tree sạch tại commit `1f52a84`. Không có file nào trong `src/` của repo bị sửa.

### 2.2. Lệnh đã chạy

```powershell
# Trong bản sao tạm
dotnet restore DontBeLazy.slnx
dotnet build -c Release --no-incremental
dotnet test -c Release --no-build

# Publish framework-dependent
dotnet publish src/DontBeLazy.WPF -c Release -r win-x64 --self-contained false -o publish

# Publish self-contained single-file (đúng đường CI release.yml)
dotnet publish src/DontBeLazy.WPF -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true -o publish-sc

# EF Core 10
tools/dotnet-ef.exe database update --project src/DontBeLazy.SqliteDataAccess `
  --startup-project src/DontBeLazy.WPF --configuration Release --no-build
tools/dotnet-ef.exe migrations list --project src/DontBeLazy.SqliteDataAccess `
  --startup-project src/DontBeLazy.WPF --configuration Release --no-build

# WiX 4
wix/wix.exe build installer/Product.wxs -arch x64 -d Version=1.0.0 `
  -d PublishDir=publish-sc/ -ext WixToolset.UI.wixext -o DontBeLazy-net10-sc.msi
```

### 2.3. Kết quả

| Bước | Kết quả | Số liệu |
|---|---|---|
| Restore | pass | Không lỗi NU1605 sau khi đồng bộ package 10.0.14 |
| Build Release | pass | 0 error, 9 warning (3 nullable trong test, 4 CA1416, 2 WFO0003) |
| Unit test | pass | 57/57, 144 ms |
| Publish framework-dependent | pass | `DontBeLazy.exe` 259.072 byte; thư mục 21 MB |
| Publish self-contained single-file | pass | `DontBeLazy.exe` 82.726.957 byte (78,9 MiB); thư mục 80 MB |
| EF Core 10 migration | pass | 4/4 migration áp dụng; DB 98.304 byte; `migrations list` không còn `(Pending)` |
| WiX MSI (self-contained) | pass | `DontBeLazy-net10-sc.msi` 76.349.440 byte (72,8 MiB); 4 warning WIX5437 |
| WiX MSI (framework-dependent) | pass | 589.824 byte; không có launch condition cho .NET Runtime |

## 3. Ma trận tương thích

### 3.1. SDK và target framework

| Thành phần | Hiện tại | Spike .NET 10 | Kết quả | Ghi chú |
|---|---|---|---|---|
| Domain, Ports, UseCases, Infrastructure, SqliteDataAccess | `net9.0` | `net10.0` | pass | Không đổi API |
| Infrastructure.Attention | `net9.0-windows` | `net10.0-windows` | pass | OpenCvSharp/ONNX giữ nguyên |
| WPF | `net9.0-windows` | `net10.0-windows` | pass | `UseWPF` + `UseWindowsForms` |
| Domain.Tests | `net10.0` (đã có) | `net10.0` | pass | Đã đúng từ trước |
| SDK | CI đang cài 9.0.x | 10.0.401 | pass | Phải đổi CI sang 10.0.x |

### 3.2. Package

| Package | Hiện tại | Đề xuất Phase 1 | Kết quả | Ghi chú |
|---|---|---|---|---|
| Microsoft.EntityFrameworkCore / Sqlite / Design | 9.0.2 (SqliteDataAccess), 9.0.4 (WPF) | 10.0.14 | pass | Migration hiện có chạy được |
| Microsoft.Extensions.DependencyInjection | 10.0.6 | 10.0.14 | bắt buộc | EF Core 10.0.14 yêu cầu >= 10.0.14; nếu không sẽ NU1605 |
| Microsoft.Extensions.DependencyInjection.Abstractions | 10.0.6 | 10.0.14 | bắt buộc | Cùng lý do |
| Microsoft.Extensions.Http | 10.0.6 | 10.0.14 | pass | Đồng bộ |
| System.Management | 10.0.6 | 10.0.14 | pass | Đồng bộ |
| Microsoft.ML.OnnxRuntime | 1.31.0 | giữ nguyên | pass | Chạy trên `net10.0-windows` |
| OpenCvSharp4 + runtime.win | 4.13.0.20260627 | giữ nguyên | pass | Không thấy điểm vỡ |
| CommunityToolkit.Mvvm | 8.4.2 | giữ nguyên | pass | |
| MaterialDesignColors / Themes | 5.3.1 | giữ nguyên | pass | |
| MdXaml | 1.27.0 | giữ nguyên | pass | |
| xUnit, FluentAssertions, coverlet, Test SDK | 2.9.3 / 8.9.0 / 6.0.4 / 17.14.1 | giữ nguyên | pass | 57/57 test |
| WiX Toolset + UI extension | 4.0.6 | giữ nguyên | pass | 4 warning WIX5437 |

### 3.3. Cảnh báo và phân loại

| Mã | Nội dung | Mức | Xử lý Phase 1 |
|---|---|---|---|
| NU1605 | EF Core 10.0.14 yêu cầu Microsoft.Extensions.DependencyInjection >= 10.0.14 nhưng direct ref là 10.0.6 | **Blocking** | Đồng bộ toàn bộ package 10.0.14 |
| WFO0003 | `app.manifest` còn cấu hình High DPI; WinForms analyzer yêu cầu chuyển sang API/property | Warning | Chuyển cấu hình DPI hoặc chấp nhận có kiểm chứng |
| IL3000 | `Assembly.Location` luôn rỗng trong single-file; `App.xaml.cs:203` dùng làm fallback auto-start | Warning | Chỉ dùng `Environment.ProcessPath`/`AppContext.BaseDirectory` |
| CA1416 | 4 call site Windows-only trong Infrastructure khi target `net10.0` | Warning | Giữ `net10.0` + annotation, hoặc chuyển Infrastructure sang `net10.0-windows` |
| CS8604/CS8625 | 3 cảnh báo nullable trong test | Warning | Không chặn; dọn khi sửa test |
| WIX5437 | Khai báo thủ công các standard directory | Warning | Không chặn; dọn khi chỉnh installer |

## 4. Thay đổi bắt buộc cho Phase 1

### 4.1. Danh sách thay đổi

| # | File/khu vực | Thay đổi | Lý do |
|---|---|---|---|
| 1 | `src/*/*.csproj` | Đổi 5 project sang `net10.0`, 2 project Windows sang `net10.0-windows` | .NET 9 hết hỗ trợ 10/11/2026 |
| 2 | Các `.csproj` có package Microsoft | Đồng bộ `Microsoft.Extensions.*`, `System.Management`, EF Core lên 10.0.14 | Tránh NU1605; EF Core 10 yêu cầu DI >= 10.0.14 |
| 3 | `.github/workflows/ci.yml`, `release.yml` | `dotnet-version: "10.0.x"` | CI đang cài SDK 9 trong khi test target `net10.0` |
| 4 | Tool manifest/CI | Pin `dotnet-ef` 10.0.14 và chạy `dotnet tool restore` | Global 9.0.14 đã cảnh báo và có thể dùng sai migration |
| 5 | `src/DontBeLazy.WPF/App.xaml.cs` | Bỏ fallback `Assembly.Location` ở auto-start | IL3000; single-file trả về chuỗi rỗng |
| 6 | `src/DontBeLazy.WPF/app.manifest` | Chuyển High DPI sang `ApplicationHighDpiMode`/API hoặc xác nhận hành vi | WFO0003; tránh regression DPI |
| 7 | `installer/Product.wxs` / release | Quyết định self-contained hay framework-dependent; nếu framework-dependent phải có launch condition + Desktop Runtime 10 | MSI hiện không kiểm tra runtime |
| 8 | `README.md`, `docs/ba_document.md` | Cập nhật trạng thái sau khi nâng cấp | Đang ghi code vẫn `net9.0` |
| 9 | Migration/backup | Backup DB trước khi chạy migration; không hỗ trợ downgrade | `docs/database_schema.md` §17 |
| 10 | OS requirement | Ghi rõ Windows 11 23H2+ là chính; Windows 10 22H2 best-effort | .NET 10 không hỗ trợ chính thức 22H2 |

### 4.2. Thứ tự thực hiện đề xuất

1. Tạo branch Phase 1; thêm `global.json` pin SDK 10.0.4xx nếu cần reproducibility.
2. Đổi TFM toàn bộ project; đồng bộ package 10.0.14; restore/build.
3. Chạy unit test; thêm smoke test mở DB và chạy migration trên bản copy.
4. Backup DB thật; chạy `dotnet ef database update` bằng tool 10.0.14; kiểm tra 4 migration.
5. Publish self-contained single-file; build MSI; cài thử trên máy sạch/Windows 11 23H2+.
6. Sửa WFO0003/IL3000; kiểm tra DPI, auto-start, single-file path.
7. Cập nhật CI/release sang SDK 10; pin EF tool; chạy pipeline.
8. Cập nhật tài liệu; đóng issue Phase 1.

### 4.3. Rủi ro và rollback

| Rủi ro | Ảnh hưởng | Giảm thiểu | Rollback |
|---|---|---|---|
| NU1605 khi nâng EF Core | Không build được | Đồng bộ package 10.0.14 | Revert commit; khôi phục phiên bản package cũ |
| Migration thất bại trên DB thật | Mất/không mở được dữ liệu | Backup trước; chạy thử trên bản copy; giữ DB cũ nếu lỗi | Dừng app, phục hồi file backup; không downgrade schema |
| Single-file thiếu file/đường dẫn | Auto-start hoặc asset lỗi | Sửa IL3000; test bản publish thật | Quay lại framework-dependent publish |
| MSI thiếu Desktop Runtime | App không khởi động trên máy đích | Self-contained hoặc launch condition + bootstrapper | Phát hành lại bản self-contained |
| DPI regression | UI mờ/lệch | Kiểm tra manifest/API trên Win11 | Giữ manifest cũ tạm thời |
| OS không được hỗ trợ | Lỗi runtime trên Windows 10 22H2 | Ghi rõ best-effort; test trên Win11 23H2+ | Phát hành hotfix hoặc giới hạn OS |
| Rollback sau khi người dùng đã migrate | DB có thể đã đổi | Không thêm migration mới trong Phase 1; backup | Cài lại bản cũ + restore DB; không hạ schema |

**Nguyên tắc rollback:** revert commit Phase 1 và cài lại MSI phiên bản trước; DB chỉ phục hồi từ backup, không hạ cấp schema. Giữ artifact .NET 9 đã ký và tag release cũ cho đến khi bản .NET 10 ổn định.

## 5. Data map

### 5.1. Nguyên tắc

- **Local-first:** không gửi dữ liệu học tập lên cloud mặc định; SQLite trên máy.
- **Tối thiểu hóa:** không lưu frame, landmark, embedding, audio, keystroke, clipboard hoặc nội dung trang.
- **Phân tách:** dữ liệu chi tiết (sample/event) có retention ngắn hơn session summary.
- **Truy cập:** phụ huynh qua UI có PIN; học sinh không vào Parent Mode; publisher không truy cập dữ liệu local.
- **Xóa:** retention job + xóa theo yêu cầu + xóa khi reset ứng dụng; hành vi uninstall phải được quy định rõ.

### 5.2. Bản đồ dữ liệu mục tiêu (theo thiết kế `docs/database_schema.md`)

| Nhóm dữ liệu | Chủ thể | Mục đích | Nơi lưu | Thời hạn | Truy cập | Cách xóa | Mức nhạy cảm |
|---|---|---|---|---|---|---|---|
| ParentCredentials (PIN/recovery hash, salt, lockout) | Phụ huynh | Xác thực, thoát hợp lệ | `%ProgramData%\EduFocus` (DB) | Đến khi reset/gỡ | Phụ huynh qua UI; SYSTEM | Reset credential/xóa DB | Cao (secret xác thực) |
| AppSettings (protection, monitoring, schedule, camera, retention, theme) | Phụ huynh | Cấu hình ứng dụng | DB | Đến khi reset | Phụ huynh; SYSTEM | Reset/xóa DB | Thấp |
| BlockRules, ScheduleWindows | Phụ huynh | Chính sách chặn/lịch học | DB | Đến khi xóa rule | Phụ huynh | Xóa rule/reset | Thấp (có thể lộ quy tắc gia đình) |
| StudySessions (thời gian, điểm attention, confidence, blocked count) | Học sinh | Báo cáo phiên học | DB | 365 ngày | Phụ huynh; SYSTEM | Retention job/xóa theo yêu cầu | Trung bình |
| ActivitySamples (app, process, window title, URL/domain, idle) | Học sinh | Timeline/top app | DB | 30 ngày | Phụ huynh; không có publisher | Retention job/xóa theo yêu cầu | **Cao** (hành vi, tiêu đề/URL) |
| AttentionEvents (state, timestamp, confidence, app/domain) | Học sinh | Tóm tắt tập trung | DB | 90 ngày | Phụ huynh | Retention job/xóa | **Cao** (dữ liệu suy ra từ camera, trẻ em) |
| BlockedAttempts (domain/app, URL, process, window title) | Học sinh | Báo cáo chặn best-effort | DB | 90 ngày | Phụ huynh | Retention job/xóa | Trung bình - cao |
| AuditLogs (actor, event, mô tả đã redact) | Phụ huynh/học sinh | Truy vết thay đổi bảo vệ/PIN/xóa | DB | 365 ngày | Phụ huynh; SYSTEM | Retention job/xóa | Trung bình |
| Watchdog state/heartbeat (PID, timestamp, protection) | Thiết bị | Phát hiện treo/restart | `%ProgramData%\EduFocus` | Tạm thời | App/SYSTEM | Ghi đè/xóa khi gỡ | Thấp |
| Log file | Thiết bị/người dùng | Chẩn đoán lỗi | `%ProgramData%\EduFocus\logs` (thiết kế) | 7-14 ngày | Admin/phụ huynh | Rotation/xóa | Trung bình nếu lộ dữ liệu trong lỗi |
| Backup DB | Học sinh/phụ huynh | Phục hồi | `%ProgramData%\EduFocus\backups` | Tối đa 3 bản | Phụ huynh/admin | Xóa backup | **Cao** |
| Consent/notice record (nếu triển khai) | Phụ huynh/học sinh | Bằng chứng đồng thuận | DB/local | Theo vòng đời tài khoản + thời hạn luật định | Phụ huynh | Xóa/reset theo quy trình | Cao (pháp lý) |
| Update-check metadata (version, OS, IP) | Thiết bị | Kiểm tra cập nhật | GitHub API (ngoài máy) | Log phía server/vendor | Publisher/GitHub | Tắt kiểm tra; yêu cầu xóa theo chính sách | Trung bình (IP là dữ liệu cá nhân) |
| Hosts block entries (domain) | Thiết bị | Thực thi chặn | `%SystemRoot%\System32\drivers\etc\hosts` | Khi protection ON | SYSTEM/admin | Restore khi tắt app/gỡ | Thấp (lộ danh sách chặn) |

### 5.3. Luồng dữ liệu chính

- **Camera:** frame được đọc vào bộ nhớ, suy luận attention cục bộ, sau đó hủy; không ghi frame/landmark/embedding, không gửi mạng. Chỉ trạng thái thay đổi và confidence được ghi thành `AttentionEvents`.
- **Activity:** foreground app/window title/URL lấy qua Win32/UI Automation; URL được chuẩn hóa về domain, bỏ query/fragment; window title bị cắt ngắn; không lưu nội dung trang/clipboard/keystroke.
- **Chặn web:** hosts file chỉ chứa domain; block page/log là best-effort; HTTPS không tạo log truy cập.
- **Lưu trữ:** SQLite + backup; retention job hằng ngày; ACL `%ProgramData%\EduFocus` cho Administrators/SYSTEM.
- **Mạng:** mặc định không có network call chứa dữ liệu học tập. Ngoại lệ phải được opt-in và công bố: kiểm tra cập nhật (version/OS/IP).

### 5.4. Hiện trạng code legacy (cần dọn ở Phase 1/2)

| Thành phần | Dữ liệu | Vị trí hiện tại | Ghi chú |
|---|---|---|---|
| `dontbelazy.db` | FocusTask, Profile/ProfileEntry, SessionHistory/SessionProfileSnapshot, SystemSettings, Quote | `%LocalAppData%\DontBeLazy\dontbelazy.db` | Khác thiết kế `%ProgramData%\EduFocus`; ACL chưa tách |
| SystemSettings | `GeminiApiKey` lưu plaintext trong DB | DB | Không đạt yêu cầu bảo mật; cần DPAPI/Credential Manager nếu giữ AI |
| GeminiAiServices | Prompt/task name/task dump gửi tới Google; API key trong query string | `https://generativelanguage.googleapis.com` | **Chuyển dữ liệu ra nước ngoài**; tài liệu kiến trúc đã đánh dấu xóa |
| UpdaterService | Kiểm tra GitHub Releases tự động khi khởi động; User-Agent chứa version; IP lộ với GitHub | `api.github.com` | Thiết kế yêu cầu opt-in; cần sửa hoặc tắt mặc định |
| FileLogger | Log ứng dụng, có thể chứa thông tin lỗi | `%AppData%\DontBeLazy\logs` | Thiết kế chuyển vào `%ProgramData%`; 7 ngày |
| WindowsStrictEngine | Domain chặn trong hosts | `%SystemRoot%\System32\drivers\etc\hosts` | Không phải dữ liệu cá nhân trực tiếp nhưng lộ rule |
| MSI | Chỉ đóng gói `DontBeLazy.exe` + `app.ico` | Program Files | Gỡ cài đặt không xóa DB/log trong LocalAppData/AppData |

### 5.5. Khoảng trống so với thiết kế

- Chưa có privacy notice, consent flow, age verification hoặc lưu bằng chứng đồng thuận.
- Chưa có tính năng export/delete-all hoàn chỉnh; gỡ cài đặt để lại dữ liệu.
- Chưa có ACL `%ProgramData%` trong code hiện tại (DB vẫn ở LocalAppData).
- API key AI lưu plaintext và truyền trong URL.
- Update check chạy mặc định, chưa opt-in.
- Chưa có integration test cho retention/xóa dữ liệu.
- Chưa có danh sách vendor/DPA đầy đủ (GitHub, Google nếu giữ AI, code signing/hosting).
- Chưa hoàn thiện DPIA và hồ sơ thông báo trước phát hành.

## 6. DPIA sơ bộ

### 6.1. Vai trò và phạm vi

- **Chủ thể dữ liệu:** học sinh (trẻ em) và phụ huynh/người giám hộ.
- **Đơn vị phát hành:** xác định vai trò pháp lý (bên kiểm soát/bên xử lý) trước khi nộp hồ sơ; app local-first không truy cập dữ liệu nhưng vẫn là công cụ xử lý do publisher cung cấp.
- **Phạm vi xử lý:** cài đặt, cấu hình bảo vệ, chặn web/app, phiên học, activity sample, attention event, blocked attempt, audit, log, update check; không bao gồm dữ liệu ngoài app.
- **Nguyên tắc:** mục đích rõ ràng, tối thiểu hóa, lưu trữ hạn chế, minh bạch, đồng thuận, trách nhiệm giải trình.

### 6.2. Căn cứ pháp lý

| Văn bản | Điểm áp dụng | Tác động tới EduFocus |
|---|---|---|
| Luật Bảo vệ dữ liệu cá nhân 91/2025/QH15 (hiệu lực 01/01/2026) | Quyền chủ thể, đồng thuận, dữ liệu nhạy cảm, trẻ em, chuyển dữ liệu ra nước ngoài, đánh giá tác động | Phải có DPIA, privacy notice, consent; đặc biệt với dữ liệu camera/attention của trẻ |
| Nghị định 356/2025/NĐ-CP (hiệu lực 01/01/2026) | Quy định chi tiết thi hành; hồ sơ/báo cáo DPIA và thông báo | Hoàn thiện hồ sơ theo mẫu hiện hành (tham chiếu Mẫu số 10 cho báo cáo DPIA; Mẫu 02a/02b cho thông báo) |
| Luật 91/2025/QH15, Điều 21 | Đánh giá tác động và thời hạn gửi hồ sơ | Gửi trong **60 ngày** kể từ khi bắt đầu xử lý; không thay thế việc hoàn thiện trước phát hành |
| Quy định về trẻ em | Trẻ từ đủ 7 tuổi | Cần sự đồng ý của trẻ **và** cha/mẹ/người giám hộ; xác minh tuổi; bảo đảm lợi ích tốt nhất của trẻ |
| Quy định về dữ liệu nhạy cảm | Sinh trắc học, sức khỏe, đời sống riêng tư | Camera/face/gaze và suy luận attention phải được đánh giá là rủi ro cao; xác nhận phân loại với luật sư |

**Lưu ý:** số điều/mẫu có thể được sửa đổi bởi văn bản hướng dẫn; đối chiếu bản hiện hành trước khi nộp. Phần này không phải tư vấn pháp lý.

### 6.3. Checklist DPIA

| Hạng mục | Trạng thái | Bằng chứng/khoảng trống | Hành động trước phát hành |
|---|---|---|---|
| Xác định vai trò (kiểm soát/xử lý/đồng kiểm soát) | Chưa | App local, publisher không truy cập dữ liệu | Luật sư/DPO xác nhận; ghi trong DPIA |
| Mục đích và phạm vi xử lý | Đã có thiết kế | `docs/ba_document.md` §10 | Rà soát khi thêm tính năng |
| Cơ sở pháp lý và đồng thuận | Một phần | Chưa có consent flow/age verification | Thiết kế onboarding: đồng thuận phụ huynh + trẻ >= 7; lưu phiên bản consent; cho rút |
| Minh bạch/privacy notice | Chưa | Chưa có notice trong app | Notice tiếng Việt/Anh, ngôn ngữ đơn giản cho trẻ; nêu rõ camera/attention, retention, quyền |
| Tối thiểu hóa dữ liệu | Đã có thiết kế | Không lưu frame/audio/clipboard; URL strip | Kiểm chứng bằng code review/test |
| Giới hạn mục đích | Đã có thiết kế | Không xếp hạng/trừng phạt/cảm xúc | Rà soát UI/report |
| Retention và xóa | Đã có schema | 30/90/365/7-14; chưa có test | Retention job + test; xóa theo yêu cầu; quy định uninstall |
| Quyền chủ thể | Một phần | Có xóa lịch sử; chưa export/delete-all | Bổ sung export/delete-all; quy trình phản hồi |
| Bảo mật | Một phần | ACL chưa áp dụng; API key plaintext | ACL `%ProgramData%`; DPAPI; ký số; least privilege |
| Dữ liệu nhạy cảm/trẻ em | Cần pháp lý | Camera/attention là rủi ro cao | DPIA chuyên sâu; biện pháp giảm thiểu; tham vấn |
| Chuyển dữ liệu ra nước ngoài | Chưa | Update GitHub; legacy Gemini | Opt-in; không gửi dữ liệu trẻ; đánh giá vendor/DPA |
| Bên thứ ba | Chưa | GitHub, Google (legacy), hosting/signing | Danh sách vendor + DPA + đánh giá |
| Vi phạm dữ liệu | Chưa | Chưa có quy trình | Incident response; thông báo theo thời hạn luật định |
| Hồ sơ DPIA | Chưa | Chưa lập/nộp | Lập báo cáo (Mẫu 10) + thông báo (Mẫu 02a/02b); nộp trong 60 ngày |
| Đánh giá lại | Chưa | Chưa có trigger | Cập nhật khi thêm dữ liệu/tính năng/vendor/schema |
| Đạo đức | Đã có thiết kế | Không giám sát bí mật; công khai với học sinh | Kiểm tra thông điệp onboarding |

### 6.4. Dữ liệu nhạy cảm và biện pháp giảm thiểu

| Dữ liệu | Rủi ro | Biện pháp |
|---|---|---|
| Frame/face/gaze từ camera | Nhận diện/suy luận sinh trắc học; rò rỉ hoặc lạm dụng | Xử lý local, không lưu frame/landmark/embedding, không gửi mạng; đèn camera; tạm dừng; DPIA chuyên sâu |
| Attention/state/confidence | Suy luận hành vi, ảnh hưởng tới trẻ | Chỉ hiển thị confidence/coverage; không dùng xếp hạng/trừng phạt; retention 90 ngày |
| Window title/URL/domain | Lộ sở thích, liên lạc, nội dung | Bỏ query/fragment; cắt ngắn; không lưu nội dung; retention 30 ngày; chỉ phụ huynh |
| PIN/recovery hash | Chiếm tài khoản | PBKDF2 >= 600.000 vòng; lockout; không ghi log/audit |
| API key/Gemini | Lộ secret, chuyển dữ liệu | Xóa tính năng legacy hoặc DPAPI + header; không gửi dữ liệu trẻ; opt-in |
| IP/version khi update check | Nhận diện thiết bị | Opt-in; chỉ version/OS; công bố IP; không dùng định danh |
| Backup DB | Bản sao dữ liệu nhạy cảm | ACL; giới hạn 3 bản; mã hóa post-MVP; xóa khi reset |
| Tài khoản Windows dùng chung | Học sinh đọc DB/log | Khuyến nghị tài khoản riêng; ACL; ghi rõ PIN chỉ bảo vệ UI |

### 6.5. Câu hỏi mở cần luật sư/DPO xác nhận

1. Xử lý frame/gaze local để suy luận attention có được coi là xử lý dữ liệu sinh trắc học không, và cơ sở pháp lý nào áp dụng?
2. Ai là bên kiểm soát dữ liệu khi phần mềm cài local và publisher không truy cập dữ liệu?
3. Hồ sơ DPIA do publisher lập hay từng phụ huynh lập; có cần nộp thay mặt người dùng không?
4. Phương thức xác minh tuổi và lưu bằng chứng đồng thuận của trẻ/phụ huynh.
5. Cơ sở pháp lý cho update check và chuyển IP/version ra nước ngoài.
6. Thời hạn lưu audit/consent và xung đột với quyền xóa.
7. Có cần đánh giá riêng cho dữ liệu sức khỏe/tâm lý suy ra từ attention không.
8. Nghĩa vụ khi gỡ cài đặt: xóa ngay hay giữ theo retention; backup xử lý thế nào.

### 6.6. Miễn trừ

Tài liệu này là đánh giá kỹ thuật sơ bộ, **không phải tư vấn pháp lý**. Trước khi phát hành thương mại cần luật sư/DPO rà soát, hoàn thiện hồ sơ DPIA và thông báo theo mẫu hiện hành.

## 7. Artifact

- Bản sao thử nghiệm: `%TEMP%\edufocus-net10-spike\20261029-2015` (ngoài repo; có thể xóa).
- Publish framework-dependent: `publish/`; self-contained single-file: `publish-sc/`.
- MSI: `DontBeLazy-net10-sc.msi` (self-contained) và `DontBeLazy-net10-check.msi` (framework-dependent).
- DB smoke: `src/DontBeLazy.SqliteDataAccess/dontbelazy_dev.db`.
- Không có artifact production nào được thêm vào repo ngoài tài liệu này.

## 8. Giới hạn của spike

- Máy đo là Windows 10 22H2, **không** nằm trong ma trận OS chính thức của .NET 10; chưa đo trên Windows 11 23H2+.
- Chưa chạy UI thật/PIN/camera trên bản publish; chưa test UAC/elevation, single-file auto-start, DPI.
- Chưa cài MSI, chưa test upgrade từ bản .NET 9, chưa test uninstall.
- Chưa chạy migration trên DB production có dữ liệu lớn; DB smoke chỉ 98 KB.
- Chưa có integration test repository/retention; test hiện tại chủ yếu là domain.
- Chưa đo hiệu năng/CPU/RAM của .NET 10 so với .NET 9.
- Legal research chỉ ở mức sơ bộ; số điều/mẫu cần đối chiếu văn bản hiện hành.

## 9. Tài liệu tham khảo

- [`docs/ba_document.md`](../ba_document.md) §10.2 Pháp lý, §11 Công nghệ, §13 Rủi ro
- [`docs/database_schema.md`](../database_schema.md) §14 Retention, §16 Bảo mật dữ liệu, §17 Migration
- [`SECURITY.md`](../../SECURITY.md)
- [.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)
- [.NET 10 supported OS](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md)
- Luật Bảo vệ dữ liệu cá nhân 91/2025/QH15; Nghị định 356/2025/NĐ-CP
