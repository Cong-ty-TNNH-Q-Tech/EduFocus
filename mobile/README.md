# EduFocus Phụ huynh (mobile)

App .NET MAUI cho phụ huynh, **Android trước**. App chỉ nhận thông báo đẩy và xem báo cáo tổng hợp;
không điều khiển từ xa bảo vệ của máy học sinh trong phase này.

- Kiến trúc và API: [../docs/cloud-backend.md](../docs/cloud-backend.md)
- Quyết định cloud: [../docs/adr/ADR-20-optional-cloud-relay.md](../docs/adr/ADR-20-optional-cloud-relay.md)
- Luồng ghép nối: [../docs/use_cases.md](../docs/use_cases.md) (UC15–UC18)
- Kiểm thử E2E và demo: [../docs/phase8-e2e-checklist.md](../docs/phase8-e2e-checklist.md)

## Cấu trúc

```text
mobile/
├── EduFocus.Parent.slnx
├── EduFocus.Parent.App/      # .NET MAUI net10.0-android: Shell, Views, SecureStorage, ZXing, push
├── EduFocus.Parent.UnifiedPush/ # Android binding org.unifiedpush.android:connector
├── EduFocus.Parent.Core/     # net10.0: AppState, pairing, push, CloudApiClient, Presentation (test được)
└── EduFocus.Parent.Tests/    # net10.0: xUnit cho parser, pairing, push, API client, view model và cache
```

`EduFocus.Parent.Core` tham chiếu trực tiếp `cloud/EduFocus.Cloud.Contracts` để dùng chung DTO,
không copy model sang mobile.

## Luồng ghép nối (issue #90)

1. Lần đầu mở app (chưa có token) → màn hình **Welcome** giới thiệu và nút "Ghép nối với máy tính".
2. Trên máy tính: Parent Mode → **Điện thoại** → bật kết nối cloud để nhận mã QR + mã 8 ký tự.
3. Trên điện thoại: quét QR (camera) hoặc nhập mã thủ công. QR mã hóa
   `edufocus://pair?v=1&u=<base64url URL>&s=<sessionId>&c=<code>`; parser từ chối sai scheme,
   sai phiên bản, URL không hợp lệ hoặc mã không đúng 8 ký tự Crockford base32.
4. App gọi `POST /v1/pairing/claim` (không cần token) với mã, sessionId (nếu có từ QR), tên thiết bị,
   `android`, phiên bản app và múi giờ IANA. Thành công → token `efm_` vào **SecureStorage**, tên máy
   tính/múi giờ/server vào **Preferences**, mở tab **Tổng quan**.
5. Deep link `edufocus://pair?...` mở thẳng màn hình ghép nối và chỉ điền sẵn mã — luôn cần bấm
   "Ghép nối" để tránh liên kết ngoài ý muốn.
6. Đã ghép nối mà quét mã mới → hộp thoại xác nhận thay thế; token cũ được thu hồi qua
   `DELETE /v1/devices/{id}` (best effort) trước khi claim.
7. Lỗi 400/410 → "Mã ghép nối không hợp lệ hoặc đã hết hạn"; 429 → đếm ngược theo `Retry-After`;
   lỗi mạng → thử lại. Không hiển thị chi tiết kỹ thuật.
8. **Cài đặt** hiển thị máy tính đang kết nối và nút **Ngắt kết nối** (xóa token, quay về Welcome).
   Địa chỉ server self-host được giữ lại để ghép nối lại nhanh.

Camera chỉ được xin quyền khi phụ huynh bấm "Bật camera quét QR"; từ chối quyền vẫn dùng được
nhập mã thủ công và có nút mở Cài đặt quyền của hệ thống.

## Bốn màn hình chính (issue #92)

View model của ba tab dữ liệu nằm trong `EduFocus.Parent.Core/Presentation` (net10.0) để unit test
được toàn bộ luồng tải/lỗi/cache mà không cần thiết bị Android; App chỉ giữ view XAML.

1. **Tổng quan** — gọi `GET /v1/reports/daily` cho hôm nay: thời gian học, thời gian máy đang dùng,
   số phiên online class, số phiên bị chặn, cảnh báo tỷ lệ thấp (chỉ khi phụ huynh bật). Kéo để làm
   mới. Không có dữ liệu hiển thị "—"/trạng thái trống, không giả số 0.
2. **Báo cáo** — biểu đồ 7 ngày và danh sách từng ngày (mock theo dữ liệu thật), chạm một ngày để
   xem chi tiết; hôm nay có nhãn "(hôm nay)". Deep link từ notification mở đúng ngày qua
   `//reports?date=yyyy-MM-dd`. Ngày hiển thị dd/MM, giờ theo múi giờ gia đình (IANA).
