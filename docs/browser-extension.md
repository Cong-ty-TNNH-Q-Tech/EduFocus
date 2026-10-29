# EduFocus — Browser extension (Chrome/Edge)

Tiện ích Manifest V3 chặn chính xác **URL và đường dẫn** (ví dụ
`https://www.youtube.com/shorts/`) mà hosts file không làm được. Tiện ích chỉ hoạt động khi
EduFocus đang bảo vệ; khi EduFocus không chạy, mọi điều hướng được cho qua (fail-open).

Tham chiếu: [system_architecture.md](./system_architecture.md),
[privacy-notice.md](./privacy-notice.md), [installer.md](./installer.md),
[security-e2e-checklist.md](./security-e2e-checklist.md).

---

## 1. Thành phần

| Thành phần | Vị trí | Vai trò |
|---|---|---|
| Extension MV3 | `browser-extension/` (ZIP phát hành) | Quan sát điều hướng, so khớp rule, chuyển tab sang trang chặn |
| Native messaging host | `%ProgramFiles%\EduFocus\native-host\EduFocus.NativeHost.exe` | Cầu nối stdio của trình duyệt ↔ named pipe của EduFocus |
| Named pipe | `\\.\pipe\EduFocus.BrowserBridge.v1` | Snapshot rule và báo cáo URL bị chặn, chỉ trong máy |
| Manifest đăng ký | `%ProgramFiles%\EduFocus\native-host\com.edufocus.native_host.json` | `allowed_origins` chỉ chứa extension ID cố định |

Luồng dữ liệu:

1. EduFocus bật bảo vệ → gửi snapshot rule hiệu lực (domain/URL, exact/suffix/contains,
   allow/block, thời hạn) qua pipe.
2. Extension nhận snapshot, giữ trong RAM của service worker.
3. Trước mỗi điều hướng cấp tab (`onBeforeNavigate`) và cập nhật history của SPA
   (`onHistoryStateUpdated`), extension làm sạch URL rồi so khớp.
4. Nếu bị chặn, tab được chuyển tới `blocked.html` đóng gói sẵn; trang chặn **không đọc, không
   hiển thị URL**. Extension gửi lại URL đã làm sạch cho EduFocus (tối đa 1 lần/5 giây/URL) để
   ghi `BlockedAttempt`.
5. EduFocus kiểm tra lại URL với rule đang hiệu lực trước khi ghi nhận, nên báo cáo giả mạo từ
   trình duyệt không thể tạo blocked attempt.

### Fail-open và tái kết nối

- Không có EduFocus/native host → extension không chặn gì; popup hiển thị **Chưa kết nối EduFocus**.
- Khi host mất pipe, host gửi `{"type":"unavailable"}` một lần; extension xóa snapshot và cho qua.
- Host thử kết nối lại mỗi 2 giây khi trình duyệt còn mở, nên bật EduFocus sẽ có hiệu lực trong
  vài giây. Khi service worker bị trình duyệt tạm ngưng, alarm 1 phút đánh thức và kết nối lại.
- Không có rule nào bị “kẹt” sau khi EduFocus tắt: quyết định chặn chỉ dựa trên snapshot đang có.

---

## 2. Quyền và giới hạn quyền

Manifest yêu cầu đúng các quyền sau:

| Quyền | Lý do | Không dùng để |
|---|---|---|
| `nativeMessaging` | Kết nối host `com.edufocus.native_host` | Đọc/ghi file, chạy lệnh tuỳ ý |
| `webNavigation` | Nhận sự kiện điều hướng cấp tab và SPA history | Đọc nội dung trang |
| `alarms` | Đánh thức service worker để kết nối lại | Theo dõi nền liên tục |
| `host_permissions` `http://*/*`, `https://*/*` | Lấy URL điều hướng để so khớp rule | Đọc DOM, cookie hay dữ liệu biểu mẫu |

Không yêu cầu `tabs`, `history`, `cookies`, `storage`, `scripting`, `webRequest`; không có content
script, không tải mã từ xa, không dùng `innerHTML`/`eval`, không ghi URL vào storage. Trang chặn
và popup chỉ dùng `textContent` với dữ liệu tĩnh.

Giới hạn có chủ đích:

- Chỉ chặn **điều hướng cấp tab** (main frame), không chặn iframe, subresource hay tải file nền.
- Chỉ xử lý `http`/`https`; URL dài hơn 2048 ký tự hoặc không hợp lệ bị bỏ qua.
- Query string và fragment bị loại trước khi so khớp, nên rule không thể nhắm vào tham số query.
- Điều hướng do server redirect được đánh giá lại ở từng bước; nếu bước cuối khớp rule, tab bị chặn.
- Học sinh có quyền quản trị hoặc tắt extension bằng DevTools có thể vô hiệu hóa; muốn chống tắt
  cần chính sách quản trị thiết bị (ngoài phạm vi MVP).
