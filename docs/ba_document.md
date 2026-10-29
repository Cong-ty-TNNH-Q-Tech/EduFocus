# Tài liệu Phân tích Nghiệp vụ (BA) - EduFocus

> **Trạng thái:** Thiết kế sản phẩm / đang chuyển đổi từ dự án tiền nhiệm **Don't Be Lazy**.
> **Phiên bản tài liệu:** 2.0
> **Cập nhật:** 2026-10-26

---

## 1. Tổng quan

**EduFocus** là ứng dụng desktop dành cho phụ huynh giám sát và hỗ trợ học sinh sử dụng máy tính đúng mục đích học tập.

Ứng dụng hoạt động theo mô hình **parent-controlled**:

- Phụ huynh tạo PIN và là người duy nhất có quyền bật/tắt chế độ bảo vệ, sửa danh sách chặn, xem báo cáo chi tiết và thoát ứng dụng.
- Học sinh sử dụng máy trong môi trường đã được giới hạn: các trang chatbot/AI bị chặn khi chế độ bảo vệ đang bật.
- Ứng dụng ghi nhận phiên học, ứng dụng/trang web đang dùng và ước lượng mức độ tập trung bằng webcam.
- Toàn bộ dữ liệu được xử lý và lưu cục bộ trên máy. Không có cloud, không có tài khoản trực tuyến.

**Định vị:** Công cụ đồng hành học tập có sự đồng thuận của gia đình — **không phải** phần mềm do thám, keylogger hay hệ thống chống gian lận thi cử.

---

## 2. Vấn đề & Giải pháp

### 2.1. Vấn đề

1. Học sinh mở máy tính để học online nhưng dễ chuyển sang chatbot AI, mạng xã hội, game hoặc video giải trí.
2. Phụ huynh không có công cụ đơn giản để giới hạn môi trường học tập mà không phải cấu hình router, firewall hay phần mềm doanh nghiệp.
3. Các ứng dụng chặn web thông thường dễ bị tắt hoặc gỡ; không có lớp xác thực phụ huynh.
4. Khi học qua Google Meet, Microsoft Teams hoặc lớp học trực tuyến, phụ huynh khó biết con có thực sự ngồi trước màn hình hay không.
5. Các giải pháp giám sát hiện có thường thu thập quá nhiều dữ liệu (ảnh, video, nội dung trò chuyện) hoặc gửi lên cloud, gây lo ngại về quyền riêng tư.

### 2.2. Giải pháp

EduFocus cung cấp một lớp bảo vệ và quan sát **tối thiểu, minh bạch, cục bộ**:

- **Protection ON/OFF bằng PIN phụ huynh.**
- **Chặn chatbot** và các nhóm nội dung phụ huynh chọn.
- **Theo dõi phiên học** và ứng dụng/domain đang active (URL là best-effort).
- **Ước lượng sự chú ý** bằng webcam (không lưu ảnh/video).
- **Báo cáo trực quan** cho phụ huynh.
- **Chống tắt ứng dụng** ở mức hợp lý, có ghi nhận và tự phục hồi.

---

## 3. Mục tiêu & Phi mục tiêu

### 3.1. Mục tiêu

| Mã | Mục tiêu |
|---|---|
| G1 | Phụ huynh bật/tắt bảo vệ bằng PIN trong tối đa 3 thao tác. |
| G2 | Chặn hiệu quả các trang chatbot phổ biến khi bảo vệ đang bật. |
| G3 | Ghi nhận được thời gian học, ứng dụng/domain đang dùng theo từng phiên (URL best-effort). |
| G4 | Ước lượng mức độ tập trung khi học online và hiển thị rõ độ tin cậy. |
| G5 | Báo cáo dễ hiểu cho phụ huynh: thời gian học, mất tập trung, vi phạm. |
| G6 | Không lưu ảnh/video khuôn mặt; dữ liệu chỉ nằm trên máy. |
| G7 | Tự phục hồi trạng thái bảo vệ sau crash/restart. |

### 3.2. Phi mục tiêu (MVP)

- Không ghi màn hình, không chụp ảnh, không keylogger, không đọc nội dung chat.
- Không gửi dữ liệu lên cloud hoặc server của nhà phát triển.
- Không theo dõi vị trí, GPS, micro hay lịch sử duyệt web ngoài phạm vi phiên học.
- Không phải hệ thống chống gian lận thi cử hoặc giám sát tập trung (MDM).
- Không hỗ trợ macOS/Linux trong MVP.
- Không đảm bảo chống bypass tuyệt đối trước người dùng có quyền quản trị Windows.
- Không nhận diện danh tính, không lưu face embedding, không phân tích cảm xúc.

---

## 4. Đối tượng người dùng & Bối cảnh

### 4.1. Đối tượng chính