3. **Thông báo** — lịch sử `GET /v1/notifications` phân trang 50, nút "Tải thêm" theo cursor; thẻ
   tùy chọn `GET|PUT /v1/preferences` (5 công tắc cảnh báo, giờ yên tĩnh, giờ tổng kết ngày) chỉ
   cho phép sửa trên thiết bị mobile.
4. **Cài đặt** — tên máy tính đã ghép nối, **host** server (không hiện URL đầy đủ), thông tin điện
   thoại/phiên bản app; danh sách thiết bị trong gia đình `GET /v1/devices` với nút **Thu hồi**
   (`DELETE /v1/devices/{id}`); ngắt kết nối; **Xóa dữ liệu trên cloud** (`DELETE /v1/family`, yêu
   cầu gõ đúng "XÓA"); liên kết chính sách quyền riêng tư.

Khi mất mạng, tab Tổng quan/Báo cáo/Thông báo hiển thị dữ liệu cache (Preferences, TTL 7 ngày,
không chứa token) kèm băng "Đang xem dữ liệu lưu lúc …" và nút thử lại. Mọi lỗi 401 đều đi qua
`SessionExpiryService`: xóa token/cache một lần duy nhất, quay về Welcome và báo "Phiên ghép nối đã
hết"; ghép nối lại thành công sẽ reset trạng thái này.

## Thông báo đẩy WSS (issue #91)

1. Sau khi ghép nối, app gọi `GET /v1/push/config`. Nếu server trả `webSocketEnabled=true`, app khởi
   động foreground service và giữ kết nối `wss://<cloud>/v1/push/stream` bằng token thiết bị; không
   cần cài ntfy hay UnifiedPush distributor.
2. Android 13+: app xin quyền `POST_NOTIFICATIONS` sau khi ghép nối. Từ chối quyền không chặn tính
   năng báo cáo; nút **Bật thông báo** trong Cài đặt xin lại quyền hoặc mở Cài đặt hệ thống.
   Android 8–12 chỉ cần channel và công tắc thông báo của hệ thống.
3. Service dùng notification channel `edufocus_connection` (importance low, ongoing) để Android giữ
   tiến trình; app vẫn nhận thông báo khi đóng bằng nút Back hoặc vuốt khỏi recent apps. Nếu OEM
   tiết kiệm pin dừng service, mở lại app hoặc nút **Kết nối lại thông báo** sẽ khởi động lại.
4. Backend gửi payload `{ type, date, notificationId }` qua WSS (heartbeat `type=heartbeat` bị bỏ
   qua). App tự dựng notification channel `edufocus_alerts` (importance high), nội dung chung theo
   loại (không số liệu, không tên học sinh/app/URL) và dedupe theo `notificationId` (tối đa 64 mã,
   hết hạn sau 7 ngày).
5. Foreground: banner Toast trong app, không thêm notification hệ thống. Background/killed:
   notification hệ thống, bấm vào mở đúng tab:
   `attention_alert`/`daily_summary`/`blocked_attempt` → Báo cáo ngày `date`;
   `protection_off`/`security_alert` → Thông báo.
6. Fallback: nếu `webSocketEnabled=false` và `enabled=true`, app dùng UnifiedPush/ntfy như cũ, đăng
   ký endpoint bằng `POST /v1/push/registrations`; backend gửi Web Push mã hóa RFC8291. Lỗi mạng/5xx
   retry backoff 2s/5s (tối đa 3 lần), 429 tôn trọng `Retry-After`; 400/401/403/404 dừng ngay.
7. Ngắt kết nối dừng WSS service, gọi `UnifiedPush.Unregister` (nếu đã dùng fallback), xóa
   endpoint/khóa cục bộ; token FCM cũ (nếu nâng cấp từ bản trước) cũng được xóa.
8. Kiểm thử E2E cần backend bật `Push__WebSocketEnabled` và thiết bị Android 8/12/14; không cần
   Firebase project, `google-services.json` hay ntfy. Trường hợp fallback cần thêm VAPID + distributor.

## Yêu cầu

- .NET SDK 10.0.200+
- Workload `maui-android` và Android SDK (API 36), JDK 17+
- Windows: Visual Studio 2022/2026 có workload .NET MAUI, hoặc dòng lệnh với biến môi trường:

```powershell
$env:ANDROID_HOME = "$env:LOCALAPPDATA\Android\android-sdk"
$env:JAVA_HOME = "C:\Program Files\Android\openjdk"
```

## Build và test

