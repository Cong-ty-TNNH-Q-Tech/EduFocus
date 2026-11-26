# EduFocus Cloud (Phase 8)

Backend tùy chọn cho cloud relay: ghép nối thiết bị, nhận sự kiện tổng hợp, gửi thông báo đẩy qua
kết nối WebSocket (WSS) tích hợp, hoặc Web Push/UnifiedPush (ví dụ ntfy) làm fallback, và trả báo
cáo tóm tắt cho app phụ huynh. Cloud **mặc định tắt** phía desktop; repo này không chứa secret và
không chứa dữ liệu học tập.

- Kiến trúc và API: [../docs/cloud-backend.md](../docs/cloud-backend.md)
- Triển khai production (TLS, backup/restore, rollback, demo LAN): [../docs/cloud-deploy.md](../docs/cloud-deploy.md)
- Quyết định: [../docs/adr/ADR-20-optional-cloud-relay.md](../docs/adr/ADR-20-optional-cloud-relay.md)

## Yêu cầu

- .NET 10 SDK (10.0.200+) để build/test.
- Docker + Docker Compose để chạy API. Backend dùng **SQLite** một file, không cần PostgreSQL.

## Chạy local bằng Docker Compose

```bash
cd cloud
cp .env.example .env
docker compose up --build -d
curl http://localhost:8080/healthz
```

`/healthz` trả `{"status":"healthy","checkedAtUtc":"..."}` khi API mở được SQLite; trả `503` và
`"unhealthy"` khi database không sẵn sàng. Database nằm trong volume `edufocus-data` tại
`/data/edufocus.db`; migration tự chạy khi `EDUFOCUS_CLOUD_AUTO_MIGRATE=true` (mặc định).

Để bật TLS bằng Caddy (tự xin chứng chỉ khi có domain công khai):

```bash
EDUFOCUS_DOMAIN=cloud.example.com docker compose --profile tls up -d
```

Caddy lắng nghe 80/443 và reverse proxy tới `api:8080`. Với `localhost`, Caddy dùng chứng chỉ nội
bộ; đặt `EDUFOCUS_CLOUD_PUBLIC_URL` đúng scheme/domain mà desktop và mobile sẽ gọi. Khi dùng Caddy,
giữ `API_BIND=127.0.0.1` và `CLOUD_TRUST_FORWARDED_HEADERS=true`.

## Chạy API trực tiếp

```bash
cd cloud
export ConnectionStrings__Sqlite="Data Source=edufocus-cloud.db;Foreign Keys=True;Default Timeout=30"
export EDUFOCUS_CLOUD_PUBLIC_URL="http://localhost:8080"
dotnet run --project EduFocus.Cloud.Api
```

Ở môi trường `Development`, nếu không có connection string, API dùng fallback
`Data Source=edufocus-cloud.db;Foreign Keys=True;Default Timeout=30`. Môi trường khác **bắt buộc**
cấu hình `ConnectionStrings__Sqlite`.

## Build và test

```bash
cd cloud
dotnet restore EduFocus.Cloud.slnx
dotnet build EduFocus.Cloud.slnx -c Release
dotnet test EduFocus.Cloud.slnx -c Release
```

Test tích hợp tự tạo database SQLite tạm (không cần service ngoài). Có thể đặt
`EDUFOCUS_CLOUD_TEST_DB` thành một connection string SQLite khác nếu muốn giữ lại file để kiểm tra.

Test tạo lại schema từ migration, kiểm tra unique `(FamilyId, ClientEventId)`, upsert
`DailySummaries`, partial unique index của pairing code, cascade delete, audit ẩn danh, luồng ghép
nối đầy đủ qua HTTP (tạo phiên → claim → trạng thái → link → xoay/thu hồi token), ingest idempotent
(áp dụng/trùng/từ chối, latest-wins, giới hạn batch) và rate limit 429.

## Migration

```bash
dotnet tool restore
dotnet ef database update --project EduFocus.Cloud.Data --startup-project EduFocus.Cloud.Api
dotnet ef migrations add <TenMigration> --project EduFocus.Cloud.Data --startup-project EduFocus.Cloud.Api --output-dir Migrations
```

Startup tự chạy migration khi `EDUFOCUS_CLOUD_AUTO_MIGRATE=true`. Với compose mặc định, biến này
bật để deploy một lệnh; có thể đặt `false` và chạy migration tường minh trước khi rollout:

```bash
docker compose run --rm api --migrate
```

## Cấu trúc

