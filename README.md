<div align="center">

<img src="./assets/logo.png" alt="EduFocus Logo" width="180" />

# 🎓 EduFocus

**Giám sát học tập có trách nhiệm — bảo vệ sự tập trung của con trên máy tính.**

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](./LICENSE)
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
| 📋 **Blocklist linh hoạt** | Thêm domain/app tùy chỉnh, allowlist tạm thời cho bài tập được phép. URL/path cần browser extension (post-MVP) |
| 🕒 **Lịch bảo vệ** | Tự bật/tắt bảo vệ theo khung giờ học trong tuần |
| 📊 **Theo dõi phiên học** | Ghi nhận thời gian học, ứng dụng/domain đang dùng (best-effort), số lần bị chặn quan sát được |
| 👁️ **Ước lượng tập trung** | Dùng webcam để nhận biết có mặt, nhìn màn hình, rời màn hình hoặc buồn ngủ |
| 🖥️ **Nhận diện lớp online** | Theo dõi riêng Google Meet, Microsoft Teams, Zoom |
| 🔒 **Local-first** | Không cloud, không tài khoản, dữ liệu chỉ nằm trên máy |
| 🛡️ **Chống tắt cơ bản** | Ẩn xuống tray và thoát cần PIN; watchdog tự khởi động lại vẫn là đề xuất Phase 7, chưa được triển khai |
| 📈 **Báo cáo phụ huynh** | Dashboard ngày/tuần, timeline, mất tập trung, blocked attempts, xuất CSV |

---

## 🔒 Quyền riêng tư là ưu tiên

EduFocus được thiết kế để **không trở thành phần mềm do thám**:

- **Không lưu ảnh, video hoặc âm thanh.** Frame webcam chỉ được xử lý trong bộ nhớ để suy ra trạng thái.
- **Không lưu nội dung trang web, tin nhắn hay clipboard.**
- **Không gửi dữ liệu học tập lên cloud.** Mọi dữ liệu nằm trong SQLite trên máy; nếu bật kiểm tra cập nhật, chỉ gửi version/OS và server vẫn có thể thấy IP.
- **Không nhận diện danh tính**, không face embedding, không phân tích cảm xúc.
- Báo cáo luôn hiển thị **độ phủ camera** và **độ tin cậy** để phụ huynh không hiểu sai kết quả.
- Ứng dụng phải được thông báo công khai cho học sinh; không khuyến khích giám sát bí mật.

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
- 🌐 Chrome, Edge, Firefox cho chặn domain qua hosts; block page chỉ với HTTP
- 🎥 Webcam tích hợp hoặc webcam ngoài (tùy chọn, cho tính năng attention)

---

## 🏗️ Tech Stack

| Layer | Công nghệ |
|---|---|
| **UI** | WPF .NET 10 LTS (mục tiêu; code hiện tại vẫn `net9.0` và cần nâng cấp), MaterialDesignInXAML, CommunityToolkit.Mvvm |
| **Architecture** | Clean Architecture + Ports & Adapters |
| **Database** | SQLite + EF Core 10 (mục tiêu; code hiện tại EF Core 9) |
| **Blocking** | Windows hosts file (domain exact) + local HTTP block page |
| **Activity** | Win32 foreground window, WMI, UI Automation |
| **Attention** | OpenCvSharp4 + ONNX Runtime (MediaPipe Face/Iris) |
| **Security** | PBKDF2-SHA256; không lưu secret khác |
| **Installer** | WiX Toolset v4 (MSI) |
| **CI/CD** | GitHub Actions |

---

## 🚀 Cài đặt & chạy

### Bản phát hành

> EduFocus đang trong quá trình chuyển đổi từ dự án tiền nhiệm **Don't Be Lazy**. Bản phát hành MVP dự kiến sau Phase 7; các tính năng trong README chưa phải bản đã phát hành.

