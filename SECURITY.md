# 🔒 Security Policy — EduFocus

## Phiên bản được hỗ trợ

EduFocus đang ở giai đoạn **Pre-release / đang chuyển đổi**. Bảng dưới đây sẽ được cập nhật khi có bản phát hành chính thức.

| Version | Supported |
|---|---|
| Pre-release / `master` | 🔄 Đang phát triển |
| 2.x | 🔜 Chưa phát hành |
| 1.x (Don't Be Lazy) | ❌ Không còn hỗ trợ |

---

## Mô hình bảo mật

EduFocus là ứng dụng desktop **local-first** dành cho phụ huynh giám sát hoạt động học tập trên máy tính của con. Ứng dụng can thiệp vào hệ thống ở mức trung bình:

- Ghi file `hosts` để chặn domain (yêu cầu quyền Administrator).
- Chạy local HTTP block page bind loopback `127.0.0.1:80`, chỉ phục vụ HTML tĩnh; port 80 có thể bị ứng dụng khác chiếm.
- Theo dõi foreground window/process.
- Có thể kill process nằm trong blocklist.
- Truy cập webcam để ước lượng trạng thái tập trung (nếu bật).
- Tự khởi động và tự phục hồi sau crash; watchdog scheduled task kiểm tra mỗi 60 giây.

### Cam kết dữ liệu

- **Không gửi dữ liệu lên cloud/server.** Không telemetry mặc định. Update check (nếu có) phải opt-in, chỉ gửi version/OS; server vẫn có thể thấy IP.
- **Không lưu ảnh, video, âm thanh hoặc frame webcam.**
- **Không lưu nội dung trang, tin nhắn, clipboard hay lịch sử duyệt web đầy đủ.**
- **Không nhận diện danh tính/khuôn mặt.**
- Dữ liệu nằm trong `%ProgramData%\EduFocus`, được bảo vệ bằng ACL cho Administrators/SYSTEM. Ứng dụng áp ACL khi khởi tạo DB; file DB, WAL và backup kế thừa ACL này. Khởi tạo dữ liệu cần quyền Administrator.
- Backup trước migration được kiểm tra `PRAGMA integrity_check`; giữ tối đa 3 bản. Khi DB hỏng, ứng dụng giữ nguyên file và yêu cầu khôi phục từ backup, không tự xóa dữ liệu.
- Log kỹ thuật nằm trong `%LocalAppData%\EduFocus\logs`, tối đa 10 MB/file và giữ 14 ngày. PIN, recovery code, token và URL đầy đủ được redact trước khi ghi; audit event quan trọng nằm trong `AuditLogs` của DB, không ghi vào file log.
- PIN/recovery code chỉ lưu dưới dạng hash PBKDF2-SHA256 600.000 iterations, salt 16 byte, hash 32 byte; so sánh constant-time; dùng chung lockout 5 lần/15 phút. Recovery code 12 ký tự chỉ hiển thị một lần và được xoay sau khi dùng để reset PIN. Mỗi lần sai PIN/recovery code được ghi audit `AuthenticationFailed`; lần thứ 5 ghi thêm `LockoutTriggered`.
- Parent Mode session chỉ nằm trong bộ nhớ, tự khóa sau 5 phút idle hoặc khi đóng cửa sổ Parent Mode, và không được khôi phục sau khi app khởi động lại; đổi PIN thành công buộc đăng nhập lại.
- Onboarding lần chạy đầu là cổng bắt buộc: app chỉ mở cửa sổ chính sau khi phụ huynh xác nhận quyền riêng tư/trách nhiệm, thiết lập PIN và xác nhận đã lưu recovery code. Timestamp consent (`PrivacyAcceptedAtUtc`, `GuardianAcknowledgedAtUtc`) lưu trong `AppSettings` và không bị ghi đè khi lặp lại. Không có đường bỏ qua bước nào từ UI; domain từ chối hoàn tất onboarding khi thiếu consent.
- Onboarding chỉ **lưu** tùy chọn `StartWithWindows` và `MonitoringEnabled`; đăng ký auto-start thật và mở webcam thuộc issue #39/#30. Tiền kiểm tra camera chỉ đọc consent store của Windows, không mở thiết bị và không lưu frame.
- Recovery code lần đầu và mã tái phát hành khi tiếp tục onboarding đều chỉ hiển thị một lần; mã cũ bị vô hiệu hóa. Nút sao chép đưa mã vào clipboard của phiên Windows hiện tại — clipboard do người dùng kiểm soát và nằm ngoài phạm vi bảo vệ của EduFocus, nên hướng dẫn lưu mã ở nơi an toàn.
- Không có PIN, recovery code hay nội dung clipboard nào được ghi vào DB, audit hay file log; ô mật khẩu được xóa sau mỗi lần gửi và khi đóng wizard.
- Retention mặc định: sample 30 ngày, event/blocked attempt 90 ngày, session summary/audit 365 ngày.

### Giới hạn đã biết (không phải lỗ hổng)

Các điểm sau **không được coi là lỗ hổng bảo mật** vì đã được ghi rõ trong thiết kế:

- Người dùng có quyền **Administrator/SYSTEM** có thể tắt, gỡ hoặc bypass EduFocus.
- File `hosts` có thể bị bypass bằng DoH/DoT, VPN, proxy hoặc sửa registry.
- Hosts không tạo log truy cập HTTPS; `BlockedAttempt` là best-effort, không phải tổng tuyệt đối.
- Trang chặn chỉ hiển thị với HTTP; HTTPS thường báo lỗi kết nối/SSL.
- Windows shutdown/logoff vẫn là đường bypass; watchdog scheduled task có độ trễ tới khoảng 60 giây.
- Watchdog scheduled task không tự phục hồi nếu task bị disable/xóa hoặc máy chưa đăng nhập lại; nếu có thêm watchdog process, kill cả app lẫn watchdog trong cùng phiên cũng không được phục hồi cho tới lần đăng nhập sau.
- Watchdog giới hạn restart (mặc định 5 lần/10 phút) để tránh vòng lặp vô hạn; sau ngưỡng đó app không tự phục hồi cho tới lần đăng nhập sau hoặc khi phụ huynh mở lại.
- Phát hiện treo dựa trên heartbeat; nếu file heartbeat/state bị khóa, hỏng hoặc ổ đĩa đầy, watchdog có thể phát hiện sai hoặc trễ.
- Uninstall PIN chỉ là best-effort; admin Windows vẫn gỡ được.
- Chặn app có thể đóng/kill process và làm mất dữ liệu chưa lưu; EduFocus ưu tiên `WM_CLOSE` trước khi kill.
- PIN không bảo vệ file DB nếu phụ huynh và học sinh dùng chung tài khoản Windows; ACL thư mục dữ liệu cũng không tách được quyền trong trường hợp này.
- Webcam attention chỉ là ước lượng, không phải eye tracker y khoa.
- Ứng dụng không chống gian lận thi cử và không nên dùng cho mục đích đó.
- Không phát hiện được mọi chatbot mới hoặc chatbot nhúng trong ứng dụng khác.
- Process watcher dựa trên tên/đường dẫn process; app đổi tên hoặc chạy portable có thể không bị phát hiện.
- URL/path, wildcard subdomain và AI sidebar chưa được enforce trong MVP.

Nếu bạn phát hiện cách vượt qua các giới hạn trên **trong điều kiện người dùng standard user**, hãy báo cáo — đó có thể là lỗ hổng thực sự.

---

## 🐛 Báo cáo lỗ hổng bảo mật

**KHÔNG mở public Issue cho lỗ hổng bảo mật.**

Vui lòng báo cáo qua **GitHub Private Security Advisory**:

1. Vào tab **Security** của repo.
2. Chọn **Report a vulnerability**.
3. Điền thông tin theo mẫu.

### Nội dung cần có

- Mô tả lỗ hổng và tác động tiềm tàng.
- Các bước tái hiện (steps to reproduce).
- Phiên bản/commit bị ảnh hưởng.
- Môi trường (Windows version, quyền tài khoản, cấu hình).
- Proof of concept tối thiểu, nếu có.
- Đề xuất khắc phục, nếu có.

### Thời gian phản hồi

- Xác nhận đã nhận: trong **72 giờ**.
- Đánh giá ban đầu: trong **7 ngày**.
- Vá hoặc kế hoạch vá: tùy mức độ nghiêm trọng, thường trong **30 ngày**.

### Phạm vi ưu tiên

| Mức | Ví dụ |
|---|---|
| Critical | Bypass PIN không cần quyền admin; lộ dữ liệu trẻ; RCE |
| High | Xóa/sửa dữ liệu trái phép; nâng quyền; lộ hash PIN |
| Medium | Bypass blocking với standard user; crash có thể lợi dụng |
| Low | Lỗi hardening, thông tin lộ không nhạy cảm |

### Safe harbor

Chúng tôi không theo đuổi pháp lý với nhà nghiên cứu bảo mật thiện chí, miễn là:

- Không truy cập, sửa hoặc xóa dữ liệu của người khác.
- Không gây gián đoạn dịch vụ.
- Báo cáo riêng tư và cho thời gian khắc phục hợp lý.
- Không công bố chi tiết trước khi có bản vá.

---

## 🔐 Khuyến nghị cho phụ huynh

- Dùng **tài khoản Windows riêng** cho học sinh (standard user).
- Đặt PIN mạnh, không chia sẻ với trẻ.
- Lưu recovery code ở nơi an toàn.
- Bật **Multi-App Camera** trên Windows 11 nếu muốn theo dõi trong Meet/Teams.
- Thông báo công khai với trẻ về việc sử dụng ứng dụng.
- Kiểm tra báo cáo định kỳ; không dùng để kết luận thay cho trao đổi trực tiếp.

---

## ⚖️ Tuân thủ dữ liệu cá nhân

- Ứng dụng xử lý dữ liệu có thể liên quan đến trẻ em và dữ liệu sinh trắc học (ước lượng từ webcam). Đây là **dữ liệu cá nhân nhạy cảm** theo pháp luật Việt Nam.
- Trước khi phát hành thương mại, cần rà soát **Luật Bảo vệ dữ liệu cá nhân 91/2025/QH15**, **Nghị định 356/2025/NĐ-CP** (thay thế Nghị định 13/2023/NĐ-CP từ 01/01/2026) và các văn bản hướng dẫn hiện hành.
- Cần đánh giá tác động xử lý dữ liệu cá nhân (DPIA), thông báo mục đích và lấy đồng ý của người đại diện hợp pháp.
- Tài liệu này không phải tư vấn pháp lý; cần luật sư/DPO xác nhận trước khi phát hành.

---

## 🙏 Cảm ơn

Chúng tôi đánh giá cao mọi đóng góp giúp EduFocus an toàn hơn cho trẻ em và gia đình.
