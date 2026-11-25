# Phase 8 — Kiểm thử E2E và kịch bản demo

Tài liệu này là **biên bản kiểm thử** cho cloud relay + app phụ huynh Android (Phase 8). Điền
thông tin môi trường, đánh dấu từng mục và đính kèm bằng chứng (ảnh, log đã lọc, kết quả lệnh) vào
release artifact hoặc issue trước khi công bố bản demo.

Tham chiếu: [mobile/README.md](../mobile/README.md),
[cloud-backend.md](./cloud-backend.md), [cloud-deploy.md](./cloud-deploy.md),
[privacy-notice.md](./privacy-notice.md), [dpia.md](./dpia.md),
[security-e2e-checklist.md](./security-e2e-checklist.md) (desktop).

> Quy tắc bằng chứng: không chụp/đính kèm token (`efd_`/`efm_`), mã ghép nối, UnifiedPush
> endpoint/khóa Web Push, PIN, mật khẩu hay tệp DB. Che các giá trị này trước khi chụp màn hình; log
> phải được lọc.

---

## 1. Thông tin lần chạy

| Trường | Giá trị |
|---|---|
| Ngày giờ chạy | `...` |
| Người thực hiện | `...` |
| Commit / tag desktop | `...` |
| Commit / tag mobile | `...` |
| APK (tên + SHA-256) | `...` |
| Backend staging (host HTTPS, không ghi secret) | `...` |
| Phiên bản image backend / SQLite | `...` |
| VAPID/ntfy staging (không ghi khóa) | `...` |
| Thiết bị 1 (model, Android, distributor) | `...` |
| Thiết bị 2 (nếu test nhiều mobile) | `...` |
| Windows desktop (bản, VM/thật) | `...` |
| Kết quả tổng | `PASS / FAIL` |

Chạy lại toàn bộ sau mỗi thay đổi backend/desktop/mobile ảnh hưởng đến luồng cloud.

---

## 2. Chuẩn bị môi trường

- [ ] Backend staging chạy bằng `cloud/docker-compose.yml` với profile `tls`, HTTPS hợp lệ,
      `/healthz` trả OK; migration mới nhất đã chạy.
- [ ] `EDUFOCUS_CLOUD_AUTO_MIGRATE` đúng cấu hình; API không publish port ra ngoài (bind
      `127.0.0.1`), không có database server/port công khai.
- [ ] Desktop bản test đã cấu hình trỏ tới backend staging; cloud mặc định **tắt** trước khi bắt đầu.
- [ ] APK release (hoặc debug có chữ ký test) cài trên thiết bị sạch; package
      `com.qtech.edufocus.parent` đúng.
- [ ] Thiết bị đã cài một UnifiedPush distributor (ví dụ ntfy), đã chọn distributor và bật thông báo
      cho app. Không cần Google Play Services.
- [ ] Backend bật `PUSH_ENABLED=true`, VAPID public/private key và subject hợp lệ; allowlist chứa host
      endpoint ntfy; `GET /v1/push/config` trả `enabled=true`.
- [ ] Có tài khoản/thiết bị **family thứ hai** để test cô lập dữ liệu.
- [ ] Công cụ: Fiddler/Charles hoặc proxy TLS trên desktop, `sqlite3` read-only tới file DB staging
      (qua container), `apksigner`/`aapt2`, `adb` (tùy chọn), công cụ chỉnh giờ/quiet hours.
- [ ] Điền sẵn privacy notice bản đang áp dụng và biết vị trí nút **Xóa dữ liệu trên cloud**.

---

## 3. Ma trận chức năng

### 3.1. Ghép nối (UC15)

- [ ] Quét QR từ desktop → điền sẵn mã, bấm **Ghép nối** → thành công, mở tab Tổng quan.
- [ ] Nhập mã 8 ký tự thủ công (chữ thường/hoa, có/không gạch nối) → thành công.
- [ ] Từ chối quyền camera → vẫn nhập mã thủ công được; nút mở Cài đặt quyền hoạt động.
- [ ] Mã sai 5 lần liên tiếp → bị chặn theo rate limit, thông báo tiếng Việt, không lộ chi tiết.
- [ ] Mã hết hạn sau 10 phút → lỗi "không hợp lệ hoặc đã hết hạn"; desktop hiển thị phiên hết hạn.
- [ ] Dùng lại mã đã claim → bị từ chối; không tạo thêm thiết bị.
- [ ] Deep link `edufocus://pair?...` chỉ điền sẵn mã, **không tự ghép**; người dùng vẫn phải xác nhận.
- [ ] Đã ghép nối rồi quét mã mới → hộp thoại thay thế; token cũ bị thu hồi; thiết bị cũ trong danh
      sách chuyển trạng thái đã thu hồi.