```bash
dotnet restore mobile/EduFocus.Parent.slnx
dotnet test mobile/EduFocus.Parent.Tests/EduFocus.Parent.Tests.csproj -c Release
dotnet build mobile/EduFocus.Parent.App/EduFocus.Parent.App.csproj -c Debug -f net10.0-android
```

Build luôn tham chiếu `EduFocus.Parent.UnifiedPush` (binding `org.unifiedpush.android:connector`
3.3.6). Lần restore đầu cần mạng để tải AAR từ Maven; không cần `google-services.json` hay thư viện
Firebase.

APK Debug nằm trong `mobile/EduFocus.Parent.App/bin/Debug/net10.0-android/`. Cài lên thiết bị:

```bash
adb install -r mobile/EduFocus.Parent.App/bin/Debug/net10.0-android/com.qtech.edufocus.parent-Signed.apk
```

## Phát hành APK ký số (issue #93)

Phase 8 phát hành nội bộ (chưa lên Google Play). **APK Debug không dùng để phát hành** vì
debuggable, ký bằng key debug và cho phép HTTP LAN.

### Khóa ký và secret

Tạo keystore một lần và lưu **ngoài repo**:

```bash
keytool -genkeypair -v -keystore edufocus-release.keystore \
  -alias edufocus -keyalg RSA -keysize 4096 -validity 10000
```

Sau đó thêm vào GitHub Secrets của repo:

| Secret | Nội dung |
|---|---|
| `ANDROID_KEYSTORE_BASE64` | keystore mã hóa Base64 (`[Convert]::ToBase64String([IO.File]::ReadAllBytes("..."))` trên PowerShell) |
| `ANDROID_KEYSTORE_PASSWORD` | mật khẩu keystore |
| `ANDROID_KEY_ALIAS` | alias, ví dụ `edufocus` |
| `ANDROID_KEY_PASSWORD` | mật khẩu key; với PKCS12 dùng **cùng giá trị** với mật khẩu keystore |

Workflow dừng ngay nếu thiếu bất kỳ secret nào và chỉ in **tên** secret, không in giá trị. Không
commit keystore/mật khẩu; `*.jks` và `*.keystore` đã nằm trong `.gitignore`.

Đặt biến repository `CLOUD_RELAY_BASE_URL` (Settings → Secrets and variables → Actions → Variables)
thành URL HTTPS production, không có dấu `/` cuối. Workflow desktop và mobile truyền giá trị này qua
`-p:CloudRelayBaseUrl`; app mobile dùng nó làm endpoint mặc định khi chưa có cấu hình/QR.

### Quy tắc version

- `versionName` lấy từ tag `vMAJOR.MINOR.PATCH` (bỏ `v`); hậu tố prerelease giữ nguyên nếu có.
- `versionCode = MAJOR*1.000.000 + MINOR*10.000 + PATCH*100 + slot`; bản stable dùng `slot=99`
  (ví dụ `v2.2.0` → `2020099`), prerelease dùng số cuối `1–98` (ví dụ `v2.2.0-rc.1` → `2020001`).
- Mỗi thành phần trong giới hạn `major ≤ 2000`, `minor ≤ 99`, `patch ≤ 99`; mỗi lần phát hành phải
  tăng phần số. Android từ chối cài đè nếu `versionCode` không lớn hơn bản đang cài.

### Workflow

`.github/workflows/mobile-release.yml` chạy khi push tag `v*.*.*` hoặc `workflow_dispatch` (nhập
version; mặc định dry-run, không đụng GitHub Release):

1. Chạy unit test.
2. Giải mã keystore vào thư mục tạm của runner.
3. `dotnet publish -f net10.0-android -c Release` với `AndroidKeyStore=true`,
   `ApplicationDisplayVersion`/`ApplicationVersion` lấy từ tag; xuất cả APK và AAB.
4. `apksigner verify` và `aapt2 dump badging` kiểm tra package id, `versionName`, `versionCode`.
5. Tạo `SHA256SUMS-Android.txt` (tách khỏi `SHA256SUMS.txt` của desktop để hai workflow không ghi
   đè nhau).
6. Upload artifact `edufocus-parent-android-<version>` và đính
   `EduFocus-Parent-Android-<version>.apk` (+ `.aab`), `SHA256SUMS-Android.txt` vào GitHub Release
   cùng tag (tạo release mới nếu chưa có).
7. Xóa keystore khỏi runner trong bước `always()`.

Build local tương đương:

```bash
dotnet publish mobile/EduFocus.Parent.App/EduFocus.Parent.App.csproj \
  --configuration Release --framework net10.0-android \
  -p:ApplicationDisplayVersion=2.1.0 -p:ApplicationVersion=20100 \
  -p:AndroidKeyStore=true \
  -p:AndroidSigningKeyStore=/duong/dan/edufocus-release.keystore \
  -p:AndroidSigningKeyAlias=edufocus \
  -p:AndroidSigningStorePass=<mat-khau-keystore> \
  -p:AndroidSigningKeyPass=<mat-khau-key> \
  --output publish
```

