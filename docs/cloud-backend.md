# Cloud relay (Phase 8) — kiến trúc, API và vận hành

> **Trạng thái:** Thiết kế đích cho Phase 8, chưa phát hành.
> **Ngày:** 2026-11-17 · **Áp dụng cho:** EduFocus 2.1.0 trở đi (cloud tùy chọn)
> **Quyết định kiến trúc:** [ADR-20](./adr/ADR-20-optional-cloud-relay.md)

Cloud relay là thành phần **tùy chọn** giúp phụ huynh nhận thông báo đẩy và xem báo cáo tóm tắt trên
điện thoại. Khi phụ huynh không ghép nối điện thoại, EduFocus hoạt động hoàn toàn cục bộ như trước.

---

## 1. Phạm vi và nguyên tắc

- **Opt-in rõ ràng:** cloud mặc định tắt; chỉ bật sau khi phụ huynh xác thực PIN, đọc thông báo cập
  nhật và ghép nối điện thoại thành công.
- **Least data:** chỉ rời máy bốn loại sự kiện tổng hợp (xem §3). Không gửi URL, tên miền, tiêu đề
  cửa sổ, tên ứng dụng, mẫu hoạt động thô, dữ liệu camera, PIN hay mã khôi phục.
- **Không khóa nhà cung cấp:** backend đóng gói Docker, tự host được; mobile chỉ phụ thuộc FCM làm
  kênh vận chuyển.
- **Có thể thu hồi và xóa:** ngắt kết nối, thu hồi token, xóa gia đình trên cloud bất kỳ lúc nào.
- **Không phải bản sao DB:** cloud không chứa đủ dữ liệu để tái tạo báo cáo chi tiết; báo cáo chi
  tiết vẫn chỉ có trên máy tính.

## 2. Thành phần

```text
┌─────────────────────────────┐        HTTPS (JSON)        ┌──────────────────────────────┐
│ EduFocus Desktop (WPF)      │ ─────────────────────────► │ EduFocus Cloud API           │
│  - CloudLinkService (opt-in)│   Bearer efd_…             │  ASP.NET Core .NET 10        │
│  - SQLite outbox            │ ◄───────────────────────── │  PostgreSQL 16               │
│  - event producers          │   pairing / status         │  - pairing, auth, ingest     │
└─────────────────────────────┘                            │  - preferences, reports      │
                                                           │  - notification jobs         │
┌─────────────────────────────┐        HTTPS (JSON)        │  - FCM sender                │
│ EduFocus Parent (MAUI)      │ ─────────────────────────► │                              │
│  - pairing (QR / mã)        │   Bearer efm_…             └──────────────┬───────────────┘
│  - notifications, reports   │ ◄─────────────────────────                │ HTTPS (HTTP v1)
└──────────────▲──────────────┘                                           ▼
               │  FCM push (data-only)                        ┌─────────────────────────┐
               └──────────────────────────────────────────────│ Firebase Cloud Messaging│
                                                              └─────────────────────────┘
```

| Thành phần | Vai trò |
|---|---|
| `EduFocus.Infrastructure.Cloud` | HTTP client, outbox worker, pairing client phía desktop |
| `EduFocus.Cloud.Api` | REST API v1, xác thực token, rate limit, ingest |
| `EduFocus.Cloud.Data` | EF Core model + migration PostgreSQL, repository |
| `EduFocus.Cloud.Contracts` | DTO dùng chung server (và tham chiếu khi test) |
| Background jobs | tổng hợp daily summary, gửi push theo preference/quiet hours, retention |
| FCM HTTP v1 | kênh gửi push duy nhất tới Android |

Backend **không** nằm trong `EduFocus.slnx`; có solution và CI riêng (`cloud/`).

## 3. Dữ liệu được đồng bộ

Mỗi sự kiện có `clientEventId` (UUID do desktop sinh) để ingest idempotent.

### 3.1. `daily_summary`

```json
{
  "type": "daily_summary",
  "clientEventId": "0f4c…",
  "generatedAtUtc": "2026-11-17T13:30:00Z",
  "localDate": "2026-11-17",
  "studyMinutes": 95,
  "focusedMinutes": 71,
  "distractionCount": 6,
  "blockedAttempts": 12,
  "cameraCoveragePercent": 88,
  "onlineClassMinutes": 45
}
```