- [ ] **Ngắt kết nối** trên mobile → về Welcome; ghép nối lại bằng mã mới thành công.
- [ ] Desktop thu hồi thiết bị mobile (Parent Mode → Điện thoại) → request kế tiếp của mobile nhận
      401, app xóa phiên và về Welcome, không crash.
- [ ] Gỡ app rồi cài lại → không còn token cũ; ghép nối mới hoạt động.
- [ ] Cài đè bản APK mới (cùng keystore, tăng versionCode) → vẫn còn liên kết trong SecureStorage.

### 3.2. Đồng bộ outbox (desktop → cloud)

- [ ] Tắt mạng desktop, tạo sự kiện (kết thúc phiên, tắt bảo vệ, attention/security nếu có) → outbox
      tăng, không mất sự kiện, UI không treo.
- [ ] Bật mạng → sự kiện tự gửi trong vòng vài phút; outbox về 0; server có bản ghi tương ứng.
- [ ] Đóng/mở lại desktop app khi outbox còn sự kiện → sự kiện vẫn còn và gửi tiếp.
- [ ] Tắt/mở app mobile không ảnh hưởng outbox desktop.
- [ ] Ngắt mạng giữa lúc gửi (hoặc backend restart) → retry backoff, không mất sự kiện, không tạo
      bản ghi trùng (`ClientEventId` idempotent).
- [ ] Tạo cùng một loại sự kiện nhiều lần → mỗi sự kiện một `ClientEventId`; server dedupe đúng.
- [ ] Batch tối đa 50 sự kiện; vượt 5.000 sự kiện/7 ngày → desktop ngừng thêm và có log/cảnh báo cục bộ.
- [ ] Ngắt kết nối cloud (desktop hoặc mobile) → outbox ngừng gửi; token desktop bị thu hồi.

### 3.3. Push (UC16)

Chuẩn bị: một điện thoại đã ghép nối; với test nhiều thiết bị, ghép thêm điện thoại thứ hai cùng gia đình.

- [ ] `attention_alert`: để attention mô phỏng > 5 phút (hoặc cấu hình test) → điện thoại nhận push;
      bấm vào mở báo cáo đúng ngày.
- [ ] `daily_summary`: job tổng kết ngày (hoặc chạy job thủ công) → nhận push đúng giờ preference.
- [ ] `protection_off`: tắt Protection bằng PIN trên desktop → nhận push; bấm vào mở tab Thông báo.
- [ ] `security_alert`: tạo sự kiện tamper `clock_jump` (theo quy trình test) → nhận push; nội dung
      chung, không chi tiết.
- [ ] App **foreground** → banner Toast trong app, không thêm notification hệ thống; app
      **background/killed** → notification hệ thống.
- [ ] `blocked_attempt`: mở một trang web bị chặn trên desktop (hoặc chèn bản ghi test) → nhận push;
      bấm vào mở báo cáo đúng ngày; payload không có domain/URL.
- [ ] Tắt từng loại trong app mobile (5 công tắc) → loại đó không còn push; các loại khác vẫn nhận.
      Bật lại → nhận lại.
- [ ] Quiet hours bao phủ thời điểm hiện tại (ví dụ 00:00–23:59) → push thường bị `Suppressed`; riêng
      `security_alert` vẫn qua vì mặc định bỏ qua quiet hours. Đổi quiet hours → áp dụng ngay.
- [ ] Tóm tắt ngày đặt giờ khác → job gửi đúng múi giờ gia đình (`Asia/Ho_Chi_Minh`).
- [ ] Ghép hai điện thoại → cả hai nhận push; thu hồi một thiết bị → thiết bị đó không nhận nữa.
- [ ] Endpoint không hợp lệ (gỡ app/đổi distributor) → backend nhận `404`/`410`, đánh dấu
      registration invalid, không lỗi dây chuyền; thiết bị hợp lệ còn lại vẫn nhận.
- [ ] Payload sau giải mã chỉ có `type`, `date`, `notificationId` (kiểm tra bằng debug app/test, không
      chứa số liệu/tên/URL); body Web Push trên dây là ciphertext.