- Không thay thế hosts/DNS filtering; hai lớp bổ sung cho nhau.

---

## 3. Ngữ nghĩa rule

Rule được cấu hình trong Parent Mode và gửi xuống extension; cùng mã so khớp với
`BlockRule.MatchesUrl` của domain:

| Loại | Match | Ví dụ | Khớp |
|---|---|---|---|
| Domain | Exact | `chatgpt.com` | chỉ host chính xác |
| Domain | Suffix | `openai.com` | `openai.com`, `chat.openai.com` |
| Domain | Contains | `tiktok.com` | host hoặc nhãn tên miền chứa chuỗi |
| URL | Exact | `https://claude.ai/chat/` | URL đã làm sạch, bỏ qua hoa/thường, coi `/chat` = `/chat/` |
| URL | Contains | `https://www.youtube.com/shorts/` | đường dẫn chứa chuỗi, không khớp query |
| URL | Suffix | `https://example.com/files/` | toàn bộ URL đã làm sạch kết thúc bằng chuỗi |

- Rule `allow` luôn thắng `block`; domain được bảo vệ (`meet.google.com`,
  `classroom.google.com`, `accounts.google.com`, `login.microsoftonline.com`, …) luôn được cho qua.
- Rule hết hạn hoặc bị tắt không được gửi xuống extension.
- Đổi rule khi đang bảo vệ được đẩy xuống ngay qua pipe; không cần khởi động lại trình duyệt.
- URL trong báo cáo bị chặn đã bỏ query/fragment/thông tin đăng nhập; không lưu nội dung trang.

---

## 4. Cài đặt

### 4.1. Qua MSI (khuyến nghị)

MSI cài native host và đăng ký native messaging cho Chrome/Edge (HKLM, cả registry 32-bit và
64-bit). Sau khi cài:

1. Cài extension từ Chrome Web Store (khi có listing) **hoặc** giải nén
   `EduFocus-<version>-browser-extension.zip` và chọn **Load unpacked** trong `chrome://extensions`
   (bật Developer mode). Với triển khai có quản trị, dùng `ExtensionInstallForcelist`/
   `ExtensionSettings` của Chrome/Edge.
2. Mở EduFocus, hoàn tất onboarding và bật bảo vệ.
3. Bấm icon EduFocus trên thanh công cụ: trạng thái phải là **Đang bảo vệ**.
4. Kiểm tra nhanh đăng ký:

```powershell
reg query "HKLM\Software\Google\Chrome\NativeMessagingHosts\com.edufocus.native_host"
reg query "HKLM\Software\Microsoft\Edge\NativeMessagingHosts\com.edufocus.native_host"
```

Giá trị mặc định của key phải trỏ tới
`%ProgramFiles%\EduFocus\native-host\com.edufocus.native_host.json`.

### 4.2. Bản portable hoặc chạy từ source

Đăng ký thủ công cho tài khoản hiện tại (không cần quyền admin):

```powershell
./scripts/register-browser-extension.ps1 `
  -HostPath ".\publish\native-host\EduFocus.NativeHost.exe" `
  -Scope User
```

Hoặc đăng ký cho toàn máy trong PowerShell elevated:

```powershell
./scripts/register-browser-extension.ps1 `
  -HostPath "C:\Program Files\EduFocus\native-host\EduFocus.NativeHost.exe" `
  -Scope Machine
```

Sau khi đăng ký, tải lại extension trong `chrome://extensions`. Gỡ đăng ký:

```powershell
./scripts/unregister-browser-extension.ps1 -Scope User
./scripts/unregister-browser-extension.ps1 -Scope Machine   # elevated
```

Host cũng hỗ trợ đăng ký trực tiếp (MSI dùng lệnh này):

```powershell
EduFocus.NativeHost.exe --register --scope machine --manifest-dir "C:\Program Files\EduFocus\native-host"
EduFocus.NativeHost.exe --unregister --scope machine --manifest-dir "C:\Program Files\EduFocus\native-host"
```

---

## 5. Gỡ cài đặt

1. Gỡ extension trong `chrome://extensions` (hoặc gỡ theo chính sách quản trị).
2. Gỡ EduFocus bằng MSI: custom action `--unregister --scope machine` xóa registry key và manifest
   trước khi file bị xóa. Dữ liệu học tập vẫn giữ theo lựa chọn trong hộp thoại gỡ.
3. Bản portable/source: chạy `scripts/unregister-browser-extension.ps1 -Scope User` (hoặc
   `-Scope Machine`) và xóa thư mục publish.

Extension không lưu lịch sử duyệt web, nên không có dữ liệu trình duyệt nào cần xóa; blocked
attempts đã ghi nằm trong DB của EduFocus và tuân theo retention/xóa dữ liệu của ứng dụng.

---

## 6. Đóng gói, ký và phát hành