| Đối tượng | Mô tả | Nhu cầu chính |
|---|---|---|
| **Phụ huynh** | Người quản lý máy tính của con | Bật/tắt bảo vệ nhanh, chặn chatbot, xem báo cáo, nhận biết con có tập trung không |
| **Học sinh** | Trẻ 10–18 tuổi dùng máy để học | Môi trường học tập ít xao nhãng, được biết mình đang được giám sát |
| **Giáo viên / gia sư** | Người hỗ trợ học tập (không phải người dùng chính) | Có thể được phụ huynh chia sẻ báo cáo dạng xuất file |

### 4.2. Bối cảnh sử dụng

- Máy tính cá nhân hoặc máy tính dùng chung trong gia đình, Windows 11 64-bit (Windows 10 22H2 best-effort, đã hết hỗ trợ chính thức).
- Phụ huynh có tài khoản Windows quản trị; học sinh dùng tài khoản người dùng chuẩn.
- Có webcam tích hợp hoặc webcam ngoài cho tính năng ước lượng tập trung.
- Học online qua Google Meet, Microsoft Teams, Zoom hoặc các nền tảng lớp học.

### 4.3. Giả định

- Phụ huynh và học sinh đã được thông báo và đồng ý với việc giám sát.
- Máy tính có kết nối Internet khi cần chặn web.
- Phần mềm diệt virus có thể yêu cầu thêm ngoại lệ cho việc sửa file `hosts`.
- Windows 11 có thể cần bật **Multi-App Camera** để Meet/Teams và EduFocus cùng dùng webcam.

---

## 5. Vai trò & Quyền hạn

| Chức năng | Phụ huynh | Học sinh | Hệ thống |
|---|---|---|---|
| Tạo PIN lần đầu | ✅ | ❌ | ✅ |
| Đăng nhập Parent Mode | ✅ | ❌ | ✅ |
| Bật bảo vệ | ✅ (PIN) | ❌ | ✅ (theo lịch) |
| Tắt bảo vệ | ✅ (PIN) | ❌ | ✅ (hết lịch) |
| Sửa blocklist / allowlist | ✅ | ❌ | ❌ |
| Xem báo cáo chi tiết | ✅ | ❌ | ✅ (tổng hợp) |
| Xem trạng thái bảo vệ hiện tại | ✅ | ✅ | ✅ |
| Xem thời gian học của chính mình | ✅ | ✅ | ✅ |
| Thoát ứng dụng | ✅ (PIN) | ❌ (luôn cần PIN) | ✅ (hệ thống) |
| Xoá dữ liệu | ✅ | ❌ | ✅ (retention) |
| Đổi / khôi phục PIN | ✅ | ❌ | ❌ (chỉ xác thực recovery code) |

---

## 6. Danh sách tính năng

### 6.1. Tính năng cốt lõi (MVP)

#### F01 — Thiết lập ban đầu & PIN phụ huynh
- Wizard lần đầu: giới thiệu, yêu cầu đồng ý giám sát, tạo PIN 6–8 chữ số, hiển thị recovery code một lần.
- Không có PIN mặc định. Không thể bỏ qua bước tạo PIN.
- PIN được hash bằng PBKDF2-SHA256 + salt; không lưu plaintext.

#### F02 — Đăng nhập Parent Mode
- Màn hình khóa phụ huynh; đăng nhập bằng PIN.
- Parent session tự khóa sau 5 phút không hoạt động hoặc khi đóng app.
- Sai PIN hoặc recovery code 5 lần → khóa 15 phút dùng chung; ghi audit log.

#### F03 — Bật/tắt bảo vệ
- Toggle **Protection ON/OFF**.
- Bật: áp blocklist, bắt đầu study session, bật activity tracking và attention monitoring (nếu được cấu hình).
- Tắt: gỡ chặn, dừng monitoring, kết thúc session.
- Thay đổi thủ công cần PIN; thay đổi do scheduler ghi audit với actor `System`. Mọi thay đổi trạng thái đều được ghi audit log.

#### F04 — Chặn chatbot & quản lý danh sách chặn
- Danh sách chatbot mặc định: ChatGPT, Claude, Gemini, Copilot, Perplexity, Poe, Character.AI, DeepSeek, Grok, Meta AI, You.com, Pi, Hugging Face Chat…
- Phụ huynh có thể:
  - Bật/tắt từng nhóm hoặc từng domain.
  - Thêm domain/app tùy chỉnh.
  - Thêm **allowlist** cho domain cần thiết (ưu tiên cao hơn blocklist).
  - Đặt thời hạn cho rule ngoại lệ.
- **MVP chặn theo domain chính xác và app.** URL/path, wildcard subdomain và chặn theo ngữ cảnh cần browser extension (post-MVP).
- Trang chặn là HTML tĩnh, chỉ phục vụ trên loopback. Trang chỉ hiển thị với HTTP; với HTTPS, trình duyệt thường báo lỗi kết nối/SSL dù domain vẫn bị chặn.
- **Blocked attempt chỉ được ghi khi hệ thống quan sát được** (HTTP block page, app bị chặn, browser extension sau này). Hosts file không tự tạo log cho truy cập HTTPS. Báo cáo phải ghi rõ đây là số liệu quan sát được, không phải tổng số tuyệt đối.
- **Chặn app:** gửi `WM_CLOSE` trước, chờ tối đa 2 giây; nếu process vẫn chạy thì kill process tree. Chỉ áp dụng process thuộc rule Block, không kill process hệ thống/EduFocus hoặc process có rule Allow. Việc kill có thể làm mất dữ liệu chưa lưu; UI phải thông báo và giới hạn này phải được ghi rõ.

