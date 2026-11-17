# EduFocus Phụ huynh (mobile)

App .NET MAUI cho phụ huynh, **Android trước**. App chỉ nhận thông báo đẩy và xem báo cáo tổng hợp;
không điều khiển từ xa bảo vệ của máy học sinh trong phase này.

- Kiến trúc và API: [../docs/cloud-backend.md](../docs/cloud-backend.md)
- Quyết định cloud: [../docs/adr/ADR-20-optional-cloud-relay.md](../docs/adr/ADR-20-optional-cloud-relay.md)
- Luồng ghép nối: [../docs/use_cases.md](../docs/use_cases.md) (UC15–UC18)

## Cấu trúc

```text
mobile/
├── EduFocus.Parent.slnx
├── EduFocus.Parent.App/      # .NET MAUI net10.0-android: Shell, Views, SecureStorage, ZXing, FCM
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
   tùy chọn `GET|PUT /v1/preferences` (4 công tắc cảnh báo, giờ yên tĩnh, giờ tổng kết ngày) chỉ
   cho phép sửa trên thiết bị mobile.
4. **Cài đặt** — tên máy tính đã ghép nối, **host** server (không hiện URL đầy đủ), thông tin điện
   thoại/phiên bản app; danh sách thiết bị trong gia đình `GET /v1/devices` với nút **Thu hồi**
   (`DELETE /v1/devices/{id}`); ngắt kết nối; **Xóa dữ liệu trên cloud** (`DELETE /v1/family`, yêu
   cầu gõ đúng "XÓA"); liên kết chính sách quyền riêng tư.

Khi mất mạng, tab Tổng quan/Báo cáo/Thông báo hiển thị dữ liệu cache (Preferences, TTL 7 ngày,
không chứa token) kèm băng "Đang xem dữ liệu lưu lúc …" và nút thử lại. Mọi lỗi 401 đều đi qua
`SessionExpiryService`: xóa token/cache một lần duy nhất, quay về Welcome và báo "Phiên ghép nối đã
hết"; ghép nối lại thành công sẽ reset trạng thái này.

## Thông báo đẩy FCM (issue #91)

1. Tạo Firebase project, thêm Android app `com.qtech.edufocus.parent`, tải `google-services.json`
   đặt tại `mobile/EduFocus.Parent.App/google-services.json` — **không commit** (đã nằm trong
   `.gitignore`). Không có tệp này, app vẫn build/chạy bình thường nhưng toàn bộ mã FCM bị loại
   khỏi build (`FCM_ENABLED`).
2. CI release giải mã secret `GOOGLE_SERVICES_JSON_BASE64` vào đúng đường dẫn trên trước khi build
   (issue #93). Không đưa khóa/keystore thật vào repo.
3. Sau khi ghép nối thành công, app giải thích và xin quyền `POST_NOTIFICATIONS` (Android 13+).
   Từ chối quyền không chặn tính năng báo cáo; nút **Bật thông báo** trong Cài đặt xin lại quyền
   hoặc mở Cài đặt hệ thống. Android 8–12 chỉ cần channel và công tắc thông báo của hệ thống.
4. Token FCM đăng ký bằng `POST /v1/push/registrations` với token mobile. Token refresh được đăng
   ký lại; lỗi mạng/5xx retry backoff 2s/5s (tối đa 3 lần), 429 tôn trọng `Retry-After`; 400/401/
   403/404 dừng ngay. Token chỉ gửi trong body và lưu trong Preferences, không ghi log.
5. Backend gửi **data-only** `{ type, date, notificationId }`. App tự dựng notification channel
   `edufocus_alerts` (importance high), nội dung chung theo loại (không số liệu, không tên học
   sinh/app/URL) và dedupe theo `notificationId` (tối đa 64 mã, hết hạn sau 7 ngày).
6. Foreground: banner Toast trong app, không thêm notification hệ thống. Background/killed:
   notification hệ thống, bấm vào mở đúng tab:
   `attention_alert`/`daily_summary` → Báo cáo ngày `date`; `protection_off`/`security_alert` →
   Thông báo.
7. Ngắt kết nối thu hồi device phía server (xóa push registration) và xóa token FCM cục bộ; không
   dùng lại token cũ.
8. Kiểm thử E2E cần Firebase project thật và thiết bị Android 8/12/14 — thực hiện ở issue #94.

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

Nếu có `google-services.json` trong thư mục app (hoặc truyền
`-p:GoogleServicesJsonFile=<đường dẫn>`), build tự bật FCM và tham chiếu
`Plugin.Firebase.CloudMessaging`; ngược lại app build không có push nhưng vẫn đầy đủ tính năng
ghép nối/báo cáo.

APK Debug nằm trong `mobile/EduFocus.Parent.App/bin/Debug/net10.0-android/`. Cài lên thiết bị:

```bash
adb install -r mobile/EduFocus.Parent.App/bin/Debug/net10.0-android/com.qtech.edufocus.parent-Signed.apk
```

## Thông số app

| Mục | Giá trị |
|---|---|
| Tên hiển thị | EduFocus Phụ huynh |
| Package id | `com.qtech.edufocus.parent` (không đổi sau khi phát hành) |
| Min SDK | 26 (Android 8.0) |
| Orientation | Dọc |
| Tab | Tổng quan, Báo cáo, Thông báo, Cài đặt |

## Bảo mật

- Token mobile (`efm_`) chỉ lưu trong `SecureStorage`; không ghi log, không đưa vào crash report.
- `CloudApiClient` không hard-code domain; base URL lấy từ QR hoặc cấu hình self-host.
- Timeout 15 giây; lỗi API được chuyển thành `CloudApiException` có mã và thông báo tiếng Việt.
- Debug có thể gọi HTTP LAN khi demo (chỉ với localhost/IP LAN); bản Release đặt
  `usesCleartextTraffic=false` và chỉ dùng HTTPS.
- Parser QR là hàm thuần trong `EduFocus.Parent.Core` và có unit test cho scheme/version/URL/code.
- Camera chỉ dùng khi người dùng bật quét QR; app không lưu ảnh. Deep link chỉ điền sẵn mã và cần
  người dùng xác nhận ghép nối.
- Không commit `google-services.json`, keystore hay secret. FCM token chỉ nằm trong request đăng
  ký và Preferences; không log, không hiển thị, không đưa vào thông báo/exception.
- Notification chỉ chứa nội dung chung theo loại; mã loại/ngày/notificationId là dữ liệu duy nhất
  đi qua FCM, không có dữ liệu học tập chi tiết.

## CI

`.github/workflows/mobile-ci.yml` cài workload `maui-android`, chạy unit test, build APK Debug và
upload artifact. Workflow độc lập với CI desktop và cloud; thay đổi trong
`cloud/EduFocus.Cloud.Contracts` cũng kích hoạt build mobile.