```powershell
# ZIP để tải lên Chrome Web Store hoặc giải nén để Load unpacked
./scripts/pack-browser-extension.ps1

# CRX tự ký (cần Chrome/Edge và private key để NGOÀI repo)
./scripts/pack-browser-extension.ps1 -PemPath D:\secure\edufocus-extension.pem
```

- Script chỉ đóng gói file runtime (`manifest.json`, service worker, matcher, trang chặn, popup),
  loại bỏ `tests/`, `native-host/` và `package.json`.
- Khóa riêng tư không bao giờ được chép vào repo/artifact; script từ chối nếu `-PemPath` nằm trong
  repository. Khóa công khai trong `manifest.json` giữ **extension ID cố định**
  `lcanbfnejggilcaeinmahlcfimecmjmp`; đổi khóa phải cập nhật đồng thời native host manifest,
  registry/script và test.
- Chrome Web Store là kênh phân phối chính (Google ký gói). CRX tự ký chỉ dùng cho self-hosting
  kèm enterprise policy.
- Workflow release đóng gói ZIP với version số của bản phát hành (ghi đè `version` trong manifest
  của gói), ký native host cùng `EduFocus.exe`, đưa ZIP vào `SHA256SUMS.txt` và release assets.
  Khi phát hành extension độc lập, tăng `version` trong `browser-extension/manifest.json` hoặc
  truyền `-Version` cho script.
- Kiểm thử extension: `node --test browser-extension/tests` (25 test: sanitize/matcher, redirect
  chain, protected domain, snapshot hỏng, XSS/HTML trong path, quyền, không storage/DOM sink).

---

## 7. Kiểm thử chấp nhận

Chạy trên Chrome và Edge (Windows 10 22H2/Windows 11), ghi kết quả vào
[release-checklist.md](./release-checklist.md):

1. Cài MSI, đăng ký extension, bật bảo vệ → popup **Đang bảo vệ**; mở
   `https://www.youtube.com/shorts/<id>` với rule URL contains → trang chặn hiển thị, không lộ URL;
   blocked attempt xuất hiện trong báo cáo.
2. Rule domain exact/suffix/contains và allow rule → đúng như mục 3.
3. Tắt EduFocus (hoặc tắt bảo vệ) → điều hướng trở lại bình thường; popup **Chưa kết nối EduFocus**;
   bật lại → chặn hoạt động trong vài giây.
4. SPA navigation (YouTube Shorts từ trang chủ) bị chặn; server redirect tới URL bị chặn bị chặn.
5. URL có query/fragment/token không xuất hiện trong DB, log hay báo cáo; chỉ URL đã làm sạch.
6. Gỡ MSI → registry key và manifest biến mất; extension không còn host nhưng trình duyệt vẫn
   hoạt động bình thường (fail-open).
7. `chrome://extensions` không có lỗi service worker; extension không xin quyền ngoài bảng ở mục 2.

---

## 8. Xử lý sự cố

| Hiện tượng | Kiểm tra |
|---|---|
| Popup **Chưa kết nối EduFocus** | EduFocus có đang chạy và bảo vệ? `native-host\EduFocus.NativeHost.exe` tồn tại? Registry key trỏ đúng manifest? |
| Chrome thấy host nhưng extension báo lỗi kết nối | `allowed_origins` phải đúng `chrome-extension://<id>/`; ID extension phải là `lcanbfnejggilcaeinmahlcfimecmjmp` |
| Edge hoạt động, Chrome không (hoặc ngược lại) | Kiểm tra cả khóa Chrome/Edge; trình duyệt 32-bit đọc `WOW6432Node` (MSI đã đăng ký cả hai view) |
| Chặn không có hiệu lực sau khi cài | Tải lại extension; mở service worker console trong `chrome://extensions`; xem log EduFocus |
| Không có blocked attempt | URL có bị làm sạch khác rule? Query string bị bỏ; rule allow/protected domain thắng |
| Chính sách công ty chặn native messaging | Kiểm tra `NativeMessagingUserLevelHosts`, `ExtensionInstallBlocklist`, `chrome://policy` |
| Host không khởi động | Chạy trực tiếp `EduFocus.NativeHost.exe` không tham số; mã thoát `2` nghĩa là origin không khớp |

---

## 9. Quyền riêng tư

- Extension **không lưu lịch sử duyệt web**, không gửi URL tới cloud, không có analytics/telemetry,
  không đọc nội dung trang, cookie, biểu mẫu hay clipboard.
- URL được xử lý trong RAM của trình duyệt. Chỉ URL **khớp rule chặn** (đã bỏ query/fragment/
  credentials, tối đa 2048 ký tự) được gửi qua native messaging tới tiến trình EduFocus trên cùng
  máy để ghi blocked attempt.
- Nhận định dữ liệu chi tiết nằm trong [privacy-notice.md](./privacy-notice.md) và
  [dpia.md](./dpia.md).