#### F05 — Lịch bảo vệ tự động
- Cấu hình nhiều khung giờ theo ngày trong tuần (ví dụ 19:00–21:00 từ T2–T6).
- Tự bật bảo vệ khi vào khung giờ; tự tắt khi hết khung giờ.
- Nếu app khởi động trong khung giờ, bảo vệ được áp dụng ngay.
- Phụ huynh có thể tạm tắt bằng PIN; override có hiệu lực **đến hết khung giờ hiện tại**. Khung giờ kế tiếp sẽ tự bật lại. Ngoài khung giờ, không tự bật lại.

#### F06 — Phiên học & theo dõi hoạt động
- Study session bắt đầu khi Protection ON (thủ công hoặc theo lịch).
- Ghi nhận mỗi 10–30 giây:
  - Ứng dụng foreground.
  - Tiêu đề cửa sổ.
  - URL/domain nếu lấy được.
  - Thời gian idle.
- Nhận diện Google Meet, Microsoft Teams, Zoom và nền tảng lớp học.
- Tổng hợp thời lượng theo `Study`, `Distraction`, `Neutral`; `OnlineClass` là overlay, không cộng trùng. Thời gian idle lưu riêng.
- Session có `ActiveSeconds` và `LastHeartbeatAtUtc` để không cộng thời gian máy sleep hoặc app tắt.
- URL detection là best-effort (UI Automation + window title). Không lưu nội dung trang, không lưu lịch sử duyệt web ngoài phiên.

#### F07 — Ước lượng mức độ tập trung
- Webcam được bật trong phiên học khi monitoring được bật và (nếu cấu hình) khi phát hiện lớp online.
- Pipeline cục bộ:
  - Phát hiện khuôn mặt.
  - Ước lượng hướng đầu.
  - Ước lượng hướng mắt/iris (coarse).
  - Phát hiện nhắm mắt/buồn ngủ.
- Phân loại trạng thái: `Focused`, `Distracted`, `Absent`, `Drowsy`, `CameraUnavailable`.
- Không lưu frame; chỉ lưu sự kiện và điểm tổng hợp.
- Có calibration cơ bản tùy chọn; calibration nâng cao/iris chính xác hơn thuộc post-MVP.
- Hiển thị rõ **độ phủ camera** và **độ tin cậy** trong báo cáo. Coverage tính trên thời gian monitoring được kỳ vọng (`MonitoringEligibleSeconds`), không chia cho toàn bộ session nếu chỉ monitor trong lớp online.
- Không mở được camera hoặc không có webcam: ghi `CameraUnavailable`, không suy diễn mất tập trung.

#### F08 — Báo cáo phụ huynh
- Dashboard hôm nay: thời gian học, % tập trung, số lần bị chặn (quan sát được), số lần mất tập trung.
- Timeline hoạt động theo giờ.
- Danh sách sự kiện: mất tập trung, vắng mặt, buồn ngủ, camera không khả dụng.
- Báo cáo tuần/tháng: dùng session summary cho dữ liệu dài hạn.
- Dữ liệu chi tiết (timeline, top app/domain) chỉ có trong thời hạn retention của `ActivitySamples` (mặc định 30 ngày).
- Xuất CSV.
- Không hiển thị ảnh/video.

#### F09 — Chống tắt & tự phục hồi
- Ứng dụng chạy nền, minimize xuống tray.
- Đóng cửa sổ không thoát app.
- Thoát app luôn cần PIN (trừ Windows shutdown/logoff).
- Tự khởi động cùng Windows bằng Scheduled Task khi Protection ON hoặc ScheduleEnabled.
- Tự phục hồi trạng thái chặn/session sau crash hoặc restart.
- Watchdog kiểm tra và khởi động lại app nếu bị kill khi Protection ON (MVP: scheduled task, kiểm tra mỗi 60 giây; Production: Windows Service). Không restart sau exit sạch.
- Uninstall có cảnh báo/PIN ở mức best-effort; admin Windows vẫn có thể gỡ.
- Ghi audit log khi có nỗ lực tắt/sửa.

#### F10 — Quyền riêng tư & quản lý dữ liệu
- Thông báo rõ cho phụ huynh và học sinh về dữ liệu được thu thập.
- Chỉ xử lý webcam cục bộ, không lưu ảnh/video.
- Chỉ lưu dữ liệu tổng hợp và sự kiện.
- Retention mặc định:
  - Activity sample: 30 ngày.
  - Attention event / blocked attempt: 90 ngày.
  - Session summary: 365 ngày.
  - Audit log: 365 ngày.