| Project | Vai trò |
|---|---|
| `EduFocus.Cloud.Api` | Minimal API nhóm `/v1`, `/healthz`, xác thực Bearer thiết bị, rate limit, Serilog JSON |
| `EduFocus.Cloud.Contracts` | DTO/enum dùng chung cho API, desktop và mobile; JSON camelCase |
| `EduFocus.Cloud.Data` | Entity, cấu hình EF Core 10 + SQLite, `CloudDbContext`, migration `InitialCloudSchema` |
| `EduFocus.Cloud.Tests` | xUnit: serialization contract, test tích hợp schema/pairing/rate limit |

Đã có: pairing (`/v1/pairing`), xác thực token thiết bị (`efd_`/`efm_`), `/v1/link/status`,
`/v1/devices`, `/v1/devices/token/rotate`, ingest `/v1/sync/events` (batch ≤50, ≤64 KB, idempotent,
latest-wins), push WSS (`GET /v1/push/stream`) + Web Push/UnifiedPush fallback + preferences (#83),
`GET /v1/push/config` và các API đọc cho mobile (#84: báo cáo ngày, notifications có cursor, xóa
thiết bị/gia đình). App phụ huynh không cần cài ntfy khi cloud bật `Push__WebSocketEnabled`.

## Biến môi trường

| Biến | Bắt buộc | Mô tả |
|---|---|---|
| `ConnectionStrings__Sqlite` | Production | Chuỗi kết nối SQLite, ví dụ `Data Source=/data/edufocus.db;Foreign Keys=True` |
| `EDUFOCUS_CLOUD_PUBLIC_URL` | Khuyến nghị | Base URL công khai dùng để tạo QR/pairing URI |
| `EDUFOCUS_CLOUD_AUTO_MIGRATE` | Không | `true` để startup tự chạy migration; compose mặc định bật |
| `ASPNETCORE_ENVIRONMENT` | Không | `Development` bật OpenAPI và fallback DB |
| `ASPNETCORE_URLS` | Không | Mặc định trong Docker là `http://+:8080` |
| `Cloud__TrustForwardedHeaders` | Không | `true` khi chạy sau Caddy nội bộ; mặc định compose là `true` |
| `Push__WebSocketEnabled` | Không | Bật kết nối WSS cho app phụ huynh; compose mặc định `true` |
| `Push__WebSocketHeartbeatSeconds` | Không | Chu kỳ heartbeat WSS, mặc định `20` (5–120) |
| `Push__Enabled`, `Push__VapidPublicKey`, `Push__VapidPrivateKey`, `Push__VapidSubject`, `Push__AllowedEndpointHosts__0` | Khi dùng fallback | VAPID và host endpoint UnifiedPush/ntfy |
| `Jobs__*`, `RateLimiting__*` | Không | Chu kỳ retention và giới hạn request |
| `API_BIND`, `API_PORT` | Compose | Địa chỉ/port publish của API; mặc định `127.0.0.1:8080` |
| `EDUFOCUS_DOMAIN` | Profile `tls` | Domain cho Caddy |

## Bảo mật

- Không commit `.env`, VAPID private key, keystore hay bất kỳ secret nào. `.env.example` chỉ chứa
  giá trị dev.
- Log dùng Serilog JSON; enricher redacts `token`, `authorization`, `endpoint`, `p256dh`, `auth`,
  `code`, `password`, `secret` và các tên tương tự. Không log body của pairing/ingest.
- Compose ánh xạ `PUSH_*` sang `Push__*`; thêm host allowlist thứ hai bằng
  `Push__AllowedEndpointHosts__1`, v.v.
- SQLite chỉ nằm trong volume Docker; API không publish port ra ngoài khi dùng Caddy (`API_BIND` mặc
  định `127.0.0.1`). Volume cần được backup và bảo vệ.
- API chỉ chấp nhận HTTPS khi chạy sau Caddy/reverse proxy; health check nội bộ dùng HTTP trong mạng
  Docker.
- Token thiết bị đục 32 byte (`efd_`/`efm_`), server chỉ lưu SHA-256 hex, so sánh constant-time,
  không log token. Pairing code chỉ lưu hash và dùng một lần.
- Rate limit fixed-window theo IP/token: tạo phiên 10/giờ, claim 20/giờ, chung 120/phút; trả 429 kèm
  `Retry-After`.

## CI

`.github/workflows/cloud-ci.yml` chạy restore/build/test trên SQLite tạm, build Docker image ở mọi
PR (không push) và publish image lên GHCR khi tag hoặc chạy thủ công. Workflow này độc lập với CI
desktop, không ảnh hưởng `EduFocus.slnx`.
