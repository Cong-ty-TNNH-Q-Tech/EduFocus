# Triển khai cloud relay (Phase 8)

Tài liệu vận hành backend cloud cho phụ huynh: đóng gói Docker, TLS, cấu hình bí mật, backup/restore,
cập nhật/rollback và chế độ demo LAN. Không đưa domain thật, mật khẩu hay service account vào repo;
mọi giá trị dưới đây là placeholder.

## 1. Kiến trúc triển khai

```
Internet ──80/443──> caddy (TLS tự động, Let's Encrypt)
                         │ mạng `edge`
                         ▼
                      api :8080 (ASP.NET Core, không publish port)
                         │ mạng `backend` (internal)
                         ▼
                      postgres:16-alpine (volume pgdata)
```

- Chỉ Caddy mở port **80/443** (kèm **443/UDP** cho HTTP/3). API và PostgreSQL chỉ nằm trong mạng
  Docker nội bộ; `backend` đặt `internal: true`.
- `docker-compose.prod.yml` là stack production; `docker-compose.yml` chỉ dùng cho dev/demo LAN.
- API nhận `X-Forwarded-For/Proto` từ Caddy khi `Cloud__TrustForwardedHeaders=true` (chỉ bật trong
  compose prod vì API không publish port). HSTS bật ở môi trường non-Development.
- Log JSON (Serilog compact) ra stdout; Docker giới hạn `10m × 5` mỗi service.

## 2. Yêu cầu hạ tầng