- Upsert **latest-wins** theo `(familyId, deviceId, localDate)`; bản có `generatedAtUtc` lớn hơn thay
  bản cũ. Desktop có thể gửi lại nhiều lần trong ngày.
- Không chứa category chi tiết, timeline hay tên ứng dụng/domain.

### 3.2. `attention_alert`

```json
{
  "type": "attention_alert",
  "clientEventId": "5a19…",
  "kind": "distracted",
  "startedAtUtc": "2026-11-17T09:05:00Z",
  "durationSeconds": 420
}
```

- Chỉ sinh khi trạng thái kéo dài **≥ 5 phút** và tối đa **1 thông báo/15 phút/loại**.
- `kind`: `distracted` | `absent`. Không gửi `drowsy` trong bản này.

### 3.3. `protection_off`

```json
{
  "type": "protection_off",
  "clientEventId": "9e02…",
  "occurredAtUtc": "2026-11-17T11:02:00Z",
  "reason": "parent_exit"
}
```

- Chỉ phát khi phụ huynh chủ động tắt hoặc thoát bằng PIN (`parent_exit`/`manual`). Không phát khi
  lịch tự tắt hoặc khi cập nhật/lỗi hệ thống.

### 3.4. `security_alert`

```json
{
  "type": "security_alert",
  "clientEventId": "audit-1234",
  "kind": "clock_jump",
  "occurredAtUtc": "2026-11-17T02:10:00Z"
}
```

- Sinh từ audit `TamperDetected` (lệch wall clock > 2 phút so với monotonic), `clientEventId` dạng
  `audit-{auditId}` để chống trùng.

## 4. API v1

Base path: `/v1`. Tất cả response lỗi dùng `application/problem+json` với `code`, `message`,
`traceId`. Xác thực: `Authorization: Bearer <token>`; token desktop `efd_`, mobile `efm_`.

| Method | Path | Auth | Mô tả |
|---|---|---|---|
| `POST` | `/v1/pairing/sessions` | không | Desktop tạo phiên ghép nối (rate limit theo IP) |
| `POST` | `/v1/pairing/claim` | không | Mobile claim bằng `code` (+ `sessionId` tùy chọn), trả token mobile |
| `GET` | `/v1/pairing/sessions/{id}` | không | Desktop poll trạng thái phiên (mã một lần, hết hạn 10 phút) |
| `GET` | `/v1/link/status` | `efd_` | Desktop kiểm tra ghép nối + thu hồi |
| `POST` | `/v1/link/disconnect` | `efd_` | Desktop ngắt kết nối, thu hồi token của mình |
| `POST` | `/v1/sync/events` | `efd_` | Ingest batch sự kiện |
| `POST` | `/v1/push/registrations` | `efm_` | Đăng ký/cập nhật FCM token |
| `GET` | `/v1/preferences` | `efm_` | Đọc preference thông báo |
| `PUT` | `/v1/preferences` | `efm_` | Cập nhật quiet hours, bật/tắt loại thông báo |
| `GET` | `/v1/reports/daily?from=&to=` | `efm_` | Báo cáo tóm tắt theo khoảng ngày |
| `GET` | `/v1/notifications?before=&limit=` | `efm_` | Danh sách thông báo đã phát |
| `GET` | `/v1/devices` | `efm_` | Thiết bị trong gia đình |
| `DELETE` | `/v1/devices/{id}` | `efd_`/`efm_` | Xóa thiết bị, thu hồi token và push registration |
| `DELETE` | `/v1/family` | `efm_` | Xóa toàn bộ dữ liệu cloud (header `X-Confirm: XÓA`) |
| `GET` | `/healthz` | không | Health check cho container/orchestrator |

### 4.1. Ghép nối

1. Desktop `POST /v1/pairing/sessions` → `{ sessionId, code, expiresAtUtc }`; desktop hiển thị QR và
   mã 8 ký tự, đồng thời poll `GET /v1/pairing/sessions/{id}` mỗi 2 giây.