APK ký nằm trong `publish/` với tên `com.qtech.edufocus.parent-Signed.apk`.

### Cài đặt trên điện thoại

- Android 8.0 (API 26) trở lên. Bật "Cài đặt ứng dụng từ nguồn không xác định" cho trình quản lý
  tệp hoặc trình duyệt đang mở APK.
- Đối chiếu SHA-256 trước khi cài:
  - Windows: `Get-FileHash .\EduFocus-Parent-Android-2.1.0.apk -Algorithm SHA256`
  - Linux/macOS: `sha256sum EduFocus-Parent-Android-2.1.0.apk`
  - So với dòng tương ứng trong `SHA256SUMS-Android.txt` đính kèm release.
- Cài đè để nâng cấp: giữ nguyên package id và keystore nên token ghép nối trong SecureStorage vẫn
  còn. **Không gỡ ứng dụng** nếu muốn giữ liên kết.
- Trước khi gỡ hẳn, vào **Cài đặt → Ngắt kết nối** hoặc **Xóa dữ liệu trên cloud** (gõ `XÓA`) để
  thu hồi thiết bị và xóa dữ liệu gia đình trên cloud, sau đó mới gỡ app.
- Bản Release chỉ dùng HTTPS; để nhận push, backend bật `Push__WebSocketEnabled` và app giữ
  foreground service WSS. Fallback UnifiedPush/ntfy + VAPID chỉ cần khi tắt WSS. Kiểm tra push trên
  thiết bị sạch trước khi công bố (issue #94).

## Thông số app

| Mục | Giá trị |
|---|---|
| Tên hiển thị | EduFocus Phụ huynh |
| Package id | `com.qtech.edufocus.parent` (không đổi sau khi phát hành) |
| Min SDK | 26 (Android 8.0) |
| Orientation | Dọc |
| Tab | Tổng quan, Báo cáo, Thông báo, Cài đặt |

## Giới hạn đã biết

- Push WSS cần foreground service còn sống; OEM tiết kiệm pin có thể dừng service, app sẽ kết nối lại
  khi được mở. Fallback UnifiedPush/Web Push phụ thuộc distributor và VAPID. Độ trễ do mạng và chính
  sách pin Android.
- App chỉ xem dữ liệu desktop đã đồng bộ; mất mạng thì dùng cache 7 ngày, không có timeline chi tiết.
- Không điều khiển từ xa: không sửa blocklist, không bật/tắt Protection, không xem màn hình.
- Gỡ app không tự xóa dữ liệu cloud; phải dùng **Xóa dữ liệu trên cloud** trên app.

## Bảo mật
- Token mobile (`efm_`) chỉ lưu trong `SecureStorage`; không ghi log, không đưa vào crash report.
- `CloudApiClient` không hard-code domain; base URL lấy từ QR hoặc cấu hình self-host.
- Timeout 15 giây; lỗi API được chuyển thành `CloudApiException` có mã và thông báo tiếng Việt.
- Debug có thể gọi HTTP LAN khi demo (chỉ với localhost/IP LAN); bản Release đặt
  `usesCleartextTraffic=false` và chỉ dùng HTTPS.
- Parser QR là hàm thuần trong `EduFocus.Parent.Core` và có unit test cho scheme/version/URL/code.
- Camera chỉ dùng khi người dùng bật quét QR; app không lưu ảnh. Deep link chỉ điền sẵn mã và cần
  người dùng xác nhận ghép nối.
- Không commit keystore hay secret. Token WSS chỉ gửi trong header `Authorization` tới backend;
  UnifiedPush endpoint/khóa Web Push chỉ nằm trong request đăng ký và Preferences; không log, không
  hiển thị, không đưa vào thông báo/exception.
- Notification chỉ chứa nội dung chung theo loại; payload `{ type, date, notificationId }` đi qua
  WSS (TLS) hoặc được mã hóa Web Push trước khi rời backend, distributor không đọc được nội dung học
  tập.

## CI

`.github/workflows/mobile-ci.yml` cài workload `maui-android`, chạy unit test, build APK Debug và
upload artifact. Workflow độc lập với CI desktop và cloud; thay đổi trong
`cloud/EduFocus.Cloud.Contracts` cũng kích hoạt build mobile.

`.github/workflows/mobile-release.yml` build APK/AAB ký số khi push tag `v*.*.*` — xem mục
"Phát hành APK ký số" ở trên.
