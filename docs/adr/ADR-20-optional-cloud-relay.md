# ADR-20: Cloud relay tùy chọn cho thông báo phụ huynh

- **Status:** Proposed (chờ review)
- **Date:** 2026-11-17
- **Issue:** [#77](https://github.com/Cong-ty-TNNH-Q-Tech/EduFocus/issues/77)
- **Related:** ADR-02 (local-first, không cloud mặc định), ADR-11 (`CameraUnavailable` là trạng thái hạng nhất), [dpia.md](../dpia.md), [privacy-notice.md](../privacy-notice.md)

## Context

EduFocus hiện là ứng dụng local-first: mọi dữ liệu nằm trong SQLite mã hóa trên máy, không có tài
khoản, không có máy chủ. Yêu cầu sản phẩm mới (Phase 8) cần **gửi thông báo đẩy tới điện thoại phụ
huynh** khi học sinh xao nhãng kéo dài, vắng mặt, hoặc khi bảo vệ bị tắt — kể cả khi máy tính đã khóa
màn hình và phụ huynh không ngồi cạnh máy.

Không thể thực hiện việc này bằng kết nối LAN thuần túy: điện thoại thường dùng mạng di động ở ngoài
nhà, NAT/ firewall hai chiều, và không có địa chỉ ổn định. Một kênh trung gian là cần thiết để:
- chuyển một lượng rất nhỏ sự kiện từ máy tính sang điện thoại;
- hoạt động khi máy tính đang ngủ/khóa nhưng service bảo vệ vẫn chạy;
- không biến EduFocus thành sản phẩm giám sát đám mây.

Các lựa chọn đã cân nhắc:

1. **Giữ local-first, không cloud.** Điện thoại chỉ xem báo cáo khi cùng Wi-Fi và app desktop đang
   chạy. Không đáp ứng yêu cầu thông báo từ xa.
2. **Đồng bộ DB đầy đủ lên cloud** (raw samples, URL, window title). Trái nguyên tắc least-data và
   làm tăng mạnh rủi ro dữ liệu trẻ em.
3. **Push trực tiếp desktop → FCM** bằng service account. Không an toàn: phải nhúng bí mật vào bản
   cài, không thu hồi được, không có nơi lưu ánh xạ gia đình/thiết bị, không thể xóa dữ liệu tập
   trung.
4. **Relay tối giản, tự lưu trữ được** (chọn): backend nhỏ chỉ nhận **sự kiện tổng hợp** và
   **metadata thông báo**, chuyển tiếp qua FCM; phụ huynh có thể tự host.

## Decision

Thêm **cloud relay tùy chọn** (opt-in) gồm ba thành phần:

- **Desktop (EduFocus.WPF):** module `EduFocus.Infrastructure.Cloud` + `ICloudLinkUseCase`.
  Mặc định **tắt**. Chỉ khi phụ huynh ghép nối điện thoại, desktop mới gửi dữ liệu qua outbox
  SQLite cục bộ (idempotent, có backoff).
- **Backend:** `ASP.NET Core Web API` (.NET 10) + `PostgreSQL 16`, đóng gói Docker, tự host được.
  Không có giao diện web cho phụ huynh ở giai đoạn này; chỉ API + job nền.
- **Mobile:** `.NET MAUI` (Android trước, package `com.qtech.edufocus.parent`), nhận thông báo qua
  Firebase Cloud Messaging HTTP v1 (data-only message, app tự dựng nội dung tiếng Việt).

### Nguyên tắc dữ liệu

Chỉ bốn loại bản ghi được phép rời khỏi máy:

| Loại | Nội dung | Dùng cho |
|---|---|---|
| `daily_summary` | ngày, phút học, phút tập trung ước lượng, số lần chuyển trạng thái, coverage camera, số lần bị chặn | màn hình tổng quan trên điện thoại |
| `attention_alert` | loại (distracted/absent), thời điểm, thời lượng, tên thiết bị | push + danh sách cảnh báo |
| `protection_off` | thời điểm, lý do (parent_exit/manual), tên thiết bị | push bảo vệ bị tắt |
| `security_alert` | loại `clock_jump`, thời điểm phát hiện | push cảnh báo an toàn |

**Không gửi:** mẫu hoạt động thô, URL, tên miền, tiêu đề cửa sổ, tên ứng dụng, sự kiện camera,
khung hình/ảnh/video, PIN, mã khôi phục, hash PIN, ghi chú phụ huynh.

### Ghép nối và xác thực

- Desktop tạo **pairing session**: mã 8 ký tự Crockford base32 (bỏ I/L/O/U), hết hạn sau 10 phút,
  server chỉ lưu hash. QR chứa `edufocus://pair?v=1&u=<base64url(cloudBaseUrl)>&s=<sessionId>&c=<code>`;
  nhập tay chỉ cần mã 8 ký tự, server tra session đang hoạt động duy nhất theo hash mã.
- Khi claim thành công, server phát **token đục** 32 byte, prefix `efd_` (desktop) hoặc `efm_`
  (mobile), chỉ lưu SHA-256 phía server; scope desktop `sync` + `family:admin`, mobile `read` +
  `prefs` + `family:admin`. Token không bao giờ được ghi log.
- Token có thể thu hồi/xoay vòng; xóa thiết bị hoặc xóa gia đình thu hồi toàn bộ token liên quan.

### Đồng bộ và thông báo

- Outbox cục bộ + `ClientEventId` UUID; ingest idempotent, batch ≤ 50 sự kiện, body ≤ 64 KB,
  retry backoff 1 → 60 phút, tối đa 5.000 sự kiện/7 ngày.
- `daily_summary` dùng upsert **latest-wins** theo `(family, device, localDate)` với
  `generatedAtUtc` để bản mới hơn thay bản cũ.
- `attention_alert` chỉ phát khi trạng thái kéo dài ≥ 5 phút, tối đa 1 thông báo/15 phút/loại.
- `protection_off` chỉ phát khi phụ huynh chủ động tắt hoặc thoát bằng PIN; **không** phát khi lịch
  tự tắt/thay đổi.
- Quiet hours mặc định 22:00–06:00 theo múi giờ gia đình; tóm tắt ngày mặc định 20:30. Cả hai
  chỉnh được trên app phụ huynh.
- Push là **data-only**, nội dung chung chung ("EduFocus: có cập nhật mới cho hôm nay"), app tự
  dựng câu tiếng Việt từ dữ liệu đã tải; không đưa chi tiết học tập vào payload FCM.

### Lưu trữ và xóa

| Dữ liệu cloud | Thời hạn |
|---|---|
| Pairing session | 24 giờ |
| Sync event thô (dedupe) | 7 ngày |
| Notification log | 30 ngày |
| Daily summary | 180 ngày |
| Cloud audit | 365 ngày |

- Phụ huynh xóa gia đình trên cloud bằng xác nhận `XÓA` trong app; backend xóa cascade, thu hồi
  token và vô hiệu push registration.
- Ngắt kết nối trên desktop dừng outbox và xóa token cục bộ; backend vẫn giữ dữ liệu tới khi phụ
  huynh xóa hoặc hết hạn.
- Tự host: chủ máy chủ chịu trách nhiệm backup/retention; tài liệu vận hành nêu rõ.

## Consequences

### Positive

- Đáp ứng yêu cầu thông báo từ xa mà không phá vỡ kiến trúc local-first: không bật cloud thì sản
  phẩm hoạt động y như trước.
- Lượng dữ liệu rời máy rất nhỏ (vài KB/ngày), không chứa nội dung học tập hay dữ liệu camera.
- Backend tách biệt khỏi `EduFocus.slnx`, CI riêng, có thể tự host, không khóa nhà cung cấp.
- Token đục + hash phía server: rò rỉ DB backend không lộ token dùng được ngay (vẫn phải thu hồi).
- FCM là kênh duy nhất phụ thuộc bên thứ ba; có thể thay bằng dịch vụ tự host sau này.

### Negative / Risks

- **Bên thứ ba:** FCM của Google xử lý metadata giao nhận (token thiết bị, IP) ngoài lãnh thổ Việt
  Nam. Phải nêu trong privacy notice và DPIA; cần đánh giá chuyển dữ liệu xuyên biên giới trước khi
  phát hành thương mại.
- Backend là bề mặt tấn công mới: lộ token, lạm dụng ingest, DoS. Giảm thiểu bằng rate limit, hash
  token, kích thước batch, audit và cảnh báo.
- Phụ huynh có thể hiểu nhầm thông báo là bằng chứng tuyệt đối; UI mobile phải ghi rõ "ước lượng".
- Tăng độ phức tạp vận hành: PostgreSQL, migrate, backup, chứng chỉ TLS, cấu hình FCM.
- Nếu phụ huynh tự host, họ trở thành bên kiểm soát dữ liệu cloud; tài liệu phải nói rõ trách nhiệm.
- Chưa có đồng bộ hai chiều: app mobile chỉ đọc báo cáo và chỉnh preference, không sửa blocklist.

## Alternatives

- **LAN-only / không backend:** loại, không đáp ứng thông báo khi ngoài mạng nhà.
- **Đồng bộ DB đầy đủ:** loại, vi phạm least-data và làm tăng rủi ro dữ liệu trẻ em.
- **Desktop gọi thẳng FCM bằng service account:** loại, không thể giữ bí mật trong bản cài và
  không có khả năng thu hồi/xóa tập trung.
- **Giải pháp bên thứ ba (OneSignal, Firebase-only, …):** nhanh nhưng khóa dữ liệu và khó tự host;
  chỉ dùng FCM làm kênh vận chuyển, logic ưu tiên/quiet hours nằm trong backend của EduFocus.

## Follow-ups

1. Review và chấp nhận/từ chối ADR này trước khi merge Phase 8.
2. Rà soát pháp lý: cơ sở xử lý dữ liệu trẻ em, chuyển dữ liệu qua FCM, vai trò bên kiểm soát/bên
   xử lý khi tự host (xem [dpia.md](../dpia.md)).
3. Đo tải ingest/push trên VPS mục tiêu; đặt rate limit và alert.
4. Quyết định nhà cung cấp VPS/domain mặc định (nếu có) và quy trình tự host.
5. Cập nhật [privacy-notice.md](../privacy-notice.md) khi bật cloud cho bản phát hành công khai.