2. Mobile quét QR hoặc nhập mã → `POST /v1/pairing/claim` với `{ code, deviceName, platform,
   pushToken? }` (không cần `sessionId` khi nhập tay). Server tìm **phiên đang hoạt động duy nhất**
   theo hash mã; nếu có nhiều hơn một, trả `409 ambiguous_code` và yêu cầu quét QR.
3. Claim thành công: server tạo `Device` mobile, phát token `efm_…` (chỉ trả một lần), đánh dấu
   phiên `Claimed`. Desktop poll thấy `Claimed` → lưu `familyId`, `deviceId` desktop và token `efd_…`
   (server phát ở bước poll, cũng chỉ trả một lần).
4. Cả hai bên hiển thị trạng thái "Đã ghép nối"; desktop bắt đầu outbox worker.

Mã ghép nối: Crockford base32 (32 ký tự, bỏ `I/L/O/U`), 8 ký tự, CSPRNG, hết hạn 10 phút, server chỉ
lưu `CodeHash`. Giới hạn 5 lần claim sai/10 phút/IP và 10 lần/phiên.

### 4.2. Ingest

- Batch ≤ **50** sự kiện, body ≤ **64 KB**; vượt → `413`.
- Mỗi sự kiện idempotent theo `(familyId, clientEventId)`; trùng thì bỏ qua và trả trạng thái đã nhận.
- Rate limit desktop: 60 request/phút/token.
- Outbox phía desktop: retry backoff 1 → 60 phút, tối đa 5.000 sự kiện/7 ngày; quá hạn thì bỏ bản
  ghi cũ nhất và ghi audit cục bộ.

### 4.3. Push

- Gửi bằng FCM HTTP v1, message **data-only** `{ "type": "...", "date": "...", "notificationId":
  "..." }`; app mobile tự dựng tiêu đề/nội dung tiếng Việt chung.
- Token FCM được lưu theo `PushRegistration`; token invalid (`UNREGISTERED`, `INVALID_ARGUMENT`) bị
  vô hiệu và đánh dấu cần đăng ký lại.
- Job gửi chạy mỗi phút: chọn sự kiện chưa thông báo, áp preference + quiet hours theo múi giờ gia
  đình, dedupe theo `(familyId, kind, windowStartUtc)`.
- Quiet hours mặc định **22:00–06:00**; tóm tắt ngày mặc định **20:30**; ngoài quiet hours mới gửi
  cảnh báo tức thời, còn lại gộp vào đầu khung thức.

## 5. Mô hình dữ liệu PostgreSQL

| Bảng | Cột chính | Ghi chú |
|---|---|---|
| `Families` | `Id`, `CreatedAtUtc`, `Timezone`, `DeletedAtUtc` | một gia đình nhiều thiết bị |
| `Devices` | `Id`, `FamilyId`, `Kind` (desktop/mobile), `Name`, `Platform`, `TokenHash`, `Scopes`, `CreatedAtUtc`, `RevokedAtUtc` | không lưu token thô |
| `DeviceTokens` | `Id`, `DeviceId`, `TokenHash`, `Scopes`, `ExpiresAtUtc`, `RevokedAtUtc` | hỗ trợ xoay vòng |
| `PairingSessions` | `Id`, `FamilyId?`, `CodeHash`, `CreatedAtUtc`, `ExpiresAtUtc`, `ClaimedAtUtc`, `Status` | unique partial index trên `CodeHash` khi còn hiệu lực |
| `PushRegistrations` | `Id`, `DeviceId`, `FcmToken`, `Platform`, `UpdatedAtUtc`, `InvalidatedAtUtc` | token FCM là dữ liệu nhạy cảm |
| `NotificationPreferences` | `FamilyId`, `QuietStart`, `QuietEnd`, `DailySummaryTime`, `AttentionAlertsEnabled`, `ProtectionOffEnabled`, `SummaryEnabled` | một bản/gia đình |
| `DailySummaries` | `Id`, `FamilyId`, `DeviceId`, `LocalDate`, `GeneratedAtUtc`, `StudyMinutes`, `FocusedMinutes`, `DistractionCount`, `BlockedAttempts`, `CameraCoveragePercent`, `OnlineClassMinutes` | unique `(FamilyId, DeviceId, LocalDate)` |
| `SyncEvents` | `Id`, `FamilyId`, `DeviceId`, `ClientEventId`, `Type`, `PayloadJson`, `ReceivedAtUtc` | dedupe + xử lý nền |
| `Notifications` | `Id`, `FamilyId`, `DeviceId`, `Kind`, `DedupeKey`, `PayloadJson`, `CreatedAtUtc`, `SentAtUtc`, `SuppressedReason` | log gửi |
| `CloudAuditLogs` | `Id`, `FamilyId?`, `Actor`, `Action`, `OccurredAtUtc`, `MetadataJson` | không chứa token/PIN |

