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
├── EduFocus.Parent.App/      # .NET MAUI net10.0-android: Shell, Views, SecureStorage, ZXing
├── EduFocus.Parent.Core/     # net10.0: AppState, pairing, CloudApiClient, ITokenStore (test được)
└── EduFocus.Parent.Tests/    # net10.0: xUnit cho parser, pairing service, API client và state
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
- Không commit `google-services.json`, keystore hay secret; FCM bổ sung ở issue #91.

## CI

`.github/workflows/mobile-ci.yml` cài workload `maui-android`, chạy unit test, build APK Debug và
upload artifact. Workflow độc lập với CI desktop và cloud; thay đổi trong
`cloud/EduFocus.Cloud.Contracts` cũng kích hoạt build mobile.
