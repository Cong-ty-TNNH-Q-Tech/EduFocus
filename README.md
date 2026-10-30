<div align="center">

# 🎓 EduFocus

**Giám sát học tập có trách nhiệm — bảo vệ sự tập trung của con trên máy tính.**

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](./LICENSE)
[![CI](https://github.com/Cong-ty-TNNH-Q-Tech/EduFocus/actions/workflows/ci.yml/badge.svg)](https://github.com/Cong-ty-TNNH-Q-Tech/EduFocus/actions/workflows/ci.yml)
[![Status](https://img.shields.io/badge/status-in%20development-blue)]()
[![Platform](https://img.shields.io/badge/platform-Windows%2011-0078D6)]()
[![PRs Welcome](https://img.shields.io/badge/PRs-welcome-brightgreen.svg)](./CONTRIBUTING.md)

🌏 **Tiếng Việt** | **[English](./README.en.md)**

</div>

---

## 🎯 Vấn đề

Học sinh mở máy tính để học online nhưng rất dễ bị cuốn vào chatbot AI, mạng xã hội, game hoặc video giải trí. Phụ huynh thiếu một công cụ đơn giản để:

- Giới hạn môi trường học tập mà không phải cấu hình router/firewall.
- Biết con đã học bao lâu và có thực sự tập trung không.
- Bật/tắt bảo vệ nhanh bằng mật khẩu, không bị học sinh tự ý gỡ.

**EduFocus** ra đời để giải quyết đúng bài toán đó.

---

## ✨ Tính năng chính

> Đây là phạm vi **MVP dự kiến**, chưa phát hành. Một số giới hạn được ghi rõ bên dưới.

| Tính năng | Mô tả |
|---|---|
| 🔐 **PIN phụ huynh** | Chỉ phụ huynh mới bật/tắt bảo vệ, sửa blocklist, xem báo cáo hoặc thoát app |
| 🚫 **Chặn chatbot** | Chặn domain chatbot mục tiêu (ChatGPT, Claude, Gemini, Copilot, Perplexity, Character.AI, DeepSeek…) và app được cấu hình |
| 📋 **Blocklist linh hoạt** | Thêm domain/app tùy chỉnh, allowlist tạm thời cho bài tập được phép. Rule URL/path chính xác qua browser extension Chrome/Edge |
| 🕒 **Lịch bảo vệ** | Tự bật/tắt bảo vệ theo khung giờ học trong tuần |
| 📊 **Theo dõi phiên học** | Ghi nhận thời gian học, ứng dụng/domain đang dùng (best-effort), số lần bị chặn quan sát được |
| 👁️ **Ước lượng tập trung** | Dùng webcam để nhận biết có mặt, nhìn màn hình, rời màn hình hoặc buồn ngủ |
| 🖥️ **Nhận diện lớp online** | Theo dõi riêng Google Meet, Microsoft Teams, Zoom |
| 🔒 **Local-first** | Không cloud, không tài khoản, dữ liệu chỉ nằm trên máy |
| 🛡️ **Chống tắt cơ bản** | Ẩn xuống tray, thoát cần PIN; Windows Service nền giữ bảo vệ khi UI đóng, tự khởi động lại sau crash/reboot (SCM restart khoảng 60 giây) |
| 📈 **Báo cáo phụ huynh** | Dashboard ngày/tuần, timeline, mất tập trung, blocked attempts, xuất CSV |

---

## 🔒 Quyền riêng tư là ưu tiên

EduFocus được thiết kế để **không trở thành phần mềm do thám**:

- **Không lưu ảnh, video hoặc âm thanh.** Frame webcam chỉ được xử lý trong bộ nhớ để suy ra trạng thái.
- **Không lưu nội dung trang web, tin nhắn hay clipboard.**
- **Không gửi dữ liệu học tập lên cloud.** Mọi dữ liệu nằm trong SQLite **mã hóa SQLCipher** trên máy; kiểm tra cập nhật mặc định tắt, khi phụ huynh bật chỉ gửi số phiên bản tới GitHub và GitHub vẫn có thể thấy IP.
- **Dữ liệu được mã hóa khi lưu:** DB, WAL và backup dùng SQLCipher; khóa 32 byte lưu bằng DPAPI
  `LocalMachine` trong `%ProgramData%\EduFocus\edufocus.key`, gắn với máy và không nằm trong log.
  Key không phải PIN; nếu tự sao lưu DB, cần sao lưu cả `edufocus.key` và chỉ khôi phục trên máy đó.
- **Không nhận diện danh tính**, không face embedding, không phân tích cảm xúc.
- Báo cáo luôn hiển thị **độ phủ camera** và **độ tin cậy** để phụ huynh không hiểu sai kết quả.
- Ứng dụng phải được thông báo công khai cho học sinh; không khuyến khích giám sát bí mật.

**Hồ sơ minh bạch:**

- [Thông báo quyền riêng tư](./docs/privacy-notice.md) — dữ liệu, mục đích, nơi lưu, thời hạn,
  quyền của phụ huynh/học sinh và kênh liên hệ. Thông báo được hiển thị trong installer và mở lại
  được từ onboarding/Parent Mode trước khi bật giám sát.
- [DPIA](./docs/dpia.md) — data map, đánh giá rủi ro, quy trình xử lý yêu cầu dữ liệu (xác nhận
  trong 72 giờ làm việc, hoàn tất trong 30 ngày) và ứng phó sự cố.
- [Third-party notices](./THIRD-PARTY-NOTICES.md) — danh mục giấy phép của mọi thư viện được
  phân phối trong MSI/ZIP.

**Xóa dữ liệu:** Parent Mode → "Dữ liệu & quyền riêng tư" → xóa toàn bộ dữ liệu quan sát bằng
PIN; khi gỡ cài đặt có thể chọn xóa `%ProgramData%\EduFocus` (DB, backup và `edufocus.key`). Chi tiết
và thời hạn phản hồi ở
[thông báo quyền riêng tư §10](./docs/privacy-notice.md#10-yêu-cầu-xóa-dữ-liệu-và-thời-hạn-phản-hồi).

> **Giới hạn:** Webcam thường không phải eye tracker hồng ngoại. "Tập trung" là ước lượng, không phải bằng chứng tuyệt đối. Khi Google Meet/Teams đang dùng camera, app có thể không quan sát được nếu máy chưa hỗ trợ Multi-App Camera.
>
> **Blocking:** Hosts chỉ chặn domain và không tạo log HTTPS. Trang chặn chỉ hiển thị với HTTP; HTTPS thường báo lỗi kết nối/SSL. Vì vậy số lần bị chặn là **best-effort**, không phải tổng tuyệt đối.

---

## 🧭 Cách hoạt động

```text
Phụ huynh tạo PIN
        ↓
Bật Protection ON (thủ công hoặc theo lịch)
        ↓
Chặn domain chatbot + app ngoài phạm vi cho phép
        ↓
Ghi nhận phiên học, app/domain (best-effort) và trạng thái tập trung
        ↓
Phụ huynh xem báo cáo; học sinh thấy trang chặn với HTTP, HTTPS báo lỗi
        ↓
Tắt Protection bằng PIN hoặc hết khung giờ
```

### Hai chế độ

| Mode | Dành cho | Quyền |
|---|---|---|
| **Parent Mode** | Phụ huynh | Toàn quyền cấu hình, báo cáo, bật/tắt bảo vệ |
| **Student Mode** | Học sinh | Xem trạng thái, thời gian học, trang chặn; không sửa cấu hình |

---

## 🖥️ Nền tảng

- ✅ **Windows 11 23H2+** (64-bit) — nền tảng chính
- ⚠️ Windows 10 22H2: chạy được nhưng Windows 10 đã hết hỗ trợ chính thức
- 🔜 macOS — chưa hỗ trợ
- 🌐 Chrome/Edge/Firefox chặn domain qua hosts; extension Chrome/Edge 116+ chặn thêm URL/path; block page chỉ với HTTP
- 🎥 Webcam tích hợp hoặc webcam ngoài (tùy chọn, cho tính năng attention)

---

## 🏗️ Tech Stack

| Layer | Công nghệ |
|---|---|
| **UI** | WPF .NET 10 LTS, CommunityToolkit.Mvvm (lớp UI mới ở Phase 6) |
| **Architecture** | Clean Architecture + Ports & Adapters |
| **Database** | SQLite (SQLCipher) + EF Core 10 |
| **Blocking** | Windows hosts file (domain exact) + local HTTP block page + MV3 extension (URL/path) |
| **Activity** | Win32 foreground window, WMI, UI Automation |
| **Attention** | OpenCvSharp4 + ONNX Runtime (MediaPipe Face/Iris) |
| **Background protection** | Windows Service `EduFocusProtection` (LocalSystem) + named-pipe IPC có ACL/xác thực ảnh tiến trình |
| **Security** | PBKDF2-SHA256; không lưu secret khác |
| **Installer** | WiX Toolset v4 (MSI, cài service + native host) |
| **CI/CD** | GitHub Actions |

---

## 🚀 Cài đặt & chạy

### Bản phát hành

> EduFocus đang trong quá trình chuyển đổi từ dự án tiền nhiệm **Don't Be Lazy**. Bản phát hành MVP dự kiến sau Phase 7; các tính năng trong README chưa phải bản đã phát hành.

Khi có bản phát hành, tải tại [Releases](https://github.com/Cong-ty-TNNH-Q-Tech/EduFocus/releases). Mỗi release đính kèm MSI, bản portable ZIP, browser extension ZIP, hướng dẫn cài và `SHA256SUMS.txt`; hãy kiểm tra SHA-256 (và chữ ký số nếu có) trước khi cài. Yêu cầu: Windows 10 22H2/Windows 11 x64; bản MVP cần quyền Administrator. Chi tiết ở [docs/installer.md](./docs/installer.md) và [docs/release-process.md](./docs/release-process.md).

### Chạy từ source

**Yêu cầu hiện tại:** .NET 10 SDK, Windows 11 23H2+ 64-bit (Windows 10 22H2 best-effort).

```bash
git clone https://github.com/Cong-ty-TNNH-Q-Tech/EduFocus.git
cd EduFocus

dotnet restore EduFocus.slnx
dotnet run --project src/EduFocus.WPF/EduFocus.WPF.csproj
```

Chạy test:

```bash
dotnet test EduFocus.slnx --configuration Release
```

> Toàn bộ solution target .NET 10 LTS và EF Core 10; Windows 11 23H2+ là nền tảng chính, Windows 10 22H2 chỉ best-effort.
>
> Codebase 1.x đã được dọn (issue #7). App hiện khởi động vào shell nền tảng tối thiểu;
> tính năng MVP sẽ được bổ sung theo từng phase. Xem [legacy-cleanup.md](./docs/legacy-cleanup.md).

---

## 📚 Tài liệu

- [📄 BA Document](./docs/ba_document.md) — Phân tích nghiệp vụ, tính năng, quy tắc, NFR
- [📋 Use Cases](./docs/use_cases.md) — Luồng nghiệp vụ chi tiết UC01–UC14
- [🏛️ System Architecture](./docs/system_architecture.md) — Kiến trúc, thành phần, quyết định thiết kế
- [🗄️ Database Schema](./docs/database_schema.md) — Schema SQLite, index, retention
- [🧩 Domain Invariants](./docs/domain-invariants.md) — Entity invariants, ranh giới domain/DB/use case
- [📝 Changelog](./CHANGELOG.md) — Lịch sử thay đổi
- [🤝 Contributing](./CONTRIBUTING.md) — Hướng dẫn đóng góp
- [🔒 Security](./SECURITY.md) — Chính sách bảo mật và báo cáo lỗ hổng
- [🌐 Browser Extension](./docs/browser-extension.md) — Chặn URL/path, quyền, đóng gói và gỡ cài đặt
- [🔏 Privacy Notice](./docs/privacy-notice.md) — Thông báo quyền riêng tư (bản đang áp dụng)
- [📊 DPIA](./docs/dpia.md) — Đánh giá tác động xử lý dữ liệu cá nhân
- [⚖️ Third-Party Notices](./THIRD-PARTY-NOTICES.md) — Giấy phép thư viện bên thứ ba

---

## 🗺️ Roadmap

- [x] Xác định lại sản phẩm và phạm vi
- [x] Viết lại BA, Use Cases, Architecture, Database Schema
- [ ] Phase 0: Spike camera + blocking + tamper resistance
- [ ] Phase 1: Dọn code cũ, đổi tên EduFocus, schema mới
- [ ] Phase 2: PIN, Parent Mode, Protection toggle
- [ ] Phase 3: Chatbot blocking engine + block page
- [ ] Phase 4: Study session + activity tracking
- [ ] Phase 5: Attention monitoring
- [ ] Phase 6: Dashboard, reports, settings
- [ ] Phase 7: Hardening, installer, CI, release MVP

---

## 🔍 Nhật ký & gỡ lỗi

- Log kỹ thuật của UI: `%LOCALAPPDATA%\EduFocus\logs\edufocus-YYYYMMDD.log`; của service:
  `%PROGRAMDATA%\EduFocus\logs\edufocus-YYYYMMDD.log` — mỗi file tối đa 10 MB, giữ 14 ngày.
- Mỗi dòng có timestamp UTC, level, category và correlation id; PIN, recovery code, token và URL đầy đủ được redact trước khi ghi.
- Trạng thái service: `EduFocus.Service.exe --health` (cần quyền admin); chạy `--console` để xem log
  trực tiếp khi gỡ lỗi.
- Khi báo lỗi, gửi các file log quanh thời điểm xảy ra kèm phiên bản EduFocus, phiên bản Windows và bước tái hiện. Không gửi DB hoặc ảnh/video webcam.

---

## ⚠️ Giới hạn đã biết

- PIN không phải ranh giới bảo mật nếu học sinh dùng chung tài khoản Windows hoặc có quyền Administrator. ACL thư mục dữ liệu chỉ tách được quyền khi phụ huynh và học sinh dùng tài khoản Windows riêng; PIN chỉ bảo vệ UI, không bảo vệ file DB.
- Hosts file có thể bị bypass bởi DoH/DoT, VPN hoặc proxy.
- Hosts không tạo log truy cập HTTPS; số blocked attempts chỉ là best-effort.
- Trang chặn chỉ hiển thị với HTTP; HTTPS thường báo lỗi kết nối/SSL.
- Chặn app có thể đóng/kill process và làm mất dữ liệu chưa lưu; EduFocus ưu tiên `WM_CLOSE` trước khi kill.
- Extension Chrome/Edge chặn URL/path cấp tab; chưa hỗ trợ Firefox, iframe/subresource hay wildcard
  subdomain. Extension có thể bị tắt bởi người dùng có quyền quản trị máy.
- Báo cáo chi tiết chỉ có trong 30 ngày; dữ liệu dài hạn dùng session summary (365 ngày).
- Webcam-based attention chỉ là ước lượng; không dùng để kết luận chắc chắn.
- Google Meet/Teams có thể chiếm webcam trên máy chưa hỗ trợ Multi-App Camera.
- Không thể phát hiện mọi chatbot mới hoặc chatbot chạy trong ứng dụng khác.
- Process watcher dựa trên tên/đường dẫn process; app đổi tên hoặc chạy portable có thể không bị phát hiện.
- Ứng dụng không chống được người dùng có quyền SYSTEM/admin; Windows shutdown/logoff vẫn là đường bypass.
- Bản cài dùng Windows Service: service chạy LocalSystem, tiếp tục bảo vệ khi UI đóng và được SCM
  restart sau crash (khoảng 60 giây; tối đa 3 lần/ngày). Admin/SYSTEM vẫn stop/disable/gỡ được service.
  UI vẫn cần quyền admin trong bản này vì đọc/ghi DB được ACL bảo vệ. Bản portable/source dùng watchdog
  thường trú: restart app trong ≤ 30 giây, watchdog bị kill được scheduled task khôi phục trong tối đa
  khoảng 60 giây. Uninstall PIN chỉ là best-effort.

EduFocus hướng tới **hỗ trợ thói quen học tập**, không phải giám sát bí mật hay chống gian lận thi cử.

---

## 🤝 Đóng góp

Mọi đóng góp đều được chào đón. Vui lòng đọc [CONTRIBUTING.md](./CONTRIBUTING.md) trước khi mở Pull Request.

---

## 📜 License

Dự án được phân phối dưới giấy phép [MIT](./LICENSE). Danh mục giấy phép của các thư viện được
phân phối kèm nằm trong [THIRD-PARTY-NOTICES.md](./THIRD-PARTY-NOTICES.md).

© 2026 ThanhTrunggDEV. Dự án kế thừa Don't Be Lazy theo giấy phép MIT.