- Phụ huynh có thể xóa dữ liệu báo cáo theo khoảng thời gian; thao tác xóa không xóa PIN/credential trừ khi chọn reset ứng dụng.
- Không gửi dữ liệu học tập ra ngoài. Nếu có kiểm tra cập nhật, chỉ gửi phiên bản/OS, có thể tắt; server vẫn có thể thấy IP.

### 6.2. Tính năng mở rộng (Post-MVP)

| Mã | Tính năng | Ghi chú |
|---|---|---|
| F11 | Browser extension + native messaging | Lấy URL chính xác, chặn theo path, chặn AI sidebar |
| F12 | Windows Service `EduFocus.Agent` | Chống tắt mạnh hơn, watchdog SYSTEM |
| F13 | Companion app điện thoại | Camera thứ hai khi webcam bị Meet/Teams chiếm |
| F14 | Windows Hello cho phụ huynh | Thay PIN nếu dùng tài khoản Windows riêng |
| F15 | Nhiều hồ sơ học sinh | Mỗi con có lịch, blocklist, báo cáo riêng |
| F16 | Báo cáo PDF | Xuất báo cáo tuần cho gia đình/gia sư |
| F17 | Gợi ý cải thiện | Phân tích xu hướng, gợi ý thời gian học |
| F18 | Đồng bộ tùy chọn | Chỉ khi có thiết kế bảo mật rõ ràng; không thuộc MVP |

### 6.3. Tính năng sẽ loại bỏ khỏi sản phẩm cũ

- Quản lý task, task lặp, kéo thả ưu tiên.
- Whitelist Profile theo task.
- Strict Mode tự ép bản thân, guilt-trip, friction, streak.
- Quote / Gemini AI / AI task breakdown / AI profile generator.
- Focus timer Pomodoro.
- Dữ liệu và migration từ `DontBeLazy` 1.x.

---

## 7. Quy tắc nghiệp vụ (Business Rules)

| Mã | Quy tắc |
|---|---|
| BR-01 | Lần chạy đầu tiên bắt buộc tạo PIN phụ huynh; không có PIN mặc định. |
| BR-02 | PIN lưu dưới dạng hash PBKDF2-SHA256, salt ngẫu nhiên, tối thiểu 600.000 iterations; không lưu plaintext. |
| BR-03 | Sai PIN hoặc recovery code 5 lần liên tiếp → khóa 15 phút dùng chung; mọi lần thử sai được ghi audit. |
| BR-04 | Parent session tự khóa sau 5 phút không hoạt động. |
| BR-05 | Mọi thay đổi thủ công Protection ON/OFF, blocklist, schedule, retention, exit đều cần PIN; scheduler được phép tự bật/tắt và ghi audit actor `System`. |
| BR-06 | Protection OFF phải gỡ toàn bộ chặn và dừng monitoring ngay lập tức. |
| BR-07 | Schedule chỉ tự động bật/tắt; không ghi đè PIN. Phụ huynh vẫn có thể override bằng PIN đến hết khung giờ hiện tại; bật thủ công sẽ xóa override. |
| BR-08 | Allowlist có ưu tiên cao hơn blocklist. Rule hết hạn tự động bị bỏ qua. |
| BR-09 | MVP chỉ chặn domain chính xác và app; không chặn domain cấp cao dùng chung (`google.com`, `microsoft.com`). Biến thể `www.` chỉ được thêm khi không có rule Allow cho biến thể đó. `Suffix`/URL/`Contains` chỉ dùng với browser extension và phải so khớp theo ranh giới domain (`.example.com`), không so khớp chuỗi thô. |
| BR-10 | MVP chặn bằng hosts; trang chặn chỉ hiển thị với HTTP. Với HTTPS, domain vẫn bị chặn nhưng browser báo lỗi. `BlockedAttempt` chỉ được ghi khi quan sát được (HTTP block page, app bị chặn, browser extension); báo cáo phải ghi rõ đây là số liệu best-effort. |
| BR-11 | Study session bắt đầu khi Protection ON và kết thúc khi Protection OFF hoặc hết lịch. Session lưu `ActiveSeconds` và heartbeat để loại trừ thời gian app tắt/sleep. |
| BR-12 | Attention monitoring chỉ chạy khi Protection ON, session active, monitoring được bật và (nếu cấu hình) đang trong lớp online. |
| BR-13 | Không lưu ảnh, video, âm thanh hoặc biometric template. Frame được xử lý trong bộ nhớ và hủy ngay sau khi suy ra trạng thái. |
| BR-14 | Nếu camera bận, không có quyền hoặc không có thiết bị, ghi `CameraUnavailable`; không suy diễn mất tập trung và không phạt. |
| BR-15 | `AttentionPercent = FocusedSeconds / (FocusedSeconds + DistractedSeconds + AbsentSeconds + DrowsySeconds)`, chỉ tính trên thời gian camera thực sự quan sát được. Báo cáo phải hiển thị coverage = `MonitoredSeconds / MonitoringEligibleSeconds`, không chia cho toàn bộ session nếu chỉ monitor trong lớp online. |
| BR-16 | Dữ liệu vượt retention tự động bị xóa; phụ huynh có thể xóa sớm hơn. Xóa dữ liệu báo cáo không xóa PIN/credential trừ khi chọn reset ứng dụng. |
| BR-17 | Sau crash/restart, nếu Protection đang ON, hệ thống phải tự áp dụng lại chặn. Session cũ kết thúc tại `LastHeartbeatAtUtc` với trạng thái `Interrupted`; nếu vẫn trong thời gian bảo vệ, tạo session mới `Trigger = Recovery`; không cộng thời gian app tắt. |
| BR-18 | Không thể thoát app từ tray nếu không nhập PIN, kể cả khi Protection OFF. Windows shutdown/logoff vẫn được phép. |
| BR-19 | Ứng dụng không thể chống lại người dùng có quyền Administrator hoặc quyền SYSTEM; đây là giới hạn đã biết. |
| BR-20 | Schedule dùng wall clock/local time để xác định khung giờ và lưu UTC cho timestamp. Monotonic clock chỉ dùng cho elapsed time trong một lần chạy và phát hiện nhảy giờ; không dùng monotonic xuyên reboot. Khi phát hiện nhảy giờ bất thường, ghi audit và không tự tắt bảo vệ chỉ vì thay đổi giờ. |
| BR-21 | Học sinh chỉ xem được trạng thái bảo vệ, thời gian học của mình và trang chặn; không xem báo cáo chi tiết. |
| BR-22 | Việc giám sát phải được thông báo công khai trong app; không có chế độ ẩn hoàn toàn. |
| BR-23 | Tên miền chatbot mặc định là dữ liệu đi kèm app, có thể cập nhật qua bản phát hành; phụ huynh có thể tùy chỉnh. |
| BR-24 | Blocklist không được ảnh hưởng đến `meet.google.com`, `teams.microsoft.com`, `classroom.google.com`, `accounts.google.com`, `login.microsoftonline.com`, `login.live.com`, `zoom.us` và các domain đăng nhập/lớp học thiết yếu. |
| BR-25 | URL/path, wildcard subdomain và chặn AI sidebar không thuộc MVP; cần browser extension sau MVP. |
| BR-26 | Block page phải là HTML tĩnh, bind loopback-only, không tải tài nguyên ngoài, escape mọi giá trị phản chiếu và không chứa dữ liệu nhạy cảm. |
| BR-27 | Mọi thay đổi rule khi Protection ON phải được áp dụng lại engine trong vòng 60 giây hoặc thông báo rõ nếu cần restart protection. |
| BR-28 | Watchdog chỉ restart app khi Protection ON và lần thoát trước không phải clean exit; exit hợp lệ có PIN phải dừng watchdog/đánh dấu clean shutdown. |
| BR-29 | Chặn app: ưu tiên gửi `WM_CLOSE`, chờ tối đa 2 giây rồi kill process tree nếu vẫn chạy; không kill process hệ thống/EduFocus/Allow; ghi `BlockedAttempt` với `ObservedVia = ProcessWatcher`; chấp nhận rủi ro mất dữ liệu chưa lưu và phải thông báo. |
| BR-30 | Thoát app hợp lệ bằng PIN sẽ đặt `ProtectionEnabled = false` và ghi clean exit; Windows shutdown/logoff giữ nguyên `ProtectionEnabled` để lần đăng nhập sau phục hồi và không ghi clean exit. |