Khi có bản phát hành, tải tại [Releases](https://github.com/Cong-ty-TNNH-Q-Tech/EduFocus/releases).

### Chạy từ source

**Yêu cầu hiện tại:** .NET 10 SDK (tests target `net10.0`; WPF sẽ được nâng ở Phase 1), Windows 11 23H2+ 64-bit (Windows 10 22H2 best-effort).

```bash
git clone https://github.com/Cong-ty-TNNH-Q-Tech/EduFocus.git
cd EduFocus

dotnet restore EduFocus.slnx
dotnet run --project src/EduFocus.WPF/EduFocus.WPF.csproj
```


Chạy test:

```bash
dotnet test src/EduFocus.Domain.Tests/EduFocus.Domain.Tests.csproj
```

> WPF và EF Core chưa được nâng cấp trong issue đổi tên; theo dõi .NET 10/EF Core 10 trong Phase 1.

---

## 📚 Tài liệu

- [📄 BA Document](./docs/ba_document.md) — Phân tích nghiệp vụ, tính năng, quy tắc, NFR
- [📋 Use Cases](./docs/use_cases.md) — Luồng nghiệp vụ chi tiết UC01–UC14
- [🏛️ System Architecture](./docs/system_architecture.md) — Kiến trúc, thành phần, quyết định thiết kế
- [🗄️ Database Schema](./docs/database_schema.md) — Schema SQLite, index, retention
- [📝 Changelog](./CHANGELOG.md) — Lịch sử thay đổi
- [🤝 Contributing](./CONTRIBUTING.md) — Hướng dẫn đóng góp
- [🔒 Security](./SECURITY.md) — Chính sách bảo mật và báo cáo lỗ hổng

---

## 🗺️ Roadmap

- [x] Xác định lại sản phẩm và phạm vi
- [x] Viết lại BA, Use Cases, Architecture, Database Schema
- [ ] Phase 0: Spike camera + blocking + tamper resistance
- [x] Đổi tên solution, project, namespace và assembly sang EduFocus.* (Issue #5)
- [ ] Phase 1: Schema mới, CI/installer và nền tảng phát hành
- [ ] Phase 2: PIN, Parent Mode, Protection toggle
- [ ] Phase 3: Chatbot blocking engine + block page
- [ ] Phase 4: Study session + activity tracking
- [ ] Phase 5: Attention monitoring
- [ ] Phase 6: Dashboard, reports, settings
- [ ] Phase 7: Hardening, installer, CI, release MVP

---

## ⚠️ Giới hạn đã biết

- PIN không phải ranh giới bảo mật nếu học sinh dùng chung tài khoản Windows hoặc có quyền Administrator.
- Hosts file có thể bị bypass bởi DoH/DoT, VPN hoặc proxy.
- Hosts không tạo log truy cập HTTPS; số blocked attempts chỉ là best-effort.
- Trang chặn chỉ hiển thị với HTTP; HTTPS thường báo lỗi kết nối/SSL.
- Chặn app có thể đóng/kill process và làm mất dữ liệu chưa lưu; EduFocus ưu tiên `WM_CLOSE` trước khi kill.
- URL/path, wildcard subdomain và chặn AI sidebar chưa thuộc MVP; cần browser extension.
- Báo cáo chi tiết chỉ có trong 30 ngày; dữ liệu dài hạn dùng session summary (365 ngày).
- Webcam-based attention chỉ là ước lượng; không dùng để kết luận chắc chắn.
- Google Meet/Teams có thể chiếm webcam trên máy chưa hỗ trợ Multi-App Camera.
- Không thể phát hiện mọi chatbot mới hoặc chatbot chạy trong ứng dụng khác.
- Process watcher dựa trên tên/đường dẫn process; app đổi tên hoặc chạy portable có thể không bị phát hiện.
- Ứng dụng không chống được người dùng có quyền SYSTEM/admin; Windows shutdown/logoff vẫn là đường bypass.
- Watchdog scheduled task mới là đề xuất, chưa được triển khai; thiết kế chỉ restart sau lần thoát bất thường khi protection đang bật, với độ trễ khoảng một phút trở lên. Administrator có thể tắt/gỡ task; installer phải dọn task khi uninstall.

EduFocus hướng tới **hỗ trợ thói quen học tập**, không phải giám sát bí mật hay chống gian lận thi cử.

---

## 🤝 Đóng góp

Mọi đóng góp đều được chào đón. Vui lòng đọc [CONTRIBUTING.md](./CONTRIBUTING.md) trước khi mở Pull Request.

---

## 📜 License

Dự án được phân phối dưới giấy phép [MIT](./LICENSE).

© 2026 ThanhTrunggDEV. Dự án kế thừa Don't Be Lazy theo giấy phép MIT.
