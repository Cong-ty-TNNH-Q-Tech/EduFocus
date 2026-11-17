# EduFocus Cloud (Phase 8)

Backend tùy chọn cho cloud relay: ghép nối thiết bị, nhận sự kiện tổng hợp, gửi thông báo đẩy qua
FCM và trả báo cáo tóm tắt cho app phụ huynh. Cloud **mặc định tắt** phía desktop; repo này không
chứa secret và không chứa dữ liệu học tập.

- Kiến trúc và API: [../docs/cloud-backend.md](../docs/cloud-backend.md)
- Quyết định: [../docs/adr/ADR-20-optional-cloud-relay.md](../docs/adr/ADR-20-optional-cloud-relay.md)

## Yêu cầu

- .NET 10 SDK (10.0.200+) để build/test.
- Docker + Docker Compose để chạy PostgreSQL/API.
- PostgreSQL 16 nếu chạy API trực tiếp, không qua Docker.

## Chạy local bằng Docker Compose

```bash
cd cloud
cp .env.example .env        # đổi mật khẩu nếu không dùng máy dev
docker compose up --build
curl http://localhost:8080/healthz
```

`/healthz` trả `{"status":"healthy","checkedAtUtc":"..."}` khi API kết nối được PostgreSQL; trả
`503` và `"unhealthy"` khi database không sẵn sàng.

Để bật TLS bằng Caddy (tự xin chứng chỉ khi có domain công khai):

```bash
EDUFOCUS_DOMAIN=cloud.example.com docker compose --profile tls up -d
```

Caddy lắng nghe 80/443 và reverse proxy tới `api:8080`. Với `localhost`, Caddy dùng chứng chỉ nội
bộ; đặt `EDUFOCUS_CLOUD_PUBLIC_URL` đúng scheme/domain mà desktop và mobile sẽ gọi.

## Chạy API trực tiếp

```bash
cd cloud
export ConnectionStrings__Postgres="Host=localhost;Port=5432;Database=edufocus_cloud;Username=edufocus;Password=edufocus_dev"
export EDUFOCUS_CLOUD_PUBLIC_URL="http://localhost:8080"
dotnet run --project EduFocus.Cloud.Api
```

Ở môi trường `Development`, nếu không có connection string, API dùng fallback
`Host=localhost;Port=5432;Database=edufocus_cloud;Username=edufocus;Password=edufocus_dev`. Môi
trường khác **bắt buộc** cấu hình `ConnectionStrings__Postgres`.

## Build và test

```bash
cd cloud
dotnet restore EduFocus.Cloud.slnx
dotnet build EduFocus.Cloud.slnx -c Release
dotnet test EduFocus.Cloud.slnx -c Release
```

Test tích hợp database chỉ chạy khi đặt `EDUFOCUS_CLOUD_TEST_DB`:

```bash
EDUFOCUS_CLOUD_TEST_DB="Host=localhost;Port=5432;Database=edufocus_cloud_test;Username=edufocus;Password=edufocus_dev" \
  dotnet test EduFocus.Cloud.slnx -c Release
```

Test này tạo lại schema từ migration, kiểm tra unique `(FamilyId, ClientEventId)`, upsert
`DailySummaries`, partial unique index của pairing code, cascade delete, audit ẩn danh, luồng ghép
nối đầy đủ qua HTTP (tạo phiên → claim → trạng thái → link → xoay/thu hồi token), ingest idempotent
(áp dụng/trùng/từ chối, latest-wins, giới hạn batch) và rate limit 429.

## Migration

```bash
dotnet tool restore
dotnet ef database update --project cloud/EduFocus.Cloud.Data --startup-project cloud/EduFocus.Cloud.Data
dotnet ef migrations add <TenMigration> --project cloud/EduFocus.Cloud.Data --startup-project cloud/EduFocus.Cloud.Data --output-dir Migrations
```

Startup chỉ tự chạy migration khi `EDUFOCUS_CLOUD_AUTO_MIGRATE=true`; production nên chạy migration
tường minh trước khi deploy.

## Cấu trúc

| Project | Vai trò |
|---|---|
| `EduFocus.Cloud.Api` | Minimal API nhóm `/v1`, `/healthz`, xác thực Bearer thiết bị, rate limit, Serilog JSON |
| `EduFocus.Cloud.Contracts` | DTO/enum dùng chung cho API, desktop và mobile; JSON camelCase |
| `EduFocus.Cloud.Data` | Entity, cấu hình EF Core 10 + Npgsql, `CloudDbContext`, migration `InitialCloudSchema` |
| `EduFocus.Cloud.Tests` | xUnit: serialization contract, test tích hợp schema/pairing/rate limit tùy chọn |

Đã có ở #81–#82: pairing (`/v1/pairing`), xác thực token thiết bị (`efd_`/`efm_`), `/v1/link/status`,
`/v1/devices/{id}`, `/v1/devices/token/rotate` và ingest `/v1/sync/events` (batch ≤50, ≤64 KB,
idempotent, latest-wins daily summary). Push, preferences và báo cáo bổ sung ở các issue #83–#84.

## Biến môi trường

| Biến | Bắt buộc | Mô tả |
|---|---|---|
| `ConnectionStrings__Postgres` | Production | Chuỗi kết nối PostgreSQL |
| `EDUFOCUS_CLOUD_PUBLIC_URL` | Khuyến nghị | Base URL công khai dùng để tạo QR/pairing URI |
| `EDUFOCUS_CLOUD_AUTO_MIGRATE` | Không | `true` để startup tự chạy migration; mặc định tắt |
| `ASPNETCORE_ENVIRONMENT` | Không | `Development` bật OpenAPI và fallback DB |
| `ASPNETCORE_URLS` | Không | Mặc định trong Docker là `http://+:8080` |
| `POSTGRES_DB`, `POSTGRES_USER`, `POSTGRES_PASSWORD` | Compose | Thông tin PostgreSQL |
| `EDUFOCUS_DOMAIN` | Profile `tls` | Domain cho Caddy |

## Bảo mật

- Không commit `.env`, service account FCM, keystore hay bất kỳ secret nào. `.env.example` chỉ chứa
  giá trị dev.
- Log dùng Serilog JSON; enricher redacts `token`, `authorization`, `pushToken`, `fcmToken`, `code`,
  `password`, `secret` và các tên tương tự. Không log body của pairing/ingest.
- PostgreSQL không public; chỉ API trong cùng mạng Compose truy cập được.
- API chỉ chấp nhận HTTPS khi chạy sau Caddy/reverse proxy; health check nội bộ dùng HTTP trong mạng
  Docker.
- Token thiết bị đục 32 byte (`efd_`/`efm_`), server chỉ lưu SHA-256 hex, so sánh constant-time,
  không log token. Pairing code chỉ lưu hash và dùng một lần.
- Rate limit fixed-window theo IP/token: tạo phiên 10/giờ, claim 20/giờ, chung 120/phút; trả 429 kèm
  `Retry-After`.

## CI

`.github/workflows/cloud-ci.yml` chạy restore/build/test với PostgreSQL 16 service, build Docker
image ở mọi PR (không push) và publish image lên GHCR khi tag hoặc chạy thủ công. Workflow này độc
lập với CI desktop, không ảnh hưởng `EduFocus.slnx`.