---

## 8. User Stories & Tiêu chí chấp nhận

### US-01 — Thiết lập lần đầu
**Là phụ huynh**, tôi muốn tạo PIN và được giải thích rõ app sẽ giám sát gì, để tôi kiểm soát máy học tập của con.

**Acceptance criteria**
- [ ] Lần chạy đầu hiển thị wizard 3 bước: giới thiệu → đồng ý → tạo PIN.
- [ ] Không thể bỏ qua bước tạo PIN.
- [ ] Recovery code hiển thị một lần và có nút sao chép.
- [ ] PIN không được lưu plaintext.

### US-02 — Bật/tắt bảo vệ
**Là phụ huynh**, tôi muốn bật bảo vệ trước giờ học và tắt khi con học xong, để chặn chatbot đúng lúc.

**Acceptance criteria**
- [ ] Bật/tắt đều cần PIN.
- [ ] Khi ON, các domain chatbot mặc định bị chặn.
- [ ] Khi OFF, truy cập lại bình thường.
- [ ] Trạng thái được lưu và tự phục hồi sau restart.

### US-03 — Chặn chatbot
**Là phụ huynh**, tôi muốn chặn ChatGPT/Claude/Gemini… để con không dùng chatbot trong giờ học.

**Acceptance criteria**
- [ ] Các domain trong danh sách mặc định bị chặn trên Chrome, Edge, Firefox.
- [ ] Không chặn nhầm Google Meet, Teams hoặc trang đăng nhập.
- [ ] Có log domain/app bị chặn khi hệ thống quan sát được; báo cáo ghi rõ giới hạn HTTPS.
- [ ] Có allowlist tạm thời cho domain giáo viên yêu cầu.

### US-04 — Xem báo cáo
**Là phụ huynh**, tôi muốn biết con đã học bao lâu và mất tập trung bao nhiêu lần, để điều chỉnh thói quen học.

