# Triển khai cloud relay (Phase 8)

Tài liệu vận hành backend cloud cho phụ huynh: dùng gói Docker, TLS, cấu hình bí mật, backup/restore,
cập nhật/rollback và chế độ demo LAN. Không đưa domain thật, mật khẩu hay service account vào repo;
mọi giá trị dưới đây là placeholder.

## 1. Kiến trúc triển khai

```text
Internet ──80/443──> caddy (TLS tự động, Let's Encrypt)
                         │
                         ▼
                     api :8080 (ASP.NET Core, bind 127.0.0.1:8080)
                         │
                         ▼
                     /data/edufocus.db (volume edufocus-data, SQLite)
```

- Chỉ Caddy mở port **80/443** (kèm **443/UDP** cho HTTP/3) khi bật profile `tls`.
- App phụ huynh giữ kết nối WebSocket tới `wss://<domain>/v1/push/stream`; Caddy tự nâng cấp
  WebSocket nên không cần mở thêm port. Kết nối dùng cùng token thiết bị qua header `Authorization`.
- API bind mặc định `127.0.0.1:${API_PORT:-8080}` để debug/local; Caddy gọi `api:8080` qua mạng
  Docker. Không có database server riêng và không có port database nào cần mở.
- Chỉ dùng **một file** `cloud/docker-compose.yml`. Production chạy thêm profile `tls`; demo LAN
  chạy API trực tiếp, không cần Caddy.
- SQLite là **một instance ghi duy nhất**. Không scale nhiều replica API cùng trỏ vào một file.
- API nhận `X-Forwarded-For/Proto` từ Caddy khi `Cloud__TrustForwardedHeaders=true`. HSTS bật ở môi
  trường non-Development.
- Log JSON (Serilog compact) ra stdout; Docker giới hạn `10m x 5` mỗi service.

## 2. Yêu cầu hạ tầng