| Hạng mục | Tối thiểu | Khuyến nghị |
|---|---|---|
| VPS | 2 vCPU, 2 GB RAM, 20 GB SSD | 2 vCPU, 4 GB RAM, 40 GB SSD |
| OS | Ubuntu 22.04/24.04 LTS | cùng |
| Docker | Engine 24+, Compose plugin v2 | bản mới nhất |
| Domain | 1 bản ghi A/AAAA trỏ về VPS | dùng subdomain riêng, ví dụ `cloud.example.com` |
| Firewall | mở TCP 80, 443 và UDP 443 | đóng toàn bộ port khác, kể cả 8080/5432 |
| Outbound | HTTPS ra ngoài | cần cho ACME (Let's Encrypt) và FCM |

Không cần mở port cho API/Postgres. Nếu dùng firewall cloud (Security Group), chỉ mở 22 (SSH, giới hạn
IP quản trị), 80, 443.

## 3. Chuẩn bị trên VPS

```bash
sudo apt update && sudo apt install -y docker.io docker-compose-v2 git
sudo systemctl enable --now docker
sudo usermod -aG docker "$USER"   # đăng nhập lại để có hiệu lực

git clone https://github.com/Cong-ty-TNNH-Q-Tech/EduFocus.git
cd EduFocus/cloud
cp .env.example .env
```

Sửa `.env` (quyền `chmod 600 .env`):

- `POSTGRES_PASSWORD`: chuỗi ngẫu nhiên dài (`openssl rand -base64 32`).
- `EDUFOCUS_CLOUD_PUBLIC_URL=https://<domain>` (không có `/` cuối).
- `EDUFOCUS_DOMAIN=<domain>` để Caddy xin chứng chỉ.
- `PUSH_ENABLED=false` cho lần deploy đầu; bật sau khi đã có Firebase.
- Các biến `JOBS_*`, `RATE_LIMIT_*` giữ mặc định trừ khi có lý do chỉnh.

Nếu bật push:

```bash
mkdir -p secrets && chmod 700 secrets
# tải service account JSON từ Firebase Console vào máy, rồi:
install -m 600 /duong/dan/firebase-service-account.json secrets/firebase-service-account.json
```

Compose mount thư mục `./secrets` vào `/run/secrets` (read-only). Tên file bắt buộc là
`firebase-service-account.json`; API đọc qua `Push__ServiceAccountPath`. Khi `PUSH_ENABLED=false`,
file này không bắt buộc và không được đọc.

`.env`, `secrets/`, `backups/`, `*.dump` đều nằm trong `.gitignore`. Không chạy `git add -f`.

## 4. Triển khai lần đầu

```bash
cd EduFocus/cloud

# 1) Build image (hoặc đặt CLOUD_API_IMAGE trỏ tới image GHCR đã publish)
docker compose -f docker-compose.prod.yml build

# 2) Khởi động PostgreSQL và chờ healthy
docker compose -f docker-compose.prod.yml up -d postgres
docker compose -f docker-compose.prod.yml ps

# 3) Chạy migration tường minh (production không tự migrate khi start)
docker compose -f docker-compose.prod.yml run --rm api --migrate

# 4) Khởi động API + Caddy
docker compose -f docker-compose.prod.yml up -d
docker compose -f docker-compose.prod.yml ps
```

Caddy xin chứng chỉ TLS trong lần request đầu tới domain; theo dõi:

```bash
docker compose -f docker-compose.prod.yml logs -f caddy
```

`EDUFOCUS_CLOUD_AUTO_MIGRATE` phải là `false` (mặc định) ở production. Lệnh `--migrate` chạy migration
rồi thoát, không start web server hay background job.

## 5. Smoke test sau deploy

```bash
DOMAIN=cloud.example.com
curl -fsS "https://$DOMAIN/healthz"          # {"status":"healthy",...}
curl -fsS "https://$DOMAIN/v1/"              # tên + version API
```

Sau đó pair thử bằng app desktop:

1. Desktop tạo phiên ghép nối → QR chứa `https://<domain>/v1/...`.
2. Mobile quét QR, claim thành công.
3. Nếu `PUSH_ENABLED=true`: gửi thử một cảnh báo từ desktop, kiểm tra điện thoại nhận thông báo.
4. Mở màn báo cáo trên mobile, xác nhận dữ liệu ngày hôm nay hiển thị.

Kiểm tra API từ chối cấu hình thiếu: log khởi động sẽ báo rõ nếu thiếu connection string hoặc thiếu
`Push__ProjectId`/service account khi `Push__Enabled=true`.

## 6. Vận hành thường ngày

```bash
cd EduFocus/cloud
docker compose -f docker-compose.prod.yml ps
docker compose -f docker-compose.prod.yml logs --since 1h api
docker compose -f docker-compose.prod.yml logs --since 1h caddy
df -h                                  # dung lượng đĩa
docker system df                       # image/volume chiếm chỗ
```

- Log là JSON một dòng, đã redaction các trường `token`, `authorization`, `fcmToken`, `code`,
  `password`, `secret`. Không bật log body request; log Serilog request không chứa payload.
- Healthcheck API gọi `/healthz` (kiểm tra luôn PostgreSQL) mỗi 30 giây.
- Job bảo trì chạy mỗi `JOBS_INTERVAL_SECONDS` (60): tạo notification tổng kết ngày và dọn dữ liệu
  hết hạn. Xem log `Cloud maintenance finished`.

## 7. Cập nhật phiên bản và rollback

Thứ tự an toàn: **backup → migrate → deploy image mới → smoke test**.

```bash
cd EduFocus/cloud

# 1) Backup trước khi đổi phiên bản
./scripts/backup.sh

# 2) Nếu bản mới có migration: chạy với image mới trước khi đổi API đang chạy
CLOUD_API_IMAGE=ghcr.io/cong-ty-tnnh-q-tech/edufocus-cloud-api:<sha-moi> \
  docker compose -f docker-compose.prod.yml run --rm api --migrate

# 3) Cập nhật image và recreate API
sed -i 's|^CLOUD_API_IMAGE=.*|CLOUD_API_IMAGE=ghcr.io/cong-ty-tnnh-q-tech/edufocus-cloud-api:<sha-moi>|' .env
docker compose -f docker-compose.prod.yml pull api
docker compose -f docker-compose.prod.yml up -d api

# 4) Smoke test
curl -fsS "https://$EDUFOCUS_DOMAIN/healthz"
```

Rollback image: đặt `CLOUD_API_IMAGE` về tag cũ và `docker compose -f docker-compose.prod.yml up -d api`.
Migration **không tự rollback**; nếu bản mới đã chạy migration không tương thích ngược, phải restore
backup theo mục 10. Vì vậy luôn backup trước khi migrate.

## 8. Xoay token và thu hồi thiết bị

Khi nghi lộ token thiết bị:

- **Thu hồi một thiết bị** (mobile token có `family:admin`): `DELETE /v1/devices/{id}`. Token và push
  registration của thiết bị bị vô hiệu ngay; thiết bị đó nhận 401 ở request kế tiếp.
- **Xoay token của chính thiết bị**: `POST /v1/devices/token/rotate`, lưu token mới, token cũ mất hiệu lực.
- **Nghi lộ toàn bộ**: dùng mobile admin gọi `DELETE /v1/family` để xóa sạch dữ liệu và ghép nối lại.
- **Khẩn cấp ở DB** (khi không còn token admin tin cậy):

  ```bash
  docker compose -f docker-compose.prod.yml exec -T postgres psql -U edufocus -d edufocus_cloud \
    -c 'UPDATE "DeviceTokens" SET "RevokedAtUtc" = now() WHERE "RevokedAtUtc" IS NULL;'
  ```

  Mọi thiết bị phải tạo phiên ghép nối mới. Ghi lại thời điểm và lý do vào nhật ký vận hành.
- **Service account Firebase** bị lộ: thu hồi key trong Google Cloud Console, tạo key mới, thay file
  `secrets/firebase-service-account.json`, `docker compose ... up -d api`. FCM token cũ vẫn dùng được
  nhưng nên yêu cầu phụ huynh mở lại app để đăng ký token mới.

## 9. Backup định kỳ

`scripts/backup.sh`:

- `pg_dump --format=custom` từ container PostgreSQL, ghi vào `backups/` với quyền `600`.
- Giữ **7 bản mới nhất** (`BACKUP_KEEP`), xóa bản cũ hơn.
- Nếu đặt `BACKUP_PASSPHRASE`, file được mã hóa AES-256 bằng `gpg` và bản thô bị xóa.
- Tên file: `edufocus-YYYYMMDDTHHMMSSZ.dump[.gpg]`.

Cron chạy hằng ngày (ví dụ 02:00 giờ máy chủ):

```cron
0 2 * * * cd /opt/EduFocus/cloud && ./scripts/backup.sh >> /var/log/edufocus-backup.log 2>&1
```

Khuyến nghị:

- Cất passphrase backup **tách biệt** khỏi VPS (password manager của công ty).
- Đồng bộ `backups/` sang nơi khác (object storage hoặc máy backup) và kiểm tra định kỳ.
- Đặt `umask 077` như script; không để backup trong thư mục web public.
- Thử restore mỗi tháng một lần vào database tạm.

## 10. Restore

`scripts/restore.sh <file> [target-db]` dừng API, xóa và tạo lại database đích, rồi `pg_restore`.

Khôi phục vào database tạm để kiểm tra trước (không ảnh hưởng production):

```bash
cd EduFocus/cloud
./scripts/restore.sh backups/edufocus-20261117T020000Z.dump.gpg edufocus_cloud_restore
docker compose -f docker-compose.prod.yml exec -T postgres \
  psql -U edufocus -d edufocus_cloud_restore -c 'SELECT count(*) FROM "Families";'
# kiểm tra xong thì xóa DB tạm
docker compose -f docker-compose.prod.yml exec -T postgres \
  psql -U edufocus -d postgres -c 'DROP DATABASE edufocus_cloud_restore;'
```

Khôi phục đè production (chỉ khi có sự cố):

```bash
./scripts/restore.sh backups/edufocus-20261117T020000Z.dump.gpg edufocus_cloud
curl -fsS "https://$EDUFOCUS_DOMAIN/healthz"
```

Lưu ý:

- Restore là thao tác phá hủy database đích; kiểm tra kỹ tên `target-db`.
- Script không tự chạy migration sau restore; bản dump đã chứa `__EFMigrationsHistory`. Nếu phục hồi
  từ bản backup cũ hơn phiên bản API đang chạy, chạy `--migrate` sau restore.
- API được start lại ở cuối script; nếu restore lỗi, API vẫn dừng để tránh ghi dữ liệu lỗi.

### Biên bản diễn tập restore

| Mục | Kết quả |
|---|---|
| Ngày | 2026-11-17 |
| Môi trường | Docker cục bộ, `postgres:16-alpine` (cùng image production) |
| Dữ liệu mẫu | 1 family, 1 desktop device, 1 notification; 3 migration |
| Backup | `pg_dump --format=custom --no-owner`, 30 KB |
| Restore | `CREATE DATABASE edufocus_restore_rehearsal` + `pg_restore --no-owner --exit-on-error`, thoát mã 0 |
| Kiểm tra | Đếm 1/1/1 khớp bản gốc, `__EFMigrationsHistory` = 3, `DisplayName` và `DedupeKey` đúng |
| Kết quả | **PASS** — dọn database tạm và file dump sau khi kiểm tra |

Lệnh diễn tập tương đương script (dùng container test cục bộ thay vì tên service compose):

```bash
docker exec <pg-container> pg_dump -U edufocus -d edufocus_cloud_test --format=custom --no-owner > backup.dump
docker exec <pg-container> psql -U edufocus -d postgres -c 'CREATE DATABASE edufocus_restore_rehearsal;'
docker exec -i <pg-container> pg_restore -U edufocus -d edufocus_restore_rehearsal --no-owner --exit-on-error < backup.dump
```

## 11. Xử lý sự cố

**API không khởi động** — `docker compose logs api`:

- `PostgreSQL connection string is required` → thiếu `ConnectionStrings__Postgres`; kiểm tra `.env`
  và biến trong compose.
- `Push:ProjectId is required when push is enabled` / `ServiceAccount... is required` → bật push nhưng
  thiếu cấu hình; sửa `.env` hoặc đặt `PUSH_ENABLED=false` tạm thời.
- `Cloud:PublicBaseUrl must be an absolute URL` → `EDUFOCUS_CLOUD_PUBLIC_URL` sai định dạng.

**Caddy trả 502** — API chưa healthy hoặc sai mạng:

```bash
docker compose -f docker-compose.prod.yml ps
docker compose -f docker-compose.prod.yml logs --since 10m api
# API không publish port; chạy healthcheck nội bộ:
docker compose -f docker-compose.prod.yml exec -T api \
  dotnet EduFocus.Cloud.Api.dll --healthcheck; echo "exit=$?"
```

**TLS không cấp được** — kiểm tra DNS trỏ đúng VPS, port 80/443 mở, Caddy log có lỗi ACME. Không chạy
nhiều Caddy cùng domain.

**PostgreSQL đầy đĩa**:

```bash
df -h
docker system df
docker compose -f docker-compose.prod.yml exec -T postgres \
  psql -U edufocus -d edufocus_cloud -c 'SELECT pg_size_pretty(pg_database_size(current_database()));'
```

- Xóa image cũ (`docker image prune`), backup cũ đã đồng bộ offsite, log cũ.
- Dọn dữ liệu hết hạn: kiểm tra job maintenance đang bật; có thể chạy tay bằng cách restart API
  (`Jobs__Enabled=true`).
- Không bao giờ xóa trực tiếp thư mục `pgdata`. Nếu buộc phải mở rộng ổ đĩa, snapshot/backup trước.
- `docker compose down` **không** xóa volume; tránh `down -v` ở production.

**Push không tới điện thoại**:

- `PUSH_ENABLED=true` và service account đúng project Firebase.
- Log API không có lỗi `TokenInvalid` hàng loạt.
- App mobile đã đăng ký token sau khi cài; token cũ bị vô hiệu khi đổi thiết bị.
- Kiểm tra quiet hours/preference của gia đình (mặc định 22:00–06:00, tổng kết 20:30).

## 12. Chế độ demo LAN (chỉ khi demo)

Dùng khi ban giám khảo cần xem trên điện thoại thật trong mạng nội bộ, chưa có domain/TLS:

```bash
cd EduFocus/cloud
cp .env.example .env
# sửa:
#   POSTGRES_PASSWORD=<chuỗi mạnh>
#   EDUFOCUS_CLOUD_PUBLIC_URL=http://<IP-LAN-VPS>:8080
#   ASPNETCORE_ENVIRONMENT=Development
#   API_PORT=8080
#   PUSH_ENABLED=false (hoặc true nếu đã có service account)
docker compose build
docker compose up -d postgres
docker compose run --rm api --migrate
docker compose up -d
ip addr show        # lấy IP LAN, ví dụ 192.168.1.50
```

Điện thoại và máy chạy Docker phải cùng mạng LAN; mở port 8080 trên firewall nội bộ. Trên desktop,
nhập `http://192.168.1.50:8080` làm cloud URL (hoặc để QR tự chứa URL này).

Cảnh báo:

- **Chỉ dùng cho demo**, không dùng dữ liệu thật của gia đình; HTTP không mã hóa token.
- Android có thể chặn cleartext HTTP; nếu app không kết nối được, dùng bản debug cho phép cleartext
  hoặc chuyển sang stack production với domain + TLS.
- Sau demo, tắt stack (`docker compose down`) và xóa `.env`/volume nếu máy không còn dùng.

## 13. Checklist trước demo

- [ ] `docker compose -f docker-compose.prod.yml ps` tất cả healthy.
- [ ] `curl https://<domain>/healthz` trả `healthy`.
- [ ] Pair desktop ↔ mobile thành công, QR dùng đúng domain HTTPS.
- [ ] Điện thoại nhận thông báo cảnh báo (nếu bật push) và mở được báo cáo ngày.
- [ ] Backup gần nhất chạy thành công; biết cách restore.
- [ ] Không có `.env`, service account hay backup nào trong `git status`.

## 14. Tài liệu liên quan

- [cloud-backend.md](./cloud-backend.md) — kiến trúc API, schema, retention, biến môi trường.
- [ADR-20](./adr/ADR-20-optional-cloud-relay.md) — quyết định cloud tùy chọn và đánh đổi.