**Acceptance criteria**
- [ ] Dashboard hiển thị thời gian học, % tập trung, số lần bị chặn (best-effort).
- [ ] Có timeline theo giờ trong thời hạn dữ liệu chi tiết.
- [ ] Có ghi rõ thời gian camera không khả dụng và coverage.
- [ ] Không hiển thị ảnh/video.

### US-05 — Theo dõi khi học Meet/Teams
**Là phụ huynh**, tôi muốn biết con có ngồi trước màn hình trong lớp online không.

**Acceptance criteria**
- [ ] App nhận diện cửa sổ Meet/Teams đang active (best-effort theo process/window title).
- [ ] Nếu camera dùng chung được, attention monitoring hoạt động.
- [ ] Nếu camera bận, app ghi `CameraUnavailable` và báo rõ coverage trong báo cáo.
- [ ] Không lưu video lớp học.

### US-06 — Quyền riêng tư
**Là phụ huynh**, tôi muốn dữ liệu của con chỉ nằm trên máy và không bị gửi đi đâu.

**Acceptance criteria**
- [ ] Không có network request chứa dữ liệu camera/activity.
- [ ] Không có file ảnh/video trong thư mục dữ liệu.
- [ ] Có màn hình privacy giải thích rõ.
- [ ] Có chức năng xóa dữ liệu.

---

## 9. Yêu cầu phi chức năng (NFR)

### 9.1. Bảo mật
- PIN hash PBKDF2-SHA256, salt riêng, constant-time compare.
- Recovery code hash tương đương PIN; chỉ hiển thị một lần khi tạo/đổi, không thể hiển thị lại.
- DB và log đặt tại `%ProgramData%\EduFocus`, ACL chỉ cho Administrators/SYSTEM. Phụ huynh thao tác qua app elevated, không đọc file trực tiếp.
- Không lưu secret trong source, log hay file cấu hình plaintext.
- Mọi thao tác nhạy cảm ghi audit log (không ghi PIN).
- App yêu cầu quyền Administrator cho hosts/process. Firewall là post-MVP.

### 9.2. Quyền riêng tư
- Local-first, không telemetry mặc định.
- Camera frame chỉ xử lý trong RAM; không ghi disk, không gửi network.
- Không lưu face embedding, template sinh trắc học hoặc ảnh.
- Chỉ lưu dữ liệu tối thiểu cần cho báo cáo.
- Có thông báo rõ ràng và lựa chọn tắt attention monitoring.
- Tuân thủ nguyên tắc purpose limitation, data minimization, storage limitation.

### 9.3. Hiệu năng
- Idle (Protection OFF): CPU < 1%, RAM < 120 MB.
- Protection ON, không camera: CPU < 3%, RAM < 200 MB.
- Attention monitoring: CPU < 10% trên CPU 4 nhân hiện đại; sampling 1–2 FPS; RAM < 350 MB.
- Không ảnh hưởng đáng kể đến Google Meet/Teams khi dùng Multi-App Camera.
- Khởi động < 3 giây trên SSD.
- Block page phản hồi < 100 ms trên loopback; không tải tài nguyên ngoài.

### 9.4. Độ tin cậy
- Tự phục hồi trạng thái bảo vệ sau crash/restart.
- Không để lại hosts block mồ côi khi app thoát sạch.
- Có cơ chế self-heal khi khởi động, resume và sau lỗi engine.
- Session dùng heartbeat; crash không cộng thời gian app tắt vào `ActiveSeconds`.
- Đóng `AttentionEvent` đang mở tại `LastHeartbeatAtUtc` khi phục hồi.
- Xử lý camera bận/không có quyền mà không crash.
- Ghi log lỗi có correlation ID.

### 9.5. Tương thích
- Windows 11 23H2+ 64-bit là nền tảng chính; Windows 10 22H2 chỉ best-effort và không còn được hỗ trợ chính thức.
- Chrome, Edge, Firefox cho blocking qua hosts; block page chỉ hiển thị với HTTP.
- Google Meet, Microsoft Teams, Zoom cho nhận diện lớp online.
- Hỗ trợ webcam UVC tiêu chuẩn; ưu tiên webcam tích hợp.
- Windows 11 Multi-App Camera được phát hiện và hướng dẫn bật.
- Port 80 có thể bị ứng dụng khác chiếm; khi đó blocking vẫn hoạt động nhưng block page HTTP không khả dụng.

### 9.6. Khả dụng
- UI tiếng Việt, tối giản, dễ dùng cho phụ huynh không chuyên kỹ thuật.
- Onboarding ≤ 3 phút.
- Trạng thái bảo vệ luôn hiển thị rõ (icon tray + màu sắc).
- Trang chặn giải thích lý do và cách liên hệ phụ huynh.
- Hỗ trợ keyboard navigation và độ tương phản tối thiểu.

