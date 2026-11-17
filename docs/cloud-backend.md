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
  "clientEventId": "uuid",
  "type": "daily_summary",
  "occurredAtUtc": "2026-11-17T13:30:00Z",
  "payload": {
    "localDate": "2026-11-17",
    "timeZoneId": "Asia/Ho_Chi_Minh",
    "generatedAtUtc": "2026-11-17T13:30:00Z",
    "sessionsCount": 3,
    "activeSeconds": 7200, "studySeconds": 6500, "idleSeconds": 700,
    "onlineClassSeconds": 3600, "monitoringEligibleSeconds": 7000,
    "monitoredSeconds": 6300, "focusedSeconds": 5400,
    "distractedSeconds": 600, "absentSeconds": 240, "drowsySeconds": 60,
    "blockedAttempts": 12,
    "attentionPercent": 85.7, "coveragePercent": 90.0
  }
}
```

- Upsert **latest-wins** theo `(familyId, deviceId, localDate)`; bản có `generatedAtUtc` lớn hơn thay
  bản cũ (`generatedAtUtc` được lưu ở `DailySummaries.UpdatedAtUtc`). Desktop có thể gửi lại nhiều
  lần trong ngày.
- Chỉ nhận cho **7 ngày gần nhất** theo múi giờ gia đình (`Sync__DailySummaryWindowDays`); cũ hơn bị
  từ chối `stale_summary`.
- Không chứa category chi tiết, timeline hay tên ứng dụng/domain.

### 3.2. `attention_alert`

```json
{
  "clientEventId": "5a19…",
  "type": "attention_alert",
  "occurredAtUtc": "2026-11-17T09:12:00Z",
  "payload": {
    "kind": "distracted",
    "localDate": "2026-11-17",
    "startedAtUtc": "2026-11-17T09:05:00Z",
    "durationSeconds": 420
  }
}
```

- Chỉ sinh khi trạng thái kéo dài **≥ 5 phút** và tối đa **1 thông báo/15 phút/loại**.
- `kind`: `distracted` | `absent`. Server từ chối `durationSeconds < 60`.

### 3.3. `protection_off`

```json
{
  "clientEventId": "9e02…",
  "type": "protection_off",
  "occurredAtUtc": "2026-11-17T11:02:00Z",
  "payload": { "source": "parent", "localDate": "2026-11-17" }
}
```

- `source`: `parent` (tắt qua PIN) | `exit` (thoát bằng PIN). Desktop **không** phát khi lịch tự tắt;
  giá trị `schedule` vẫn được schema chấp nhận để tương thích nhưng không dùng trong bản này.

### 3.4. `security_alert`

```json
{
  "clientEventId": "audit-1234",
  "type": "security_alert",
  "occurredAtUtc": "2026-11-17T02:10:00Z",
  "payload": { "kind": "clock_jump", "localDate": "2026-11-17" }
}
```

- Sinh từ audit `TamperDetected` (lệch wall clock > 2 phút so với monotonic), `clientEventId` dạng
  `audit-{auditId}` để chống trùng.

## 4. API v1

Base path: `/v1`. Response lỗi dùng JSON `{"error":{"code","message","traceId?"}}` (401/403/404/429 có
`code` ổn định). Xác thực: `Authorization: Bearer <token>`; token desktop `efd_`, mobile `efm_`.

| Method | Path | Auth | Mô tả |
|---|---|---|---|
| `POST` | `/v1/pairing/sessions` | không | Desktop tạo phiên ghép nối, nhận `code` + token desktop (rate limit theo IP) |
| `POST` | `/v1/pairing/claim` | không | Mobile claim bằng `code` (+ `sessionId` tùy chọn), trả token mobile |
| `GET` | `/v1/pairing/sessions/{id}` | `efd_`+`family:admin` | Desktop poll trạng thái phiên (hết hạn 10 phút) |
| `GET` | `/v1/link/status` | `efd_`/`efm_` | Trạng thái ghép nối, thiết bị, lần đồng bộ cuối |
| `DELETE` | `/v1/devices/{id}` | `family:admin` | Thu hồi thiết bị, token và push registration |
| `POST` | `/v1/devices/token/rotate` | `efd_`/`efm_` | Xoay token của chính thiết bị đang gọi |
| `POST` | `/v1/sync/events` | `efd_`+`sync` | Ingest batch sự kiện |
| `POST` | `/v1/push/registrations` | `efm_` | Đăng ký/cập nhật FCM token |
| `GET` | `/v1/preferences` | `efm_`+`read` | Đọc preference thông báo |
| `PUT` | `/v1/preferences` | `efm_`+`prefs` | Cập nhật quiet hours, bật/tắt loại thông báo |
| `GET` | `/v1/reports/daily?from=&to=` | `efm_`+`read` | Báo cáo tóm tắt theo khoảng ngày |
| `GET` | `/v1/notifications?before=&limit=` | `efm_`+`read` | Danh sách thông báo đã phát |
| `GET` | `/v1/devices` | `efm_`+`read` | Thiết bị trong gia đình |
| `DELETE` | `/v1/family` | `efm_`+`family:admin` | Xóa toàn bộ dữ liệu cloud (body `{"confirmation":"XÓA"}`) |
| `GET` | `/healthz` | không | Health check cho container/orchestrator |

Rate limit fixed-window: tạo phiên **10/giờ/IP**, claim **20/giờ/IP**, chung **120/phút/token**;
vượt → `429` kèm `Retry-After` và `code: rate_limited`.

### 4.1. Ghép nối

1. Desktop `POST /v1/pairing/sessions` với `{ desktopName, platform, appVersion?, timeZoneId? }` →
   `{ sessionId, code, pairingUri, expiresAtUtc, desktopToken, cloudBaseUrl }`. Server tạo gia đình,
   thiết bị desktop và token `efd_…` **một lần**; desktop lưu token và hiển thị QR (chứa
   `pairingUri`) cùng mã 8 ký tự, rồi poll `GET /v1/pairing/sessions/{id}` mỗi 2 giây.
2. Mobile quét QR hoặc nhập mã → `POST /v1/pairing/claim` với `{ code, deviceName, platform,
   appVersion?, sessionId?, timeZoneId? }`. Server tìm phiên chưa claim theo hash mã; `sessionId`
   (từ QR) phải khớp nếu có.
3. Claim thành công: server tạo `Device` mobile, phát token `efm_…` (chỉ trả một lần), đánh dấu
   phiên `Claimed`. Desktop poll thấy `Claimed` kèm tên thiết bị mobile.
4. Cả hai bên hiển thị trạng thái "Đã ghép nối"; desktop bắt đầu outbox worker.

Mã ghép nối: Crockford base32 (32 ký tự, bỏ `I/L/O/U`), 8 ký tự, CSPRNG, hết hạn 10 phút, server chỉ
lưu `CodeHash` (unique trên các phiên chưa claim). Mọi lỗi claim (mã sai, hết hạn, đã dùng, quá số
lần) trả thống nhất `410 pairing_unavailable`; tối đa **5 lần claim sai/phiên**, sau đó phiên bị vô
hiệu. Claim dùng cập nhật nguyên tử để hai điện thoại không thể claim cùng một mã.

### 4.2. Ingest

- Batch ≤ **50** sự kiện, body ≤ **64 KB**; vượt → `400 too_many_events` hoặc `413 payload_too_large`.
- Body: `{ "events": [ { clientEventId, type, occurredAtUtc, payload } ] }`. Mỗi event nhận kết quả
  `applied` | `duplicate` | `rejected` kèm `code` khi bị từ chối; HTTP 200 nếu batch đọc được.
- Payload được validate theo whitelist từng type; **field lạ bị từ chối** (`unknown_field`). Số giây
  0–86400, phần trăm 0–100 hoặc null, `timeZoneId` IANA hợp lệ (đổi múi giờ được ghi audit).
- Idempotent theo `(familyId, clientEventId)`; event đã nhận trả `duplicate`, không tạo notification
  lần hai. Event bị từ chối vẫn lưu `SyncEvents.Status = Rejected` để chống retry lặp.
- `daily_summary` upsert theo `(familyId, deviceId, localDate)`, chỉ ghi đè khi `generatedAtUtc` mới
  hơn. Alert events tạo `NotificationRecord` (Pending) theo dedupe rồi cả hai loại đều chuyển
  `Processed` ngay trong cùng transaction; payload không lưu lại.
- Lỗi DB → `503 store_unavailable` để desktop retry; rate limit desktop **60 request/phút/token**.
- Outbox phía desktop: retry backoff 1 → 60 phút, tối đa 5.000 sự kiện/7 ngày; quá hạn thì bỏ bản
  ghi cũ nhất và ghi audit cục bộ.

### 4.3. Push

- `POST /v1/push/registrations` (chỉ thiết bị mobile) upsert token theo device; cùng token ở thiết bị
  khác bị vô hiệu để không gửi trùng. Token invalid (`UNREGISTERED`, `INVALID_ARGUMENT`) bị đánh dấu
  `InvalidatedAtUtc` và không thử lại.
- `GET/PUT /v1/preferences`: bật/tắt 4 loại, quiet hours 22:00–06:00 mặc định, tổng kết ngày 20:30
  theo múi giờ gia đình; phút 0–1439, `timeZoneId` IANA; đổi preference ghi audit (không chứa token).
- Job gửi chạy mỗi phút (`Push:PollSeconds`): chọn `Notifications` Pending, áp preference + quiet
  hours, chỉ gửi khi có ít nhất một thiết bị mobile active có token hợp lệ.
- Dedupe khóa unique `(FamilyId, DedupeKey)`:

  | Category | DedupeKey | Tần suất |
  |---|---|---|
  | `attention_alert` | `attention_alert:{kind}:{window15m}` | 1 lần/15 phút/loại |
  | `daily_summary` | `daily_summary:{localDate}` | 1 lần/ngày/gia đình (job #84) |
  | `protection_off` | `protection_off:{source}:{hourUtc}` | 1 lần/giờ/source |
  | `security_alert` | `security_alert:{kind}:{hourUtc}` | 1 lần/giờ/kind |

- Trạng thái: `Pending` → `Sent` | `Failed` | `Suppressed` (`preference_disabled`, `quiet_hours`,
  `no_device`, `family_unavailable`). Lỗi tạm thời retry tối đa 3 lần, backoff 2 → 4 phút; hết lượt
  hoặc toàn bộ token invalid → `Failed`. Security alert bỏ qua quiet hours khi
  `SecurityAlertBypassQuietHours = true` (mặc định).
- Gửi bằng FCM HTTP v1, message **data-only** đúng 3 khóa `{ "type", "date", "notificationId" }`;
  tiêu đề/nội dung tiếng Việt chung do app tự dựng, không chứa tên học sinh, URL hay số liệu chi tiết.
  Dùng `SendEachAsync` theo batch, xử lý lỗi từng token độc lập; token không bao giờ nằm trong log.

### 4.4. Đọc dữ liệu cho mobile

Mọi endpoint đọc chỉ trả dữ liệu thuộc gia đình của token; thiết bị khác gia đình trả `404` như không
tồn tại.

- `GET /v1/reports/daily?from=&to=` (`read`): `from`/`to` bắt buộc dạng `yyyy-MM-dd`, khoảng tối đa
  **31 ngày** (tính cả hai đầu), ngược/hợp lệ sai → `400 invalid_range`. Trả mảng
  `DailySummaryDto` sắp xếp tăng dần theo `localDate`; mỗi ngày lấy bản ghi có `UpdatedAtUtc` mới nhất
  (nhiều desktop cùng ngày thì bản mới nhất thắng). `timeZoneId` lấy từ `Families`.
- `GET /v1/notifications?before=&limit=` (`read`): mặc định **50**, tối đa **100**, ngoài khoảng →
  `400 invalid_limit`. Sắp xếp `CreatedAtUtc DESC, Id DESC`; `nextBefore` là con trỏ đục
  (base64url của `{utcTicks}:{id:N}`) để lấy trang kế tiếp ổn định kể cả khi có bản ghi mới; con trỏ
  sai → `400 invalid_cursor`. Mỗi item gồm `category`, `status` (`pending`/`sent`/`suppressed`/`failed`),
  `createdAtUtc`, `sentAtUtc`; không có nội dung nhạy cảm.
- `GET /v1/devices` (`read`): danh sách thiết bị trong gia đình kèm `revokedAtUtc`.
- `DELETE /v1/devices/{id}` (`family:admin`): thu hồi bất kỳ thiết bị nào trong gia đình, kể cả thiết
  bị đang gọi (token bị thu hồi có hiệu lực ngay ở request kế tiếp).
- `DELETE /v1/family` (`family:admin`): body `{ "confirmation": "XÓA" }` (so khớp chính xác, sai →
  `400 confirmation_required`). Xóa cứng ngay trong một transaction theo cascade (thiết bị, token,
  push registration, preference, summary, sync event, notification, pairing session) và ghi audit ẩn
  danh `FamilyDeleted` với `FamilyId`/`ActorDeviceId` = NULL.

## 5. Mô hình dữ liệu PostgreSQL

| Bảng | Cột chính | Ghi chú |
|---|---|---|
| `Families` | `Id`, `CreatedAtUtc`, `TimeZoneId`, `DeletedAtUtc` | một gia đình nhiều thiết bị |
| `Devices` | `Id`, `FamilyId`, `Kind` (`Desktop`/`Mobile`), `DisplayName`, `Platform`, `AppVersion`, `CreatedAtUtc`, `LastSeenAtUtc`, `RevokedAtUtc` | không lưu token thô |
| `DeviceTokens` | `Id`, `DeviceId`, `TokenHash char(64)`, `Scopes`, `CreatedAtUtc`, `LastUsedAtUtc`, `RevokedAtUtc` | hash SHA-256, unique toàn cục |
| `PairingSessions` | `Id`, `FamilyId`, `DesktopDeviceId`, `CodeHash char(64)`, `CreatedAtUtc`, `ExpiresAtUtc`, `ClaimedAtUtc`, `ClaimedByDeviceId`, `AttemptCount` | unique partial trên `CodeHash` khi `ClaimedAtUtc IS NULL` |
| `PushRegistrations` | `Id`, `DeviceId`, `FcmToken`, `Platform`, `UpdatedAtUtc`, `InvalidatedAtUtc` | token FCM là dữ liệu nhạy cảm |
| `NotificationPreferences` | `FamilyId`, `AttentionAlert`, `DailySummary`, `ProtectionOff`, `SecurityAlert`, `SecurityAlertBypassQuietHours`, `QuietStartMinutes`, `QuietEndMinutes`, `DailySummaryMinutes`, `TimeZoneId`, `UpdatedAtUtc` | một bản/gia đình |
| `DailySummaries` | `Id`, `FamilyId`, `DesktopDeviceId`, `LocalDate date`, `SessionsCount`, các cột `*Seconds`, `BlockedAttempts`, `AttentionPercent double?`, `CoveragePercent double?`, `UpdatedAtUtc` | unique `(FamilyId, DesktopDeviceId, LocalDate)` cho upsert; `UpdatedAtUtc` = `generatedAtUtc` mới nhất |
| `SyncEvents` | `Id`, `FamilyId`, `ClientEventId`, `Type`, `ReceivedAtUtc`, `ProcessedAtUtc`, `Status` | unique `(FamilyId, ClientEventId)`; payload xử lý ngay, không lưu |
| `Notifications` | `Id`, `FamilyId`, `Category`, `DedupeKey`, `LocalDate?`, `CreatedAtUtc`, `SentAtUtc`, `Status`, `FailureCode`, `AttemptCount`, `LastAttemptAtUtc?` | unique `(FamilyId, DedupeKey)`; retry state cho push worker |
| `CloudAuditLogs` | `Id`, `FamilyId?`, `ActorDeviceId?`, `Action`, `MetadataJson jsonb`, `CreatedAtUtc` | FK `SET NULL` để giữ bản ghi ẩn danh sau khi xóa |

Quan hệ cascade từ `Families` xuống các bảng con; riêng `CloudAuditLogs` dùng `ON DELETE SET NULL` để
bản ghi audit còn lại nhưng không còn định danh gia đình/thiết bị. Check constraint bảo vệ dữ liệu:
giây ≥ 0, phần trăm 0–100 hoặc NULL, quiet minutes 0–1439 và `Kind`/`Type`/`Status`/`Category` chỉ nhận
giá trị enum đã định nghĩa.

Migration `InitialCloudSchema` nằm trong `cloud/EduFocus.Cloud.Data/Migrations`. Startup chỉ tự chạy
migration khi `EDUFOCUS_CLOUD_AUTO_MIGRATE=true`; mặc định tắt và production chạy migration tường minh
(`dotnet ef database update`).

## 6. Thời hạn lưu trên cloud

| Dữ liệu | Thời hạn | Cơ chế |
|---|---|---|
| `PairingSessions` | 24 giờ sau khi hết hạn | job retention |
| `SyncEvents` | 7 ngày | job retention |
| `Notifications` | 30 ngày | job retention |
| `DailySummaries` | 180 ngày (theo `UpdatedAtUtc`) | job retention |
| `CloudAuditLogs` | 365 ngày | job retention (trừ khi pháp luật yêu cầu khác) |
| Gia đình đã xóa mềm (`DeletedAtUtc`) | 30 ngày | job retention, xóa cứng + cascade |
| Tài khoản/token | tới khi thu hồi hoặc xóa gia đình | cascade |

Job bảo trì `CloudMaintenanceWorker` chạy mỗi `Jobs:IntervalSeconds` (mặc định 60 giây) và dùng
PostgreSQL advisory lock (`pg_try_advisory_lock`) để chỉ một instance xử lý tại một thời điểm; instance
khác bỏ qua lượt đó. Mỗi lượt job tạo notification `daily_summary` khi đã tới `DailySummaryMinutes`
theo múi giờ gia đình, preference bật, không trong quiet hours và đã có summary của ngày; dedupe
`(FamilyId, DedupeKey)` bảo đảm mỗi ngày một lần. `DELETE /v1/family` xóa cứng đồng bộ ngay khi nhận
yêu cầu; retention chỉ dọn các bản ghi còn sót của gia đình đã xóa mềm (nếu có).

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

- `cloud/docker-compose.prod.yml`: `caddy` (TLS tự động, chỉ service này mở 80/443) + `api` +
  `postgres:16-alpine`; API/Postgres chỉ nằm trong mạng Docker nội bộ. `cloud/docker-compose.yml` chỉ
  dùng cho dev/demo LAN.
- `Dockerfile` multi-stage, chạy user không phải root, healthcheck `/healthz`; `.dockerignore` loại
  `.env`, `secrets/`, service account và backup khỏi build context. CI build image và kiểm tra image
  không chứa file bí mật.
- Biến chính: `ConnectionStrings__Postgres`, `EDUFOCUS_CLOUD_PUBLIC_URL` (hoặc `Cloud__PublicBaseUrl`),
  `Push__Enabled`, `Push__ProjectId`, `Push__ServiceAccountPath` hoặc `Push__ServiceAccountJson`
  (secret mount), `ASPNETCORE_URLS`; rate limit tinh chỉnh qua `RateLimiting__…`, chu kỳ gửi qua
  `Push__PollSeconds`.
  Job bảo trì: `Jobs__Enabled` (mặc định `true`), `Jobs__IntervalSeconds` (60), thời hạn retention
  `Jobs__PairingSessionHours` (24), `Jobs__SyncEventDays` (7), `Jobs__NotificationDays` (30),
  `Jobs__DailySummaryDays` (180), `Jobs__CloudAuditDays` (365), `Jobs__SoftDeletedFamilyDays` (30).
  Service account JSON **không** commit, không nằm trong image.
- Migration: production **không** tự migrate khi start; chạy tường minh
  `docker compose -f docker-compose.prod.yml run --rm api --migrate` trước khi đưa API mới lên.
  `EDUFOCUS_CLOUD_AUTO_MIGRATE=true` chỉ dành cho dev.
- HTTPS/HSTS tại Caddy + API; chỉ tin `X-Forwarded-For/Proto` khi `Cloud__TrustForwardedHeaders=true`
  và API không publish port. Body request giới hạn 1 MB toàn cục, sync 64 KB; CORS không bật.
- Log JSON ra stdout (rotation ở Docker), redaction `token`/`authorization`/FCM token; không log body.
- Backup: `cloud/scripts/backup.sh` (pg_dump hằng ngày, giữ 7 bản, tùy chọn mã hóa gpg) và
  `cloud/scripts/restore.sh`; quy trình chi tiết và biên bản diễn tập restore trong
  [cloud-deploy.md](./cloud-deploy.md).
- Health check `/healthz`; self-host: chủ máy chủ chịu trách nhiệm TLS, backup PostgreSQL, cập nhật
  image, và tuân thủ nghĩa vụ dữ liệu cá nhân tương ứng. Runbook đầy đủ:
  [cloud-deploy.md](./cloud-deploy.md).

## 9. Giới hạn đã biết

- Push phụ thuộc FCM/Google; thiết bị không có Google Play Services sẽ không nhận được thông báo.
- Độ trễ phụ thuộc mạng và chính sách tiết kiệm pin của Android; cảnh báo có thể đến muộn.
- Cloud không có timeline chi tiết; app mobile chỉ hiển thị tổng hợp ngày và danh sách cảnh báo.
- Không hỗ trợ đồng bộ hai chiều cấu hình blocklist từ điện thoại trong bản này.
- Nếu phụ huynh tự host, chất lượng dịch vụ và trách nhiệm pháp lý thuộc về họ.

## 10. Tài liệu liên quan

- [ADR-20](./adr/ADR-20-optional-cloud-relay.md) — quyết định và đánh đổi.
- [cloud-deploy.md](./cloud-deploy.md) — runbook triển khai, backup/restore, rollback, demo LAN.
- [privacy-notice.md](./privacy-notice.md) — thông báo quyền riêng tư bản 2.0.
- [dpia.md](./dpia.md) — data map và đánh giá rủi ro bổ sung cho cloud.
- [use_cases.md](./use_cases.md) — UC15–UC18.
- [system_architecture.md](./system_architecture.md) §10–§11 — vị trí cloud trong kiến trúc.
