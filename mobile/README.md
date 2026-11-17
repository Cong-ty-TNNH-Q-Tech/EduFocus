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
├── EduFocus.Parent.App/      # .NET MAUI net10.0-android: Shell, Views, SecureStorage
├── EduFocus.Parent.Core/     # net10.0: AppState, CloudApiClient, ITokenStore (test được)
└── EduFocus.Parent.Tests/    # net10.0: xUnit cho API client và state
```

`EduFocus.Parent.Core` tham chiếu trực tiếp `cloud/EduFocus.Cloud.Contracts` để dùng chung DTO,
không copy model sang mobile.

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
- `CloudApiClient` không hard-code domain; base URL lấy từ kết quả ghép nối.
- Timeout 15 giây; lỗi API được chuyển thành `CloudApiException` có mã và thông báo tiếng Việt.
- Debug có thể gọi HTTP LAN khi demo; bản Release đặt `usesCleartextTraffic=false` và chỉ dùng HTTPS.
- Không commit `google-services.json`, keystore hay secret; FCM/QR bổ sung ở issue #90–#93.

## CI

`.github/workflows/mobile-ci.yml` cài workload `maui-android`, chạy unit test, build APK Debug và
upload artifact. Workflow độc lập với CI desktop và cloud; thay đổi trong
`cloud/EduFocus.Cloud.Contracts` cũng kích hoạt build mobile.
