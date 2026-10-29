# EduFocus — Đánh giá tác động xử lý dữ liệu cá nhân (DPIA)

| Trường | Giá trị |
|---|---|
| Phiên bản hồ sơ | 1.0 |
| Ngày lập | 2026-10-30 |
| Issue | [#44](https://github.com/Cong-ty-TNNH-Q-Tech/EduFocus/issues/44) |
| Người chịu trách nhiệm | Nguyễn Thành Trung — Product/Release owner |
| Tư vấn pháp lý | Luật sư/DPO bên ngoài — **cần chỉ định và ký duyệt trước phát hành thương mại** |
| Trạng thái | Hoàn chỉnh về kỹ thuật; sẵn sàng cho rà soát pháp lý |
| Tài liệu liên quan | [privacy-notice.md](./privacy-notice.md), [ba_document.md](./ba_document.md) §10.2, [database_schema.md](./database_schema.md) §14/§16, [security-e2e-checklist.md](./security-e2e-checklist.md), [spikes/net10-ef10-dpia-evaluation.md](./spikes/net10-ef10-dpia-evaluation.md) |

> Tài liệu này là đánh giá kỹ thuật phục vụ tuân thủ, **không phải tư vấn pháp lý** và không thay
> thế hồ sơ nộp cơ quan có thẩm quyền. Trước khi phát hành thương mại, luật sư/DPO phải rà soát,
> xác nhận vai trò pháp lý, mẫu hồ sơ và thời hạn thông báo hiện hành.

---

## 1. Phạm vi và mô tả xử lý

EduFocus là ứng dụng desktop Windows (WPF, .NET 10) giúp phụ huynh theo dõi hoạt động học tập
trên máy của con. Sản phẩm gồm:

- **Onboarding và Parent Mode:** phụ huynh tạo PIN, nhận recovery code, cấu hình giám sát/bảo vệ.
- **Bảo vệ:** chặn domain/app qua hosts + block page loopback và theo dõi tiến trình.
- **Phiên học và hoạt động:** ghi nhận ứng dụng/domain foreground, idle, lớp trực tuyến.
- **Attention (tùy chọn):** camera được mở trong phiên để ước lượng trạng thái tập trung; không lưu
  ảnh/video/landmark/embedding.
- **Báo cáo và dữ liệu:** dashboard, báo cáo ngày/tuần/tháng, xuất CSV, retention và xóa dữ liệu.

**Luồng dữ liệu:** camera/foreground/hệ thống → xử lý trong tiến trình trên máy → SQLite
`%ProgramData%\EduFocus` → UI/báo cáo/CSV do phụ huynh mở. **Không có máy chủ EduFocus**, không có
telemetry, không upload dữ liệu học tập.

**Ngoài phạm vi:** dịch vụ cloud, tài khoản trực tuyến, chia sẻ dữ liệu giữa các thiết bị, nhận diện
danh tính, chống gian lận thi cử.

---

## 2. Vai trò và trách nhiệm

| Vai trò | Đơn vị/cá nhân | Trách nhiệm |
|---|---|---|
| Nhà phát hành / bên cung cấp công cụ | ThanhTrunggDEV | Thiết kế privacy-by-design, phát hành, xử lý sự cố sản phẩm, duy trì hồ sơ DPIA/thông báo |
| Bên kiểm soát dữ liệu trên thiết bị | Phụ huynh/người giám hộ | Quyết định bật giám sát, cấu hình retention, xóa dữ liệu, thông báo cho trẻ |
| Người chịu trách nhiệm DPIA | Nguyễn Thành Trung (Product/Release owner) | Duy trì hồ sơ, điều phối rà soát pháp lý và sự cố |
| Tư vấn pháp lý/DPO | Cần chỉ định | Xác nhận vai trò pháp lý, mẫu hồ sơ, thời hạn thông báo, ký duyệt |
| Đầu mối an toàn thông tin | Kênh bảo mật GitHub của repo | Tiếp nhận báo cáo riêng tư/bảo mật, phối hợp điều tra |

**Câu hỏi pháp lý còn mở (cần luật sư xác nhận):**

1. Khi phần mềm cài cục bộ và nhà phát hành không truy cập dữ liệu, ai là bên kiểm soát/bên xử lý
   theo Luật 91/2025/QH15?
2. Hồ sơ DPIA do nhà phát hành lập hay từng phụ huynh lập; có cần nộp thay mặt người dùng không?
3. Mẫu thông báo/DPIA áp dụng (tham chiếu Mẫu 02a/02b và Mẫu 10 theo hướng dẫn hiện hành) và thời
   hạn nộp 60 ngày kể từ khi bắt đầu xử lý có áp dụng cho sản phẩm cài cục bộ không?
4. Yêu cầu đồng ý của trẻ từ đủ 7 tuổi được thực hiện trong gia đình như thế nào để đủ bằng chứng?

Cho tới khi được xác nhận, sản phẩm áp dụng cách tiếp cận thận trọng: minh bạch đầy đủ, xin đồng ý
của phụ huynh, nhắc phụ huynh thông báo cho trẻ, không hỗ trợ giám sát che giấu.

---

## 3. Data map

| Nhóm dữ liệu | Ví dụ trường | Nguồn | Mục đích | Nơi lưu | Thời hạn | Truy cập | Chia sẻ | Mức nhạy cảm |
|---|---|---|---|---|---|---|---|---|
| Cấu hình | bật bảo vệ/giám sát, ngưỡng, retention, camera, update check | Phụ huynh | Vận hành đúng lựa chọn | `AppSettings` | Theo vòng đời cài đặt | Parent Mode (PIN) | Không | Trung bình |
| Xác thực | hash PIN/recovery, salt, số lần sai, lockout | Phụ huynh | Chống truy cập trái phép | `ParentCredentials` | Theo vòng đời cài đặt | Tiến trình app | Không | Cao (bảo mật) |
| Phiên học | start/end, heartbeat, counters, confidence, blocked count, note | App | Báo cáo thời gian học | `StudySessions` | 365 ngày (cấu hình) | Parent Mode | Không | Trung bình |
| Mẫu hoạt động | app, process, window title đã làm sạch, domain/URL đã làm sạch, category, idle, online class | Hệ điều hành/UIA | Phân loại và báo cáo | `ActivitySamples` | 30 ngày (cấu hình) | Parent Mode/CSV | Không | **Cao** (title có thể chứa tên tài liệu) |
| Sự kiện tập trung | state, start/end, confidence, app/domain, note | Camera + activity | Ước lượng tập trung | `AttentionEvents` | 90 ngày (cấu hình) | Parent Mode/CSV | Không | **Cao** (dữ liệu trẻ em) |
| Lần truy cập bị chặn | target type/value, domain/URL đã làm sạch, process, window title, observed via | Block page/watcher | Minh bạch nỗ lực truy cập | `BlockedAttempts` | 90 ngày (cấu hình) | Parent Mode | Không | **Cao** |
| Audit | actor, event type, description, correlation id | App | Truy vết thay đổi nhạy cảm | `AuditLogs` | 365 ngày | Hỗ trợ/điều tra | Không | Trung bình |
| Log kỹ thuật | timestamp, level, category, correlation id, message đã redact | App | Chẩn đoán lỗi | `%LocalAppData%\EduFocus\logs` | 14 ngày, 10 MB/tệp | Người dùng máy | Không | Trung bình |
| Backup/khôi phục | bản sao DB, bản sao hosts, journal | App | Chống mất dữ liệu/khôi phục chặn | `%ProgramData%\EduFocus\backups`, `hosts-backup`, `runtime` | 3 bản backup; hosts xóa sau restore | SYSTEM/Administrators | Không | Trung bình |
| Bản xuất CSV | tổng hợp phiên, app/domain, sự kiện | Phụ huynh | Chia sẻ nội bộ do phụ huynh chọn | `%LocalAppData%\EduFocus\exports` | Đến khi phụ huynh xóa | Phụ huynh | Phụ huynh quyết định | **Cao** |
| Yêu cầu cập nhật | IP, User-Agent | GitHub (khi bật) | Kiểm tra phiên bản | Không lưu cục bộ | Không áp dụng | GitHub | GitHub | Thấp |

**Không có trong data map:** frame/ảnh/video/audio, landmark/embedding khuôn mặt, clipboard, nội dung
trang, lịch sử duyệt đầy đủ, vị trí, danh bạ, email, dữ liệu sinh trắc học.

---

## 4. Đồng ý và minh bạch

- Onboarding bắt buộc hiển thị tóm tắt quyền riêng tư, liên kết mở **toàn văn thông báo** trong ứng
  dụng và hai checkbox: đã đọc thông báo; đã thông báo cho học sinh và nhận đồng ý trong gia đình.
- Bước privacy đứng **trước** bước bật giám sát; nút xem lại thông báo cũng có ngay cạnh tùy chọn
  giám sát và trong Parent Mode.
- Timestamp `PrivacyAcceptedAtUtc`/`GuardianAcknowledgedAtUtc` được lưu bền vững; domain từ chối
  hoàn tất onboarding nếu thiếu consent. Không có đường bỏ qua từ UI.
- Thông báo được cài kèm (`docs/privacy-notice.md` trong thư mục cài đặt), hiển thị trong app và
  liên kết từ README/installer.
- Rút lại đồng ý: tắt giám sát (dừng camera), tắt bảo vệ, hoặc gỡ cài đặt và xóa dữ liệu.
- Kiểm tra cập nhật mặc định tắt; khi bật, UI nêu rõ GitHub thấy IP và không có dữ liệu học tập
  được gửi.

---

## 5. Nguyên tắc tối thiểu hóa và biện pháp kỹ thuật

| Nguyên tắc | Cách thực hiện |
|---|---|
| Tối thiểu hóa dữ liệu | Chỉ lưu app/process/domain/category và sự kiện trạng thái; không lưu frame, landmark, nội dung trang, clipboard |
| Giới hạn mục đích | Dữ liệu chỉ dùng cho báo cáo học tập và vận hành bảo vệ; không quảng cáo, không bán |
| Lưu trữ hạn chế | Retention 30/90/365 ngày + audit 365 + log 14 ngày; retention job hằng ngày |
| Minh bạch | Thông báo đầy đủ trong app/installer; nhãn camera, trạng thái giám sát; báo cáo nêu coverage thấp |
| Bảo vệ dữ liệu | ACL Administrators/SYSTEM, hash PBKDF2, redaction log, không log domain/title, không secret trong audit |
| Xóa được | Xóa toàn bộ trong Parent Mode (PIN + `XÓA`), uninstall `DELETEUSERDATA=1`, CSV do phụ huynh xóa |
| Không chuyển dữ liệu | Không cloud/telemetry; update check opt-in chỉ tới GitHub khi phụ huynh bật |
| Kiểm soát truy cập | Parent Mode khóa sau 5 phút idle, lockout 5 lần/15 phút, PIN không lưu bản rõ |
| An toàn camera | Frame trong RAM, dispose trước khi phát event; không ghi disk/network; camera chỉ mở trong phiên |

**Ghi chú window title:** đây là dữ liệu nhạy cảm tiềm tàng (tên tài liệu). Hiện title được làm sạch,
cắt ngắn, chỉ dùng để phân loại và **không xuất** trong báo cáo/CSV. Thông báo quyền riêng tư nêu rõ
điều này (finding F-42-01). Phương án bỏ lưu title được cân nhắc ở bản sau nếu không cần cho mục
đích học tập; khi đó cần đo lại độ chính xác phân loại.

---

## 6. Đánh giá rủi ro

Thang điểm: Khả năng (L) và Tác động (I) 1–5; mức = L×I (1–6 thấp, 8–12 trung bình, 15–25 cao).

| # | Rủi ro | L | I | Mức | Biện pháp hiện có | Mức còn lại |
|---|---|---|---|---|---|---|
| R1 | Học sinh/người dùng cùng tài khoản Windows đọc dữ liệu nhạy cảm | 3 | 4 | 12 | ACL Administrators/SYSTEM; cảnh báo trong SECURITY; Parent Mode PIN | Trung bình — phụ thuộc tài khoản Windows |
| R2 | CSV xuất ra ngoài bị lộ (email, USB, cloud cá nhân) | 3 | 4 | 12 | Chỉ phụ huynh chủ động xuất; cảnh báo bảo vệ tệp; không auto-upload; escape formula | Trung bình |
| R3 | Window title trong DB chứa tên tài liệu nhạy cảm | 3 | 3 | 9 | Làm sạch/cắt ngắn; không xuất báo cáo/CSV; retention 30/90 ngày; thông báo rõ | Thấp–trung bình |
| R4 | Camera/attention bị hiểu sai hoặc dùng để gán nhãn trẻ | 3 | 3 | 9 | Chỉ ước lượng, hiển thị coverage/confidence, cảnh báo dữ liệu chưa đủ tin cậy; không nhận diện danh tính | Trung bình |
| R5 | Giám sát che giấu/không đồng ý trong gia đình | 2 | 5 | 10 | Onboarding yêu cầu xác nhận đã thông báo cho trẻ; privacy notice hiển thị trước khi bật monitoring và trong MSI; tài liệu nhấn mạnh không hỗ trợ che giấu | Trung bình |
| R6 | Lộ dữ liệu qua log kỹ thuật | 2 | 4 | 8 | Redaction PIN/recovery/token/URL; không log domain/title; log cục bộ 14 ngày | Thấp |
| R7 | Mất máy/ổ đĩa bị truy cập vật lý | 2 | 4 | 8 | ACL; dữ liệu không mã hóa toàn đĩa (BitLocker ngoài phạm vi) | Trung bình — khuyến nghị BitLocker |
| R8 | Admin Windows tắt/gỡ/bypass giám sát | 4 | 2 | 8 | Ghi nhận là giới hạn đã biết; watchdog chỉ chống crash | Trung bình — chấp nhận |
| R9 | Retention/xóa thất bại, dữ liệu sống quá hạn | 2 | 3 | 6 | Batch 500, báo lỗi từng bảng, audit `RetentionApplied`, test tự động | Thấp |
| R10 | Cấu hình sai làm camera bật ngoài phiên | 2 | 4 | 8 | Pipeline chỉ start khi bảo vệ + phiên + monitoring bật; test vòng đời | Thấp |
| R11 | Update check tiết lộ IP cho GitHub | 2 | 1 | 2 | Mặc định tắt; UI giải thích; không gửi dữ liệu học tập | Thấp |
| R12 | Thư viện bên thứ ba có license không phù hợp phát hành thương mại | 2 | 4 | 8 | Inventory tại [THIRD-PARTY-NOTICES.md](../THIRD-PARTY-NOTICES.md) + hạ FluentAssertions về 7.2.0 Apache-2.0; rà soát mỗi release | Thấp |
| R13 | Sự cố không được phát hiện/kéo dài | 2 | 5 | 10 | Kênh báo cáo riêng tư; quy trình §8; audit cục bộ | Trung bình — cần diễn tập |

Rủi ro cao nhất còn lại là R1/R2/R5 (truy cập gia đình, CSV, đồng thuận). Các biện pháp bổ sung:
khuyến nghị BitLocker, hướng dẫn phụ huynh dùng tài khoản Windows riêng cho trẻ, nhắc không gửi CSV
qua kênh công khai, và duy trì thông báo rõ ràng trong mọi bản phát hành.

---

## 7. Quy trình yêu cầu chủ thể dữ liệu

1. **Tiếp nhận:** qua GitHub Security Advisory (riêng tư) hoặc kênh liên hệ trong thông báo; không
   nhận dữ liệu nhạy cảm trong issue công khai.
2. **Xác minh:** xác nhận người gửi có liên quan tới thiết bị/gia đình; không yêu cầu PIN/recovery
   code.
3. **Thực hiện:** hướng dẫn thao tác trong app (xóa dữ liệu, tắt giám sát, xuất CSV) hoặc hỗ trợ
   gỡ cài đặt; nhà phát triển không truy cập dữ liệu trên máy nên không thể xóa thay.
4. **Thời hạn:** xác nhận tiếp nhận trong **72 giờ làm việc**; hướng dẫn/hoàn tất trong **30 ngày**
   kể từ khi xác minh, trừ khi pháp luật yêu cầu khác.
5. **Ghi nhận:** lưu biên nhận tối thiểu (thời điểm, loại yêu cầu, kết quả) **không kèm** dữ liệu
   học tập, PIN hay nội dung nhạy cảm; lưu tối đa 24 tháng.
6. **Từ chối:** nêu lý do hợp pháp và quyền khiếu nại tới cơ quan có thẩm quyền.

---

## 8. Quy trình xử lý sự cố dữ liệu

| Bước | Nội dung | Thời hạn mục tiêu |
|---|---|---|
| Phát hiện | Nhận báo cáo qua kênh riêng tư, log/audit cục bộ, hoặc rà soát phát hành | Ngay khi nhận |
| Phân loại | Xác định loại dữ liệu, số người ảnh hưởng, nguyên nhân, mức rủi ro | ≤ 24 giờ |
| Ngăn chặn | Vô hiệu hóa bản phát hành/tính năng lỗi, hướng dẫn người dùng tắt giám sát/xóa dữ liệu, thu hồi artifact | ≤ 72 giờ |
| Khắc phục | Vá lỗi, phát hành bản sửa, kiểm thử hồi quy và test bảo mật | Theo mức độ |
| Thông báo | Thông báo cho người bị ảnh hưởng và cơ quan có thẩm quyền theo thời hạn luật định (dự kiến 72 giờ kể từ khi xác nhận; luật sư xác nhận mẫu và thời hạn) | ≤ 72 giờ |
| Hậu kiểm | Ghi biên bản, cập nhật DPIA/risk register, bổ sung test/checklist | ≤ 30 ngày |

**Đầu mối sự cố:** Nguyễn Thành Trung (Product/Release owner) phối hợp với tư vấn pháp lý và đầu mối
an toàn thông tin. Mọi bằng chứng được lưu riêng tư, không chứa PIN/recovery code/dữ liệu học tập
trong báo cáo công khai.

---

## 9. Retention và xóa dữ liệu — ánh xạ triển khai

| Bảng/tệp | Cơ chế | Mặc định | Ghi chú |
|---|---|---|---|
| `ActivitySamples` | `RetentionService` theo `ActivityRetentionDays` | 30 ngày | Không xóa sample của phiên `Active` |
| `AttentionEvents` | `EventRetentionDays` | 90 ngày | Không xóa event còn mở |
| `BlockedAttempts` | `EventRetentionDays` | 90 ngày | `SessionId` chuyển NULL khi session bị xóa |
| `StudySessions` | `SessionRetentionDays` | 365 ngày | Chi tiết xóa trước, session sau |
| `AuditLogs` | Cố định 365 ngày | 365 ngày | Không chứa secret |
| Log kỹ thuật | `FileLogger` rotation | 14 ngày / 10 MB | Redaction trước khi ghi |
| Backup DB | `SqliteBackupStore` | 3 bản | Tạo trước migration |
| Hosts backup | `HostsFileBlocker` | Xóa sau restore/self-heal | Chỉ trong thư mục backup |
| CSV | Người dùng | Không tự xóa | Nằm ngoài DB; UI nhắc bảo vệ tệp |

Xóa toàn bộ dữ liệu trong Parent Mode xóa sample → event → blocked attempt → session không `Active`
theo batch 500, giữ settings/PIN/audit và dữ liệu phiên đang chạy, chạy `VACUUM` best-effort và ghi
audit `DataDeleted`. Lỗi từng bảng được báo rõ, không báo thành công giả.

---

## 10. Rà soát và ký duyệt

- Rà soát lại DPIA **trước mỗi bản phát hành lớn**, khi thêm nhóm dữ liệu/tính năng mới, khi đổi
  retention/chia sẻ, hoặc khi có sự cố; tối thiểu **mỗi 12 tháng**.
- Kết quả rà soát được ghi vào [release-checklist.md](./release-checklist.md) §2.2/§2.6.
- Hồ sơ cần được luật sư/DPO ký duyệt và lưu cùng tài liệu phát hành tối thiểu 365 ngày.

| Vai trò | Tên | Ngày | Kết luận |
|---|---|---|---|
| Product/Release owner | Nguyễn Thành Trung | 2026-10-30 | Hoàn chỉnh về kỹ thuật, chờ rà soát pháp lý |
| Kỹ thuật bảo mật | | | |
| Tư vấn pháp lý/DPO | | | |
| Quản lý phát hành | | | |

---

## 11. Kết luận

EduFocus xử lý dữ liệu nhạy cảm của trẻ nhưng áp dụng kiến trúc local-first, không lưu frame/video,
không nhận diện danh tính, không upload mặc định, có retention/xóa và ACL. Các rủi ro chính còn
lại liên quan tới môi trường gia đình (tài khoản Windows dùng chung, CSV do phụ huynh chia sẻ) và
đồng thuận trong gia đình; các biện pháp giảm thiểu đã được ghi trong thông báo và hướng dẫn.

Hồ sơ đã đủ để chuyển tư vấn pháp lý. **Không phát hành thương mại** cho tới khi:
luật sư/DPO xác nhận vai trò pháp lý và mẫu hồ sơ; thông báo quyền riêng tư được duyệt; danh sách
license bên thứ ba được rà soát; và các finding P0/P1 của checklist phát hành được đóng.