### 9.7. Khả bảo trì
- Clean Architecture, Ports & Adapters.
- Business logic trong UseCases, không nằm trong ViewModel.
- Có unit test cho domain/use case; integration test cho repo.
- Migration EF Core rõ ràng, có version.
- Logging có cấu trúc, xoay vòng theo ngày.

---

## 10. Tuân thủ, đạo đức & giới hạn

### 10.1. Đạo đức
- EduFocus phải được giới thiệu công khai với học sinh; không khuyến khích giám sát bí mật.
- Phụ huynh chịu trách nhiệm về sự đồng thuận trong gia đình.
- Không sử dụng dữ liệu để xếp hạng, trừng phạt hoặc chia sẻ công khai.
- Không phân tích cảm xúc, không nhận diện danh tính, không dự đoán hành vi.

### 10.2. Pháp lý
- Tại Việt Nam, dữ liệu khuôn mặt/sinh trắc học là **dữ liệu cá nhân nhạy cảm**. Việc xử lý cần tuân thủ **Luật Bảo vệ dữ liệu cá nhân 91/2025/QH15** (hiệu lực 01/01/2026) và **Nghị định 356/2025/NĐ-CP** (hiệu lực 01/01/2026, thay thế Nghị định 13/2023/NĐ-CP) cùng các văn bản hướng dẫn hiện hành.
- Nguyên tắc áp dụng: mục đích rõ ràng, tối thiểu hóa, lưu trữ hạn chế, minh bạch, có sự đồng ý của người đại diện hợp pháp và đánh giá tác động xử lý dữ liệu cá nhân (DPIA) trước khi phát hành.
- Không lưu ảnh/video làm giảm rủi ro nhưng không loại bỏ hoàn toàn nghĩa vụ thông báo, đồng thuận và đánh giá tác động.
- Tài liệu này không phải tư vấn pháp lý; cần luật sư/DPO xem xét trước khi phát hành thương mại.

### 10.3. Giới hạn kỹ thuật đã biết
- PIN không phải ranh giới bảo mật nếu học sinh dùng chung tài khoản Windows hoặc có quyền admin.
- Hosts file có thể bị bypass bởi DoH/VPN/proxy.
- Hosts không tạo log truy cập HTTPS; blocked attempts chỉ là best-effort.
- Trang chặn chỉ hiển thị với HTTP; HTTPS thường báo lỗi kết nối/SSL.
- URL/path, wildcard subdomain và AI sidebar chưa được enforce trong MVP.
- Windows shutdown/logoff vẫn là đường bypass; watchdog scheduled task có độ trễ tới khoảng 60 giây.
- Webcam không phải eye tracker hồng ngoại; attention chỉ là ước lượng.
- Meet/Teams có thể chiếm camera trên máy chưa hỗ trợ Multi-App Camera.
- Không thể đảm bảo phát hiện mọi chatbot mới hoặc chatbot chạy trong app khác.
- Process watcher dựa trên tên/đường dẫn process; app đổi tên, chạy portable hoặc khởi động/thoát quá nhanh có thể không bị phát hiện.

---

## 11. Đề xuất công nghệ

| Thành phần | Công nghệ |
|---|---|
| UI | WPF .NET 10 LTS (mục tiêu; code hiện tại vẫn `net9.0`, hết hỗ trợ 10/11/2026, cần nâng cấp), MaterialDesignInXAML, CommunityToolkit.Mvvm |
| Architecture | Clean Architecture + Ports & Adapters |
| Database | SQLite + EF Core 10 (mục tiêu; code hiện tại EF Core 9) |
| Chặn web | Windows hosts file + local HTTP block page (MVP, domain-level) |
| Chặn URL/path | Browser extension (Post-MVP) |
| Process / foreground window | Win32 API (`GetForegroundWindow`, WMI, `Process`) |
| URL tracking | UI Automation + window title (MVP), browser extension (Post-MVP) |
| Attention | OpenCvSharp4 + ONNX Runtime + MediaPipe Face/Iris models |
| Bảo mật PIN | PBKDF2-SHA256; không lưu secret khác |
| Installer | WiX Toolset v4 (MSI) |
| CI/CD | GitHub Actions |
| Logging | File logger xoay vòng theo ngày, không ghi dữ liệu nhạy cảm |

> Spike #4 (2026-10-29) đã xác nhận build/test/publish và MSI trên .NET 10 + EF Core 10; danh sách thay đổi bắt buộc, rollback và checklist DPIA ở [`docs/spikes/net10-ef10-dpia-evaluation.md`](./spikes/net10-ef10-dpia-evaluation.md).

---

## 12. Phạm vi MVP & Lộ trình

### 12.1. MVP bắt buộc

- PIN + Parent Mode.
- Protection ON/OFF.
- Chatbot blocking theo domain chính xác + app; blocked log best-effort.
- Study session + app/domain tracking cơ bản (URL best-effort).
- Attention estimate cơ bản (face presence + head pose + camera unavailable).
- Dashboard/report cơ bản; dữ liệu chi tiết 30 ngày, session summary 365 ngày.
- Auto-start, tray, crash recovery.
- Privacy notice + retention/xóa dữ liệu.

### 12.2. Sau MVP