Quan hệ cascade từ `Families`; xóa gia đình xóa toàn bộ bảng con.

## 6. Thời hạn lưu trên cloud

| Dữ liệu | Thời hạn | Cơ chế |
|---|---|---|
| `PairingSessions` | 24 giờ sau khi hết hạn/claim | job retention |
| `SyncEvents` | 7 ngày | job retention |
| `Notifications` | 30 ngày | job retention |
| `DailySummaries` | 180 ngày | job retention |
| `CloudAuditLogs` | 365 ngày | job retention (trừ khi pháp luật yêu cầu khác) |
| Tài khoản/token | tới khi thu hồi hoặc xóa gia đình | cascade |

Retention cloud chạy mỗi ngày, batch nhỏ, ghi audit. Không xóa dữ liệu của gia đình đang trong
thời gian yêu cầu xóa (xử lý đồng bộ ngay khi nhận yêu cầu).

## 7. Bảo mật

- Toàn bộ API sau HTTPS/TLS; HSTS; không nhận HTTP trần.
- Token đục 32 byte, hash SHA-256 phía server; so sánh constant-time; không ghi log.
- Rate limit theo IP/token/endpoint; body size limit; validate schema chặt (reject unknown fields ở
  ingest).
- Pairing code chỉ lưu hash; giới hạn thử; mã hết hạn 10 phút.
- FCM service account chỉ nằm trong secret của môi trường deploy, **không** commit vào repo, không
  nằm trong image.
- Database không public; chỉ API trong cùng mạng Docker; backup mã hóa nếu hạ tầng hỗ trợ.
- Audit mọi thao tác xóa/thu hồi và mọi lỗi xác thực bất thường.

## 8. Triển khai và vận hành

- `cloud/docker-compose.yml`: `api` + `postgres` + volume; cấu hình qua biến môi trường
  (`.env.example` chỉ chứa placeholder).
- Biến chính: `ConnectionStrings__Postgres`, `EduFocus__PublicBaseUrl`, `Fcm__ProjectId`,
  `Fcm__ServiceAccountPath`, `EduFocus__TokenPepper` (tùy chọn), `ASPNETCORE_URLS`.
- Migration tự động khi khởi động chỉ ở môi trường Development; production chạy job/migration riêng.
- Health check `/healthz`; log có cấu trúc, che token/FCM token.
- Self-host: chủ máy chủ chịu trách nhiệm TLS, backup PostgreSQL, cập nhật image, và tuân thủ nghĩa
  vụ dữ liệu cá nhân tương ứng.

## 9. Giới hạn đã biết

- Push phụ thuộc FCM/Google; thiết bị không có Google Play Services sẽ không nhận được thông báo.
- Độ trễ phụ thuộc mạng và chính sách tiết kiệm pin của Android; cảnh báo có thể đến muộn.
- Cloud không có timeline chi tiết; app mobile chỉ hiển thị tổng hợp ngày và danh sách cảnh báo.
- Không hỗ trợ đồng bộ hai chiều cấu hình blocklist từ điện thoại trong bản này.
- Nếu phụ huynh tự host, chất lượng dịch vụ và trách nhiệm pháp lý thuộc về họ.

## 10. Tài liệu liên quan

- [ADR-20](./adr/ADR-20-optional-cloud-relay.md) — quyết định và đánh đổi.
- [privacy-notice.md](./privacy-notice.md) — thông báo quyền riêng tư bản 2.0.
- [dpia.md](./dpia.md) — data map và đánh giá rủi ro bổ sung cho cloud.
- [use_cases.md](./use_cases.md) — UC15–UC18.
- [system_architecture.md](./system_architecture.md) §10–§11 — vị trí cloud trong kiến trúc.