| Hạng mục | Tối thiểu | Khuyến nghị |
|---|---|---|
| VPS | 2 vCPU, 2 GB RAM, 20 GB SSD | 2 vCPU, 4 GB RAM, 40 GB SSD |
| OS | Ubuntu 22.04/24.04 LTS | cùng |
| Docker | Engine 24+, Compose plugin v2 | bản mới nhất |
| Domain | 1 bản ghi A/AAAA trỏ về VPS | dùng subdomain riêng, ví dụ `cloud.example.com` |
| Firewall | mở TCP 80, 443 và UDP 443 | đóng toàn bộ port khác, kể cả 8080 |
| Outbound | HTTPS ra ngoài | cần cho ACME (Let's Encrypt) và Web Push/ntfy fallback |

Không cần mở port cho API/SQLite. Nếu dùng firewall cloud (Security Group), chỉ mở 22 (SSH, giới hạn
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

- `EDUFOCUS_CLOUD_PUBLIC_URL=https://<domain>` (không có `/` cuối).
- `EDUFOCUS_DOMAIN=<domain>` để Caddy xin chứng chỉ.
- `API_BIND=127.0.0.1`, `API_PORT=8080`, `CLOUD_TRUST_FORWARDED_HEADERS=true`.
- `EDUFOCUS_CLOUD_AUTO_MIGRATE=true` cho deploy một lệnh; đặt `false` nếu muốn chạy migration tường
  minh trước khi rollout.
- `PUSH_WEBSOCKET_ENABLED=true` để app nhận thông báo trực tiếp, không cần ntfy.
- `PUSH_ENABLED=false` cho lần deploy đầu; chỉ bật khi cần fallback Web Push/UnifiedPush.
- Các biến `JOBS_*`, `RATE_LIMIT_*` giữ mặc định trừ khi có lý do chính.

Nếu dùng fallback UnifiedPush/Web Push, tạo cặp khóa VAPID bằng image ntfy:

```bash
docker run --rm binwiederhier/ntfy webpush keys
```

Sao chép public/private key vào `.env`, đặt subject là `mailto:` hoặc `https:` mà bạn quản lý, và
thêm host endpoint của ntfy/Web Push vào allowlist:

```dotenv
PUSH_ENABLED=true
PUSH_VAPID_PUBLIC_KEY=<public-key>
PUSH_VAPID_PRIVATE_KEY=<private-key>
PUSH_VAPID_SUBJECT=mailto:admin@example.com
PUSH_ALLOWED_ENDPOINT_HOSTS=ntfy.example.com
```

`PUSH_ALLOWED_ENDPOINT_HOSTS` trong compose chỉ map host đầu tiên. Nếu dùng nhiều distributor, thêm
`Push__AllowedEndpointHosts__1`, `__2`… trong `docker-compose.yml`. Private key **không** commit và
không nằm trong image; khi `PUSH_ENABLED=false`, các biến VAPID không bắt buộc. WSS chỉ cần
`PUSH_WEBSOCKET_ENABLED=true` và `PUSH_WEBSOCKET_HEARTBEAT_SECONDS=20` (mặc định compose).

`.env`, `secrets/`, `backups/`, `*.db` đều nằm trong `.gitignore`. Không chạy `git add -f`.

## 4. Triển khai lần đầu

```bash
cd EduFocus/cloud

# 1) Build image (hoặc đặt CLOUD_API_IMAGE trỏ tới image GHCR đã publish)
docker compose build

# 2) Khởi động API + Caddy; API tự chạy migration nếu EDUFOCUS_CLOUD_AUTO_MIGRATE=true
docker compose --profile tls up -d
docker compose ps
```

Nếu đặt `EDUFOCUS_CLOUD_AUTO_MIGRATE=false`, chạy migration tường minh trước khi start API:

```bash
docker compose run --rm api --migrate
docker compose --profile tls up -d
```

Lệnh `--migrate` chỉ chạy migration rồi thoát, không start web server hay background job. Caddy xin
chứng chỉ TLS trong lần request đầu tới domain; theo dõi:

```bash
docker compose logs -f caddy
```

Có thể thêm `COMPOSE_PROFILES=tls` vào `.env` để dùng `docker compose up -d` mà không cần
`--profile tls`.

## 5. Smoke test sau deploy

```bash
DOMAIN=cloud.example.com
curl -fsS "https://$DOMAIN/healthz"          # {"status":"healthy",...}
curl -fsS "https://$DOMAIN/v1/"              # tên + version API
```

Sau đó pair thử bằng app desktop:

1. Desktop tạo phiên ghép nối → QR chứa `https://<domain>/v1/...`.
2. Mobile quét QR, claim thành công.
3. Nếu `PUSH_WEBSOCKET_ENABLED=true`: gửi thử một cảnh báo từ desktop; app phải giữ kết nối WSS
   và nhận thông báo kể cả khi đóng app. Khi dùng fallback, kiểm tra thêm `PUSH_ENABLED=true`.
4. Mở màn báo cáo trên mobile, xác nhận dữ liệu ngày hôm nay hiển thị.

Kiểm tra API từ chối cấu hình thiếu: log khởi động sẽ báo nếu thiếu connection string, heartbeat WSS
ngoài khoảng 5–120 giây, hoặc thiếu VAPID public/private key, subject hay allowlist khi
`Push__Enabled=true`.

## 6. Vận hành thường ngày

```bash
cd EduFocus/cloud
docker compose ps
docker compose logs --since 1h api
docker compose logs --since 1h caddy
df -h                                  # dung lượng đĩa
docker system df                       # image/volume chiếm chỗ
```

- Log là JSON một dòng, đã redaction các trường `token`, `authorization`, `endpoint`, `code`,
  `password`, `secret`. Không bật log body request; log Serilog request không chứa payload.
- Healthcheck API gọi `/healthz` (kiểm tra luôn SQLite) mỗi 30 giây.
- Job bảo trì chạy mỗi `JOBS_INTERVAL_SECONDS` (60): tạo notification tổng kết ngày và dọn dữ liệu
  hết hạn. Xem log `Cloud maintenance finished`.
- SQLite tự checkpoint WAL; không xóa `edufocus.db-wal`/`edufocus.db-shm` khi API đang chạy.

## 7. Cập nhật phiên bản và rollback

Thứ tự an toàn: **backup → migrate → deploy image mới → smoke test**.

```bash
cd EduFocus/cloud

# 1) Backup trước khi đổi phiên bản
./scripts/backup.sh

# 2) Nếu bản mới có migration: chạy với image mới trước khi đổi API đang chạy
CLOUD_API_IMAGE=ghcr.io/cong-ty-tnnh-q-tech/edufocus-cloud-api:<sha-moi> \
  docker compose run --rm api --migrate

# 3) Cập nhật image và recreate API
sed -i 's|^CLOUD_API_IMAGE=.*|CLOUD_API_IMAGE=ghcr.io/cong-ty-tnnh-q-tech/edufocus-cloud-api:<sha-moi>|' .env
docker compose pull api
docker compose up -d api

# 4) Smoke test
curl -fsS "https://$EDUFOCUS_DOMAIN/healthz"
```

Rollback image: đặt `CLOUD_API_IMAGE` về tag cũ và `docker compose up -d api`. Migration **không tự
rollback**; nếu bản mới đã chạy migration không tương thích ngược, phải restore backup theo mục 10.
Vì vậy luôn backup trước khi migrate.

## 8. Xoay token và thu hồi thiết bị

Khi nghi lộ token thiết bị:

- **Thu hồi một thiết bị** (mobile token có `family:admin`): `DELETE /v1/devices/{id}`. Token và push
  registration của thiết bị bị vô hiệu ngay; thiết bị đó nhận 401 ở request kế tiếp.
- **Xoay token của chính thiết bị**: `POST /v1/devices/token/rotate`, lưu token mới, token cũ mất hiệu lực.
- **Nghi lộ toàn bộ**: dùng mobile admin gọi `DELETE /v1/family` để xóa sạch dữ liệu và ghép nối lại.
- **Khẩn cấp ở DB** (khi không còn token admin tin cậy): runtime image có `sqlite3`. Trường
  `RevokedAtUtc` lưu .NET UTC ticks (INTEGER), nên dùng biểu thức chuyển đổi:

  ```bash
  docker compose exec -T api sqlite3 /data/edufocus.db \
    "UPDATE DeviceTokens SET RevokedAtUtc = CAST(strftime('%s','now') AS INTEGER) * 10000000 + 621355968000000000 WHERE RevokedAtUtc IS NULL;"
  ```

  Mọi thiết bị phải tạo phiên ghép nối mới. Ghi lại thời điểm và lý do vào nhật ký vận hành.
- **VAPID private key bị lộ**: tạo cặp khóa mới bằng `ntfy webpush keys`, cập nhật
  `PUSH_VAPID_PUBLIC_KEY`/`PUSH_VAPID_PRIVATE_KEY`, rồi `docker compose up -d api`. Endpoint cũ vẫn
  hợp lệ nhưng yêu cầu phụ huynh mở lại app để đăng ký lại với public key mới; xoay khóa có thể làm
  message đang chờ không giải mã được.

## 9. Backup định kỳ

`scripts/backup.sh`:

- Dừng API trong thời gian ngắn, đóng gói toàn bộ volume `/data` thành `tar.gz` để có snapshot SQLite
  nhất quán, rồi start API lại.
- Ghi vào `backups/` với quyền `600`.
- Giữ **7 bản mới nhất** (`BACKUP_KEEP`), xóa bản cũ hơn.
- Nếu đặt `BACKUP_PASSPHRASE`, file được mã hóa AES-256 bằng `gpg` và bản thô bị xóa.
- Tên file: `edufocus-YYYYMMDDTHHMMSSZ.tar.gz[.gpg]`.

Cron chạy hằng ngày (ví dụ 02:00 giờ máy chủ):

```cron
0 2 * * * cd /opt/EduFocus/cloud && ./scripts/backup.sh >> /var/log/edufocus-backup.log 2>&1
```

Khuyến nghị:

- Cất passphrase backup **tách biệt** khỏi VPS (password manager của công ty).
- Đồng bộ `backups/` sang nơi khác (object storage hoặc máy backup) và kiểm tra định kỳ.
- Giữ `umask 077` như script; không để backup trong thư mục web public.
- Thử restore mỗi tháng một lần vào thư mục tạm (xem mục 10).

## 10. Restore

`scripts/restore.sh <file>` dừng API, chuyển file DB hiện tại sang tên `.pre-restore-<stamp>` trong
volume, giải nén bản backup vào `/data`, rồi start API.

Khôi phục production (chỉ khi có sự cố):

```bash
cd EduFocus/cloud
./scripts/restore.sh backups/edufocus-20261117T020000Z.tar.gz.gpg
curl -fsS "https://$EDUFOCUS_DOMAIN/healthz"
```

Kiểm tra bản backup mà không đụng production:

```bash
mkdir -p /tmp/edufocus-restore
tar -xzf backups/edufocus-20261117T020000Z.tar.gz -C /tmp/edufocus-restore
sqlite3 /tmp/edufocus-restore/edufocus.db 'PRAGMA integrity_check; SELECT count(*) FROM Families;'
```

Nếu file `.gpg`, giải mã trước bằng `gpg --decrypt` rồi mới kiểm tra.

Lưu ý:

- Restore là thao tác phá hủy dữ liệu hiện tại; kiểm tra kỹ tên file.
- Script không tự chạy migration sau restore; bản backup đã chứa `__EFMigrationsHistory`. Nếu phục
  hồi từ bản backup cũ hơn phiên bản API đang chạy, chạy `--migrate` sau restore.
- Sau khi xác nhận dữ liệu đúng, xóa các file `*.pre-restore-*` trong volume để tránh đầy đĩa:

  ```bash
  docker compose exec -T api sh -c 'rm -f /data/*.pre-restore-*'
  ```

- API được start lại ở cuối script, kể cả khi restore lỗi (trap cleanup), để service không bị dừng
  ngoài ý muốn; kiểm tra log và dữ liệu trước khi cho phép ghi tiếp.

## 11. Xử lý sự cố

**API không khởi động** - `docker compose logs api`:

- `SQLite connection string is required` → thiếu `ConnectionStrings__Sqlite`; kiểm tra biến trong
  compose/`.env`.
- `unable to open database file` hoặc `attempt to write a readonly database` → volume `/data` không
  ghi được. Kiểm tra quyền volume; container chạy user `APP_UID` (1654). Với volume cũ, chạy một lần
  container có quyền root để `chown -R 1654:1654 /data` hoặc tạo lại volume.
- `database is locked` → có hơn một API instance cùng mở file, hoặc backup/restore đang chạy. Chỉ
  chạy một instance API.
- `Push:VapidPublicKey ...` / `Push:VapidPrivateKey ...` / `Push:VapidSubject ...` /
  `Push:AllowedEndpointHosts ...` → bật push nhưng thiếu cấu hình; sửa `.env` hoặc đặt
  `PUSH_ENABLED=false` tạm thời.
- `Cloud:PublicBaseUrl must be an absolute URL` → `EDUFOCUS_CLOUD_PUBLIC_URL` sai định dạng.

**Caddy trả 502** - API chưa healthy hoặc sai mạng:

```bash
docker compose ps
docker compose logs --since 10m api
# API bind localhost; chạy healthcheck nội bộ:
docker compose exec -T api dotnet EduFocus.Cloud.Api.dll --healthcheck; echo "exit=$?"
```

**TLS không cấp được** - kiểm tra DNS trỏ đúng VPS, port 80/443 mở, Caddy log có lỗi ACME. Không chạy
nhiều Caddy cùng domain.

**SQLite đầy đĩa**:

```bash
df -h
docker system df
docker compose exec -T api sh -c 'ls -lh /data'
docker compose exec -T api sqlite3 /data/edufocus.db 'PRAGMA page_count; PRAGMA page_size;'
```

- Xóa image cũ (`docker image prune`), backup cũ đã đồng bộ offsite, log cũ.
- Dọn dữ liệu hết hạn: kiểm tra job maintenance đang bật; có thể chạy tay bằng cách restart API
  (`Jobs__Enabled=true`).
- Không bao giờ xóa trực tiếp volume `edufocus-data`. Nếu buộc phải mở rộng ổ đĩa, snapshot/backup
  trước.
- `docker compose down` **không** xóa volume; tránh `down -v` ở production.

**Push không tới điện thoại**:

- `PUSH_ENABLED=true`, VAPID hợp lệ và allowlist chứa host endpoint ntfy.
- Log API không có lỗi `endpoint_invalid`/`push_unavailable` hàng loạt.
- App mobile đã chọn distributor, cấp quyền thông báo và đăng ký endpoint sau khi cài.
- Kiểm tra quiet hours/preference của gia đình (mặc định 22:00-06:00, tổng kết 20:30).

## 12. Chế độ demo LAN (chỉ khi demo)

Dùng khi ban giám khảo cần xem trên điện thoại thật trong mạng nội bộ, chưa có domain/TLS:

```bash
cd EduFocus/cloud
cp .env.example .env
# sửa:
#   EDUFOCUS_CLOUD_PUBLIC_URL=http://<IP-LAN-VPS>:8080
#   ASPNETCORE_ENVIRONMENT=Development
#   API_BIND=0.0.0.0
#   API_PORT=8080
#   CLOUD_TRUST_FORWARDED_HEADERS=false
#   PUSH_ENABLED=false (hoặc true nếu đã có service account)
docker compose up -d --build
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

- [ ] `docker compose ps` tất cả healthy.
- [ ] `curl https://<domain>/healthz` trả `healthy`.
- [ ] Ghép nối desktop ↔ mobile thành công, QR dùng đúng domain HTTPS.
- [ ] Điện thoại nhận thông báo cảnh báo (nếu bật push) và mở được báo cáo ngày.
- [ ] Backup gần nhất chạy thành công; biết cách restore.
- [ ] Không có `.env`, service account hay backup nào trong `git status`.

## 14. Tài liệu liên quan

- [cloud-backend.md](./cloud-backend.md) - kiến trúc API, schema, retention, biến môi trường.
- [ADR-20](./adr/ADR-20-optional-cloud-relay.md) - quyết định cloud tùy chọn và đánh đổi.