- Browser extension.
- Windows Service agent.
- Calibration nâng cao + iris gaze.
- Multi-App Camera auto-setup.
- Companion phone camera.
- Báo cáo PDF, multi-child, Windows Hello.

### 12.3. Lộ trình

| Phase | Nội dung | Ước tính |
|---|---|---|
| 0 | Spike camera/blocking/tamper | 3–5 ngày |
| 1 | Dọn code cũ, rename EduFocus, schema mới | 2–3 ngày |
| 2 | PIN, Parent Mode, Protection toggle | 3–4 ngày |
| 3 | Blocking engine, blocklist, block page | 3–5 ngày |
| 4 | Study session, activity tracking | 3–4 ngày |
| 5 | Attention monitoring | 5–7 ngày |
| 6 | Dashboard, reports, settings | 4–6 ngày |
| 7 | Hardening, installer, CI, test | 4–5 ngày |

---

## 13. Rủi ro & Biện pháp

| Rủi ro | Mức độ | Biện pháp |
|---|---|---|
| Camera bị Meet/Teams chiếm | Cao | Spike sớm; Multi-App Camera; fallback `CameraUnavailable`; companion camera post-MVP |
| Chặn app làm mất dữ liệu chưa lưu | Trung bình | `WM_CLOSE` trước, kill sau 2 giây; cảnh báo trong UI/known limitations; chỉ kill app trong Block |
| Attention estimate sai | Cao | Calibration; hiển thị confidence/coverage; không dùng làm kết luận tuyệt đối |
| Bypass hosts bằng DoH/VPN | Cao | Browser extension + firewall/DNS policy; ghi nhận cấu hình mạng bất thường |
| Không log được truy cập HTTPS | Cao | Ghi rõ best-effort; browser extension sau MVP; không quảng cáo số liệu tuyệt đối |
| URL/path rule không được enforce trong MVP | Trung bình | Chỉ hỗ trợ domain/app trong MVP; URL rules chờ extension |
| Học sinh có quyền admin | Cao | Service SYSTEM + tài khoản Windows riêng; ghi rõ giới hạn |
| Chặn nhầm domain thiết yếu | Trung bình | Allowlist mặc định cho Google/Microsoft; test regression |
| Dữ liệu nhạy cảm của trẻ | Cao | Local-only, không lưu frame, retention, minh bạch, DPIA/tư vấn pháp lý |
| Xung đột antivirus | Trung bình | Ký số app; hướng dẫn exclusion; tránh driver kernel |
| PIN bị quên | Trung bình | Recovery code; quy trình reset qua Windows admin có cảnh báo |
| Hiệu năng khi chạy nền | Trung bình | Sampling thấp, ONNX CPU, tạm dừng khi pin yếu |
| License model ONNX/MediaPipe | Trung bình | Rà soát license và ghi attribution trước khi phân phối |

---

## 14. Chỉ số thành công

- ≥ 90% phụ huynh hoàn tất onboarding trong < 3 phút.
- ≥ 95% domain chatbot mục tiêu được áp dụng vào hosts trong kiểm thử.
- ≥ 80% phiên học có dữ liệu activity hợp lệ.
- ≥ 70% thời gian trong lớp online có camera khả dụng trên máy hỗ trợ Multi-App Camera.
- Báo cáo blocked attempts ghi rõ là best-effort, không dùng làm KPI tuyệt đối.
- Tỷ lệ crash < 1% phiên.
- Không có dữ liệu camera rời khỏi máy (kiểm chứng qua audit network).
- Phụ huynh hiểu báo cáo: ≥ 80% nắm được "coverage" và "confidence" sau khi đọc trợ giúp.

---

## 15. Thuật ngữ

| Thuật ngữ | Định nghĩa |
|---|---|
| **Protection** | Trạng thái bảo vệ: chặn web/app theo rule + chạy monitoring |
| **Parent Mode** | Phiên đăng nhập có quyền quản trị EduFocus |
| **Student Mode** | Giao diện giới hạn dành cho học sinh |
| **BlockRule** | Một rule chặn hoặc cho phép domain/app; URL rule là post-MVP |
| **Allowlist** | Danh sách ngoại lệ được ưu tiên hơn blocklist |
| **StudySession** | Một phiên học được ghi nhận từ lúc bật đến lúc tắt protection |
| **ActiveSeconds** | Thời gian session thực sự chạy, loại trừ app tắt/sleep |
| **AttentionEvent** | Sự kiện focused/distracted/absent/drowsy/camera không khả dụng |
| **Coverage** | % thời gian monitoring được kỳ vọng mà camera thực sự quan sát được |
| **Multi-App Camera** | Tính năng Windows 11 cho phép nhiều app dùng webcam cùng lúc |
| **Tamper resistance** | Các biện pháp chống tắt/sửa app trái phép |

---

## 16. Tài liệu liên quan

- [Use Cases](./use_cases.md)
- [System Architecture](./system_architecture.md)
- [Database Schema](./database_schema.md)
- [README](../README.md)
- [Security Policy](../SECURITY.md)