### 3.4. Báo cáo (UC17)

- [ ] Tổng quan hôm nay: phút học, phút đang dùng, số phiên online class, số lần bị chặn; số liệu
      khớp `DailySummaries` của ngày.
- [ ] Báo cáo 7 ngày: biểu đồ đủ 7 cột (kể cả ngày trống), danh sách mới nhất trước; chạm ngày xem chi tiết.
- [ ] Ngày không có dữ liệu → trạng thái trống/"—", không hiển thị số 0 giả.
- [ ] Coverage camera < 50% → hiện cảnh báo tỷ lệ thấp ở cả Tổng quan, Báo cáo và chi tiết ngày.
- [ ] Giá trị null (ví dụ online class không đo được) → "—"; phần trăm một chữ số thập phân dùng dấu phẩy.
- [ ] Đổi múi giờ desktop (ví dụ `Asia/Tokyo`) rồi tạo summary mới → ngày/giờ hiển thị theo múi giờ mới;
      dữ liệu cũ vẫn đúng ngày đã ghi.
- [ ] Mất mạng khi mở app → hiện dữ liệu cache kèm "Đang xem dữ liệu lưu lúc …" và nút thử lại; có
      mạng lại → cập nhật mới, banner biến mất.
- [ ] Deep link `//reports?date=yyyy-MM-dd` từ notification mở đúng ngày (kể cả ngày không phải hôm nay).
- [ ] Kéo để làm mới không kẹt trạng thái loading.

### 3.5. Bảo mật

- [ ] Token mobile bị sửa/sai → 401, app xóa phiên một lần, về Welcome, hiện "Phiên ghép nối đã hết".
- [ ] Token thiết bị đã thu hồi → 401 cho mọi endpoint mobile (reports, notifications, preferences,
      devices).
- [ ] Token desktop dùng gọi endpoint mobile-only → 403; token mobile gọi endpoint desktop-only → 403.
- [ ] Token family B không đọc được dữ liệu family A (reports/notifications/devices) → 404/403 nhất quán.
- [ ] Rate limit pairing/claim và các endpoint đọc: vượt ngưỡng → 429 kèm `Retry-After`; app hiển thị
      thông báo thân thiện, không retry vô hạn.
- [ ] Backend chỉ phục vụ HTTPS; gọi HTTP bị redirect/từ chối. APK release không cho cleartext.
- [ ] Grep log backend/desktop/mobile: không có token `efd_`/`efm_`, mã pairing, endpoint/khóa Web
      Push, mật khẩu.
- [ ] Pairing code chỉ xuất hiện trong QR/chuỗi nhập, không bị ghi vào DB dạng thô (chỉ hash) và
      không nằm trong log.

### 3.6. Xóa dữ liệu (UC18)

- [ ] **Ngắt kết nối** trên mobile: token mobile bị thu hồi, cache cục bộ bị xóa, về Welcome; token
      cũ không dùng lại được.
- [ ] **Ngắt kết nối** trên desktop: outbox dừng gửi, token desktop bị thu hồi; mobile hiện 401 khi
      gọi API.
- [ ] **Xóa dữ liệu trên cloud**: chỉ xác nhận khi gõ đúng `XÓA` (sai → không xóa); sau khi xóa,
      gia đình/thiết bị/token/summary/notification/push registration không còn trong SQLite.
- [ ] Sau khi xóa, token desktop và mobile cũ đều 401; desktop không tự tạo lại gia đình khi cloud tắt.
- [ ] Audit cloud còn bản ghi ẩn danh (actor/kind/thời điểm), không chứa token hay dữ liệu học tập.
- [ ] Xóa gia đình rồi ghép nối lại bằng mã mới → tạo family mới, không thấy dữ liệu cũ.

### 3.7. Không ảnh hưởng desktop

- [ ] Cloud tắt (mặc định) → không có request tới backend khi khởi động/chạy; xác nhận bằng proxy.
- [ ] Backend sập/mất mạng khi cloud bật → Protection, camera, heartbeat, block page vẫn hoạt động;
      UI chỉ báo lỗi cloud, không treo.
- [ ] Tắt cloud trong lúc outbox còn sự kiện → sự kiện giữ cục bộ, không gửi lén khi bật lại (chỉ gửi
      sau khi người dùng bật lại).
- [ ] Service desktop restart giữa lúc sync → outbox không mất, app mobile không nhận dữ liệu trùng.

---

