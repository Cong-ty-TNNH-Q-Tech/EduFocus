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
`DailySummaries`, partial unique index của pairing code, cascade delete và audit ẩn danh.

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
| `EduFocus.Cloud.Api` | Minimal API nhóm `/v1`, `/healthz`, ProblemDetails, OpenAPI (Development), Serilog JSON |
| `EduFocus.Cloud.Contracts` | DTO/enum dùng chung cho API, desktop và mobile; JSON camelCase |
| `EduFocus.Cloud.Data` | Entity, cấu hình EF Core 10 + Npgsql, `CloudDbContext`, migration `InitialCloudSchema` |
| `EduFocus.Cloud.Tests` | xUnit: serialization contract, cấu hình DB, test tích hợp schema tùy chọn |

API nghiệp vụ (pairing, ingest, push, reports) được bổ sung ở các issue #81–#84.

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
- Token/pairing code và giới hạn rate limit sẽ được triển khai ở issue #81.

## CI

`.github/workflows/cloud-ci.yml` chạy restore/build/test với PostgreSQL 16 service, build Docker
image ở mọi PR (không push) và publish image lên GHCR khi tag hoặc chạy thủ công. Workflow này độc
lập với CI desktop, không ảnh hưởng `EduFocus.slnx`.
