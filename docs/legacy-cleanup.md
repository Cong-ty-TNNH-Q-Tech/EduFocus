# Dọn codebase 1.x — Issue #7

> **Trạng thái:** Đã hoàn tất trong Phase 1. Tài liệu này là inventory của issue #7:
> danh sách thành phần 1.x đã xóa, thành phần được giữ lại và lý do.

## 1. Mục tiêu

- Loại bỏ code, script, asset và tính năng của Don't Be Lazy 1.x không thuộc EduFocus 2.0
  (theo [BA §6.3](./ba_document.md#63-tính-năng-sẽ-loại-bỏ-khỏi-sản-phẩm-cũ)).
- Không còn entry point hoặc cấu hình vô tình kích hoạt engine 1.x.
- Không còn asset/logo/tên sản phẩm cũ trong UI hoặc build output.
- Giữ lại hạ tầng còn giá trị cho 2.0 để các issue sau tái sử dụng.

## 2. Thành phần đã xóa

| Khu vực | Thành phần | Lý do |
|---|---|---|
| `EduFocus.Domain` | `Entities/` (FocusTask, Profile, ProfileEntry, Quote, SessionHistory, SessionProfileSnapshot, SystemSettings) | Mô hình 1.x; 2.0 có entity mới ở issue #8 |
| `EduFocus.Domain` | `Enums/`, `ValueObjects/` của 1.x | Gắn với entity 1.x |
| `EduFocus.Domain.Tests` | FocusTask/Profile/SessionHistory/SystemSettings/ValueObject tests | Test cho code đã xóa |
| `EduFocus.Ports` | Toàn bộ `Inbound/` (8 use-case port) | Không còn use case 1.x |
| `EduFocus.Ports` | Toàn bộ `Outbound/Repositories/` | Repository 1.x; schema mới ở issue #9 |
| `EduFocus.Ports` | `IAiGuiltTripPort`, `IAiProfileAssistantPort`, `IAiTaskAssistantPort` | Gemini AI bị loại khỏi sản phẩm |
| `EduFocus.Ports` | `IStrictEnginePort`, `INotificationPort` | Engine/tray của 1.x |
| `EduFocus.Ports` | `DTOs/Dtos.cs`, `DTOs/Enums.cs` | DTO 1.x |
| `EduFocus.UseCases` | Toàn bộ 12 file (FocusTask, Profile, Quote, Settings, Analytics, FocusSession, ActiveSessionState, decorator, mapper, DI) | Nghiệp vụ 1.x; viết lại theo module 2.0 |
| `EduFocus.Infrastructure` | `GeminiAiServices.cs` | AI/guilt-trip bị loại |
| `EduFocus.Infrastructure` | `WindowsStrictEngine.cs` | Engine 1.x hard-code whitelist/blacklist, marker hosts `DONTBELAZY` |
| `EduFocus.Infrastructure` | `LocalRedirectServer.cs` | Trang chặn guilt-trip; thay bằng `BlockPageServer` ở Phase 3 |
| `EduFocus.Infrastructure` | `LoggingStrictEngineDecorator.cs` | Decorator cho engine đã xóa |
| `EduFocus.SqliteDataAccess` | `AppDbContext`, factory, 4 migrations, repositories, converters, UnitOfWork | Schema 1.x; `InitialEduFocusSchema` ở issue #9 |
| `EduFocus.WPF` | 5 view + code-behind (Dashboard, Profiles, FocusSession, Analytics, Settings) | UI 1.x; view mới ở Phase 2–6 |
| `EduFocus.WPF` | 6 view model (Main, Dashboard, Profiles, FocusSession, Analytics, Settings) | Gắn với UI 1.x |
| `EduFocus.WPF` | `Converters.cs`, `TrayNotificationService.cs` | Chỉ phục vụ UI 1.x |
| `EduFocus.WPF` | `Assets/give_up_quotes.txt` | Asset guilt-trip |
| `EduFocus.WPF` | `app.ico` | Icon thương hiệu cũ; icon 2.0 sẽ bổ sung sau |
| `assets/` | `logo.png` | Logo "DON'T BE LAZY" cũ, đã gỡ khỏi README |
| Build/installer | Chuỗi "Don't Be Lazy", mô tả "Stay focused. Don't be lazy.", `AppIcon` trong `Product.wxs` | Rebrand sang EduFocus |

Ngoài ra, `App.xaml.cs` đã bỏ: `Database.Migrate()` + self-heal `__EFMigrationsHistory`,
seed settings/profile, MaterialDesign theme, `GlobalStrictMode` khởi động, auto-start registry,
tray icon và logic chặn thoát khi có phiên.

## 3. Thành phần giữ lại

| Thành phần | Lý do |
|---|---|
| `EduFocus.Domain/Attention/*` + `Domain.Tests/Attention/*` | Spike pipeline attention (issue #1), dùng lại cho Phase 5 |
| `EduFocus.Infrastructure.Attention/*` | Adapter OpenCvSharp/ONNX của spike attention |
| `EduFocus.Ports/Outbound/Services/IAttentionFrameAnalyzer.cs`, `IMonotonicClockPort.cs`, `IAppLogger.cs`, `DTOs/AttentionDtos.cs` | Hợp đồng hạ tầng còn dùng |
| `EduFocus.Infrastructure/Logging/FileLogger.cs` | Log kỹ thuật có cấu trúc tại `%LocalAppData%\EduFocus\logs` (issue #12) |
| `EduFocus.Infrastructure/Services/MonotonicClockAdapter.cs` | Đo elapsed/detect clock jump (ADR-06) |
| `EduFocus.WPF/Services/UpdaterService.cs`, `ViewModels/UpdateViewModel.cs` | Auto-update qua GitHub Releases vẫn dùng cho 2.0 |
| `EduFocus.WPF/app.manifest`, `app.debug.manifest` | DPI awareness và elevation cho bản Release (ADR-07) |
| `EduFocus.WPF/AssemblyInfo.cs`, `App.xaml` | Hạ tầng WPF tối thiểu |
| `src/EduFocus.UseCases`, `src/EduFocus.SqliteDataAccess` | Giữ project scaffold (csproj rỗng) để issue #8/#9 thêm code đúng cấu trúc đích |
| `installer/Product.wxs` | Giữ WiX v4, đổi tên sản phẩm; `UpgradeCode` giữ nguyên để nâng cấp từ 1.x |
| `tools/AttentionPipelineSpike`, `tools/HostsBlockingSpike`, `tools/WatchdogSpike` | Bằng chứng spike Phase 0 |
| `docs/spikes/*` | Báo cáo đánh giá Phase 0 |

## 4. Hành vi sau khi dọn

- App khởi động vào **shell tối thiểu** (version + kiểm tra cập nhật), không mở DB,
  không chạy migration/seed, không theme MaterialDesign, không tray, không auto-start.
- Không còn đường nào ghi marker `DONTBELAZY` vào hosts file hay chặn process.
- Luồng cập nhật (GitHub Releases → MSI/ZIP) giữ nguyên.
- MSI dùng tên **EduFocus**, shortcut Desktop/Start Menu "EduFocus";
  `UpgradeCode` cũ giữ nguyên nên cài 2.0 sẽ thay thế bản 1.x.
- Build Release: 0 warning, test attention 15/15 pass.

## 5. Việc tiếp theo

- Issue #8: domain model 2.0 — đã hoàn tất; xem [domain-invariants.md](./domain-invariants.md).
- Issue #9: `InitialEduFocusSchema` + DbContext/converter/configuration — đã hoàn tất; xem [database_schema.md](./database_schema.md) §17.
- Issue #10: ACL thư mục dữ liệu, backup verify/retention và restore — đã hoàn tất.
- Issue #11: CI .NET 10 với cache/artifact và hướng dẫn branch protection — đã hoàn tất.
- Issue #12: structured logging, correlation id và audit log tách khỏi log kỹ thuật — đã hoàn tất.
- Issue #13: PBKDF2 hasher, recovery code, `ParentAuthUseCase` và credential repository — đã hoàn tất.
- Issue #14: Parent Mode UI, phiên 5 phút, lockout và reset PIN — đã hoàn tất.
- Issue #15: onboarding thiết lập PIN, privacy consent và tùy chọn an toàn — đã hoàn tất.
- Issue #16: bật/tắt bảo vệ, session/audit, tray và reconcile sau restart — đã hoàn tất; engine chặn thật thuộc issue #24.
- Issue #17: lịch bảo vệ, scheduler 30 giây, override và UI Parent Mode — đã hoàn tất.
- Các phase sau: ProtectionEngine/BlockPageServer, session/activity,
  attention monitoring, dashboard/reports, installer hoàn chỉnh.