## 4. Kiểm tra dữ liệu và quyền riêng tư

### 4.1. Gói tin desktop → backend

- [ ] Chụp request `POST /v1/sync/events` bằng proxy: body chỉ có `clientEventId`, `type`, `occurredAtUtc`,
      `localDate`, các trường tổng hợp (phút, số lần, coverage, lý do loại cảnh báo).
- [ ] Không có URL, domain, window title, app name, tên học sinh, mẫu thô, ảnh/frame, dữ liệu camera,
      PIN hay recovery code trong bất kỳ request nào.
- [ ] `GET /v1/reports/daily`, `GET /v1/notifications`, `GET|PUT /v1/preferences` chỉ trả dữ liệu tổng hợp
      của đúng gia đình.

### 4.2. SQLite

- [ ] Liệt kê schema (`sqlite3 /data/edufocus.db '.tables'`, `PRAGMA table_info(<table>)`): không có
      bảng/cột chứa URL, domain, window title, app, frame, ảnh, mẫu hoạt động chi tiết.
- [ ] `DeviceTokens.TokenHash` là hash 64 ký tự hex; không có cột lưu token thô `efd_`/`efm_`.
- [ ] `PairingSessions` chỉ lưu hash mã và thời điểm hết hạn, không lưu mã thô.
- [ ] `Notifications` chỉ chứa `Type`, `Category`, `LocalDate`, trạng thái gửi và dedupe key; payload
      push chỉ 3 khóa.
- [ ] Xóa gia đình → cascade sạch các bảng liên quan; audit ẩn danh còn lại.

### 4.3. Log

- [ ] Backend JSON log: redaction `token`/`authorization`/push endpoint hoạt động; không có body request.
- [ ] Desktop log: không có token `efd_`, mã pairing, nội dung payload.
- [ ] Mobile: không log token `efm_`, endpoint/khóa Web Push (kiểm tra `adb logcat` khi ghép nối và nhận push).

### 4.4. Tài liệu và APK

- [ ] Privacy notice hiển thị/link **trước khi bật cloud lần đầu**; onboarding yêu cầu xác nhận.
- [ ] Nội dung privacy notice và DPIA khớp dữ liệu thực tế (năm loại sự kiện, retention,
      UnifiedPush/ntfy, quyền xóa).
- [ ] APK/repo không chứa keystore, mật khẩu hay VAPID private key:
      `git ls-files | grep -Ei "keystore|jks|vapid"` không trả kết quả;
      `unzip -l <apk> | grep -Ei "keystore|jks"` không có.
- [ ] APK được ký bằng certificate release; `apksigner verify` pass; SHA-256 khớp `SHA256SUMS.txt`.

---

## 5. Kịch bản demo cho giám khảo (10 phút)

### Chuẩn bị trước (làm trước buổi demo)

- [ ] Backend staging + SQLite volume chạy ổn định; đã test `/healthz` và một request đọc.
- [ ] Desktop đã cài bản release, đăng nhập Parent Mode, có dữ liệu mẫu của ngày hôm nay.
- [ ] Hai điện thoại Android đã cài APK và distributor UnifiedPush, đã bật thông báo; một điện thoại
      đã ghép nối sẵn để dự phòng, một điện thoại sạch để demo ghép nối.
- [ ] Backend VAPID/ntfy hoạt động; đã gửi thử một push thành công trong vòng 24 giờ.
- [ ] Mạng Wi-Fi ổn định; có 4G dự phòng cho điện thoại.
- [ ] Chuẩn bị sẵn công cụ mô phỏng attention > 5 phút (cấu hình test hoặc script).
- [ ] Video quay trước toàn bộ luồng để phát nếu push bị chậm.

### Các bước (10 phút)

1. **0:00–1:00 — Giới thiệu:** desktop local-first, cloud tắt mặc định; mở Privacy Notice.
2. **1:00–2:30 — Ghép nối:** Parent Mode → Điện thoại → bật cloud → QR; điện thoại quét → xác nhận
   → tab Tổng quan hiển thị trạng thái đã ghép; desktop hiển thị tên thiết bị.
3. **2:30–4:00 — Báo cáo:** mở Tổng quan (hôm nay) và Báo cáo 7 ngày trên điện thoại; chỉ ra ngày
   trống hiển thị "—", cảnh báo coverage nếu có.
4. **4:00–5:30 — Cảnh báo attention:** bật Protection, mở domain bị chặn, để attention mô phỏng > 5
   phút → điện thoại nhận push `attention_alert`; bấm vào mở đúng báo cáo ngày.
5. **5:30–6:30 — Bảo vệ bị tắt:** tắt Protection bằng PIN → push `protection_off`; mở tab Thông báo.
6. **6:30–7:30 — Tùy chọn thông báo:** tắt `attention_alert`, đặt quiet hours bao phủ hiện tại; tạo
   lại cảnh báo → không có push; bật lại → có push.
7. **7:30–8:30 — Đồng bộ offline:** ngắt mạng desktop, tạo sự kiện, bật mạng → điện thoại nhận báo cáo
   cập nhật; outbox gửi lại không trùng.
8. **8:30–9:30 — Xóa dữ liệu:** mobile → Cài đặt → Xóa dữ liệu trên cloud → gõ `XÓA`; app về Welcome;
   thử token cũ (hoặc mở lại app) → 401/không còn dữ liệu.
9. **9:30–10:00 — Kết luận:** nhắc lại giới hạn (UnifiedPush/ntfy, cần mạng, chỉ dữ liệu tổng hợp,
   không điều khiển từ xa) và cách tự host/xóa dữ liệu.

### Dự phòng khi sự cố

- Push chậm > 2 phút: tiếp tục kịch bản, phát video quay trước; không để giám khảo chờ.
- Backend lỗi: chuyển sang chế độ local-first, trình bày kiến trúc và video; ghi nhận sự cố.
- Không có mạng: dùng cache trên app (banner "Đang xem dữ liệu lưu lúc …") và video.

---

## 6. Tổng hợp kết quả

| Nhóm | Số mục | Pass | Fail | Ghi chú |
|---|---|---|---|---|
| 3.1 Ghép nối | 12 | | | |
| 3.2 Đồng bộ | 8 | | | |
| 3.3 Push | 9 | | | |
| 3.4 Báo cáo | 10 | | | |
| 3.5 Bảo mật | 8 | | | |
| 3.6 Xóa dữ liệu | 6 | | | |
| 3.7 Desktop | 4 | | | |
| 4. Dữ liệu/quyền riêng tư | 14 | | | |

### Defect log

| ID | Mức | Mô tả | Bước tái hiện | Trạng thái | Issue |
|---|---|---|---|---|---|
| | | | | | |

### Ký duyệt

| Vai trò | Tên | Ngày | Kết luận |
|---|---|---|---|
| QA / người chạy | | | |
| Release owner | Nguyễn Thành Trung | | |

---

## 7. Giới hạn đã biết (ghi trong README và privacy notice)

- Push phụ thuộc distributor UnifiedPush (ví dụ ntfy); thiết bị chưa cài/chọn distributor hoặc bị tắt
  thông báo sẽ không nhận.
- Độ trễ push do mạng và chính sách pin Android; cảnh báo có thể đến muộn vài phút.
- App mobile chỉ có dữ liệu desktop đã đồng bộ; mất mạng thì xem cache, không có timeline chi tiết.
- Không điều khiển từ xa: không sửa blocklist, không bật/tắt Protection, không xem màn hình.
- Desktop là nguồn sự thật; cloud không đồng bộ hai chiều và không thay thế dữ liệu cục bộ.
- Xóa cloud không thể hoàn tác; gỡ app không tự xóa dữ liệu cloud — phải dùng **Xóa dữ liệu trên cloud**.

---

## 8. Phần đã được tự động hóa

Các mục sau được unit test chạy trong CI, không cần lặp lại thủ công (nhưng vẫn nên chạy smoke trên
staging):

- Parser QR/mã ghép nối, token storage, push payload parser/dedupe phía mobile.
- API client, view model Tổng quan/Báo cáo/Thông báo/Cài đặt (loading, lỗi, cache, 401, định dạng
  tiếng Việt), `ParentDataCache`, `SessionExpiryService`, `FamilyAdminService` — 173 test.
- Backend: claim pairing, auth scope, idempotent ingest, read APIs, preferences/quiet hours, Web Push
  payload/endpoint validation, cascade delete — `cloud/EduFocus.Cloud.Tests`.
- Release: `apksigner verify` + `aapt2 dump badging` + SHA-256 trong workflow mobile release.

Phần **bắt buộc thủ công**: mọi mục trong §3 chạy trên staging + thiết bị thật, §4 chụp mạng/DB/log,
và §5 demo hai lần liên tiếp.
